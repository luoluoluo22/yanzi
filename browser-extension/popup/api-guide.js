(() => {
  const base = 'http://127.0.0.1:53921';
  const tokenPath = '%LOCALAPPDATA%\\OpenQuickHost\\ExtensionStorage\\chatgpt-bridge\\api-token.txt';
  const task = { action: 'chatgpt_send', prompt: '请用 JSON 代码块回复：{"ok":true}', temporary: true, tabPolicy: 'reuse', closeAfter: true, timeoutSeconds: 180 };
  const samples = {
    send: { name: '发送并接收回复', method: 'POST', path: '/api/jobs', body: task },
    continue: { name: '继续指定聊天', method: 'POST', path: '/api/jobs', body: { ...task, tabId: 123, closeAfter: false }, note: '把 123 换成之前返回的 tabId；该页面必须仍打开且处于临时模式。' },
    status: { name: '查看 ChatGPT 标签页', method: 'POST', path: '/api/jobs', body: { action: 'chatgpt_status' } },
    read: { name: '读取指定聊天消息', method: 'POST', path: '/api/jobs', body: { action: 'chatgpt_messages', tabId: 123 }, note: '把 123 换成要读取的 tabId。' },
    new: { name: '开始新的临时聊天', method: 'POST', path: '/api/jobs', body: { action: 'chatgpt_new_chat', temporary: true, tabPolicy: 'reuse' } },
    cleanup: { name: '清理空闲工作台标签页', method: 'POST', path: '/api/jobs', body: { action: 'chatgpt_cleanup' } },
    close: { name: '关闭指定工作台标签页', method: 'POST', path: '/api/jobs', body: { action: 'chatgpt_close', tabId: 123 } },
    result: { name: '查询任务结果', method: 'GET', path: '/api/jobs/{jobId}', note: '把 {jobId} 换成提交任务返回的 id。' },
    interval: { name: '每小时自动调用', method: 'POST', path: '/api/schedules', body: { intervalSeconds: 3600, task }, note: '保存计划会实际周期执行；通过 PATCH /api/schedules/{id} 暂停。' },
    plan: { name: '注册事件触发计划', method: 'POST', path: '/api/schedules', body: { event: 'file.changed', task }, note: '先注册计划，再通过触发事件接口调用。' },
    event: { name: '触发已注册事件', method: 'POST', path: '/api/events', body: { name: 'file.changed' }, note: '返回 jobs 列表；使用每个任务的 id 查询结果。事件数据不会拼入提示词。' },
    pause: { name: '暂停计划', method: 'PATCH', path: '/api/schedules/{id}', body: { enabled: false }, note: '把 {id} 换成计划 id。' }
  };
  const endpoints = [
    ['GET', '/health', '服务和扩展连接状态（无需鉴权）'],
    ['POST', '/api/jobs', '提交任务，202 返回 id，异步执行'],
    ['GET', '/api/jobs/{id}', '查询 status、data 和 message'],
    ['GET', '/api/jobs', '任务与结果列表'],
    ['POST / GET', '/api/schedules', '创建 / 列出计划'],
    ['PATCH', '/api/schedules/{id}', 'enabled:false 暂停，true 启用'],
    ['POST', '/api/events', '触发同名计划，返回 jobs']
  ];
  function example(key, language) {
    const s = samples[key], json = s.body ? JSON.stringify(s.body, null, 2) : '';
    const polls = s.path === '/api/jobs' && s.method === 'POST';
    if (language === 'http') return `${s.method} ${base}${s.path}\nAuthorization: Bearer <从令牌文件读取>\n${s.method === 'GET' ? '' : 'X-Bridge-Request: 1\nContent-Type: application/json\n'}${json ? '\n' + json : ''}`;
    if (language === 'powershell') return String.raw`[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$base = '${base}'
$tokenFile = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$headers = @{ Authorization = 'Bearer ' + (Get-Content $tokenFile -Raw).Trim(); 'X-Bridge-Request' = '1' }
${json ? "$body = @'\n" + json + "\n'@\n$bytes = [System.Text.Encoding]::UTF8.GetBytes($body)\n" : ''}$result = Invoke-RestMethod "$base${s.path}" -Method ${s.method} -Headers $headers${json ? " -ContentType 'application/json; charset=utf-8' -Body $bytes" : ''}
${polls ? String.raw`$deadline = (Get-Date).AddSeconds(270)
do {
    if ((Get-Date) -gt $deadline) { throw '查询超时；请检查原任务，不要自动重新提交' }
    Start-Sleep -Seconds 1
    $job = Invoke-RestMethod "$base/api/jobs/$($result.id)" -Headers $headers
} while ($job.status -in @('queued', 'running'))
if ($job.status -ne 'success') { throw ($job | ConvertTo-Json -Depth 20) }
$job.data | ConvertTo-Json -Depth 30` : '$result | ConvertTo-Json -Depth 30'}`;
    if (language === 'python') return `import json, os, time
from pathlib import Path
from urllib.request import Request, urlopen

base = '${base}'
token = (Path(os.environ['LOCALAPPDATA']) / 'OpenQuickHost/ExtensionStorage/chatgpt-bridge/api-token.txt').read_text(encoding='utf-8').strip()
def api(path, method='GET', body=None):
    headers = {'Authorization': 'Bearer ' + token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json'}
    payload = None if body is None else json.dumps(body, ensure_ascii=False).encode('utf-8')
    with urlopen(Request(base + path, data=payload, headers=headers, method=method), timeout=30) as response:
        return json.load(response)

${json ? 'body = json.loads(' + JSON.stringify(JSON.stringify(s.body)) + ')\n' : ''}result = api('${s.path}', '${s.method}'${json ? ', body' : ''})
${polls ? `deadline = time.monotonic() + 270
while True:
    if time.monotonic() > deadline:
        raise TimeoutError('查询超时；检查原任务，不要自动重新提交')
    job = api('/api/jobs/' + result['id'])
    if job['status'] not in ('queued', 'running'):
        break
    time.sleep(1)
if job['status'] != 'success':
    raise RuntimeError(json.dumps(job, ensure_ascii=False))
print(json.dumps(job['data'], ensure_ascii=False, indent=2))` : "print(json.dumps(result, ensure_ascii=False, indent=2))"}`;
    return `// Node.js 22+，保存为 .mjs 后运行
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
const base = '${base}';
const token = readFileSync(join(process.env.LOCALAPPDATA, 'OpenQuickHost/ExtensionStorage/chatgpt-bridge/api-token.txt'), 'utf8').trim();
async function api(path, method = 'GET', body) {
  const response = await fetch(base + path, { method,
    headers: { Authorization: 'Bearer ' + token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(30000) });
  const result = await response.json();
  if (!response.ok) throw new Error(JSON.stringify(result));
  return result;
}
const result = await api('${s.path}', '${s.method}'${json ? ', ' + json : ''});
${polls ? `const deadline = Date.now() + 270000;
let job;
while (true) {
  if (Date.now() > deadline) throw new Error('查询超时；检查原任务，不要自动重新提交');
  job = await api('/api/jobs/' + result.id);
  if (!['queued', 'running'].includes(job.status)) break;
  await new Promise(resolve => setTimeout(resolve, 1000));
}
if (job.status !== 'success') throw new Error(JSON.stringify(job));
console.log(JSON.stringify(job.data, null, 2));` : 'console.log(JSON.stringify(result, null, 2));'}`;
  }
  function notes() {
    return `# ChatGPT 网页 API 接入\n\n基础地址：${base}\n仅本机使用，需要浏览器已打开、已登录 ChatGPT，并启动 ChatGPT 后台工作台。\n这是工作台的异步任务接口；不兼容 OpenAI /v1/chat/completions 请求格式。\n\n令牌文件：${tokenPath}\nAuthorization: Bearer <文件内容，去掉首尾空白>\n写入请求另需 X-Bridge-Request: 1 和 Content-Type: application/json\n\n${endpoints.map(row => row.join(' · ')).join('\n')}\n\nPOST /api/jobs 的 id 是任务 id；GET /api/jobs/{id} 查询，queued/running 时继续轮询。success 读取 data；error/timeout/interrupted 时检查 message 与原聊天，不自动重发。\n\n操作：chatgpt_send、chatgpt_new_chat、chatgpt_status、chatgpt_messages、chatgpt_close、chatgpt_cleanup。\n参数：prompt（发送时必填）；tabId（指定已有页面）；temporary（默认 true，正式聊天需 false）；tabPolicy（reuse 默认，或 new）；closeAfter（默认 false，仅成功后关闭空闲工作台页）；timeoutSeconds（10–600，默认 180）；newChat:false（无 tabId 时仅继续唯一页面）；includePageSnapshot（读取时可选网页快照）。\n\n结果：data.text 正文；data.markdown 是 DOM 重建；data.codeBlocks[].text 保留代码原文及缩进；data.json / jsonError 为 JSON 解析结果；data.tabId 和 conversationId 为网页信息；data.temporary / temporaryEvidence 为模式确认；data.lifecycle 为标签页处理结果。chatgpt_messages 返回 data.messages 数组。\n\ncloseAfter:true 后 tabId 不能继续使用。临时页面关闭后不保证恢复；本地保留最近 200 个任务结果。计划使用 at（未来 ISO 时间）、intervalSeconds（至少 60）、event 三选一；事件 payload 不会拼入提示词。\n`;
  }
  async function copy(text, status) {
    try { await navigator.clipboard.writeText(text); status.textContent = '已复制'; }
    catch { status.textContent = '复制失败，请选中文本手动复制'; }
  }
  function init(host) {
    host.innerHTML = `<div class="section-title-row"><h2>ChatGPT API 接入</h2><span class="log-count">异步任务</span></div>
      <p class="guide-help">面向本机小程序 · 默认临时聊天</p>
      <div class="guide-address"><code>${base}</code><button type="button" data-copy="base">复制地址</button></div>
      <p class="guide-help">先提交任务，再用返回的 id 查询结果。需启动 ChatGPT 后台工作台。</p>
      <label>调用场景<select data-scenario>${Object.entries(samples).map(([key, s]) => `<option value="${key}">${s.name}</option>`).join('')}</select></label>
      <label>示例格式<select data-language><option value="http">HTTP + JSON</option><option value="powershell">PowerShell · 完整调用</option><option value="python">Python · 完整调用</option><option value="javascript">JavaScript / Node.js · 完整调用</option></select></label>
      <p class="guide-help" data-note></p><pre class="guide-code" tabindex="0" aria-label="API 调用示例" data-code></pre>
      <div class="guide-actions"><button type="button" data-copy="example">复制调用示例</button><button type="button" data-copy="notes">复制完整接入说明</button></div>
      <p class="copy-status" role="status" data-copy-status></p>
      <details><summary>鉴权与令牌位置</summary><p>读取下列文件内容作为 Bearer 令牌。它是本地工作台令牌。</p><code class="token-path">${tokenPath}</code><button type="button" data-copy="token">复制文件路径</button><pre>Authorization: Bearer &lt;token&gt;\nX-Bridge-Request: 1\nContent-Type: application/json</pre><p>PowerShell、Python、Node.js 示例已包含文件读取。令牌未显示或写入扩展。</p></details>
      <details><summary>接口列表与接入流程</summary><table><thead><tr><th>方法</th><th>路径</th><th>用途</th></tr></thead><tbody>${endpoints.map(row => '<tr>' + row.map(v => `<td>${v}</td>`).join('') + '</tr>').join('')}</tbody></table><p>1. POST /api/jobs → HTTP 202 返回 id。<br>2. GET /api/jobs/{id} → queued / running 时继续查询。<br>3. success 时读取 data；其他结束状态检查 message。</p><p>id 是任务 ID，tabId 是标签页 ID。超时或断线后检查原聊天，避免重复发送。</p><p>这是工作台异步接口，不兼容 OpenAI /v1/chat/completions 格式。</p></details>
      <details><summary>参数、结果与标签页策略</summary><pre class="guide-notes"></pre></details>`;
    host.querySelector('.guide-notes').textContent = notes().split('操作：')[1];
    const scenario = host.querySelector('[data-scenario]'), language = host.querySelector('[data-language]'), status = host.querySelector('[data-copy-status]');
    const update = () => { host.querySelector('[data-code]').textContent = example(scenario.value, language.value); host.querySelector('[data-note]').textContent = samples[scenario.value].note || (language.value === 'http' ? 'HTTP 示例展示请求格式；选择脚本版本可获取包含轮询的完整代码。' : '脚本会读取本地令牌；任务接口包含结果轮询，失败时停止。'); status.textContent = ''; };
    scenario.onchange = language.onchange = update;
    host.querySelectorAll('[data-copy]').forEach(button => { button.onclick = () => copy(({ base, token: tokenPath, notes: notes(), example: example(scenario.value, language.value) })[button.dataset.copy], status); });
    update();
  }
  globalThis.YanziApiGuide = { samples, example, notes, init };
  document.addEventListener('DOMContentLoaded', () => { document.querySelectorAll('[data-api-guide]').forEach(init); });
})();
