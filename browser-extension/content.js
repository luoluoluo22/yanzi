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
    } else if (task.action === "workflow") {
      performWorkflow(task);
    } else {
      sendResult(task.taskId, "error", null, task.closeOnComplete, `未知的任务动作: ${task.action}`);
    }
  } catch (err) {
    sendResult(task.taskId, "error", null, task.closeOnComplete, `执行异常: ${err.message}`);
  }
}

// ==========================================
// 1. 声明式多步骤工作流引擎 (Workflow Engine)
// ==========================================
async function performWorkflow(task) {
  const steps = task.steps || [];
  const results = {};
  
  console.log(`[Workflow] 开始执行任务 [ID: ${task.taskId}]，共 ${steps.length} 个步骤`);

  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    console.log(`[Workflow] 正在执行第 ${i + 1}/${steps.length} 步: ${step.type}`);
    
    try {
      if (step.type === "wait") {
        await handleWaitStep(step);
      } else if (step.type === "fill") {
        await handleFillStep(step);
      } else if (step.type === "click") {
        await handleClickStep(step);
      } else if (step.type === "scroll") {
        await handleScrollStep(step);
      } else if (step.type === "scrape") {
        const scrapeResult = await handleScrapeStep(step);
        // 将抓取到的数据累加进结果集中
        Object.assign(results, scrapeResult);
      } else {
        throw new Error(`不支持的步骤类型: ${step.type}`);
      }
    } catch (err) {
      console.error(`[Workflow] 步骤 ${i + 1} (${step.type}) 失败: ${err.message}`);
      sendResult(
        task.taskId, 
        "error", 
        null, 
        task.closeOnComplete, 
        `步骤 ${i + 1} (${step.type}) 失败: ${err.message}`
      );
      return;
    }
  }

  console.log(`[Workflow] 任务 [ID: ${task.taskId}] 执行成功，正在回传数据...`);
  sendResult(task.taskId, "success", results, task.closeOnComplete);
}

// ------------------------------------------
// 工作流步骤原子操作处理器
// ------------------------------------------

// 1.1 等待操作 (Wait)
async function handleWaitStep(step) {
  const timeout = Math.min(step.timeout || 5000, 30000);
  
  if (step.selector) {
    await waitForElement(step.selector, timeout);
  } else {
    // 仅仅是静态延时等待
    await new Promise(resolve => setTimeout(resolve, timeout));
  }
}

function waitForElement(selector, timeout) {
  if (document.querySelector(selector)) {
    return Promise.resolve();
  }

  return new Promise((resolve, reject) => {
    const startTime = Date.now();
    let finished = false;
    let observer = null;
    let fallbackPoll = null;
    let timer = null;

    const cleanup = () => {
      if (finished) {
        return;
      }
      finished = true;
      observer?.disconnect();
      if (fallbackPoll) {
        clearInterval(fallbackPoll);
      }
      if (timer) {
        clearTimeout(timer);
      }
    };

    const resolveOnce = () => {
      cleanup();
      resolve();
    };

    const rejectOnce = () => {
      cleanup();
      reject(new Error(`等待元素超时: ${selector}`));
    };

    observer = new MutationObserver(() => {
      if (document.querySelector(selector)) {
        resolveOnce();
      }
    });

    timer = window.setTimeout(rejectOnce, timeout);

    observer.observe(document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true
    });

    fallbackPoll = window.setInterval(() => {
      if (document.querySelector(selector)) {
        resolveOnce();
      } else if (Date.now() - startTime > timeout) {
        rejectOnce();
      }
    }, 500);
  });
}

// 1.2 高保真输入操作 (Fill)
async function handleFillStep(step) {
  const el = document.querySelector(step.selector);
  if (!el) {
    throw new Error(`未找到输入框元素: ${step.selector}`);
  }

  el.value = step.value;
  
  // 触发 SPA 双向绑定更新事件
  el.dispatchEvent(new Event("input", { bubbles: true }));
  el.dispatchEvent(new Event("change", { bubbles: true }));
  
  // React 专有状态追踪器触发
  const tracker = el._valueTracker;
  if (tracker) {
    tracker.setValue(step.value);
  }
  
  // 稍微等待 100ms 确保页面数据流渲染更新完成
  await new Promise(resolve => setTimeout(resolve, 100));
}

// 1.3 模拟点击操作 (Click)
async function handleClickStep(step) {
  const el = document.querySelector(step.selector);
  if (!el) {
    throw new Error(`未找到点击目标元素: ${step.selector}`);
  }
  el.click();
  // 等待 150ms 给浏览器事件留出缓冲时间
  await new Promise(resolve => setTimeout(resolve, 150));
}

// 1.4 页面滚动操作 (Scroll)
async function handleScrollStep(step) {
  const distance = step.distance || 400;
  window.scrollBy(0, distance);
  // 等待 200ms 让滚动惯性与数据懒加载进行
  await new Promise(resolve => setTimeout(resolve, 200));
}

// 1.5 页面数据抓取操作 (Scrape)
async function handleScrapeStep(step) {
  const resultData = {};
  const selectors = step.selectors || {};

  for (const [key, selectorConfig] of Object.entries(selectors)) {
    let selectorStr = "";
    let attributeToExtract = "innerText";

    if (typeof selectorConfig === "string") {
      if (selectorConfig.includes("|")) {
        const parts = selectorConfig.split("|");
        selectorStr = parts[0].trim();
        attributeToExtract = parts[1].trim();
      } else {
        selectorStr = selectorConfig.trim();
      }
    } else {
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

  return resultData;
}

// ==========================================
// 2. 原单步处理接口 (保持向后兼容)
// ==========================================

function performScrape(task) {
  const selectors = task.selectors || {};
  // 构造单步 scrape 为一个 workflow 步骤并执行
  handleScrapeStep({ selectors: selectors }).then(data => {
    sendResult(task.taskId, "success", data, task.closeOnComplete);
  }).catch(err => {
    sendResult(task.taskId, "error", null, task.closeOnComplete, err.message);
  });
}

function performAutofill(task) {
  const fields = task.fields || [];
  fields.forEach(field => {
    const el = document.querySelector(field.selector);
    if (el) {
      el.value = field.value;
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
      const tracker = el._valueTracker;
      if (tracker) tracker.setValue(field.value);
    }
  });

  if (task.clickSelector) {
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

// 辅助向 background 发送结果
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
