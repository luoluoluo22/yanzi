(function () {
  /* ═══════════════════════════════════════════════════════════
   *  Demo 1 — 长按右键呼出面板 → 拖文件到空槽 → 点击执行
   * ═══════════════════════════════════════════════════════════ */
  function initPanelDemo(root) {
    const desktop = root.querySelector("[data-panel-desktop]");
    const panel = root.querySelector("[data-panel-grid]");
    const hint = root.querySelector("[data-panel-hint]");
    const status = root.querySelector("[data-panel-status]");
    const resetBtn = root.querySelector("[data-panel-reset]");
    const confetti = root.querySelector("[data-panel-confetti]");
    const fileShelf = root.querySelector("[data-panel-files]");
    const files = root.querySelectorAll("[data-panel-file]");
    const slots = root.querySelectorAll("[data-panel-slot]");
    const emptySlots = root.querySelectorAll("[data-panel-slot].is-empty");
    const modal = root.querySelector("[data-panel-modal]");
    const modalTitle = root.querySelector("[data-panel-modal-title]");
    const modalBody = root.querySelector("[data-panel-modal-body]");
    const modalClose = root.querySelector("[data-panel-modal-close]");

    if (!desktop || !panel || !status) return;

    let phase = "hold"; // hold → drag → click
    let holdTimer = null;
    let panelRevealed = false;
    let dragState = null;
    let filledCount = 0;

    // Phase 1: Long press to reveal panel
    desktop.addEventListener("pointerdown", (e) => {
      if (phase !== "hold" || panelRevealed) return;
      if (e.button !== 2 && e.pointerType === "mouse") {
        // Allow left click too for demo purposes
      }
      e.preventDefault();
      hint.classList.add("is-pressing");
      status.textContent = "继续按住... 不要松开";

      holdTimer = setTimeout(() => {
        revealPanel();
      }, 800);
    });

    desktop.addEventListener("pointerup", () => {
      if (phase === "hold" && !panelRevealed) {
        clearTimeout(holdTimer);
        hint.classList.remove("is-pressing");
        status.textContent = "按住鼠标不放（模拟长按右键），等面板弹出";
      }
    });

    desktop.addEventListener("pointerleave", () => {
      if (phase === "hold" && !panelRevealed) {
        clearTimeout(holdTimer);
        hint.classList.remove("is-pressing");
      }
    });

    // Prevent context menu
    desktop.addEventListener("contextmenu", (e) => e.preventDefault());

    function revealPanel() {
      panelRevealed = true;
      phase = "drag";
      panel.classList.add("is-visible");
      hint.classList.add("is-hidden");
      fileShelf.classList.add("is-visible");
      status.textContent = "面板弹出了！现在把左边的文件拖到空白槽位 →";
      burstConfetti(confetti);
    }

    // Phase 2: Drag files to slots
    files.forEach((file) => {
      file.addEventListener("pointerdown", (e) => {
        if (phase !== "drag") return;
        e.preventDefault();
        startFileDrag(e, file);
      });
    });

    function startFileDrag(e, file) {
      const rect = file.getBoundingClientRect();
      const ghost = file.cloneNode(true);
      ghost.classList.add("panel-drag-ghost");
      ghost.style.width = rect.width + "px";
      document.body.appendChild(ghost);

      dragState = {
        ghost,
        file,
        offsetX: e.clientX - rect.left,
        offsetY: e.clientY - rect.top,
      };

      file.classList.add("is-dragging");
      moveGhost(e.clientX, e.clientY);

      window.addEventListener("pointermove", onDragMove);
      window.addEventListener("pointerup", onDragEnd);
    }

    function onDragMove(e) {
      if (!dragState) return;
      moveGhost(e.clientX, e.clientY);

      // Highlight nearest empty slot
      emptySlots.forEach((slot) => {
        if (slot.classList.contains("is-filled")) return;
        const r = slot.getBoundingClientRect();
        const inside = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
        slot.classList.toggle("is-hovered", inside);
      });
    }

    function onDragEnd(e) {
      if (!dragState) return;
      window.removeEventListener("pointermove", onDragMove);
      window.removeEventListener("pointerup", onDragEnd);

      // Find target slot
      let targetSlot = null;
      emptySlots.forEach((slot) => {
        if (slot.classList.contains("is-filled")) return;
        const r = slot.getBoundingClientRect();
        if (e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom) {
          targetSlot = slot;
        }
        slot.classList.remove("is-hovered");
      });

      if (targetSlot) {
        fillSlot(targetSlot, dragState.file);
      } else {
        status.textContent = "没放到槽位上，再试一次，拖到空白的 + 格子里";
      }

      dragState.file.classList.remove("is-dragging");
      dragState.ghost.remove();
      dragState = null;
    }

    function fillSlot(slot, file) {
      const icon = file.dataset.fileIcon;
      const name = file.dataset.fileName;
      const color = file.dataset.fileColor;

      slot.classList.add("is-filled");
      slot.classList.remove("is-empty");
      slot.dataset.slotName = name;
      slot.dataset.slotColor = color;
      slot.innerHTML = `<span class="panel-slot-icon">${icon}</span><small>${name}</small>`;
      slot.style.setProperty("--slot-color", color);

      file.classList.add("is-placed");
      filledCount++;

      if (filledCount >= 3 || filledCount >= files.length) {
        phase = "click";
        status.textContent = "全部添加完毕！现在点击面板里的按钮试试 →";
        fileShelf.classList.add("is-done");
      } else {
        status.textContent = `已添加「${name}」！继续拖下一个文件到空槽位`;
      }

      burstConfetti(confetti);
    }

    function moveGhost(x, y) {
      if (!dragState) return;
      dragState.ghost.style.transform = `translate3d(${x - dragState.offsetX}px,${y - dragState.offsetY}px,0)`;
    }

    // Phase 3: Click slots to execute
    root.addEventListener("click", (e) => {
      const slot = e.target.closest("[data-panel-slot].is-filled");
      if (!slot || phase !== "click") return;

      const name = slot.dataset.slotName || "文件";
      openModal(name, slot.dataset.slotColor);
    });

    function openModal(name, color) {
      if (!modal) return;
      modalTitle.textContent = name;
      modalBody.innerHTML = buildPreview(name);
      modal.style.setProperty("--modal-color", color || "#3b82f6");
      modal.classList.add("is-visible");
      status.textContent = `✓ 已打开「${name}」— 从添加到使用，就是这么快！`;
    }

    modalClose?.addEventListener("click", () => modal.classList.remove("is-visible"));
    modal?.addEventListener("click", (e) => {
      if (e.target === modal) modal.classList.remove("is-visible");
    });

    function buildPreview(name) {
      if (name.includes("报价")) {
        return `<div class="modal-preview-table"><div><strong>项目</strong><strong>金额</strong></div><div><span>设计</span><span>¥2,400</span></div><div><span>开发</span><span>¥6,800</span></div></div>`;
      }
      if (name.includes("文件夹") || name.includes("素材")) {
        return `<div class="modal-preview-folder"><span>📷 图片</span><span>📄 文档</span><span>🎨 素材</span><span>📦 归档</span></div>`;
      }
      return `<div class="modal-preview-generic"><p>「${escapeHtml(name)}」已通过鼠标面板快速打开。<br>在真实的燕子中，这会直接启动对应的文件或程序。</p></div>`;
    }

    // Reset
    resetBtn?.addEventListener("click", () => {
      phase = "hold";
      panelRevealed = false;
      filledCount = 0;
      dragState = null;
      clearTimeout(holdTimer);
      panel.classList.remove("is-visible");
      hint.classList.remove("is-hidden", "is-pressing");
      fileShelf.classList.remove("is-visible", "is-done");
      modal?.classList.remove("is-visible");
      confetti?.replaceChildren();
      files.forEach((f) => f.classList.remove("is-placed", "is-dragging"));
      emptySlots.forEach((slot) => {
        slot.classList.remove("is-filled", "is-hovered");
        slot.classList.add("is-empty");
        slot.innerHTML = `<span class="panel-slot-icon">+</span><small>空槽</small>`;
        slot.removeAttribute("style");
        delete slot.dataset.slotName;
        delete slot.dataset.slotColor;
      });
      status.textContent = "按住鼠标不放（模拟长按右键），等面板弹出";
    });
  }

  /* ═══════════════════════════════════════════════════════════
   *  Demo 2 — Alt+空格 弹出搜索 → 输入搜索
   * ═══════════════════════════════════════════════════════════ */
  function initSearchDemo(root) {
    const trigger = root.querySelector("[data-search-trigger]");
    const launcher = root.querySelector("[data-search-launcher]");
    const input = root.querySelector("[data-search-input]");
    const list = root.querySelector("[data-search-list]");
    const status = root.querySelector("[data-search-status]");
    const resetBtn = root.querySelector("[data-search-reset]");
    const resultToast = root.querySelector("[data-search-result]");
    const resultTitle = root.querySelector("[data-search-result-title]");

    if (!trigger || !launcher || !input || !list || !status) return;

    const items = [
      { name: "微信", pinyin: "wx", icon: "💬", color: "#22c55e", desc: "即时通讯" },
      { name: "谷歌翻译", pinyin: "ggfy", icon: "🌐", color: "#3b82f6", desc: "在线翻译" },
      { name: "文件管理器", pinyin: "wjglq", icon: "📁", color: "#f59e0b", desc: "系统工具" },
      { name: "截图工具", pinyin: "jtgj", icon: "✂️", color: "#ef4444", desc: "屏幕截图" },
      { name: "计算器", pinyin: "jsq", icon: "🧮", color: "#8b5cf6", desc: "系统工具" },
      { name: "记事本", pinyin: "jsb", icon: "📝", color: "#06b6d4", desc: "文本编辑" },
      { name: "VS Code", pinyin: "vscode", icon: "💻", color: "#007acc", desc: "代码编辑器" },
    ];

    let opened = false;

    trigger.addEventListener("click", () => {
      if (opened) return;
      openLauncher();
    });

    // Also listen for Alt+Space on the root
    root.addEventListener("keydown", (e) => {
      if (e.altKey && e.code === "Space") {
        e.preventDefault();
        if (!opened) openLauncher();
      }
    });

    function openLauncher() {
      opened = true;
      launcher.classList.add("is-visible");
      trigger.classList.add("is-pressed");
      status.textContent = "搜索框已弹出！输入关键词或拼音缩写试试";
      renderList(items);
      setTimeout(() => input.focus(), 100);
    }

    input.addEventListener("input", () => {
      const q = input.value.trim().toLowerCase();
      if (!q) {
        renderList(items);
        status.textContent = "输入关键词或拼音缩写，秒速找到";
        return;
      }
      const filtered = items.filter(
        (i) => i.name.toLowerCase().includes(q) || i.pinyin.includes(q)
      );
      renderList(filtered);
      status.textContent = filtered.length
        ? `找到 ${filtered.length} 个结果，点击或按 Enter 执行`
        : "没有匹配结果，换个关键词试试";
    });

    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        const first = list.querySelector(".search-item");
        if (first) first.click();
      }
    });

    function renderList(data) {
      list.innerHTML = data
        .map(
          (item, i) => `
        <button class="search-item ${i === 0 ? "is-highlight" : ""}" data-search-item data-name="${escapeHtml(item.name)}">
          <span class="search-item-icon" style="background:${item.color}18;color:${item.color}">${item.icon}</span>
          <span class="search-item-info">
            <strong>${escapeHtml(item.name)}</strong>
            <small>${escapeHtml(item.desc)}</small>
          </span>
          <span class="search-item-hint">${i === 0 ? "↵" : ""}</span>
        </button>`
        )
        .join("");

      list.querySelectorAll("[data-search-item]").forEach((btn) => {
        btn.addEventListener("click", () => executeItem(btn.dataset.name));
      });
    }

    function executeItem(name) {
      status.textContent = `✓ 已启动「${name}」— 从呼出到执行不到 1 秒`;
      if (resultToast && resultTitle) {
        resultTitle.textContent = name;
        resultToast.classList.add("is-visible");
        // Keep visible longer so user sees the feedback clearly
        setTimeout(() => resultToast.classList.remove("is-visible"), 3500);
      }
      // Also show a simulated app window
      const appModal = root.querySelector("[data-search-app-modal]");
      const appModalTitle = root.querySelector("[data-search-app-title]");
      if (appModal && appModalTitle) {
        appModalTitle.textContent = name;
        appModal.classList.add("is-visible");
      }
    }

    // Close app modal
    const appModal = root.querySelector("[data-search-app-modal]");
    const appModalClose = root.querySelector("[data-search-app-close]");
    appModalClose?.addEventListener("click", () => appModal?.classList.remove("is-visible"));
    appModal?.addEventListener("click", (e) => {
      if (e.target === appModal) appModal.classList.remove("is-visible");
    });

    resetBtn?.addEventListener("click", () => {
      opened = false;
      launcher.classList.remove("is-visible");
      trigger.classList.remove("is-pressed");
      input.value = "";
      resultToast?.classList.remove("is-visible");
      const appModal = root.querySelector("[data-search-app-modal]");
      appModal?.classList.remove("is-visible");
      status.textContent = "点击下方按钮模拟按下 Alt + 空格";
    });
  }

  /* ═══════════════════════════════════════════════════════════
   *  Demo 3 — 内置模板 + AI 生成 → 安装 → 面板弹出 → 点击打开
   * ═══════════════════════════════════════════════════════════ */
  function initAiDemo(root) {
    const prompts = root.querySelectorAll("[data-ai2-prompt]");
    const chatBubble = root.querySelector("[data-ai2-chat]");
    const codeBlock = root.querySelector("[data-ai2-code]");
    const generateBtn = root.querySelector("[data-ai2-generate]");
    const installBtn = root.querySelector("[data-ai2-install]");
    const status = root.querySelector("[data-ai2-status]");
    const resetBtn = root.querySelector("[data-ai2-reset]");
    const confetti = root.querySelector("[data-ai2-confetti]");
    const panelOverlay = root.querySelector("[data-ai2-panel-overlay]");
    const panelNewSlot = root.querySelector("[data-ai2-new-slot]");
    const modelSelector = root.querySelector("[data-ai2-model]");
    const modelName = root.querySelector("[data-ai2-model-name]");
    const openResult = root.querySelector("[data-ai2-open-result]");

    if (!prompts.length || !codeBlock || !generateBtn || !status) return;

    const templates = {
      folder: {
        prompt: "帮我做一个打开工作文件夹的按钮",
        name: "打开工作文件夹",
        icon: "📁",
        json: `{
  "id": "open-work-folder",
  "name": "打开工作文件夹",
  "version": "0.1.0",
  "category": "目录",
  "description": "一键打开常用工作目录",
  "icon": "mdi:folder",
  "openTarget": "D:\\\\Work"
}`,
      },
      translate: {
        prompt: "做一个翻译选中文字的工具",
        name: "翻译选中文字",
        icon: "🌐",
        json: `{
  "id": "translate-selection",
  "name": "翻译选中文字",
  "version": "0.1.0",
  "category": "AI",
  "description": "选中文字后一键翻译成中文",
  "icon": "mdi:translate",
  "runtime": "csharp",
  "entryMode": "inline",
  "script": {
    "source": "// AI 生成的翻译脚本..."
  }
}`,
      },
      search: {
        prompt: "做一个谷歌搜索的快捷命令",
        name: "谷歌搜索",
        icon: "🔍",
        json: `{
  "id": "google-search",
  "name": "谷歌搜索",
  "version": "0.1.0",
  "category": "搜索",
  "description": "输入关键词直接谷歌搜索",
  "keywords": ["谷歌", "google", "gg"],
  "queryPrefixes": ["谷歌", "gg"],
  "queryTargetTemplate": "https://google.com/search?q={query}"
}`,
      },
    };

    const models = [
      { id: "doubao", name: "豆包", color: "#6366f1" },
      { id: "deepseek", name: "DeepSeek", color: "#0ea5e9" },
      { id: "gpt", name: "ChatGPT", color: "#10a37f" },
      { id: "claude", name: "Claude", color: "#d97706" },
    ];

    let selectedKey = "folder";
    let selectedModel = 0;
    let typing = false;
    let phase = "select"; // select → generate → install → panel → open

    // Model selector
    if (modelSelector) {
      modelSelector.addEventListener("click", () => {
        selectedModel = (selectedModel + 1) % models.length;
        const m = models[selectedModel];
        modelName.textContent = m.name;
        modelSelector.style.setProperty("--model-color", m.color);
      });
    }

    prompts.forEach((btn) => {
      btn.addEventListener("click", () => {
        if (typing) return;
        selectedKey = btn.dataset.ai2Template || "folder";
        prompts.forEach((p) => p.classList.toggle("is-selected", p === btn));
        chatBubble.textContent = templates[selectedKey].prompt;
        codeBlock.textContent = "";
        installBtn && (installBtn.disabled = true);
        panelOverlay?.classList.remove("is-visible");
        openResult?.classList.remove("is-visible");
        phase = "select";
        status.textContent = "需求已选好，点击「让 AI 生成」";
      });
    });

    generateBtn.addEventListener("click", () => {
      if (typing) return;
      typing = true;
      phase = "generate";
      generateBtn.disabled = true;
      installBtn && (installBtn.disabled = true);
      codeBlock.textContent = "";
      const m = models[selectedModel];
      status.textContent = `${m.name} 正在生成扩展 JSON...`;

      const json = templates[selectedKey].json;
      let i = 0;
      const interval = setInterval(() => {
        // Type 2-3 chars at a time for speed
        const chunk = json.slice(i, i + 2);
        codeBlock.textContent += chunk;
        i += 2;
        if (i >= json.length) {
          codeBlock.textContent = json; // ensure complete
          clearInterval(interval);
          typing = false;
          generateBtn.disabled = false;
          installBtn && (installBtn.disabled = false);
          phase = "install";
          status.textContent = "生成完毕！点击「安装到鼠标面板」";
        }
      }, 18);
    });

    installBtn?.addEventListener("click", () => {
      if (phase !== "install") return;
      phase = "panel";
      installBtn.disabled = true;

      // Show panel overlay with new slot
      const t = templates[selectedKey];
      if (panelNewSlot) {
        panelNewSlot.innerHTML = `<span>${t.icon}</span><small>${escapeHtml(t.name.slice(0, 4))}</small>`;
      }
      panelOverlay?.classList.add("is-visible");
      status.textContent = `已安装「${t.name}」！点击面板中新增的按钮试试 →`;
      burstConfetti(confetti);

      // Make new slot clickable
      if (panelNewSlot) {
        panelNewSlot.onclick = () => {
          if (phase !== "panel") return;
          phase = "open";
          openResult?.classList.add("is-visible");
          status.textContent = `✓ 已打开「${t.name}」— 从 AI 生成到使用，一气呵成！`;
          burstConfetti(confetti);
        };
      }
    });

    resetBtn?.addEventListener("click", () => {
      typing = false;
      phase = "select";
      selectedKey = "folder";
      prompts.forEach((p) =>
        p.classList.toggle("is-selected", p.dataset.ai2Template === "folder")
      );
      chatBubble.textContent = templates.folder.prompt;
      codeBlock.textContent = "";
      generateBtn.disabled = false;
      installBtn && (installBtn.disabled = true);
      panelOverlay?.classList.remove("is-visible");
      openResult?.classList.remove("is-visible");
      confetti?.replaceChildren();
      status.textContent = "选一个需求，然后让 AI 生成";
    });
  }

  /* ═══════════════════════════════════════════════════════════
   *  共用工具
   * ═══════════════════════════════════════════════════════════ */
  function burstConfetti(layer) {
    if (!layer || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    layer.replaceChildren();
    for (let i = 0; i < 30; i++) {
      const p = document.createElement("span");
      const angle = Math.random() * Math.PI * 2;
      const dist = 80 + Math.random() * 160;
      p.style.setProperty("--dx", `${Math.cos(angle) * dist}px`);
      p.style.setProperty("--dy", `${Math.sin(angle) * dist - 60}px`);
      p.style.setProperty("--rotate", `${Math.random() * 500 - 250}deg`);
      p.style.setProperty("--delay", `${Math.random() * 80}ms`);
      p.style.background = ["#60a5fa", "#22c55e", "#f59e0b", "#f472b6", "#a78bfa"][i % 5];
      layer.appendChild(p);
    }
    setTimeout(() => layer.replaceChildren(), 1100);
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#039;"
    })[c]);
  }

  /* ═══════════════════════════════════════════════════════════
   *  初始化
   * ═══════════════════════════════════════════════════════════ */
  window.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-demo-v2='panel']").forEach(initPanelDemo);
    document.querySelectorAll("[data-demo-v2='search']").forEach(initSearchDemo);
    document.querySelectorAll("[data-demo-v2='ai']").forEach(initAiDemo);
  });
})();
