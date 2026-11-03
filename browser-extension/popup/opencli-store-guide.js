// Shared popup guide; store auto-opening is allowed ONLY after an explicit
// button click in the Yanzi popup. Preserve clickable manual link on failure.
window.yanziOpenCliShowGuideIfMissing = async (reply, statusNode) => {
  const code = reply?.code;
  if (code !== "opencli_extension_missing" && code !== "opencli_extension_disabled") return false;
  if (!statusNode) return true;
  const installMissing = code === "opencli_extension_missing";
  const store = "https://chromewebstore.google.com/detail/opencli/ildkmabpimmkaediidaifkhjpohdnifk";
  const old = statusNode.parentElement?.querySelector(".opencli-install-guide");
  old?.remove();
  const guide = document.createElement("div");
  guide.className = "opencli-install-guide";
  const message = document.createElement("span");
  message.textContent = installMissing
    ? "没有检测到 OpenCLI 扩展。请从官方商店安装，安装完成后回到燕子重新读取。"
    : "OpenCLI 扩展已经安装但处于禁用状态，请在浏览器扩展管理页启用。";
  const action = document.createElement("a");
  action.textContent = installMissing ? "前往 OpenCLI 官方扩展商店" : "打开扩展管理";
  action.href = installMissing ? store : "chrome://extensions";
  action.rel = "noopener noreferrer";
  action.target = "_blank";
  guide.append(message, action);
  statusNode.after(guide);
  if (installMissing) {
    try {
      const opened = await chrome.runtime.sendMessage({action:"yanzi_opencli_open_store"});
      if (opened?.reason === "cooldown")
        message.textContent += " 商店已引导过，可随时点击下方链接重新打开。";
    } catch {
      message.textContent += " 自动打开失败，请点击下方链接。";
    }
  }
  return true;
};
