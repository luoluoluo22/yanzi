// Manual reads only. The browser extension never uploads account data.
document.addEventListener("DOMContentLoaded", () => {
  const siteSelect = document.getElementById("opencli-site");
  const operationSelect = document.getElementById("opencli-operation");
  const status = document.getElementById("opencli-status");
  const summary = document.getElementById("opencli-summary");
  const raw = document.getElementById("opencli-result");
  const readButton = document.getElementById("opencli-read");
  const copyButton = document.getElementById("opencli-copy");
  const pagination = document.getElementById("opencli-pagination");
  const prev = document.getElementById("opencli-prev");
  const next = document.getElementById("opencli-next");
  const pageLabel = document.getElementById("opencli-page-label");
  const profileInput = document.createElement("input");
  profileInput.type = "text";
  profileInput.placeholder = "浏览器配置：留空自动选择唯一在线配置";
  profileInput.setAttribute("aria-label", "OpenCLI 浏览器配置名称或 ID");
  profileInput.maxLength = 64;
  profileInput.style.cssText = "width:100%;box-sizing:border-box;margin-top:8px;padding:6px";
  (siteSelect.closest(".opencli-controls-row") || siteSelect.parentElement).after(profileInput);

  const PAGE_SIZE = 20;
  const MAX_PAGE = 20;
  const operations = Object.freeze({
    xiaohongshu: [["profile", "个人资料"], ["stats", "近7天数据"], ["notes", "最近20篇笔记"]],
    bilibili: [["profile", "我的资料"], ["history", "观看历史"], ["following", "关注列表"]],
    douyin: [["profile", "个人资料"], ["videos", "作品列表（每页20条）"]]
  });
  let latest = null;
  let currentPage = 1;
  let busy = false;
  const loadedPages = new Map();

  const isPaged = () => siteSelect.value === "douyin" && operationSelect.value === "videos";
  const getRecords = data => Array.isArray(data) ? data :
    (data && typeof data === "object"
      ? [data.items, data.list, data.data, data.videos, data.notes]
          .find(Array.isArray) || [data]
      : []);
  const getId = record => record?.aweme_id || record?.note_id || record?.id || "";
  const refreshPagination = (length = 0) => {
    const paged = isPaged();
    pagination.hidden = !paged;
    pageLabel.textContent = "第 " + currentPage + " 页";
    prev.disabled = busy || !paged || currentPage <= 1;
    next.disabled = busy || !paged || currentPage >= MAX_PAGE || length < PAGE_SIZE;
  };
  const reset = () => {
    latest = null;
    currentPage = 1;
    loadedPages.clear();
    status.textContent = "需要运行中的燕子、OpenCLI Browser Bridge 和已登录的网站";
    summary.textContent = "";
    raw.textContent = "";
    copyButton.disabled = true;
    refreshPagination();
  };
  const onSiteChange = () => {
    profileInput.hidden = siteSelect.value !== "xiaohongshu";
    operationSelect.replaceChildren();
    for (const [value, label] of operations[siteSelect.value] || []) {
      operationSelect.add(new Option(label, value));
    }
    reset();
  };
  siteSelect.addEventListener("change", onSiteChange);
  operationSelect.addEventListener("change", reset);
  onSiteChange();

  async function read(page = 1) {
    if (busy || page < 1 || page > MAX_PAGE) return;
    busy = true;
    readButton.disabled = true;
    siteSelect.disabled = operationSelect.disabled = profileInput.disabled = true;
    latest = null;
    raw.textContent = "";
    copyButton.disabled = true;
    prev.disabled = true;
    next.disabled = true;
    status.textContent = "正在读取，请保持浏览器登录……";
    summary.textContent = "";
    try {
      const reply = await chrome.runtime.sendMessage({
        action: "yanzi_opencli_read",
        site: siteSelect.value,
        operation: operationSelect.value,
        page: isPaged() ? page : 1,
        profile: siteSelect.value === "xiaohongshu" ? profileInput.value.trim() : ""
      });
      if (!reply?.ok) throw new Error(reply?.error || "读取未成功");
      latest = reply.data;
      currentPage = isPaged() ? page : 1;
      const records = getRecords(latest);
      const ids = records.map(getId).filter(Boolean).map(String);
      const withinDuplicates = ids.length - new Set(ids).size;
      const previous = new Set([...loadedPages.entries()]
        .filter(([p]) => p !== currentPage)
        .flatMap(([, items]) => items));
      const acrossDuplicates = ids.filter(id => previous.has(id)).length;
      loadedPages.set(currentPage, ids);

      const isList = Array.isArray(latest) ||
        Boolean(latest && typeof latest === "object" && Array.isArray(latest.items));
      const prefix = isPaged() ? "第 " + currentPage + " 页 · " : "";
      const totalText = isPaged() && Number.isInteger(latest?.total)
        ? " / 共 " + latest.total + " 条" : "";
      const issues = [
        withinDuplicates ? "本页重复 ID " + withinDuplicates + " 个" : "",
        acrossDuplicates ? "与其他页重复 " + acrossDuplicates + " 个" : ""
      ].filter(Boolean);
      const missingTitles = isPaged() ?
        records.filter(record => !String(record?.title || "").trim()).length : 0;
      if (missingTitles) issues.push(missingTitles + " 条作品无标题描述");
      status.textContent = "读取成功 · " + prefix + records.length +
        (isList ? " 条记录" : " 项数据") + totalText +
        (issues.length ? "；提示：" + issues.join("、") : "");
      const first = records[0];
      const previewFields = ["nickname", "name", "title", "date",
        "field", "value", "metric", "total",
        "views", "likes", "collects", "comments",
        "play_count", "digg_count", "create_time", "follower_count", "aweme_count"];
      summary.textContent = first && typeof first === "object"
        ? previewFields.filter(key => Object.hasOwn(first, key)).slice(0, 6)
          .map(key => key + "：" + String(first[key]).slice(0, 65)).join(" · ")
        : "";
      raw.textContent = JSON.stringify(latest, null, 2);
      if (reply.source === "opencli") {
        summary.textContent = "来源：OpenCLI · 浏览器配置：" + reply.profile +
          " · 采集：" + new Date(reply.collectedAt).toLocaleString() +
          (reply.period === "seven" ? " · 网站近7天口径（具体日期以创作者中心为准）" : "");
      }
      copyButton.disabled = false;
      refreshPagination(records.length);
    } catch (error) {
      status.textContent = "读取失败：" + (error?.message || String(error));
      refreshPagination(getRecords(latest).length);
    } finally {
      busy = false;
      readButton.disabled = false;
      siteSelect.disabled = operationSelect.disabled = profileInput.disabled = false;
      refreshPagination(getRecords(latest).length);
    }
  }

  readButton.addEventListener("click", () => read(1));
  prev.addEventListener("click", () => read(currentPage - 1));
  next.addEventListener("click", () => read(currentPage + 1));
  copyButton.addEventListener("click", async () => {
    if (latest === null) return;
    try {
      await navigator.clipboard.writeText(JSON.stringify(latest, null, 2));
      status.textContent = "当前页 JSON 已复制到剪贴板（未上传至云端）";
    } catch {
      status.textContent = "复制失败，请检查剪贴板权限";
    }
  });
});
