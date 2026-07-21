(() => {
  "use strict";

  const TOOLS = {
    recent: [
      { id: "clip", title: "剪贴板", glyph: "剪", color: "#3B82F6" },
      { id: "shot", title: "截图", glyph: "截", color: "#0EA5E9" },
      { id: "trans", title: "翻译", glyph: "译", color: "#14B8A6" },
    ],
    app: [
      { id: "fmt", title: "格式化", glyph: "格", color: "#8B5CF6", app: "Cursor" },
      { id: "jump", title: "跳转定义", glyph: "跳", color: "#6366F1", app: "Cursor" },
      { id: "ref", title: "重构", glyph: "重", color: "#EC4899", app: "Cursor" },
    ],
    all: [
      { id: "clip", title: "剪贴板", glyph: "剪", color: "#3B82F6", group: "通用" },
      { id: "shot", title: "截图", glyph: "截", color: "#0EA5E9", group: "通用" },
      { id: "trans", title: "翻译", glyph: "译", color: "#14B8A6", group: "通用" },
      { id: "note", title: "笔记", glyph: "记", color: "#F59E0B", group: "通用" },
      { id: "cal", title: "日历", glyph: "历", color: "#F97316", group: "通用" },
      { id: "term", title: "终端", glyph: "终", color: "#22C55E", group: "开发" },
      { id: "git", title: "Git", glyph: "G", color: "#EF4444", group: "开发" },
      { id: "fmt", title: "格式化", glyph: "格", color: "#8B5CF6", group: "Cursor" },
      { id: "jump", title: "跳转定义", glyph: "跳", color: "#6366F1", group: "Cursor" },
      { id: "ref", title: "重构", glyph: "重", color: "#EC4899", group: "Cursor" },
      { id: "polish", title: "润色", glyph: "润", color: "#2DD4BF", group: "写作" },
      { id: "sum", title: "摘要", glyph: "摘", color: "#38BDF8", group: "写作" },
    ],
  };

  const META = {
    peek: {
      title: "A · 先轻后重 Peek",
      hint: "在演示区点击：先出现快捷条。悬停约 0.45s 或点 ▾ 展开；点工具立即执行并收起。",
      points: "核心假设：唤出后 1 秒内用户只需要 3～6 个工具。全面板（分组/空位/编辑）推迟到「刻意展开」。解决扫视过载与光标遮挡。",
    },
    radial: {
      title: "B · 扇形手势 Radial",
      hint: "按住鼠标左键拖向某个扇区高亮，松开即执行。点中心可切换「通用 / 当前应用」两圈。",
      points: "适合现有「中键长按 / 拖出」触发。方向记忆比格子坐标更快；不适合超多工具（建议每圈 ≤8）。",
    },
    hub: {
      title: "C · 搜索优先 Hub",
      hint: "唤出后面板带搜索框自动聚焦。↑↓ 选择，Enter 执行。顶部自动显示当前应用上下文。",
      points: "侧栏单字分组改为可读 Tab；工具多时搜索是主路径，格子是浏览路径。和 Raycast / 命令面板心智一致。",
    },
  };

  let concept = "peek";
  let layer = null;
  let expandTimer = null;
  let hubIndex = 0;
  let hubTab = "当前";
  let hubQuery = "";
  let radialMode = "app"; // app | general

  const stage = document.getElementById("stage");
  const stageTitle = document.getElementById("stageTitle");
  const stageHint = document.getElementById("stageHint");
  const stagePoints = document.getElementById("stagePoints");
  const toastEl = document.getElementById("toast");

  function toast(msg) {
    toastEl.textContent = msg;
    toastEl.hidden = false;
    clearTimeout(toast._t);
    toast._t = setTimeout(() => (toastEl.hidden = true), 1400);
  }

  function clearLayer() {
    clearTimeout(expandTimer);
    expandTimer = null;
    layer = null;
    stage.innerHTML = "";
  }

  function setConcept(next) {
    concept = next;
    document.querySelectorAll(".concept-card").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.concept === next);
    });
    const m = META[next];
    stageTitle.textContent = m.title;
    stageHint.textContent = m.hint;
    stagePoints.textContent = m.points;
    clearLayer();
  }

  function run(tool) {
    toast(`执行 · ${tool.title}`);
    clearLayer();
  }

  function place(el, x, y, w = 0, h = 0) {
    const rect = stage.getBoundingClientRect();
    let left = x - rect.left;
    let top = y - rect.top;
    if (w) left = Math.max(12, Math.min(left, rect.width - w - 12));
    if (h) top = Math.max(12, Math.min(top, rect.height - h - 12));
    el.style.left = `${left}px`;
    el.style.top = `${top}px`;
  }

  // —— A Peek ——
  function showPeek(clientX, clientY) {
    clearLayer();
    const strip = document.createElement("div");
    strip.className = "peek-strip";
    strip.innerHTML = `
      <div class="tag">Cursor · 快捷</div>
      ${TOOLS.recent
        .map(
          (t) => `
        <button type="button" class="chip" data-id="${t.id}">
          <div class="ico" style="background:${t.color}">${t.glyph}</div>
          <span>${t.title}</span>
        </button>`
        )
        .join("")}
      <div class="divider"></div>
      ${TOOLS.app
        .map(
          (t) => `
        <button type="button" class="chip" data-id="${t.id}">
          <div class="ico" style="background:${t.color}">${t.glyph}</div>
          <span>${t.title}</span>
        </button>`
        )
        .join("")}
      <button type="button" class="expand" title="展开全面板" aria-label="展开">▾</button>
    `;
    stage.appendChild(strip);
    place(strip, clientX, clientY - 8, 420, 70);
    // offset so strip sits just below cursor, not under finger
    strip.style.top = `${parseFloat(strip.style.top) + 18}px`;
    layer = { type: "peek", el: strip, x: clientX, y: clientY };

    strip.querySelectorAll(".chip").forEach((btn) => {
      btn.addEventListener("click", (e) => {
        e.stopPropagation();
        const t = [...TOOLS.recent, ...TOOLS.app].find((x) => x.id === btn.dataset.id);
        if (t) run(t);
      });
    });

    const expand = () => showPeekExpanded(clientX, clientY);
    strip.querySelector(".expand").addEventListener("click", (e) => {
      e.stopPropagation();
      expand();
    });
    strip.addEventListener("mouseenter", () => {
      expandTimer = setTimeout(expand, 450);
    });
    strip.addEventListener("mouseleave", () => {
      clearTimeout(expandTimer);
    });
  }

  function showPeekExpanded(clientX, clientY) {
    clearTimeout(expandTimer);
    stage.innerHTML = "";
    const panel = document.createElement("div");
    panel.className = "peek-panel";
    let tab = "app";
    const render = () => {
      const items = tab === "app" ? TOOLS.app.concat(TOOLS.recent) : TOOLS.all.filter((t) => t.group !== "Cursor");
      panel.innerHTML = `
        <div class="tabs">
          <button type="button" class="tab ${tab === "app" ? "active" : ""}" data-tab="app">当前 · Cursor</button>
          <button type="button" class="tab ${tab === "all" ? "active" : ""}" data-tab="all">通用工具</button>
        </div>
        <div class="body">
          <div class="grid">
            ${items
              .map(
                (t) => `
              <button type="button" class="cell" data-id="${t.id}">
                <div class="ico" style="background:${t.color}">${t.glyph}</div>
                <em>${t.title}</em>
              </button>`
              )
              .join("")}
          </div>
        </div>`;
      panel.querySelectorAll(".tab").forEach((b) =>
        b.addEventListener("click", (e) => {
          e.stopPropagation();
          tab = b.dataset.tab;
          render();
        })
      );
      panel.querySelectorAll(".cell").forEach((b) =>
        b.addEventListener("click", (e) => {
          e.stopPropagation();
          const t = TOOLS.all.find((x) => x.id === b.dataset.id);
          if (t) run(t);
        })
      );
    };
    render();
    stage.appendChild(panel);
    place(panel, clientX - 40, clientY + 12, 280, 280);
    layer = { type: "peek-panel", el: panel };
  }

  // —— B Radial ——
  function showRadial(clientX, clientY) {
    clearLayer();
    const root = document.createElement("div");
    root.className = "radial";
    const items = radialMode === "app" ? TOOLS.app.concat(TOOLS.recent.slice(0, 2)) : TOOLS.recent.concat(TOOLS.all.filter((t) => t.group === "开发").slice(0, 2));
    const n = items.length;
    root.innerHTML = `
      <div class="ring"></div>
      <button type="button" class="center">${radialMode === "app" ? "Cursor<br/>点此切换" : "通用<br/>点此切换"}</button>
      ${items
        .map((t, i) => {
          const angle = (360 / n) * i - 90;
          return `
          <button type="button" class="wedge" data-id="${t.id}" style="transform: rotate(${angle}deg) translateY(-96px) rotate(${-angle}deg)">
            <div class="ico" style="background:${t.color}">${t.glyph}</div>
            <span>${t.title}</span>
          </button>`;
        })
        .join("")}
    `;
    stage.appendChild(root);
    place(root, clientX, clientY);
    // radial uses margin centering; left/top are center point
    root.style.left = root.style.left;
    root.style.top = root.style.top;
    layer = { type: "radial", el: root, cx: clientX, cy: clientY, items };

    root.querySelector(".center").addEventListener("click", (e) => {
      e.stopPropagation();
      radialMode = radialMode === "app" ? "general" : "app";
      showRadial(clientX, clientY);
    });

    root.querySelectorAll(".wedge").forEach((w) => {
      w.addEventListener("click", (e) => {
        e.stopPropagation();
        const t = items.find((x) => x.id === w.dataset.id);
        if (t) run(t);
      });
    });

    const onMove = (e) => highlightRadial(e.clientX, e.clientY);
    const onUp = (e) => {
      stage.removeEventListener("pointermove", onMove);
      stage.removeEventListener("pointerup", onUp);
      const hot = root.querySelector(".wedge.is-hot");
      if (hot) {
        const t = items.find((x) => x.id === hot.dataset.id);
        if (t) run(t);
      }
    };
    stage.addEventListener("pointermove", onMove);
    stage.addEventListener("pointerup", onUp);
  }

  function highlightRadial(x, y) {
    if (!layer || layer.type !== "radial") return;
    const rect = stage.getBoundingClientRect();
    const cx = parseFloat(layer.el.style.left) + rect.left;
    const cy = parseFloat(layer.el.style.top) + rect.top;
    const dx = x - cx;
    const dy = y - cy;
    const dist = Math.hypot(dx, dy);
    const wedges = [...layer.el.querySelectorAll(".wedge")];
    wedges.forEach((w) => w.classList.remove("is-hot"));
    if (dist < 36 || dist > 140) return;
    let angle = (Math.atan2(dy, dx) * 180) / Math.PI; // -180..180, 0 = east
    angle = (angle + 90 + 360) % 360; // 0 = north
    const n = wedges.length;
    const sector = 360 / n;
    const idx = Math.round(angle / sector) % n;
    wedges[idx]?.classList.add("is-hot");
  }

  // —— C Hub ——
  function showHub(clientX, clientY) {
    clearLayer();
    hubIndex = 0;
    hubQuery = "";
    const hub = document.createElement("div");
    hub.className = "hub";
    stage.appendChild(hub);
    place(hub, clientX - 40, clientY - 20, 340, 420);
    layer = { type: "hub", el: hub };
    renderHub();
    queueMicrotask(() => hub.querySelector("input")?.focus());
  }

  function filteredHub() {
    let list = TOOLS.all;
    if (hubTab === "当前") list = TOOLS.all.filter((t) => t.group === "Cursor");
    else if (hubTab === "最近") list = TOOLS.recent.map((t) => TOOLS.all.find((x) => x.id === t.id) || t);
    else if (hubTab !== "全部") list = TOOLS.all.filter((t) => t.group === hubTab);
    if (hubQuery) {
      const q = hubQuery.toLowerCase();
      list = list.filter((t) => t.title.toLowerCase().includes(q) || (t.group || "").toLowerCase().includes(q));
    }
    return list;
  }

  function renderHub() {
    if (!layer || layer.type !== "hub") return;
    const list = filteredHub();
    if (hubIndex >= list.length) hubIndex = Math.max(0, list.length - 1);
    const tabs = ["当前", "最近", "通用", "开发", "写作", "全部"];
    layer.el.innerHTML = `
      <div class="hub-search">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="#8b93a3"><path d="M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z"/></svg>
        <input type="search" placeholder="搜索工具、分组…" value="${hubQuery.replaceAll('"', "&quot;")}" />
        <kbd>↵</kbd>
      </div>
      <div class="hub-context"><span class="dot"></span>正在使用 Cursor · 专属工具已置顶</div>
      <div class="hub-tabs">
        ${tabs.map((t) => `<button type="button" class="${hubTab === t ? "active" : ""}" data-tab="${t}">${t}</button>`).join("")}
      </div>
      <div class="hub-list">
        ${list
          .map(
            (t, i) => `
          <button type="button" class="hub-row ${i === hubIndex ? "is-active" : ""}" data-id="${t.id}" data-i="${i}">
            <div class="ico" style="background:${t.color}">${t.glyph}</div>
            <div class="meta"><strong>${t.title}</strong><small>${t.group || "工具"}</small></div>
            <div class="key">${i < 9 ? i + 1 : ""}</div>
          </button>`
          )
          .join("") || `<p style="padding:16px;color:#8b93a3;font-size:13px;">无匹配</p>`}
      </div>`;

    const input = layer.el.querySelector("input");
    input?.addEventListener("click", (e) => e.stopPropagation());
    input?.addEventListener("input", (e) => {
      hubQuery = e.target.value;
      hubIndex = 0;
      renderHub();
      layer.el.querySelector("input")?.focus();
      const el = layer.el.querySelector("input");
      if (el) {
        el.focus();
        el.selectionStart = el.selectionEnd = el.value.length;
      }
    });
    layer.el.querySelectorAll("[data-tab]").forEach((b) =>
      b.addEventListener("click", (e) => {
        e.stopPropagation();
        hubTab = b.dataset.tab;
        hubIndex = 0;
        renderHub();
      })
    );
    layer.el.querySelectorAll(".hub-row").forEach((b) =>
      b.addEventListener("click", (e) => {
        e.stopPropagation();
        const t = list.find((x) => x.id === b.dataset.id);
        if (t) run(t);
      })
    );
  }

  // Stage events
  stage.addEventListener("pointerdown", (e) => {
    if (e.target.closest(".peek-strip, .peek-panel, .radial, .hub")) return;
    if (concept === "peek") showPeek(e.clientX, e.clientY);
    else if (concept === "radial") showRadial(e.clientX, e.clientY);
    else showHub(e.clientX, e.clientY);
  });

  document.getElementById("conceptNav").addEventListener("click", (e) => {
    const card = e.target.closest(".concept-card");
    if (card) setConcept(card.dataset.concept);
  });

  document.getElementById("btnDemo").addEventListener("click", () => {
    const r = stage.getBoundingClientRect();
    const x = r.left + r.width * 0.55;
    const y = r.top + r.height * 0.42;
    if (concept === "peek") showPeek(x, y);
    else if (concept === "radial") showRadial(x, y);
    else showHub(x, y);
  });

  document.getElementById("btnClear").addEventListener("click", clearLayer);

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      clearLayer();
      return;
    }
    if (layer?.type !== "hub") return;
    const list = filteredHub();
    if (e.key === "ArrowDown") {
      e.preventDefault();
      hubIndex = Math.min(list.length - 1, hubIndex + 1);
      renderHub();
      layer.el.querySelector("input")?.blur();
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      hubIndex = Math.max(0, hubIndex - 1);
      renderHub();
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (list[hubIndex]) run(list[hubIndex]);
    } else if (/^[1-9]$/.test(e.key) && document.activeElement?.tagName !== "INPUT") {
      const i = Number(e.key) - 1;
      if (list[i]) run(list[i]);
    }
  });

  setConcept("peek");
})();
