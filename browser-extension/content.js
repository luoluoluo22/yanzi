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
    if (task.action === "ai_prompt_transfer") {
      performAiPromptTransfer(task);
    } else if (task.action === "scrape") {
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
// 0. AI 提示词自动传递与 JSON 提取引擎 (AI Transfer Engine)
// ==========================================
async function performAiPromptTransfer(task) {
  const prompt = task.prompt || "";
  const timeoutMs = (task.timeoutSeconds || 120) * 1000;
  console.log("%c[Yanzi AiTransfer] 收到 DeepSeek 任务 [ID: " + task.taskId + "], 提示词长度: " + prompt.length, "color: #3b82f6; font-weight: bold;");

  if (!prompt) {
    sendResult(task.taskId, "error", null, false, "提示词内容为空");
    return;
  }

  try {
    // 0. 若为开启新会话且页面处于旧对话中，优先点击新对话按钮或重定向
    if (task.isNewSession) {
      console.log("%c[Yanzi AiTransfer] 任务要求开启新会话，正在定位【开启新对话】按钮...", "color: #3b82f6;");
      try {
        const isOldUrl = window.location.href.includes("/a/chat/s/") || window.location.href.includes("/chat/");
        if (isOldUrl) {
          const candidates = Array.from(document.querySelectorAll("button, div[role='button'], a, span, div")).filter(el => {
            const text = (el.innerText || "").trim();
            const aria = el.getAttribute("aria-label") || "";
            const title = el.getAttribute("title") || "";
            return text === "+" || text.includes("开启新对话") || text.includes("新建对话") || text.includes("新对话") ||
                   aria.includes("新对话") || aria.includes("New Chat") || title.includes("新对话") ||
                   el.closest("a[href='/']") !== null;
          });

          if (candidates.length > 0) {
            candidates[0].click();
            console.log("%c[Yanzi AiTransfer] 已点击【开启新对话】按钮/元素", "color: #10b981;");
            await new Promise(r => setTimeout(r, 800));
          }
        }
      } catch (e) {
        console.warn("点击新对话按钮提示:", e);
      }
    } else {
      console.log("%c[Yanzi AiTransfer] 任务为延续对话，将在当前会话中直接追加提示词...", "color: #3b82f6;");
    }

    // 1. 等待并定位输入框
    console.log("%c[Yanzi AiTransfer] 正在定位 DeepSeek 输入框...", "color: #3b82f6;");
    const inputSelector = "textarea#chat-input, textarea[placeholder*='输入'], textarea[placeholder*='DeepSeek'], textarea, div[contenteditable='true']";
    await waitForElement(inputSelector, 15000);

    const inputEl = document.querySelector(inputSelector);
    if (!inputEl) {
      throw new Error("未找到 DeepSeek 输入框元素");
    }

    // 2. 聚焦并高保真填入提示词
    console.log("%c[Yanzi AiTransfer] 正在填入提示词...", "color: #3b82f6;");
    inputEl.focus();
    if (inputEl.tagName === "TEXTAREA" || inputEl.tagName === "INPUT") {
      inputEl.value = prompt;
      inputEl.dispatchEvent(new Event("input", { bubbles: true }));
      inputEl.dispatchEvent(new Event("change", { bubbles: true }));
      if (inputEl._valueTracker) {
        inputEl._valueTracker.setValue(prompt);
      }
    } else {
      inputEl.innerText = prompt;
      inputEl.dispatchEvent(new InputEvent("input", { bubbles: true }));
    }

    await new Promise(r => setTimeout(r, 400));

    // 3. 点击发送按钮
    console.log("%c[Yanzi AiTransfer] 正在点击发送...", "color: #3b82f6;");
    let sent = false;

    const sendButtons = Array.from(document.querySelectorAll("button, div[role='button']")).filter(btn => {
      const aria = btn.getAttribute("aria-label") || "";
      const text = btn.innerText || "";
      const isSend = aria.includes("发送") || aria.includes("Send") || text.includes("发送");
      const hasSvg = btn.querySelector("svg") !== null;
      return isSend || (hasSvg && btn.closest("form, div[class*='input'], div[class*='chat']"));
    });

    if (sendButtons.length > 0) {
      const sendBtn = sendButtons[sendButtons.length - 1];
      sendBtn.click();
      sent = true;
      console.log("%c[Yanzi AiTransfer] 点击了发送按钮", "color: #10b981;");
    }

    if (!sent) {
      inputEl.dispatchEvent(new KeyboardEvent("keydown", {
        key: "Enter",
        code: "Enter",
        keyCode: 13,
        which: 13,
        bubbles: true,
        cancelable: true
      }));
      console.log("%c[Yanzi AiTransfer] 触发了 Enter 键提交", "color: #10b981;");
    }

    // 4. 监听生成开始与完成
    console.log("%c[Yanzi AiTransfer] 正在监听 DeepSeek 生成响应...", "color: #3b82f6;");
    await new Promise(r => setTimeout(r, 2000));

    const startTime = Date.now();
    let finalJson = null;
    let stableText = "";
    let stableCount = 0;

    while (Date.now() - startTime < timeoutMs) {
      await new Promise(r => setTimeout(r, 1000));

      // 检测停止按钮
      const isGenerating = document.querySelector("button[aria-label*='停止'], div[role='button']:has(rect), div[role='button'][aria-label*='Stop']") !== null;

      // 实时尝试从 DOM 提取合法 JSON
      const candidateJson = extractJsonFromDeepSeekDom();

      if (candidateJson) {
        if (candidateJson === stableText) {
          stableCount++;
        } else {
          stableCount = 0;
          stableText = candidateJson;
        }

        // 完成条件：停止按钮消失，或者内容已稳定 2 秒以上且符合 JSON 规范
        if (!isGenerating || stableCount >= 2) {
          finalJson = candidateJson;
          console.log("%c[Yanzi AiTransfer] 成功提取到合法 JSON (长度: " + finalJson.length + ")", "color: #10b981; font-weight: bold;");
          break;
        }
      }
    }

    if (!finalJson) {
      // 最终兜底尝试提取
      finalJson = extractJsonFromDeepSeekDom();
    }

    if (!finalJson) {
      throw new Error("等待超时，未能在 DeepSeek 页面中提取到有效的 JSON 扩展定义");
    }

    console.log("%c[Yanzi AiTransfer] 回传 JSON 结果至燕子桌面端...", "color: #10b981; font-weight: bold;");
    sendResult(task.taskId, "success", {
      rawJson: finalJson,
      site: "deepseek"
    }, false);

  } catch (err) {
    console.error("%c[Yanzi AiTransfer] 错误: " + err.message, "color: #ef4444;");
    sendResult(task.taskId, "error", null, false, "DeepSeek 自动化失败: " + err.message);
  }
}

// 核心：从 DeepSeek 页面 DOM 多级提取合法扩展 JSON
function extractJsonFromDeepSeekDom() {
  // 1. 扫描所有 pre, code, .md-code-block
  const codeNodes = Array.from(document.querySelectorAll("pre code, pre, .md-code-block, code, div[class*='code']"));
  for (let i = codeNodes.length - 1; i >= 0; i--) {
    const text = codeNodes[i].innerText || "";
    const parsed = tryParseValidExtensionJson(text);
    if (parsed) return parsed;
  }

  // 2. 扫描所有 markdown 正文
  const markdownNodes = Array.from(document.querySelectorAll(".ds-markdown, div[class*='markdown'], div[class*='message']"));
  for (let i = markdownNodes.length - 1; i >= 0; i--) {
    const text = markdownNodes[i].innerText || "";
    const matches = text.matchAll(/```(?:json)?\s*([\s\S]*?)```/gi);
    for (const m of matches) {
      const parsed = tryParseValidExtensionJson(m[1]);
      if (parsed) return parsed;
    }
    const parsed = tryParseValidExtensionJson(text);
    if (parsed) return parsed;
  }

  // 3. 扫描整个 body
  const bodyText = document.body.innerText || "";
  const bodyMatches = Array.from(bodyText.matchAll(/```(?:json)?\s*([\s\S]*?)```/gi)).reverse();
  for (const m of bodyMatches) {
    const parsed = tryParseValidExtensionJson(m[1]);
    if (parsed) return parsed;
  }

  return null;
}

// 辅助：校验并格式化为标准 JSON 字符串
function tryParseValidExtensionJson(raw) {
  if (!raw || typeof raw !== "string") return null;
  let text = raw.trim();
  text = text.replace(/^```json\s*/i, "").replace(/^```\s*/i, "").replace(/```$/i, "").trim();

  // 尝试直接解析
  try {
    const obj = JSON.parse(text);
    if (obj && typeof obj === "object" && (obj.id || obj.name || obj.openTarget || obj.runtime || obj.category)) {
      return JSON.stringify(obj, null, 2);
    }
  } catch (e) {}

  // 截取最外层 { ... }
  const firstBrace = text.indexOf("{");
  const lastBrace = text.lastIndexOf("}");
  if (firstBrace !== -1 && lastBrace > firstBrace) {
    const sub = text.substring(firstBrace, lastBrace + 1);
    try {
      const obj = JSON.parse(sub);
      if (obj && typeof obj === "object" && (obj.id || obj.name || obj.openTarget || obj.runtime || obj.category)) {
        return JSON.stringify(obj, null, 2);
      }
    } catch (e) {}
  }

  return null;
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
