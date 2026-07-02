// 监听来自 background.js 的指令
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "start_task") {
    const task = message.task;
    executeTask(task);
  }
});

// 核心任务执行引擎
function executeTask(task) {
  try {
    if (task.action === "scrape") {
      performScrape(task);
    } else if (task.action === "autofill") {
      performAutofill(task);
    } else {
      sendResult(task.taskId, "error", null, task.closeOnComplete, `未知的任务动作: ${task.action}`);
    }
  } catch (err) {
    sendResult(task.taskId, "error", null, task.closeOnComplete, `执行异常: ${err.message}`);
  }
}

// 1. 数据抓取逻辑
function performScrape(task) {
  const resultData = {};
  const selectors = task.selectors || {};

  for (const [key, selectorConfig] of Object.entries(selectors)) {
    let selectorStr = "";
    let attributeToExtract = "innerText"; // 默认读取 innerText

    // 支持简洁的字符串格式: "a.postTitle|href"
    if (typeof selectorConfig === "string") {
      if (selectorConfig.includes("|")) {
        const parts = selectorConfig.split("|");
        selectorStr = parts[0].trim();
        attributeToExtract = parts[1].trim();
      } else {
        selectorStr = selectorConfig.trim();
      }
    } else {
      // 支持结构化的对象格式: { "selector": "...", "attr": "..." }
      selectorStr = selectorConfig.selector;
      attributeToExtract = selectorConfig.attr || "innerText";
    }

    const elements = document.querySelectorAll(selectorStr);
    const extractedValues = [];

    elements.forEach((el) => {
      let val = "";
      if (attributeToExtract === "innerText") {
        val = el.innerText;
      } else if (attributeToExtract === "innerHTML") {
        val = el.innerHTML;
      } else if (attributeToExtract === "textContent") {
        val = el.textContent;
      } else {
        val = el.getAttribute(attributeToExtract) || "";
      }
      extractedValues.push(val.trim());
    });

    resultData[key] = extractedValues;
  }

  sendResult(task.taskId, "success", resultData, task.closeOnComplete);
}

// 2. 表单高保真自动填充逻辑
function performAutofill(task) {
  const fields = task.fields || [];

  fields.forEach((field) => {
    const el = document.querySelector(field.selector);
    if (!el) {
      console.warn(`未找到表单元素: ${field.selector}`);
      return;
    }

    // 设置输入框值
    el.value = field.value;

    // 高保真 SPA 框架适配 (React/Vue/Angular 事件分发)
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));

    // 针对 React 15/16+ 专有的 value tracker 触发机制，确保输入框状态能被 React 正确捕获
    const tracker = el._valueTracker;
    if (tracker) {
      tracker.setValue(field.value);
    }
  });

  // 如果定义了点击触发器 (例如“发布”按钮)
  if (task.clickSelector) {
    // 稍微延迟 300ms 确保表单渲染/事件绑定处理完毕
    setTimeout(() => {
      const btn = document.querySelector(task.clickSelector);
      if (btn) {
        btn.click();
        sendResult(task.taskId, "success", null, task.closeOnComplete);
      } else {
        sendResult(task.taskId, "error", null, task.closeOnComplete, `未找到点击元素: ${task.clickSelector}`);
      }
    }, 300);
  } else {
    sendResult(task.taskId, "success", null, task.closeOnComplete);
  }
}

// 辅助函数：将结果发回 background.js
function sendResult(taskId, status, data, closeOnComplete, errorMessage = "") {
  chrome.runtime.sendMessage({
    type: "task_result",
    taskId: taskId,
    status: status,
    data: data,
    message: errorMessage,
    closeOnComplete: !!closeOnComplete
  });
}
