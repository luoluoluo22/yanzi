// Metadata-only, opt-in scoped observers for Yanzi-owned ChatGPT tabs.
// Never request headers, request bodies, response bodies, cookies, URL query, or full URLs.
(() => {
  const sessions = new Map();
  const filter = { urls: ["https://chatgpt.com/*"] };
  const bands = code => {
    if (!Number.isInteger(code)) return "unknown";
    return code >= 200 && code < 300 ? "2xx" :
      code >= 300 && code < 400 ? "3xx" :
      code >= 400 && code < 500 ? "4xx" :
      code >= 500 && code < 600 ? "5xx" : "other";
  };
  const kind = url => {
    try {
      const path = new URL(url).pathname;
      return path.startsWith("/backend-api/") ? "conversation_api" :
        path.startsWith("/api/") ? "site_api" : "page_resource";
    } catch { return "page_resource"; }
  };
  const method = value =>
    ["GET", "POST", "PUT", "PATCH", "DELETE"].includes(value) ? value : "OTHER";
  const requestType = value =>
    ["xmlhttprequest", "fetch", "websocket", "main_frame", "sub_frame",
      "script", "stylesheet", "image"].includes(value) ? value : "other";
  const add = (object, key) => { object[key] = (object[key] || 0) + 1; };
  function onBeforeRequest(d) {
    const session = sessions.get(d.tabId);
    if (!session || session.done || session.total >= 200) return;
    session.total += 1;
    const tag = kind(d.url);
    add(session.classes, tag);
    add(session.methods, method(d.method));
    add(session.types, requestType(d.type));
    session.requests.set(d.requestId, {started:Date.now(), tag});
  }
  function complete(d, failed) {
    const session = sessions.get(d.tabId);
    if (!session || session.done) return;
    const req = session.requests.get(d.requestId);
    if (!req) return;
    session.requests.delete(d.requestId);
    if (failed) session.failed += 1;
    else {
      session.completed += 1;
      add(session.statusBands, bands(d.statusCode));
    }
    session.longestMs = Math.max(session.longestMs, Math.max(0, Date.now() - req.started));
  }
  const networkApi = chrome.webRequest;
  const supported = Boolean(networkApi?.onBeforeRequest?.addListener &&
    networkApi?.onCompleted?.addListener && networkApi?.onErrorOccurred?.addListener);
  if (supported) {
    // No extraInfoSpec: request bodies and headers are intentionally never requested.
    networkApi.onBeforeRequest.addListener(onBeforeRequest, filter);
    networkApi.onCompleted.addListener(d => complete(d, false), filter);
    networkApi.onErrorOccurred.addListener(d => complete(d, true), filter);
  }
  function start(tabId) {
    if (!supported || !Number.isInteger(tabId) || tabId < 1 ||
        sessions.has(tabId)) return null;
    const session = {
      startedAt:Date.now(), done:false,total:0,completed:0,failed:0,
      methods:{},types:{},classes:{},statusBands:{},requests:new Map(),longestMs:0
    };
    sessions.set(tabId, session);
    const snapshot = () => ({
      available:true, tabId, sampledMs:Math.max(0,Date.now()-session.startedAt),
      started:session.total, completed:session.completed, failed:session.failed,
      pending:session.requests.size, capped:session.total >= 200,
      methods:{...session.methods},types:{...session.types},
      classes:{...session.classes},statusBands:{...session.statusBands},
      longestMs:session.longestMs
    });
    return {
      snapshot,
      stop: () => {
        if (!session.done) {
          session.done = true;
          sessions.delete(tabId);
        }
        return snapshot();
      }
    };
  }
  globalThis.yanziChatGptNetworkObserver = {supported,start};
})();
