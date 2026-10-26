// Local, manually requested, read-only account data bridge.
const YANZI_OPENCLI_READS = Object.freeze({
  xiaohongshu: Object.freeze(["profile", "stats", "notes"]),
  bilibili: Object.freeze(["profile", "history", "following"]),
  douyin: Object.freeze(["profile", "videos"])
});
const yanziOpenCliPending = new Map();

function validateOpenCliRead(site, operation, page = 1) {
  if (!Object.hasOwn(YANZI_OPENCLI_READS, site) ||
      !YANZI_OPENCLI_READS[site].includes(operation)) {
    throw new Error("不允许的读取操作");
  }
  if (!Number.isInteger(page) || page < 1 || page > 20 ||
      (site !== "douyin" || operation !== "videos") && page !== 1) {
    throw new Error("页码必须在允许的范围内");
  }
}

function requestYanziOpenCliRead(site, operation, page = 1) {
  try {
    validateOpenCliRead(site, operation, page);
  } catch (error) {
    return Promise.resolve({ ok: false, error: error.message });
  }
  if (!globalThis.yanziBrowserHost?.isConnected()) {
    return Promise.resolve({ ok: false, error: "燕子主程序未连接，请先启动燕子" });
  }
  return new Promise(resolve => {
    const requestId = crypto.randomUUID();
    const timer = setTimeout(() => {
      yanziOpenCliPending.delete(requestId);
      resolve({ ok: false, error: "OpenCLI 读取超时，请检查浏览器登录状态" });
    }, 110000);
    yanziOpenCliPending.set(requestId, { resolve, timer });
    try {
      globalThis.yanziBrowserHost.send({
        type: "opencli_request", requestId, site, operation, page
      });
    } catch (error) {
      clearTimeout(timer);
      yanziOpenCliPending.delete(requestId);
      resolve({ ok: false, error: error?.message || "无法发送到燕子主程序" });
    }
  });
}

globalThis.yanziOpenCliPerformRead = requestYanziOpenCliRead;
globalThis.handleYanziOpenCliResponse = message => {
  const pending = yanziOpenCliPending.get(message?.requestId);
  if (!pending) return;
  clearTimeout(pending.timer);
  yanziOpenCliPending.delete(message.requestId);
  pending.resolve({
    ok: message.ok === true,
    page: Number.isInteger(message.page) ? message.page : undefined,
    data: message.ok === true ? message.data : undefined,
    error: message.ok === true ? undefined : String(message.error || "读取失败")
  });
};

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.action !== "yanzi_opencli_read") return;
  if (sender.id !== chrome.runtime.id) {
    sendResponse({ ok: false, error: "仅允许燕子扩展自身读取" });
    return;
  }
  void requestYanziOpenCliRead(message.site, message.operation, message.page ?? 1)
    .then(sendResponse)
    .catch(error => sendResponse({ ok: false, error: error?.message || "读取失败" }));
  return true;
});

globalThis.yanziOpenCliOnDisconnect = () => {
  for (const [id, pending] of yanziOpenCliPending) {
    clearTimeout(pending.timer);
    pending.resolve({ ok: false, error: "本地连接已断开" });
    yanziOpenCliPending.delete(id);
  }
};
