// Popup client for the allowlisted local media.v1 API.
document.addEventListener("DOMContentLoaded", () => {
  const box = document.getElementById("media-unified-card");
  if (!box) return;
  const site = box.querySelector("#media-site");
  const limit = box.querySelector("#media-limit");
  const keyword = box.querySelector("#media-keyword");
  const target = box.querySelector("#media-target");
  const search = box.querySelector("#media-search");
  const detail = box.querySelector("#media-detail");
  const comments = box.querySelector("#media-comments");
  const status = box.querySelector("#media-status");
  const results = box.querySelector("#media-results");
  const raw = box.querySelector("#media-result");
  const copy = box.querySelector("#media-copy");
  const example = box.querySelector("#media-copy-example");
  let busy = false, last = null;
  const buttons = [search, detail, comments];

  function displayMetric(metric) {
    if (!metric || metric.status === "not_provided") return "未提供";
    if (metric.status === "unverified") return "未验证";
    if (metric.status === "failed") return "读取失败";
    return metric.display || String(metric.value);
  }
  function statusOf(result, operation) {
    if (!result) return "未返回有效数据";
    if (operation === "detail") return "详情已读取；缺失的指标显示「未提供」";
    if (operation === "comments")
      return "评论读取成功 · " + (result.returned ?? result.items?.length ?? 0) + " 条";
    return "搜索成功 · " + (result.returned ?? result.items?.length ?? 0) + " 条";
  }
  function safeLink(url) {
    try {
      const u = new URL(url);
      return u.protocol === "https:" &&
        ["www.xiaohongshu.com", "www.douyin.com", "www.bilibili.com"].includes(u.hostname);
    } catch { return false; }
  }
  function renderSearchItems(items) {
    results.replaceChildren();
    for (const item of items) {
      const row = document.createElement("div");
      row.className = "media-result-row";
      const text = document.createElement("div");
      const title = document.createElement("strong");
      title.textContent = item.title || "无标题";
      text.append(title);
      const subtitle = document.createElement("small");
      subtitle.textContent = (item.author || "作者未提供") + " · 点赞：" + displayMetric(item.metrics?.likes);
      text.append(subtitle);
      row.append(text);
      const actions = document.createElement("div");
      actions.className = "opencli-actions";
      for (const [name, op] of [["详情", "detail"], ["评论", "comments"]]) {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = name;
        button.disabled = !safeLink(item.url);
        button.addEventListener("click", () => {
          if (!safeLink(item.url)) return;
          target.value = item.url;
          void run(op, item.url);
        });
        actions.append(button);
      }
      if (safeLink(item.url)) {
        const link = document.createElement("a");
        link.href = item.url;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        link.textContent = "打开作品";
        actions.append(link);
      }
      row.append(actions);
      results.append(row);
    }
  }
  async function run(operation, value) {
    if (busy) return;
    busy = true;
    buttons.forEach(b => b.disabled = true);
    status.textContent = operation === "search" ? "正在搜索作品……" :
      "正在获取作品" + (operation === "comments" ? "评论" : "详情") + "……";
    raw.textContent = "";
    copy.disabled = true;
    try {
      const response = await chrome.runtime.sendMessage({
        action: "yanzi_media_api", site: site.value, operation,
        value: (value || "").trim(), limit: Number(limit.value)
      });
      if (!response?.ok || !response.data) {
        await window.yanziOpenCliShowGuideIfMissing?.(response, status);
        throw Error(response?.error || "统一媒体接口未返回数据");
      }
      last = response.data;
      raw.textContent = JSON.stringify(last, null, 2);
      copy.disabled = false;
      status.textContent = statusOf(last, operation)
        + (response.cleanupWarning ? " · " + response.cleanupWarning : "");
      if (operation === "search") renderSearchItems(last.items || []);
      if (operation === "detail" && last.item?.metrics) {
        const metrics = last.item.metrics;
        status.textContent += " · 播放：" + displayMetric(metrics.plays)
          + " · 点赞：" + displayMetric(metrics.likes)
          + " · 评论：" + displayMetric(metrics.comments);
      }
    } catch (error) {
      last = null;
      status.textContent = "读取失败：" + (error?.message || String(error));
    } finally {
      busy = false;
      buttons.forEach(b => b.disabled = false);
    }
  }
  search.addEventListener("click", () => run("search", keyword.value));
  detail.addEventListener("click", () => run("detail", target.value));
  comments.addEventListener("click", () => run("comments", target.value));
  keyword.addEventListener("keydown", event => {
    if (event.key === "Enter") void run("search", keyword.value);
  });
  site.addEventListener("change", () => {
    results.replaceChildren();
    target.value = "";
    last = null; raw.textContent = ""; copy.disabled = true;
    status.textContent = "当前平台：" + site.options[site.selectedIndex].text;
  });
  copy.addEventListener("click", async () => {
    if (!last) return;
    try {
      await navigator.clipboard.writeText(JSON.stringify(last, null, 2));
      status.textContent = "统一 JSON 已复制到剪贴板";
    } catch { status.textContent = "复制失败，请检查剪贴板权限"; }
  });
  example.addEventListener("click", async () => {
    const code = [
      "// 燕子浏览器助手内部调用（只读）",
      "const response = await chrome.runtime.sendMessage({",
      '  action: "yanzi_media_api",',
      '  site: "bilibili",',
      '  operation: "search", // search | detail | comments',
      '  value: "人工智能",',
      "  limit: 5",
      "});",
      "if (!response.ok) throw Error(response.error);",
      "console.log(response.data);"
    ].join("\n");
    try { await navigator.clipboard.writeText(code); status.textContent = "接入代码已复制"; }
    catch { status.textContent = "复制失败"; }
  });
});
