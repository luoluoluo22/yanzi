const $ = id => document.getElementById(id);
async function api(path, method = 'GET', body) {
  const response = await fetch(path, { method, headers: { 'Content-Type': 'application/json', 'X-Bridge-Request': '1' }, body: body ? JSON.stringify(body) : undefined });
  const result = await response.json();
  if (!response.ok) throw new Error(result.error || '请求失败');
  return result;
}
$('trigger').onchange = () => {
  for (const value of ['at', 'interval', 'event']) $(value + '-row').hidden = $('trigger').value !== value;
};
$('task').onsubmit = async event => {
  event.preventDefault();
  const button = event.submitter; button.disabled = true;
  try {
    const task = { action: $('action').value, prompt: $('prompt').value, timeoutSeconds: Number($('timeout').value), newChat: !$('tabId').value, tabPolicy: $('tabPolicy').value, closeAfter: $('closeAfter').checked };
    task.temporary = $('temporary').checked;
    if ($('tabId').value) task.tabId = Number($('tabId').value);
    const trigger = $('trigger').value;
    let result;
    if (trigger === 'now') result = await api('/api/jobs', 'POST', task);
    else {
      const plan = { task };
      if (trigger === 'at') plan.at = new Date($('at').value).toISOString();
      if (trigger === 'interval') plan.intervalSeconds = Number($('interval').value);
      if (trigger === 'event') plan.event = $('event').value;
      result = await api('/api/schedules', 'POST', plan);
    }
    $('feedback').textContent = '已保存：' + result.id; await refresh();
  } catch (error) { $('feedback').textContent = error.message; }
  finally { button.disabled = false; }
};
$('emit').onsubmit = async event => {
  event.preventDefault();
  try { const result = await api('/api/events', 'POST', { name: $('event-name').value }); $('feedback').textContent = `已触发 ${result.jobs.length} 个任务`; await refresh(); }
  catch (error) { $('feedback').textContent = error.message; }
};
async function refresh() {
  try {
    const [health, jobs, schedules] = await Promise.all([api('/health'), api('/api/jobs'), api('/api/schedules')]);
    $('connection').textContent = health.connected ? `浏览器扩展已连接 · ${health.extensionVersion || ''}` : '浏览器扩展未连接：请在扩展管理页重新加载“燕子浏览器助手”0.2.2';
    $('jobs').replaceChildren(...jobs.slice(0, 50).map(job => {
      const article = document.createElement('article'), title = document.createElement('strong'), pre = document.createElement('pre');
      title.textContent = `${job.status} · ${job.task.action} · ${new Date(job.createdAt).toLocaleString()}`;
      pre.textContent = JSON.stringify({ id: job.id, data: job.data, message: job.message }, null, 2);
      article.append(title, pre); return article;
    }));
    $('schedules').replaceChildren(...schedules.map(plan => {
      const article = document.createElement('article'), title = document.createElement('p'), button = document.createElement('button');
      title.textContent = `${plan.event || (plan.intervalSeconds ? '每 ' + plan.intervalSeconds + ' 秒' : new Date(plan.nextAt).toLocaleString())} · ${plan.task.action}${plan.lastError ? ' · ' + plan.lastError : ''}`;
      button.textContent = plan.enabled ? '暂停计划' : '启用计划';
      button.onclick = async () => { try { await api('/api/schedules/' + plan.id, 'PATCH', { enabled: !plan.enabled }); await refresh(); } catch (error) { $('feedback').textContent = error.message; } };
      article.append(title, button); return article;
    }));
  } catch (error) { $('connection').textContent = '后台服务不可用：' + error.message; }
}
refresh(); setInterval(refresh, 3000);
