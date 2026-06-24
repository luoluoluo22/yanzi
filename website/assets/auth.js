(function() {
  const API_BASE = "https://sync.luoluoluo.cc.cd";
  const TOKEN_KEY = "yanzi.auth.token";
  const USER_KEY = "yanzi.auth.user";

  // 兼容旧版燕幕登录 key 的迁移
  const OLD_TOKEN_KEY = "yanzi.yanm.token";
  const OLD_USER_KEY = "yanzi.yanm.user";

  function getToken() {
    let token = localStorage.getItem(TOKEN_KEY) || sessionStorage.getItem(TOKEN_KEY);
    if (!token) {
      // 迁移旧版
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

  // 统一的 API 请求
  async function apiFetch(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set("accept", "application/json");
    headers.set("x-yanzi-client", "web");
    headers.set("x-yanzi-client-version", "0.1.0");
    if (options.body && !headers.has("content-type")) {
      headers.set("content-type", "application/json");
    }

    const token = getToken();
    if (token) {
      headers.set("authorization", `Bearer ${token}`);
    }

    const response = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers
    });

    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.message || `请求失败：${response.status}`);
    }
    return payload;
  }

  // 全局 API 暴露
  window.YanziAuth = {
    getToken,
    getUser,
    api: apiFetch,
    showLogin: function() {
      showLoginModal();
    },
    logout: function() {
      performLogout();
    }
  };

  // 状态改变时通知
  function notifyStateChanged() {
    const event = new CustomEvent("yanzi-auth-changed", {
      detail: {
        token: getToken(),
        user: getUser()
      }
    });
    window.dispatchEvent(event);
    renderNavbarAuth();
  }

  // 页面加载完成后渲染
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }

  let loginModal = null;
  let profileModal = null;

  function init() {
    injectModals();
    renderNavbarAuth();
    
    // 自动恢复会话
    if (getToken()) {
      apiFetch("/v1/auth/me")
        .then(user => {
          // 如果之前是记住的，就继续记住
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

  // 动态在 nav 中插入登录容器
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
      container.innerHTML = `
        <div id="global-user-profile" class="user-avatar-circle" title="查看个人信息" style="cursor: pointer;">
          ${initial}
        </div>
      `;
      document.getElementById("global-user-profile").addEventListener("click", () => {
        window.location.href = "/profile.html";
      });
    } else {
      container.innerHTML = `
        <button id="global-login-btn" class="button secondary" style="padding: 0 12px; height: 32px; font-size: 0.85rem; border-radius: 6px; margin: 0; cursor: pointer;">
          登录
        </button>
      `;
      document.getElementById("global-login-btn").addEventListener("click", showLoginModal);
    }
  }

  // 注入模态框 HTML
  function injectModals() {
    if (document.getElementById("global-auth-modals")) return;

    const wrapper = document.createElement("div");
    wrapper.id = "global-auth-modals";
    wrapper.innerHTML = `
      <!-- 登录模态框 -->
      <div id="global-login-modal" class="auth-modal-overlay">
        <div class="auth-modal-card">
          <button class="auth-modal-close" id="login-close-btn">&times;</button>
          <h2 style="margin-top: 0; margin-bottom: 1.5rem; font-size: 1.4rem; font-weight: 600; text-align: left;">登录燕子账号</h2>
          
          <div class="auth-modal-field">
            <label for="modal-email">电子邮箱</label>
            <input id="modal-email" class="auth-modal-input" type="email" autocomplete="username" placeholder="name@example.com">
          </div>
          
          <div class="auth-modal-field">
            <label for="modal-password">密码</label>
            <input id="modal-password" class="auth-modal-input" type="password" autocomplete="current-password" placeholder="请输入密码">
          </div>
          
          <label class="auth-modal-remember">
            <input type="checkbox" id="modal-remember" checked>
            记住登录状态
          </label>
          
          <div id="login-status-msg" class="auth-modal-status"></div>
          
          <button id="modal-login-action-btn" class="auth-modal-btn" type="button">安全登录</button>
        </div>
      </div>

      <!-- 个人信息模态框 -->
      <div id="global-profile-modal" class="auth-modal-overlay">
        <div class="auth-modal-card">
          <button class="auth-modal-close" id="profile-close-btn">&times;</button>
          <h2 style="margin-top: 0; margin-bottom: 0.5rem; font-size: 1.4rem; font-weight: 600; text-align: left;">个人中心</h2>
          <p style="color: #9ca3af; font-size: 0.85rem; margin-top: 0; margin-bottom: 1.5rem; text-align: left;">您的燕子账号基本信息</p>
          
          <div class="profile-modal-info">
            <div class="profile-info-row">
              <span>用户名</span>
              <span id="profile-username">-</span>
            </div>
            <div class="profile-info-row">
              <span>电子邮箱</span>
              <span id="profile-email">-</span>
            </div>
            <div class="profile-info-row">
              <span>账号角色</span>
              <span id="profile-role">-</span>
            </div>
          </div>
          
          <button id="modal-logout-action-btn" class="auth-modal-btn logout" type="button">退出登录</button>
        </div>
      </div>
    `;

    document.body.appendChild(wrapper);

    // 绑定事件
    document.getElementById("login-close-btn").addEventListener("click", hideLoginModal);
    document.getElementById("profile-close-btn").addEventListener("click", hideProfileModal);
    document.getElementById("modal-login-action-btn").addEventListener("click", performLogin);
    document.getElementById("modal-logout-action-btn").addEventListener("click", performLogout);

    // 点击遮罩层关闭
    document.getElementById("global-login-modal").addEventListener("click", function(e) {
      if (e.target === this) hideLoginModal();
    });
    document.getElementById("global-profile-modal").addEventListener("click", function(e) {
      if (e.target === this) hideProfileModal();
    });

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

  function hideLoginModal() {
    if (loginModal) loginModal.classList.remove("is-visible");
  }

  function hideProfileModal() {
    if (profileModal) profileModal.classList.remove("is-visible");
  }

  async function performLogin() {
    const email = document.getElementById("modal-email").value.trim();
    const password = document.getElementById("modal-password").value;
    const remember = document.getElementById("modal-remember").checked;
    const loginBtn = document.getElementById("modal-login-action-btn");
    const statusMsg = document.getElementById("login-status-msg");

    if (!email || !password) {
      statusMsg.className = "auth-modal-status error";
      statusMsg.textContent = "请输入邮箱和密码";
      return;
    }

    loginBtn.disabled = true;
    loginBtn.textContent = "正在安全登录...";
    statusMsg.className = "auth-modal-status";
    statusMsg.textContent = "";

    try {
      const auth = await apiFetch("/v1/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password })
      });

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
