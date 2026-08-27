/**
 * 燕子启动器 (Yanzi) - 心愿墙 (Wish Wall) 与 星辰大海 (Starry Wish Galaxy) 交互引擎
 */
(function () {
  const API_BASE = "https://sync.luoluoluo.cc.cd";

  let currentStatus = "all";
  let currentCategory = "all";
  let currentSearch = "";
  let currentPage = 1;
  let activeWishId = null;
  let allWishesCache = [];

  // 初始化
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initWishWall);
  } else {
    initWishWall();
  }

  function initWishWall() {
    initGalaxyCanvas();
    bindTabs();
    bindSearch();
    bindActions();
    loadWishes();
    loadLeaderboard();

    window.addEventListener("yanzi-auth-changed", () => {
      loadWishes();
      loadLeaderboard();
      if (activeWishId) {
        openWishDetailModal(activeWishId);
      }
    });
  }

  // 1. 宇宙星尘 Canvas 粒子背景引擎
  function initGalaxyCanvas() {
    const canvas = document.getElementById("galaxy-canvas");
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let width = (canvas.width = canvas.parentElement.offsetWidth || window.innerWidth);
    let height = (canvas.height = canvas.parentElement.offsetHeight || 440);

    const stars = [];
    const numStars = Math.min(80, Math.floor(width / 15));

    for (let i = 0; i < numStars; i++) {
      stars.push({
        x: Math.random() * width,
        y: Math.random() * height,
        radius: Math.random() * 1.5 + 0.5,
        alpha: Math.random() * 0.8 + 0.2,
        speedX: (Math.random() - 0.5) * 0.3,
        speedY: (Math.random() - 0.5) * 0.3
      });
    }

    function animate() {
      ctx.clearRect(0, 0, width, height);

      // 绘制星辰微粒
      for (let i = 0; i < stars.length; i++) {
        const s = stars[i];
        s.x += s.speedX;
        s.y += s.speedY;

        if (s.x < 0) s.x = width;
        if (s.x > width) s.x = 0;
        if (s.y < 0) s.y = height;
        if (s.y > height) s.y = 0;

        ctx.beginPath();
        ctx.arc(s.x, s.y, s.radius, 0, Math.PI * 2);
        ctx.fillStyle = `rgba(186, 230, 253, ${s.alpha})`;
        ctx.shadowBlur = 6;
        ctx.shadowColor = "#38bdf8";
        ctx.fill();

        // 邻近粒子微弱连线
        for (let j = i + 1; j < stars.length; j++) {
          const s2 = stars[j];
          const dist = Math.hypot(s.x - s2.x, s.y - s2.y);
          if (dist < 90) {
            ctx.beginPath();
            ctx.moveTo(s.x, s.y);
            ctx.lineTo(s2.x, s2.y);
            ctx.strokeStyle = `rgba(59, 130, 246, ${0.15 * (1 - dist / 90)})`;
            ctx.stroke();
          }
        }
      }

      requestAnimationFrame(animate);
    }

    window.addEventListener("resize", () => {
      width = canvas.width = canvas.parentElement.offsetWidth || window.innerWidth;
      height = canvas.height = canvas.parentElement.offsetHeight || 440;
    });

    animate();
  }

  // 2. 渲染顶部星辰大海胶囊轨道流
  function renderGalaxyOrbitStream(wishes) {
    const orbitContainer = document.getElementById("galaxy-orbit-stream");
    if (!orbitContainer || !wishes || wishes.length === 0) return;

    const row1 = wishes.slice(0, Math.ceil(wishes.length / 2));
    const row2 = wishes.slice(Math.ceil(wishes.length / 2));

    function makeNodes(list) {
      // 循环两次以实现 CSS 无缝平滑循环漂移
      const fullList = [...list, ...list, ...list];
      return fullList.map((item) => {
        const isAccepted = item.status === "accepted";
        const isAnswered = item.status === "answered";
        const stateClass = isAccepted ? "is-accepted" : isAnswered ? "is-answered" : "is-open";
        const badgeText = isAccepted ? "🏆 已采纳" : isAnswered ? `💡 ${item.reply_count}方案` : "✨ 征集中";

        return `
          <div class="galaxy-star-node ${stateClass} js-galaxy-node" data-wish-id="${item.id}" title="${escapeHtml(item.title)}">
            <span class="star-dot"></span>
            <span class="star-title">${escapeHtml(item.title)}</span>
            <span class="star-badge">${badgeText}</span>
            <span style="color:#facc15; font-size:0.75rem;">+${item.reward_points || 50}分</span>
          </div>
        `;
      }).join("");
    }

    orbitContainer.innerHTML = `
      <div class="wish-orbit-row">
        ${makeNodes(row1.length ? row1 : wishes)}
      </div>
      <div class="wish-orbit-row reverse">
        ${makeNodes(row2.length ? row2 : wishes)}
      </div>
    `;

    // 绑定星辰微粒点击直接打开心愿详情与代码
    orbitContainer.querySelectorAll(".js-galaxy-node").forEach((node) => {
      node.addEventListener("click", () => {
        const id = node.dataset.wishId;
        if (id) openWishDetailModal(id);
      });
    });
  }

  // 绑定状态 Tabs
  function bindTabs() {
    const tabBtns = document.querySelectorAll(".wish-tab-btn");
    tabBtns.forEach((btn) => {
      btn.addEventListener("click", () => {
        tabBtns.forEach((b) => b.classList.remove("active"));
        btn.classList.add("active");
        currentStatus = btn.dataset.status || "all";
        currentPage = 1;
        loadWishes();
      });
    });

    const categorySelect = document.getElementById("wish-category-filter");
    if (categorySelect) {
      categorySelect.addEventListener("change", () => {
        currentCategory = categorySelect.value || "all";
        currentPage = 1;
        loadWishes();
      });
    }
  }

  // 绑定搜索框
  function bindSearch() {
    const searchInput = document.getElementById("wish-search-input");
    if (!searchInput) return;

    let debounceTimer = null;
    searchInput.addEventListener("input", (e) => {
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(() => {
        currentSearch = e.target.value.trim();
        currentPage = 1;
        loadWishes();
      }, 350);
    });
  }

  // 绑定弹窗与提交动作
  function bindActions() {
    const publishBtn = document.getElementById("wish-publish-btn");
    if (publishBtn) {
      publishBtn.addEventListener("click", handleOpenPublishModal);
    }

    // 关闭模态弹窗点击背景
    document.querySelectorAll(".wish-modal-backdrop").forEach((backdrop) => {
      backdrop.addEventListener("click", (e) => {
        if (e.target === backdrop) closeAllModals();
      });
    });

    // 弹窗关闭按钮
    document.querySelectorAll(".wish-modal-close").forEach((btn) => {
      btn.addEventListener("click", closeAllModals);
    });

    // 发布心愿表单提交
    const submitWishBtn = document.getElementById("wish-submit-create-btn");
    if (submitWishBtn) {
      submitWishBtn.addEventListener("click", handleCreateWishSubmit);
    }

    // 提交代码回复方案表单提交
    const submitReplyBtn = document.getElementById("wish-submit-reply-btn");
    if (submitReplyBtn) {
      submitReplyBtn.addEventListener("click", handleCreateReplySubmit);
    }
  }

  // 拉取心愿列表
  async function loadWishes() {
    const container = document.getElementById("wish-cards-list");
    if (!container) return;

    container.innerHTML = `
      <div style="text-align:center; padding: 3rem; color: #94a3b8; grid-column: 1 / -1;">
        <span style="display:inline-block; animation: spin 1s linear infinite;">⏳</span> 正在探索心愿星海...
      </div>
    `;

    try {
      const url = `${API_BASE}/v1/wishes?status=${currentStatus}&category=${currentCategory}&search=${encodeURIComponent(currentSearch)}&page=${currentPage}&limit=18`;
      const res = await fetch(url);
      const data = await res.json();

      if (!data.ok || !data.wishes || data.wishes.length === 0) {
        container.innerHTML = `
          <div class="wish-empty-state" style="grid-column: 1 / -1;">
            <p style="font-size: 2rem; margin-bottom: 0.5rem;">🎋</p>
            <h4 style="color:#e2e8f0; margin-bottom: 0.5rem;">暂无匹配的心愿需求</h4>
            <p>成为第一个提出该分类小程序心愿的人，发布即可获得 +5 积分奖励！</p>
            <button class="wish-primary-btn" style="margin-top: 1rem;" onclick="document.getElementById('wish-publish-btn').click()">
              ＋ 发布第一个心愿
            </button>
          </div>
        `;
        return;
      }

      allWishesCache = data.wishes;
      renderGalaxyOrbitStream(data.wishes);
      renderWishCards(data.wishes, container);
    } catch (err) {
      container.innerHTML = `
        <div class="wish-empty-state" style="grid-column: 1 / -1;">
          <p style="color:#ef4444;">❌ 加载心愿列表失败，请稍后重试</p>
        </div>
      `;
    }
  }

  // 渲染心愿卡片列表
  function renderWishCards(wishes, container) {
    let html = "";
    wishes.forEach((item) => {
      const initial = (item.username || "燕").substring(0, 1).toUpperCase();
      const isAccepted = item.status === "accepted";
      const isAnswered = item.status === "answered";

      let statusBadge = "";
      if (isAccepted) {
        statusBadge = `<span class="wish-status-badge accepted">🏆 已验收通过</span>`;
      } else if (isAnswered) {
        statusBadge = `<span class="wish-status-badge answered">💡 已有 ${item.reply_count} 个方案</span>`;
      } else {
        statusBadge = `<span class="wish-status-badge open">✨ 征集中</span>`;
      }

      const formattedTime = formatTimeAgo(item.created_at);

      html += `
        <div class="wish-card ${isAccepted ? "is-accepted" : ""}" data-wish-id="${item.id}">
          <div class="wish-card-top">
            <div class="wish-author-info">
              <div class="wish-avatar">${escapeHtml(initial)}</div>
              <div class="wish-author-meta">
                <span class="wish-author-name">${escapeHtml(item.username)}</span>
                <span class="wish-time">${formattedTime} · ${escapeHtml(item.category || "通用")}</span>
              </div>
            </div>
            <div style="display:flex; align-items:center; gap:6px;">
              ${statusBadge}
              <span class="wish-reward-badge">+${item.reward_points || 50} 积分</span>
            </div>
          </div>

          <h3 class="wish-title">${escapeHtml(item.title)}</h3>
          <div class="wish-desc">${escapeHtml(item.description)}</div>

          <div class="wish-card-footer">
            <div class="wish-reply-stat">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
              <span>${item.reply_count || 0} 个方案</span>
            </div>
            <div>
              <button class="wish-sec-btn js-view-wish-btn" data-wish-id="${item.id}">
                ${isAccepted ? "查看采纳方案" : "查看 / 提供方案"}
              </button>
            </div>
          </div>
        </div>
      `;
    });

    container.innerHTML = html;

    container.querySelectorAll(".js-view-wish-btn").forEach((btn) => {
      btn.addEventListener("click", () => {
        const id = btn.dataset.wishId;
        if (id) openWishDetailModal(id);
      });
    });
  }

  // 打开心愿详情与回复抽屉
  async function openWishDetailModal(wishId) {
    activeWishId = wishId;
    const modalBackdrop = document.getElementById("wish-detail-modal-backdrop");
    const modalBody = document.getElementById("wish-detail-modal-body");
    if (!modalBackdrop || !modalBody) return;

    modalBackdrop.classList.add("active");
    modalBody.innerHTML = `
      <div style="text-align:center; padding: 2rem; color: #94a3b8;">
        ⏳ 正在加载方案与代码...
      </div>
    `;

    try {
      const res = await fetch(`${API_BASE}/v1/wishes/${wishId}`);
      const data = await res.json();
      if (!data.ok || !data.wish) {
        modalBody.innerHTML = `<p style="color:#ef4444;">无法获取心愿详情</p>`;
        return;
      }

      renderWishDetail(data.wish, data.replies || [], modalBody);
    } catch (err) {
      modalBody.innerHTML = `<p style="color:#ef4444;">网络请求失败</p>`;
    }
  }

  // 渲染心愿详情与方案内容
  function renderWishDetail(wish, replies, container) {
    const currentUser = window.YanziAuth ? window.YanziAuth.getUser() : null;
    const isOwner = currentUser && (currentUser.userId === wish.user_id || currentUser.username === wish.username);
    const isAccepted = wish.status === "accepted";

    let repliesHtml = "";
    if (replies.length === 0) {
      repliesHtml = `
        <div style="text-align:center; padding: 2rem; color: #64748b; background: rgba(0,0,0,0.25); border-radius: 12px; margin-top: 1rem;">
          <p>暂无开发者提供小程序代码，快来成为第一个方案贡献者吧！</p>
        </div>
      `;
    } else {
      replies.forEach((rep) => {
        const repInitial = (rep.username || "燕").substring(0, 1).toUpperCase();
        const repIsAccepted = rep.is_accepted === 1;

        let actionBtnHtml = "";
        if (isOwner && !isAccepted) {
          actionBtnHtml = `
            <button class="wish-primary-btn js-accept-reply-btn" data-reply-id="${rep.id}" style="padding: 0.35rem 0.85rem; font-size: 0.8rem;">
              ✅ 验收通过并采纳
            </button>
          `;
        }

        let codeHtml = "";
        if (rep.code_snippet) {
          codeHtml = `
            <div class="wish-code-block-wrapper">
              <div class="wish-code-header">
                <span>小程序代码 / 配置清单</span>
                <button class="wish-code-copy-btn js-copy-code-btn" data-code="${encodeURIComponent(rep.code_snippet)}">
                  📋 复制代码
                </button>
              </div>
              <pre class="wish-code-content"><code>${escapeHtml(rep.code_snippet)}</code></pre>
            </div>
          `;
        }

        repliesHtml += `
          <div class="wish-reply-item ${repIsAccepted ? "is-accepted" : ""}">
            <div class="wish-reply-header">
              <div class="wish-author-info">
                <div class="wish-avatar" style="width:28px;height:28px;font-size:0.75rem;">${escapeHtml(repInitial)}</div>
                <div class="wish-author-meta">
                  <span class="wish-author-name" style="font-size:0.86rem;">${escapeHtml(rep.username)}</span>
                  <span class="wish-time">${formatTimeAgo(rep.created_at)}</span>
                </div>
              </div>
              <div>
                ${repIsAccepted ? '<span class="wish-accepted-ribbon">👑 最佳方案 (已采纳)</span>' : actionBtnHtml}
              </div>
            </div>

            <div style="font-size: 0.92rem; color: #e2e8f0; line-height: 1.6; margin-bottom: 0.5rem; white-space: pre-wrap;">
              ${escapeHtml(rep.content)}
            </div>

            ${codeHtml}
          </div>
        `;
      });
    }

    container.innerHTML = `
      <div style="margin-bottom: 1.5rem;">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 0.5rem;">
          <span style="font-size: 0.82rem; color: #60a5fa; font-weight: 600;">分类：${escapeHtml(wish.category || "通用")}</span>
          <span class="wish-reward-badge">采纳悬赏 +${wish.reward_points || 50} 积分</span>
        </div>
        <h2 style="font-size: 1.35rem; font-weight: 700; color: #fff; margin: 0 0 0.75rem 0;">${escapeHtml(wish.title)}</h2>
        <div style="background: rgba(0,0,0,0.35); padding: 1rem; border-radius: 12px; border: 1px solid rgba(255,255,255,0.06); color: #cbd5e1; font-size: 0.94rem; line-height: 1.6; white-space: pre-wrap;">
          ${escapeHtml(wish.description)}
        </div>
      </div>

      <div class="wish-replies-section">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 1rem;">
          <h4 style="margin:0; font-size: 1.1rem; color: #fff; display:flex; align-items:center; gap: 6px;">
            💬 社区方案回复 (${replies.length})
          </h4>
          ${
            !isAccepted
              ? `<button class="wish-primary-btn" id="wish-detail-reply-btn" style="padding: 0.4rem 1rem; font-size: 0.85rem;">
                   ＋ 提供代码方案
                 </button>`
              : ""
          }
        </div>

        ${repliesHtml}
      </div>
    `;

    // 复制代码按钮
    container.querySelectorAll(".js-copy-code-btn").forEach((btn) => {
      btn.addEventListener("click", () => {
        const code = decodeURIComponent(btn.dataset.code || "");
        if (code) {
          navigator.clipboard.writeText(code).then(() => {
            btn.textContent = "✅ 已复制";
            setTimeout(() => (btn.textContent = "📋 复制代码"), 2000);
            showPointToast("📋 代码已复制到剪贴板，可在燕子启动器中直接导入！");
          });
        }
      });
    });

    // 提供方案按钮
    const replyBtn = container.querySelector("#wish-detail-reply-btn");
    if (replyBtn) {
      replyBtn.addEventListener("click", () => handleOpenReplyModal(wish));
    }

    // 验收采纳按钮
    container.querySelectorAll(".js-accept-reply-btn").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const replyId = btn.dataset.replyId;
        if (!replyId) return;

        if (!confirm("确认验收并采纳该方案吗？验收后将向该开发者发放 +50 积分，同时为您发放 +10 积分回馈！")) {
          return;
        }

        try {
          btn.disabled = true;
          btn.textContent = "正在验收结算...";
          await window.YanziAuth.api(`/v1/wishes/${wish.id}/accept`, {
            method: "POST",
            body: JSON.stringify({ replyId })
          });

          showPointToast("🏆 验收成功！开发者获得 +50 积分，您获得 +10 积分奖励！");
          openWishDetailModal(wish.id);
          loadWishes();
          loadLeaderboard();
        } catch (err) {
          alert("验收失败: " + (err.message || "未知错误"));
          btn.disabled = false;
          btn.textContent = "✅ 验收通过并采纳";
        }
      });
    });
  }

  // 打开发布心愿弹窗
  function handleOpenPublishModal() {
    if (!window.YanziAuth || !window.YanziAuth.getToken()) {
      if (window.YanziAuth && window.YanziAuth.showLogin) {
        window.YanziAuth.showLogin();
      } else {
        alert("请先登录后再提小程序需求！");
      }
      return;
    }

    const backdrop = document.getElementById("wish-publish-modal-backdrop");
    if (backdrop) {
      backdrop.classList.add("active");
      const titleInput = document.getElementById("wish-form-title");
      if (titleInput) titleInput.focus();
    }
  }

  // 提交心愿
  async function handleCreateWishSubmit() {
    const title = document.getElementById("wish-form-title")?.value?.trim();
    const description = document.getElementById("wish-form-desc")?.value?.trim();
    const category = document.getElementById("wish-form-category")?.value || "通用";
    const submitBtn = document.getElementById("wish-submit-create-btn");

    if (!title) {
      alert("请输入心愿标题！");
      return;
    }
    if (!description || description.length < 5) {
      alert("请至少用 5 个字详细描述您希望小程序具备的功能！");
      return;
    }

    try {
      if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.textContent = "正在发布...";
      }

      await window.YanziAuth.api("/v1/wishes", {
        method: "POST",
        body: JSON.stringify({ title, description, category })
      });

      closeAllModals();
      showPointToast("🎉 心愿发布成功！+5 积分已到账！");

      if (document.getElementById("wish-form-title")) document.getElementById("wish-form-title").value = "";
      if (document.getElementById("wish-form-desc")) document.getElementById("wish-form-desc").value = "";

      loadWishes();
      loadLeaderboard();
    } catch (err) {
      alert("发布失败: " + (err.message || "网络错误"));
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.textContent = "发布心愿 (+5 积分)";
      }
    }
  }

  // 打开提供代码方案弹窗
  function handleOpenReplyModal(wish) {
    if (!window.YanziAuth || !window.YanziAuth.getToken()) {
      if (window.YanziAuth && window.YanziAuth.showLogin) {
        window.YanziAuth.showLogin();
      } else {
        alert("请先登录后再提交方案！");
      }
      return;
    }

    const backdrop = document.getElementById("wish-reply-modal-backdrop");
    const targetTitle = document.getElementById("wish-reply-target-title");
    if (targetTitle) targetTitle.textContent = wish.title;

    if (backdrop) backdrop.classList.add("active");
  }

  // 提交代码方案
  async function handleCreateReplySubmit() {
    if (!activeWishId) return;
    const content = document.getElementById("wish-reply-form-content")?.value?.trim();
    const codeSnippet = document.getElementById("wish-reply-form-code")?.value?.trim();
    const submitBtn = document.getElementById("wish-submit-reply-btn");

    if (!content && !codeSnippet) {
      alert("请填写方案说明或提供小程序代码！");
      return;
    }

    try {
      if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.textContent = "正在提交...";
      }

      await window.YanziAuth.api(`/v1/wishes/${activeWishId}/replies`, {
        method: "POST",
        body: JSON.stringify({ content, codeSnippet })
      });

      const replyBackdrop = document.getElementById("wish-reply-modal-backdrop");
      if (replyBackdrop) replyBackdrop.classList.remove("active");

      showPointToast("🚀 代码方案提交成功！等待心愿发布者验收！");

      if (document.getElementById("wish-reply-form-content")) document.getElementById("wish-reply-form-content").value = "";
      if (document.getElementById("wish-reply-form-code")) document.getElementById("wish-reply-form-code").value = "";

      openWishDetailModal(activeWishId);
      loadWishes();
    } catch (err) {
      alert("提交方案失败: " + (err.message || "网络错误"));
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.textContent = "提交方案";
      }
    }
  }

  // 拉取贡献榜
  async function loadLeaderboard() {
    const listEl = document.getElementById("wish-leaderboard-list");
    if (!listEl) return;

    try {
      const res = await fetch(`${API_BASE}/v1/wishes/leaderboard`);
      const data = await res.json();
      if (!data.ok || !data.leaderboard || data.leaderboard.length === 0) {
        listEl.innerHTML = `
          <div style="color: #64748b; font-size: 0.85rem; text-align: center; padding: 1rem 0;">
            暂无积分排行数据，快来贡献你的第一个小程序方案吧！
          </div>
        `;
        return;
      }

      let html = "";
      data.leaderboard.forEach((user, idx) => {
        const rank = idx + 1;
        const rankClass = rank === 1 ? "rank-1" : rank === 2 ? "rank-2" : rank === 3 ? "rank-3" : "";

        html += `
          <div class="wish-rank-item">
            <div class="wish-rank-left">
              <span class="wish-rank-num ${rankClass}">${rank}</span>
              <span class="wish-rank-user">${escapeHtml(user.username)}</span>
            </div>
            <div class="wish-rank-points">
              <span>✨ ${user.points || 0}</span>
              <span style="font-size:0.75rem; color:#64748b; font-weight:normal;">(${user.accepted_count || 0}采纳)</span>
            </div>
          </div>
        `;
      });

      listEl.innerHTML = html;
    } catch (e) {
      listEl.innerHTML = `<div style="color:#64748b;font-size:0.85rem;">排行榜加载失败</div>`;
    }
  }

  function closeAllModals() {
    document.querySelectorAll(".wish-modal-backdrop").forEach((b) => b.classList.remove("active"));
  }

  function showPointToast(msg) {
    let toast = document.getElementById("wish-point-toast");
    if (!toast) {
      toast = document.createElement("div");
      toast.id = "wish-point-toast";
      toast.className = "wish-point-toast";
      document.body.appendChild(toast);
    }
    toast.innerHTML = msg;
    toast.classList.add("show");
    setTimeout(() => toast.classList.remove("show"), 4000);
  }

  function formatTimeAgo(isoString) {
    if (!isoString) return "刚刚";
    const date = new Date(isoString);
    const now = new Date();
    const diffSec = Math.floor((now - date) / 1000);
    if (diffSec < 60) return "刚刚";
    if (diffSec < 3600) return `${Math.floor(diffSec / 60)} 分钟前`;
    if (diffSec < 86400) return `${Math.floor(diffSec / 3600)} 小时前`;
    if (diffSec < 2592000) return `${Math.floor(diffSec / 86400)} 天前`;
    return date.toLocaleDateString("zh-CN");
  }

  function escapeHtml(str) {
    if (!str) return "";
    return String(str)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#039;");
  }
})();
