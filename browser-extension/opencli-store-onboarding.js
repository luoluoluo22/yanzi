// OpenCLI Browser Bridge onboarding. On demand only: a missing extension can
// open its OFFICIAL Chrome Web Store listing after an explicit popup action.
// Agent/background tasks only receive structured guidance; never spawn tabs.
(() => {
  "use strict";
  const STORE_URL = "https://chromewebstore.google.com/detail/opencli/ildkmabpimmkaediidaifkhjpohdnifk";
  const COOLDOWN_MS = 10 * 60 * 1000;
  const KEY = "opencliInstallGuideOpenedAt";

  function outcome(status, message) {
    return {
      ok: false, code: "opencli_extension_" + status,
      error: message,
      storeUrl: STORE_URL,
      needsInstall: status === "missing",
      needsEnable: status === "disabled"
    };
  }

  async function preflight() {
    // No daemon/host connection is NOT equivalent to an absent extension.
    // Fail open to the ordinary read error in this case.
    if (!globalThis.yanziBrowserHost?.isConnected()
        || typeof globalThis.yanziOpenCliCheckExtension !== "function")
      return { ok: true, status: "unknown" };
    try {
      const reply = await globalThis.yanziOpenCliCheckExtension();
      if (!reply?.ok || !reply.data) return { ok: true, status: "unknown" };
      const status = reply.data.Status || reply.data.status;
      if (status === "missing") return outcome("missing",
        "当前电脑未检测到 OpenCLI Browser Bridge 扩展，请在官方商店安装后重新读取。");
      if (status === "disabled") return outcome("disabled",
        "OpenCLI 扩展已经安装但已禁用，请在浏览器扩展管理页启用，不要重复安装。");
      return { ok: true, status: status || "unknown" };
    } catch { return { ok: true, status: "unknown" }; }
  }

  async function openStore() {
    const now = Date.now();
    const tabs = await chrome.tabs.query({url: STORE_URL + "*"});
    if (tabs.length) {
      await chrome.tabs.update(tabs[0].id, {active: true});
      if (tabs[0].windowId != null)
        await chrome.windows.update(tabs[0].windowId, {focused: true});
      return {ok:true,opened:false,reason:"existing-tab",url:STORE_URL};
    }
    const saved = await chrome.storage.local.get(KEY);
    if (now - Number(saved[KEY] || 0) < COOLDOWN_MS)
      return {ok:true,opened:false,reason:"cooldown",url:STORE_URL};
    const created = await chrome.tabs.create({url:STORE_URL,active:true});
    await chrome.storage.local.set({[KEY]:now});
    return {ok:!!created?.id,opened:!!created?.id,url:STORE_URL};
  }

  globalThis.yanziOpenCliStoreOnboarding = {preflight,openStore,STORE_URL};
  chrome.runtime.onMessage.addListener((message,sender,sendResponse) => {
    if (message?.action !== "yanzi_opencli_open_store") return;
    if (sender.id !== chrome.runtime.id) {
      sendResponse({ok:false,error:"只允许燕子浏览器助手自身打开安装引导"});
      return;
    }
    void openStore().then(sendResponse)
      .catch(e=>sendResponse({ok:false,error:"无法打开官方商店："+String(e?.message||e),url:STORE_URL}));
    return true;
  });
})();
