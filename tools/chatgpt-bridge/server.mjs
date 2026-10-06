import http from 'node:http';
import { WebSocketServer, WebSocket } from 'ws';
import { randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync, renameSync, existsSync, readdirSync, statSync, unlinkSync } from 'node:fs';
import { join, dirname, basename, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const FILE_MIME_TYPES = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.gif': 'image/gif',
  '.pdf': 'application/pdf',
  '.txt': 'text/plain',
  '.md': 'text/markdown',
  '.json': 'application/json',
  '.csv': 'text/csv',
  '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  '.pptx': 'application/vnd.openxmlformats-officedocument.presentationml.presentation'
};

function normalizeTaskFiles(files) {
  if (files == null) return [];
  if (!Array.isArray(files)) throw new Error('files 必须是数组');
  if (files.length > 4) throw new Error('一次最多传递 4 个文件');

  return files.map((item, index) => {
    const source = typeof item === 'string' ? { path: item } : item;
    if (!source || typeof source.path !== 'string' || !source.path.trim()) {
      throw new Error('files[' + index + '].path 不能为空');
    }
    const name = source.name == null ? null : String(source.name).trim();
    if (name && (name.length > 240 || /[\\/]/.test(name))) {
      throw new Error('files[' + index + '].name 无效');
    }
    return { path: source.path.trim(), name: name || null };
  });
}

function materializeTaskFiles(files) {
  const items = [];
  let totalBytes = 0;

  for (const file of files || []) {
    const filePath = resolve(file.path);
    if (!existsSync(filePath)) throw new Error('文件不存在：' + file.path);
    const stat = statSync(filePath);
    if (!stat.isFile()) throw new Error('不是文件：' + file.path);
    if (stat.size <= 0) throw new Error('文件为空：' + file.path);
    if (stat.size > 8 * 1024 * 1024) throw new Error('单个文件最大 8MB：' + file.path);

    totalBytes += stat.size;
    if (totalBytes > 12 * 1024 * 1024) throw new Error('文件总大小最大 12MB');

    const bytes = readFileSync(filePath);
    const extension = extname(filePath).toLowerCase();
    const mimeType = FILE_MIME_TYPES[extension] || 'application/octet-stream';
    items.push({
      name: file.name || basename(filePath),
      mimeType,
      byteLength: bytes.length,
      base64: bytes.toString('base64')
    });
  }

  return items;
}

export function validateTask(input) {
  const actions = ['chatgpt_status', 'chatgpt_new_chat', 'chatgpt_send', 'chatgpt_image', 'chatgpt_capture_images', 'chatgpt_messages', 'chatgpt_close', 'chatgpt_cleanup'];
  if (!input || !actions.includes(input.action)) throw new Error('不支持的 action');
  if (['chatgpt_send', 'chatgpt_image'].includes(input.action) && (typeof input.prompt !== 'string' || !input.prompt.trim() || input.prompt.length > 100000)) throw new Error('prompt 必须是 1–100000 字符');
  if (input.tabId != null && (!Number.isInteger(input.tabId) || input.tabId < 0)) throw new Error('tabId 无效');
  if (input.timeoutSeconds != null && (!Number.isInteger(input.timeoutSeconds) || input.timeoutSeconds < 10 || input.timeoutSeconds > 1200)) throw new Error('timeoutSeconds 应为 10–1200');
  if (input.newChat != null && typeof input.newChat !== 'boolean') throw new Error('newChat 必须是布尔值');
  if (input.includePageSnapshot != null && typeof input.includePageSnapshot !== 'boolean') throw new Error('includePageSnapshot 必须是布尔值');
  if (input.tabPolicy != null && !['reuse', 'new'].includes(input.tabPolicy)) throw new Error('tabPolicy 应为 reuse 或 new');
  if (input.closeAfter != null && typeof input.closeAfter !== 'boolean') throw new Error('closeAfter 必须是布尔值');
  if (input.temporary != null && typeof input.temporary !== 'boolean') throw new Error('temporary 必须是布尔值');
  if (input.action === 'chatgpt_close' && input.tabId == null) throw new Error('关闭标签页必须指定 tabId');
  if (input.action === 'chatgpt_capture_images' && input.tabId == null) throw new Error('抓取生成图片必须指定 tabId');
  const files = normalizeTaskFiles(input.files);
  if (files.length && !['chatgpt_send', 'chatgpt_image'].includes(input.action)) {
    throw new Error('只有 chatgpt_send / chatgpt_image 支持 files');
  }
  const temporary = input.temporary != null
    ? input.temporary
    : input.action === 'chatgpt_image'
      ? false
      : true;
  return { action: input.action, prompt: input.prompt, files, tabId: input.tabId, newChat: input.newChat, temporary, tabPolicy: input.tabPolicy || 'reuse', closeAfter: input.closeAfter === true, includePageSnapshot: input.includePageSnapshot === true, timeoutSeconds: input.timeoutSeconds || (input.action === 'chatgpt_image' ? 900 : 180) };
}

export async function createBridge({ port = 53921, directory, now = Date.now, allowTestSocket = false } = {}) {
  mkdirSync(directory, { recursive: true });
  const assetsDirectory = join(directory, 'assets');
  mkdirSync(assetsDirectory, { recursive: true });
  const statePath = join(directory, 'state.json');
  const tokenPath = join(directory, 'api-token.txt');
  const token = existsSync(tokenPath) ? readFileSync(tokenPath, 'utf8').trim() : randomBytes(32).toString('hex');
  if (!existsSync(tokenPath)) writeFileSync(tokenPath, token, { mode: 0o600 });
  const state = existsSync(statePath) ? JSON.parse(readFileSync(statePath, 'utf8')) : { jobs: [], schedules: [] };
  for (const job of state.jobs) if (job.status === 'running') Object.assign(job, { status: 'interrupted', message: '服务重启；请求可能已发送，请先查看聊天，未自动重试', completedAt: now() });
  let socket = null, active = null, extensionVersion = null;
  let origin;

  function saveImageAsset(dataUrl) {
    if (typeof dataUrl !== 'string') throw new Error('图片数据为空');
    const match = dataUrl.match(/^data:(image\/(?:png|jpeg|webp|gif));base64,([A-Za-z0-9+/=\r\n]+)$/);
    if (!match) throw new Error('只接受 PNG/JPEG/WebP/GIF 图片数据');

    const bytes = Buffer.from(match[2], 'base64');
    if (!bytes.length) throw new Error('图片数据为空');
    if (bytes.length > 12 * 1024 * 1024) throw new Error('单张图片最大 12MB');

    const extension = {
      'image/png': '.png',
      'image/jpeg': '.jpg',
      'image/webp': '.webp',
      'image/gif': '.gif'
    }[match[1]];
    const id = randomUUID();
    const fileName = id + extension;
    const filePath = join(assetsDirectory, fileName);
    writeFileSync(filePath, bytes, { mode: 0o600 });

    const files = readdirSync(assetsDirectory)
      .filter(name => /^[0-9a-f-]{36}\.(?:png|jpg|webp|gif)$/i.test(name))
      .map(name => {
        const path = join(assetsDirectory, name);
        return { name, path, mtimeMs: statSync(path).mtimeMs };
      })
      .sort((a, b) => b.mtimeMs - a.mtimeMs);
    for (const stale of files.slice(300)) {
      try { unlinkSync(stale.path); } catch {}
    }

    return {
      id,
      fileName,
      mimeType: match[1],
      byteLength: bytes.length,
      url: origin + '/assets/' + fileName
    };
  }

  function persist() {
    const finished = state.jobs.filter(j => !['queued', 'running'].includes(j.status));
    const keep = new Set(finished.slice(-200).map(j => j.id));
    state.jobs = state.jobs.filter(j => ['queued', 'running'].includes(j.status) || keep.has(j.id));
    writeFileSync(statePath + '.tmp', JSON.stringify(state, null, 2), { mode: 0o600 });
    renameSync(statePath + '.tmp', statePath);
  }
  function finish(status, data, message) {
    if (!active) return;
    clearTimeout(active.timer);
    Object.assign(active.job, { status, data, message, completedAt: now() });
    active = null;
    persist();
    pump();
  }
  function pump() {
    if (active || socket?.readyState !== WebSocket.OPEN) return;
    const job = state.jobs.find(j => j.status === 'queued');
    if (!job) return;
    job.status = 'running'; job.startedAt = now(); persist();
    let attachments;
    try {
      attachments = materializeTaskFiles(job.task.files);
    } catch (error) {
      Object.assign(job, {
        status: 'error',
        message: error.message,
        completedAt: now()
      });
      persist();
      pump();
      return;
    }

    active = { job, timer: setTimeout(() => {
      // Close the connection before releasing the queue: timed-out page work may still be running.
      const peer = socket; socket = null;
      finish('timeout', null, '请求超时，可能已发送；请查看聊天后再重试');
      peer?.close(1011, 'task timeout');
    }, (job.task.timeoutSeconds + 45) * 1000) };
    socket.send(JSON.stringify({
      ...job.task,
      files: undefined,
      attachments,
      type: 'task_request',
      taskId: job.id
    }), error => {
      if (error && active?.job === job) { socket = null; finish('interrupted', null, '连接中断，请检查聊天后重试'); }
    });
  }
  function enqueue(input, source = 'manual') {
    const task = validateTask(input);
    if (state.jobs.filter(j => ['queued', 'running'].includes(j.status)).length >= 100) throw new Error('待执行任务已达 100 个');
    const job = { id: randomUUID(), task, source, status: 'queued', createdAt: now() };
    state.jobs.push(job); persist(); pump(); return job;
  }
  function tick() {
    for (const schedule of state.schedules) {
      if (!schedule.enabled || schedule.event || schedule.nextAt > now()) continue;
      // Coalesce missed intervals; never burst all missed occurrences after sleep.
      if (schedule.intervalSeconds) schedule.nextAt = now() + schedule.intervalSeconds * 1000;
      else schedule.enabled = false;
      persist();
      try { enqueue(schedule.task, schedule.id); schedule.lastError = null; }
      catch (error) { schedule.lastError = error.message; }
      persist();
    }
  }
  function authorize(req) {
    const supplied = req.headers.authorization?.replace(/^Bearer /, '') || req.headers.cookie?.split('; ').find(s => s.startsWith('bridge_session='))?.slice(15) || '';
    const suppliedBytes = Buffer.from(supplied), tokenBytes = Buffer.from(token);
    return suppliedBytes.length === tokenBytes.length && timingSafeEqual(suppliedBytes, tokenBytes);
  }
  async function body(req) {
    let text = '';
    for await (const chunk of req) { text += chunk; if (text.length > 200000) throw new Error('请求体过大'); }
    return JSON.parse(text);
  }
  const server = http.createServer(async (req, res) => {
    const reply = (status, data) => { res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(data)); };
    const expectedHost = new URL(origin).host;
    const path = new URL(req.url, origin).pathname;

    // Generated-image assets are read-only and may be embedded by HTTPS pages.
    if (req.headers.host === expectedHost && req.method === 'GET' && path.startsWith('/assets/')) {
      const fileName = path.slice('/assets/'.length);
      if (!/^[0-9a-f-]{36}\.(?:png|jpg|webp|gif)$/i.test(fileName)) {
        res.writeHead(404); res.end(); return;
      }
      const filePath = join(assetsDirectory, fileName);
      if (!existsSync(filePath)) { res.writeHead(404); res.end(); return; }
      const extension = fileName.split('.').pop().toLowerCase();
      const contentType = { png: 'image/png', jpg: 'image/jpeg', webp: 'image/webp', gif: 'image/gif' }[extension];
      res.writeHead(200, {
        'Content-Type': contentType,
        'Content-Length': statSync(filePath).size,
        'Cache-Control': 'public, max-age=31536000, immutable',
        'Cross-Origin-Resource-Policy': 'cross-origin',
        'Access-Control-Allow-Origin': '*',
        'X-Content-Type-Options': 'nosniff'
      });
      res.end(readFileSync(filePath));
      return;
    }

    // Host/Origin checks prevent DNS rebinding and cross-site browser requests.
    if (req.headers.host !== expectedHost || (req.headers.origin && req.headers.origin !== origin)) return reply(403, { error: 'origin_not_allowed' });
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'");
    if (req.method === 'GET' && path === '/health') return reply(200, { ok: true, service: 'yanzi-chatgpt-bridge', connected: socket?.readyState === WebSocket.OPEN, extensionVersion });
    if (req.method === 'GET' && ['/', '/app.js', '/style.css'].includes(path)) {
      const file = path === '/' ? 'index.html' : path.slice(1);
      if (path === '/') res.setHeader('Set-Cookie', `bridge_session=${token}; HttpOnly; SameSite=Strict; Path=/`);
      res.setHeader('Content-Type', path === '/app.js' ? 'text/javascript; charset=utf-8' : path === '/style.css' ? 'text/css; charset=utf-8' : 'text/html; charset=utf-8');
      res.setHeader('Cache-Control', 'no-store');
      res.end(readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'public', file))); return;
    }
    if (!authorize(req)) return reply(401, { error: 'unauthorized' });
    if (req.method !== 'GET' && req.headers['x-bridge-request'] !== '1') return reply(403, { error: 'missing_request_header' });
    try {
      if (req.method === 'GET' && path === '/api/jobs') return reply(200, state.jobs.slice().reverse());
      if (req.method === 'GET' && path.startsWith('/api/jobs/')) {
        const job = state.jobs.find(j => j.id === path.slice(10));
        return reply(job ? 200 : 404, job || { error: 'job_not_found' });
      }
      if (req.method === 'POST' && path === '/api/jobs') return reply(202, enqueue(await body(req)));
      if (req.method === 'GET' && path === '/api/schedules') return reply(200, state.schedules);
      if (req.method === 'POST' && path === '/api/schedules') {
        const input = await body(req), task = validateTask(input.task);
        const event = input.event;
        const intervalSeconds = input.intervalSeconds;
        const at = input.at ? Date.parse(input.at) : null;
        if ([Boolean(event), intervalSeconds != null, at != null].filter(Boolean).length !== 1) throw new Error('请选择一个触发条件：at、intervalSeconds 或 event');
        if (event && (typeof event !== 'string' || !/^[\w.-]{1,100}$/.test(event))) throw new Error('event 使用 1–100 个字母、数字、点、横线');
        if (intervalSeconds != null && (!Number.isInteger(intervalSeconds) || intervalSeconds < 60)) throw new Error('间隔至少 60 秒');
        if (at != null && (!Number.isFinite(at) || at <= now())) throw new Error('at 必须是未来时间，建议附带时区');
        if (state.schedules.length >= 100) throw new Error('最多保存 100 个计划');
        const schedule = { id: randomUUID(), task, event, intervalSeconds, nextAt: at || now() + intervalSeconds * 1000, enabled: true };
        state.schedules.push(schedule); persist(); return reply(201, schedule);
      }
      if (req.method === 'PATCH' && path.startsWith('/api/schedules/')) {
        const schedule = state.schedules.find(s => s.id === path.slice(15));
        if (!schedule) return reply(404, { error: 'schedule_not_found' });
        const input = await body(req);
        if (typeof input.enabled !== 'boolean') throw new Error('enabled 必须是布尔值');
        schedule.enabled = input.enabled; persist(); return reply(200, schedule);
      }
      if (req.method === 'POST' && path === '/api/events') {
        const input = await body(req);
        if (typeof input.name !== 'string') throw new Error('事件 name 不能为空');
        const matches = state.schedules.filter(s => s.enabled && s.event === input.name);
        if (state.jobs.filter(j => ['queued', 'running'].includes(j.status)).length + matches.length > 100) throw new Error('待执行任务已达 100 个');
        return reply(202, { jobs: matches.map(s => enqueue(s.task, s.id)) });
      }
      reply(404, { error: 'not_found' });
    } catch (error) { reply(400, { error: error.message }); }
  });
  const wss = new WebSocketServer({ noServer: true, maxPayload: 20 * 1024 * 1024 });
  server.on('upgrade', (req, peer, head) => {
    if (req.url !== '/v1/browser/ws' || req.headers.host !== new URL(origin).host || (!allowTestSocket && !/^chrome-extension:\/\/[a-p]{32}$/.test(req.headers.origin || ''))) { peer.destroy(); return; }
    wss.handleUpgrade(req, peer, head, ws => wss.emit('connection', ws));
  });
  wss.on('connection', ws => {
    if (socket) { ws.close(1013, 'another extension connected'); return; }
    socket = ws;
    ws.on('message', raw => {
      try {
        const message = JSON.parse(raw.toString());
        if (message.type === 'ping') ws.send(JSON.stringify({ type: 'pong' }));
        if (message.type === 'register') { extensionVersion = message.version || null; pump(); }
        if (message.type === 'asset_upload') {
          try {
            const asset = saveImageAsset(message.dataUrl);
            ws.send(JSON.stringify({ type: 'asset_response', requestId: message.requestId, ok: true, asset }));
          } catch (error) {
            ws.send(JSON.stringify({ type: 'asset_response', requestId: message.requestId, ok: false, error: error.message }));
          }
        }
        if (message.type === 'task_response' && active?.job.id === message.taskId) finish(message.status === 'success' ? 'success' : 'error', message.data, message.message);
      } catch { ws.close(1003, 'invalid message'); }
    });
    ws.on('close', () => {
      if (socket !== ws) return;
      socket = null; extensionVersion = null;
      finish('interrupted', null, '扩展连接断开，请先检查聊天；未自动重发');
    });
    ws.on('error', () => ws.close());
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  origin = `http://127.0.0.1:${server.address().port}`;
  persist();
  const timer = setInterval(tick, 1000);
  return { origin, token, state, tick, enqueue, close: async () => {
    clearInterval(timer);
    socket?.terminate(); socket = null;
    if (active) finish('interrupted', null, '服务停止，请检查聊天后再重试');
    wss.close();
    await new Promise(resolve => server.close(resolve));
  } };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const directory = process.env.YANZI_CHATGPT_DATA || join(process.env.LOCALAPPDATA || process.env.HOME, 'OpenQuickHost', 'ExtensionStorage', 'chatgpt-bridge');
  const bridge = await createBridge({ directory });
  console.log(`ChatGPT 小程序：${bridge.origin}`);
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, async () => { await bridge.close(); process.exit(0); });
}
