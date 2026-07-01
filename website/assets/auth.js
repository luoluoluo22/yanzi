(function() {
  const API_BASE = "https://sync.luoluoluo.cc.cd";
  const TOKEN_KEY = "yanzi.auth.token";
  const USER_KEY = "yanzi.auth.user";

  const OLD_TOKEN_KEY = "yanzi.yanm.token";
  const OLD_USER_KEY = "yanzi.yanm.user";

  function getToken() {
    let token = localStorage.getItem(TOKEN_KEY) || sessionStorage.getItem(TOKEN_KEY);
    if (!token) {
      token = localStorage.getItem(OLD_TOKEN_KEY);
      if (token) {
        localStorage.setItem(TOKEN_KEY, token);
        localStorage.removeItem(OLD_TOKEN_KEY);
      }
    }
    return token || "";
  }

  function setToken(token, remember = false) {
    if (token) {
      if (remember) {
        localStorage.setItem(TOKEN_KEY, token);
        sessionStorage.removeItem(TOKEN_KEY);
      } else {
        sessionStorage.setItem(TOKEN_KEY, token);
        localStorage.removeItem(TOKEN_KEY);
      }
    } else {
      localStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(OLD_TOKEN_KEY);
    }
  }

  function getUser() {
    let userStr = localStorage.getItem(USER_KEY) || sessionStorage.getItem(USER_KEY);
    if (!userStr) {
      userStr = localStorage.getItem(OLD_USER_KEY);
      if (userStr) {
        localStorage.setItem(USER_KEY, userStr);
        localStorage.removeItem(OLD_USER_KEY);
      }
    }
    try {
      return JSON.parse(userStr || "null");
    } catch {
      return null;
    }
  }

  function setUser(user, remember = false) {
    if (user) {
      const userStr = JSON.stringify(user);
      if (remember) {
        localStorage.setItem(USER_KEY, userStr);
        sessionStorage.removeItem(USER_KEY);
      } else {
        sessionStorage.setItem(USER_KEY, userStr);
        localStorage.removeItem(USER_KEY);
      }
    } else {
      localStorage.removeItem(USER_KEY);
      sessionStorage.removeItem(USER_KEY);
      localStorage.removeItem(OLD_USER_KEY);
    }
  }

  async function apiFetch(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set("accept", "application/json");
    headers.set("x-yanzi-client", "web");
    headers.set("x-yanzi-client-version", "0.1.0");
    if (options.body && !headers.has("content-type")) headers.set("content-type", "application/json");
    const token = getToken();
    if (token) headers.set("authorization", `Bearer ${token}`);
    const response = await fetch(`${API_BASE}${path}`, { ...options, headers });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.message || `请求失败：${response.status}`);
    return payload;
  }

  window.YanziAuth = {
    getToken,
    getUser,
    api: apiFetch,
    showLogin: function() { showLoginModal(); },
    logout: function() { performLogout(); }
  };

  function notifyStateChanged() {
    const event = new CustomEvent("yanzi-auth-changed", { detail: { token: getToken(), user: getUser() } });
    window.dispatchEvent(event);
    renderNavbarAuth();
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();

  let loginModal = null;
  let profileModal = null;
  let simplifyingDownload = false;

  function init() {
    injectResponsiveFixes();
    repairDocsHeader();
    injectModals();
    renderNavbarAuth();
    bindTopDownloadLinks();
    simplifyHomeDownloadSection();
    setTimeout(() => { repairDocsHeader(); bindTopDownloadLinks(); simplifyHomeDownloadSection(); }, 300);
    setTimeout(() => { repairDocsHeader(); bindTopDownloadLinks(); simplifyHomeDownloadSection(); }, 1200);
    setTimeout(() => { repairDocsHeader(); bindTopDownloadLinks(); simplifyHomeDownloadSection(); }, 2500);

    if (getToken()) {
      apiFetch("/v1/auth/me")
        .then(user => {
          const isRemembered = !!localStorage.getItem(TOKEN_KEY);
          setUser(user, isRemembered);
          notifyStateChanged();
        })
        .catch(() => {
          setToken("");
          setUser(null);
          notifyStateChanged();
        });
    }
  }

  function injectResponsiveFixes() {
    if (document.getElementById("yanzi-responsive-fixes")) return;
    const style = document.createElement("style");
    style.id = "yanzi-responsive-fixes";
    style.textContent = `
      body.docs-header-repaired::before { display: none !important; content: none !important; }
      @media (max-width: 760px) {
        .site-header { padding: 1rem 1.25rem; gap: 1rem; flex-wrap: wrap; justify-content: center; }
        .site-header .brand { width: 100%; justify-content: center; display: flex; }
        .site-header nav { width: 100%; display: flex; justify-content: center; gap: 1.2rem; flex-wrap: wrap; margin: 0; }
        .site-header nav a { font-size: .92rem; line-height: 1.25; white-space: normal; text-align: center; }
        .doc-page { width: min(100% - 1.5rem, 1100px); padding-top: 2rem; }
        .doc-layout { grid-template-columns: 1fr !important; gap: 1.5rem; }
        .doc-sidebar { position: relative !important; top: auto !important; width: 100%; max-height: none; }
        .guide-card { padding: 1.5rem; }
        .guide-stack { max-width: 100%; }
        .store-grid-container { width: min(100% - 1.5rem, 900px); margin: 0 auto; }
        .store-grid { grid-template-columns: 1fr !important; gap: 1rem !important; padding: 1rem 0 2rem !important; }
        .store-card { flex-direction: row !important; align-items: center !important; justify-content: flex-start !important; gap: 1rem; padding: 1rem; border: 1px solid var(--border); border-radius: 18px; background: var(--panel); text-align: left; min-height: 88px; }
        .store-card:hover { transform: none !important; }
        .store-card-icon-wrapper { width: 56px !important; height: 56px !important; border-radius: 16px !important; margin: 0 !important; flex: 0 0 auto; }
        .store-card-icon { width: 30px !important; height: 30px !important; font-size: 1.4rem !important; }
        .store-card-name { text-align: left !important; font-size: 1rem !important; margin: 0 0 .25rem !important; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: calc(100vw - 150px); }
        .store-card-version { text-align: left !important; font-size: .82rem !important; }
        .doc-hero { padding: 3rem 0 2rem !important; }
        .store-search-container { width: min(100% - 1.5rem, 560px); margin-top: 1rem !important; }
        .store-search-input { height: 52px !important; font-size: 1rem !important; border-radius: 16px !important; }
        .view-mode-detail .store-layout { padding: 2rem 1rem !important; }
        .detail-head { flex-direction: row !important; align-items: center !important; gap: 1rem !important; }
        .detail-title h2 { font-size: 1.45rem !important; }
        .detail-body { font-size: 1rem !important; }
        .detail-body > div[style*="display: flex"] { flex-direction: column; }
      }
    `;
    document.head.appendChild(style);
  }

  function repairDocsHeader() {
    if (!location.pathname.startsWith("/docs/")) return;
    document.body.classList.add("docs-header-repaired");
    document.querySelectorAll("body > :not(.noise):not(header):not(main):not(script):not(style)").forEach(node => {
      if (node.nodeType === Node.TEXT_NODE || String(node.textContent || "").includes("`r`n")) node.remove();
    });
    const brandSpan = document.querySelector(".site-header .brand span");
    if (brandSpan) brandSpan.textContent = "燕子启动器";
    const logo = document.querySelector(".site-header .brand img");
    if (logo) logo.alt = "燕子启动器 Logo";
    const nav = document.querySelector(".site-header nav");
    if (nav && !nav.dataset.repairedDocsNav) {
      nav.dataset.repairedDocsNav = "1";
      nav.innerHTML = `
        <a href="/">首页</a>
        <a href="/store.html">扩展商店</a>
        <a href="/yanm.html">燕幕</a>
        <a href="/docs/product-overview.html" class="active">文档</a>
        <a href="https://wwbnh.lanzout.com/b0pnkaj6j" target="_blank" rel="noopener noreferrer">免费下载</a>
        <span id="global-auth-container" style="margin-left: 16px; display: inline-flex; align-items: center; vertical-align: middle;"></span>
      `;
    }
  }

  function injectMinimalDownloadStyles() {
    if (document.getElementById("minimal-download-style")) return;
    const style = document.createElement("style");
    style.id = "minimal-download-style";
    style.textContent = `
      #download.download-section-minimal { padding-top: 4rem; }
      #download.download-section-minimal .minimal-download-grid { width: min(100%, 980px); margin: 0 auto; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.5rem; }
      #download.download-section-minimal .download-panel { margin: 0 !important; min-height: 260px; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1.1rem; text-align: center; }
      #download.download-section-minimal .download-platform-icon { width: 64px; height: 64px; display: grid; place-items: center; border-radius: 18px; border: 1px solid rgba(255,255,255,0.12); background: rgba(59,130,246,0.12); box-shadow: 0 16px 36px rgba(59,130,246,0.18); }
      #download.download-section-minimal .download-platform-icon img { width: 36px; height: 36px; display: block; }
      #download.download-section-minimal .download-panel > strong { display: none !important; }
      #download.download-section-minimal .button.primary:not(.split-button-arrow) { min-width: 190px; text-decoration: none !important; }
      #download.download-section-minimal .button.primary:hover, #download.download-section-minimal .button.primary:focus, #download.download-section-minimal .button.primary:visited { text-decoration: none !important; }
      #download.download-section-minimal .download-password { margin: 0; }
      @media (max-width: 760px) { #download.download-section-minimal .minimal-download-grid { grid-template-columns: 1fr; } }
    `;
    document.head.appendChild(style);
  }

  function showCopyToast(code) {
    const id = "yanzi-copy-toast";
    let toast = document.getElementById(id);
    if (toast) toast.remove();
    
    toast = document.createElement("div");
    toast.id = id;
    toast.className = "copy-toast";
    toast.innerHTML = `
      <div class="copy-toast-content">
        <div class="copy-toast-icon">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"></path><rect x="8" y="2" width="8" height="4" rx="1" ry="1"></rect></svg>
        </div>
        <div class="copy-toast-text">
          <strong>提取码 <span class="toast-code">${code}</span> 已复制</strong>
          <span>打开蓝奏云直接粘贴即可使用</span>
        </div>
      </div>
    `;
    document.body.appendChild(toast);
    
    setTimeout(() => toast.classList.add("show"), 10);
    
    setTimeout(() => {
      toast.classList.remove("show");
      setTimeout(() => { if (toast.parentNode) toast.remove(); }, 400);
    }, 3000);
  }

  function installDownloadClick(button, code) {
    if (!button || button.dataset.copyCodeBound === "1") return;
    button.dataset.copyCodeBound = "1";
    button.target = "_blank";
    button.rel = "noopener noreferrer";
    button.addEventListener("click", async function(event) {
      event.preventDefault();
      const href = button.href;
      try {
        if (navigator.clipboard && navigator.clipboard.writeText) await navigator.clipboard.writeText(code);
      } catch {}
      showCopyToast(code);
      if (href) {
        setTimeout(() => {
          window.open(href, "_blank", "noopener,noreferrer");
        }, 100);
      }
    });
  }

  function bindTopDownloadLinks() {
    const windowsUrl = "https://wwbnh.lanzout.com/b0pnkaj6j";
    document.querySelectorAll('.site-header nav a[href="#download"], .site-header nav a[href="https://wwbnh.lanzout.com/b0pnkaj6j"], .hero-actions .js-update-download-link').forEach((link) => {
      link.href = windowsUrl;
      link.target = "_blank";
      link.rel = "noopener noreferrer";
      installDownloadClick(link, "62yn");
    });
  }

  function getPlatformIconMarkup(platform) {
    const isAndroid = platform === "android";
    const src = isAndroid ? "https://cdn.simpleicons.org/android/ffffff" : "https://cdn.simpleicons.org/windows/ffffff";
    const alt = isAndroid ? "Android" : "Windows";
    return `<div class="download-platform-icon"><img src="${src}" alt="${alt} 图标" loading="lazy"></div>`;
  }

  function prepareDownloadPanel(panel, platform) {
    if (!panel) return;
    const isAndroid = platform === "android";
    const buttonText = isAndroid ? "安卓版下载" : "Windows 版本下载";
    const code = isAndroid ? "92ty" : "62yn";
    const downloadUrl = isAndroid ? "https://wwbnh.lanzout.com/b0pnm6z2j" : "https://wwbnh.lanzout.com/b0pnkaj6j";
    const title = panel.querySelector("strong");
    const meta = isAndroid ? panel.querySelector("span") : panel.querySelector("#download-meta");
    const releaseLink = panel.querySelector(".release-link");
    const button = panel.querySelector(".button.primary");
    const password = panel.querySelector(".download-password");
    if (!panel.querySelector(".download-platform-icon")) panel.insertAdjacentHTML("afterbegin", getPlatformIconMarkup(platform));
    if (title) title.remove();
    if (meta) meta.remove();
    if (releaseLink) releaseLink.remove();
    if (button) {
      button.classList.remove("js-update-download-link");
      button.removeAttribute("data-default-label");
      button.href = downloadUrl;
      button.target = "_blank";
      button.rel = "noopener noreferrer";
      button.textContent = buttonText;
      button.style.textDecoration = "none";
      installDownloadClick(button, code);
    }
    if (password) {
      password.classList.remove("js-update-password-wrap");
      password.innerHTML = `蓝奏云提取码：<strong>${code}</strong>`;
    }
  }

  function simplifyHomeDownloadSection() {
    const section = document.getElementById("download");
    if (!section || simplifyingDownload) return;
    simplifyingDownload = true;
    try {
      injectMinimalDownloadStyles();
      section.classList.add("download-section-minimal");
      const sectionHead = section.querySelector(".section-head");
      if (sectionHead) sectionHead.remove();
      const desktopPanel = section.querySelector(".download-panel:not(.download-panel-mobile)");
      const mobilePanel = section.querySelector(".download-panel-mobile");
      let grid = section.querySelector(".minimal-download-grid");
      if (!grid) {
        grid = document.createElement("div");
        grid.className = "minimal-download-grid";
        if (desktopPanel) grid.appendChild(desktopPanel);
        if (mobilePanel) grid.appendChild(mobilePanel);
        section.appendChild(grid);
      }
      prepareDownloadPanel(desktopPanel, "windows");
      prepareDownloadPanel(mobilePanel, "android");
    } finally {
      simplifyingDownload = false;
    }
  }

  function renderNavbarAuth() {
    const nav = document.querySelector(".site-header nav");
    if (!nav) return;
    let container = document.getElementById("global-auth-container");
    if (!container) {
      container = document.createElement("span");
      container.id = "global-auth-container";
      container.style.marginLeft = "16px";
      container.style.display = "inline-flex";
      container.style.alignItems = "center";
      container.style.verticalAlign = "middle";
      nav.appendChild(container);
    }
    const user = getUser();
    if (user) {
      const initial = (user.username || user.email || "U").substring(0, 1).toUpperCase();
      container.innerHTML = `<div id="global-user-profile" class="user-avatar-circle" title="查看个人信息" style="cursor: pointer;">${initial}</div>`;
      document.getElementById("global-user-profile").addEventListener("click", () => { window.location.href = "/profile.html"; });
    } else {
      container.innerHTML = `<button id="global-login-btn" class="button secondary" style="padding: 0 12px; height: 32px; font-size: 0.85rem; border-radius: 6px; margin: 0; cursor: pointer;">登录</button>`;
      document.getElementById("global-login-btn").addEventListener("click", showLoginModal);
    }
  }

  function injectModals() {
    if (document.getElementById("global-auth-modals")) return;
    const wrapper = document.createElement("div");
    wrapper.id = "global-auth-modals";
    wrapper.innerHTML = `
      <div id="global-login-modal" class="auth-modal-overlay"><div class="auth-modal-card"><button class="auth-modal-close" id="login-close-btn">&times;</button><h2 style="margin-top:0;margin-bottom:1.5rem;font-size:1.4rem;font-weight:600;text-align:left;">登录燕子账号</h2><div class="auth-modal-field"><label for="modal-email">电子邮箱</label><input id="modal-email" class="auth-modal-input" type="email" autocomplete="username" placeholder="name@example.com"></div><div class="auth-modal-field"><label for="modal-password">密码</label><input id="modal-password" class="auth-modal-input" type="password" autocomplete="current-password" placeholder="请输入密码"></div><label class="auth-modal-remember"><input type="checkbox" id="modal-remember" checked>记住登录状态</label><div id="login-status-msg" class="auth-modal-status"></div><button id="modal-login-action-btn" class="auth-modal-btn" type="button">安全登录</button></div></div>
      <div id="global-profile-modal" class="auth-modal-overlay"><div class="auth-modal-card"><button class="auth-modal-close" id="profile-close-btn">&times;</button><h2 style="margin-top:0;margin-bottom:.5rem;font-size:1.4rem;font-weight:600;text-align:left;">个人中心</h2><p style="color:#9ca3af;font-size:.85rem;margin-top:0;margin-bottom:1.5rem;text-align:left;">您的燕子账号基本信息</p><div class="profile-modal-info"><div class="profile-info-row"><span>用户名</span><span id="profile-username">-</span></div><div class="profile-info-row"><span>电子邮箱</span><span id="profile-email">-</span></div><div class="profile-info-row"><span>账号角色</span><span id="profile-role">-</span></div></div><button id="modal-logout-action-btn" class="auth-modal-btn logout" type="button">退出登录</button></div></div>`;
    document.body.appendChild(wrapper);
    document.getElementById("login-close-btn").addEventListener("click", hideLoginModal);
    document.getElementById("profile-close-btn").addEventListener("click", hideProfileModal);
    document.getElementById("modal-login-action-btn").addEventListener("click", performLogin);
    document.getElementById("modal-logout-action-btn").addEventListener("click", performLogout);
    document.getElementById("global-login-modal").addEventListener("click", function(e) { if (e.target === this) hideLoginModal(); });
    document.getElementById("global-profile-modal").addEventListener("click", function(e) { if (e.target === this) hideProfileModal(); });
    loginModal = document.getElementById("global-login-modal");
    profileModal = document.getElementById("global-profile-modal");
  }

  function showLoginModal() {
    if (!loginModal) injectModals();
    document.getElementById("login-status-msg").className = "auth-modal-status";
    document.getElementById("login-status-msg").textContent = "";
    loginModal.classList.add("is-visible");
    document.getElementById("modal-email").focus();
  }
  function hideLoginModal() { if (loginModal) loginModal.classList.remove("is-visible"); }
  function hideProfileModal() { if (profileModal) profileModal.classList.remove("is-visible"); }

  async function performLogin() {
    const email = document.getElementById("modal-email").value.trim();
    const password = document.getElementById("modal-password").value;
    const remember = document.getElementById("modal-remember").checked;
    const loginBtn = document.getElementById("modal-login-action-btn");
    const statusMsg = document.getElementById("login-status-msg");
    if (!email || !password) { statusMsg.className = "auth-modal-status error"; statusMsg.textContent = "请输入邮箱和密码"; return; }
    loginBtn.disabled = true;
    loginBtn.textContent = "正在安全登录...";
    statusMsg.className = "auth-modal-status";
    statusMsg.textContent = "";
    try {
      const auth = await apiFetch("/v1/auth/login", { method: "POST", body: JSON.stringify({ email, password }) });
      setToken(auth.accessToken || "", remember);
      const me = await apiFetch("/v1/auth/me");
      setUser(me, remember);
      document.getElementById("modal-password").value = "";
      hideLoginModal();
      notifyStateChanged();
    } catch (error) {
      statusMsg.className = "auth-modal-status error";
      statusMsg.textContent = error.message || "登录失败，请检查邮箱和密码";
    } finally {
      loginBtn.disabled = false;
      loginBtn.textContent = "安全登录";
    }
  }

  function performLogout() {
    setToken("");
    setUser(null);
    hideProfileModal();
    notifyStateChanged();
  }
})();