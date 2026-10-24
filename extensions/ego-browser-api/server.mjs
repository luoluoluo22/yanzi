import http from 'node:http';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { siteAdapters } from './site-adapters.mjs';
import { buildSiteDetailsLines } from './site-details.mjs';

const extensionDir = path.dirname(fileURLToPath(import.meta.url));
const publicDir = path.join(extensionDir, 'public');
const port = Number(process.env.EGO_BROWSER_API_PORT || 43820);
const host = '127.0.0.1';
const serviceName = 'ego-browser-api';
const serviceVersion = '0.4.4';
const dataDir = process.env.EGO_BROWSER_API_DATA_DIR ||
  path.join(process.env.LOCALAPPDATA || os.homedir(), 'OpenQuickHost', 'ExtensionStorage', 'ego-browser-api');
const tokenFile = path.join(dataDir, 'api-token.txt');
const jobsFile = path.join(dataDir, 'jobs.json');
const sessionsFile = path.join(dataDir, 'sessions.json');
const stdoutLog = path.join(dataDir, 'service.stdout.log');
const stderrLog = path.join(dataDir, 'service.stderr.log');
const maxJobs = 200;
const maxSessions = 50;
const maxBodyBytes = 64 * 1024;
const startedAt = new Date().toISOString();

await fs.mkdir(dataDir, { recursive: true });
let apiToken = '';
try { apiToken = (await fs.readFile(tokenFile, 'utf8')).trim(); } catch {}
if (!/^[a-f0-9]{64}$/i.test(apiToken)) {
  apiToken = crypto.randomBytes(32).toString('hex');
  await fs.writeFile(tokenFile, apiToken, { encoding: 'utf8', mode: 0o600 });
}

let jobs = [];
try {
  const saved = JSON.parse(await fs.readFile(jobsFile, 'utf8'));
  if (Array.isArray(saved)) jobs = saved.slice(-maxJobs);
} catch {}
for (const job of jobs) {
  if (job.status === 'queued' || job.status === 'running') {
    job.status = 'interrupted';
    job.message = '服务重启前任务未完成。请先核实浏览器页面，再由用户决定是否重新提交。';
    job.completedAt = new Date().toISOString();
  }
}
let sessions = [];
try {
  const savedSessions = JSON.parse(await fs.readFile(sessionsFile, 'utf8'));
  if (Array.isArray(savedSessions)) sessions = savedSessions.slice(-maxSessions);
} catch {}
for (const session of sessions) {
  if (session.status === 'creating' || session.status === 'closing') {
    session.status = 'interrupted';
    session.lastError = '服务重启时会话操作尚未完成；请先读取会话状态，再决定是否继续。';
  }
}
let sessionsSaveChain = Promise.resolve();
let saveChain = Promise.resolve();
function persistJobs() {
  const snapshot = JSON.stringify(jobs.slice(-maxJobs), null, 2);
  saveChain = saveChain.then(async () => {
    const tempFile = jobsFile + '.' + process.pid + '.tmp';
    await fs.writeFile(tempFile, snapshot, 'utf8');
    await fs.rename(tempFile, jobsFile);
  }).catch(error => console.error('jobs persistence failed:', error.message));
  return saveChain;
}
await persistJobs();

async function persistSessions() {
  const snapshot = JSON.stringify(sessions.slice(-maxSessions), null, 2);
  sessionsSaveChain = sessionsSaveChain.then(async () => {
    const tempFile = sessionsFile + '.' + process.pid + '.tmp';
    await fs.writeFile(tempFile, snapshot, 'utf8');
    await fs.rename(tempFile, sessionsFile);
  }).catch(error => console.error('sessions persistence failed:', error.message));
  return sessionsSaveChain;
}
await persistSessions();

function findSession(id) {
  return sessions.find(session => session.id === id);
}
function validateNavigationUrl(value) {
  if (typeof value !== 'string' || !value.trim() || value.length > 2048) {
    throw new Error('url 必须是 1–2048 个字符的完整 HTTP(S) 地址。');
  }
  let parsed;
  try { parsed = new URL(value.trim()); }
  catch { throw new Error('无效 URL，请提供完整的 http:// 或 https:// 地址。'); }
  if (!['http:', 'https:'].includes(parsed.protocol)) {
    throw new Error('仅允许 http:// 或 https:// 网页地址。');
  }
  if (parsed.username || parsed.password) {
    throw new Error('为避免泄露凭据，URL 中不能包含用户名或密码。');
  }
  return parsed.href;
}
function safeDisplayUrl(value) {
  try {
    const parsed = new URL(value);
    for (const key of [...parsed.searchParams.keys()]) {
      if (/(token|secret|password|passwd|auth|authorization|api[_-]?key|access[_-]?key|ticket|session|code)/i.test(key)) {
        parsed.searchParams.set(key, '[redacted]');
      }
    }
    if (parsed.hash && parsed.hash.length > 120) parsed.hash = '#[redacted]';
    return parsed.href;
  } catch {
    return String(value || '').slice(0, 2048);
  }
}
function validateSelector(value, required = true) {
  if (typeof value !== 'string' || (required && !value.trim()) || value.length > 500) {
    throw new Error('selector 必须是 1–500 个字符的快照 ref、语义选择器或 CSS 选择器。');
  }
  return value.trim();
}
const allowedHosts = new Set(['127.0.0.1:' + port, 'localhost:' + port]);
const allowedOrigins = new Set(['http://127.0.0.1:' + port, 'http://localhost:' + port]);
function isLoopback(address) {
  const value = String(address || '').toLowerCase();
  return value === '127.0.0.1' || value === '::1' || value === '::ffff:127.0.0.1';
}
function isAllowedOrigin(origin) {
  return !origin || allowedOrigins.has(origin);
}
function sendJson(res, status, value, extraHeaders = {}) {
  const body = JSON.stringify(value, null, 2);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    ...extraHeaders
  });
  res.end(body);
}
function sendText(res, status, text, contentType = 'text/plain; charset=utf-8', extraHeaders = {}) {
  res.writeHead(status, {
    'Content-Type': contentType,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    ...extraHeaders
  });
  res.end(text);
}
function isAuthorized(req) {
  const value = String(req.headers.authorization || '');
  if (!value.startsWith('Bearer ')) return false;
  const supplied = Buffer.from(value.slice(7));
  const expected = Buffer.from(apiToken);
  return supplied.length === expected.length && crypto.timingSafeEqual(supplied, expected);
}
async function readBody(req) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > maxBodyBytes) throw new Error('请求正文超过 64 KB 限制。');
    chunks.push(chunk);
  }
  if (!size) return {};
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new Error('请求正文不是有效 JSON。'); }
}
function egoCliPath() {
  return path.join(os.homedir(), '.local', 'share', 'ego', 'active_version_dir', 'ego-browser.exe');
}
function publicHealth() {
  return {
    ok: true,
    service: serviceName,
    version: serviceVersion,
    status: 'online',
    host,
    port,
    nodeVersion: process.version,
    uptimeSeconds: Math.floor(process.uptime()),
    startedAt,
    now: new Date().toISOString()
  };
}
function findJob(id) {
  return jobs.find(job => job.id === id);
}
function queueSummary() {
  return {
    queued: jobs.filter(job => job.status === 'queued').length,
    running: jobs.filter(job => job.status === 'running').length,
    completed: jobs.filter(job => ['success', 'error', 'timeout', 'interrupted'].includes(job.status)).length
  };
}
function buildTaskScript(taskName, bodyLines, closeAfter = false) {
  const lines = [
    'let task = null;',
    'try {',
    '  task = await taskSpace(' + JSON.stringify(taskName) + ');',
    ...bodyLines,
    '} catch (error) {',
    '  console.log("EGO_BROWSER_API_ERROR=" + JSON.stringify({ message: error?.message || String(error) }));',
    '}'
  ];
  if (closeAfter) {
    lines.push(
      'finally {',
      '  if (task) { try { await task.finish({ keep: [] }); } catch (error) { console.log("EGO_BROWSER_API_FINISH_ERROR=" + String(error?.message || error)); } }',
      '}'
    );
  }
  return lines.join('\n');
}

function executeEgoScript(job, script, timeoutMs = 60000) {
  return new Promise((resolve, reject) => {
    const executable = egoCliPath();
    if (!existsSync(executable)) {
      reject(new Error('未找到 Ego Browser 命令：' + executable));
      return;
    }
    let stdout = '';
    let stderr = '';
    let timedOut = false;
    let settled = false;
    const child = spawn(executable, ['nodejs'], {
      cwd: extensionDir,
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe']
    });
    const settle = (fn, value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      fn(value);
    };
    const timer = setTimeout(() => {
      timedOut = true;
      try { child.kill(); } catch {}
    }, timeoutMs);
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => { stdout = (stdout + chunk).slice(-50000); });
    child.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-20000); });
    child.on('error', error => settle(reject, error));
    child.on('close', code => {
      if (timedOut) {
        const error = new Error('Ego Browser 操作超过 ' + Math.round(timeoutMs / 1000) + ' 秒，已终止本次命令。');
        error.code = 'ETIMEDOUT';
        settle(reject, error);
        return;
      }
      const combinedOutput = stdout + '\n' + stderr;
      const resultLine = combinedOutput.split(/\r?\n/).filter(line => line.includes('EGO_BROWSER_API_RESULT=')).pop();
      const errorLine = combinedOutput.split(/\r?\n/).filter(line => line.includes('EGO_BROWSER_API_ERROR=')).pop();
      if (errorLine) {
        let message = errorLine.slice(errorLine.indexOf('EGO_BROWSER_API_ERROR=') + 'EGO_BROWSER_API_ERROR='.length);
        try { message = JSON.parse(message).message || message; } catch {}
        settle(reject, new Error('Ego 执行失败：' + message));
        return;
      }
      if (resultLine) {
        try {
          const data = JSON.parse(resultLine.slice(resultLine.indexOf('EGO_BROWSER_API_RESULT=') + 'EGO_BROWSER_API_RESULT='.length));
          if (data.url) data.url = safeDisplayUrl(data.url);
          if (Array.isArray(data.tabs)) {
            data.tabs = data.tabs.map(tab => ({ ...tab, url: tab.url ? safeDisplayUrl(tab.url) : tab.url }));
          }
          if (Array.isArray(data.results)) {
            data.results = data.results.map(item => ({ ...item, url: item.url ? safeDisplayUrl(item.url) : item.url }));
          }
          settle(resolve, {
            ...data,
            exitCode: code,
            elapsedSeconds: Math.round((Date.now() - Date.parse(job.startedAt)) / 1000)
          });
        } catch (error) {
          settle(reject, new Error('无法解析 Ego 返回结果：' + error.message));
        }
        return;
      }
      settle(reject, new Error('Ego 未返回结构化结果。退出码：' + code + '；stdout：' + stdout.slice(-1800) + '；stderr：' + stderr.slice(-1200)));
    });
    child.stdin.on('error', error => {
      if (!settled) console.error('Ego stdin error:', error.message);
    });
    child.stdin.end(script + '\n');
  });
}

async function runEgoOperation(job, payload = {}) {
  if (job.operation === 'ego_smoke_test') {
    const taskName = 'ego-browser-api-smoke-' + job.id.replace(/-/g, '').slice(0, 12);
    const lines = [
      '  const page = task.page("p1");',
      '  await page.goto("https://chatgpt.com/?temporary-chat=true");',
      '  await page.waitForLoadState();',
      '  await page.waitForSelector("body", { state: "visible", timeout: 45000 });',
      '  const result = await page.evaluate(() => { const bodyText = document.body?.innerText || ""; const visible = el => !!el && el.getClientRects().length > 0; const editor = [...document.querySelectorAll("#prompt-textarea, main [contenteditable=true][role=textbox], textarea")].find(visible); return { url: location.href, title: document.title, heading: (document.querySelector("h1")?.innerText || "").trim(), readyState: document.readyState, temporaryEvidence: location.search.includes("temporary-chat=true") || /temporary chat|临时聊天/i.test(bodyText), loginNeeded: /log in to continue|登录以继续/i.test(bodyText), editorPresent: !!editor, bodyChars: bodyText.length }; });',
      '  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));'
    ];
    return executeEgoScript(job, buildTaskScript(taskName, lines, true), 60000);
  }

  const session = job.sessionId ? findSession(job.sessionId) : null;
  if (!session) throw new Error('找不到该任务对应的浏览器会话。');
  const taskName = session.name;
  const pageLabel = session.pageLabel || 'p1';
  const pageExpression = 'task.page(' + JSON.stringify(pageLabel) + ')';
  const lines = [];
  if (job.operation === 'session_create') {
    lines.push('  const page = task.page("p1");');
    lines.push('  let navigationError = null;');
    if (payload.url) {
      lines.push('  try { await page.goto(' + JSON.stringify(payload.url) + ', { timeout: 45000, waitUntil: "domcontentloaded" }); } catch (error) { navigationError = error?.message || String(error); }');
      lines.push('  if ((await page.url()).startsWith("chrome-error:")) navigationError = "浏览器返回错误页面，目标站点可能无法访问。";');
    }
    lines.push('  const tabs = await task.tabs();');
    lines.push('  const result = { spaceId: task.spaceId, pageLabel: page.label, url: await page.url(), title: await page.title(), pages: (await task.pages()).map(item => item.label), tabs: tabs.map(tab => ({ label: tab.label || null, title: tab.title, url: tab.url, active: tab.active, openedBy: tab.openedBy })), navigationError };');
    lines.push('  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));');
  } else if (job.operation === 'session_navigate') {
    lines.push('  const page = ' + pageExpression + ';');
    lines.push('  await page.goto(' + JSON.stringify(payload.url) + ', { timeout: 45000, waitUntil: "domcontentloaded" });');
    lines.push('  if ((await page.url()).startsWith("chrome-error:")) throw new Error("浏览器返回错误页面，目标站点可能无法访问。");');
    lines.push('  const result = { url: await page.url(), title: await page.title(), readyState: await page.evaluate(() => document.readyState) };');
    lines.push('  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));');
  } else if (job.operation === 'session_snapshot') {
    lines.push('  const page = ' + pageExpression + ';');
    lines.push('  const snapshotValue = await page.snapshot({ scope: ' + JSON.stringify(payload.scope || 'only_within_viewport') + ' });');
    lines.push('  const snapshotText = typeof snapshotValue === "string" ? snapshotValue : JSON.stringify(snapshotValue);');
    lines.push('  const tabs = await task.tabs();');
    lines.push('  const result = { url: await page.url(), title: await page.title(), snapshot: snapshotText.slice(0, 30000), truncated: snapshotText.length > 30000, tabs: tabs.map(tab => ({ label: tab.label || null, title: tab.title, url: tab.url, active: tab.active, openedBy: tab.openedBy })) };');
    lines.push('  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));');
  } else if (job.operation === 'session_action') {
    lines.push('  const page = ' + pageExpression + ';');
    const selector = JSON.stringify(payload.selector || '');
    if (payload.action === 'click') {
      lines.push('  await page.click(' + selector + ', { timeout: 8000, label: "API click action" });');
    } else if (payload.action === 'fill') {
      lines.push('  await page.fill(' + selector + ', ' + JSON.stringify(payload.value) + ', { timeout: 8000 });');
    } else if (payload.action === 'press') {
      lines.push('  await page.press(' + selector + ', ' + JSON.stringify(payload.key) + ', { timeout: 8000 });');
    } else if (payload.action === 'select') {
      lines.push('  await page.selectOption(' + selector + ', ' + JSON.stringify(payload.value) + ');');
    } else if (payload.action === 'wait_for_selector') {
      lines.push('  await page.waitForSelector(' + selector + ', { timeout: ' + Number(payload.timeoutMs || 10000) + ', state: ' + JSON.stringify(payload.state || 'visible') + ' });');
    } else if (payload.action === 'reload') {
      lines.push('  await page.reload({ timeout: 45000, waitUntil: "domcontentloaded" });');
    } else {
      throw new Error('不支持的页面操作。');
    }

    lines.push('  const result = { action: ' + JSON.stringify(payload.action) + ', selector: ' + JSON.stringify(payload.selector || '') + ', url: await page.url(), title: await page.title() };');
    lines.push('  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));');
  } else if (job.operation === 'site_action') {
    const adapter = siteAdapters[payload.site];
    if (!adapter) throw new Error('未找到对应的网站适配器。');
    lines.push('  const page = ' + pageExpression + ';');
    if (!['open', 'new_chat', 'search'].includes(payload.action)) {
      lines.push('  const currentUrl = await page.url();');
      lines.push('  let currentHost = ""; try { currentHost = new URL(currentUrl).hostname; } catch {}');
      lines.push('  if (!' + JSON.stringify(adapter.allowedHosts) + '.includes(currentHost)) throw new Error("当前浏览器页面与目标网站不匹配。请重新打开该网站或选择正确会话。当前地址：" + currentUrl);');
    }
    if (['open', 'new_chat', 'search'].includes(payload.action)) {
      if (!payload.targetUrl) throw new Error('该网站操作缺少目标网址。');
      lines.push('  await page.goto(' + JSON.stringify(payload.targetUrl) + ', { timeout: 45000, waitUntil: "domcontentloaded" });');
      lines.push('  if ((await page.url()).startsWith("chrome-error:")) throw new Error("浏览器返回错误页面，目标站点可能无法访问。");');
      lines.push('  await page.waitForSelector("body", { state: "visible", timeout: 12000 });');
      lines.push('  for (let i = 0; i < 8 && !(await page.title()).trim(); i++) await new Promise(resolve => setTimeout(resolve, 500));');
      lines.push('  const result = { site: ' + JSON.stringify(payload.site) + ', action: ' + JSON.stringify(payload.action) + ', url: await page.url(), title: await page.title(), readyState: await page.evaluate(() => document.readyState), spaceId: task.spaceId, pageLabel: page.label };');
    } else if (payload.action === 'read_page') {
      lines.push('  await page.waitForSelector("body", { state: "visible", timeout: 10000 });');
      lines.push('  const snapshotValue = await page.snapshot({ scope: "only_within_viewport" });');
      lines.push('  const snapshotText = typeof snapshotValue === "string" ? snapshotValue : JSON.stringify(snapshotValue);');
      lines.push('  const bodyText = await page.evaluate(() => (document.body?.innerText || "").slice(0, 12000));');
      lines.push('  const result = { site: ' + JSON.stringify(payload.site) + ', action: "read_page", url: await page.url(), title: await page.title(), text: bodyText, snapshot: snapshotText.slice(0, 16000), truncated: snapshotText.length > 16000 };');
    } else if (payload.action === 'extract_results') {
      lines.push('  const extracted = await page.evaluate(() => { const selectors = ' + JSON.stringify(adapter.resultSelectors) + '; const allowedHosts = ' + JSON.stringify(adapter.allowedHosts) + '; const titleSelectors = ' + JSON.stringify(adapter.titleSelectors || []) + '; const items = []; const seen = new Set(); for (const selector of selectors) { for (const element of document.querySelectorAll(selector)) { try { const anchor = element.tagName === "A" ? element : (element.querySelector("a[href]") || element.closest("a[href]")); const href = anchor ? anchor.getAttribute("href") : element.getAttribute("href"); let link = null; if (href) { link = new URL(href, location.href); if (!["http:", "https:"].includes(link.protocol) || !allowedHosts.includes(link.hostname)) continue; } else if (element.tagName === "A") continue; const card = element.matches("article, li, [data-e2e=video-card], .bili-video-card, .note-item, .search-result-card") ? element : (element.closest("article, li, [data-e2e=video-card], .bili-video-card, .note-item, .search-result-card") || element); const titleElement = titleSelectors.map(selector => card.querySelector(selector)).find(element => element && ((element.innerText || "").trim() || element.getAttribute("title"))); const lines = (card.innerText || "").split(String.fromCharCode(10)).map(line => line.trim()).filter(Boolean); const lineTitle = lines.find(line => !line.includes(":") && !/^[0-9,.万亿]+$/.test(line) && !line.startsWith("@") && !line.startsWith("·")); const title = ((titleElement && (titleElement.getAttribute("title") || titleElement.innerText)) || (anchor && anchor.getAttribute("title")) || (anchor && anchor.getAttribute("aria-label")) || lineTitle || "").trim().replace(/\\s+/g, " "); if (!title) continue; const key = link ? link.href : element; if (seen.has(key)) continue; seen.add(key); items.push({ title: title.slice(0, 300), url: link ? link.href : null, summary: (card.innerText || "").trim().replace(/\\s+/g, " ").slice(0, 500), clickable: !link }); if (items.length >= 30) break; } catch {} } if (items.length >= 30) break; } return { count: items.length, items }; });');
      lines.push('  const result = { site: ' + JSON.stringify(payload.site) + ', action: "extract_results", url: await page.url(), title: await page.title(), count: extracted.count, results: extracted.items };');
    } else if (payload.action === 'extract_details') {
      lines.push(...buildSiteDetailsLines(adapter, payload.site));
    } else if (payload.action === 'open_result') {
      if (payload.site === 'douyin') {
        lines.push('  const targetTitle = await page.evaluate(() => { const cards = [...document.querySelectorAll("div.search-result-card")]; const target = cards[' + Number(payload.index || 0) + ']; if (!target) return ""; const cardLines = (target.innerText || "").split(String.fromCharCode(10)).map(line => line.trim()).filter(Boolean); return cardLines.find(line => !line.includes(":") && !/^[0-9,.万亿]+$/.test(line) && !line.startsWith("@") && !line.startsWith("·")) || ""; });');
        lines.push('  if (!targetTitle) throw new Error("当前页面没有找到该序号的视频卡片。请先执行“提取结果”，确认结果数量大于 0，并检查是否需要登录、验证或等待限流恢复。");');
        lines.push('  await page.click("text=" + targetTitle, { timeout: 8000, label: "Open Douyin result" });');
        lines.push('  await new Promise(resolve => setTimeout(resolve, 1000));');
        lines.push('  const result = { site: "douyin", action: "open_result", index: ' + Number(payload.index || 0) + ', url: await page.url(), title: await page.title(), clicked: true, resultTitle: targetTitle, readyState: await page.evaluate(() => document.readyState) };');
      } else {
        lines.push('  const targetUrl = await page.evaluate(() => { const selectors = ' + JSON.stringify(adapter.resultSelectors) + '; const allowedHosts = ' + JSON.stringify(adapter.allowedHosts) + '; const wantedIndex = ' + Number(payload.index || 0) + '; const items = []; const seen = new Set(); for (const selector of selectors) { for (const anchor of document.querySelectorAll(selector)) { try { const href = anchor.getAttribute("href"); if (!href) continue; const link = new URL(href, location.href); if (!["http:", "https:"].includes(link.protocol) || !allowedHosts.includes(link.hostname) || seen.has(link.href)) continue; seen.add(link.href); items.push(link.href); } catch {} } } return items[wantedIndex] || null; });');
        lines.push('  if (!targetUrl) throw new Error("当前页面没有找到该序号的站内结果。请先执行“提取结果”，确认结果数量大于 0，并检查是否需要登录、验证或等待限流恢复。");');
        lines.push('  const target = new URL(targetUrl);');
        lines.push('  if (!' + JSON.stringify(adapter.allowedHosts) + '.includes(target.hostname)) throw new Error("结果链接不属于当前网站，已阻止导航。");');
        lines.push('  await page.goto(target.href, { timeout: 45000, waitUntil: "domcontentloaded" });');
        lines.push('  if ((await page.url()).startsWith("chrome-error:")) throw new Error("目标内容无法打开。");');
        lines.push('  const result = { site: ' + JSON.stringify(payload.site) + ', action: "open_result", index: ' + Number(payload.index || 0) + ', url: await page.url(), title: await page.title(), readyState: await page.evaluate(() => document.readyState) };');
      }    } else if (payload.action === 'fill_prompt') {
      lines.push('  let editorSelector = "#prompt-textarea";');
      lines.push('  try { await page.fill(editorSelector, ' + JSON.stringify(payload.text) + ', { timeout: 8000 }); } catch { editorSelector = "main [contenteditable=true][role=textbox]"; await page.fill(editorSelector, ' + JSON.stringify(payload.text) + ', { timeout: 8000 }); }');
      lines.push('  const result = { site: "chatgpt", action: "fill_prompt", url: await page.url(), title: await page.title(), filled: true, textLength: ' + String(payload.text.length) + ', editorSelector };');
    } else if (payload.action === 'send_prompt') {
      lines.push('  let editorSelector = "#prompt-textarea";');
      lines.push('  try { await page.fill(editorSelector, ' + JSON.stringify(payload.text) + ', { timeout: 8000 }); } catch { editorSelector = "main [contenteditable=true][role=textbox]"; await page.fill(editorSelector, ' + JSON.stringify(payload.text) + ', { timeout: 8000 }); }');
      // Do not use Enter: multi-line ChatGPT prompts can remain as unsent drafts.
      // Unicode escapes keep the generated Ego CLI script ASCII-safe on Windows.
      lines.push("  const sendSelector = 'button[aria-label=\"\\u53d1\\u9001\"], button[data-testid=\"send-button\"], button[aria-label=\"Send\"]';");
      lines.push('  await page.click(sendSelector, { timeout: 30000 });');
      lines.push('  let cleared=false; for(let i=0;i<20;i++){ const draft=await page.evaluate(() => {const box=document.querySelector("#prompt-textarea"); return (box?.innerText || box?.value || "").trim();}); if(!draft){cleared=true;break;} await new Promise(resolve=>setTimeout(resolve,300)); } if(!cleared) throw new Error("send_not_confirmed_composer_not_cleared");');
      lines.push('  const result = { site: "chatgpt", action: "send_prompt", url: await page.url(), title: await page.title(), submitted: true, textLength: ' + String(payload.text.length) + ' };');
    } else if (payload.action === 'read_response') {
      lines.push('  const answer = await page.evaluate(() => { const nodes = [...document.querySelectorAll("[data-message-author-role=assistant]")]; return { count: nodes.length, text: nodes.length ? (nodes[nodes.length - 1].innerText || "").slice(0, 12000) : "" }; });');
      lines.push('  const result = { site: "chatgpt", action: "read_response", url: await page.url(), title: await page.title(), assistantMessageCount: answer.count, hasResponse: Boolean(answer.text.trim()), pageState: answer.text.trim() ? "assistant_response_found" : "no_assistant_response", response: answer.text };');
    } else if (payload.action === 'reload') {
      lines.push('  await page.reload({ timeout: 45000, waitUntil: "domcontentloaded" });');
      lines.push('  const result = { site: ' + JSON.stringify(payload.site) + ', action: "reload", url: await page.url(), title: await page.title(), readyState: await page.evaluate(() => document.readyState) };');
    } else {
      throw new Error('不支持的网站操作：' + payload.action);
    }
    lines.push('  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));');  } else if (job.operation === 'session_close') {
    lines.push('  const receipt = await task.finish({ keep: [] });');
    lines.push('  task = null;');
    lines.push('  const result = { closed: true, receipt };');
    lines.push('  console.log("EGO_BROWSER_API_RESULT=" + JSON.stringify(result));');
  } else {
    throw new Error('不支持的 Ego Browser 操作：' + job.operation);
  }

  const script = buildTaskScript(taskName, lines, false);
  const result = await executeEgoScript(job, script, job.operation === 'session_snapshot' ? 45000 : 60000);
  session.lastActivityAt = new Date().toISOString();
  session.lastOperation = job.operation === 'site_action' ? 'site:' + payload.site + ':' + payload.action : job.operation;
  session.lastError = result.navigationError || null;
  if (result.url) session.latestUrl = safeDisplayUrl(result.url);
  if (result.title) session.latestTitle = String(result.title).slice(0, 500);
  if (job.operation === 'session_create' || job.operation === 'site_action') {
    session.status = 'active';
    if (result.spaceId) session.spaceId = result.spaceId;
    session.pageLabel = result.pageLabel || session.pageLabel || 'p1';
  } else if (job.operation === 'session_close') {
    session.status = 'closed';
    session.closedAt = new Date().toISOString();
    session.lastError = null;
  } else {
    session.status = 'active';
  }
  await persistSessions();
  return result;
}
const pendingIds = [];
const jobPayloads = new Map();
async function enqueueJob(operation, payload = {}, sessionId = null) {
  if (pendingIds.length >= 20) {
    const error = new Error('待执行队列已满，请稍后再试。');
    error.statusCode = 429;
    throw error;
  }
  const job = {
    id: crypto.randomUUID(),
    operation,
    status: 'queued',
    createdAt: new Date().toISOString(),
    startedAt: null,
    completedAt: null,
    message: '任务已接收，等待执行。',
    data: null
  };
  if (sessionId) job.sessionId = sessionId;
  jobs.push(job);
  if (jobs.length > maxJobs) jobs = jobs.slice(-maxJobs);
  if (payload && Object.keys(payload).length) jobPayloads.set(job.id, payload);
  pendingIds.push(job.id);
  await persistJobs();
  return job;
}
let workerRunning = false;
async function drainQueue() {
  if (workerRunning) return;
  workerRunning = true;
  try {
    while (pendingIds.length) {
      const id = pendingIds.shift();
      const job = findJob(id);
      if (!job || job.status !== 'queued') continue;
      job.status = 'running';
      job.startedAt = new Date().toISOString();
      job.message = '正在执行 Ego Browser 操作：' + job.operation + '。';
      await persistJobs();
      try {
        job.data = await runEgoOperation(job, jobPayloads.get(id) || {});
        job.status = 'success';
        job.message = ({
          ego_smoke_test: 'Ego Browser 自检已完成。',
          session_create: '浏览器会话已创建。',
          session_navigate: '页面导航已完成。',
          session_snapshot: '页面快照已读取。',
          session_action: '页面操作已执行并读取结果。',
          site_action: '网站适配器操作已完成。',
          session_close: '浏览器会话已关闭。'
        })[job.operation] || 'Ego Browser 操作已完成。';
      } catch (error) {
        job.status = error && error.code === 'ETIMEDOUT' ? 'timeout' : 'error';
        job.message = String(error && error.message ? error.message : error).slice(0, 2000);
        if (job.sessionId) {
          const session = findSession(job.sessionId);
          if (session) {
            if (job.operation === 'session_create' || (job.operation === 'site_action' && session.status === 'creating')) session.status = 'error';
            else if (job.operation === 'session_close') session.status = 'active';
            else if (job.operation === 'site_action' && /当前浏览器页面与目标网站不匹配/.test(job.message)) session.status = 'interrupted';
            session.lastError = job.message;
            session.lastActivityAt = new Date().toISOString();
            await persistSessions();
          }
        }
      }
      jobPayloads.delete(id);
      job.completedAt = new Date().toISOString();
      await persistJobs();
    }
  } finally {
    workerRunning = false;
    if (pendingIds.length) void drainQueue();
  }
}
async function readLog(filePath, lineLimit = 80) {
  try {
    const text = await fs.readFile(filePath, 'utf8');
    return text.split(/\r?\n/).filter(Boolean).slice(-lineLimit);
  } catch { return []; }
}
async function serveStatic(res, fileName) {
  const fullPath = path.join(publicDir, fileName);
  try {
    const content = await fs.readFile(fullPath);
    const ext = path.extname(fullPath).toLowerCase();
    const type = ext === '.html' ? 'text/html; charset=utf-8' :
      ext === '.js' ? 'text/javascript; charset=utf-8' :
      ext === '.css' ? 'text/css; charset=utf-8' : 'application/octet-stream';
    res.writeHead(200, {
      'Content-Type': type,
      'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff',
      'Referrer-Policy': 'no-referrer'
    });
    res.end(content);
  } catch {
    sendText(res, 404, 'Not found');
  }
}

const server = http.createServer(async (req, res) => {
  const hostHeader = String(req.headers.host || '').toLowerCase();
  const origin = String(req.headers.origin || '');
  const remote = req.socket.remoteAddress;
  if (!allowedHosts.has(hostHeader) || !isLoopback(remote)) {
    sendJson(res, 403, { ok: false, error: '本服务仅接受本机回环地址请求。' });
    return;
  }
  let url;
  try { url = new URL(req.url || '/', 'http://' + hostHeader); }
  catch { sendJson(res, 400, { ok: false, error: '无效 URL。' }); return; }

  if (req.method === 'OPTIONS' && url.pathname === '/health') {
    res.writeHead(204, {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Methods': 'GET, OPTIONS',
      'Access-Control-Allow-Private-Network': 'true',
      'Access-Control-Max-Age': '300'
    });
    res.end();
    return;
  }
  if (req.method === 'GET' && url.pathname === '/health') {
    sendJson(res, 200, publicHealth(), {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Private-Network': 'true'
    });
    return;
  }
  if (!isAllowedOrigin(origin)) {
    sendJson(res, 403, { ok: false, error: '拒绝非本机工作台来源的请求。' });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/api/ui-config') {
    sendJson(res, 200, { ok: true, apiBase: 'http://' + hostHeader, token: apiToken, version: serviceVersion });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/') return serveStatic(res, 'index.html');
  if (req.method === 'GET' && url.pathname === '/app.js') return serveStatic(res, 'app.js');
  if (req.method === 'GET' && url.pathname === '/style.css') return serveStatic(res, 'style.css');
  if (req.method === 'GET' && (url.pathname === '/docs' || url.pathname === '/docs.html')) return serveStatic(res, 'docs.html');

  if (url.pathname.startsWith('/api/') && !isAuthorized(req)) {
    sendJson(res, 401, { ok: false, error: '缺少或无效的本机 API 令牌。' });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/api/sites') {
    const publicSites = Object.values(siteAdapters).map(site => ({
      id: site.id,
      name: site.name,
      icon: site.icon,
      category: site.category,
      description: site.description,
      homeUrl: site.homeUrl,
      actions: site.actions
    }));
    sendJson(res, 200, { ok: true, sites: publicSites, count: publicSites.length });
    return;
  }

  const siteActionRoute = url.pathname.match(/^\/api\/sites\/([^/]+)\/actions$/);
  if (req.method === 'POST' && siteActionRoute) {
    const siteId = decodeURIComponent(siteActionRoute[1]);
    const adapter = siteAdapters[siteId];
    if (!adapter) return sendJson(res, 404, { ok: false, error: '未找到该网站适配器。请先读取 /api/sites 获取支持列表。' });
    let body;
    try { body = await readBody(req); }
    catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    const action = adapter.actions.find(item => item.id === body.action);
    if (!action) return sendJson(res, 400, { ok: false, error: '该网站不支持此操作。', supportedActions: adapter.actions.map(item => item.id) });
    if (action.requiresConfirm && body.confirm !== true) {
      return sendJson(res, 409, { ok: false, error: '此操作需要显式确认：请传入 confirm=true。' });
    }
    const payload = { site: siteId, action: action.id };
    if (action.input === 'query') {
      if (typeof body.query !== 'string' || !body.query.trim() || body.query.length > 300) {
        return sendJson(res, 400, { ok: false, error: 'query 必须为 1–300 个字符。' });
      }
      payload.query = body.query.trim();
      payload.targetUrl = adapter.searchUrl(payload.query);
    } else if (action.input === 'text') {
      if (typeof body.text !== 'string' || !body.text.trim() || body.text.length > 20000) {
        return sendJson(res, 400, { ok: false, error: 'text 必须为 1–20000 个字符。' });
      }
      payload.text = body.text;
    } else if (action.input === 'index') {
      const index = body.index === undefined ? 0 : Number(body.index);
      if (!Number.isInteger(index) || index < 0 || index > 29) {
        return sendJson(res, 400, { ok: false, error: 'index 必须是 0–29 的整数。' });
      }
      payload.index = index;
    }

    if (['open', 'new_chat'].includes(action.id)) payload.targetUrl = adapter.homeUrl;
    if (payload.targetUrl) {
      try {
        payload.targetUrl = validateNavigationUrl(payload.targetUrl);
        const targetHost = new URL(payload.targetUrl).hostname;
        if (!adapter.allowedHosts.includes(targetHost)) throw new Error('目标网址不属于当前网站。');
      } catch (error) {
        return sendJson(res, 400, { ok: false, error: error.message });
      }
    }

    const operation = action.id === 'close_session' ? 'session_close' : 'site_action';
    let session = null;
    const requestedSessionId = typeof body.sessionId === 'string' ? body.sessionId.trim() : '';
    if (requestedSessionId) {
      session = findSession(requestedSessionId);
      if (!session) return sendJson(res, 404, { ok: false, error: '未找到指定的浏览器会话。' });
      if (session.site && session.site !== siteId) {
        return sendJson(res, 409, { ok: false, error: '所选会话属于 ' + (siteAdapters[session.site]?.name || session.site) + '，请切换到对应网站会话。' });
      }
      if (['creating', 'closing'].includes(session.status)) {
        return sendJson(res, 409, { ok: false, error: '该会话正在执行其他操作，请稍后再试。' });
      }
      if (['closed', 'error'].includes(session.status)) {
        return sendJson(res, 409, { ok: false, error: '该会话已关闭或创建失败，请新建会话。' });
      }
      if (!session.site) {
        let currentHost = '';
        try { currentHost = new URL(session.latestUrl || '').hostname; } catch {}
        if (!adapter.allowedHosts.includes(currentHost) && !payload.targetUrl) {
          return sendJson(res, 409, { ok: false, error: '该通用会话尚未打开此网站；请先执行“打开网站”或“搜索”。' });
        }
        session.site = siteId;
      }
    } else if (payload.targetUrl) {
      const id = crypto.randomUUID();
      session = {
        id,
        name: 'ego-site-' + siteId + '-' + id.replace(/-/g, '').slice(0, 12),
        site: siteId,
        pageLabel: 'p1',
        spaceId: null,
        status: 'creating',
        createdAt: new Date().toISOString(),
        lastActivityAt: new Date().toISOString(),
        latestUrl: null,
        latestTitle: null,
        lastOperation: null,
        lastError: null
      };
      sessions.push(session);
      if (sessions.length > maxSessions) sessions = sessions.slice(-maxSessions);
    } else {
      return sendJson(res, 400, { ok: false, error: '此操作需要先选择该网站的会话，或先执行打开/搜索操作。' });
    }

    if (operation === 'session_close') session.status = 'closing';
    let job;
    try {
      job = await enqueueJob(operation, operation === 'session_close' ? {} : payload, session.id);
    } catch (error) {
      if (session.status === 'creating') sessions = sessions.filter(item => item.id !== session.id);
      if (operation === 'session_close') session.status = 'active';
      await persistSessions();
      return sendJson(res, error.statusCode || 500, { ok: false, error: error.message });
    }
    await persistSessions();
    sendJson(res, 202, { ok: true, site: siteId, session, job });
    void drainQueue();
    return;
  }
  if (req.method === 'GET' && url.pathname === '/api/sessions') {
    sendJson(res, 200, {
      ok: true,
      sessions: sessions.slice().reverse().map(session => ({ ...session })),
      count: sessions.length
    });
    return;
  }
  if (req.method === 'POST' && url.pathname === '/api/sessions') {
    let body;
    try { body = await readBody(req); }
    catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    let initialUrl = null;
    if (body.url !== undefined && body.url !== null && String(body.url).trim()) {
      try { initialUrl = validateNavigationUrl(body.url); }
      catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    }
    if (pendingIds.length >= 20) return sendJson(res, 429, { ok: false, error: '待执行队列已满，请稍后再试。' });
    const id = crypto.randomUUID();
    const session = {
      id,
      name: 'ego-api-' + id.replace(/-/g, '').slice(0, 16),
      pageLabel: 'p1',
      spaceId: null,
      status: 'creating',
      createdAt: new Date().toISOString(),
      lastActivityAt: new Date().toISOString(),
      latestUrl: null,
      latestTitle: null,
      lastOperation: null,
      lastError: null
    };
    sessions.push(session);
    if (sessions.length > maxSessions) sessions = sessions.slice(-maxSessions);
    await persistSessions();
    let job;
    try { job = await enqueueJob('session_create', { url: initialUrl }, session.id); }
    catch (error) {
      sessions = sessions.filter(item => item.id !== session.id);
      await persistSessions();
      return sendJson(res, error.statusCode || 500, { ok: false, error: error.message });
    }
    sendJson(res, 202, { ok: true, session, job });
    void drainQueue();
    return;
  }

  const sessionRoute = url.pathname.match(/^\/api\/sessions\/([^/]+)(?:\/(navigate|snapshot|actions|close))?$/);
  if (sessionRoute && req.method === 'GET' && !sessionRoute[2]) {
    const id = decodeURIComponent(sessionRoute[1]);
    const session = findSession(id);
    if (!session) return sendJson(res, 404, { ok: false, error: '未找到浏览器会话。' });
    const latestJob = jobs.slice().reverse().find(job => job.sessionId === id) || null;
    sendJson(res, 200, { ok: true, session, latestJob });
    return;
  }
  if (sessionRoute && req.method === 'POST' && sessionRoute[2]) {
    const id = decodeURIComponent(sessionRoute[1]);
    const actionRoute = sessionRoute[2];
    const session = findSession(id);
    if (!session) return sendJson(res, 404, { ok: false, error: '未找到浏览器会话。' });
    if (['creating', 'closing'].includes(session.status)) {
      return sendJson(res, 409, { ok: false, error: '该会话当前正在执行其他生命周期操作，请稍后再试。' });
    }
    if (session.status === 'closed' || session.status === 'error') {
      return sendJson(res, 409, { ok: false, error: '该会话当前不可操作，请创建新会话。' });
    }
    let body = {};
    if (actionRoute !== 'close') {
      try { body = await readBody(req); }
      catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    }
    let operation;
    let payload = {};
    try {
      if (actionRoute === 'navigate') {
        operation = 'session_navigate';
        payload.url = validateNavigationUrl(body.url);
      } else if (actionRoute === 'snapshot') {
        operation = 'session_snapshot';
        payload.scope = body.scope || 'only_within_viewport';
        if (!['only_within_viewport', 'full_page'].includes(payload.scope)) {
          throw new Error('scope 仅支持 only_within_viewport 或 full_page。');
        }
      } else if (actionRoute === 'actions') {
        operation = 'session_action';
        const allowedActions = ['click', 'fill', 'press', 'select', 'wait_for_selector', 'reload'];
        if (!allowedActions.includes(body.action)) throw new Error('action 仅支持 click、fill、press、select、wait_for_selector、reload。');
        payload.action = body.action;
        if (body.action !== 'reload') payload.selector = validateSelector(body.selector);
        if (body.action === 'fill') {
          if (typeof body.value !== 'string' || body.value.length > 20000) throw new Error('fill 的 value 必须是字符串且不超过 20000 个字符。');
          payload.value = body.value;
        }
        if (body.action === 'press') {
          if (typeof body.key !== 'string' || !body.key.trim() || body.key.length > 80) throw new Error('press 的 key 必须是 1–80 个字符。');
          payload.key = body.key.trim();
        }
        if (body.action === 'select') {
          if (typeof body.value !== 'string' || body.value.length > 500) throw new Error('select 的 value 必须是字符串且不超过 500 个字符。');
          payload.value = body.value;
        }
        if (body.action === 'wait_for_selector') {
          payload.state = body.state || 'visible';
          if (!['attached', 'detached', 'visible', 'hidden'].includes(payload.state)) throw new Error('wait state 无效。');
          payload.timeoutMs = Math.max(1000, Math.min(15000, Number(body.timeoutMs) || 10000));
        }
      } else if (actionRoute === 'close') {
        operation = 'session_close';
      }
    } catch (error) {
      return sendJson(res, 400, { ok: false, error: error.message });
    }
    if (pendingIds.length >= 20) return sendJson(res, 429, { ok: false, error: '待执行队列已满，请稍后再试。' });
    if (operation === 'session_close') session.status = 'closing';
    let job;
    try {
      job = await enqueueJob(operation, payload, session.id);
    } catch (error) {
      if (operation === 'session_close') session.status = 'active';
      await persistSessions();
      return sendJson(res, error.statusCode || 500, { ok: false, error: error.message });
    }
    await persistSessions();
    sendJson(res, 202, { ok: true, session, job });
    void drainQueue();
    return;
  }
  if (req.method === 'GET' && url.pathname === '/api/environment') {
    const egoPath = egoCliPath();
    sendJson(res, 200, {
      ok: true,
      nodeVersion: process.version,
      nodeExecutable: process.execPath,
      egoCliExists: existsSync(egoPath),
      egoCliPath: egoPath,
      extensionDir,
      dataDir,
      port,
      platform: process.platform,
      architecture: process.arch
    });
    return;
  }
  if (req.method === 'GET' && url.pathname === '/api/jobs') {
    const limit = Math.max(1, Math.min(100, Number(url.searchParams.get('limit') || 30)));
    sendJson(res, 200, { ok: true, summary: queueSummary(), jobs: jobs.slice(-limit).reverse() });
    return;
  }
  if (req.method === 'GET' && url.pathname.startsWith('/api/jobs/')) {
    const id = decodeURIComponent(url.pathname.slice('/api/jobs/'.length));
    const job = findJob(id);
    if (!job) return sendJson(res, 404, { ok: false, error: '未找到任务。' });
    sendJson(res, 200, { ok: true, job });
    return;
  }
  if (req.method === 'POST' && url.pathname === '/api/jobs') {
    let body;
    try { body = await readBody(req); }
    catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    if (body.operation !== 'ego_smoke_test') {
      return sendJson(res, 400, { ok: false, error: '当前 MVP 仅开放 ego_smoke_test 只读诊断操作。' });
    }
    if (pendingIds.length >= 20) return sendJson(res, 429, { ok: false, error: '待执行队列已满，请稍后再试。' });
    const job = {
      id: crypto.randomUUID(),
      operation: 'ego_smoke_test',
      status: 'queued',
      createdAt: new Date().toISOString(),
      startedAt: null,
      completedAt: null,
      message: '任务已接收，等待执行。',
      data: null
    };
    jobs.push(job);
    if (jobs.length > maxJobs) jobs = jobs.slice(-maxJobs);
    pendingIds.push(job.id);
    await persistJobs();
    sendJson(res, 202, { ok: true, id: job.id, status: job.status, job });
    void drainQueue();
    return;
  }
  if (req.method === 'GET' && url.pathname === '/api/logs') {
    const [stdout, stderr] = await Promise.all([readLog(stdoutLog), readLog(stderrLog)]);
    sendJson(res, 200, { ok: true, stdout, stderr });
    return;
  }
  if (req.method === 'GET' && url.pathname === '/api/openapi.json') {
    sendJson(res, 200, {
      openapi: '3.1.0',
      info: { title: 'Ego Browser API', version: serviceVersion, description: '本机异步 Ego Browser API，含浏览器会话和网站分类适配器。' },
      servers: [{ url: 'http://127.0.0.1:' + port }],
      paths: {
        '/health': {
          get: {
            summary: '读取服务健康状态',
            security: [],
            responses: { '200': { description: '健康状态' } }
          }
        },
        '/api/environment': {
          get: {
            summary: '读取 Node 与 Ego 环境',
            responses: { '200': { description: '运行环境' } }
          }
        },
        '/api/jobs': {
          get: { summary: '列出最近任务', responses: { '200': { description: '任务列表' } } },
          post: {
            summary: '提交只读 Ego 页面自检',
            requestBody: {
              required: true,
              content: {
                'application/json': {
                  schema: {
                    type: 'object',
                    required: ['operation'],
                    properties: { operation: { const: 'ego_smoke_test' } }
                  }
                }
              }
            },
            responses: { '202': { description: '任务已排队' } }
          }
        },
        '/api/jobs/{id}': {
          get: {
            summary: '查询任务状态与结果',
            parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'string' } }],
            responses: { '200': { description: '任务详情' } }
          }
        },
        '/api/sites': {
          get: { summary: '列出已注册的网站适配器与常用操作', responses: { '200': { description: '网站适配器目录' } } }
        },
        '/api/sites/{site}/actions': {
          post: {
            summary: '执行指定网站适配器操作',
            parameters: [{ name: 'site', in: 'path', required: true, schema: { type: 'string', enum: ['chatgpt', 'xiaohongshu', 'douyin', 'bilibili'] } }],
            requestBody: { required: true, content: { 'application/json': { schema: { type: 'object', required: ['action'], properties: { action: { type: 'string' }, sessionId: { type: 'string' }, query: { type: 'string' }, text: { type: 'string' }, index: { type: 'integer', minimum: 0, maximum: 29 }, confirm: { type: 'boolean' } } } } } },
            responses: { '202': { description: '网站操作已排队' }, '400': { description: '参数无效' }, '409': { description: '需要确认或会话状态冲突' } }
          }
        },        '/api/sessions': {
          get: { summary: '列出浏览器会话', responses: { '200': { description: '会话列表' } } },
          post: { summary: '创建浏览器会话', responses: { '202': { description: '创建任务已排队' } } }
        },
        '/api/sessions/{id}': {
          get: { summary: '读取会话状态', responses: { '200': { description: '会话状态' } } }
        },
        '/api/sessions/{id}/navigate': { post: { summary: '导航页面', responses: { '202': { description: '任务已排队' } } } },
        '/api/sessions/{id}/snapshot': { post: { summary: '读取页面快照', responses: { '202': { description: '任务已排队' } } } },
        '/api/sessions/{id}/actions': { post: { summary: '执行白名单页面操作', responses: { '202': { description: '任务已排队' } } } },
        '/api/sessions/{id}/close': { post: { summary: '关闭浏览器会话', responses: { '202': { description: '任务已排队' } } } },        '/api/logs': {
          get: { summary: '读取最近服务日志', responses: { '200': { description: '日志' } } }
        }
      },
      security: [{ bearerAuth: [] }],
      components: { securitySchemes: { bearerAuth: { type: 'http', scheme: 'bearer' } } }
    });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/favicon.ico') {
    res.writeHead(204);
    res.end();
    return;
  }
  sendJson(res, 404, { ok: false, error: '未找到接口。', path: url.pathname });
});

server.on('error', error => {
  console.error('server error:', error.message);
  process.exitCode = 1;
});
server.listen(port, host, () => {
  console.log('Ego Browser API listening at http://' + host + ':' + port);
  console.log('Node runtime: ' + process.version);
  console.log('Extension directory: ' + extensionDir);
  console.log('Ego CLI: ' + egoCliPath() + ' (exists=' + existsSync(egoCliPath()) + ')');
});
