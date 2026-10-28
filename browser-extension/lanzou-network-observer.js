// 蓝奏云请求结构观察器。主动开启；只保存请求元信息，不保存 Cookie、令牌、请求体或响应体。
(() => {
  "use strict";
  const HOSTS = new Set([
    "pc.woozooo.com", "up.woozooo.com", "accounts.woozooo.com",
    "www.lanzou.com", "lanzou.com",
    "lanzoui.com", "www.lanzoui.com",
    "lanzouo.com", "www.lanzouo.com",
    "lanzouw.com", "www.lanzouw.com",
    "lanzoux.com", "www.lanzoux.com"
  ]);
  const MAX_ITEMS = 150;
  let enabled = false;
  let events = [];
  const inFlight = new Map();
  const safeText = text => String(text || "").slice(0, 160);
  const sensitive = /(cookie|session|token|secret|password|passwd|pwd|auth|ph(pd)?disk|ylogin|key|sign|code|csrf|hash|credential|email|phone|name|user)/i;
  const inspect = url => {
    try {
      const u = new URL(url);
      if (u.protocol !== "https:" || !HOSTS.has(u.hostname.toLowerCase())) return null;
      // 非 PHP 页面路径可能包含私密分享标识，不记录。
      const path = /^\/[a-z0-9_-]+\.php$/i.test(u.pathname) ? u.pathname : "/[page-or-share]";
      return { host: u.hostname.toLowerCase(), path };
    } catch (_) { return null; }
  };
  const fields = details => {
    const keys = [];
    const body = details.requestBody;
    if (body?.formData) keys.push(...Object.keys(body.formData));
    // requestBody.raw 包含文件/敏感数据，永不读取。
    return Array.from(new Set(keys.filter(k => !sensitive.test(k)))).slice(0, 25);
  };
  const append = item => {
    events.push(item);
    if (events.length > MAX_ITEMS) events = events.slice(-MAX_ITEMS);
  };
  chrome.webRequest.onBeforeRequest.addListener(details => {
    if (!enabled || !["xmlhttprequest", "main_frame", "sub_frame"].includes(details.type)) return;
    const endpoint = inspect(details.url);
    if (!endpoint) return;
    const record = {
      method: safeText(details.method).toUpperCase(), ...endpoint,
      bodyFields: fields(details),
      // 仅收集不含凭证语义的纯数字操作号，供请求协议辨认。
      taskCode: (() => {
        const raw = details.requestBody?.formData?.task;
        const candidate = Array.isArray(raw) && raw.length === 1 ? raw[0] : "";
        return typeof candidate === "string" && /^[0-9]{1,3}$/.test(candidate) ? candidate : null;
      })(),
      type: safeText(details.type),
      time: new Date(details.timeStamp).toISOString()
    };
    inFlight.set(details.requestId, record);
    if (inFlight.size > 300) inFlight.delete(inFlight.keys().next().value);
  }, { urls: ["<all_urls>"] }, ["requestBody"]);
  chrome.webRequest.onCompleted.addListener(details => {
    if (!enabled) return;
    const record = inFlight.get(details.requestId);
    if (!record) return;
    inFlight.delete(details.requestId);
    append({ ...record, status: details.statusCode });
  }, { urls: ["<all_urls>"] });
  chrome.webRequest.onErrorOccurred.addListener(details => {
    inFlight.delete(details.requestId);
  }, { urls: ["<all_urls>"] });

  // 每次 Service Worker 重启默认关闭，避免无感长时间跟踪。
  globalThis.handleLanzouNetworkControl = async function(task) {
    const operation = String(task.operation || "");
    if (operation === "start") { events = []; inFlight.clear(); enabled = true; }
    else if (operation === "stop") { enabled = false; inFlight.clear(); }
    else if (operation === "clear") { events = []; inFlight.clear(); }
    // API replay is intentionally disabled until an authenticated traffic sample
    // proves the endpoint, request schema and response semantics.
    else if (operation !== "status" && operation !== "snapshot") throw new Error("unsupported operation");
    return {
      enabled, count: events.length,
      fieldsPolicy: "metadata-only; no cookies, auth, query, raw bodies or response bodies",
      events: operation === "snapshot" ? events.slice() : []
    };
  };
})();
