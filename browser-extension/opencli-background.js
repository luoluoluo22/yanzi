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

function sendOpenCliHostRequest(site, operation, page, profile, timeoutMs) {
  return new Promise(resolve => {
    const requestId = crypto.randomUUID();
    const timer = setTimeout(() => {
      yanziOpenCliPending.delete(requestId);
      resolve({ ok: false, error: operation === "capabilities"
        ? "当前燕子宿主尚不支持 OpenCLI 数据读取，请更新宿主后重试。"
        : "OpenCLI 读取超时，请检查网站登录与浏览器连接。" });
    }, timeoutMs);
    yanziOpenCliPending.set(requestId, { resolve, timer });
    try {
      globalThis.yanziBrowserHost.send({ type: "opencli_request", requestId, site, operation, page, profile });
    } catch (error) {
      clearTimeout(timer);
      yanziOpenCliPending.delete(requestId);
      resolve({ ok: false, error: error?.message || "无法发送到燕子主程序" });
    }
  });
}

async function requestYanziOpenCliRead(site, operation, page = 1, profile = "") {
  try { validateOpenCliRead(site, operation, page); }
  catch (error) { return { ok: false, error: error.message }; }
  if (!globalThis.yanziBrowserHost?.isConnected())
    return { ok: false, error: "燕子主程序未连接，请先启动燕子。" };
  if (site === "xiaohongshu") {
    const capabilities = await sendOpenCliHostRequest(site, "capabilities", 1, "", 5000);
    if (!capabilities.ok) return capabilities;
  }
  return sendOpenCliHostRequest(site, operation, page, profile, 100000);
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
    data: message.ok === true ? (message.result?.data ?? message.data) : undefined,
    source: message.result?.source,
    profile: message.result?.profile,
    command: message.result?.command,
    collectedAt: message.result?.collectedAt,
    period: message.result?.period,
    error: message.ok === true ? undefined : String(message.error || "读取失败")
  });
};

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.action !== "yanzi_opencli_read") return;
  if (sender.id !== chrome.runtime.id) {
    sendResponse({ ok: false, error: "仅允许燕子扩展自身读取" });
    return;
  }
  void requestYanziOpenCliRead(message.site, message.operation, message.page ?? 1, message.profile ?? "")
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
