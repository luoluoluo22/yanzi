(function () {
  'use strict';
  var token = '';
  var activeSessionId = null;
  var sessionBusy = false;
  var currentSiteAdapters = [];
  var activeSiteId = 'chatgpt';
  var siteSessionId = null;
  var siteBusy = false;
  var siteCatalogSignature = '';
  var refreshInProgress = false;
  var toastTimer = null;
  function byId(id) { return document.getElementById(id); }
  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (ch) {
      return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch];
    });
  }
  function formatTime(value) {
    if (!value) return '—';
    try { return new Date(value).toLocaleString(); } catch { return String(value); }
  }
  function toast(message, isError) {
    var el = byId('toast');
    el.textContent = message;
    el.className = 'toast show' + (isError ? ' error' : '');
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { el.className = 'toast'; }, 3500);
  }
  async function fetchJson(path, options) {
    options = options || {};
    var headers = Object.assign({}, options.headers || {});
    if (token) headers.Authorization = 'Bearer ' + token;
    if (options.body) headers['Content-Type'] = 'application/json';
    var response = await fetch(path, Object.assign({}, options, { headers: headers, cache: 'no-store' }));
    var data = await response.json().catch(function () { return {}; });
    if (!response.ok) throw new Error(data.error || data.message || ('HTTP ' + response.status));
    return data;
  }
  async function loadToken() {
    var response = await fetch('/api/ui-config', { cache: 'no-store' });
    var data = await response.json();
    if (!response.ok || !data.token) throw new Error('无法初始化本机 API 授权。');
    token = data.token;
  }
  function setPill(id, text, state) {
    var el = byId(id);
    el.textContent = text;
    el.className = 'mini-status' + (state ? ' ' + state : '');
  }
  function setTrack(id, value) {
    byId(id).style.width = Math.max(0, Math.min(100, value)) + '%';
  }
  function renderHealth(health) {
    var online = !!(health && health.ok && health.service === 'ego-browser-api');
    byId('api-state').textContent = online ? '在线' : '离线';
    byId('api-foot').textContent = online ? '127.0.0.1:' + health.port : '等待本机服务响应';
    setPill('api-state-pill', online ? '正常' : '不可用', online ? 'good' : 'bad');
    setTrack('api-track', online ? 100 : 0);
    byId('sidebar-dot').className = 'status-dot ' + (online ? 'online' : 'offline');
    byId('sidebar-status-text').textContent = online ? 'API 服务运行中' : 'API 服务不可用';
    byId('last-checked').textContent = '最近检测：' + new Date().toLocaleTimeString();
    return online;
  }
  function renderEnvironment(env) {
    byId('node-state').textContent = env.nodeVersion || '--';
    byId('node-foot').textContent = env.nodeExecutable || '未读取到 Node 路径';
    setPill('node-state-pill', env.nodeVersion ? '正常' : '异常', env.nodeVersion ? 'good' : 'bad');
    setTrack('node-track', env.nodeVersion ? 100 : 0);
    byId('ego-state').textContent = env.egoCliExists ? '已就绪' : '未找到';
    byId('ego-foot').textContent = env.egoCliExists ? '命令文件存在' : '检查 Ego 安装目录';
    setPill('ego-state-pill', env.egoCliExists ? '正常' : '缺失', env.egoCliExists ? 'good' : 'bad');
    setTrack('ego-track', env.egoCliExists ? 100 : 0);
    byId('env-node-path').textContent = env.nodeExecutable || '—';
    byId('env-node-version').textContent = env.nodeVersion || '—';
    byId('env-node-version').className = 'env-value ' + (env.nodeVersion ? 'good' : 'bad');
    byId('env-ego-path').textContent = env.egoCliPath || '—';
    byId('env-ego-exists').textContent = env.egoCliExists ? '可用' : '缺失';
    byId('env-ego-exists').className = 'env-value ' + (env.egoCliExists ? 'good' : 'bad');
    byId('env-data-path').textContent = env.dataDir || '—';
  }
  function statusText(status) {
    var map = { queued: '排队中', running: '执行中', success: '成功', error: '失败', timeout: '超时', interrupted: '中断' };
    return map[status] || status || '未知';
  }
  function renderJobs(data) {
    var jobs = data.jobs || [];
    var summary = data.summary || { queued: 0, running: 0, completed: 0 };
    byId('queue-state').innerHTML = String(summary.running || 0) + ' <small>进行中</small>';
    byId('queue-foot').textContent = '等待 ' + (summary.queued || 0) + ' · 已完成 ' + (summary.completed || 0);
    setPill('queue-state-pill', (summary.running || 0) > 0 ? '执行中' : '实时', (summary.running || 0) > 0 ? 'good' : '');
    setTrack('queue-track', Math.min(100, (summary.running || 0) * 35 + (summary.queued || 0) * 10));
    byId('jobs-count').textContent = jobs.length + ' 条记录';
    if (!jobs.length) {
      byId('jobs-list').innerHTML = '<div class="empty-state"><div class="empty-icon">≋</div><strong>还没有执行记录</strong><span>点击“运行 Ego 实测”创建第一条真实任务。</span></div>';
      return;
    }
    byId('jobs-list').innerHTML = jobs.map(function (job) {
      var dataText = '';
      if (job.data) {
        dataText = JSON.stringify({
          title: job.data.title,
          heading: job.data.heading,
          url: job.data.url,
          readyState: job.data.readyState,
          temporaryEvidence: job.data.temporaryEvidence,
          loginNeeded: job.data.loginNeeded,
          editorPresent: job.data.editorPresent,
          site: job.data.site,
          action: job.data.action,
          selector: job.data.selector,
          spaceId: job.data.spaceId,
          pages: job.data.pages,
          count: job.data.count,
          results: job.data.results,
          response: job.data.response,
          submitted: job.data.submitted,
          filled: job.data.filled,
          textLength: job.data.textLength,
          navigationError: job.data.navigationError,
          elapsedSeconds: job.data.elapsedSeconds
        }, null, 2);
        if (dataText.length > 5000) dataText = dataText.slice(0, 5000) + '\n…（结果过长，已截断）';
      }
      return '<div class="job-item"><div class="job-top"><span class="job-title">' + escapeHtml(({ ego_smoke_test: 'Ego 页面自检', session_create: '创建会话', session_navigate: '页面导航', session_snapshot: '读取页面', session_action: '页面操作', site_action: '网站操作', session_close: '关闭会话' })[job.operation] || job.operation || '浏览器任务') + ' · ' + escapeHtml(String(job.id || '').slice(0, 8)) + '</span><span class="job-status ' + escapeHtml(job.status) + '">' + escapeHtml(statusText(job.status)) + '</span></div><p class="job-message">' + escapeHtml(job.message || '') + '</p><div class="job-meta"><span>' + escapeHtml(formatTime(job.createdAt)) + '</span><span>' + escapeHtml(job.operation || '') + '</span></div>' + (dataText ? '<pre class="job-result">' + escapeHtml(dataText) + '</pre>' : '') + '</div>';
    }).join('');
  }
  var currentSessions = [];
  function sessionStatusText(status) {
    var labels = { creating: '创建中', active: '可操作', error: '错误', closing: '关闭中', closed: '已关闭', interrupted: '待恢复' };
    return labels[status] || status || '未知';
  }
  function getSelectedSession() {
    return currentSessions.find(function (session) { return session.id === activeSessionId; }) || null;
  }
  function updateSessionControls() {
    var session = getSelectedSession();
    var usable = !!session && ['active', 'interrupted'].includes(session.status) && !sessionBusy;
    ['session-navigate-button', 'session-snapshot-button', 'session-action-run'].forEach(function (id) {
      byId(id).disabled = !usable;
    });
    byId('session-close-button').disabled = !usable;
    byId('create-session-button').disabled = sessionBusy;
    byId('session-selected-meta').textContent = session
      ? sessionStatusText(session.status) + ' · ' + (session.latestUrl || 'about:blank') + (session.lastError ? ' · ' + session.lastError : '')
      : '先创建会话，再选择会话操作。';
    if (!session && document.activeElement !== byId('session-navigation-url')) {
      byId('session-navigation-url').value = '';
      byId('session-navigation-url').dataset.sessionId = '';
    }
    if (session && document.activeElement !== byId('session-navigation-url')) {
      if (byId('session-navigation-url').dataset.sessionId !== session.id) {
        byId('session-navigation-url').value = session.latestUrl || '';
        byId('session-navigation-url').dataset.sessionId = session.id;
      }
    }
    byId('session-action-selector').disabled = !usable || byId('session-action-kind').value === 'reload';
    byId('session-action-value').disabled = !usable || byId('session-action-kind').value === 'reload' || byId('session-action-kind').value === 'wait_for_selector';
  }
  function renderSessions(data) {
    currentSessions = data.sessions || [];
    byId('session-count').textContent = currentSessions.length + ' 个会话';
    var picker = byId('session-picker');
    var found = currentSessions.some(function (session) { return session.id === activeSessionId; });
    if (!found) {
      var preferred = currentSessions.find(function (session) { return session.status === 'active'; }) ||
        currentSessions.find(function (session) { return session.status === 'creating' || session.status === 'interrupted'; });
      activeSessionId = preferred ? preferred.id : null;
    }
    picker.innerHTML = '<option value="">请选择会话</option>' + currentSessions.map(function (session) {
      var label = session.latestTitle || session.latestUrl || session.name;
      return '<option value="' + escapeHtml(session.id) + '">' + escapeHtml(label) + ' · ' + escapeHtml(sessionStatusText(session.status)) + '</option>';
    }).join('');
    picker.value = activeSessionId || '';
    updateSessionControls();
    renderSiteSessionPicker();
    updateSiteControls();
  }
  function showSessionResult(job) {
    var data = job && job.data ? job.data : {};
    var output = data.snapshot || JSON.stringify(data, null, 2);
    byId('session-snapshot-content').textContent = output || (job && job.message) || '操作完成，但没有返回页面内容。';
    var resultMeta = byId('session-result-meta');
    if (resultMeta) resultMeta.textContent = (job.operation || '任务') + ' · ' + sessionStatusText(job.status) + ' · ' + (data.elapsedSeconds || '—') + ' 秒';
  }
  async function waitForJob(id, timeoutMs) {
    var start = Date.now();
    while (Date.now() - start < (timeoutMs || 65000)) {
      var response = await fetchJson('/api/jobs/' + encodeURIComponent(id));
      if (response.job && ['success', 'error', 'timeout', 'interrupted'].includes(response.job.status)) return response.job;
      await new Promise(function (resolve) { setTimeout(resolve, 1200); });
    }
    throw new Error('任务仍未结束。请在异步任务列表中查看状态，不要重复提交。');
  }
  async function runSessionRequest(path, body, successLabel) {
    if (sessionBusy) return;
    sessionBusy = true;
    updateSessionControls();
    try {
      var response = await fetchJson(path, { method: 'POST', body: JSON.stringify(body || {}) });
      if (response.session) activeSessionId = response.session.id;
      toast('任务已提交，正在等待浏览器结果…');
      var job = await waitForJob(response.job.id, 70000);
      showSessionResult(job);
      await refreshAll();
      if (job.status === 'success') {
        toast(successLabel || job.message || '操作完成');
        if (job.data && job.data.navigationError) toast('会话已创建，但首次导航失败：' + job.data.navigationError, true);
      } else {
        toast('操作失败：' + (job.message || job.status), true);
      }
    } catch (error) {
      toast('浏览器操作失败：' + error.message, true);
    } finally {
      sessionBusy = false;
      updateSessionControls();
      await refreshAll();
    }
  }
  async function createSession() {
    if (sessionBusy) return;
    var url = byId('session-create-url').value.trim();
    sessionBusy = true;
    updateSessionControls();
    try {
      var response = await fetchJson('/api/sessions', {
        method: 'POST',
        body: JSON.stringify(url ? { url: url } : {})
      });
      activeSessionId = response.session.id;
      toast('会话创建任务已提交…');
      var job = await waitForJob(response.job.id, 70000);
      showSessionResult(job);
      await refreshAll();
      if (job.status === 'success') {
        byId('session-create-url').value = '';
        toast(job.data && job.data.navigationError ? '会话已创建，但导航失败：' + job.data.navigationError : '浏览器会话已创建');
      } else {
        toast('创建会话失败：' + job.message, true);
      }
    } catch (error) {
      toast('创建会话失败：' + error.message, true);
    } finally {
      sessionBusy = false;
      updateSessionControls();
      await refreshAll();
    }
  }
  function submitSessionNavigate() {
    if (!activeSessionId) return toast('请先选择一个会话。', true);
    var url = byId('session-navigation-url').value.trim();
    if (!url) return toast('请输入要访问的网址。', true);
    return runSessionRequest('/api/sessions/' + encodeURIComponent(activeSessionId) + '/navigate', { url: url }, '页面导航完成');
  }
  function submitSessionSnapshot() {
    if (!activeSessionId) return toast('请先选择一个会话。', true);
    return runSessionRequest('/api/sessions/' + encodeURIComponent(activeSessionId) + '/snapshot', { scope: 'only_within_viewport' }, '页面快照已读取');
  }
  function submitSessionClose() {
    if (!activeSessionId) return toast('请先选择一个会话。', true);
    return runSessionRequest('/api/sessions/' + encodeURIComponent(activeSessionId) + '/close', {}, '会话已关闭');
  }
  function submitSessionAction() {
    if (!activeSessionId) return toast('请先选择一个会话。', true);
    var action = byId('session-action-kind').value;
    var selector = byId('session-action-selector').value.trim();
    var value = byId('session-action-value').value;
    var body = { action: action };
    if (action !== 'reload') body.selector = selector;
    if (action === 'fill' || action === 'select') body.value = value;
    if (action === 'press') body.key = value;
    if (action === 'wait_for_selector') body.state = 'visible';
    return runSessionRequest('/api/sessions/' + encodeURIComponent(activeSessionId) + '/actions', body, '页面操作完成');
  }
  function getActiveSiteAdapter() {
    return currentSiteAdapters.find(function (site) { return site.id === activeSiteId; }) || null;
  }
  function getSiteAction(site, actionId) {
    return site && site.actions ? site.actions.find(function (action) { return action.id === actionId; }) || null : null;
  }
  function renderSiteCatalog(data) {
    var sites = data && Array.isArray(data.sites) ? data.sites : [];
    var signature = JSON.stringify(sites);
    currentSiteAdapters = sites;
    byId('site-adapter-count').textContent = sites.length + ' 个网站适配器';
    if (!sites.some(function (site) { return site.id === activeSiteId; })) {
      activeSiteId = sites.length ? sites[0].id : '';
    }
    if (signature !== siteCatalogSignature) {
      siteCatalogSignature = signature;
      byId('site-tabs').innerHTML = sites.map(function (site) {
        return '<button class="site-tab' + (site.id === activeSiteId ? ' active' : '') + '" type="button" role="tab" data-site="' + escapeHtml(site.id) + '" aria-selected="' + (site.id === activeSiteId ? 'true' : 'false') + '">' + escapeHtml(site.icon || '◎') + ' ' + escapeHtml(site.name) + '</button>';
      }).join('');
      renderSitePanel();
    } else {
      updateSiteControls();
    }
  }
  function renderSitePanel() {
    var site = getActiveSiteAdapter();
    if (!site) {
      byId('site-name').textContent = '网站适配器未加载';
      byId('site-description').textContent = '请检查本机 API 服务与 /api/sites 接口。';
      byId('site-actions').innerHTML = '<span class="site-empty">没有可用的网站适配器。</span>';
      return;
    }
    byId('site-tabs').querySelectorAll('[data-site]').forEach(function (button) {
      var active = button.dataset.site === activeSiteId;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    byId('site-icon').textContent = site.icon || '◎';
    byId('site-name').textContent = site.name;
    byId('site-category').textContent = site.category || '网站';
    byId('site-description').textContent = site.description || '';
    byId('site-query-wrap').classList.toggle('hidden', !site.actions.some(function (action) { return action.input === 'query'; }));
    byId('site-text-wrap').classList.toggle('hidden', !site.actions.some(function (action) { return action.input === 'text'; }));
    byId('site-index-wrap').classList.toggle('hidden', !site.actions.some(function (action) { return action.input === 'index'; }));
    byId('site-query-input').placeholder = site.id === 'xiaohongshu' ? '例如：家庭教育、亲子沟通' : site.id === 'douyin' ? '例如：AI 自动剪辑教程' : '例如：AI 视频剪辑';
    byId('site-text-input').placeholder = '输入要填入 ChatGPT 的提示词';
    byId('site-actions').innerHTML = site.actions.map(function (action) {
      var kind = action.requiresConfirm ? ' confirm' : (['open', 'new_chat', 'search'].includes(action.id) ? ' primary' : '');
      return '<button class="site-action-button' + kind + '" type="button" data-site-action="' + escapeHtml(action.id) + '" title="' + escapeHtml(action.description || action.label) + '">' + escapeHtml(action.label) + '</button>';
    }).join('');
    renderSiteSessionPicker();
    updateSiteControls();
  }
  function renderSiteSessionPicker() {
    var picker = byId('site-session-picker');
    if (!picker) return;
    var eligible = currentSessions.filter(function (session) {
      return session.site === activeSiteId && ['active', 'creating', 'interrupted', 'closing'].includes(session.status);
    });
    if (!eligible.some(function (session) { return session.id === siteSessionId; })) {
      var preferred = eligible.find(function (session) { return session.status === 'active'; }) ||
        eligible.find(function (session) { return session.status === 'interrupted'; }) ||
        eligible.find(function (session) { return session.status === 'creating'; });
      siteSessionId = preferred ? preferred.id : null;
    }
    var markup = '<option value="">自动创建新会话</option>' + eligible.map(function (session) {
      var label = session.latestTitle || session.latestUrl || session.name;
      return '<option value="' + escapeHtml(session.id) + '">' + escapeHtml(label) + ' · ' + escapeHtml(sessionStatusText(session.status)) + '</option>';
    }).join('');
    if (picker.dataset.optionMarkup !== markup) {
      picker.innerHTML = markup;
      picker.dataset.optionMarkup = markup;
    }
    picker.value = siteSessionId || '';
    var selected = eligible.find(function (session) { return session.id === siteSessionId; });
    byId('site-session-note').textContent = selected
      ? sessionStatusText(selected.status) + ' · ' + (selected.latestUrl || '页面地址待读取')
      : '打开网站或搜索时会自动创建独立会话。';
  }
  function updateSiteControls() {
    var site = getActiveSiteAdapter();
    var session = currentSessions.find(function (item) { return item.id === siteSessionId; }) || null;
    var needsSession = ['fill_prompt', 'send_prompt', 'read_response', 'read_page', 'extract_results', 'extract_details', 'open_result', 'reload', 'close_session'];
    var sessionReady = !!session && ['active', 'interrupted'].includes(session.status);
    byId('site-actions').querySelectorAll('[data-site-action]').forEach(function (button) {
      var action = getSiteAction(site, button.dataset.siteAction);
      button.disabled = siteBusy || (!!action && needsSession.includes(action.id) && !sessionReady) ||
        (!!action && session && ['creating', 'closing'].includes(session.status) && needsSession.includes(action.id));
    });
    byId('site-session-picker').disabled = siteBusy;
    byId('site-query-input').disabled = siteBusy;
    byId('site-text-input').disabled = siteBusy;
    byId('site-index-input').disabled = siteBusy;
  }
  function showSiteResult(job, site, action) {
    var data = job && job.data ? job.data : {};
    var output;
    if (Array.isArray(data.results)) {
      output = JSON.stringify({ site: data.site, action: data.action, url: data.url, title: data.title, count: data.count, results: data.results }, null, 2);
    } else if (typeof data.response === 'string') {
      output = data.response || '(当前页面还没有检测到助手回答。)';
    } else if (typeof data.text === 'string') {
      output = data.text + (data.snapshot ? '\n\n--- 页面语义快照 ---\n' + data.snapshot : '');
    } else {
      output = JSON.stringify(data, null, 2);
    }
    byId('site-result-content').textContent = String(output || (job && job.message) || '操作完成，但没有返回内容。').slice(0, 30000);
    byId('site-result-meta').textContent = (site ? site.name : '网站') + ' · ' + (action ? action.label : (job.operation || '操作')) + ' · ' + sessionStatusText(job.status);
  }
  async function runSiteAction(actionId) {
    if (siteBusy) return;
    var site = getActiveSiteAdapter();
    var action = getSiteAction(site, actionId);
    if (!site || !action) return toast('没有找到对应的网站操作。', true);
    var body = { action: actionId };
    if (siteSessionId) body.sessionId = siteSessionId;
    if (action.input === 'query') {
      body.query = byId('site-query-input').value.trim();
      if (!body.query) return toast('请先输入搜索关键词。', true);
    }
    if (action.input === 'text') {
      body.text = byId('site-text-input').value;
      if (!body.text.trim()) return toast('请先输入提示词。', true);
    }
    if (action.input === 'index') {
      body.index = Number(byId('site-index-input').value || 0);
      if (!Number.isInteger(body.index) || body.index < 0 || body.index > 29) return toast('结果序号必须为 0–29 的整数。', true);
    }
    if (action.requiresConfirm) {
      var approved = window.confirm('即将执行“' + action.label + '”。这会向 ' + site.name + ' 发送你输入的内容，是否继续？');
      if (!approved) return;
      body.confirm = true;
    }
    siteBusy = true;
    updateSiteControls();
    try {
      var response = await fetchJson('/api/sites/' + encodeURIComponent(site.id) + '/actions', {
        method: 'POST',
        body: JSON.stringify(body)
      });
      if (response.session) siteSessionId = response.session.id;
      renderSiteSessionPicker();
      toast('已提交 ' + site.name + ' · ' + action.label + '，等待浏览器执行…');
      var job = await waitForJob(response.job.id, 75000);
      showSiteResult(job, site, action);
      await refreshAll();
      if (job.status === 'success' && job.data && job.data.action === 'extract_details' && job.data.contentAccessible === false) toast(site.name + ' · 页面内容未能完整读取：' + ((job.data.warnings || []).join(' ') || '请查看缺失字段。'), true);
      else if (job.status === 'success' && job.data && job.data.action === 'read_response' && job.data.hasResponse === false) toast('当前页面没有检测到助手回答，请确认 ChatGPT 已完成回答生成。', true);
      else if (job.status === 'success') toast(site.name + ' · ' + action.label + ' 完成');
      else toast(site.name + ' 操作失败：' + (job.message || job.status), true);
    } catch (error) {
      toast(site.name + ' 操作失败：' + error.message, true);
      byId('site-result-content').textContent = error.message;
      byId('site-result-meta').textContent = site.name + ' · ' + action.label + ' · 请求失败';
    } finally {
      siteBusy = false;
      updateSiteControls();
      await refreshAll();
    }
  }
  async function refreshAll() {
    if (refreshInProgress) return;
    refreshInProgress = true;
    try {
      var healthResponse = await fetch('/health', { cache: 'no-store' });
      var health = await healthResponse.json();
      var online = renderHealth(health);
      if (!online) throw new Error('本机 API 未处于预期服务状态。');
      var results = await Promise.all([
        fetchJson('/api/environment'),
        fetchJson('/api/jobs?limit=30'),
        fetchJson('/api/sessions'),
        fetchJson('/api/sites')
      ]);
      renderEnvironment(results[0]);
      renderJobs(results[1]);
      renderSiteCatalog(results[3]);
      renderSessions(results[2]);
    } catch (error) {
      byId('api-state').textContent = '不可用';
      setPill('api-state-pill', '检查失败', 'bad');
      byId('sidebar-dot').className = 'status-dot offline';
      byId('sidebar-status-text').textContent = '服务连接失败';
      if (!token) byId('api-foot').textContent = '初始化授权失败';
      console.warn('refresh failed:', error);
    } finally {
      refreshInProgress = false;
    }
  }
  async function runTest() {
    var button = byId('run-test-button');
    button.disabled = true;
    byId('run-test-label').textContent = '提交中…';
    try {
      var result = await fetchJson('/api/jobs', { method: 'POST', body: JSON.stringify({ operation: 'ego_smoke_test' }) });
      toast('任务已提交：' + String(result.id).slice(0, 8));
      await refreshAll();
      var id = result.id;
      var start = Date.now();
      while (Date.now() - start < 65000) {
        await new Promise(function (resolve) { setTimeout(resolve, 1600); });
        var item = await fetchJson('/api/jobs/' + encodeURIComponent(id));
        if (item.job) {
          renderJobs(await fetchJson('/api/jobs?limit=30'));
          if (['success', 'error', 'timeout', 'interrupted'].includes(item.job.status)) {
            if (item.job.status === 'success') toast('Ego 实测成功：' + (item.job.data && item.job.data.title ? item.job.data.title : '已取得页面结果'));
            else toast('Ego 实测结束：' + (item.job.message || item.job.status), true);
            break;
          }
        }
      }
    } catch (error) {
      toast('无法执行任务：' + error.message, true);
    } finally {
      button.disabled = false;
      byId('run-test-label').textContent = '运行 Ego 实测';
      await refreshAll();
    }
  }
  async function showLogs() {
    try {
      var data = await fetchJson('/api/logs');
      var lines = ['--- stdout ---'].concat(data.stdout || [], ['', '--- stderr ---'], data.stderr || []);
      byId('logs-content').textContent = lines.join('\n') || '暂无日志';
      byId('logs-box').classList.remove('hidden');
    } catch (error) { toast('读取日志失败：' + error.message, true); }
  }
  async function init() {
    try {
      await loadToken();
      await refreshAll();
      setInterval(refreshAll, 5000);
    } catch (error) {
      byId('api-state').textContent = '连接失败';
      byId('api-foot').textContent = error.message;
      setPill('api-state-pill', '不可用', 'bad');
      byId('sidebar-dot').className = 'status-dot offline';
      byId('sidebar-status-text').textContent = '无法连接服务';
      toast(error.message, true);
    }
  }
  byId('site-tabs').addEventListener('click', function (event) {
    var button = event.target.closest('[data-site]');
    if (!button) return;
    activeSiteId = button.dataset.site;
    siteSessionId = null;
    renderSitePanel();
    byId('site-result-meta').textContent = '已切换到 ' + (getActiveSiteAdapter()?.name || activeSiteId);
    byId('site-result-content').textContent = '选择一个常用操作开始执行。已有网站会话会自动显示在会话列表中。';
  });
  byId('site-actions').addEventListener('click', function (event) {
    var button = event.target.closest('[data-site-action]');
    if (button && !button.disabled) runSiteAction(button.dataset.siteAction);
  });
  byId('site-session-picker').addEventListener('change', function () {
    siteSessionId = byId('site-session-picker').value || null;
    updateSiteControls();
    renderSiteSessionPicker();
  });
  byId('create-session-button').addEventListener('click', createSession);
  byId('session-picker').addEventListener('change', function () {
    activeSessionId = byId('session-picker').value || null;
    byId('session-snapshot-content').textContent = '点击“读取页面”获取当前会话的语义快照。';
    updateSessionControls();
  });
  byId('session-navigate-button').addEventListener('click', submitSessionNavigate);
  byId('session-snapshot-button').addEventListener('click', submitSessionSnapshot);
  byId('session-close-button').addEventListener('click', submitSessionClose);
  byId('session-action-run').addEventListener('click', submitSessionAction);
  byId('session-action-kind').addEventListener('change', updateSessionControls);
  byId('run-test-button').addEventListener('click', runTest);
  byId('refresh-button').addEventListener('click', function () { refreshAll().then(function () { toast('状态已刷新'); }); });
  byId('refresh-env-button').addEventListener('click', function () { refreshAll().then(function () { toast('环境检查完成'); }); });
  byId('logs-button').addEventListener('click', showLogs);
  byId('close-logs-button').addEventListener('click', function () { byId('logs-box').classList.add('hidden'); });
  init();
})();