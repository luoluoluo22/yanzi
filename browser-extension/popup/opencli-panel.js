// The user explicitly triggers each read. No background collection or cloud sync.
document.addEventListener("DOMContentLoaded", () => {
  const siteSelect = document.getElementById("opencli-site");
  const operationSelect = document.getElementById("opencli-operation");
  const status = document.getElementById("opencli-status");
  const summary = document.getElementById("opencli-summary");
  const raw = document.getElementById("opencli-result");
  const readButton = document.getElementById("opencli-read");
  const copyButton = document.getElementById("opencli-copy");
  const operations = Object.freeze({
    xiaohongshu: [["profile", "个人资料"], ["stats", "近7天数据"], ["notes", "最近5篇笔记"]],
    bilibili: [["profile", "我的资料"], ["history", "观看历史"], ["following", "关注列表"]],
    douyin: [["profile", "个人资料"], ["videos", "作品列表（前5条）"]]
  });
  let latest = null;

  const reset = () => {
    latest = null;
    status.textContent = "需要已登录的浏览器及运行中的燕子、OpenCLI";
    summary.textContent = "";
    raw.textContent = "";
    copyButton.disabled = true;
  };
  const onSiteChange = () => {
    operationSelect.replaceChildren();
    for (const [value, label] of operations[siteSelect.value] || []) {
      operationSelect.add(new Option(label, value));
    }
    reset();
  };
  siteSelect.addEventListener("change", onSiteChange);
  operationSelect.addEventListener("change", reset);
  onSiteChange();

  readButton.addEventListener("click", async () => {
    readButton.disabled = true;
    status.textContent = "正在通过本地 OpenCLI 读取，请保持浏览器登录...";
    summary.textContent = "";
    raw.textContent = "";
    copyButton.disabled = true;
    try {
      const reply = await chrome.runtime.sendMessage({
        action: "yanzi_opencli_read", site: siteSelect.value, operation: operationSelect.value
      });
      if (!reply?.ok) throw new Error(reply?.error || "读取未成功");
      latest = reply.data;
      const entries = Array.isArray(latest) ? latest : [latest];
      const labels = Array.isArray(latest) ? "条记录" : "份数据";
      status.textContent = "读取成功 · " + entries.length + labels;
      const first = entries[0];
      const safeFields = ["nickname", "name", "title", "follower_count", "followers",
        "following_count", "aweme_count", "field", "value", "views", "likes"];
      const preview = first && typeof first === "object"
        ? safeFields.filter(key => Object.hasOwn(first, key)).slice(0, 6)
            .map(key => key + ": " + String(first[key]).slice(0, 65)).join(" · ")
        : "";
      summary.textContent = preview;
      raw.textContent = JSON.stringify(latest, null, 2);
      copyButton.disabled = false;
    } catch (error) {
      latest = null;
      status.textContent = "读取失败：" + (error?.message || String(error));
    } finally {
      readButton.disabled = false;
    }
  });

  copyButton.addEventListener("click", async () => {
    if (latest === null) return;
    try {
      await navigator.clipboard.writeText(JSON.stringify(latest, null, 2));
      status.textContent = "JSON 已复制到剪贴板（未上传至云端）";
    } catch (error) {
      status.textContent = "复制失败，请检查剪贴板权限";
    }
  });
});
