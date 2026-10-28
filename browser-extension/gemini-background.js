// Gemini background runner and tab manager for Yanzi Browser Helper.
const GEMINI_MAX_CONCURRENT_TASKS = 4;
let geminiActiveTaskCount = 0;
const geminiExecutionWaiters = [];
const geminiReservedTabs = new Set();
const geminiTabWaiters = new Map();
const geminiOperations = new Set([
  "gemini_status",
  "gemini_new_chat",
  "gemini_model_switch",
  "gemini_send",
  "gemini_messages",
  "gemini_diagnostics",
  "gemini_network_probe",
  "gemini_close",
  "gemini_cleanup"
]);

async function acquireGeminiExecutionSlot(timeoutMs = 45000) {
  if (geminiActiveTaskCount < GEMINI_MAX_CONCURRENT_TASKS) {
    geminiActiveTaskCount += 1;
  } else {
    let timer;
    let settled = false;
    await new Promise((resolve, reject) => {
      const onSlot = () => {
        if (settled) {
          const next = geminiExecutionWaiters.shift();
          if (next) next();
          else geminiActiveTaskCount = Math.max(0, geminiActiveTaskCount - 1);
          return;
        }
        settled = true;
        if (timer) clearTimeout(timer);
        resolve();
      };
      geminiExecutionWaiters.push(onSlot);
      timer = setTimeout(() => {
        if (settled) return;
        settled = true;
        const idx = geminiExecutionWaiters.indexOf(onSlot);
        if (idx !== -1) geminiExecutionWaiters.splice(idx, 1);
        reject(new Error("等待 Gemini 并发执行槽位超时"));
      }, timeoutMs);
    });
  }

  let released = false;
  return () => {
    if (released) return;
    released = true;

    const next = geminiExecutionWaiters.shift();
    if (next) {
      next();
      return;
    }
    geminiActiveTaskCount = Math.max(0, geminiActiveTaskCount - 1);
  };
}

function tryReserveGeminiTab(tabId) {
  const id = Number(tabId || 0);
  if (!id || geminiReservedTabs.has(id)) return false;
  geminiReservedTabs.add(id);
  return true;
}

async function reserveGeminiTab(tabId, timeoutMs = 45000) {
  const id = Number(tabId || 0);
  if (!id) throw new Error("tabId 无效");
  if (tryReserveGeminiTab(id)) return;

  let timer;
  let settled = false;
  return new Promise((resolve, reject) => {
    const onGranted = () => {
      if (settled) {
        releaseGeminiTab(id);
        return;
      }
      settled = true;
      if (timer) clearTimeout(timer);
      resolve();
    };

    const waiters = geminiTabWaiters.get(id) || [];
    waiters.push(onGranted);
    geminiTabWaiters.set(id, waiters);

    timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      const currentWaiters = geminiTabWaiters.get(id) || [];
      const idx = currentWaiters.indexOf(onGranted);
      if (idx !== -1) currentWaiters.splice(idx, 1);
      if (!currentWaiters.length) geminiTabWaiters.delete(id);
      reject(new Error(`等待标签页 [TabID: ${id}] 锁超时`));
    }, timeoutMs);
  });
}

function releaseGeminiTab(tabId) {
  const id = Number(tabId || 0);
  if (!id) return;

  const waiters = geminiTabWaiters.get(id);
  const next = waiters?.shift();

  if (waiters && !waiters.length) {
    geminiTabWaiters.delete(id);
  }

  if (next) {
    next();
    return;
  }
  geminiReservedTabs.delete(id);
}

const geminiManagedTabsKey = 'geminiManagedTabs';
let geminiManagedTabsMutation = Promise.resolve();

async function mutateGeminiManagedTabs(mutator) {
  let release;
  const currentLock = new Promise(resolve => { release = resolve; });
  const previous = geminiManagedTabsMutation;
  geminiManagedTabsMutation = currentLock;

  try {
    await previous.catch(() => {});
    const stored = (await chrome.storage.session.get(geminiManagedTabsKey))[geminiManagedTabsKey] || {};
    const managed = { ...stored };
    const value = await mutator(managed);
    await chrome.storage.session.set({ [geminiManagedTabsKey]: managed });
    return value === undefined ? managed : value;
  } finally {
    release?.();
  }
}

async function readGeminiManagedTabs() {
  await geminiManagedTabsMutation.catch(() => {});
  return {
    ...((await chrome.storage.session.get(geminiManagedTabsKey))[geminiManagedTabsKey] || {})
  };
}

function sameGeminiAccountRoute(recordedUrl, tabUrl) {
  try {
    const previous = new URL(recordedUrl);
    const current = new URL(tabUrl);
    if (previous.protocol !== "https:" || current.protocol !== "https:" ||
        previous.hostname !== "gemini.google.com" || current.hostname !== "gemini.google.com") return false;
    const route = url => {
      const prefix = url.pathname.match(/^\/u\/(\d+)\/app(?:\/|$)/);
      if (prefix) return "u/" + prefix[1];
      return /^\/app(?:\/|$)/.test(url.pathname) ? "default" : "";
    };
    return Boolean(route(previous)) && route(previous) === route(current);
  } catch { return false; }
}

async function inspectGeminiTab(tab) {
  if (tab.active || tab.pinned || tab.discarded || tab.status !== 'complete') return false;
  try {
    const [probe] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        const input = document.querySelector('rich-textarea [contenteditable="true"], textarea');
        const draft = (input?.innerText || input?.textContent || input?.value || '').trim();
        const stop = document.querySelector('button[aria-label*="停止" i], button[aria-label*="Stop" i], [data-test-id="stop-button"]');
        return Boolean(input) && !draft && !stop;
      }
    });
    return probe?.result === true;
  } catch {
    return false;
  }
}

async function waitGeminiTab(tabId, timeout = 30000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const tab = await chrome.tabs.get(tabId);
    if (tab.status === "complete" && !tab.discarded) return tab;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error("Gemini 标签页加载超时");
}

function resolveGeminiAppUrl(allTabs, existingUrl) {
  if (typeof existingUrl === 'string') {
    const match = existingUrl.match(/https:\/\/gemini\.google\.com(\/u\/\d+)/i);
    if (match) return `https://gemini.google.com${match[1]}/app`;
  }
  for (const t of allTabs || []) {
    const match = (t?.url || '').match(/https:\/\/gemini\.google\.com(\/u\/\d+)/i);
    if (match) return `https://gemini.google.com${match[1]}/app`;
  }
  return "https://gemini.google.com/app";
}

async function withGeminiPageScriptTimeout(operation, waitMs = 12000) {
  let timeoutId;
  try {
    return await Promise.race([
      Promise.resolve().then(operation),
      new Promise((_, reject) => {
        timeoutId = setTimeout(() => reject(new Error("Gemini 页面渲染进程无响应")), waitMs);
      })
    ]);
  } finally {
    if (timeoutId !== undefined) clearTimeout(timeoutId);
  }
}

async function ensureTabAwake(tabId) {
  try {
    const tab = await chrome.tabs.get(tabId);
    if (tab.discarded) {
      await chrome.tabs.reload(tabId);
      await waitGeminiTab(tabId);
      return;
    }
    await withGeminiPageScriptTimeout(() => chrome.scripting.executeScript({
      target: { tabId },
      func: () => document.readyState
    }), 2500);
  } catch {
    await chrome.tabs.reload(tabId);
    await waitGeminiTab(tabId);
  }
}

async function recoverGeminiPageAfterChannelDrop(tabId, prompt, { maxWaitMs = 120000 } = {}) {
  const normalize = text => String(text || "").replace(/\s+/g, " ").trim();
  const expected = normalize(prompt);
  if (!Number.isInteger(tabId) || !expected) return null;
  const deadline = Date.now() + maxWaitMs;
  let stableText = "";
  let stableSince = 0;

  while (Date.now() < deadline) {
    try {
      const tab = await chrome.tabs.get(tabId);
      if (!tab || tab.discarded) return null;

      const [reading] = await withGeminiPageScriptTimeout(() => chrome.scripting.executeScript({
        target: { tabId },
        func: () => {
          const fn = window.__yanziGeminiGetMessages;
          const isGen = window.__yanziGeminiIsGenerating ? window.__yanziGeminiIsGenerating() : false;
          const meta = window.__yanziGeminiMeta ? window.__yanziGeminiMeta() : {};
          if (typeof fn !== "function") return null;
          const msgs = fn(false);
          const lastUserIndex = msgs.findLastIndex(m => m.role === "user");
          const reply = lastUserIndex >= 0 ? msgs.slice(lastUserIndex + 1).filter(m => m.role === "assistant").at(-1) : null;
          return {
            meta,
            isGenerating: isGen,
            lastUserText: lastUserIndex >= 0 ? msgs[lastUserIndex].text : null,
            reply: reply || null
          };
        }
      }), 12000);

      const snapshot = reading?.result;
      if (snapshot && normalize(snapshot.lastUserText) === expected && snapshot.reply && !snapshot.isGenerating) {
        const text = (snapshot.reply.text || "").trim();
        if (text && text === stableText) {
          if (Date.now() - stableSince >= 2000) {
            return {
              status: "success",
              data: {
                ...snapshot.meta,
                ...snapshot.reply,
                tabId,
                recoveredFromChannelDrop: true
              }
            };
          }
        } else if (text) {
          stableText = text;
          stableSince = Date.now();
        }
      }
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 1500));
  }
  return null;
}

async function runGeminiTask(task) {
  const result = {
    type: "gemini_task_response",
    taskId: task.taskId || `gemini-task-${crypto.randomUUID()}`,
    status: "error",
    data: null
  };

  const needsExecutionSlot = ![
    "gemini_status",
    "gemini_diagnostics",
    "gemini_messages",
    "gemini_close",
    "gemini_cleanup"
  ].includes(task.action);

  const releaseExecutionSlot = needsExecutionSlot
    ? await acquireGeminiExecutionSlot()
    : null;

  let reservedTabId = null;

  try {
    if (!geminiOperations.has(task.action)) {
      throw new Error("不支持的 Gemini 操作: " + task.action);
    }

    let tabs = await chrome.tabs.query({ url: "*://gemini.google.com/*" });

    if (task.action === "gemini_status") {
      let managedMap = {};
      try { managedMap = await readGeminiManagedTabs(); } catch {}
      const owns = tab => Boolean(managedMap[tab.id] && sameGeminiAccountRoute(managedMap[tab.id].url, tab.url));
      const canClose = tab => owns(tab) && !tab.active && !tab.pinned;

      return {
        ...result,
        status: "success",
        data: {
          exists: tabs.length > 0,
          extensionVersion: chrome.runtime.getManifest().version,
          concurrency: {
            active: geminiActiveTaskCount,
            queued: geminiExecutionWaiters.length,
            max: GEMINI_MAX_CONCURRENT_TASKS
          },
          tabs: tabs.map(tab => ({
            tabId: tab.id,
            url: tab.url,
            active: tab.active,
            discarded: tab.discarded,
            managed: owns(tab),
            reserved: geminiReservedTabs.has(tab.id),
            canClose: canClose(tab)
          }))
        }
      };
    }

    let managed = await mutateGeminiManagedTabs(current => {
      for (const [id, record] of Object.entries(current)) {
        const tabId = Number(id);
        if (geminiReservedTabs.has(tabId)) continue;
        const tab = tabs.find(item => item.id === tabId);
        if (!tab || !sameGeminiAccountRoute(record.url, tab.url)) delete current[id];
      }
      return { ...current };
    });

    const refreshManaged = async () => {
      managed = await readGeminiManagedTabs();
      return managed;
    };

    const owns = tab => Boolean(managed[tab.id] && sameGeminiAccountRoute(managed[tab.id].url, tab.url));
    const canClose = tab => owns(tab) && !tab.active && !tab.pinned;

    const targetTabId = task.tabId != null ? Number(task.tabId) : null;

    if (task.action === "gemini_new_chat") {
      const target = targetTabId != null ? tabs.find(item => item.id === targetTabId) : tabs[0];
      if (!target) throw new Error("没有找到打开的 Gemini 标签页");
      const appUrl = resolveGeminiAppUrl(tabs, target.url);
      await chrome.tabs.update(target.id, { url: appUrl });
      await waitGeminiTab(target.id);
      return {
        ...result,
        status: "success",
        data: { tabId: target.id, url: appUrl }
      };
    }

    if (task.action === "gemini_diagnostics" || task.action === "gemini_messages") {
      const target = targetTabId != null ? tabs.find(item => item.id === targetTabId) : tabs[0];
      if (!target) throw new Error("没有找到打开的 Gemini 标签页");
      if (!target.url || !target.url.includes("/app")) {
        const preferredUrl = resolveGeminiAppUrl(tabs, target.url);
        await chrome.tabs.update(target.id, { url: preferredUrl });
        await waitGeminiTab(target.id);
      }
      await ensureTabAwake(target.id);
      await withGeminiPageScriptTimeout(() => chrome.scripting.executeScript({
        target: { tabId: target.id },
        files: ["gemini-content.js"]
      }), 12000);
      const response = await chrome.tabs.sendMessage(target.id, {
        type: "yanzi_gemini_task",
        task
      });
      return {
        ...result,
        ...response,
        data: {
          ...(response?.data || response),
          tabId: target.id,
          lifecycle: {
            created: false,
            reused: false,
            managed: owns(target),
            closed: false,
            closeSkipped: null
          }
        }
      };
    }

    if (task.action === "gemini_close" || task.action === "gemini_cleanup") {
      const candidates = task.action === "gemini_close"
        ? tabs.filter(tab => tab.id === task.tabId)
        : tabs.filter(owns);

      if (task.action === "gemini_close" && task.tabId == null) {
        throw new Error("关闭标签页必须指定 tabId");
      }

      const closed = [];
      const skipped = [];
      for (const candidate of candidates) {
        if (geminiReservedTabs.has(candidate.id)) {
          skipped.push(candidate.id);
          continue;
        }
        const current = await chrome.tabs.get(candidate.id);
        if (!canClose(current) || !(await inspectGeminiTab(current))) {
          skipped.push(candidate.id);
          continue;
        }
        await chrome.tabs.remove(candidate.id);
        await mutateGeminiManagedTabs(map => { delete map[candidate.id]; });
        delete managed[candidate.id];
        closed.push(candidate.id);
      }

      return {
        ...result,
        status: "success",
        data: { closedTabIds: closed, skippedTabIds: skipped }
      };
    }

    // Determine target tab
    let tab = targetTabId != null ? tabs.find(item => item.id === targetTabId) : null;
    let created = false;
    let reused = false;

    if (tab) {
      await reserveGeminiTab(tab.id);
      reservedTabId = tab.id;
    } else {
      const existingCandidate = tabs.find(t => !geminiReservedTabs.has(t.id));
      const preferredUrl = resolveGeminiAppUrl(tabs, task.targetUrl || existingCandidate?.url);

      if (task.tabPolicy !== "new" && existingCandidate) {
        tab = existingCandidate;
        await reserveGeminiTab(tab.id);
        reservedTabId = tab.id;
        reused = true;
      } else {
        tab = await chrome.tabs.create({ url: preferredUrl, active: false });
        created = true;
        await reserveGeminiTab(tab.id);
        reservedTabId = tab.id;
        await mutateGeminiManagedTabs(map => { map[tab.id] = { url: preferredUrl }; });
        await refreshManaged();
      }
    }

    const currentTab = await chrome.tabs.get(tab.id);
    if (!currentTab.url || !currentTab.url.includes("/app")) {
      const preferredUrl = resolveGeminiAppUrl(tabs, currentTab.url);
      await chrome.tabs.update(tab.id, { url: preferredUrl });
      await waitGeminiTab(tab.id);
    }

    await ensureTabAwake(tab.id);

    // Inject Content Script
    await withGeminiPageScriptTimeout(() => chrome.scripting.executeScript({
      target: { tabId: tab.id },
      files: ["gemini-content.js"]
    }), 12000);

    // Send task to tab
    let response;
    try {
      response = await chrome.tabs.sendMessage(tab.id, {
        type: "yanzi_gemini_task",
        task
      });
    } catch (error) {
      if (task.action !== "gemini_send" ||
          !/message channel closed before a response|message port closed before a response|Extension context invalidated/i.test(error?.message || "")) {
        throw error;
      }
      const recovered = await recoverGeminiPageAfterChannelDrop(tab.id, task.prompt);
      if (!recovered) {
        return {
          ...result,
          data: { tabId: tab.id, deliveryUncertain: true },
          message: "Gemini 页面异步消息通道中断，原消息可能已发送；只读核对未取得最终回复，已保留原标签页，请核查后再重试"
        };
      }
      response = recovered;
    }

    if (!response) {
      throw new Error("Gemini 页面未返回任何结果");
    }

    const current = await chrome.tabs.get(tab.id);
    let closed = false;
    let closeSkipped = null;

    if (task.closeAfter && response.status === "success") {
      if (canClose(current) && await inspectGeminiTab(current)) {
        await chrome.tabs.remove(tab.id);
        await mutateGeminiManagedTabs(map => { delete map[tab.id]; });
        closed = true;
      } else {
        closeSkipped = "页面不是空闲的 Gemini 工作标签页，已保留";
      }
    }

    return {
      ...result,
      ...response,
      data: {
        ...response.data,
        tabId: tab.id,
        lifecycle: {
          created,
          reused,
          managed: owns(current),
          closed,
          closeSkipped
        }
      }
    };
  } catch (error) {
    return {
      ...result,
      message: error?.message || String(error)
    };
  } finally {
    if (reservedTabId) {
      releaseGeminiTab(reservedTabId);
    }
    releaseExecutionSlot?.();
  }
}

// 扩展内监听
chrome.runtime?.onMessage?.addListener?.((message, sender, sendResponse) => {
  if (message?.type === "yanzi_gemini_editor_input") {
    const url = sender?.tab?.url || "";
    const prompt = message?.prompt;
    if (!/^https:\/\/gemini\.google\.com(?:\/|$)/.test(url) || !sender?.tab?.id ||
        typeof prompt !== "string" || !prompt.trim() || prompt.length > 32000) {
      sendResponse({ accepted: false, reason: "invalid Gemini tab or prompt" });
      return false;
    }
    chrome.scripting.executeScript({
      target: { tabId: sender.tab.id },
      world: "MAIN",
      args: [prompt],
      func: text => {
        const input = document.querySelector('rich-textarea .ql-editor[contenteditable="true"], rich-textarea [contenteditable="true"]');
        if (!input) return { accepted: false, reason: "editor missing" };
        const quill = input.__quill || input.closest('.ql-container')?.__quill ||
          (typeof window.Quill?.find === 'function' ? window.Quill.find(input) : null);
        if (quill && typeof quill.setText === 'function') {
          quill.setText(text, 'user');
          quill.setSelection(quill.getLength(), 0, 'silent');
          return { accepted: (input.innerText || '').trim() === text.trim(), engine: "quill" };
        }
        input.focus();
        const range = document.createRange();
        range.selectNodeContents(input);
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
        const inserted = document.execCommand('insertText', false, text);
        return { accepted: inserted && (input.innerText || '').trim() === text.trim(), engine: "main-world-native" };
      }
    }).then(result => sendResponse(result?.[0]?.result || { accepted: false, reason: "no result" }))
      .catch(error => sendResponse({ accepted: false, reason: error?.message || String(error) }));
    return true;
  }
  if (message?.type === "yanzi_gemini_task") {
    runGeminiTask(message.task)
      .then(res => sendResponse(res))
      .catch(err => sendResponse({ status: "error", message: err?.message || String(err) }));
    return true;
  }
});

// 右键菜单
const GEMINI_FEEDBACK_ORIGIN_MENU = "yanzi-gemini-bind-feedback-origin";
function installGeminiFeedbackOriginMenu() {
  chrome.contextMenus?.remove?.(GEMINI_FEEDBACK_ORIGIN_MENU, () => {
    void chrome.runtime.lastError;
    chrome.contextMenus?.create?.({
      id: GEMINI_FEEDBACK_ORIGIN_MENU,
      title: "燕子：将此 Gemini 对话设为反馈回传目标",
      contexts: ["page"],
      documentUrlPatterns: ["*://gemini.google.com/*"]
    }, () => { void chrome.runtime.lastError; });
  });
}
chrome.runtime.onInstalled.addListener(installGeminiFeedbackOriginMenu);
installGeminiFeedbackOriginMenu();

globalThis.yanziGeminiBackground = {
  runGeminiTask
};
