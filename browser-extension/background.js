let ws = null;
let reconnectDelay = 1000;
const maxReconnectDelay = 30000;
let isConnected = false;

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
  
  chrome.storage.local.get({ logs: [] }, (result) => {
    let logs = result.logs;
    logs.push(logMessage);
    if (logs.length > 50) {
      logs.shift(); // 仅保留最近 50 条日志
    }
    chrome.storage.local.set({ logs: logs });
  });
}

// 连接本地 WebSocket 服务
function connectWebSocket() {
  logEvent("尝试连接到燕子启动器本地服务...");
  
  ws = new WebSocket("ws://127.0.0.1:53919/v1/browser/ws");
  
  ws.onopen = () => {
    updateStatus("connected");
    reconnectDelay = 1000; // 重连成功，重置延迟
    
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
    scheduleReconnect();
  };
  
  ws.onerror = (err) => {
    console.error("WebSocket 错误:", err);
    // ws.close() 会被自动调用，触发 onclose 逻辑
  };
}

// 自动重连逻辑 (指数退避)
function scheduleReconnect() {
  logEvent(`连接断开，将在 ${reconnectDelay / 1000} 秒后尝试重新连接...`);
  setTimeout(() => {
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
  logEvent(`开始执行任务 [${task.taskId}]，目标网址: ${task.url}`);
  
  // 1. 静默创建后台 Tab 页 (active: false)
  chrome.tabs.create({ url: task.url, active: false }, (tab) => {
    const tabId = tab.id;
    
    // 监听页面加载状态
    chrome.tabs.onUpdated.addListener(function listener(updatedTabId, info) {
      if (updatedTabId === tabId && info.status === "complete") {
        // 移除监听器，防止多次执行
        chrome.tabs.onUpdated.removeListener(listener);
        
        logEvent(`网页加载完成，开始注入执行脚本 [TabID: ${tabId}]`);
        
        // 2. 动态注入 content.js
        chrome.scripting.executeScript({
          target: { tabId: tabId },
          files: ["content.js"]
        }, () => {
          if (chrome.runtime.lastError) {
            logEvent(`脚本注入失败: ${chrome.runtime.lastError.message}`);
            sendToLocalClient({
              type: "task_response",
              taskId: task.taskId,
              status: "error",
              message: chrome.runtime.lastError.message
            });
            chrome.tabs.remove(tabId);
            return;
          }
          
          // 3. 向 content.js 发送具体任务配置
          chrome.tabs.sendMessage(tabId, {
            type: "start_task",
            task: task
          });
        });
      }
    });
  });
}

// 监听来自 content.js 或 popup 的消息
chrome.runtime.onMessage.addListener((message, sender) => {
  // 1. 处理来自控制面板的重连请求
  if (message.action === "reconnect") {
    logEvent("收到来自控制面板的重新连接请求...");
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
  } else if (!ws || ws.readyState === WebSocket.CLOSED) {
    // 兜底重连
    connectWebSocket();
  }
}, 20000);

// 初始化连接
connectWebSocket();
