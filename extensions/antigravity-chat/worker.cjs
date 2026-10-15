'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { spawn, spawnSync } = require('node:child_process');

function save(file, value) {
  fs.writeFileSync(file + '.tmp', JSON.stringify(value));
  fs.renameSync(file + '.tmp', file);
}
function read(file, fallback = {}) { return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : fallback; }

function findLastErrorInTranscript(conversationId) {
  if (!conversationId) return '';
  try {
    const userProfile = process.env.USERPROFILE || process.env.HOME || '';
    const transcriptPath = path.join(userProfile, '.gemini', 'antigravity-cli', 'brain', conversationId, '.system_generated', 'logs', 'transcript.jsonl');
    if (!fs.existsSync(transcriptPath)) return '';
    const content = fs.readFileSync(transcriptPath, 'utf8');
    const lines = content.trim().split(/\r?\n/);
    for (let i = lines.length - 1; i >= 0; i--) {
      const line = lines[i].trim();
      if (!line) continue;
      try {
        const item = JSON.parse(line);
        if (item.error) {
          const firstLine = String(item.error).split(/\r?\n/)[0].trim();
          return firstLine.slice(0, 150);
        }
      } catch {}
    }
  } catch {}
  return '';
}

async function invokeCapability(name, payload, options = {}, maxRetries = 4) {
  const apiUrl = options.apiUrl || process.env.AGY_BRIDGE_API;
  const apiToken = options.apiToken || process.env.AGY_BRIDGE_TOKEN;
  if (!apiUrl || !apiToken) return null;

  for (let attempt = 1; attempt <= maxRetries; attempt++) {
    try {
      const currentPayload = { ...payload };
      const doFetch = (p) => fetch(apiUrl + '/v1/capabilities/invoke', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + apiToken },
        body: JSON.stringify({ name, payload: p }),
        signal: AbortSignal.timeout(name === 'chat.stream' ? 6000 : 20000)
      });

      let res = await doFetch(currentPayload);
      if (name === 'chat.send' && res.status === 400 && currentPayload.conversationId) {
        delete currentPayload.conversationId;
        res = await doFetch(currentPayload);
      }
      const data = await res.json();
      if (res.ok && data.success) return data.data || {};
      throw new Error(data.error || `${name} 调用失败`);
    } catch (err) {
      if (attempt === maxRetries) throw err;
      await new Promise(r => setTimeout(r, Math.min(3000, 800 * attempt)));
    }
  }
}

async function sendMessageWithRetry(payload, options = {}, maxRetries = 4) {
  const data = await invokeCapability('chat.send', payload, options, maxRetries);
  if (data == null) return null;
  if (!data.messageId) throw new Error('chat.send 未能确认消息入队');
  return data;
}

function streamPreview(text) {
  const chars = Array.from(String(text || ''));
  if (chars.length <= 40000) return chars.join('');
  return chars.slice(0, 40000).join('') + '\n\n[流式预览已达到上限，完整内容将在生成完成后显示]';
}

function createStreamPublisher(job, config, options = {}) {
  const target = job.replyDeviceId || config.target || 'android-f85961cb-a306-46aa-8125-33f9d8614a7e';
  const conversationId = job.conversationId || '';
  let enabled = Boolean(target && conversationId && !options.disableStreaming &&
    (options.apiUrl || process.env.AGY_BRIDGE_API) && (options.apiToken || process.env.AGY_BRIDGE_TOKEN));
  let latest = '';
  let lastSent = '';
  let dirty = false;
  let timer = null;
  let sending = false;
  let sendPromise = Promise.resolve();
  let seq = 0;
  let started = false;
  let closed = false;
  let failures = 0;
  let intervalMs = options.streamIntervalMs || 220;

  const clearScheduled = () => {
    if (timer) clearTimeout(timer);
    timer = null;
  };

  const schedule = (delay) => {
    if (!enabled || closed || timer || sending) return;
    timer = setTimeout(() => {
      timer = null;
      publish(started ? 'update' : 'start').catch(() => {});
    }, delay);
  };

  const publish = async (state, force = false) => {
    if (!enabled || closed || sending) return sendPromise;
    const snapshot = latest;
    if (!force && snapshot === lastSent) {
      dirty = false;
      return sendPromise;
    }
    dirty = false;
    sending = true;
    const currentSeq = seq++;
    const payload = { target, conversationId, streamId: `agy-${job.id}`, state, seq: currentSeq, text: snapshot };
    sendPromise = invokeCapability('chat.stream', payload, options, 1)
      .then(result => {
        failures = 0;
        lastSent = snapshot;
        started = true;
        if (result?.transport === 'cloud') intervalMs = Math.max(intervalMs, 1400);
        else if (result?.transport === 'lan') intervalMs = Math.min(intervalMs, 220);
        return result;
      })
      .catch(() => {
        failures++;
        if (failures >= 2) enabled = false;
        return null;
      })
      .finally(() => {
        sending = false;
        if (enabled && !closed && dirty) schedule(intervalMs);
      });
    return sendPromise;
  };

  return {
    start() {
      if (!enabled || closed || started || sending) return Promise.resolve();
      return publish('start', true);
    },
    update(text) {
      if (!enabled || closed) return;
      latest = streamPreview(text);
      dirty = true;
      schedule(started ? intervalMs : 80);
    },
    reset() {
      clearScheduled();
      latest = '';
      lastSent = '';
      dirty = false;
      started = false;
    },
    async flush() {
      if (!enabled || closed) return;
      clearScheduled();
      while (enabled && (dirty || sending)) {
        if (sending) await sendPromise;
        else await publish(started ? 'update' : 'start', true);
      }
    },
    async terminal(state, text = '') {
      if (!enabled || closed) return;
      clearScheduled();
      if (sending) await sendPromise;
      if (!enabled) return;
      closed = true;
      const payload = {
        target,
        conversationId,
        streamId: `agy-${job.id}`,
        state,
        seq: seq++,
        text: streamPreview(text || latest || '')
      };
      try { await invokeCapability('chat.stream', payload, options, 1); } catch {}
    }
  };
}

function extractAttachmentsFromText(text) {
  if (!text) return [];
  const regex = /(?:!\[.*?\]\((?:file:\/\/\/)?|\[.*?\]\((?:file:\/\/\/)?|(?:\b))([a-zA-Z]:[\\\/][^\s\)\r\n<>\*\"\|]+?\.(?:png|jpg|jpeg|webp|gif|bmp|pdf|zip|txt|docx|xlsx|pptx))/gi;
  const found = new Map();
  let m;
  while ((m = regex.exec(text)) !== null) {
    try {
      const p = path.normalize(m[1].replace(/\//g, '\\'));
      if (fs.existsSync(p)) {
        const stat = fs.statSync(p);
        if (stat.isFile() && stat.size > 0 && stat.size <= 30 * 1024 * 1024) {
          const base = path.basename(p).toLowerCase();
          if (!found.has(base) || (!p.includes('.gemini') && found.get(base).path.includes('.gemini'))) {
            const ext = path.extname(p).toLowerCase();
            const isPhoto = ['.png', '.jpg', '.jpeg', '.webp', '.gif', '.bmp'].includes(ext);
            found.set(base, { path: p, kind: isPhoto ? 'photo' : 'file', size: stat.size });
          }
        }
      }
    } catch {}
  }
  return Array.from(found.values());
}

class PersistentCliSession {
  constructor(root, cliPath, options = {}) {
    this.root = root;
    this.cliPath = cliPath;
    this.options = options;
    this.proc = null;
    this.pid = 0;
    this.buffer = '';
    this.current = null;
    this.startPromise = null;
    this.disposed = false;
    this.generation = 0;
  }

  buildEnv() {
    const env = { ...process.env };
    for (const key of Object.keys(env)) if (key.startsWith('YANZI_') || key.startsWith('AGY_BRIDGE_')) delete env[key];
    if (!env.HTTP_PROXY && !env.http_proxy) {
      env.HTTP_PROXY = 'http://127.0.0.1:7890';
      env.HTTPS_PROXY = 'http://127.0.0.1:7890';
      env.ALL_PROXY = 'http://127.0.0.1:7890';
    }
    env.GODEBUG = 'http2client=0';
    return env;
  }

  async ensureStarted(fresh = false) {
    if (this.disposed) throw new Error('persistent_cli_session_disposed');
    if (this.proc && this.proc.exitCode === null) return;
    if (this.startPromise) return this.startPromise;
    this.startPromise = new Promise((resolve, reject) => {
      const workspace = path.join(this.root, 'workspace');
      fs.mkdirSync(workspace, { recursive: true });
      const args = [...(this.options.cliPrefix || []), '--input-format', 'stream-json', '--output-format', 'stream-json',
        '--dangerously-skip-permissions', '--print-timeout', '10m'];
      if (this.options.mode) args.push('--mode', this.options.mode);
      if (!fresh) {
        const saved = read(path.join(this.root, 'session.json'));
        if (saved.conversationId) args.push('--conversation', saved.conversationId);
      }
      const stamp = ++this.generation;
      const proc = spawn(this.cliPath, args, { cwd: workspace, env: this.buildEnv(), windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      this.proc = proc;
      this.pid = proc.pid || 0;
      this.buffer = '';
      let settled = false;
      const startupTimer = setTimeout(() => {
        if (!settled) {
          settled = true;
          reject(new Error('反重力常驻 CLI 启动超时。'));
          this.stopProcess();
        }
      }, this.options.startupTimeoutMs || 20000);

      // Real agy 1.3.5 emits init only AFTER the first stdin user message.
      // Waiting for init here deadlocks the first turn. The spawn event is the transport handshake.
      proc.once('spawn', () => {
        if (settled) return;
        settled = true;
        clearTimeout(startupTimer);
        resolve();
      });
      proc.stdout.setEncoding('utf8');
      proc.stderr.setEncoding('utf8');
      proc.stdout.on('data', data => {
        if (stamp !== this.generation) return;
        this.buffer += data.toString('utf8');
        let newline;
        while ((newline = this.buffer.indexOf('\n')) >= 0) {
          const line = this.buffer.slice(0, newline);
          this.buffer = this.buffer.slice(newline + 1);
          const event = this.consumeEvent(line);
          // init carries conversation metadata, not startup readiness.
        }
      });
      proc.stderr.on('data', () => {});
      proc.on('error', error => {
        if (stamp !== this.generation) return;
        if (!settled) {
          settled = true;
          clearTimeout(startupTimer);
          reject(error);
        }
        if (this.current) this.current.reject(error);
      });
      proc.on('close', code => {
        if (stamp !== this.generation) return;
        this.proc = null;
        this.pid = 0;
        if (!settled) {
          settled = true;
          clearTimeout(startupTimer);
          reject(new Error('反重力常驻 CLI 启动失败，退出码 ' + code));
        }
        if (this.current) {
          const current = this.current;
          this.current = null;
          clearTimeout(current.timer);
          current.reject(new Error('反重力常驻 CLI 已退出，退出码 ' + code));
        }
      });
    }).finally(() => { this.startPromise = null; });
    return this.startPromise;
  }

  consumeEvent(line) {
    const trimmed = String(line || '').trim();
    if (!trimmed) return null;
    let item;
    try { item = JSON.parse(trimmed); } catch { return null; }
    if (item.event === 'init') {
      const conversationId = item.conversation_id || item.init?.conversation_id || '';
      if (conversationId) save(path.join(this.root, 'session.json'), { conversationId, updatedAt: new Date().toISOString() });
      return item;
    }
    const current = this.current;
    if (!current) return item;
    if (item.event === 'step_update') {
      const update = item.step_update || {};
      if (update.step_type === 'agent_response' && typeof update.text_delta === 'string' && update.text_delta) {
        current.streamText += update.text_delta;
        if (current.streamText.length > 4 * 1024 * 1024) {
          current.reject(new Error('任务输出超限。'));
          this.current = null;
          clearTimeout(current.timer);
          this.stopProcess();
        } else {
          current.onDelta?.(current.streamText);
        }
      }
      return item;
    }
    if (item.event === 'result') {
      const result = item.result || item;
      this.current = null;
      clearTimeout(current.timer);
      const conversationId = result.conversation_id || '';
      if (conversationId) save(path.join(this.root, 'session.json'), { conversationId, updatedAt: new Date().toISOString() });
      current.resolve({
        code: result.status === 'SUCCESS' ? 0 : 1,
        out: trimmed,
        result,
        streamText: typeof result.response === 'string' && result.response ? result.response : current.streamText,
        conversationId
      });
    }
    return item;
  }

  async runTurn(prompt, onDelta, timeoutMs = 610000) {
    let attempt = await this.runTurnOnce(prompt, onDelta, timeoutMs, false);
    if (attempt.result?.error?.includes('trajectory not found')) {
      await this.restartFresh();
      attempt = await this.runTurnOnce(prompt, onDelta, timeoutMs, true);
    }
    return attempt;
  }

  async runTurnOnce(prompt, onDelta, timeoutMs, fresh) {
    await this.ensureStarted(fresh);
    if (!this.proc || this.proc.exitCode !== null || !this.proc.stdin.writable)
      throw new Error('反重力常驻 CLI 不可用。');
    if (this.current) throw new Error('当前会话已有一条消息正在生成。');
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        if (!this.current) return;
        const current = this.current;
        this.current = null;
        current.reject(new Error('任务超时，常驻 CLI 已重启。'));
        this.stopProcess();
      }, timeoutMs);
      this.current = { resolve, reject, timer, streamText: '', onDelta };
      try {
        this.proc.stdin.write(JSON.stringify({ event: 'user', message: { role: 'user', content: prompt } }) + '\n');
      } catch (error) {
        this.current = null;
        clearTimeout(timer);
        reject(error);
      }
    });
  }

  async restartFresh() {
    this.stopProcess();
    const statePath = path.join(this.root, 'session.json');
    if (fs.existsSync(statePath)) {
      try { fs.renameSync(statePath, path.join(this.root, 'session-stale-' + Date.now() + '.json')); } catch {}
    }
    await new Promise(resolve => setTimeout(resolve, 120));
    await this.ensureStarted(true);
  }

  cancelCurrent() {
    this.stopProcess();
  }

  stopProcess() {
    const proc = this.proc;
    if (!proc || proc.exitCode !== null || !proc.pid) return;
    this.generation++;
    this.proc = null;
    this.pid = 0;
    if (this.current) {
      const current = this.current;
      this.current = null;
      clearTimeout(current.timer);
      current.reject(new Error('反重力常驻 CLI 已停止。'));
    }
    // Dispose the live child before returning, otherwise orphaned stdout/stdio
    // keeps Node alive even after a session is marked finished.
    if (process.platform === 'win32') {
      try {
        spawnSync('taskkill', ['/PID', String(proc.pid), '/T', '/F'],
          { windowsHide: true, stdio: 'ignore', timeout: 2500 });
      } catch {}
    }
    try { proc.kill('SIGKILL'); } catch {}
    try { proc.stdin.destroy(); proc.stdout.destroy(); proc.stderr.destroy(); } catch {}
  }

  dispose() {
    this.disposed = true;
    this.stopProcess();
  }
}

async function runJob(jobPath, options = {}) {
  const root = path.dirname(jobPath);
  const job = read(jobPath);
  const activePath = path.join(root, 'active.json');
  if (!job.id || read(activePath).id !== job.id || job.status !== 'pending') throw new Error('任务已经执行或不拥有执行锁。');
  const config = read(path.join(root, 'config.json'), { target: 'android-f85961cb-a306-46aa-8125-33f9d8614a7e' });
  const streamPublisher = createStreamPublisher(job, config, options);
  const statePath = path.join(root, 'session.json');
  let child;
  let timer;
  let cancelTimer;
  let killCurrentProcess = () => {};
  const cancelPath = path.join(root, job.id + '.cancel');
  const cancellationRequested = () => cancelled || fs.existsSync(cancelPath);
  const started = Date.now();
  let cancelled = false;
  let timedOut = false;
  let response = '';
  try {
    const cliPath = options.cliPath || job.cliPath || path.join(process.env.LOCALAPPDATA, 'agy', 'bin', 'agy.exe');
    const workspace = path.join(root, 'workspace');
    fs.mkdirSync(workspace, { recursive: true });
    const args = [...(options.cliPrefix || []), '-p', job.prompt, '--output-format', 'stream-json', '--dangerously-skip-permissions', '--print-timeout', '10m'];
    if (options.mode) args.push('--mode', options.mode);
    const session = read(statePath);
    if (session.conversationId) args.push('--conversation', session.conversationId);
    const env = { ...process.env };
    for (const key of Object.keys(env)) if (key.startsWith('YANZI_') || key.startsWith('AGY_BRIDGE_')) delete env[key];
    if (!env.HTTP_PROXY && !env.http_proxy) {
      env.HTTP_PROXY = 'http://127.0.0.1:7890';
      env.HTTPS_PROXY = 'http://127.0.0.1:7890';
      env.ALL_PROXY = 'http://127.0.0.1:7890';
    }
    env.GODEBUG = 'http2client=0';
    const runCliOnce = async (cliArgs) => {
      if (options.persistentSession) {
        const persistent = options.persistentSession;
        await persistent.ensureStarted(false);
        job.status = 'running'; job.pid = persistent.pid; save(jobPath, job);
        save(activePath, { id: job.id, pid: process.pid, createdAt: new Date().toISOString() });
        killCurrentProcess = () => persistent.cancelCurrent();
        try {
          return await persistent.runTurn(job.prompt, text => streamPublisher.update(text), options.timeoutMs || 610000);
        } finally {
          killCurrentProcess = () => {};
        }
      }
      let proc = spawn(cliPath, cliArgs, { cwd: workspace, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
      job.status = 'running'; job.pid = proc.pid; save(jobPath, job);
      save(activePath, { id: job.id, pid: process.pid, createdAt: new Date().toISOString() });
      proc.stdout.setEncoding('utf8'); proc.stderr.setEncoding('utf8');
      let out = '';
      let lineBuffer = '';
      let streamText = '';
      let result = null;
      let conversationId = '';
      const consumeLine = (line) => {
        const trimmed = String(line || '').trim();
        if (!trimmed) return;
        let item;
        try { item = JSON.parse(trimmed); } catch { return; }
        if (item.event === 'init') {
          conversationId = item.conversation_id || item.init?.conversation_id || conversationId;
          return;
        }
        if (item.event === 'step_update') {
          const update = item.step_update || {};
          if (update.step_type === 'agent_response' && typeof update.text_delta === 'string' && update.text_delta) {
            streamText += update.text_delta;
            streamPublisher.update(streamText);
          }
          return;
        }
        if (item.event === 'result') {
          result = item.result || item;
          conversationId = result.conversation_id || item.conversation_id || conversationId;
          if (typeof result.response === 'string' && result.response) streamText = result.response;
          return;
        }
        // Backward-compatible parser for old fixture/CLI JSON.
        if (item.status || item.response || item.error) {
          result = item;
          conversationId = item.conversation_id || conversationId;
          if (typeof item.response === 'string' && item.response) {
            streamText = item.response;
            streamPublisher.update(streamText);
          }
        }
      };
      const procKill = () => {
        if (proc.exitCode !== null || !proc.pid) return;
        if (process.platform === 'win32') spawn('taskkill', ['/PID', String(proc.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }).on('error', () => proc.kill());
        else proc.kill('SIGKILL');
      };
      killCurrentProcess = procKill;
      const subTimer = setTimeout(() => { timedOut = true; procKill(); }, options.timeoutMs || 610000);
      proc.stdout.on('data', data => {
        const chunk = data.toString('utf8');
        out += chunk;
        lineBuffer += chunk;
        let newline;
        while ((newline = lineBuffer.indexOf('\n')) >= 0) {
          const line = lineBuffer.slice(0, newline);
          lineBuffer = lineBuffer.slice(newline + 1);
          consumeLine(line);
        }
        if (out.length > 4 * 1024 * 1024) { timedOut = true; procKill(); }
      });
      const code = await new Promise((resolve, reject) => { proc.on('error', reject); proc.on('close', resolve); });
      if (lineBuffer.trim()) consumeLine(lineBuffer);
      clearTimeout(subTimer);
      killCurrentProcess = () => {};
      return { code, out, result, streamText, conversationId };
    };

    cancelTimer = setInterval(() => {
      if (fs.existsSync(cancelPath)) {
        cancelled = true;
        killCurrentProcess();
      }
    }, 250);

    // A blank thinking bubble can appear before the first model token.
    void streamPublisher.start();
    let cliRun = await runCliOnce(args);
    let exitCode = cliRun.code;
    let stdout = cliRun.out;
    if (cancelled) throw new Error('任务已由用户取消。');
    if (timedOut) throw new Error('任务超时或输出超限，未自动重新执行。');

    let result = cliRun.result;
    if (!result) {
      try { result = JSON.parse(stdout.trim()); } catch { result = { status: 'ERROR', error: stdout.trim() }; }
    }

    // 自愈重试：若指定会话在 CLI 无法恢复（例如 trajectory not found），自动作为新会话发起
    if (!options.persistentSession && (exitCode !== 0 || result.status !== 'SUCCESS') && result.error && result.error.includes('trajectory not found')) {
      const fallbackArgs = args.filter((a, idx) => a !== '--conversation' && args[idx - 1] !== '--conversation');
      streamPublisher.reset();
      cliRun = await runCliOnce(fallbackArgs);
      exitCode = cliRun.code;
      stdout = cliRun.out;
      result = cliRun.result;
      if (!result) {
        try { result = JSON.parse(stdout.trim()); } catch { result = { status: 'ERROR', error: stdout.trim() }; }
      }
    }
    if (exitCode !== 0 || result.status !== 'SUCCESS') throw new Error(result.error || 'CLI 执行失败，请在电脑查看账号状态。');
    if (cancellationRequested()) {
      cancelled = true;
      throw new Error('任务已由用户取消。');
    }
    response = String(result.response || cliRun.streamText || '').trim();
    if (!response) {
      const convId = result.conversation_id || session.conversationId;
      const lastError = findLastErrorInTranscript(convId);
      if (lastError) throw new Error(`CLI 未生成文本回复（${lastError}）。`);
      throw new Error('CLI 返回了空回复。');
    }
    const resolvedConversationId = result.conversation_id || cliRun.conversationId;
    if (resolvedConversationId) save(statePath, { conversationId: resolvedConversationId, updatedAt: new Date().toISOString() });
    await streamPublisher.flush();
    job.status = 'completed'; job.response = response;
  } catch (e) {
    job.status = cancelled ? 'cancelled' : 'failed';
    const elapsedSec = Math.max(1, Math.round((Date.now() - started) / 1000));
    response = `【反重力 · 执行异常】\n${e.message}\n（任务编号: ${job.id.slice(0, 8)}，耗时: ${elapsedSec} 秒）\n可发送 /结果 查看历史回复，或发送 /恢复 清除锁。`;
    job.error = e.message;
    await streamPublisher.terminal(cancelled ? 'cancelled' : 'error', cancelled ? '' : response);
  } finally {
    clearTimeout(timer);
    job.finishedAt = new Date().toISOString(); job.durationMs = Date.now() - started;
    fs.writeFileSync(path.join(root, 'last-result.txt'), response);
    save(jobPath, job);
  }
  if (job.status === 'cancelled' || cancellationRequested()) {
    cancelled = true;
    job.status = 'cancelled';
    job.error = job.error || '任务已由用户取消。';
    job.deliveryStatus = 'cancelled';
    clearInterval(cancelTimer);
    save(jobPath, job);
    if (read(activePath).id === job.id && fs.existsSync(activePath)) fs.unlinkSync(activePath);
    if (fs.existsSync(cancelPath)) fs.unlinkSync(cancelPath);
    return job;
  }

  try {
    let formattedResponse = response;
    try {
      const attachments = extractAttachmentsFromText(response);
      for (const att of attachments) {
        if (att.kind === 'photo') {
          const fileName = path.basename(att.path);
          const escapedPath = att.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
          const bareRegex = new RegExp(`(?<!!\\[[^\\]]*\\]\\([^)]*)${escapedPath}`, 'g');
          formattedResponse = formattedResponse.replace(bareRegex, `![${fileName}](file:///${att.path.replace(/\\/g, '/')})`);
        }
      }
    } catch {}

    const characters = Array.from(formattedResponse);
    const parts = [];
    for (let i = 0; i < characters.length; i += 3000) parts.push(characters.slice(i, i + 3000).join(''));
    job.deliveries = [];
    const rawConv = job.conversationId ? (job.conversationId.split(':').pop() || 'def') : 'def';
    for (let i = 0; i < parts.length; i++) {
      if (cancellationRequested()) {
        cancelled = true;
        throw new Error('任务已由用户取消。');
      }
      const partText = (parts.length > 1 ? `(${i + 1}/${parts.length})\n` : '') + parts[i];
      const finalText = partText.startsWith('【反重力') ? partText : '【反重力】\n' + partText;
      const payload = {
        target: job.replyDeviceId || config.target || 'android-f85961cb-a306-46aa-8125-33f9d8614a7e',
        text: finalText,
        requestId: `agy-${job.id}--conv--${rawConv}--${i}`,
        waitForAck: false
      };
      if (job.conversationId) payload.conversationId = job.conversationId;
      const delivery = await sendMessageWithRetry(payload, options, 4);
      if (delivery) {
        job.deliveries.push(delivery);
        save(jobPath, job);
      }
    }
    try {
      const attachments = extractAttachmentsFromText(response);
      if (attachments.length > 0) {
        const listToSend = attachments.slice(0, 8);
        for (let j = 0; j < listToSend.length; j++) {
          if (cancellationRequested()) {
            cancelled = true;
            throw new Error('任务已由用户取消。');
          }
          const att = listToSend[j];
          const filePayload = {
            target: job.replyDeviceId || config.target || 'android-f85961cb-a306-46aa-8125-33f9d8614a7e',
            text: `【反重力】照片: ${path.basename(att.path)}`,
            filePath: att.path,
            kind: att.kind,
            requestId: `agy-att-${job.id}--conv--${rawConv}--${j}`,
            waitForAck: false
          };
          if (job.conversationId) filePayload.conversationId = job.conversationId;
          const delivery = await sendMessageWithRetry(filePayload, options, 4);
          if (delivery) {
            job.deliveries.push(delivery);
            save(jobPath, job);
          }
        }
      }
    } catch {}
    if (cancellationRequested()) {
      cancelled = true;
      throw new Error('任务已由用户取消。');
    }
    job.deliveryStatus = 'submitted';
  } catch (e) {
    if (cancelled || fs.existsSync(cancelPath)) {
      cancelled = true;
      job.status = 'cancelled';
      job.error = '任务已由用户取消。';
      job.deliveryStatus = 'cancelled';
      delete job.deliveryError;
    } else {
      job.deliveryStatus = 'failed';
      job.deliveryError = e.message;
    }
  }
  finally {
    // Completion is a realtime control event; the durable answer may arrive later.
    if (job.status === 'cancelled') await streamPublisher.terminal('cancelled');
    else if (job.deliveryStatus === 'submitted') await streamPublisher.terminal('complete', response);
    else if (job.deliveryStatus === 'failed') await streamPublisher.terminal('error', '最终消息投递失败，请在电脑端使用 /结果 检查保存的回复。');
    clearInterval(cancelTimer);
    save(jobPath, job);
    if (read(activePath).id === job.id && fs.existsSync(activePath)) fs.unlinkSync(activePath);
    if (fs.existsSync(cancelPath)) fs.unlinkSync(cancelPath);
  }
  return job;
}
async function runDaemon(root, options = {}) {
  fs.mkdirSync(root, { recursive: true });
  const daemonPath = path.join(root, 'daemon.json');
  const activePath = path.join(root, 'active.json');
  save(daemonPath, { pid: process.pid, startedAt: new Date().toISOString() });
  let persistent = null;
  let persistentCliPath = '';
  let idleSince = Date.now();
  const idleTimeoutMs = options.idleTimeoutMs || Number(process.env.AGY_SESSION_IDLE_MS || 30 * 60 * 1000);
  try {
    while (true) {
      const active = read(activePath);
      const id = active.id;
      if (id) {
        const jobPath = path.join(root, id + '.json');
        const job = read(jobPath);
        if (job.id === id && job.status === 'pending') {
          const cliPath = options.cliPath || job.cliPath || path.join(process.env.LOCALAPPDATA, 'agy', 'bin', 'agy.exe');
          if (!persistent || persistentCliPath !== cliPath) {
            if (persistent) persistent.dispose();
            persistent = new PersistentCliSession(root, cliPath, options);
            persistentCliPath = cliPath;
          }
          await runJob(jobPath, { ...options, persistentSession: persistent });
          idleSince = Date.now();
          continue;
        }
      }
      if (Date.now() - idleSince >= idleTimeoutMs) break;
      await new Promise(resolve => setTimeout(resolve, options.pollIntervalMs || 150));
    }
  } finally {
    if (persistent) persistent.dispose();
    try {
      const marker = read(daemonPath);
      if (marker.pid === process.pid && fs.existsSync(daemonPath)) fs.unlinkSync(daemonPath);
    } catch {}
  }
}

module.exports = { runJob, runDaemon, PersistentCliSession, streamPreview };
if (require.main === module) {
  const task = process.argv[2] === '--daemon'
    ? runDaemon(process.argv[3])
    : runJob(process.argv[2]);
  task.catch(error => { try { process.stderr.write(String(error?.stack || error) + '\n'); } catch {} process.exitCode = 1; });
}
