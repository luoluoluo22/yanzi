// Unified read-only media API. Only the Yanzi extension itself may call it.
// Contract: search(site, keyword) -> items; detail(site, url) -> item;
// comments(site, url) -> items. Missing metrics are NEVER invented as zero.
(() => {
  "use strict";
  const SITES = new Set(["xiaohongshu", "douyin", "bilibili"]);
  const OPERATIONS = new Set(["search", "detail", "comments"]);
  const METRICS = ["plays", "likes", "comments", "collects", "shares", "danmaku", "coins"];

  function validate(site, operation, value, limit) {
    if (!SITES.has(site) || !OPERATIONS.has(operation))
      throw Error("不支持的平台或操作。");
    if (!Number.isInteger(limit) || limit < 1 || limit > 20)
      throw Error("数量须为 1～20。");
    if (typeof value !== "string" || !value.trim() || value.length > 550 || /[\x00-\x1f\x7f]/.test(value))
      throw Error("关键词或作品 URL 无效。");
    if (operation === "search" && (value.length > 80 || value.startsWith("-")))
      throw Error("关键词须为 1～80 字。");
    if (site === "douyin" && operation !== "search" &&
        !/^https:\/\/www\.douyin\.com\/video\/\d{12,22}\/?(?:\?.*)?$/.test(value))
      throw Error("请输入 https://www.douyin.com/video/ 后跟作品 ID 的官方地址。");
  }

  function metric(raw, source) {
    if (raw == null || String(raw).trim() === "" || raw === "-")
      return { value: null, status: "not_provided", source };
    if (typeof raw === "number" && Number.isFinite(raw))
      return { value: raw, status: "available", source };
    const m = String(raw).trim().match(/^([\d,.]+)\s*(万|亿)?$/);
    if (!m) return { value: null, status: "unverified", source, raw: String(raw).slice(0, 35) };
    const number = Number(m[1].replace(/,/g, ""));
    const multiplier = m[2] === "亿" ? 1e8 : m[2] === "万" ? 1e4 : 1;
    return { value: Math.round(number * multiplier), status: "available",
             source, display: String(raw), precision: multiplier === 1 ? "exact" : "rounded_display" };
  }

  function emptyMetrics(source) {
    return Object.fromEntries(METRICS.map(k => [k, { value: null, status: "not_provided", source }]));
  }

  async function douyinPublic(site, operation, value, limit) {
    const target = new URL(value);
    const expectedId = target.pathname.match(/^\/video\/(\d{12,22})\/?$/)?.[1];
    if (!expectedId) throw Error("抖音视频 URL 不是官方作品地址。");
    const source = "douyin_public_page";
    const result = await withEphemeralTab(
      "*://www.douyin.com/video/" + expectedId + "*",
      "https://www.douyin.com/video/" + expectedId,
      async (tabId, temporary) => {
        const answer = await chrome.scripting.executeScript({
          target: { tabId },
          world: "ISOLATED",
          func: async (method, requestedId, maxItems) => {
            const src = "douyin_public_page";
            const names = ["plays", "likes", "comments", "collects", "shares", "danmaku", "coins"];
            const metrics = Object.fromEntries(names.map(k => [k, { value: null, status: "not_provided", source: src }]));
            if (location.hostname !== "www.douyin.com" ||
                location.pathname !== "/video/" + requestedId)
              return { ok: false, error: "页面地址与请求作品 ID 不匹配。" };
            const count = raw => {
              if (raw == null) return { value: null, status: "not_provided", source: src };
              const value = String(raw).trim();
              const m = value.match(/^([\d,.]+)\s*(万|亿)?$/);
              if (!m) return { value: null, status: "unverified", source: src, raw: value.slice(0, 30) };
              const n = Number(m[1].replace(/,/g, ""));
              const factor = m[2] === "亿" ? 1e8 : m[2] === "万" ? 1e4 : 1;
              return { value: Math.round(n * factor), status: "available", source: src,
                display: value, precision: factor === 1 ? "exact" : "rounded_display" };
            };
            const selectors = {
              likes: '[data-e2e="video-player-digg"]',
              comments: '[data-e2e="feed-comment-icon"]',
              collects: '[data-e2e="video-player-collect"]',
              shares: '[data-e2e="video-player-share"]'
            };
            for (let i = 0; i < 24 && !document.querySelector(selectors.likes); i++)
              await new Promise(done => setTimeout(done, 400));
            for (const [name, selector] of Object.entries(selectors))
              metrics[name] = count(document.querySelector(selector)?.innerText);
            if (method === "detail") {
              // An inactive Douyin tab may render full video info while leaving
              // document.title empty. Take the actual displayed work title from
              // the official video-info DOM, never infer it from search results.
              const infoLines = (document.querySelector('[data-e2e="detail-video-info"]')?.innerText || "")
                .split(/\r?\n/).map(x => x.trim()).filter(Boolean);
              const title = document.title.replace(/\s*-\s*抖音\s*$/, "").trim() ||
                infoLines.find(x => x.length >= 12 && !/^\d+(?:\.\d+)?(万|亿)?$/.test(x)
                  && !/^发布时间|举报/.test(x))?.slice(0,280) || "";
              if (!title || !Object.values(metrics).some(x => x.status === "available"))
                return { ok: false, error: "公开视频数据尚未加载，不能返回不完整的详情。" };
              const authorLink = [...document.querySelectorAll('a[href*="/user/"]')]
                .find(a => a.getAttribute("href") && !a.getAttribute("href").includes("/user/self")
                  && a.textContent?.trim());
              return { ok: true, data: {
                schemaVersion: "1", site: "douyin", operation: "detail",
                source: src, collectedAt: new Date().toISOString(),
                item: { id: requestedId, url: location.href, title,
                  author: authorLink?.textContent?.trim() || null,
                  description: document.querySelector('meta[name="description"]')?.content || null,
                  publishedAt: null, duration: null, metrics, source: src }
              }};
            }
            const panel = document.querySelector(".comment-mainContent");
            panel?.scrollIntoView({ block: "nearest" });
            let rows = [];
            for (let i = 0; i < 22; i++) {
              rows = [...document.querySelectorAll(".comment-mainContent .comment-item-info-wrap")];
              if (rows.length) break;
              await new Promise(done => setTimeout(done, 500));
            }
            // The public DOM currently exposes a combined human-readable row.
            // Return exactly that row; do not guess author, likes or reply totals.
            const items = rows.slice(0, maxItems).map(el => ({
              id: null, author: null,
              text: (el.innerText || "").trim().slice(0, 1800),
              likes: null, replyCount: null, publishedAt: null, source: src
            })).filter(x => x.text);
            if (!items.length) return { ok: false,
              error: "抖音评论未加载或当前页面未开放评论，不返回虚假的空列表。" };
            return { ok: true, data: {
              schemaVersion: "1", site: "douyin", operation: "comments",
              source: src, target: location.href, returned: items.length,
              items, collectedAt: new Date().toISOString()
            }};
          },
          args: [operation, expectedId, limit]
        });
        const data = answer?.[0]?.result;
        return data?.ok ? data : { ok: false, error: data?.error || "作品页面未返回有效数据。" };
      },
      8500, true
    );
    if (!result?.ok) return result || { ok: false, error: "读取失败。" };
    return { ok: true, data: result.data, cleanupWarning: result.cleanupWarning || null,
             temporaryTabClosed: result.temporaryTabClosed };
  }

  async function performMediaRequest({ site, operation, value, limit = 5, profile = "" }) {
    try {
      validate(site, operation, value, limit);
      if (site === "douyin" && operation !== "search")
        return await douyinPublic(site, operation, value, limit);
      if (!globalThis.yanziBrowserHost?.isConnected())
        return { ok: false, error: "燕子本地宿主未连接，无法通过 OpenCLI 执行搜索和作品读取。" };
      return await globalThis.yanziMediaReadHost(site, operation, value, limit, profile);
    } catch (error) {
      return { ok: false, error: error?.message || String(error) };
    }
  }

  globalThis.yanziMediaRequest = performMediaRequest;
  globalThis.yanziMediaHelpers = { validate, metric, emptyMetrics };
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.action !== "yanzi_media_api") return;
    if (sender.id !== chrome.runtime.id) {
      sendResponse({ ok: false, error: "只允许燕子浏览器助手自身调用。" });
      return;
    }
    void performMediaRequest(message).then(sendResponse)
      .catch(error => sendResponse({ ok: false, error: String(error?.message || error) }));
    return true;
  });
})();
