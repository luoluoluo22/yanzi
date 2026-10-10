importScripts("lanzou-network-observer.js");
let ws = null;
let currentWsSeq = 0;
let reconnectDelay = 1000;
const maxReconnectDelay = 30000;
let isConnected = false;
let reconnectTimer = null;
const LOG_BATCH_INTERVAL_MS = 3000;
const HEARTBEAT_INTERVAL_MS = 20000;
const PONG_TIMEOUT_MS = 45000;
let lastPongAt = Date.now();
let pendingLogs = [];
let logFlushTimer = null;
let localWsUrl = "ws://127.0.0.1:53919/v1/browser/ws";

// 辅助函数：更新连接状态到本地存储，供 popup 读取
function updateStatus(status) {
  isConnected = (status === "connected");
  chrome.storage.local.set({ connectionStatus: status });
  logEvent(`连接状态更新为: ${status}`);
  globalThis.yanziWebAppsOnConnectionChanged?.(isConnected);
}

// 辅助函数：记录任务日志到本地存储
function logEvent(message) {
  const timestamp = new Date().toLocaleTimeString();
  const logMessage = `[${timestamp}] ${message}`;
  console.log(logMessage);

  pendingLogs.push(logMessage);
  if (!logFlushTimer) {
    logFlushTimer = setTimeout(flushLogs, LOG_BATCH_INTERVAL_MS);
  }
}

function flushLogs() {
  logFlushTimer = null;
  if (!pendingLogs.length) {
    return;
  }

  const batch = pendingLogs.splice(0, pendingLogs.length);
  chrome.storage.local.get({ logs: [] }, (result) => {
    const logs = result.logs.concat(batch).slice(-50);
    chrome.storage.local.set({ logs });
  });
}

// 连接本地 WebSocket 服务 (带严格的单实例自增序号锁，杜绝重连级联死循环)
function connectWebSocket(reason = "auto") {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
    return;
  }

  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }

  const thisSeq = ++currentWsSeq;
  logEvent(`尝试连接到燕子桌面端本地服务 (Seq #${thisSeq}, 原因: ${reason})...`);
  
  try {
    const socket = new WebSocket(localWsUrl);
    ws = socket;
    
    socket.onopen = () => {
      if (thisSeq !== currentWsSeq || socket !== ws) {
        try { socket.close(); } catch(e){}
        return;
      }
      lastPongAt = Date.now();
      updateStatus("connected");
      reconnectDelay = 1000; // 重连成功，重置延迟
      if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      
      // 发送握手注册消息
      socket.send(JSON.stringify({
        type: "register",
        client: "yanzi-extension"
      }));
    };
    
    socket.onmessage = (event) => {
      if (thisSeq !== currentWsSeq) return;
      try {
        const message = JSON.parse(event.data);
        if (message.type === "pong") {
          lastPongAt = Date.now();
          return;
        }
        if (message.type === "webapp_storage_response") {
          globalThis.handleYanziWebAppHostMessage?.(message);
          return;
        }
        if (message.type === "browser_extension_reload") {
          const requestId = message.requestId || "";
          logEvent(`收到扩展自重载请求: ${requestId || "no-request-id"}`);
          try {
            socket.send(JSON.stringify({
              type: "browser_extension_reload_ack",
              requestId,
              version: chrome.runtime.getManifest().version
            }));
          } catch (ackError) {
            logEvent(`扩展自重载 ACK 发送失败: ${ackError.message}`);
          }
          chrome.storage.local.set({
            lastExtensionReloadRequestedAt: Date.now(),
            yanziPendingExtensionReloadRefresh: true
          }).finally(() => {
            setTimeout(() => chrome.runtime.reload(), 80);
          });
          return;
        }
        logEvent(`收到来自燕子的指令: ${message.action || message.type}`);
        
        if (message.type === "task_request") {
          handleTask(message);
        }
      } catch (err) {
        logEvent(`解析消息失败: ${err.message}`);
      }
    };
    
    socket.onclose = (event) => {
      if (thisSeq !== currentWsSeq) {
        // 过期的旧 socket 关闭，静默丢弃，绝不触发级联重连
        return;
      }
      ws = null;
      updateStatus("disconnected");
      scheduleReconnect(`socket_closed_code_${event.code}`);
    };
    
    socket.onerror = (err) => {
      if (thisSeq !== currentWsSeq) return;
      console.warn("WebSocket 报错:", err);
    };
  } catch (err) {
    logEvent(`创建 WebSocket 异常: ${err.message}`);
    scheduleReconnect("create_exception");
  }
}

function forceReconnect(reason = "forced") {
  const oldWs = ws;
  ws = null;
  currentWsSeq++;
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  try { oldWs?.close(); } catch {}
  reconnectDelay = 1000;
  updateStatus("disconnected");
  connectWebSocket(reason);
}

function heartbeat(reason = "heartbeat") {
  if (ws && ws.readyState === WebSocket.OPEN) {
    const age = Date.now() - lastPongAt;
    if (age > PONG_TIMEOUT_MS) {
      logEvent(`心跳超时 ${Math.round(age / 1000)}s，强制重建连接 (${reason})`);
      forceReconnect("pong_timeout");
      return;
    }
    try {
      ws.send(JSON.stringify({ type: "ping" }));
    } catch (error) {
      logEvent(`心跳发送失败，强制重连: ${error?.message || error}`);
      forceReconnect("heartbeat_send_failed");
    }
    return;
  }

  if (!reconnectTimer) connectWebSocket(reason);
}

// 自动重连逻辑 (指数退避)
function scheduleReconnect(reason = "unknown") {
  if (reconnectTimer) {
    return;
  }

  logEvent(`连接已断开 (${reason})，将在 ${reconnectDelay / 1000} 秒后尝试重新连接...`);
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectWebSocket("reconnect_timer");
    reconnectDelay = Math.min(reconnectDelay * 2, maxReconnectDelay);
  }, reconnectDelay);
}

// 向本地燕子服务发送消息
function sendToLocalClient(message) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify(message));
  } else {
    logEvent("发送失败：WebSocket 未连接");
  }
}

// 核心任务处理逻辑
function handleTask(task) {
  const targetUrl = task.url || (task.action === "ai_prompt_transfer" ? "https://chat.deepseek.com/" : "");
  logEvent(`开始执行任务 [${task.taskId}]，动作: ${task.action}, 目标网址: ${targetUrl}`);
  
  if (task.action === "ai_prompt_transfer") {
    handleAiPromptTransferTask(task, targetUrl);
    return;
  }

  if (task.action === "lanzou_network_control") {
    void globalThis.handleLanzouNetworkControl(task)
      .then(data => sendBrowserTaskResponse(task, "success", data))
      .catch(error => sendBrowserTaskResponse(task, "error", null, error?.message || "network observer failed"));
    return;
  }

  if (task.action === "probe_webapps") {
    void handleWebAppProbeTask(task);
    return;
  }

  if (task.action === "webapp_data_get" ||
      task.action === "webapp_data_set" ||
      task.action === "xiaohongshu_custom_card_list" ||
      task.action === "xiaohongshu_custom_card_upsert" ||
      task.action === "xiaohongshu_custom_card_remove" ||
      task.action === "xiaohongshu_custom_card_clear") {
    void handleWebAppDataTask(task);
    return;
  }

  // 普通自动化任务：静默创建后台 Tab 页 (active: false)
  chrome.tabs.create({ url: targetUrl, active: false }, (tab) => {
    const tabId = tab.id;
    
    // 监听页面加载状态
    chrome.tabs.onUpdated.addListener(function listener(updatedTabId, info) {
      if (updatedTabId === tabId && info.status === "complete") {
        chrome.tabs.onUpdated.removeListener(listener);
        logEvent(`网页加载完成，开始注入执行脚本 [TabID: ${tabId}]`);
        injectAndStart(tabId, task);
      }
    });
  });
}

function withProbeTimeout(promise, timeoutMs, fallback) {
  return Promise.race([
    Promise.resolve(promise),
    new Promise(resolve => setTimeout(() => resolve(fallback), timeoutMs))
  ]);
}

async function handleWebAppProbeTask(task) {
  try {
    const appId = task.appId || "xiaohongshu.filter";
    const catalog = await loadYanziWebAppCatalog?.();
    const app = (catalog?.apps || []).find(item => item.id === appId);
    if (!app) throw new Error(`unknown webapp: ${appId}`);

    const allTabs = await chrome.tabs.query({});
    const tabs = allTabs.filter(tab =>
      tab.id &&
      tab.url &&
      (app.matches || []).some(pattern => matchPattern(tab.url, pattern))
    );
    const results = [];

    for (const tab of tabs) {
      const injection = await withProbeTimeout(
        (async () => {
          try {
            await ensureWebAppInjected?.(tab.id, appId);
            return { injected: true };
          } catch (error) {
            return { injected: false, injectError: error?.message || String(error) };
          }
        })(),
        10000,
        { injected: false, injectError: "inject_timeout" }
      );

      const response = await withProbeTimeout(
        new Promise(resolve => {
          chrome.tabs.sendMessage(tab.id, {
            type: "yanzi_webapp_probe",
            appId,
            debugLayoutTest: task.debugLayoutTest || null,
            debugInterestTest: task.debugInterestTest || null,
            debugSelectionTest: task.debugSelectionTest || null,
            debugLocalFeedTest: task.debugLocalFeedTest || null,
            debugSkipTest: task.debugSkipTest || null,
            debugLiveDom: task.debugLiveDom || null
          }, value => {
            if (chrome.runtime.lastError) {
              resolve({
                ok: false,
                error: chrome.runtime.lastError.message
              });
              return;
            }
            resolve(value || { ok: false, error: "empty_response" });
          });
        }),
        10000,
        { ok: false, error: "probe_timeout" }
      );

      results.push({
        tabId: tab.id,
        title: tab.title || "",
        url: tab.url || "",
        active: Boolean(tab.active),
        discarded: Boolean(tab.discarded),
        status: tab.status || "",
        ...injection,
        ...response
      });
    }

    sendToLocalClient({
      type: "task_response",
      taskId: task.taskId,
      status: "success",
      data: {
        extensionVersion: chrome.runtime.getManifest().version,
        appId,
        matchedTabs: results.length,
        xiaohongshuTabs: appId === "xiaohongshu.filter" ? results.length : undefined,
        tabs: results
      },
      message: `probed ${results.length} tab(s) for ${appId}`
    });
  } catch (error) {
    sendToLocalClient({
      type: "task_response",
      taskId: task.taskId,
      status: "error",
      message: error?.message || String(error),
      data: null
    });
  }
}

function sendBrowserTaskResponse(task, status, data, message) {
  sendToLocalClient({
    type: "task_response",
    taskId: task.taskId,
    status,
    data: data ?? null,
    message: message || ""
  });
}

function normalizeXiaohongshuCustomCard(input) {
  if (!input || typeof input !== "object") {
    throw new Error("card is required");
  }

  const text = value => String(value ?? "").trim();
  const title = text(input.title);
  if (!title) throw new Error("card.title is required");

  const safeHttpUrl = value => {
    const candidate = text(value);
    if (!candidate) return "";
    try {
      const parsed = new URL(candidate);
      return parsed.protocol === "http:" || parsed.protocol === "https:"
        ? parsed.href
        : "";
    } catch {
      return "";
    }
  };

  return {
    id: text(input.id) || crypto.randomUUID(),
    title: title.slice(0, 160),
    body: text(input.body || input.summary || input.description).slice(0, 1200),
    author: (text(input.author) || "燕子").slice(0, 80),
    badge: (text(input.badge) || "燕子").slice(0, 24),
    imageUrl: safeHttpUrl(input.imageUrl || input.coverUrl),
    url: safeHttpUrl(input.url),
    enabled: input.enabled !== false,
    priority: Number.isFinite(Number(input.priority)) ? Number(input.priority) : 0,
    createdAt: Number(input.createdAt) || Date.now(),
    updatedAt: Date.now()
  };
}

async function handleWebAppDataTask(task) {
  try {
    if (task.action === "webapp_data_get") {
      const appId = String(task.appId || "").trim();
      const key = String(task.key || "").trim();
      if (!appId || !key) throw new Error("appId/key is required");
      const value = await getWebAppData(appId, key, task.fallback ?? null);
      sendBrowserTaskResponse(task, "success", { appId, key, value }, "webapp data read");
      return;
    }

    if (task.action === "webapp_data_set") {
      const appId = String(task.appId || "").trim();
      const key = String(task.key || "").trim();
      if (!appId || !key) throw new Error("appId/key is required");
      const record = await setWebAppData(appId, key, task.value);
      await broadcastWebAppDataChanged(appId, key, task.value);
      sendBrowserTaskResponse(
        task,
        "success",
        { appId, key, value: task.value, updatedAt: record.updatedAt },
        "webapp data saved"
      );
      return;
    }

    const appId = "xiaohongshu.filter";
    const key = "customCards";
    const current = await getWebAppData(appId, key, { version: 1, items: [] });
    let items = Array.isArray(current?.items) ? current.items : [];

    if (task.action === "xiaohongshu_custom_card_list") {
      sendBrowserTaskResponse(
        task,
        "success",
        { version: 1, items },
        `listed ${items.length} custom card(s)`
      );
      return;
    }

    if (task.action === "xiaohongshu_custom_card_upsert") {
      const card = normalizeXiaohongshuCustomCard(task.card);
      const index = items.findIndex(item => String(item?.id || "") === card.id);
      if (index >= 0) {
        const previous = items[index] || {};
        items[index] = {
          ...previous,
          ...card,
          createdAt: Number(previous.createdAt) || card.createdAt
        };
      } else {
        items.push(card);
      }

      items = items
        .filter(item => item && item.id)
        .sort((a, b) => Number(b.priority || 0) - Number(a.priority || 0) ||
          Number(b.updatedAt || b.createdAt || 0) - Number(a.updatedAt || a.createdAt || 0))
        .slice(0, 100);

      const value = { version: 1, updatedAt: Date.now(), items };
      const record = await setWebAppData(appId, key, value);
      await broadcastWebAppDataChanged(appId, key, value);
      sendBrowserTaskResponse(
        task,
        "success",
        { card, count: items.length, updatedAt: record.updatedAt },
        "xiaohongshu custom card upserted"
      );
      return;
    }

    if (task.action === "xiaohongshu_custom_card_remove") {
      const id = String(task.id || "").trim();
      if (!id) throw new Error("id is required");
      const before = items.length;
      items = items.filter(item => String(item?.id || "") !== id);
      const value = { version: 1, updatedAt: Date.now(), items };
      await setWebAppData(appId, key, value);
      await broadcastWebAppDataChanged(appId, key, value);
      sendBrowserTaskResponse(
        task,
        "success",
        { id, removed: before !== items.length, count: items.length },
        "xiaohongshu custom card removed"
      );
      return;
    }

    if (task.action === "xiaohongshu_custom_card_clear") {
      const value = { version: 1, updatedAt: Date.now(), items: [] };
      await setWebAppData(appId, key, value);
      await broadcastWebAppDataChanged(appId, key, value);
      sendBrowserTaskResponse(task, "success", { count: 0 }, "xiaohongshu custom cards cleared");
      return;
    }

    throw new Error(`unsupported webapp data action: ${task.action}`);
  } catch (error) {
    sendBrowserTaskResponse(task, "error", null, error?.message || String(error));
  }
}

// 专门处理 AI 任务：优先复用已打开的 AI 标签页
function handleAiPromptTransferTask(task, targetUrl) {
  const urlPattern = "*://chat.deepseek.com/*";
  
  chrome.tabs.query({ url: urlPattern }, (tabs) => {
    if (tabs && tabs.length > 0) {
      const existingTab = tabs[0];
      const tabId = existingTab.id;
      logEvent(`发现已存在的 DeepSeek 标签页 [TabID: ${tabId}], isNewSession=${task.isNewSession}, url=${existingTab.url}`);
      
      const isSpecificChat = existingTab.url && (existingTab.url.includes("/a/chat/s/") || existingTab.url.includes("/chat/"));
      
      if (task.isNewSession && isSpecificChat) {
        logEvent(`[AiTransfer] 新会话需重置标签页至主站根路径开启全新对话`);
        chrome.tabs.update(tabId, { url: "https://chat.deepseek.com/", active: true }, () => {
          chrome.tabs.onUpdated.addListener(function listener(updatedTabId, info) {
            if (updatedTabId === tabId && info.status === "complete") {
              chrome.tabs.onUpdated.removeListener(listener);
              setTimeout(() => {
                injectAndStart(tabId, task);
              }, 800);
            }
          });
        });
        return;
      }

      // 激活该标签页并直接注入执行
      chrome.tabs.update(tabId, { active: true }, () => {
        injectAndStart(tabId, task);
      });
    } else {
      logEvent(`未发现 DeepSeek 标签页，正在创建新标签页: ${targetUrl}`);
      chrome.tabs.create({ url: targetUrl, active: true }, (tab) => {
        const tabId = tab.id;
        chrome.tabs.onUpdated.addListener(function listener(updatedTabId, info) {
          if (updatedTabId === tabId && info.status === "complete") {
            chrome.tabs.onUpdated.removeListener(listener);
            logEvent(`DeepSeek 页面加载完成，开始注入执行脚本 [TabID: ${tabId}]`);
            // 稍等 800ms 确保 SPA 框架完全初始化
            setTimeout(() => {
              injectAndStart(tabId, task);
            }, 800);
          }
        });
      });
    }
  });
}

// 注入脚本并启动任务
function injectAndStart(tabId, task) {
  chrome.scripting.executeScript({
    target: { tabId: tabId },
    files: ["content.js"]
  }, () => {
    if (chrome.runtime.lastError) {
      logEvent(`脚本注入提示: ${chrome.runtime.lastError.message}`);
      // 部分情况下即使脚本已注入也会报错，尝试直接通信
    }
    
    // 向 content.js 发送具体任务配置
    chrome.tabs.sendMessage(tabId, {
      type: "start_task",
      task: task
    }, (response) => {
      if (chrome.runtime.lastError) {
        logEvent(`消息发送警告: ${chrome.runtime.lastError.message}`);
      }
    });
  });
}

// 监听来自 content.js 或 popup 的消息
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  // 1. 获取实时活跃状态
  if (message.action === "get_status") {
    const isWsOpen = Boolean(ws && ws.readyState === WebSocket.OPEN);
    const isFresh = isWsOpen && (Date.now() - lastPongAt <= PONG_TIMEOUT_MS);
    sendResponse({ status: isFresh ? "connected" : "disconnected" });
    if (isWsOpen && !isFresh) {
      forceReconnect("popup_detected_stale_socket");
    } else if (!isWsOpen && (!ws || ws.readyState === WebSocket.CLOSED)) {
      connectWebSocket("popup_check");
    }
    return true;
  }

  // 2. 处理来自控制面板的重连请求
  if (message.action === "reconnect") {
    logEvent("收到来自控制面板的重新连接请求...");
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    const oldWs = ws;
    ws = null;
    currentWsSeq++; // 提升 seq，确保 oldWs 触发的 onclose 被直接丢弃
    if (oldWs) {
      try { oldWs.close(); } catch(e){}
    }
    reconnectDelay = 1000; // 重置延迟
    connectWebSocket("user_reconnect_btn");
    sendResponse({ status: "connecting" });
    return true;
  }

  // 3. 监听来自 content.js 的返回结果
  if (message.type === "task_result" && sender.tab) {
    const tabId = sender.tab.id;
    logEvent(`任务 [${message.taskId}] 执行完成，提取到数据，正在回传...`);
    
    // 发送数据给本地燕子服务
    sendToLocalClient({
      type: "task_response",
      taskId: message.taskId,
      status: message.status,
      data: message.data,
      message: message.message
    });
    
    // 如果任务要求完成后自动销毁页面
    if (message.closeOnComplete) {
      logEvent(`任务要求关闭网页，正在销毁 Tab [TabID: ${tabId}]`);
      chrome.tabs.remove(tabId);
    }
  }
});

// 使用 chrome.alarms 实现可靠的后台保活与心跳
chrome.alarms.create("yanziKeepAlive", { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "yanziKeepAlive") heartbeat("alarm");
});

// 初始化连接
chrome.storage.local.get({ localWsUrl }, (config) => {
  localWsUrl = config.localWsUrl;
  connectWebSocket();
});
chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== "local" || !changes.localWsUrl) return;
  localWsUrl = changes.localWsUrl.newValue;
  const old = ws;
  ws = null;
  currentWsSeq++;
  if (old) old.close();
  connectWebSocket("configuration_changed");
});
// Exchange messages inside the MV3 30-second inactivity window.
setInterval(() => heartbeat("interval"), HEARTBEAT_INTERVAL_MS);
globalThis.yanziBrowserHost = {
  send: sendToLocalClient,
  isConnected: () => Boolean(ws && ws.readyState === WebSocket.OPEN),
  log: logEvent
};
importScripts("chatgpt-background.js", "webapps/runtime-background.js");
