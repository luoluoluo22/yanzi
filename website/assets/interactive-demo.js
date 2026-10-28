(function () {
  const dragSelector = "[data-demo-file]";
  const aiTemplates = {
    folder: {
      prompt: "帮我做一个一键打开工作文件夹的工具",
      title: "打开工作文件夹",
      description: "点击后直接打开 D:\\Work",
      json: {
        name: "打开工作文件夹",
        description: "一键打开常用工作目录",
        icon: "folder",
        actions: [
          {
            type: "openPath",
            path: "D:\\Work"
          }
        ]
      }
    },
    translate: {
      prompt: "帮我做一个把选中文字翻译成中文的工具",
      title: "翻译选中文字",
      description: "读取当前选区并交给 AI 翻译",
      json: {
        name: "翻译选中文字",
        description: "把当前选中文字翻译成中文",
        icon: "ai",
        actions: [
          {
            type: "aiPrompt",
            input: "{{selectedText}}",
            prompt: "请把下面内容翻译成简体中文，保持原意：{{selectedText}}"
          }
        ]
      }
    },
    daily: {
      prompt: "帮我做一个每天 9 点打开日报网页的工具",
      title: "打开日报网页",
      description: "每天 9 点提醒并打开日报地址",
      json: {
        name: "打开日报网页",
        description: "每天 9 点打开团队日报页面",
        icon: "calendar",
        schedule: {
          type: "daily",
          time: "09:00"
        },
        actions: [
          {
            type: "openUrl",
            url: "https://example.com/daily-report"
          }
        ]
      }
    }
  };

  function initInteractiveDemo(root) {
    const files = Array.from(root.querySelectorAll(dragSelector));
    const dropzone = root.querySelector("[data-demo-dropzone]");
    const status = root.querySelector("[data-demo-status]");
    const resetButton = root.querySelector("[data-demo-reset]");
    const confettiLayer = root.querySelector("[data-demo-confetti]");
    const dropHint = root.querySelector(".demo-drop-hint");
    const fileModal = root.querySelector("[data-demo-file-modal]");

    if (!files.length || !dropzone || !status || !resetButton || !confettiLayer) {
      return;
    }

    let dragState = null;

    files.forEach((file) => {
      file.addEventListener("pointerdown", (event) => startDrag(event, file));
      file.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") {
          return;
        }

        event.preventDefault();
        completeDrop(file);
      });
    });

    resetButton.addEventListener("click", resetDemo);
    root.addEventListener("click", (event) => {
      const slot = event.target.closest(".demo-slot.is-added");
      if (slot && root.contains(slot)) {
        openFileModal(slot);
      }
    });
    root.addEventListener("keydown", (event) => {
      const slot = event.target.closest(".demo-slot.is-added");
      if (!slot || !root.contains(slot) || (event.key !== "Enter" && event.key !== " ")) {
        return;
      }

      event.preventDefault();
      openFileModal(slot);
    });
    fileModal?.querySelectorAll("[data-demo-modal-close]").forEach((button) => {
      button.addEventListener("click", closeFileModal);
    });
    fileModal?.addEventListener("click", (event) => {
      if (event.target === fileModal) {
        closeFileModal();
      }
    });

    function startDrag(event, source) {
      if (event.pointerType === "mouse" && event.button !== 0) {
        return;
      }

      event.preventDefault();

      const sourceRect = source.getBoundingClientRect();
      const ghost = source.cloneNode(true);
      const offsetX = event.clientX - sourceRect.left;
      const offsetY = event.clientY - sourceRect.top;

      ghost.classList.add("demo-drag-ghost");
      ghost.style.width = `${sourceRect.width}px`;
      document.body.appendChild(ghost);

      dragState = {
        ghost,
        offsetX,
        offsetY,
        source,
        pointerId: event.pointerId
      };

      source.classList.add("is-dragging");
      root.classList.add("is-dragging");
      status.textContent = "移动到右侧面板，松开即可添加";
      source.setPointerCapture?.(event.pointerId);
      moveGhost(event.clientX, event.clientY);

      window.addEventListener("pointermove", moveDrag);
      window.addEventListener("pointerup", endDrag);
      window.addEventListener("pointercancel", cancelDrag);
    }

    function moveDrag(event) {
      if (!dragState) {
        return;
      }

      moveGhost(event.clientX, event.clientY);
      dropzone.classList.toggle("is-hovered", isInsideDropzone(event.clientX, event.clientY));
    }

    function endDrag(event) {
      if (!dragState) {
        return;
      }

      const shouldDrop = isInsideDropzone(event.clientX, event.clientY);
      const source = dragState.source;
      cleanupDrag();

      if (shouldDrop) {
        completeDrop(source);
      } else {
        status.textContent = "还没放到面板里，再拖一次试试";
        dropzone.classList.add("is-missed");
        window.setTimeout(() => dropzone.classList.remove("is-missed"), 420);
      }
    }

    function cancelDrag() {
      cleanupDrag();
      status.textContent = "拖动一个文件到右侧鼠标面板";
    }

    function cleanupDrag() {
      if (!dragState) {
        return;
      }

      try {
        dragState.source.releasePointerCapture?.(dragState.pointerId);
      } catch {
      }
      dragState.source.classList.remove("is-dragging");
      dragState.ghost.remove();
      dragState = null;
      root.classList.remove("is-dragging");
      dropzone.classList.remove("is-hovered");
      window.removeEventListener("pointermove", moveDrag);
      window.removeEventListener("pointerup", endDrag);
      window.removeEventListener("pointercancel", cancelDrag);
    }

    function moveGhost(clientX, clientY) {
      if (!dragState) {
        return;
      }

      dragState.ghost.style.transform = `translate3d(${clientX - dragState.offsetX}px, ${clientY - dragState.offsetY}px, 0)`;
    }

    function isInsideDropzone(clientX, clientY) {
      const rect = dropzone.getBoundingClientRect();
      return clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom;
    }

    function completeDrop(source) {
      const slot = root.querySelector(".demo-slot.is-empty") || root.querySelector(".demo-slot.is-added");
      if (!slot) {
        return;
      }

      const color = source.dataset.fileColor || "#3b82f6";
      const kind = source.dataset.fileKind || "文件";
      const name = source.dataset.fileName || "新动作";

      root.classList.add("is-complete");
      dropzone.classList.add("is-success");
      slot.className = "demo-slot is-added is-openable";
      slot.style.setProperty("--slot-color", color);
      slot.dataset.openName = name;
      slot.dataset.openKind = kind;
      slot.dataset.openColor = color;
      slot.setAttribute("role", "button");
      slot.setAttribute("tabindex", "0");
      slot.setAttribute("aria-label", `打开 ${name}`);
      slot.innerHTML = `<span>${escapeHtml(kind.slice(0, 2))}</span><small>${escapeHtml(name)}</small>`;
      status.textContent = `已添加「${name}」，现在点击新槽位打开文件`;
      if (dropHint) {
        dropHint.textContent = "点击刚添加的槽位，会像真实鼠标面板一样打开对应文件。";
      }
      burstConfetti();
    }

    function resetDemo() {
      root.classList.remove("is-complete", "is-dragging");
      dropzone.classList.remove("is-success", "is-hovered", "is-missed");
      status.textContent = "拖动一个文件到右侧鼠标面板";
      if (dropHint) {
        dropHint.textContent = "拖入文件后，燕子会把它变成一个可点击的面板动作。";
      }
      root.querySelectorAll(".demo-slot.is-added").forEach((slot) => {
        slot.className = "demo-slot is-empty";
        slot.removeAttribute("style");
        slot.removeAttribute("role");
        slot.removeAttribute("tabindex");
        slot.removeAttribute("aria-label");
        delete slot.dataset.openName;
        delete slot.dataset.openKind;
        delete slot.dataset.openColor;
        slot.innerHTML = "<span>+</span><small>空槽</small>";
      });
      confettiLayer.replaceChildren();
      closeFileModal();
    }

    function openFileModal(slot) {
      if (!fileModal) {
        return;
      }

      const name = slot.dataset.openName || "示例文件";
      const kind = slot.dataset.openKind || "文件";
      const color = slot.dataset.openColor || "#3b82f6";
      const title = fileModal.querySelector("[data-demo-modal-title]");
      const meta = fileModal.querySelector("[data-demo-modal-meta]");
      const icon = fileModal.querySelector("[data-demo-modal-icon]");
      const body = fileModal.querySelector("[data-demo-modal-body]");

      if (title) {
        title.textContent = name;
      }
      if (meta) {
        meta.textContent = `已通过鼠标面板打开 · ${kind}`;
      }
      if (icon) {
        icon.textContent = kind.slice(0, 2);
        icon.style.background = color;
      }
      if (body) {
        body.innerHTML = buildFilePreview(name, kind);
      }

      fileModal.hidden = false;
      status.textContent = `已打开「${name}」`;
    }

    function closeFileModal() {
      if (fileModal) {
        fileModal.hidden = true;
      }
    }

    function burstConfetti() {
      if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        return;
      }

      confettiLayer.replaceChildren();
      const fragment = document.createDocumentFragment();

      for (let index = 0; index < 34; index += 1) {
        const piece = document.createElement("span");
        const angle = Math.random() * Math.PI * 2;
        const distance = 90 + Math.random() * 190;

        piece.style.setProperty("--dx", `${Math.cos(angle) * distance}px`);
        piece.style.setProperty("--dy", `${Math.sin(angle) * distance - 80}px`);
        piece.style.setProperty("--rotate", `${Math.random() * 520 - 260}deg`);
        piece.style.setProperty("--delay", `${Math.random() * 90}ms`);
        piece.style.background = ["#60a5fa", "#22c55e", "#f59e0b", "#f472b6", "#a78bfa"][index % 5];
        fragment.appendChild(piece);
      }

      confettiLayer.appendChild(fragment);
      window.setTimeout(() => confettiLayer.replaceChildren(), 1200);
    }
  }

  function initAiJsonDemo(root) {
    const promptButtons = Array.from(root.querySelectorAll("[data-ai-prompt]"));
    const status = root.querySelector("[data-ai-status]");
    const resetButton = root.querySelector("[data-ai-reset]");
    const openButton = root.querySelector("[data-ai-open]");
    const generateButton = root.querySelector("[data-ai-generate]");
    const pasteButton = root.querySelector("[data-ai-paste]");
    const userMessage = root.querySelector("[data-ai-user-message]");
    const loading = root.querySelector("[data-ai-loading]");
    const code = root.querySelector("[data-ai-code]");
    const editor = root.querySelector("[data-ai-editor]");
    const browser = root.querySelector("[data-ai-browser]");
    const url = root.querySelector("[data-ai-url]");
    const confettiLayer = root.querySelector("[data-ai-confetti]");

    if (!promptButtons.length || !status || !openButton || !generateButton || !pasteButton || !userMessage || !loading || !code || !editor || !confettiLayer) {
      return;
    }

    let selectedKey = promptButtons.find((button) => button.classList.contains("is-selected"))?.dataset.aiTemplate || "folder";
    let generatedJson = "";

    promptButtons.forEach((button) => {
      button.addEventListener("click", () => {
        selectedKey = button.dataset.aiTemplate || "folder";
        promptButtons.forEach((item) => item.classList.toggle("is-selected", item === button));
        applyTemplate(root.classList.contains("is-ai-open"));
      });
    });

    openButton.addEventListener("click", () => {
      root.classList.add("is-ai-open");
      browser?.classList.add("is-open");
      generateButton.disabled = false;
      if (url) {
        url.textContent = "ai.example.com / 生成燕子扩展 JSON";
      }
      status.textContent = "已跳转到 AI 网页，点击生成 JSON";
      loading.textContent = "AI 已收到你的需求，准备生成可粘贴的扩展 JSON。";
    });

    generateButton.addEventListener("click", () => {
      const template = getSelectedTemplate(selectedKey);
      root.classList.add("is-ai-generating");
      generateButton.disabled = true;
      pasteButton.disabled = true;
      status.textContent = "AI 正在生成扩展 JSON";
      loading.textContent = "生成中...";
      code.textContent = "";

      window.setTimeout(() => {
        generatedJson = JSON.stringify(template.json, null, 2);
        code.textContent = generatedJson;
        loading.textContent = `已生成「${template.title}」扩展 JSON。`;
        pasteButton.disabled = false;
        root.classList.remove("is-ai-generating");
        root.classList.add("is-ai-generated");
        status.textContent = "复制这段 JSON，然后粘贴到燕子扩展编辑器";
      }, 520);
    });

    pasteButton.addEventListener("click", () => {
      const template = getSelectedTemplate(selectedKey);
      if (!generatedJson) {
        return;
      }

      editor.innerHTML = `<pre><code>${escapeHtml(generatedJson)}</code></pre><strong>已添加：${escapeHtml(template.title)}</strong><small>${escapeHtml(template.description)}</small>`;
      editor.classList.add("is-pasted");
      root.classList.add("is-ai-pasted");
      status.textContent = `「${template.title}」已粘贴到燕子，下一步就是保存扩展`;
      burstLayerConfetti(confettiLayer, 82, 52);
    });

    resetButton?.addEventListener("click", resetAiDemo);
    applyTemplate(false);

    function applyTemplate(keepOpenState) {
      const template = getSelectedTemplate(selectedKey);
      userMessage.textContent = template.prompt;
      generatedJson = "";
      code.textContent = "// 这里会显示 AI 生成的扩展 JSON";
      loading.textContent = keepOpenState ? "点击生成 JSON。" : "打开 AI 网页后，点击生成 JSON。";
      editor.innerHTML = "<span>等待粘贴 AI 生成的 JSON</span>";
      editor.classList.remove("is-pasted");
      pasteButton.disabled = true;
      root.classList.remove("is-ai-generated", "is-ai-pasted", "is-ai-generating");
      status.textContent = keepOpenState ? "点击生成 JSON" : "选择一句需求，然后打开 AI 网页生成 JSON";
      if (!keepOpenState) {
        root.classList.remove("is-ai-open");
        browser?.classList.remove("is-open");
        generateButton.disabled = true;
        if (url) {
          url.textContent = "等待打开 AI 网页";
        }
      } else {
        generateButton.disabled = false;
      }
    }

    function resetAiDemo() {
      selectedKey = "folder";
      promptButtons.forEach((button) => button.classList.toggle("is-selected", button.dataset.aiTemplate === selectedKey));
      confettiLayer.replaceChildren();
      applyTemplate(false);
    }
  }

  function getSelectedTemplate(key) {
    return aiTemplates[key] || aiTemplates.folder;
  }

  function buildFilePreview(name, kind) {
    if (kind === "表格") {
      return `
        <div class="demo-sheet-preview">
          <div><strong>项目</strong><strong>数量</strong><strong>金额</strong></div>
          <div><span>设计服务</span><span>1</span><span>¥2,400</span></div>
          <div><span>开发联调</span><span>3</span><span>¥6,800</span></div>
          <div><span>交付合计</span><span>4</span><span>¥9,200</span></div>
        </div>`;
    }

    if (kind === "PDF") {
      return `
        <div class="demo-pdf-preview">
          <strong>${escapeHtml(name)}</strong>
          <p>客户资料已打开。这里用一个小弹窗模拟真实文件被唤起的效果，官网用户能立刻理解“面板动作 = 打开文件”。</p>
          <span>PDF 预览 · 3 页</span>
        </div>`;
    }

    return `
      <div class="demo-folder-preview">
        <span>图片</span>
        <span>报价</span>
        <span>合同</span>
        <span>素材</span>
      </div>`;
  }

  function burstLayerConfetti(layer, leftPercent, topPercent) {
    if (!layer || window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      return;
    }

    layer.replaceChildren();
    const fragment = document.createDocumentFragment();

    for (let index = 0; index < 34; index += 1) {
      const piece = document.createElement("span");
      const angle = Math.random() * Math.PI * 2;
      const distance = 90 + Math.random() * 190;

      piece.style.left = `${leftPercent}%`;
      piece.style.top = `${topPercent}%`;
      piece.style.setProperty("--dx", `${Math.cos(angle) * distance}px`);
      piece.style.setProperty("--dy", `${Math.sin(angle) * distance - 80}px`);
      piece.style.setProperty("--rotate", `${Math.random() * 520 - 260}deg`);
      piece.style.setProperty("--delay", `${Math.random() * 90}ms`);
      piece.style.background = ["#60a5fa", "#22c55e", "#f59e0b", "#f472b6", "#a78bfa"][index % 5];
      fragment.appendChild(piece);
    }

    layer.appendChild(fragment);
    window.setTimeout(() => layer.replaceChildren(), 1200);
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (char) => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      "\"": "&quot;",
      "'": "&#039;"
    })[char]);
  }

  window.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-interactive-demo='mouse-panel']").forEach(initInteractiveDemo);
    document.querySelectorAll("[data-interactive-demo='ai-json']").forEach(initAiJsonDemo);
  });
})();
