// Restrict the browser-side protocol to read-only personal-data operations.
const YANZI_OPENCLI_READS = Object.freeze({
  xiaohongshu: Object.freeze(["profile", "stats", "notes"]),
  bilibili: Object.freeze(["profile", "history", "following"]),
  douyin: Object.freeze(["profile", "videos"])
});
const yanziOpenCliPending = new Map();

globalThis.handleYanziOpenCliResponse = (message) => {
  const pending = yanziOpenCliPending.get(message.requestId);
  if (!pending) return;
  clearTimeout(pending.timer);
  yanziOpenCliPending.delete(message.requestId);
  pending.sendResponse({
    ok: message.ok === true,
    data: message.ok ? message.data : undefined,
    error: message.ok ? undefined : String(message.error || "读取失败")
  });
};

globalThis.yanziOpenCliPerformRead = (site, operation) => new Promise((resolve) => {
  if (!Object.hasOwn(YANZI_OPENCLI_READS, site) ||
      !YANZI_OPENCLI_READS[site].includes(operation)) {
    resolve({ ok: false, error: "不允许的读取操作" });
    return;
  }
  if (!globalThis.yanziBrowserHost?.isConnected()) {
    resolve({ ok: false, error: "燕子主程序未连接" });
    return;
  }
  const requestId = crypto.randomUUID();
  const timer = setTimeout(() => {
    yanziOpenCliPending.delete(requestId);
    resolve({ ok: false, error: "OpenCLI 读取超时" });
  }, 110000);
  yanziOpenCliPending.set(requestId, { sendResponse: resolve, timer });
  try {
    globalThis.yanziBrowserHost.send({ type: "opencli_request", requestId, site, operation });
  } catch (error) {
    clearTimeout(timer);
    yanziOpenCliPending.delete(requestId);
    resolve({ ok: false, error: String(error?.message || error) });
  }
});

globalThis.yanziOpenCliPerformRead = (site, operation) => new Promise(resolve => {
  if (!Object.hasOwn(YANZI_OPENCLI_READS, site) || !YANZI_OPENCLI_READS[site].includes(operation)) {
    resolve({ok:false,error:"不允许的读取操作"}); return;
  }
  if (!globalThis.yanziBrowserHost?.isConnected()) {
    resolve({ok:false,error:"燕子主程序未连接"}); return;
  }
  const requestId = crypto.randomUUID();
  const timer = setTimeout(() => {
    yanziOpenCliPending.delete(requestId);
    resolve({ok:false,error:"OpenCLI 读取超时"});
  }, 110000);
  yanziOpenCliPending.set(requestId,{sendResponse:resolve,timer});
  try {
    globalThis.yanziBrowserHost.send({type:"opencli_request",requestId,site,operation});
  } catch(error) {
    clearTimeout(timer);
    yanziOpenCliPending.delete(requestId);
    resolve({ok:false,error:error?.message||"调用失败"});
  }
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.action !== "yanzi_opencli_read") return;
  if (sender.id !== chrome.runtime.id ||
      !Object.hasOwn(YANZI_OPENCLI_READS, message.site) ||
      !YANZI_OPENCLI_READS[message.site].includes(message.operation)) {
    sendResponse({ ok: false, error: "不允许的读取操作" });
    return;
  }
  if (!globalThis.yanziBrowserHost?.isConnected()) {
    sendResponse({ ok: false, error: "燕子主程序未连接，请先启动燕子" });
    return;
  }

  const requestId = crypto.randomUUID();
  const timer = setTimeout(() => {
    yanziOpenCliPending.delete(requestId);
    sendResponse({ ok: false, error: "OpenCLI 读取超时，请检查浏览器登录状态" });
  }, 110000);
  yanziOpenCliPending.set(requestId, { sendResponse, timer });
  try {
    globalThis.yanziBrowserHost.send({
      type: "opencli_request", requestId,
      site: message.site, operation: message.operation
    });
  } catch (error) {
    clearTimeout(timer);
    yanziOpenCliPending.delete(requestId);
    sendResponse({ ok: false, error: error.message || "无法发送到燕子主程序" });
  }
  return true;
});

globalThis.yanziOpenCliOnDisconnect = () => {
  for (const [id, pending] of yanziOpenCliPending) {
    clearTimeout(pending.timer);
    pending.sendResponse({ ok: false, error: "本地连接已断开" });
    yanziOpenCliPending.delete(id);
  }
};
