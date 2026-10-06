const YANZI_WEBAPP_CATALOG_URL = chrome.runtime.getURL("webapps/catalog.json");
const YANZI_WEBAPP_INSTALL_KEY = "yanziWebApps.installed";
const YANZI_WEBAPP_DATA_PREFIX = "yanziWebAppData::";
const YANZI_WEBAPP_MARKET_APP_ID = "yanzi.browser.market";
const YANZI_WEBAPP_MARKET_KEY = "installed-apps";
const YANZI_WEBAPP_HOST_TIMEOUT_MS = 8000;

let yanziWebAppCatalog = null;
const yanziWebAppHostRequests = new Map();

async function loadYanziWebAppCatalog() {
  if (yanziWebAppCatalog) return yanziWebAppCatalog;
  const response = await fetch(YANZI_WEBAPP_CATALOG_URL);
  if (!response.ok) throw new Error(`网页小程序目录读取失败: ${response.status}`);
  yanziWebAppCatalog = await response.json();
  return yanziWebAppCatalog;
}

function webAppDataStorageKey(appId, key) {
  return `${YANZI_WEBAPP_DATA_PREFIX}${appId}::${key}`;
}

function matchPattern(url, pattern) {
  if (!url || !pattern) return false;
  const escaped = pattern
    .replace(/[.+?^$()|[\]\\{}]/g, "\\$&")
    .replace(/\*/g, ".*");
  return new RegExp(`^${escaped}$`, "i").test(url);
}

async function getInstalledWebApps() {
  const catalog = await loadYanziWebAppCatalog();
  const stored = await chrome.storage.local.get(YANZI_WEBAPP_INSTALL_KEY);
  const installed = stored[YANZI_WEBAPP_INSTALL_KEY] || {};
  let changed = false;

  for (const app of catalog.apps || []) {
    if (typeof installed[app.id] !== "boolean") {
      installed[app.id] = Boolean(app.defaultInstalled);
      changed = true;
    }
  }

  if (changed || !stored[YANZI_WEBAPP_INSTALL_KEY]) {
    await chrome.storage.local.set({ [YANZI_WEBAPP_INSTALL_KEY]: installed });
  }
  return installed;
}

async function saveInstalledWebApps(installed, sync = true) {
  await chrome.storage.local.set({ [YANZI_WEBAPP_INSTALL_KEY]: installed });
  if (sync) {
    await setWebAppData(
      YANZI_WEBAPP_MARKET_APP_ID,
      YANZI_WEBAPP_MARKET_KEY,
      installed,
      { syncNow: true }
    );
  }
}

async function listMarketApps() {
  const catalog = await loadYanziWebAppCatalog();
  const installed = await getInstalledWebApps();
  return (catalog.apps || []).map(app => ({
    ...app,
    installed: Boolean(installed[app.id])
  }));
}

async function applyInstalledWebAppState(installed) {
  const catalog = await loadYanziWebAppCatalog();
  const tabs = await chrome.tabs.query({});
  for (const app of catalog.apps || []) {
    for (const tab of tabs) {
      if (!tab.id || !tab.url || !(app.matches || []).some(pattern => matchPattern(tab.url, pattern))) continue;
      if (installed[app.id]) {
        await injectWebAppIntoTab(tab.id, app);
      } else {
        chrome.tabs.sendMessage(tab.id, {
          type: "yanzi_webapp_command",
          appId: app.id,
          action: "disable"
        }).catch(() => {});
      }
    }
  }
}

async function setWebAppInstalled(appId, enabled) {
  const catalog = await loadYanziWebAppCatalog();
  const app = (catalog.apps || []).find(item => item.id === appId);
  if (!app) throw new Error(`未知网页小程序: ${appId}`);

  const installed = await getInstalledWebApps();
  installed[appId] = Boolean(enabled);
  await saveInstalledWebApps(installed, true);
  await refreshYanziWebAppContextMenus();

  const tabs = await chrome.tabs.query({});
  for (const tab of tabs) {
    if (!tab.id || !tab.url || !(app.matches || []).some(pattern => matchPattern(tab.url, pattern))) continue;
    if (enabled) {
      await injectWebAppIntoTab(tab.id, app);
    } else {
      chrome.tabs.sendMessage(tab.id, {
        type: "yanzi_webapp_command",
        appId,
        action: "disable"
      }).catch(() => {});
    }
  }
}

async function injectWebAppIntoTab(tabId, app) {
  if (!app?.contentScript) return;
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: [app.contentScript]
    });
  } catch (error) {
    const message = error?.message || String(error);
    if (!/Cannot access|chrome:\/\/|edge:\/\/|The extensions gallery/i.test(message)) {
      globalThis.yanziBrowserHost?.log?.(`[网页小程序] 注入 ${app.id} 失败: ${message}`);
    }
  }
}

async function injectInstalledWebApps(tabId, url) {
  const apps = await listMarketApps();
  for (const app of apps) {
    if (!app.installed) continue;
    if ((app.matches || []).some(pattern => matchPattern(url, pattern))) {
      await injectWebAppIntoTab(tabId, app);
    }
  }
}

async function ensureWebAppInjected(tabId, appId) {
  const catalog = await loadYanziWebAppCatalog();
  const app = (catalog.apps || []).find(item => item.id === appId);
  if (!app) return false;
  await injectWebAppIntoTab(tabId, app);
  return true;
}

async function refreshYanziWebAppContextMenus() {
  const ownedMenuIds = [
    "yanzi-xhs-block-selection",
    "yanzi-xhs-block-author",
    "yanzi-xhs-block-note"
  ];
  await Promise.allSettled(ownedMenuIds.map(id => chrome.contextMenus.remove(id)));
  const installed = await getInstalledWebApps();
  if (!installed["xiaohongshu.filter"]) return;

  const patterns = ["https://www.xiaohongshu.com/*"];
  chrome.contextMenus.create({
    id: "yanzi-xhs-block-selection",
    title: "屏蔽关键词“%s”",
    contexts: ["selection"],
    documentUrlPatterns: patterns
  });
  chrome.contextMenus.create({
    id: "yanzi-xhs-block-author",
    title: "屏蔽这篇笔记的作者",
    contexts: ["page", "selection", "link", "image"],
    documentUrlPatterns: patterns
  });
  chrome.contextMenus.create({
    id: "yanzi-xhs-block-note",
    title: "屏蔽这篇笔记",
    contexts: ["page", "selection", "link", "image"],
    documentUrlPatterns: patterns
  });
}

async function sendContextCommand(info, tab) {
  if (!tab?.id) return;
  const appId = "xiaohongshu.filter";
  const actionMap = {
    "yanzi-xhs-block-selection": "block-selection",
    "yanzi-xhs-block-author": "block-author",
    "yanzi-xhs-block-note": "block-note"
  };
  const action = actionMap[info.menuItemId];
  if (!action) return;

  await ensureWebAppInjected(tab.id, appId);
  try {
    await chrome.tabs.sendMessage(tab.id, {
      type: "yanzi_webapp_command",
      appId,
      action,
      selectionText: info.selectionText || ""
    });
  } catch (error) {
    globalThis.yanziBrowserHost?.log?.(`[网页小程序] 右键命令发送失败: ${error?.message || error}`);
  }
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  void sendContextCommand(info, tab);
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" && tab.url) {
    void injectInstalledWebApps(tabId, tab.url);
  }
});

async function getLocalWebAppRecord(appId, key) {
  const storageKey = webAppDataStorageKey(appId, key);
  const stored = await chrome.storage.local.get(storageKey);
  return stored[storageKey] || null;
}

async function writeLocalWebAppRecord(appId, key, record) {
  const storageKey = webAppDataStorageKey(appId, key);
  await chrome.storage.local.set({ [storageKey]: record });
}

async function getWebAppData(appId, key, fallback = null) {
  const record = await getLocalWebAppRecord(appId, key);
  return record ? record.value : fallback;
}

async function setWebAppData(appId, key, value, options = {}) {
  const previous = await getLocalWebAppRecord(appId, key);
  const record = {
    value,
    updatedAt: Date.now(),
    dirty: true,
    revision: previous?.revision || 0
  };
  await writeLocalWebAppRecord(appId, key, record);

  if (options.syncNow !== false && globalThis.yanziBrowserHost?.isConnected?.()) {
    void pushWebAppRecordToCloud(appId, key, record);
  }
  return record;
}

function handleYanziWebAppHostMessage(message) {
  if (message?.type !== "webapp_storage_response" || !message.requestId) return false;
  const pending = yanziWebAppHostRequests.get(message.requestId);
  if (!pending) return true;
  clearTimeout(pending.timer);
  yanziWebAppHostRequests.delete(message.requestId);
  pending.resolve(message);
  return true;
}

globalThis.handleYanziWebAppHostMessage = handleYanziWebAppHostMessage;

function requestYanziWebAppHost(payload) {
  if (!globalThis.yanziBrowserHost?.isConnected?.()) {
    return Promise.resolve({ ok: false, available: false, error: "desktop_not_connected" });
  }

  const requestId = crypto.randomUUID();
  return new Promise(resolve => {
    const timer = setTimeout(() => {
      yanziWebAppHostRequests.delete(requestId);
      resolve({ ok: false, available: false, error: "timeout" });
    }, YANZI_WEBAPP_HOST_TIMEOUT_MS);

    yanziWebAppHostRequests.set(requestId, { resolve, timer });
    globalThis.yanziBrowserHost.send({ ...payload, requestId });
  });
}

async function pushWebAppRecordToCloud(appId, key, record) {
  const content = JSON.stringify({
    value: record.value,
    updatedAt: record.updatedAt
  });
  const response = await requestYanziWebAppHost({
    type: "webapp_storage_put",
    appId,
    key,
    content
  });
  if (!response?.ok || !response.available) return false;

  const latest = await getLocalWebAppRecord(appId, key);
  if (!latest || latest.updatedAt !== record.updatedAt) return true;
  await writeLocalWebAppRecord(appId, key, {
    ...latest,
    dirty: false,
    revision: response.revision || latest.revision || 0
  });
  return true;
}

async function reconcileWebAppRecord(appId, key) {
  const local = await getLocalWebAppRecord(appId, key);
  const response = await requestYanziWebAppHost({
    type: "webapp_storage_get",
    appId,
    key
  });
  if (!response?.ok || !response.available) return;

  let cloud = null;
  if (response.exists && response.content) {
    try {
      cloud = JSON.parse(response.content);
    } catch {
      cloud = { value: null, updatedAt: 0 };
    }
  }

  if (local?.dirty && (!cloud || local.updatedAt >= Number(cloud.updatedAt || 0))) {
    await pushWebAppRecordToCloud(appId, key, local);
    return;
  }

  if (cloud && Number(cloud.updatedAt || 0) > Number(local?.updatedAt || 0)) {
    await writeLocalWebAppRecord(appId, key, {
      value: cloud.value,
      updatedAt: Number(cloud.updatedAt || Date.now()),
      dirty: false,
      revision: response.revision || 0
    });

    if (appId === YANZI_WEBAPP_MARKET_APP_ID && key === YANZI_WEBAPP_MARKET_KEY && cloud.value) {
      await chrome.storage.local.set({ [YANZI_WEBAPP_INSTALL_KEY]: cloud.value });
      await refreshYanziWebAppContextMenus();
      await applyInstalledWebAppState(cloud.value);
    }

    await broadcastWebAppDataChanged(appId, key, cloud.value);
  } else if (local && !local.dirty && response.revision && local.revision !== response.revision) {
    await writeLocalWebAppRecord(appId, key, { ...local, revision: response.revision });
  }
}

async function reconcileAllWebAppData() {
  if (!globalThis.yanziBrowserHost?.isConnected?.()) return;
  await reconcileWebAppRecord(YANZI_WEBAPP_MARKET_APP_ID, YANZI_WEBAPP_MARKET_KEY);

  const catalog = await loadYanziWebAppCatalog();
  const installed = await getInstalledWebApps();
  const scheduled = new Set();
  const tasks = [];

  for (const app of catalog.apps || []) {
    if (!installed[app.id]) continue;
    for (const key of app.syncKeys || []) {
      const identity = `${app.id}::${key}`;
      scheduled.add(identity);
      tasks.push(reconcileWebAppRecord(app.id, key));
    }
  }

  const all = await chrome.storage.local.get(null);
  for (const storageKey of Object.keys(all)) {
    if (!storageKey.startsWith(YANZI_WEBAPP_DATA_PREFIX)) continue;
    const remainder = storageKey.slice(YANZI_WEBAPP_DATA_PREFIX.length);
    const separator = remainder.indexOf("::");
    if (separator <= 0) continue;
    const appId = remainder.slice(0, separator);
    const key = remainder.slice(separator + 2);
    if (appId === YANZI_WEBAPP_MARKET_APP_ID && key === YANZI_WEBAPP_MARKET_KEY) continue;
    const identity = `${appId}::${key}`;
    if (scheduled.has(identity)) continue;
    scheduled.add(identity);
    tasks.push(reconcileWebAppRecord(appId, key));
  }
  await Promise.allSettled(tasks);
}

async function broadcastWebAppDataChanged(appId, key, value) {
  const catalog = await loadYanziWebAppCatalog();
  const app = (catalog.apps || []).find(item => item.id === appId);
  if (!app) return;
  const tabs = await chrome.tabs.query({});
  for (const tab of tabs) {
    if (!tab.id || !tab.url || !(app.matches || []).some(pattern => matchPattern(tab.url, pattern))) continue;
    chrome.tabs.sendMessage(tab.id, {
      type: "yanzi_webapp_data_changed",
      appId,
      key,
      value
    }).catch(() => {});
  }
}

globalThis.yanziWebAppsOnConnectionChanged = connected => {
  if (connected) void reconcileAllWebAppData();
};

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "yanzi_webapp_market_list") {
    listMarketApps()
      .then(apps => sendResponse({ ok: true, apps }))
      .catch(error => sendResponse({ ok: false, error: error?.message || String(error) }));
    return true;
  }

  if (message?.type === "yanzi_webapp_set_installed") {
    setWebAppInstalled(message.appId, Boolean(message.installed))
      .then(() => listMarketApps())
      .then(apps => sendResponse({ ok: true, apps }))
      .catch(error => sendResponse({ ok: false, error: error?.message || String(error) }));
    return true;
  }

  if (message?.type === "yanzi_webapp_storage_get") {
    getWebAppData(message.appId, message.key, message.fallback ?? null)
      .then(value => sendResponse({ ok: true, value }))
      .catch(error => sendResponse({ ok: false, error: error?.message || String(error) }));
    return true;
  }

  if (message?.type === "yanzi_webapp_storage_set") {
    setWebAppData(
      message.appId,
      message.key,
      message.value,
      { syncNow: message.syncNow !== false }
    )
      .then(record => {
        void broadcastWebAppDataChanged(message.appId, message.key, message.value);
        sendResponse({ ok: true, updatedAt: record.updatedAt });
      })
      .catch(error => sendResponse({ ok: false, error: error?.message || String(error) }));
    return true;
  }

  if (message?.type === "yanzi_webapp_storage_sync_key") {
    reconcileWebAppRecord(message.appId, message.key)
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: error?.message || String(error) }));
    return true;
  }

  if (message?.type === "yanzi_webapp_sync_now") {
    reconcileAllWebAppData()
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: error?.message || String(error) }));
    return true;
  }
});

async function refreshWebAppTabsAfterExtensionReload() {
  const stored = await chrome.storage.local.get("yanziPendingExtensionReloadRefresh");
  if (!stored.yanziPendingExtensionReloadRefresh) return false;

  // Clear first so tab reloads cannot form a loop if the service worker restarts again.
  await chrome.storage.local.remove("yanziPendingExtensionReloadRefresh");

  const apps = await listMarketApps();
  const installedApps = apps.filter(app => app.installed);
  const tabs = await chrome.tabs.query({});
  const tabIds = new Set();

  for (const tab of tabs) {
    if (!tab.id || !tab.url) continue;
    if (installedApps.some(app =>
      (app.matches || []).some(pattern => matchPattern(tab.url, pattern))
    )) {
      tabIds.add(tab.id);
    }
  }

  for (const tabId of tabIds) {
    try {
      await chrome.tabs.reload(tabId);
    } catch (error) {
      globalThis.yanziBrowserHost?.log?.(
        `[网页小程序] 开发重载后刷新 Tab ${tabId} 失败: ${error?.message || error}`
      );
    }
  }

  globalThis.yanziBrowserHost?.log?.(
    `[网页小程序] 扩展自重载完成，已刷新 ${tabIds.size} 个网页小程序标签页。`
  );
  return tabIds.size;
}

async function initializeYanziWebApps() {
  await getInstalledWebApps();
  await refreshYanziWebAppContextMenus();

  const refreshedTabCount = await refreshWebAppTabsAfterExtensionReload();
  if (!refreshedTabCount) {
    const tabs = await chrome.tabs.query({});
    for (const tab of tabs) {
      if (tab.id && tab.url) await injectInstalledWebApps(tab.id, tab.url);
    }
  }

  globalThis.yanziBrowserHost?.send?.({
    type: "browser_extension_runtime_ready",
    version: chrome.runtime.getManifest().version,
    refreshedWebAppTabs: refreshedTabCount
  });

  if (globalThis.yanziBrowserHost?.isConnected?.()) {
    void reconcileAllWebAppData();
  }
}

void initializeYanziWebApps().catch(error => {
  globalThis.yanziBrowserHost?.log?.(`[网页小程序] 初始化失败: ${error?.message || error}`);
});
