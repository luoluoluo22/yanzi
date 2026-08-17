let ws = null;
let reconnectDelay = 1000;
const maxReconnectDelay = 30000;
let isConnected = false;
let reconnectTimer = null;
const LOG_BATCH_INTERVAL_MS = 5000;
let pendingLogs = [];
let logFlushTimer = null;

// 辅助函数：更新连接状态到本地存储，供 popup 读取
function updateStatus(status) {
  isConnected = (status === "connected");
  chrome.storage.local.set({ connectionStatus: status });
  logEvent(`连接状态更新为: ${status}`);
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

// 连接本地 WebSocket 服务
function connectWebSocket() {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
    return;
  }

  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }

  logEvent("尝试连接到燕子启动器本地服务...");
  
  ws = new WebSocket("ws://127.0.0.1:53919/v1/browser/ws");
  
  ws.onopen = () => {
    updateStatus("connected");
    reconnectDelay = 1000; // 重连成功，重置延迟
    reconnectTimer = null;
    
    // 发送握手注册消息
    ws.send(JSON.stringify({
      type: "register",
      client: "yanzi-extension"
    }));
  };
  
  ws.onmessage = (event) => {
    try {
      const message = JSON.parse(event.data);
      logEvent(`收到来自燕子的指令: ${message.action || message.type}`);
      
      if (message.type === "task_request") {
        handleTask(message);
      }
    } catch (err) {
      logEvent(`解析消息失败: ${err.message}`);
    }
  };
  
  ws.onclose = () => {
    updateStatus("disconnected");
    ws = null;
    scheduleReconnect();
  };
  
  ws.onerror = (err) => {
    console.error("WebSocket 错误:", err);
    // ws.close() 会被自动调用，触发 onclose 逻辑
  };
}

// 自动重连逻辑 (指数退避)
function scheduleReconnect() {
  if (reconnectTimer) {
    return;
  }

  logEvent(`连接断开，将在 ${reconnectDelay / 1000} 秒后尝试重新连接...`);
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectWebSocket();
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
chrome.runtime.onMessage.addListener((message, sender) => {
  // 1. 处理来自控制面板的重连请求
  if (message.action === "reconnect") {
    logEvent("收到来自控制面板的重新连接请求...");
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    if (ws) {
      try { ws.close(); } catch(e){}
    }
    reconnectDelay = 1000; // 重置延迟
    connectWebSocket();
    return;
  }

  // 2. 监听来自 content.js 的返回结果
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

// 定时心跳以保持 Service Worker 存活并维护 WebSocket 活性
setInterval(() => {
  if (ws && ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify({ type: "ping" }));
  } else if ((!ws || ws.readyState === WebSocket.CLOSED) && !reconnectTimer) {
    // 兜底重连
    scheduleReconnect();
  }
}, 20000);

// 初始化连接
connectWebSocket();
