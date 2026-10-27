document.addEventListener("DOMContentLoaded", () => {
  function showPanel(name) {
    document.querySelectorAll('[data-panel]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.panel === name)));
    for (const panel of ['overview', 'market', 'opencli', 'logs']) document.getElementById('panel-' + panel).hidden = panel !== name;
    if (name === 'market') loadWebAppMarket();
  }
  document.querySelectorAll('[data-panel]').forEach(button => button.onclick = () => showPanel(button.dataset.panel));
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      showPanel('market');
      document.getElementById('market-search')?.focus();
    }
  });

  document.getElementById('open-workbench').onclick = () => chrome.tabs.create({ url: 'http://127.0.0.1:43830/' });

  const marketList = document.getElementById('market-list');
  const marketCount = document.getElementById('market-count');
  const marketSyncStatus = document.getElementById('market-sync-status');
  const marketSearch = document.getElementById('market-search');
  const marketCategories = document.getElementById('market-categories');
  const expandedCapabilityApps = new Set();
  let webAppMarketApps = [];
  let selectedMarketCategory = 'all';
  let marketSearchQuery = '';
  let activeTabDomain = '';

  // 规则状态数据
  let allXhsRuleItems = [];
  let selectedXhsRuleType = 'all';
  let isXhsRulesCollapsed = false;

  let allDouyinRuleItems = [];
  let isDouyinRulesCollapsed = false;

  const DOUYIN_DEFAULT_RULES = {
    version: 2,
    items: [
      { id: 'default-shopping', type: 'keyword', value: '购物', label: '购物', scopes: ['title', 'tags', 'author', 'commerce'], enabled: true, createdAt: 0 },
      { id: 'default-live', type: 'keyword', value: '直播', label: '直播', scopes: ['title', 'tags', 'author', 'commerce'], enabled: true, createdAt: 0 },
      { id: 'default-ad', type: 'keyword', value: '广告', label: '广告', scopes: ['ad'], enabled: true, createdAt: 0 }
    ]
  };

  // 读取折叠偏好
  chrome.storage.local.get({
    'yanzi.popup.xhsRulesCollapsed': false,
    'yanzi.popup.douyinRulesCollapsed': false
  }, res => {
    isXhsRulesCollapsed = Boolean(res['yanzi.popup.xhsRulesCollapsed']);
    isDouyinRulesCollapsed = Boolean(res['yanzi.popup.douyinRulesCollapsed']);
  });

  const extensionVersion = document.getElementById('extension-version');
  if (extensionVersion) {
    extensionVersion.textContent = 'v' + chrome.runtime.getManifest().version;
  }
  const marketSearchShortcut = document.getElementById('market-search-shortcut');
  if (marketSearchShortcut && /Mac|iPhone|iPad/.test(navigator.platform || '')) {
    marketSearchShortcut.textContent = '⌘ K';
  }

  async function detectActiveTabDomain() {
    try {
      const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
      const current = tabs?.[0];
      if (current && current.url) {
        const u = new URL(current.url);
        activeTabDomain = u.hostname.toLowerCase().replace(/^www\./, '');
      }
    } catch {}
  }

  function getAppSiteDetails(app) {
    const matches = Array.isArray(app.matches) ? app.matches : [];
    let domain = '';
    for (const pattern of matches) {
      const m = String(pattern).match(/^[a-z]+:\/\/([^/*]+)/i);
      if (m && m[1]) {
        domain = m[1].toLowerCase().replace(/^(\*\.)?www\./, '').replace(/^\*\./, '');
        break;
      }
    }
    if (!domain) domain = 'other';

    let siteName = domain;
    let fallbackSvg = '';

    if (domain.includes('xiaohongshu.com')) {
      siteName = '小红书';
      fallbackSvg = '<svg viewBox="0 0 24 24" width="22" height="22"><rect width="24" height="24" rx="5" fill="#ff2442"/><path d="M7 6.5h3v11H7zm7 0h3v11h-3z" fill="#fff"/></svg>';
    } else if (domain.includes('douyin.com')) {
      siteName = '抖音';
      fallbackSvg = '<svg viewBox="0 0 24 24" width="22" height="22"><rect width="24" height="24" rx="5" fill="#000"/><path d="M15 6a3 3 0 0 0 1.5.5V9a5 5 0 0 1-2.5-.7v4.5a3.5 3.5 0 1 1-3.5-3.5v2.2a1.5 1.5 0 1 0 1.5 1.5V5z" fill="#00f2fe"/><path d="M15 6a3 3 0 0 0 1.5.5V8a5 5 0 0 1-2.5-.7v4.5a3.5 3.5 0 1 1-3.5-3.5v1.2a1.5 1.5 0 1 0 1.5 1.5V5z" fill="#fe2c55" opacity="0.8"/></svg>';
    } else if (domain.includes('chatgpt.com') || domain.includes('openai.com')) {
      siteName = 'ChatGPT';
      fallbackSvg = '<svg viewBox="0 0 24 24" width="22" height="22"><rect width="24" height="24" rx="5" fill="#10a37f"/><circle cx="12" cy="12" r="6" fill="none" stroke="#fff" stroke-width="2.5"/><circle cx="12" cy="12" r="2.5" fill="#fff"/></svg>';
    } else {
      siteName = domain === 'other' ? '通用网页' : domain;
      fallbackSvg = '<svg viewBox="0 0 24 24" width="22" height="22"><rect width="24" height="24" rx="5" fill="#334155"/><circle cx="12" cy="12" r="6" fill="none" stroke="#94a3b8" stroke-width="2"/></svg>';
    }

    const faviconUrl = domain !== 'other'
      ? `https://www.google.com/s2/favicons?domain=${domain}&sz=64`
      : '';

    const isCurrentSite = Boolean(activeTabDomain && (
      activeTabDomain === domain ||
      activeTabDomain.endsWith('.' + domain) ||
      domain.endsWith('.' + activeTabDomain)
    ));

    return {
      domain,
      siteName,
      faviconUrl,
      fallbackSvg,
      isCurrentSite
    };
  }

  async function loadWebAppMarket() {
    try {
      await detectActiveTabDomain();
      const [marketRes, xhsRes, douyinRes] = await Promise.all([
        chrome.runtime.sendMessage({ type: 'yanzi_webapp_market_list' }),
        chrome.runtime.sendMessage({ type: 'yanzi_webapp_storage_get', appId: 'xiaohongshu.filter', key: 'rules', fallback: { version: 1, items: [] } }),
        chrome.runtime.sendMessage({ type: 'yanzi_webapp_storage_get', appId: 'douyin.filter', key: 'rules', fallback: DOUYIN_DEFAULT_RULES })
      ]);

      if (!marketRes?.ok) throw new Error(marketRes?.error || '读取失败');

      allXhsRuleItems = Array.isArray(xhsRes?.value?.items) ? xhsRes.value.items : [];
      allDouyinRuleItems = Array.isArray(douyinRes?.value?.items) ? douyinRes.value.items : DOUYIN_DEFAULT_RULES.items;

      renderWebAppMarket(marketRes.apps || []);
    } catch (error) {
      marketList.innerHTML = `<div class="market-empty">读取失败：${escapeHtml(error?.message || String(error))}</div>`;
    }
  }

  function renderWebAppMarket(apps) {
    webAppMarketApps = Array.isArray(apps) ? apps : [];
    renderMarketCategories();
    applyWebAppMarketFilters();
  }

  function renderMarketCategories() {
    const siteMap = new Map();
    let hasCurrentSite = false;

    for (const app of webAppMarketApps) {
      const site = getAppSiteDetails(app);
      if (site.isCurrentSite) hasCurrentSite = true;
      if (!siteMap.has(site.domain)) {
        siteMap.set(site.domain, {
          domain: site.domain,
          name: site.siteName,
          count: 0,
          isCurrentSite: site.isCurrentSite
        });
      }
      siteMap.get(site.domain).count++;
    }

    const categories = [{ id: 'all', label: '全部', count: webAppMarketApps.length }];
    if (hasCurrentSite) {
      const currentCount = webAppMarketApps.filter(a => getAppSiteDetails(a).isCurrentSite).length;
      categories.push({ id: 'current', label: '当前网页', count: currentCount, isCurrent: true });
    }

    const sites = Array.from(siteMap.values()).sort((a, b) => {
      if (a.isCurrentSite !== b.isCurrentSite) return a.isCurrentSite ? -1 : 1;
      return a.name.localeCompare(b.name, 'zh-CN');
    });

    for (const s of sites) {
      categories.push({
        id: 'site:' + s.domain,
        label: s.name,
        count: s.count,
        domain: s.domain,
        isCurrent: s.isCurrentSite
      });
    }

    if (!categories.some(c => c.id === selectedMarketCategory)) {
      selectedMarketCategory = 'all';
    }

    marketCategories.innerHTML = categories.map(cat => {
      const isSelected = selectedMarketCategory === cat.id;
      const currentDot = cat.isCurrent ? '<span class="current-site-dot" style="width:6px;height:6px;background:#10b981;border-radius:50%;display:inline-block;margin-right:4px;"></span>' : '';
      return `<button type="button" class="market-category-chip ${cat.isCurrent ? 'is-current-chip' : ''}" data-market-category="${escapeHtml(cat.id)}" aria-pressed="${String(isSelected)}">
        <span>${currentDot}${escapeHtml(cat.label)}</span>
        <small>${cat.count}</small>
      </button>`;
    }).join('');
  }

  // 构建小红书规则卡片
  function buildXhsRulesCardHtml(site) {
    const filteredRules = selectedXhsRuleType === 'all'
      ? allXhsRuleItems
      : allXhsRuleItems.filter(r => (r.type || 'keyword') === selectedXhsRuleType);

    const pillsHtml = filteredRules.length
      ? filteredRules.map(rule => {
          const type = rule.type || 'keyword';
          const typeName = type === 'author' ? '作者' : type === 'note' ? '笔记' : '关键词';
          const typeClass = type === 'author' ? 'type-author' : type === 'note' ? 'type-note' : 'type-keyword';
          const text = escapeHtml(rule.label || rule.value || '');
          return `<div class="webapp-rule-pill" title="${text} (${typeName})">
            <strong>${text}</strong>
            <span class="rule-type-badge ${typeClass}">${typeName}</span>
            <button type="button" class="rule-pill-delete" data-delete-xhs-rule="${escapeHtml(rule.id || '')}" title="移出此条屏蔽">×</button>
          </div>`;
        }).join('')
      : `<div class="market-empty compact" style="padding:10px 4px;font-size:11px;color:#94a3b8;">
          ${allXhsRuleItems.length ? '当前分类下暂无规则' : '暂无屏蔽规则'}
        </div>`;

    return `<article class="webapp-card webapp-rule-card" data-rule-card="xhs">
      <div class="webapp-card-main">
        <div class="webapp-card-head">
          <div class="webapp-card-title">
            <div class="webapp-app-icon-box" title="小红书 (xiaohongshu.com)">
              <img class="webapp-site-favicon" src="${escapeHtml(site.faviconUrl)}" alt="小红书" onerror="this.style.display='none'; this.nextElementSibling.style.display='inline-flex';">
              <span class="webapp-site-svg-fallback" style="display:none;">${site.fallbackSvg}</span>
            </div>
            <div class="webapp-card-title-info">
              <div class="webapp-title-row">
                <strong>小红书内容规则</strong>
                <span class="webapp-version">${allXhsRuleItems.length} 条规则</span>
              </div>
              <div class="webapp-meta">
                <span class="webapp-site-domain-tag">@match xiaohongshu.com</span>
                <span class="webapp-category-label">内容控制</span>
              </div>
            </div>
          </div>
          <button type="button" class="webapp-toggle" data-toggle-xhs-collapse="1">${isXhsRulesCollapsed ? '展开 (' + allXhsRuleItems.length + '条)' : '收起'}</button>
        </div>
        <p class="webapp-description">管理小红书信息流中的关键词、作者及单篇笔记屏蔽规则。</p>
        <div class="webapp-rule-card-body ${isXhsRulesCollapsed ? 'collapsed' : ''}">
          <div class="webapp-rules-filter-pills" id="xhs-card-type-filter">
            <button type="button" class="rule-type-pill ${selectedXhsRuleType === 'all' ? 'active' : ''}" data-xhs-type="all">全部</button>
            <button type="button" class="rule-type-pill ${selectedXhsRuleType === 'keyword' ? 'active' : ''}" data-xhs-type="keyword">关键词</button>
            <button type="button" class="rule-type-pill ${selectedXhsRuleType === 'author' ? 'active' : ''}" data-xhs-type="author">作者</button>
            <button type="button" class="rule-type-pill ${selectedXhsRuleType === 'note' ? 'active' : ''}" data-xhs-type="note">笔记</button>
          </div>
          <div class="webapp-rule-pill-box xhs-adaptive-pill-list">${pillsHtml}</div>
        </div>
        <div class="webapp-card-actions" style="margin-top:8px;">
          <button type="button" class="webapp-context-copy" data-copy-context="xiaohongshu.filter" title="复制小红书模块的开发背景给 AI"><span aria-hidden="true">✦</span>帮帮忙</button>
        </div>
      </div>
    </article>`;
  }

  // 构建抖音屏蔽关键词卡片
  function buildDouyinRulesCardHtml(site) {
    const scopeMap = { title: '标题', tags: '标签', author: '作者', commerce: '商品', ad: '广告' };
    const pillsHtml = allDouyinRuleItems.length
      ? allDouyinRuleItems.map(rule => {
          const scopeText = (rule.scopes || ['title', 'tags', 'author'])
            .map(s => scopeMap[s] || s)
            .join('·');
          const text = escapeHtml(rule.label || rule.value || '');
          return `<div class="webapp-rule-pill" title="${text} (${escapeHtml(scopeText)})">
            <strong>${text}</strong>
            <span class="rule-type-badge type-keyword">${escapeHtml(scopeText)}</span>
            <button type="button" class="rule-pill-delete" data-delete-douyin-rule="${escapeHtml(rule.id || '')}" title="删除此关键词">×</button>
          </div>`;
        }).join('')
      : `<div class="market-empty compact" style="padding:10px 4px;font-size:11px;color:#94a3b8;">暂无屏蔽关键词</div>`;

    return `<article class="webapp-card webapp-rule-card" data-rule-card="douyin">
      <div class="webapp-card-main">
        <div class="webapp-card-head">
          <div class="webapp-card-title">
            <div class="webapp-app-icon-box" title="抖音 (douyin.com)">
              <img class="webapp-site-favicon" src="${escapeHtml(site.faviconUrl)}" alt="抖音" onerror="this.style.display='none'; this.nextElementSibling.style.display='inline-flex';">
              <span class="webapp-site-svg-fallback" style="display:none;">${site.fallbackSvg}</span>
            </div>
            <div class="webapp-card-title-info">
              <div class="webapp-title-row">
                <strong>抖音屏蔽关键词</strong>
                <span class="webapp-version">${allDouyinRuleItems.length} 个关键词</span>
              </div>
              <div class="webapp-meta">
                <span class="webapp-site-domain-tag">@match douyin.com</span>
                <span class="webapp-category-label">内容控制</span>
              </div>
            </div>
          </div>
          <button type="button" class="webapp-toggle" data-toggle-douyin-collapse="1">${isDouyinRulesCollapsed ? '展开 (' + allDouyinRuleItems.length + '条)' : '收起'}</button>
        </div>
        <p class="webapp-description">添加关键词跳过抖音视频，同时自动匹配标题、话题标签和作者。</p>
        <div class="webapp-rule-card-body ${isDouyinRulesCollapsed ? 'collapsed' : ''}">
          <form class="webapp-rule-form" id="douyin-card-rule-form" style="margin:0 0 8px;">
            <input id="douyin-card-rule-input" type="text" maxlength="80" placeholder="输入关键词，如：购物、直播" style="flex:1;">
            <button type="submit">添加</button>
          </form>
          <div class="webapp-rule-pill-box douyin-adaptive-pill-list">${pillsHtml}</div>
        </div>
        <div class="webapp-card-actions" style="margin-top:8px;">
          <button type="button" class="webapp-context-copy" data-copy-context="douyin.filter" title="复制抖音模块的开发背景给 AI"><span aria-hidden="true">✦</span>帮帮忙</button>
        </div>
      </div>
    </article>`;
  }

  function applyWebAppMarketFilters() {
    const query = marketSearchQuery.trim().toLocaleLowerCase();
    const filteredApps = webAppMarketApps.filter(app => {
      const site = getAppSiteDetails(app);
      if (selectedMarketCategory === 'current' && !site.isCurrentSite) return false;
      if (selectedMarketCategory.startsWith('site:')) {
        const targetDomain = selectedMarketCategory.slice(5);
        if (site.domain !== targetDomain) return false;
      }
      if (!query) return true;
      const searchable = [
        app.name, app.id, app.description, site.domain, site.siteName,
        ...(Array.isArray(app.capabilities) ? app.capabilities : []),
        ...(Array.isArray(app.summaryCapabilities) ? app.summaryCapabilities : [])
      ].join(' ').toLocaleLowerCase();
      return searchable.includes(query);
    });

    if (marketCount) {
      marketCount.textContent = filteredApps.length === webAppMarketApps.length
        ? webAppMarketApps.length + ' 个'
        : filteredApps.length + '/' + webAppMarketApps.length + ' 个';
    }

    if (!filteredApps.length && selectedMarketCategory !== 'all') {
      marketList.innerHTML = '<div class="market-empty">没有匹配的小程序。试试其他关键词或切换分类。</div>';
      return;
    }

    // 按站点分组归档
    const groupsMap = new Map();
    for (const app of filteredApps) {
      const site = getAppSiteDetails(app);
      if (!groupsMap.has(site.domain)) {
        groupsMap.set(site.domain, {
          site,
          apps: []
        });
      }
      groupsMap.get(site.domain).apps.push(app);
    }

    // 排序：当前正在访问的站点置顶
    const sortedGroups = Array.from(groupsMap.values()).sort((a, b) => {
      if (a.site.isCurrentSite !== b.site.isCurrentSite) return a.site.isCurrentSite ? -1 : 1;
      return a.site.siteName.localeCompare(b.site.siteName, 'zh-CN');
    });

    marketList.innerHTML = sortedGroups.map(group => {
      const { site, apps } = group;
      const currentBadge = site.isCurrentSite
        ? '<span class="webapp-site-badge-current">● 当前正在访问</span>'
        : '';

      const cardsHtml = apps.map(app => {
        const allCapabilities = Array.isArray(app.capabilities) ? app.capabilities : [];
        const summaryCapabilities = Array.isArray(app.summaryCapabilities) && app.summaryCapabilities.length
          ? app.summaryCapabilities.slice(0, 4)
          : allCapabilities.slice(0, 4);
        const expanded = expandedCapabilityApps.has(app.id);
        const visibleCapabilities = expanded ? allCapabilities : summaryCapabilities;
        const capabilities = visibleCapabilities.map(item =>
          '<span class="webapp-capability-tag">' + escapeHtml(String(item)) + '</span>'
        ).join('');
        const more = allCapabilities.length > summaryCapabilities.length
          ? '<button type="button" class="webapp-capability-more" data-capabilities-toggle="' +
            escapeHtml(app.id) + '" aria-expanded="' + String(expanded) + '">' +
            (expanded ? '收起能力' : '还有 ' + (allCapabilities.length - summaryCapabilities.length) + ' 项 · 查看全部') +
            '</button>'
          : '';

        return `<article class="webapp-card" data-app-id="${escapeHtml(app.id)}">
          <div class="webapp-card-main">
            <div class="webapp-card-head">
              <div class="webapp-card-title">
                <div class="webapp-app-icon-box" title="${escapeHtml(site.siteName)} (${escapeHtml(site.domain)})">
                  <img class="webapp-site-favicon" src="${escapeHtml(site.faviconUrl)}" alt="${escapeHtml(site.siteName)}" onerror="this.style.display='none'; this.nextElementSibling.style.display='inline-flex';">
                  <span class="webapp-site-svg-fallback" style="display:none;">${site.fallbackSvg}</span>
                </div>
                <div class="webapp-card-title-info">
                  <div class="webapp-title-row">
                    <strong>${escapeHtml(app.name)}</strong>
                    <span class="webapp-version">v${escapeHtml(app.version || '')}</span>
                  </div>
                  <div class="webapp-meta">
                    <span class="webapp-site-domain-tag">@match ${escapeHtml(site.domain)}</span>
                    <span class="webapp-category-label">${escapeHtml(app.category || '网页能力')}</span>
                  </div>
                </div>
              </div>
              <button type="button" class="webapp-toggle ${app.installed ? 'installed' : ''}" data-webapp-toggle="${escapeHtml(app.id)}" data-installed="${app.installed ? '1' : '0'}">${app.installed ? '已启用' : '未启用'}</button>
            </div>
            <p class="webapp-description">${escapeHtml(app.description || '')}</p>
            <div class="webapp-card-actions">
              <button type="button" class="webapp-context-copy" data-copy-context="${escapeHtml(app.id)}" title="复制此小程序的开发背景给 AI"><span aria-hidden="true">✦</span>帮帮忙</button>
            </div>
          </div>
          <aside class="webapp-card-side">
            <div class="webapp-capabilities-header"><span>主要能力</span><small>${allCapabilities.length} 项</small></div>
            <div class="webapp-capabilities" data-expanded="${String(expanded)}">${capabilities}</div>
            ${more}
          </aside>
        </article>`;
      }).join('');

      // 小红书专属规则卡片
      let extraRuleCard = '';
      if (site.domain.includes('xiaohongshu.com') && (!query || '小红书内容规则屏蔽作者关键词笔记'.includes(query))) {
        extraRuleCard = buildXhsRulesCardHtml(site);
      } else if (site.domain.includes('douyin.com') && (!query || '抖音屏蔽关键词购物直播广告跳过'.includes(query))) {
        extraRuleCard = buildDouyinRulesCardHtml(site);
      }

      return `<div class="webapp-site-group">
        <div class="webapp-site-header">
          <div class="webapp-site-title">
            <img class="webapp-site-mini-icon" src="${escapeHtml(site.faviconUrl)}" onerror="this.style.display='none';">
            <span>${escapeHtml(site.siteName)}</span>
            <span class="webapp-site-domain-tag">${escapeHtml(site.domain)}</span>
            ${currentBadge}
          </div>
          <span class="webapp-site-count">${apps.length + (extraRuleCard ? 1 : 0)} 个模块</span>
        </div>
        <div class="webapp-site-cards">${cardsHtml}${extraRuleCard}</div>
      </div>`;
    }).join('');
  }

  async function readExtensionDocument(path) {
    if (!path) return '';
    const response = await fetch(chrome.runtime.getURL(path));
    if (!response.ok) throw new Error('无法读取开发背景文档：' + path);
    return response.text();
  }

  async function copyWebAppContext(app, button) {
    if (!app || !button) return;
    const originalLabel = button.textContent;
    button.disabled = true;
    button.textContent = '准备中…';
    try {
      const manifest = chrome.runtime.getManifest();
      const docs = await Promise.all([
        readExtensionDocument('DEVELOPMENT_CONTEXT.md'),
        readExtensionDocument(app.contextDoc)
      ]);
      const generatedAt = new Intl.DateTimeFormat('zh-CN', {
        dateStyle: 'medium',
        timeStyle: 'short'
      }).format(new Date());
      const context = [
        '# 帮帮忙｜燕子浏览器助手开发交接上下文',
        '',
        '## 本次目标项目',
        '- 小程序：' + app.name,
        '- 唯一 ID：' + app.id,
        '- 分类：' + (app.category || '网页能力'),
        '- 小程序版本：' + (app.version || '未声明'),
        '- 扩展版本：' + manifest.version,
        '- 安装状态：' + (app.installed ? '已安装' : '未安装'),
        '- 生成时间：' + generatedAt,
        '- Git 状态：浏览器扩展无法直接读取本机工作树。开始修改前，请先运行 git status --short --branch 和 git diff --stat，确认未提交改动。',
        '',
        '## 扩展通用背景',
        docs[0].trim(),
        '',
        '## ' + app.name + ' 专项背景',
        docs[1].trim(),
        '',
        '## 接手时必须遵守',
        '1. 先核对实际文件、分支和未提交修改；本文档是导航，不替代源码。',
        '2. 以仓库中当前代码为准；路径不存在或实现不一致时先重新定位，不要猜测。',
        '3. 只修改完成任务所必需的文件，不覆盖其他工作树或无关改动。',
        '4. 修改后运行相关 node --check、JSON 解析和 git diff --check；UI 改动应实际重载验证。',
        '5. 不复制、输出或写入 API Token、Cookie、密码等凭证。'
      ].join('\n');

      try {
        await navigator.clipboard.writeText(context);
      } catch {
        const textarea = document.createElement('textarea');
        textarea.value = context;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        const copied = document.execCommand('copy');
        textarea.remove();
        if (!copied) throw new Error('浏览器未允许访问剪贴板');
      }
      button.textContent = '已复制';
      marketSyncStatus.textContent = '已复制「' + app.name + '」的开发上下文，可直接发送给其他 AI。';
    } catch (error) {
      button.textContent = '重试帮帮忙';
      marketSyncStatus.textContent = error?.message || '复制失败，请重试。';
    } finally {
      button.disabled = false;
      setTimeout(() => {
        if (button.isConnected) button.textContent = originalLabel;
      }, 1800);
    }
  }

  // 统一事件委托处理市场与规则卡片
  marketList.addEventListener('click', async event => {
    // 1. 小红书规则折叠
    const xhsCollapseBtn = event.target.closest('[data-toggle-xhs-collapse]');
    if (xhsCollapseBtn) {
      isXhsRulesCollapsed = !isXhsRulesCollapsed;
      chrome.storage.local.set({ 'yanzi.popup.xhsRulesCollapsed': isXhsRulesCollapsed });
      applyWebAppMarketFilters();
      return;
    }

    // 2. 小红书规则类型筛选
    const xhsTypeBtn = event.target.closest('[data-xhs-type]');
    if (xhsTypeBtn) {
      selectedXhsRuleType = xhsTypeBtn.dataset.xhsType || 'all';
      applyWebAppMarketFilters();
      return;
    }

    // 3. 小红书规则删除
    const xhsDeleteBtn = event.target.closest('[data-delete-xhs-rule]');
    if (xhsDeleteBtn) {
      const ruleId = xhsDeleteBtn.dataset.deleteXhsRule;
      allXhsRuleItems = allXhsRuleItems.filter(r => r.id !== ruleId);
      await chrome.runtime.sendMessage({
        type: 'yanzi_webapp_storage_set',
        appId: 'xiaohongshu.filter',
        key: 'rules',
        value: { version: 1, items: allXhsRuleItems }
      });
      applyWebAppMarketFilters();
      return;
    }

    // 4. 抖音规则折叠
    const douyinCollapseBtn = event.target.closest('[data-toggle-douyin-collapse]');
    if (douyinCollapseBtn) {
      isDouyinRulesCollapsed = !isDouyinRulesCollapsed;
      chrome.storage.local.set({ 'yanzi.popup.douyinRulesCollapsed': isDouyinRulesCollapsed });
      applyWebAppMarketFilters();
      return;
    }

    // 5. 抖音规则删除
    const douyinDeleteBtn = event.target.closest('[data-delete-douyin-rule]');
    if (douyinDeleteBtn) {
      const ruleId = douyinDeleteBtn.dataset.deleteDouyinRule;
      allDouyinRuleItems = allDouyinRuleItems.filter(r => r.id !== ruleId);
      await chrome.runtime.sendMessage({
        type: 'yanzi_webapp_storage_set',
        appId: 'douyin.filter',
        key: 'rules',
        value: { version: 2, items: allDouyinRuleItems }
      });
      applyWebAppMarketFilters();
      return;
    }

    // 6. 复制开发上下文
    const contextButton = event.target.closest('[data-copy-context]');
    if (contextButton) {
      event.preventDefault();
      event.stopPropagation();
      const targetAppId = contextButton.dataset.copyContext;
      let app = webAppMarketApps.find(item => item.id === targetAppId);
      if (!app) {
        if (targetAppId === 'xiaohongshu.filter') {
          app = {
            id: 'xiaohongshu.filter',
            name: '小红书内容规则',
            category: '内容控制',
            version: '0.7.24',
            installed: true,
            contextDoc: 'webapps/xiaohongshu-filter/DEVELOPMENT_CONTEXT.md'
          };
        } else if (targetAppId === 'douyin.filter') {
          app = {
            id: 'douyin.filter',
            name: '抖音屏蔽关键词',
            category: '内容控制',
            version: '0.2.9',
            installed: true,
            contextDoc: 'webapps/douyin-filter/DEVELOPMENT_CONTEXT.md'
          };
        }
      }
      await copyWebAppContext(app, contextButton);
      return;
    }

    // 7. 能力展开/收起
    const capabilityButton = event.target.closest('[data-capabilities-toggle]');
    if (capabilityButton) {
      const appId = capabilityButton.dataset.capabilitiesToggle;
      if (expandedCapabilityApps.has(appId)) expandedCapabilityApps.delete(appId);
      else expandedCapabilityApps.add(appId);
      applyWebAppMarketFilters();
      return;
    }

    // 8. 小程序启停开关
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

  // 抖音规则添加表单监听
  marketList.addEventListener('submit', async event => {
    const form = event.target.closest('#douyin-card-rule-form');
    if (!form) return;
    event.preventDefault();

    const input = form.querySelector('#douyin-card-rule-input');
    const value = (input?.value || '').replace(/\s+/g, ' ').trim();
    if (!value) return;

    const duplicate = allDouyinRuleItems.some(rule =>
      String(rule.value || '').trim().toLowerCase() === value.toLowerCase()
    );
    if (!duplicate) {
      allDouyinRuleItems.push({
        id: crypto.randomUUID(),
        type: 'keyword',
        value,
        label: value,
        scopes: ['title', 'tags', 'author'],
        enabled: true,
        createdAt: Date.now()
      });
      await chrome.runtime.sendMessage({
        type: 'yanzi_webapp_storage_set',
        appId: 'douyin.filter',
        key: 'rules',
        value: { version: 2, items: allDouyinRuleItems }
      });
    }
    if (input) input.value = '';
    applyWebAppMarketFilters();
  });

  marketSearch.addEventListener('input', event => {
    marketSearchQuery = event.target.value;
    applyWebAppMarketFilters();
  });

  marketCategories.addEventListener('click', event => {
    const button = event.target.closest('[data-market-category]');
    if (!button) return;
    selectedMarketCategory = button.dataset.marketCategory;
    renderMarketCategories();
    applyWebAppMarketFilters();
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

  showPanel('market');

  const portInput = document.getElementById("server-port");
  chrome.storage.local.get({ localWsUrl: "ws://127.0.0.1:53919/v1/browser/ws" }, config => {
    portInput.value = new URL(config.localWsUrl).port;
  });
  document.getElementById("save-port").addEventListener("click", () => {
    const port = Number(portInput.value);
    if (!Number.isInteger(port) || port < 1 || port > 65535) return;
    chrome.storage.local.set({ localWsUrl: `ws://127.0.0.1:${port}/v1/browser/ws`, manualLocalWsUrl: true });
  });

  const statusDot = document.getElementById("connection-status-dot");
  const statusText = document.getElementById("connection-status-text");
  const logsContainer = document.getElementById("logs-container");
  const logCountText = document.getElementById("log-count");
  const reconnectBtn = document.getElementById("reconnect-btn");

  // 初始化并读取当前状态与日志
  chrome.storage.local.get(["connectionStatus", "logs"], (result) => {
    updateStatusUI(result.connectionStatus || "disconnected");
    renderLogs(result.logs || []);
  });

  // 监听来自后台的数据变化
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.connectionStatus) updateStatusUI(changes.connectionStatus.newValue);
    if (changes.logs) renderLogs(changes.logs.newValue || []);
    if (changes['yanziWebApps.installed'] || changes['yanziWebAppData::xiaohongshu.filter::rules'] || changes['yanziWebAppData::douyin.filter::rules']) {
      loadWebAppMarket();
    }
  });

  function updateStatusUI(status) {
    if (status === "connected") {
      statusDot.className = "led-dot connected";
      statusText.textContent = "已连接并处于活动状态";
      statusText.className = "status-text text-connected";
      reconnectBtn.style.display = "none";
    } else if (status === "connecting") {
      statusDot.className = "led-dot connecting";
      statusText.textContent = "正在尝试建立连接...";
      statusText.className = "status-text text-connecting";
      reconnectBtn.style.display = "none";
    } else {
      statusDot.className = "led-dot disconnected";
      statusText.textContent = "未连接至燕子主程序";
      statusText.className = "status-text text-disconnected";
      reconnectBtn.style.display = "block";
    }
  }

  function renderLogs(logs) {
    logCountText.textContent = `${logs.length} 条`;
    if (logs.length === 0) {
      logsContainer.innerHTML = '<div class="log-placeholder">暂无任何执行日志</div>';
      return;
    }
    logsContainer.innerHTML = logs.map(item => {
      let time = "";
      let msg = "";
      if (typeof item === "string") {
        const match = item.match(/^\[(.*?)\]\s*(.*)$/);
        if (match) {
          time = match[1];
          msg = match[2];
        } else {
          msg = item;
        }
      } else if (item && typeof item === "object") {
        time = item.time || item.timestamp || "";
        msg = item.message || item.text || item.msg || JSON.stringify(item);
      } else {
        msg = String(item ?? "");
      }
      return `
        <div class="log-item">
          ${time ? `<span class="log-time">${escapeHtml(time)}</span>` : ""}
          <span class="log-msg">${escapeHtml(msg)}</span>
        </div>
      `;
    }).join("");
    logsContainer.scrollTop = logsContainer.scrollHeight;
  }

  reconnectBtn.addEventListener("click", () => {
    chrome.runtime.sendMessage({ action: "reconnect" }, (response) => {
      if (chrome.runtime.lastError) console.error(chrome.runtime.lastError.message);
    });
  });

  function escapeHtml(value) {
    return String(value || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
});
