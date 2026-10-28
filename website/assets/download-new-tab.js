(function () {
  const WINDOWS_URL = "https://wwbnh.lanzout.com/b0pnkaj6j";
  const ANDROID_URL = "https://wwbnh.lanzout.com/b0pnm6z2j";
  const WINDOWS_CODE = "62yn";
  const ANDROID_CODE = "92ty";

  async function copyCode(code) {
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(code);
      }
      alert(`提取码 ${code} 已复制，打开蓝奏云后直接粘贴提取码即可。`);
    } catch {
      alert(`蓝奏云提取码：${code}。请复制后粘贴到蓝奏云。`);
    }
  }

  function bindDownloadLink(link, url, code) {
    if (!link || link.dataset.newTabDownloadBound === "1") return;
    link.dataset.newTabDownloadBound = "1";
    link.href = url;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.addEventListener("click", async function (event) {
      event.preventDefault();
      await copyCode(code);
      window.open(url, "_blank", "noopener,noreferrer");
    }, true);
  }

  function bindDownloads() {
    document.querySelectorAll('.site-header nav a[href="#download"], .hero-actions .js-update-download-link').forEach((link) => {
      bindDownloadLink(link, WINDOWS_URL, WINDOWS_CODE);
    });

    const windowsButton = document.querySelector('#download .download-panel:not(.download-panel-mobile) .button.primary');
    const androidButton = document.querySelector('#download .download-panel.download-panel-mobile .button.primary');
    bindDownloadLink(windowsButton, WINDOWS_URL, WINDOWS_CODE);
    bindDownloadLink(androidButton, ANDROID_URL, ANDROID_CODE);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", bindDownloads);
  } else {
    bindDownloads();
  }

  const retryDelays = [300, 1200];
  retryDelays.forEach((delay) => {
    setTimeout(bindDownloads, delay);
  });
})();
