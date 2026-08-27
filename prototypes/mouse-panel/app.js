(() => {
  "use strict";

  const STORAGE_KEY = "oqh-mouse-panel-proto-v1";

  const COLORS = [
    "#3B82F6", "#6366F1", "#0EA5E9", "#14B8A6", "#22C55E",
    "#F59E0B", "#F97316", "#EF4444", "#EC4899", "#8B5CF6",
  ];

  const DEFAULT_STATE = () => ({
    globalGroups: [
      { id: "g-default", name: "默认", short: "默" },
      { id: "g-dev", name: "开发", short: "开" },
    ],
    contextGroups: [
      { id: "c-default", name: "默认", short: "默" },
      { id: "c-write", name: "写作", short: "写" },
    ],
    selectedGlobal: "g-default",
    selectedContext: "c-default",
    showGlobalFav: false,
    showContextFav: false,
    pinned: false,
    editMode: false,
    slots: {
      "g-default": makeSlots([
        tool("剪贴板", "剪", 0, { favorite: true }),
        tool("截图", "截", 1),
        tool("翻译", "译", 2, { running: true }),
        tool("笔记", "记", 3),
        tool("工具箱", "箱", 4, {
          folder: true,
          children: [
            tool("JSON", "J", 5),
            tool("正则", "正", 6),
            tool("Base64", "B", 7),
            tool("时间戳", "时", 8),
          ],
        }),
        tool("日历", "历", 9),
        null, null, null, null, null, null,
      ]),
      "g-dev": makeSlots([
        tool("终端", "终", 0),
        tool("Git", "G", 1),
        tool("Docker", "D", 2),
        tool("API", "A", 3),
        null, null, null, null, null, null, null, null,
      ]),
      "c-default": makeSlots([
        tool("格式化", "格", 5),
        tool("跳转定义", "跳", 6),
        tool("重构", "重", 7),
        null, null, null, null, null, null, null, null, null,
      ]),
      "c-write": makeSlots([
        tool("润色", "润", 8),
        tool("摘要", "摘", 9),
        null, null, null, null, null, null, null, null, null, null,
      ]),
    },
  });

  function tool(title, glyph, colorIndex, extra = {}) {
    return {
      id: `t-${title}-${Math.random().toString(36).slice(2, 7)}`,
      title,
      glyph,
      color: COLORS[colorIndex % COLORS.length],
      favorite: !!extra.favorite,
      running: !!extra.running,
      folder: !!extra.folder,
      children: extra.children || [],
    };
  }

  function makeSlots(items) {
    const out = Array.from({ length: 12 }, (_, i) => items[i] ?? null);
    return out;
  }

  function emptySlots(n = 12) {
    return Array.from({ length: n }, () => null);
  }

  function loadState() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return DEFAULT_STATE();
      return { ...DEFAULT_STATE(), ...JSON.parse(raw) };
    } catch {
      return DEFAULT_STATE();
    }
  }

  function saveState() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  }

  let state = loadState();
  let searchQuery = "";
  let openFolder = null; // { key, index, item }
  let dragPayload = null;
  let ctxTarget = null;

  const el = {
    desktop: document.getElementById("desktop"),
    panel: document.getElementById("panel"),
    globalGroups: document.getElementById("globalGroups"),
    contextGroups: document.getElementById("contextGroups"),
    globalSlots: document.getElementById("globalSlots"),
    contextSlots: document.getElementById("contextSlots"),
    folderOverlay: document.getElementById("folderOverlay"),
    folderSlots: document.getElementById("folderSlots"),
    folderTitle: document.getElementById("folderTitle"),
    folderBack: document.getElementById("folderBack"),
    panelTitle: document.getElementById("panelTitle"),
    editHint: document.getElementById("editHint"),
    contextLabel: document.getElementById("contextLabel"),
    searchBox: document.getElementById("searchBox"),
    btnEdit: document.getElementById("btnEdit"),
    btnPin: document.getElementById("btnPin"),
    btnShow: document.getElementById("btnShow"),
    btnReset: document.getElementById("btnReset"),
    dragHint: document.getElementById("dragHint"),
    ctxMenu: document.getElementById("ctxMenu"),
    toast: document.getElementById("toast"),
    mobileBadge: document.getElementById("mobileBadge"),
  };

  function slotKey(isGlobal) {
    if (isGlobal) {
      if (state.showGlobalFav) return "__fav_global__";
      return state.selectedGlobal;
    }
    if (state.showContextFav) return "__fav_context__";
    return state.selectedContext;
  }

  function getSlots(isGlobal) {
    const key = slotKey(isGlobal);
    if (key === "__fav_global__") {
      return collectFavorites(true);
    }
    if (key === "__fav_context__") {
      return collectFavorites(false);
    }
    if (!state.slots[key]) state.slots[key] = emptySlots();
    return state.slots[key];
  }

  function collectFavorites(fromGlobal) {
    const groups = fromGlobal ? state.globalGroups : state.contextGroups;
    const favs = [];
    for (const g of groups) {
      const list = state.slots[g.id] || [];
      for (const s of list) {
        if (s?.favorite) favs.push(s);
      }
    }
    while (favs.length < 12) favs.push(null);
    return favs.slice(0, 12);
  }

  function toast(msg) {
    el.toast.textContent = msg;
    el.toast.hidden = false;
    clearTimeout(toast._t);
    toast._t = setTimeout(() => {
      el.toast.hidden = true;
    }, 1600);
  }

  function renderGroups() {
    el.globalGroups.innerHTML = state.globalGroups
      .map(
        (g) => `
      <button type="button" class="group-btn ${!state.showGlobalFav && state.selectedGlobal === g.id ? "active" : ""}"
        data-kind="global" data-id="${g.id}" title="${escapeAttr(g.name)}">${escapeHtml(g.short)}</button>`
      )
      .join("");

    el.contextGroups.innerHTML = state.contextGroups
      .map(
        (g) => `
      <button type="button" class="group-btn ${!state.showContextFav && state.selectedContext === g.id ? "active" : ""}"
        data-kind="context" data-id="${g.id}" title="${escapeAttr(g.name)}">${escapeHtml(g.short)}</button>`
      )
      .join("");

    document.querySelector('[data-action="global-fav"]')?.classList.toggle("active", state.showGlobalFav);
    document.querySelector('[data-action="context-fav"]')?.classList.toggle("active", state.showContextFav);
  }

  function renderTitle() {
    if (state.showGlobalFav) {
      el.panelTitle.textContent = "通用收藏";
    } else {
      const g = state.globalGroups.find((x) => x.id === state.selectedGlobal);
      el.panelTitle.textContent = g?.name || "通用工具";
    }
    el.btnEdit.setAttribute("aria-pressed", String(state.editMode));
    el.btnPin.setAttribute("aria-pressed", String(state.pinned));
    el.btnEdit.title = state.editMode ? "完成编辑" : "编辑面板";
    el.btnPin.title = state.pinned ? "已常驻，失焦不关闭" : "点击后常驻";
    el.panel.classList.toggle("is-edit", state.editMode);
    el.editHint.hidden = !state.editMode;
  }

  function renderSlot(item, index, zone) {
    if (!item) {
      return `
        <button type="button" class="slot is-empty" data-zone="${zone}" data-index="${index}" draggable="false" aria-label="添加扩展">
          <span class="slot-icon"><span class="plus">+</span></span>
          <span class="slot-label">添加</span>
        </button>`;
    }

    const mosaic = item.folder
      ? `<span class="folder-mosaic">${(item.children || [])
          .slice(0, 4)
          .map((c) => `<span style="background:${c.color}"></span>`)
          .join("")}</span>`
      : `<span>${escapeHtml(item.glyph)}</span>`;

    return `
      <button type="button" class="slot" data-zone="${zone}" data-index="${index}"
        draggable="${state.editMode || true}" aria-label="${escapeAttr(item.title)}">
        <span class="slot-icon" style="background:${item.folder ? "#1e293b" : item.color}">
          ${item.favorite ? '<i class="fav-dot"></i>' : ""}
          ${item.running ? '<i class="run-dot"></i>' : ""}
          ${item.folder ? `<i class="folder-badge">${Math.min((item.children || []).length, 9)}</i>` : ""}
          ${mosaic}
        </span>
        <span class="slot-label">${escapeHtml(item.title)}</span>
      </button>`;
  }

  function matchesSearch(item) {
    if (!searchQuery) return true;
    if (!item) return false;
    return item.title.toLowerCase().includes(searchQuery);
  }

  function renderSlots() {
    const global = getSlots(true);
    const context = getSlots(false);

    el.globalSlots.innerHTML = global
      .map((item, i) => {
        const html = renderSlot(item, i, "global");
        return matchesSearch(item) || !searchQuery
          ? html
          : html.replace('class="slot', 'class="slot is-filtered-out');
      })
      .join("");

    // When searching, hide empty slots that don't match; occupied non-match already filtered
    if (searchQuery) {
      el.globalSlots.querySelectorAll(".slot.is-empty").forEach((n) => n.classList.add("is-filtered-out"));
      [...el.globalSlots.children].forEach((node, i) => {
        if (global[i] && !matchesSearch(global[i])) node.classList.add("is-filtered-out");
      });
    }

    el.contextSlots.innerHTML = context.map((item, i) => renderSlot(item, i, "context")).join("");
    if (searchQuery) {
      el.contextSlots.querySelectorAll(".slot.is-empty").forEach((n) => n.classList.add("is-filtered-out"));
      [...el.contextSlots.children].forEach((node, i) => {
        if (!matchesSearch(context[i])) node.classList.add("is-filtered-out");
      });
    }

    if (openFolder) {
      el.folderOverlay.hidden = false;
      el.folderTitle.textContent = openFolder.item.title;
      const kids = [...(openFolder.item.children || [])];
      while (kids.length < 12) kids.push(null);
      el.folderSlots.innerHTML = kids
        .slice(0, 12)
        .map((item, i) => renderSlot(item, i, "folder"))
        .join("");
    } else {
      el.folderOverlay.hidden = true;
    }
  }

  function renderAll() {
    renderGroups();
    renderTitle();
    renderSlots();
  }

  function showPanel(clientX, clientY) {
    const rect = el.desktop.getBoundingClientRect();
    const x = clientX - rect.left;
    const y = clientY - rect.top;
    const w = 292;
    const h = 560;
    let left = x - w / 2;
    let top = y - h / 2;
    left = Math.max(12, Math.min(left, rect.width - w - 12));
    top = Math.max(12, Math.min(top, rect.height - h - 12));

    el.panel.style.left = `${left}px`;
    el.panel.style.top = `${top}px`;
    el.panel.hidden = false;
    el.panel.classList.remove("is-leaving");
    el.desktop.classList.add("is-panel-open");
    el.searchBox.value = "";
    searchQuery = "";
    openFolder = null;
    renderAll();
  }

  function hidePanel(force = false) {
    if (state.pinned && !force) return;
    if (el.panel.hidden) return;
    el.panel.classList.add("is-leaving");
    setTimeout(() => {
      el.panel.hidden = true;
      el.panel.classList.remove("is-leaving");
      el.desktop.classList.remove("is-panel-open");
      hideCtx();
    }, 150);
  }

  function escapeHtml(s) {
    return String(s)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function escapeAttr(s) {
    return escapeHtml(s).replaceAll("'", "&#39;");
  }

  function zoneList(zone) {
    if (zone === "folder" && openFolder) {
      if (!openFolder.item.children) openFolder.item.children = [];
      return openFolder.item.children;
    }
    return getSlots(zone === "global");
  }

  function persistZone(zone, list) {
    if (zone === "folder" && openFolder) {
      openFolder.item.children = list;
      // also write back into parent slot array
      const parentList = getSlots(openFolder.zone === "global");
      parentList[openFolder.index] = openFolder.item;
      const key = slotKey(openFolder.zone === "global");
      if (!key.startsWith("__fav")) state.slots[key] = parentList;
    } else {
      const key = slotKey(zone === "global");
      if (!key.startsWith("__fav")) state.slots[key] = list;
    }
    saveState();
  }

  function executeSlot(item, zone, index) {
    if (!item) {
      const title = prompt("扩展名称", "新工具");
      if (!title) return;
      const list =
        zone === "folder" && openFolder
          ? (() => {
              const kids = [...(openFolder.item.children || [])];
              while (kids.length < 12) kids.push(null);
              return kids;
            })()
          : [...getSlots(zone === "global")];
      list[index] = tool(title.trim(), title.trim().slice(0, 1), Math.floor(Math.random() * COLORS.length));
      persistZone(zone, list);
      renderSlots();
      toast(`已添加「${title.trim()}」`);
      return;
    }

    if (item.folder) {
      openFolder = { zone, index, item };
      renderSlots();
      return;
    }

    toast(`已执行：${item.title}`);
    if (!state.pinned && !state.editMode) {
      setTimeout(() => hidePanel(), 280);
    }
  }

  function showCtx(x, y, zone, index, item) {
    ctxTarget = { zone, index, item };
    const items = item
      ? [
          { label: item.favorite ? "取消收藏" : "收藏", action: "fav" },
          { label: "复制扩展", action: "copy" },
          { sep: true },
          { label: "编辑扩展", action: "edit" },
          { label: item.running ? "停止运行" : "标记运行中", action: "run" },
          { sep: true },
          { label: "移除扩展", action: "remove" },
          { label: "删除扩展", action: "delete", danger: true },
        ]
      : [{ label: "粘贴扩展", action: "paste" }, { label: "添加扩展", action: "add" }];

    el.ctxMenu.innerHTML = items
      .map((it) =>
        it.sep
          ? '<div class="sep"></div>'
          : `<button type="button" role="menuitem" data-action="${it.action}" class="${it.danger ? "danger" : ""}">${it.label}</button>`
      )
      .join("");
    el.ctxMenu.hidden = false;
    const mw = el.ctxMenu.offsetWidth;
    const mh = el.ctxMenu.offsetHeight;
    el.ctxMenu.style.left = `${Math.min(x, window.innerWidth - mw - 8)}px`;
    el.ctxMenu.style.top = `${Math.min(y, window.innerHeight - mh - 8)}px`;
  }

  function hideCtx() {
    el.ctxMenu.hidden = true;
    ctxTarget = null;
  }

  // Events
  el.desktop.addEventListener("click", (e) => {
    if (e.target.closest(".panel") || e.target.closest(".ctx-menu") || e.target.closest(".page-chrome") || e.target.closest(".ghost-btn")) {
      return;
    }
    if (el.panel.hidden) {
      showPanel(e.clientX, e.clientY);
    } else if (!state.pinned) {
      hidePanel();
    }
  });

  el.btnShow.addEventListener("click", (e) => {
    e.stopPropagation();
    const r = el.desktop.getBoundingClientRect();
    showPanel(r.left + r.width * 0.55, r.top + r.height * 0.42);
  });

  el.btnReset.addEventListener("click", (e) => {
    e.stopPropagation();
    if (!confirm("重置原型数据？")) return;
    localStorage.removeItem(STORAGE_KEY);
    state = DEFAULT_STATE();
    openFolder = null;
    renderAll();
    toast("已重置");
  });

  el.btnEdit.addEventListener("click", (e) => {
    e.stopPropagation();
    state.editMode = !state.editMode;
    if (!state.editMode) openFolder = openFolder; // keep
    saveState();
    renderAll();
    toast(state.editMode ? "已进入编辑模式" : "已退出编辑模式");
  });

  el.btnPin.addEventListener("click", (e) => {
    e.stopPropagation();
    state.pinned = !state.pinned;
    saveState();
    renderTitle();
    toast(state.pinned ? "面板已常驻" : "已取消常驻");
  });

  el.searchBox.addEventListener("input", () => {
    searchQuery = el.searchBox.value.trim().toLowerCase();
    renderSlots();
  });

  el.searchBox.addEventListener("click", (e) => e.stopPropagation());

  el.folderBack.addEventListener("click", (e) => {
    e.stopPropagation();
    openFolder = null;
    renderSlots();
  });

  document.querySelector(".sidebar").addEventListener("click", (e) => {
    e.stopPropagation();
    const actionBtn = e.target.closest("[data-action]");
    if (actionBtn) {
      const a = actionBtn.dataset.action;
      if (a === "global-fav") {
        state.showGlobalFav = !state.showGlobalFav;
        saveState();
        renderAll();
      } else if (a === "context-fav") {
        state.showContextFav = !state.showContextFav;
        saveState();
        renderAll();
      } else if (a === "add-global") {
        const name = prompt("通用分组名称", "新分组");
        if (!name) return;
        const id = `g-${Date.now()}`;
        state.globalGroups.push({ id, name: name.trim(), short: name.trim().slice(0, 1) });
        state.slots[id] = emptySlots();
        state.selectedGlobal = id;
        state.showGlobalFav = false;
        saveState();
        renderAll();
      } else if (a === "add-context") {
        const name = prompt("专属分组名称", "新分组");
        if (!name) return;
        const id = `c-${Date.now()}`;
        state.contextGroups.push({ id, name: name.trim(), short: name.trim().slice(0, 1) });
        state.slots[id] = emptySlots();
        state.selectedContext = id;
        state.showContextFav = false;
        saveState();
        renderAll();
      } else if (a === "mobile") {
        toast("手机消息（原型占位）");
        el.mobileBadge.style.display = "none";
      } else if (a === "settings") {
        toast("设置窗口（原型占位）");
      }
      return;
    }

    const groupBtn = e.target.closest(".group-btn");
    if (groupBtn) {
      if (groupBtn.dataset.kind === "global") {
        state.selectedGlobal = groupBtn.dataset.id;
        state.showGlobalFav = false;
      } else {
        state.selectedContext = groupBtn.dataset.id;
        state.showContextFav = false;
      }
      saveState();
      renderAll();
    }
  });

  el.panel.addEventListener("click", (e) => {
    e.stopPropagation();
    const slot = e.target.closest(".slot");
    if (!slot) return;
    hideCtx();
    const zone = slot.dataset.zone;
    const index = Number(slot.dataset.index);
    const list = zone === "folder" && openFolder
      ? (() => {
          const kids = [...(openFolder.item.children || [])];
          while (kids.length < 12) kids.push(null);
          return kids;
        })()
      : getSlots(zone === "global");
    executeSlot(list[index], zone, index);
  });

  el.panel.addEventListener("contextmenu", (e) => {
    const slot = e.target.closest(".slot");
    if (!slot) return;
    e.preventDefault();
    e.stopPropagation();
    const zone = slot.dataset.zone;
    const index = Number(slot.dataset.index);
    let list;
    if (zone === "folder" && openFolder) {
      list = [...(openFolder.item.children || [])];
      while (list.length < 12) list.push(null);
    } else {
      list = getSlots(zone === "global");
    }
    showCtx(e.clientX, e.clientY, zone, index, list[index]);
  });

  el.ctxMenu.addEventListener("click", (e) => {
    const btn = e.target.closest("button[data-action]");
    if (!btn || !ctxTarget) return;
    const { zone, index, item } = ctxTarget;
    const list =
      zone === "folder" && openFolder
        ? (() => {
            const kids = [...(openFolder.item.children || [])];
            while (kids.length < 12) kids.push(null);
            return kids;
          })()
        : [...getSlots(zone === "global")];

    switch (btn.dataset.action) {
      case "fav":
        if (item) {
          item.favorite = !item.favorite;
          list[index] = item;
          persistZone(zone, list);
          toast(item.favorite ? "已收藏" : "已取消收藏");
        }
        break;
      case "copy":
        if (item) {
          sessionStorage.setItem("oqh-clip", JSON.stringify(item));
          toast("已复制扩展");
        }
        break;
      case "paste":
      case "add": {
        let next = null;
        const clip = sessionStorage.getItem("oqh-clip");
        if (btn.dataset.action === "paste" && clip) {
          next = { ...JSON.parse(clip), id: `t-${Date.now()}` };
        } else {
          const title = prompt("扩展名称", "新工具");
          if (!title) break;
          next = tool(title.trim(), title.trim().slice(0, 1), Math.floor(Math.random() * COLORS.length));
        }
        list[index] = next;
        persistZone(zone, list);
        toast(`已放入「${next.title}」`);
        break;
      }
      case "edit":
        if (item) {
          const title = prompt("重命名", item.title);
          if (title) {
            item.title = title.trim();
            item.glyph = title.trim().slice(0, 1);
            list[index] = item;
            persistZone(zone, list);
          }
        }
        break;
      case "run":
        if (item) {
          item.running = !item.running;
          list[index] = item;
          persistZone(zone, list);
        }
        break;
      case "remove":
        list[index] = null;
        persistZone(zone, list);
        toast("已移除");
        break;
    }
    hideCtx();
    renderAll();
  });

  // Drag & drop
  el.panel.addEventListener("dragstart", (e) => {
    const slot = e.target.closest(".slot");
    if (!slot || slot.classList.contains("is-empty")) {
      e.preventDefault();
      return;
    }
    const zone = slot.dataset.zone;
    const index = Number(slot.dataset.index);
    dragPayload = { zone, index, ctrl: false };
    slot.classList.add("is-dragging");
    el.dragHint.hidden = false;
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", `${zone}:${index}`);
  });

  el.panel.addEventListener("dragend", () => {
    el.panel.querySelectorAll(".is-dragging, .is-drag-over").forEach((n) => {
      n.classList.remove("is-dragging", "is-drag-over");
    });
    el.dragHint.hidden = true;
    dragPayload = null;
  });

  el.panel.addEventListener("dragover", (e) => {
    const slot = e.target.closest(".slot");
    if (!slot || !dragPayload) return;
    e.preventDefault();
    el.panel.querySelectorAll(".is-drag-over").forEach((n) => n.classList.remove("is-drag-over"));
    slot.classList.add("is-drag-over");
    if (e.ctrlKey) {
      el.dragHint.querySelector("span").textContent = "松开将成组 / 加入分组";
    } else {
      el.dragHint.querySelector("span").textContent = "Ctrl：成组 / 加入分组";
    }
  });

  el.panel.addEventListener("dragleave", (e) => {
    const slot = e.target.closest(".slot");
    slot?.classList.remove("is-drag-over");
  });

  el.panel.addEventListener("drop", (e) => {
    const slot = e.target.closest(".slot");
    if (!slot || !dragPayload) return;
    e.preventDefault();
    const toZone = slot.dataset.zone;
    const toIndex = Number(slot.dataset.index);
    const fromZone = dragPayload.zone;
    const fromIndex = dragPayload.index;
    if (fromZone === toZone && fromIndex === toIndex) return;

    const readList = (zone) => {
      if (zone === "folder" && openFolder) {
        const kids = [...(openFolder.item.children || [])];
        while (kids.length < 12) kids.push(null);
        return kids;
      }
      return [...getSlots(zone === "global")];
    };

    const fromList = readList(fromZone);
    const toList = fromZone === toZone ? fromList : readList(toZone);
    const moving = fromList[fromIndex];
    if (!moving) return;

    if (e.ctrlKey && toList[toIndex] && !toList[toIndex].folder) {
      // merge into folder
      const target = toList[toIndex];
      const folder = {
        id: `folder-${Date.now()}`,
        title: "分组",
        glyph: "组",
        color: "#1e293b",
        folder: true,
        children: [target, moving],
      };
      toList[toIndex] = folder;
      fromList[fromIndex] = null;
      if (fromZone === toZone) {
        persistZone(toZone, toList);
      } else {
        persistZone(fromZone, fromList);
        persistZone(toZone, toList);
      }
      toast("已成组");
    } else if (e.ctrlKey && toList[toIndex]?.folder) {
      toList[toIndex].children = [...(toList[toIndex].children || []), moving];
      fromList[fromIndex] = null;
      if (fromZone === toZone) persistZone(toZone, toList);
      else {
        persistZone(fromZone, fromList);
        persistZone(toZone, toList);
      }
      toast("已加入分组");
    } else {
      // swap / move
      const tmp = toList[toIndex];
      toList[toIndex] = moving;
      fromList[fromIndex] = tmp;
      if (fromZone === toZone) persistZone(toZone, toList);
      else {
        persistZone(fromZone, fromList);
        persistZone(toZone, toList);
      }
    }
    renderSlots();
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      if (!el.ctxMenu.hidden) {
        hideCtx();
        return;
      }
      if (openFolder) {
        openFolder = null;
        renderSlots();
        return;
      }
      hidePanel(true);
    }
  });

  document.addEventListener("click", (e) => {
    if (!e.target.closest(".ctx-menu")) hideCtx();
  });

  // Prevent panel clicks from closing via desktop handler — already stopPropagation on panel
  el.panel.addEventListener("mousedown", (e) => e.stopPropagation());

  renderAll();
})();
