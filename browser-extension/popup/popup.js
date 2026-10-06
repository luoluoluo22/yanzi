document.addEventListener("DOMContentLoaded", () => {
  function showPanel(name) {
    document.querySelectorAll('[data-panel]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.panel === name)));
    for (const panel of ['overview', 'market', 'api', 'logs']) document.getElementById('panel-' + panel).hidden = panel !== name;
    if (name === 'market') loadWebAppMarket();
  }
  document.querySelectorAll('[data-panel]').forEach(button => button.onclick = () => showPanel(button.dataset.panel));
  document.getElementById('show-api').onclick = () => showPanel('api');
  document.getElementById('open-workbench').onclick = () => chrome.tabs.create({ url: 'http://127.0.0.1:53921/' });

  const marketList = document.getElementById('market-list');
  const marketCount = document.getElementById('market-count');
  const marketSyncStatus = document.getElementById('market-sync-status');

  async function loadWebAppMarket() {
    try {
      const response = await chrome.runtime.sendMessage({ type: 'yanzi_webapp_market_list' });
      if (!response?.ok) throw new Error(response?.error || '读取失败');
      renderWebAppMarket(response.apps || []);
      await Promise.all([loadXhsRules(), loadDouyinRules()]);
    } catch (error) {
      marketList.innerHTML = `<div class="market-empty">读取失败：${escapeHtml(error?.message || String(error))}</div>`;
    }
  }

  function renderWebAppMarket(apps) {
    marketCount.textContent = `${apps.length} 个`;
    if (!apps.length) {
      marketList.innerHTML = '<div class="market-empty">暂无网页小程序</div>';
      return;
    }

    marketList.innerHTML = apps.map(app => {
      const capabilities = (app.capabilities || []).map(item => `<span>${escapeHtml(item)}</span>`).join('');
      return `<article class="webapp-card">
        <div class="webapp-card-head">
          <div>
            <div class="webapp-title-row"><strong>${escapeHtml(app.name)}</strong><span class="webapp-version">v${escapeHtml(app.version || '')}</span></div>
            <div class="webapp-meta">${escapeHtml(app.category || '网页能力')} · ${escapeHtml(app.id)}</div>
          </div>
          <button type="button" class="webapp-toggle ${app.installed ? 'installed' : ''}" data-webapp-toggle="${escapeHtml(app.id)}" data-installed="${app.installed ? '1' : '0'}">${app.installed ? '已安装' : '安装'}</button>
        </div>
        <p>${escapeHtml(app.description || '')}</p>
        <div class="webapp-capabilities">${capabilities}</div>
      </article>`;
    }).join('');
  }

  const xhsRuleList = document.getElementById('xhs-rule-list');
  const xhsRuleCount = document.getElementById('xhs-rule-count');

  async function loadXhsRules() {
    const response = await chrome.runtime.sendMessage({
      type: 'yanzi_webapp_storage_get',
      appId: 'xiaohongshu.filter',
      key: 'rules',
      fallback: { version: 1, items: [] }
    });
    const items = Array.isArray(response?.value?.items) ? response.value.items : [];
    renderXhsRules(items);
  }

  function renderXhsRules(items) {
    xhsRuleCount.textContent = `${items.length} 条`;
    if (!items.length) {
      xhsRuleList.innerHTML = '<div class="market-empty compact">暂无屏蔽规则</div>';
      return;
    }

    xhsRuleList.innerHTML = items.map(rule => {
      const typeName = rule.type === 'author' ? '作者' : rule.type === 'note' ? '笔记' : '关键词';
      return `<div class="webapp-rule-item">
        <div><strong>${escapeHtml(rule.label || rule.value || '')}</strong><span>${typeName}</span></div>
        <button type="button" data-delete-xhs-rule="${escapeHtml(rule.id || '')}" title="取消屏蔽">×</button>
      </div>`;
    }).join('');
  }

  xhsRuleList.addEventListener('click', async event => {
    const button = event.target.closest('[data-delete-xhs-rule]');
    if (!button) return;
    const current = await chrome.runtime.sendMessage({
      type: 'yanzi_webapp_storage_get',
      appId: 'xiaohongshu.filter',
      key: 'rules',
      fallback: { version: 1, items: [] }
    });
    const items = Array.isArray(current?.value?.items) ? current.value.items : [];
    const nextItems = items.filter(rule => rule.id !== button.dataset.deleteXhsRule);
    await chrome.runtime.sendMessage({
      type: 'yanzi_webapp_storage_set',
      appId: 'xiaohongshu.filter',
      key: 'rules',
      value: { version: 1, items: nextItems }
    });
    renderXhsRules(nextItems);
  });

  const douyinRuleList = document.getElementById('douyin-rule-list');
  const douyinRuleCount = document.getElementById('douyin-rule-count');
  const douyinRuleForm = document.getElementById('douyin-rule-form');
  const douyinRuleInput = document.getElementById('douyin-rule-input');

  const DOUYIN_DEFAULT_RULES = {
    version: 2,
    items: [
      { id: 'default-shopping', type: 'keyword', value: '购物', label: '购物', scopes: ['title', 'tags', 'author', 'commerce'], enabled: true, createdAt: 0 },
      { id: 'default-live', type: 'keyword', value: '直播', label: '直播', scopes: ['title', 'tags', 'author', 'commerce'], enabled: true, createdAt: 0 },
      { id: 'default-ad', type: 'keyword', value: '广告', label: '广告', scopes: ['ad'], enabled: true, createdAt: 0 }
    ]
  };

  async function loadDouyinRules() {
    const response = await chrome.runtime.sendMessage({
      type: 'yanzi_webapp_storage_get',
      appId: 'douyin.filter',
      key: 'rules',
      fallback: DOUYIN_DEFAULT_RULES
    });
    const items = Array.isArray(response?.value?.items)
      ? response.value.items
      : DOUYIN_DEFAULT_RULES.items;
    renderDouyinRules(items);
  }

  function renderDouyinRules(items) {
    douyinRuleCount.textContent = `${items.length} 条`;
    if (!items.length) {
      douyinRuleList.innerHTML = '<div class="market-empty compact">暂无屏蔽关键词</div>';
      return;
    }

    douyinRuleList.innerHTML = items.map(rule => {
      const scopeMap = { title: '标题', tags: '标签', author: '作者', commerce: '商品', ad: '广告标签' };
      const scopeText = (rule.scopes || ['title', 'tags', 'author'])
        .map(scope => scopeMap[scope] || scope)
        .join('·');
      return `<div class="webapp-rule-item">
        <div><strong>${escapeHtml(rule.label || rule.value || '')}</strong><span>${escapeHtml(scopeText)}</span></div>
        <button type="button" data-delete-douyin-rule="${escapeHtml(rule.id || '')}" title="取消屏蔽">×</button>
      </div>`;
    }).join('');
  }

  async function getDouyinRuleItems() {
    const response = await chrome.runtime.sendMessage({
      type: 'yanzi_webapp_storage_get',
      appId: 'douyin.filter',
      key: 'rules',
      fallback: DOUYIN_DEFAULT_RULES
    });
    return Array.isArray(response?.value?.items)
      ? response.value.items
      : DOUYIN_DEFAULT_RULES.items.map(item => ({ ...item }));
  }

  async function saveDouyinRules(items) {
    await chrome.runtime.sendMessage({
      type: 'yanzi_webapp_storage_set',
      appId: 'douyin.filter',
      key: 'rules',
      value: { version: 2, items }
    });
    renderDouyinRules(items);
  }

  douyinRuleForm.addEventListener('submit', async event => {
    event.preventDefault();
    const value = douyinRuleInput.value.replace(/\s+/g, ' ').trim();
    if (!value) return;

    const items = await getDouyinRuleItems();
    const duplicate = items.some(rule =>
      String(rule.value || '').trim().toLowerCase() === value.toLowerCase()
    );
    if (!duplicate) {
      items.push({
        id: crypto.randomUUID(),
        type: 'keyword',
        value,
        label: value,
        scopes: ['title', 'tags', 'author', 'commerce'],
        enabled: true,
        createdAt: Date.now()
      });
      await saveDouyinRules(items);
    }
    douyinRuleInput.value = '';
  });

  douyinRuleList.addEventListener('click', async event => {
    const button = event.target.closest('[data-delete-douyin-rule]');
    if (!button) return;
    const items = await getDouyinRuleItems();
    const nextItems = items.filter(rule => rule.id !== button.dataset.deleteDouyinRule);
    await saveDouyinRules(nextItems);
  });

  marketList.addEventListener('click', async event => {
    const button = event.target.closest('[data-webapp-toggle]');
    if (!button) return;
    button.disabled = true;
    const appId = button.dataset.webappToggle;
    const installed = button.dataset.installed === '1';
    try {
      const response = await chrome.runtime.sendMessage({
        type: 'yanzi_webapp_set_installed',
        appId,
        installed: !installed
      });
      if (!response?.ok) throw new Error(response?.error || '操作失败');
      renderWebAppMarket(response.apps || []);
    } catch (error) {
      marketSyncStatus.textContent = error?.message || String(error);
    } finally {
      button.disabled = false;
    }
  });

  document.getElementById('sync-webapps').addEventListener('click', async () => {
    marketSyncStatus.textContent = '同步中…';
    try {
      const response = await chrome.runtime.sendMessage({ type: 'yanzi_webapp_sync_now' });
      marketSyncStatus.textContent = response?.ok ? '已同步' : '当前无法同步';
      if (response?.ok) await loadWebAppMarket();
    } catch {
      marketSyncStatus.textContent = '当前无法同步';
    }
  });

  void loadWebAppMarket();
  const portInput = document.getElementById("server-port");
  chrome.storage.local.get({ localWsUrl: "ws://127.0.0.1:53919/v1/browser/ws", chatgptBridgeStatus: "disconnected" }, config => {
    portInput.value = new URL(config.localWsUrl).port;
    document.getElementById("chatgpt-status").textContent = config.chatgptBridgeStatus === "connected" ? "已连接" : "未连接";
  });
  document.getElementById("save-port").addEventListener("click", () => {
    const port = Number(portInput.value);
    if (!Number.isInteger(port) || port < 1 || port > 65535) return;
    chrome.storage.local.set({ localWsUrl: `ws://127.0.0.1:${port}/v1/browser/ws` });
  });
  chrome.storage.onChanged.addListener(changes => {
    if (changes.chatgptBridgeStatus) document.getElementById("chatgpt-status").textContent = changes.chatgptBridgeStatus.newValue === "connected" ? "已连接" : "未连接";
  });
  const statusDot = document.getElementById("connection-status-dot");
  const statusText = document.getElementById("connection-status-text");
  const logsContainer = document.getElementById("logs-container");
  const logCountText = document.getElementById("log-count");
  const reconnectBtn = document.getElementById("reconnect-btn");

  // 1. 初始化并读取当前状态与日志
  chrome.storage.local.get(["connectionStatus", "logs"], (result) => {
    updateStatusUI(result.connectionStatus || "disconnected");
    renderLogs(result.logs || []);

    // 立即向 background.js 发送实时状态探测，唤醒并校验活跃连接
    chrome.runtime.sendMessage({ action: "get_status" }, (response) => {
      if (chrome.runtime.lastError) {
        updateStatusUI("disconnected");
        return;
      }
      if (response && response.status) {
        updateStatusUI(response.status);
      }
    });
  });

  // 2. 监听本地存储的数据变动，实现免刷新实时同步
  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName === "local") {
      if (changes.connectionStatus) {
        updateStatusUI(changes.connectionStatus.newValue);
      }
      if (changes.logs) {
        renderLogs(changes.logs.newValue);
      }
    }
  });

  // 3. 重新连接服务按钮绑定
  reconnectBtn.addEventListener("click", () => {
    reconnectBtn.disabled = true;
    reconnectBtn.classList.add("btn-connecting");
    reconnectBtn.innerText = "正在尝试连接...";

    // 向 background.js 发送重连消息
    chrome.runtime.sendMessage({ action: "reconnect" });

    // 1.5 秒后恢复按钮状态 (给网络建立留出响应时间)
    setTimeout(() => {
      reconnectBtn.disabled = false;
      reconnectBtn.classList.remove("btn-connecting");
      reconnectBtn.innerText = "重新连接燕子主程序";
    }, 1500);
  });

  // 辅助函数：更新连接状态 UI
  function updateStatusUI(status) {
    if (status === "connected") {
      statusDot.className = "led-dot connected";
      statusText.className = "status-text text-connected";
      statusText.innerText = "已连接";
    } else {
      statusDot.className = "led-dot disconnected";
      statusText.className = "status-text text-disconnected";
      statusText.innerText = "未连接";
    }
  }

  // 辅助函数：渲染日志
  function renderLogs(logs) {
    if (!logs || logs.length === 0) {
      logsContainer.innerHTML = `<div class="log-placeholder">暂无任何执行日志</div>`;
      logCountText.innerText = "0 条";
      return;
    }

    logCountText.innerText = `${logs.length} 条`;
    
    // 反转数组，最新的日志显示在最上面
    const html = logs.slice().reverse().map(log => {
      return `<div class="log-item">${escapeHtml(log)}</div>`;
    }).join("");

    logsContainer.innerHTML = html;
  }

  // 安全防注入 XSS 过滤
  function escapeHtml(text) {
    const map = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#039;'
    };
    return text.replace(/[&<>"']/g, m => map[m]);
  }
});
