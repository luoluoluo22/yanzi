document.addEventListener("DOMContentLoaded", () => {
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
      reconnectBtn.innerText = "重新连接服务";
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
