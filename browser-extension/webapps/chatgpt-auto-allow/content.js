(function () {
  "use strict";

  const APP_ID = "chatgpt.auto-allow";
  const APP_VERSION = "0.1.0";
  const SETTINGS_KEY = "settings";
  const STYLE_ID = "yanzi-chatgpt-auto-allow-style";

  const DEFAULT_SETTINGS = {
    enabled: true,
    countdownSeconds: 15,
    preferAlways: true
  };

  if (globalThis.__yanziChatGPTAutoAllowLoaded) {
    globalThis.__yanziChatGPTAutoAllowRefresh?.();
    return;
  }
  globalThis.__yanziChatGPTAutoAllowLoaded = true;

  let settings = { ...DEFAULT_SETTINGS };
  let enabled = true;
  let activeTasks = new Map(); // containerElement -> taskObject
  let processedElements = new WeakSet();
  let scanTimer = null;
  let observer = null;
  let recentLogs = [];

  function log(message, detail = null) {
    const entry = {
      time: new Date().toLocaleTimeString("zh-CN", { hour12: false }),
      message,
      detail
    };
    recentLogs.unshift(entry);
    if (recentLogs.length > 50) recentLogs.pop();
    console.log("[燕子自动允许]", message, detail || "");
  }

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
      .yanzi-auto-allow-bar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        margin-top: 10px;
        padding: 6px 12px;
        border-radius: 8px;
        background: rgba(30, 41, 59, 0.92);
        border: 1px solid rgba(148, 163, 184, 0.28);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
        color: #f1f5f9;
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Microsoft YaHei", sans-serif;
        font-size: 12px;
        line-height: 1.4;
        box-sizing: border-box;
        transition: all 0.2s ease-in-out;
        z-index: 20;
      }
      .yanzi-auto-allow-bar.paused {
        background: rgba(51, 65, 85, 0.95);
        border-color: rgba(245, 158, 11, 0.45);
      }
      .yanzi-auto-allow-bar.done {
        background: rgba(20, 83, 45, 0.9);
        border-color: rgba(34, 197, 94, 0.45);
      }
      .yanzi-auto-allow-bar.cancelled {
        background: rgba(71, 85, 105, 0.75);
        border-color: rgba(148, 163, 184, 0.2);
        opacity: 0.85;
      }
      .yanzi-auto-allow-text {
        display: flex;
        align-items: center;
        gap: 6px;
        flex: 1;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .yanzi-auto-allow-count {
        display: inline-block;
        min-width: 24px;
        padding: 1px 7px;
        border-radius: 999px;
        background: #10b981;
        color: #ffffff;
        font-weight: 700;
        text-align: center;
        font-size: 11px;
        transition: transform 0.15s ease;
      }
      .yanzi-auto-allow-bar.paused .yanzi-auto-allow-count {
        background: #f59e0b;
      }
      .yanzi-auto-allow-btn-cancel {
        cursor: pointer;
        border: 1px solid rgba(255, 255, 255, 0.25);
        background: rgba(255, 255, 255, 0.12);
        color: #cbd5e1;
        border-radius: 6px;
        padding: 3px 9px;
        font-size: 11px;
        font-weight: 500;
        transition: all 0.15s ease;
      }
      .yanzi-auto-allow-btn-cancel:hover {
        background: rgba(239, 68, 68, 0.28);
        border-color: rgba(239, 68, 68, 0.45);
        color: #fca5a5;
      }
      .yanzi-auto-allow-progress {
        position: relative;
        height: 3px;
        width: 100%;
        background: rgba(255, 255, 255, 0.15);
        border-radius: 2px;
        overflow: hidden;
        margin-top: 5px;
      }
      .yanzi-auto-allow-progress-inner {
        height: 100%;
        background: #10b981;
        border-radius: 2px;
        transition: width 0.2s linear;
      }
      .yanzi-auto-allow-bar.paused .yanzi-auto-allow-progress-inner {
        background: #f59e0b;
      }
    `;
    (document.head || document.documentElement).appendChild(style);
  }

  function cleanString(str) {
    return String(str || "")
      .replace(/[\u200B-\u200D\u2060\uFEFF]/g, "")
      .replace(/\s+/g, " ")
      .trim();
  }

  function isElementVisible(el) {
    if (!el || !el.isConnected) return false;
    if (el.disabled || el.getAttribute("aria-disabled") === "true") return false;
    const style = window.getComputedStyle(el);
    if (style.display === "none" || style.visibility === "hidden" || style.opacity === "0") {
      return false;
    }
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function triggerElementClick(el) {
    if (!el) return false;
    try {
      el.scrollIntoView?.({ block: "nearest", inline: "nearest" });
      const mouseOptions = {
        bubbles: true,
        cancelable: true,
        view: window,
        buttons: 1
      };
      el.dispatchEvent(new PointerEvent("pointerdown", mouseOptions));
      el.dispatchEvent(new MouseEvent("mousedown", mouseOptions));
      el.dispatchEvent(new PointerEvent("pointerup", mouseOptions));
      el.dispatchEvent(new MouseEvent("mouseup", mouseOptions));
      el.click();
      return true;
    } catch (e) {
      console.error("[燕子自动允许] 点击触发异常:", e);
      try {
        el.click();
        return true;
      } catch (err) {
        return false;
      }
    }
  }

  function findDialogCandidates() {
    const dialogs = [];
    const allButtons = Array.from(document.querySelectorAll("button, [role='button']"));

    for (const btn of allButtons) {
      if (!isElementVisible(btn)) continue;
      const text = cleanString(btn.innerText || btn.textContent);
      
      const isAlways = text.includes("始终允许") || /always\s*allow/i.test(text);
      const isAllowOnce = text.includes("允许一次") || /allow\s*once/i.test(text);

      if (!isAlways && !isAllowOnce) continue;

      // 向上查找卡片容器
      let container = btn.closest("[role='dialog'], [role='alertdialog'], [data-testid*='confirm'], [data-testid*='approval']");
      if (!container) {
        let cur = btn.parentElement;
        let depth = 0;
        while (cur && depth < 6 && cur !== document.body) {
          const t = cur.textContent || "";
          if (t.includes("允许 ChatGPT 使用") || t.includes("Allow ChatGPT to use") || t.includes("Run shell command") || t.includes("查看详情") || t.includes("MCP")) {
            container = cur;
            break;
          }
          cur = cur.parentElement;
          depth++;
        }
      }
      if (!container) {
        container = btn.parentElement?.parentElement || btn.parentElement;
      }

      if (container && !dialogs.includes(container)) {
        dialogs.push(container);
      }
    }

    return dialogs;
  }

  function inspectDialog(container) {
    if (!container || !container.isConnected) return null;
    const buttons = Array.from(container.querySelectorAll("button, [role='button']")).filter(isElementVisible);

    let alwaysAllowBtn = null;
    let allowOnceBtn = null;
    let declineBtn = null;

    for (const btn of buttons) {
      const text = cleanString(btn.innerText || btn.textContent);
      if (text.includes("始终允许") || /always\s*allow/i.test(text)) {
        alwaysAllowBtn = btn;
      } else if (text.includes("允许一次") || /allow\s*once/i.test(text)) {
        allowOnceBtn = btn;
      } else if (text.includes("拒绝") || /decline|deny|cancel/i.test(text)) {
        declineBtn = btn;
      }
    }

    const targetBtn = (settings.preferAlways && alwaysAllowBtn) ? alwaysAllowBtn : (alwaysAllowBtn || allowOnceBtn);
    if (!targetBtn) return null;

    return {
      container,
      targetBtn,
      alwaysAllowBtn,
      allowOnceBtn,
      declineBtn,
      isAlways: targetBtn === alwaysAllowBtn
    };
  }

  function cancelTask(task, reason = "手动取消") {
    if (!task) return;
    if (task.timer) {
      clearInterval(task.timer);
      task.timer = null;
    }
    task.status = "cancelled";
    task.cancelReason = reason;

    if (task.uiBar) {
      task.uiBar.className = "yanzi-auto-allow-bar cancelled";
      task.uiBar.innerHTML = `
        <div class="yanzi-auto-allow-text">
          <span>已取消自动点击（${reason}）</span>
        </div>
      `;
      setTimeout(() => {
        task.uiBar?.remove();
        task.uiBar = null;
      }, 3000);
    }

    log(`倒计时已中止: ${reason}`);
    activeTasks.delete(task.container);
    processedElements.add(task.container);
  }

  function completeTask(task) {
    if (!task || task.status !== "running") return;
    if (task.timer) {
      clearInterval(task.timer);
      task.timer = null;
    }
    task.status = "done";

    log(`15s 倒计时结束，自动触发点击: ${cleanString(task.targetBtn.innerText || task.targetBtn.textContent)}`);

    if (task.uiBar) {
      task.uiBar.className = "yanzi-auto-allow-bar done";
      task.uiBar.innerHTML = `
        <div class="yanzi-auto-allow-text">
          <span>已自动点击【${task.isAlways ? "始终允许" : "允许一次"}】</span>
        </div>
      `;
    }

    triggerElementClick(task.targetBtn);

    setTimeout(() => {
      task.uiBar?.remove();
      task.uiBar = null;
    }, 2500);

    activeTasks.delete(task.container);
    processedElements.add(task.container);
  }

  function startCountdownTask(info) {
    const { container, targetBtn, declineBtn, isAlways } = info;
    if (activeTasks.has(container) || processedElements.has(container)) return;

    ensureStyle();

    const totalSeconds = Math.max(1, Number(settings.countdownSeconds) || 15);
    const task = {
      container,
      targetBtn,
      declineBtn,
      isAlways,
      totalSeconds,
      remainingSeconds: totalSeconds,
      deadline: Date.now() + totalSeconds * 1000,
      isPaused: false,
      status: "running",
      timer: null,
      uiBar: null
    };

    // 构建视觉条
    const bar = document.createElement("div");
    bar.className = "yanzi-auto-allow-bar";
    bar.innerHTML = `
      <div class="yanzi-auto-allow-text">
        <span class="yanzi-auto-allow-count">${totalSeconds}s</span>
        <span>后自动点击【${isAlways ? "始终允许" : "允许一次"}】</span>
      </div>
      <button type="button" class="yanzi-auto-allow-btn-cancel">取消</button>
      <div class="yanzi-auto-allow-progress">
        <div class="yanzi-auto-allow-progress-inner" style="width: 100%"></div>
      </div>
    `;

    // 挂载到卡片内部（优先插入在按钮操作行下方或卡片末尾）
    const targetParent = targetBtn.closest("div")?.parentElement || container;
    targetParent.appendChild(bar);
    task.uiBar = bar;

    const countLabel = bar.querySelector(".yanzi-auto-allow-count");
    const progressInner = bar.querySelector(".yanzi-auto-allow-progress-inner");
    const cancelBtn = bar.querySelector(".yanzi-auto-allow-btn-cancel");

    cancelBtn.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      cancelTask(task, "用户手动点击取消");
    });

    // 鼠标悬停防误触暂停
    bar.addEventListener("mouseenter", () => {
      if (task.status === "running") {
        task.isPaused = true;
        bar.classList.add("paused");
        if (countLabel) countLabel.textContent = "暂停";
      }
    });
    bar.addEventListener("mouseleave", () => {
      if (task.status === "running" && task.isPaused) {
        task.isPaused = false;
        task.deadline = Date.now() + task.remainingSeconds * 1000;
        bar.classList.remove("paused");
      }
    });

    // 监听卡片内部点击（若用户手动点击了按钮，取消自动任务）
    const onContainerClick = (e) => {
      if (bar.contains(e.target)) return;
      const clickedBtn = e.target.closest("button, [role='button']");
      if (clickedBtn) {
        cancelTask(task, "用户已手动交互");
        container.removeEventListener("click", onContainerClick, true);
      }
    };
    container.addEventListener("click", onContainerClick, true);

    // 监听 Esc 键
    const onKeydown = (e) => {
      if (e.key === "Escape" && task.status === "running") {
        cancelTask(task, "检测到 Esc 按键");
        window.removeEventListener("keydown", onKeydown, true);
      }
    };
    window.addEventListener("keydown", onKeydown, true);

    // 启动定时刷新
    task.timer = setInterval(() => {
      if (!container.isConnected || !targetBtn.isConnected) {
        cancelTask(task, "弹窗节点已被移除");
        return;
      }
      if (task.isPaused) return;

      const msLeft = task.deadline - Date.now();
      const secLeft = Math.max(0, Math.ceil(msLeft / 1000));
      task.remainingSeconds = secLeft;

      if (countLabel) countLabel.textContent = `${secLeft}s`;
      if (progressInner) {
        const percent = Math.max(0, Math.min(100, (msLeft / (task.totalSeconds * 1000)) * 100));
        progressInner.style.width = `${percent}%`;
      }

      if (secLeft <= 0) {
        completeTask(task);
      }
    }, 200);

    activeTasks.set(container, task);
    log(`已检测到授权弹窗，启动 ${totalSeconds}s 自动允许倒计时`, { isAlways });
  }

  function scanAndEvaluate() {
    if (!enabled || !settings.enabled) return;
    const candidates = findDialogCandidates();
    for (const dialog of candidates) {
      if (activeTasks.has(dialog) || processedElements.has(dialog)) continue;
      const info = inspectDialog(dialog);
      if (info) {
        startCountdownTask(info);
      }
    }
  }

  function startObservation() {
    if (scanTimer) clearInterval(scanTimer);
    scanTimer = setInterval(scanAndEvaluate, 800);

    if (!observer && document.body) {
      observer = new MutationObserver(() => {
        scanAndEvaluate();
      });
      observer.observe(document.body, { childList: true, subtree: true });
    }
    scanAndEvaluate();
  }

  function stopObservation() {
    if (scanTimer) {
      clearInterval(scanTimer);
      scanTimer = null;
    }
    if (observer) {
      observer.disconnect();
      observer = null;
    }
    for (const task of activeTasks.values()) {
      cancelTask(task, "功能已停用");
    }
    activeTasks.clear();
  }

  // 扩展存储通信
  async function loadSettings() {
    try {
      const res = await new Promise((resolve) => {
        chrome.runtime.sendMessage(
          {
            type: "yanzi_webapp_storage_get",
            appId: APP_ID,
            key: SETTINGS_KEY
          },
          (reply) => resolve(reply)
        );
      });
      if (res && res.ok && res.value && typeof res.value === "object") {
        settings = { ...DEFAULT_SETTINGS, ...res.value };
      }
    } catch (e) {
      // 容错降级
    }
    enabled = Boolean(settings.enabled);
    if (enabled) {
      startObservation();
    } else {
      stopObservation();
    }
  }

  // 扩展消息监听
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.appId !== APP_ID) return;

    if (message.type === "yanzi_webapp_ping") {
      sendResponse({ ok: true, appId: APP_ID, version: APP_VERSION });
      return;
    }

    if (message.type === "yanzi_webapp_command") {
      if (message.action === "disable") {
        enabled = false;
        stopObservation();
        sendResponse({ ok: true, enabled: false });
      } else if (message.action === "enable") {
        enabled = true;
        startObservation();
        sendResponse({ ok: true, enabled: true });
      }
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === SETTINGS_KEY) {
      settings = { ...DEFAULT_SETTINGS, ...(message.value || {}) };
      enabled = Boolean(settings.enabled);
      if (enabled) startObservation();
      else stopObservation();
      return;
    }

    if (message.type === "yanzi_webapp_probe") {
      let debugDom = null;

      if (message.debugLiveDom) {
        const matched = [];
        const all = Array.from(document.querySelectorAll("button, [role='button'], div, span, p"));
        for (const el of all) {
          const t = cleanString(el.innerText || el.textContent);
          if (t && (t.includes("始终允许") || t.includes("允许一次") || t.includes("公司浣熊") || t.includes("Run shell command") || t.includes("允许 ChatGPT 使用"))) {
            if (el.children.length === 0 || el.tagName === "BUTTON") {
              matched.push({
                tag: el.tagName,
                role: el.getAttribute("role") || "",
                text: t.slice(0, 100),
                className: String(el.className || "").slice(0, 80),
                visible: isElementVisible(el)
              });
            }
          }
        }
        debugDom = matched.slice(0, 15);
      }

      const candidates = findDialogCandidates();
      sendResponse({
        ok: true,
        appId: APP_ID,
        version: APP_VERSION,
        status: {
          enabled,
          countdownSeconds: settings.countdownSeconds,
          preferAlways: settings.preferAlways,
          activeTasksCount: activeTasks.size,
          detectedDialogsCount: candidates.length,
          recentLogs: recentLogs.slice(0, 5)
        },
        debugDom
      });
      return;
    }
  });

  // 启动与自恢复
  globalThis.__yanziChatGPTAutoAllowRefresh = () => {
    loadSettings();
    scanAndEvaluate();
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => loadSettings(), { once: true });
  } else {
    loadSettings();
  }
})();
