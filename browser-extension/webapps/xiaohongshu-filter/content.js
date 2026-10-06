(() => {
  const APP_ID = "xiaohongshu.filter";
  const RULES_KEY = "rules";
  const SETTINGS_KEY = "settings";
  const NOTE_STATS_KEY = "noteStats";
  const CUSTOM_CARDS_KEY = "customCards";
  const STYLE_ID = "yanzi-xhs-filter-style";

  const NOTE_STATS_MAX_ITEMS = 400;
  const NOTE_STATS_MAX_EVENTS = 800;
  const NOTE_STATS_LOCAL_SAVE_DELAY_MS = 450;
  const NOTE_STATS_CLOUD_SYNC_DELAY_MS = 20000;

  // Development-only feed experiment. These cards live only inside the local
  // extension package: they are never written to customCards or noteStats.
  const LOCAL_TEST_NOTES_ENABLED = true;
  const LOCAL_TEST_REAL_NOTES_PER_CARD = 5;
  const LOCAL_FEED_STORAGE_KEY = "yanzi.xhs.local-feed.v2";
  const LOCAL_FEED_REPLENISH_THRESHOLD = 2;
  const LOCAL_FEED_BATCH_SIZE = 30;
  const LOCAL_FEED_GENERATION_RETRY_MS = 60000;
  const LOCAL_FEED_MAX_NOTES = 240;
  const LOCAL_FEED_MAX_FEEDBACK = 120;
  const LOCAL_FEED_MAX_EVALUATIONS = 80;
  const LOCAL_FEED_MAX_INTERESTS = 30;
  const LOCAL_TEST_NOTES = [
    {
      id: "local-test-ai-01",
      title: "如果 AI 每天只替你做一件事，最值得自动化什么？",
      body: "本地测试笔记：把一天里重复、可验证、反馈快的动作交给 AI，观察一周后节省了多少注意力。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-worldmodel-02",
      title: "把“想办法”拆成可计算的路径，会发生什么？",
      body: "先列条件，再找可改变变量，再验证最小动作。这个测试卡只存在当前电脑。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-windows-03",
      title: "Windows 上最被低估的效率提升：减少切换",
      body: "把常用动作压缩成快捷键、悬浮入口和自动触发，比继续安装更多工具更容易产生复利。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-data-04",
      title: "个人数据真正有价值的时刻，是形成时间序列",
      body: "单次记录只是快照；连续几个月的选择、停留、点击和放弃，才开始显露稳定偏好。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-camera-05",
      title: "手机相册不只是存照片，它其实可以成为现实传感器",
      body: "拍摄、地点、时间、选择删除与保留，本身都能成为理解生活状态的低成本信号。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-smart-home-06",
      title: "智能家居更有价值的不是遥控，而是条件触发",
      body: "当“日落后、人在家、电脑准备关机”同时成立，再自动开灯，这才是环境智能。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-invest-07",
      title: "投资里最难积累的不是信息，而是可复盘的决策记录",
      body: "买之前为什么买、什么条件会改变判断、多久复盘一次，比事后解释涨跌更有价值。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-agent-08",
      title: "智能体下一步可能不是会更多工具，而是会自己验证",
      body: "执行、观察结果、判断偏差、自动修正，形成闭环后，工具数量才真正转化为能力。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-attention-09",
      title: "刷信息流时，停留时间可能比点赞更接近真实兴趣",
      body: "点赞是显式动作，停留是低摩擦行为。两者一起看，比单独统计任何一个都更可靠。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-learning-10",
      title: "真正记住一个概念，最好马上换一个现实场景再解释一次",
      body: "如果只能复述原话，说明记住的是表述；换场景还能推出来，才更接近掌握结构。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-vlog-11",
      title: "Vlog 最值得 AI 接管的可能不是剪辑，而是素材选择",
      body: "一天拍 100 个片段后，决定哪 10 个值得留下，本身就是比加转场更重要的表达决策。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    },
    {
      id: "local-test-feedback-12",
      title: "反馈越快，AI 越容易把现实问题变成可优化的问题",
      body: "能在几秒内验证的界面、代码、筛选规则，天然比半年后才知道结果的问题更容易迭代。",
      author: "燕子 · 本地",
      badge: "本地测试",
      localTest: true
    }
  ].map((item, index) => ({
    ...item,
    imageUrl: "",
    url: "",
    enabled: true,
    priority: 1000 - index,
    createdAt: 1,
    updatedAt: 1
  }));

  if (globalThis.__yanziXiaohongshuFilterLoaded) {
    globalThis.__yanziXiaohongshuFilterRefresh?.();
    return;
  }
  globalThis.__yanziXiaohongshuFilterLoaded = true;

  let enabled = true;
  let filteringEnabled = true;
  let rules = [];
  let lastContextTarget = null;
  let filterScheduled = false;
  let observer = null;
  let hoveredCard = null;
  let selectionPopoverContext = null;
  let rulePanelMode = "all";

  let noteStats = {
    version: 1,
    updatedAt: 0,
    items: [],
    customCards: [],
    events: []
  };
  let customCards = [];
  let pendingOpenedNote = null;
  let activeNoteSession = null;
  let analyticsInspectTimer = null;
  let noteStatsSaveTimer = null;
  let noteStatsSyncTimer = null;
  let customCardSequenceIndex = 0;
  let customCardSequenceInitialized = false;
  let localFeedState = {
    version: 3,
    updatedAt: 0,
    notes: [],
    feedback: [],
    evaluations: [],
    preferences: {
      interests: [],
      updatedAt: 0
    },
    generation: {
      inFlight: false,
      startedAt: 0,
      lastCompletedAt: 0,
      lastReturnedCount: 0,
      lastElapsedMs: 0,
      lastContext: null,
      lastError: ""
    }
  };
  let localFeedSaveTimer = null;
  let localFeedRetryTimer = null;
  let localFeedMutationInProgress = false;
  let localFeedLoaded = false;
  let evaluationPanelContext = null;
  const customCardImpressionsThisPage = new Set();

  const CARD_SELECTORS = [
    "section.note-item",
    "section[class*='note-item']",
    ".note-item",
    "[class*='note-item']"
  ];

  const TITLE_SELECTORS = [
    ".title",
    "[class*='title']"
  ];

  const AUTHOR_SELECTORS = [
    ".author .name",
    ".author-wrapper .name",
    "[class*='author'] [class*='name']",
    "a[href*='/user/profile/']"
  ];

  const NOTE_LINK_SELECTORS = [
    "a.cover",
    "a[href*='/explore/']",
    "a[href*='/discovery/item/']"
  ];

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
      .yanzi-xhs-filter-hidden {
        display: none !important;
      }
      section.note-item .title,
      section.note-item .name,
      section.note-item a[href*="/user/profile/"],
      section[class*="note-item"] .title,
      section[class*="note-item"] .name,
      section[class*="note-item"] a[href*="/user/profile/"] {
        -webkit-user-select: text !important;
        user-select: text !important;
      }
      section.note-item a.title,
      section.note-item a.name,
      section.note-item a[href*="/user/profile/"],
      section[class*="note-item"] a.title,
      section[class*="note-item"] a.name,
      section[class*="note-item"] a[href*="/user/profile/"] {
        -webkit-user-drag: none !important;
      }
      .yanzi-xhs-hover-block,
      .yanzi-xhs-hover-manage {
        position: absolute !important;
        top: 7px !important;
        right: 7px !important;
        z-index: 2147483646 !important;
        display: none !important;
        align-items: center !important;
        justify-content: center !important;
        min-width: 42px !important;
        height: 25px !important;
        box-sizing: border-box !important;
        margin: 0 !important;
        padding: 0 9px !important;
        border: 1px solid rgba(255,255,255,.24) !important;
        border-radius: 999px !important;
        background: rgba(20,20,22,.66) !important;
        color: #fff !important;
        font: 500 11px/1 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        letter-spacing: .2px !important;
        text-decoration: none !important;
        opacity: 0 !important;
        visibility: hidden !important;
        pointer-events: none !important;
        backdrop-filter: blur(6px);
        -webkit-backdrop-filter: blur(6px);
        box-shadow: 0 2px 10px rgba(0,0,0,.18) !important;
        cursor: pointer !important;
        user-select: none !important;
        transition: background .14s ease, border-color .14s ease, transform .14s ease !important;
      }
      .yanzi-xhs-filter-card[data-yanzi-hover="1"] > .yanzi-xhs-hover-block,
      .yanzi-xhs-hover-block:focus-visible {
        display: inline-flex !important;
        opacity: 1 !important;
        visibility: visible !important;
        pointer-events: auto !important;
      }
      .yanzi-xhs-hover-manage {
        right: 56px !important;
        min-width: 28px !important;
        width: 28px !important;
        padding: 0 !important;
        font-size: 16px !important;
      }
      .yanzi-xhs-filter-card[data-yanzi-hover="1"] > .yanzi-xhs-hover-manage,
      .yanzi-xhs-hover-manage:focus-visible {
        display: inline-flex !important;
        opacity: 1 !important;
        visibility: visible !important;
        pointer-events: auto !important;
      }
      .yanzi-xhs-hover-block:hover {
        background: #ff2442 !important;
        border-color: #ff2442 !important;
        transform: translateY(-1px) !important;
      }
      .yanzi-xhs-hover-block:active {
        transform: translateY(0) scale(.97) !important;
      }
      .yanzi-xhs-filter-menu {
        position: fixed !important;
        z-index: 2147483647 !important;
        min-width: 176px !important;
        padding: 6px !important;
        border: 1px solid rgba(255,255,255,.12) !important;
        border-radius: 10px !important;
        background: rgba(24,24,26,.96) !important;
        color: #fff !important;
        box-shadow: 0 12px 36px rgba(0,0,0,.30) !important;
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        font: 12px/1.4 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
      }
      .yanzi-xhs-filter-menu[hidden], .yanzi-xhs-filter-panel[hidden] { display: none !important; }
      .yanzi-xhs-menu-item {
        display: flex !important;
        align-items: center !important;
        justify-content: space-between !important;
        gap: 12px !important;
        min-height: 32px !important;
        padding: 0 9px !important;
        border-radius: 7px !important;
        cursor: pointer !important;
        user-select: none !important;
      }
      .yanzi-xhs-menu-item:hover { background: rgba(255,255,255,.08) !important; }
      .yanzi-xhs-menu-state { color: #9ca3af !important; font-size: 11px !important; }
      .yanzi-xhs-filter-panel {
        position: fixed !important;
        top: 72px !important;
        right: 24px !important;
        z-index: 2147483647 !important;
        width: min(360px, calc(100vw - 32px)) !important;
        max-height: min(620px, calc(100vh - 100px)) !important;
        display: flex !important;
        flex-direction: column !important;
        overflow: hidden !important;
        border: 1px solid rgba(255,255,255,.12) !important;
        border-radius: 14px !important;
        background: rgba(24,24,26,.97) !important;
        color: #fff !important;
        box-shadow: 0 18px 50px rgba(0,0,0,.36) !important;
        backdrop-filter: blur(14px);
        -webkit-backdrop-filter: blur(14px);
        font: 13px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
      }
      .yanzi-xhs-panel-head, .yanzi-xhs-panel-toolbar {
        display: flex !important;
        align-items: center !important;
        justify-content: space-between !important;
        gap: 10px !important;
        padding: 11px 14px !important;
        border-bottom: 1px solid rgba(255,255,255,.08) !important;
      }
      .yanzi-xhs-panel-head strong { font-size: 14px !important; font-weight: 600 !important; }
      .yanzi-xhs-panel-toolbar { color: #a1a1aa !important; font-size: 11px !important; }
      .yanzi-xhs-panel-close {
        width: 26px !important;
        height: 26px !important;
        display: grid !important;
        place-items: center !important;
        border-radius: 7px !important;
        cursor: pointer !important;
        color: #a1a1aa !important;
        font-size: 18px !important;
      }
      .yanzi-xhs-panel-close:hover { background: rgba(255,255,255,.08) !important; color: #fff !important; }
      .yanzi-xhs-panel-toggle {
        padding: 5px 9px !important;
        border-radius: 999px !important;
        background: rgba(255,255,255,.08) !important;
        color: #fff !important;
        cursor: pointer !important;
      }
      .yanzi-xhs-rule-list { overflow: auto !important; padding: 6px !important; }
      .yanzi-xhs-rule-empty { padding: 28px 12px !important; text-align: center !important; color: #8b8b93 !important; }
      .yanzi-xhs-rule-row {
        display: flex !important;
        align-items: center !important;
        justify-content: space-between !important;
        gap: 10px !important;
        min-height: 42px !important;
        padding: 7px 8px !important;
        border-radius: 8px !important;
      }
      .yanzi-xhs-rule-row:hover { background: rgba(255,255,255,.05) !important; }
      .yanzi-xhs-rule-main { min-width: 0 !important; }
      .yanzi-xhs-rule-label {
        overflow: hidden !important;
        text-overflow: ellipsis !important;
        white-space: nowrap !important;
        color: #f4f4f5 !important;
      }
      .yanzi-xhs-rule-type { margin-top: 2px !important; color: #85858e !important; font-size: 10px !important; }
      .yanzi-xhs-rule-remove {
        flex-shrink: 0 !important;
        padding: 5px 8px !important;
        border-radius: 7px !important;
        color: #fca5a5 !important;
        cursor: pointer !important;
      }
      .yanzi-xhs-rule-remove:hover { background: rgba(239,68,68,.14) !important; }
      .yanzi-xhs-custom-host > :not(.yanzi-xhs-custom-card) {
        visibility: hidden !important;
        pointer-events: none !important;
      }
      .yanzi-xhs-custom-card {
        position: absolute !important;
        inset: 0 !important;
        z-index: 2147483000 !important;
        display: flex !important;
        flex-direction: column !important;
        overflow: hidden !important;
        box-sizing: border-box !important;
        border-radius: 12px !important;
        background: #fff !important;
        color: #171719 !important;
        box-shadow: 0 1px 0 rgba(0,0,0,.06) !important;
        cursor: default !important;
        visibility: visible !important;
        pointer-events: auto !important;
        font: 13px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
      }
      .yanzi-xhs-custom-card[data-clickable="1"] {
        cursor: pointer !important;
      }
      .yanzi-xhs-custom-cover {
        position: relative !important;
        flex: 1 1 auto !important;
        min-height: 0 !important;
        overflow: hidden !important;
        display: flex !important;
        align-items: center !important;
        justify-content: center !important;
        background:
          radial-gradient(circle at 20% 10%, rgba(255,36,66,.16), transparent 38%),
          linear-gradient(145deg, #f6f6f8, #e9eaee) !important;
      }
      .yanzi-xhs-custom-cover img {
        width: 100% !important;
        height: 100% !important;
        object-fit: cover !important;
        display: block !important;
      }
      .yanzi-xhs-custom-cover-text {
        max-width: 82% !important;
        color: #313137 !important;
        font-size: 18px !important;
        font-weight: 650 !important;
        line-height: 1.35 !important;
        text-align: center !important;
        white-space: pre-wrap !important;
        word-break: break-word !important;
      }
      .yanzi-xhs-custom-badge {
        position: absolute !important;
        top: 9px !important;
        left: 9px !important;
        z-index: 2 !important;
        padding: 4px 7px !important;
        border-radius: 999px !important;
        background: rgba(20,20,22,.72) !important;
        color: #fff !important;
        font-size: 10px !important;
        line-height: 1 !important;
        backdrop-filter: blur(6px);
        -webkit-backdrop-filter: blur(6px);
      }
      .yanzi-xhs-custom-info {
        flex: 0 0 auto !important;
        padding: 10px 11px 11px !important;
        background: #fff !important;
      }
      .yanzi-xhs-custom-heading {
        color: #1d1d20 !important;
        font-size: 14px !important;
        font-weight: 600 !important;
        line-height: 1.4 !important;
        display: -webkit-box !important;
        -webkit-box-orient: vertical !important;
        -webkit-line-clamp: 2 !important;
        overflow: hidden !important;
        user-select: text !important;
        -webkit-user-select: text !important;
        cursor: text !important;
      }
      .yanzi-xhs-custom-body {
        margin-top: 5px !important;
        color: #6f7178 !important;
        font-size: 11px !important;
        line-height: 1.45 !important;
        display: -webkit-box !important;
        -webkit-box-orient: vertical !important;
        -webkit-line-clamp: 2 !important;
        overflow: hidden !important;
        user-select: text !important;
        -webkit-user-select: text !important;
        cursor: text !important;
      }
      .yanzi-xhs-custom-card[data-text-only="1"] .yanzi-xhs-custom-info {
        flex: 1 1 auto !important;
        min-height: 100% !important;
        display: flex !important;
        flex-direction: column !important;
        box-sizing: border-box !important;
        padding: 36px 14px 12px !important;
        background:
          radial-gradient(circle at 18% 6%, rgba(255,36,66,.08), transparent 34%),
          linear-gradient(145deg, #fafafd, #f1f2f6) !important;
      }
      .yanzi-xhs-custom-card[data-text-only="1"] .yanzi-xhs-custom-heading {
        display: block !important;
        overflow: visible !important;
        color: #25262b !important;
        font-size: 17px !important;
        font-weight: 700 !important;
        line-height: 1.45 !important;
        -webkit-line-clamp: unset !important;
      }
      .yanzi-xhs-custom-card[data-text-only="1"] .yanzi-xhs-custom-body {
        flex: 1 1 auto !important;
        display: block !important;
        overflow: hidden !important;
        margin-top: 12px !important;
        color: #4f5159 !important;
        font-size: 13.5px !important;
        line-height: 1.7 !important;
        white-space: pre-wrap !important;
        word-break: break-word !important;
        -webkit-line-clamp: unset !important;
      }
      .yanzi-xhs-custom-card[data-text-only="1"] .yanzi-xhs-custom-author {
        margin-top: 12px !important;
        flex: 0 0 auto !important;
      }
      .yanzi-xhs-custom-author {
        margin-top: 7px !important;
        color: #8b8d94 !important;
        font-size: 11px !important;
        white-space: nowrap !important;
        overflow: hidden !important;
        text-overflow: ellipsis !important;
      }
      .yanzi-xhs-local-feedback {
        position: absolute !important;
        top: 9px !important;
        right: 9px !important;
        z-index: 4 !important;
        display: flex !important;
        align-items: center !important;
        gap: 5px !important;
        margin: 0 !important;
        opacity: 0 !important;
        visibility: hidden !important;
        pointer-events: none !important;
        transform: translateY(-3px) !important;
        transition:
          opacity .14s ease,
          transform .14s ease,
          visibility 0s linear .14s !important;
      }
      .yanzi-xhs-custom-card:hover .yanzi-xhs-local-feedback,
      .yanzi-xhs-custom-card:focus-within .yanzi-xhs-local-feedback {
        opacity: 1 !important;
        visibility: visible !important;
        pointer-events: auto !important;
        transform: translateY(0) !important;
        transition-delay: 0s !important;
      }
      .yanzi-xhs-local-feedback button {
        min-width: 0 !important;
        height: 28px !important;
        padding: 0 9px !important;
        border: 1px solid rgba(255,255,255,.18) !important;
        border-radius: 999px !important;
        background: rgba(26,27,30,.80) !important;
        color: rgba(255,255,255,.92) !important;
        box-shadow: 0 4px 14px rgba(0,0,0,.18) !important;
        backdrop-filter: blur(9px) !important;
        -webkit-backdrop-filter: blur(9px) !important;
        font: 11px/1 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        cursor: pointer !important;
        white-space: nowrap !important;
      }
      .yanzi-xhs-local-feedback button:hover {
        background: rgba(26,27,30,.94) !important;
        color: #fff !important;
      }
      .yanzi-xhs-local-feedback button[data-active="1"] {
        border-color: rgba(255,255,255,.34) !important;
        background: #ff2442 !important;
        color: #fff !important;
        font-weight: 600 !important;
      }

      .yanzi-xhs-interest-nav-item {
        display: block !important;
        width: 100% !important;
        margin: 0 !important;
        padding: 0 !important;
        list-style: none !important;
      }
      .yanzi-xhs-interest-nav {
        display: flex !important;
        width: 100% !important;
        align-items: center !important;
        gap: 10px !important;
        min-height: 44px !important;
        box-sizing: border-box !important;
        padding: 0 12px !important;
        margin: 2px 0 !important;
        border-radius: 10px !important;
        color: inherit !important;
        cursor: pointer !important;
        user-select: none !important;
        font: inherit !important;
      }
      .yanzi-xhs-interest-nav:hover {
        background: rgba(255,255,255,.08) !important;
      }
      .yanzi-xhs-interest-nav svg {
        width: 20px !important;
        height: 20px !important;
        flex: 0 0 20px !important;
      }
      .yanzi-xhs-interest-nav-label {
        min-width: 0 !important;
        flex: 1 1 auto !important;
      }
      .yanzi-xhs-interest-nav-count {
        min-width: 18px !important;
        padding: 2px 5px !important;
        border-radius: 999px !important;
        background: rgba(255,255,255,.08) !important;
        color: #9ca3af !important;
        font-size: 10px !important;
        text-align: center !important;
      }

      .yanzi-xhs-interest-panel,
      .yanzi-xhs-evaluation-panel {
        position: fixed !important;
        z-index: 2147483647 !important;
        width: min(380px, calc(100vw - 28px)) !important;
        max-height: min(620px, calc(100vh - 40px)) !important;
        box-sizing: border-box !important;
        padding: 14px !important;
        overflow: auto !important;
        border: 1px solid rgba(255,255,255,.12) !important;
        border-radius: 14px !important;
        background: rgba(24,24,26,.97) !important;
        color: #fff !important;
        box-shadow: 0 18px 50px rgba(0,0,0,.36) !important;
        backdrop-filter: blur(14px) !important;
        -webkit-backdrop-filter: blur(14px) !important;
        font: 13px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
      }
      .yanzi-xhs-interest-panel[hidden],
      .yanzi-xhs-evaluation-panel[hidden] {
        display: none !important;
      }
      .yanzi-xhs-interest-head,
      .yanzi-xhs-evaluation-head {
        display: flex !important;
        align-items: center !important;
        justify-content: space-between !important;
        gap: 12px !important;
        margin-bottom: 8px !important;
      }
      .yanzi-xhs-interest-head strong,
      .yanzi-xhs-evaluation-head strong {
        font-size: 15px !important;
        font-weight: 650 !important;
      }
      .yanzi-xhs-interest-close,
      .yanzi-xhs-evaluation-close {
        width: 28px !important;
        height: 28px !important;
        border: 0 !important;
        border-radius: 8px !important;
        background: transparent !important;
        color: #a1a1aa !important;
        cursor: pointer !important;
        font-size: 18px !important;
      }
      .yanzi-xhs-interest-close:hover,
      .yanzi-xhs-evaluation-close:hover {
        background: rgba(255,255,255,.08) !important;
        color: #fff !important;
      }
      .yanzi-xhs-interest-help,
      .yanzi-xhs-evaluation-help {
        margin: 0 0 12px !important;
        color: #a1a1aa !important;
        font-size: 12px !important;
      }
      .yanzi-xhs-interest-chips {
        display: flex !important;
        flex-wrap: wrap !important;
        gap: 7px !important;
        min-height: 34px !important;
        margin-bottom: 10px !important;
      }
      .yanzi-xhs-interest-chip {
        display: inline-flex !important;
        align-items: center !important;
        gap: 5px !important;
        min-height: 28px !important;
        padding: 0 9px !important;
        border-radius: 999px !important;
        background: rgba(255,255,255,.09) !important;
        color: #f4f4f5 !important;
      }
      .yanzi-xhs-interest-chip button {
        border: 0 !important;
        padding: 0 !important;
        background: transparent !important;
        color: #a1a1aa !important;
        cursor: pointer !important;
        font-size: 14px !important;
      }
      .yanzi-xhs-interest-input-row {
        display: flex !important;
        gap: 8px !important;
      }
      .yanzi-xhs-interest-input,
      .yanzi-xhs-evaluation-text {
        width: 100% !important;
        box-sizing: border-box !important;
        border: 1px solid rgba(255,255,255,.13) !important;
        border-radius: 10px !important;
        outline: none !important;
        background: rgba(255,255,255,.07) !important;
        color: #fff !important;
        font: 13px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
      }
      .yanzi-xhs-interest-input {
        height: 38px !important;
        padding: 0 10px !important;
      }
      .yanzi-xhs-evaluation-text {
        min-height: 110px !important;
        resize: vertical !important;
        padding: 10px !important;
      }
      .yanzi-xhs-interest-input:focus,
      .yanzi-xhs-evaluation-text:focus {
        border-color: rgba(255,255,255,.32) !important;
      }
      .yanzi-xhs-interest-add,
      .yanzi-xhs-evaluation-save {
        flex: 0 0 auto !important;
        min-height: 38px !important;
        padding: 0 14px !important;
        border: 0 !important;
        border-radius: 10px !important;
        background: #ff2442 !important;
        color: #fff !important;
        font: 600 12px/1 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        cursor: pointer !important;
      }
      .yanzi-xhs-interest-foot,
      .yanzi-xhs-evaluation-foot {
        display: flex !important;
        align-items: center !important;
        justify-content: space-between !important;
        gap: 10px !important;
        margin-top: 10px !important;
        color: #8b8b93 !important;
        font-size: 11px !important;
      }
      .yanzi-xhs-interest-clear {
        border: 0 !important;
        background: transparent !important;
        color: #a1a1aa !important;
        cursor: pointer !important;
        font-size: 11px !important;
      }

      .yanzi-xhs-selection-popover {
        position: fixed !important;
        z-index: 2147483647 !important;
        display: inline-flex !important;
        align-items: stretch !important;
        min-height: 34px !important;
        padding: 3px !important;
        border: 1px solid rgba(255,255,255,.12) !important;
        border-radius: 8px !important;
        background: rgba(28,29,32,.96) !important;
        color: #fff !important;
        box-shadow: 0 8px 24px rgba(0,0,0,.28) !important;
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        font: 12px/1.2 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        user-select: none !important;
      }
      .yanzi-xhs-selection-popover[hidden] { display: none !important; }
      .yanzi-xhs-selection-action {
        display: inline-flex !important;
        align-items: center !important;
        justify-content: center !important;
        min-width: 48px !important;
        padding: 0 10px !important;
        border-radius: 6px !important;
        color: rgba(255,255,255,.94) !important;
        white-space: nowrap !important;
        cursor: pointer !important;
      }
      .yanzi-xhs-selection-action:hover { background: rgba(255,255,255,.10) !important; }
      .yanzi-xhs-selection-action + .yanzi-xhs-selection-action {
        border-left: 1px solid rgba(255,255,255,.10) !important;
        border-top-left-radius: 0 !important;
        border-bottom-left-radius: 0 !important;
      }
      .yanzi-xhs-selection-action[data-action="settings"] {
        color: #b8bcc5 !important;
      }
      .yanzi-xhs-filter-toast {
        position: fixed;
        left: 50%;
        bottom: 36px;
        transform: translateX(-50%);
        z-index: 2147483647;
        background: rgba(20, 20, 22, .92);
        color: #fff;
        border: 1px solid rgba(255,255,255,.12);
        border-radius: 10px;
        padding: 9px 13px;
        font: 13px/1.4 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        box-shadow: 0 8px 28px rgba(0,0,0,.24);
        pointer-events: none;
      }
    `;
    document.documentElement.appendChild(style);
  }

  function escapeHtml(value) {
    return String(value || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function showToast(text) {
    let toast = document.querySelector(".yanzi-xhs-filter-toast");
    if (!toast) {
      toast = document.createElement("div");
      toast.className = "yanzi-xhs-filter-toast";
      document.documentElement.appendChild(toast);
    }
    toast.textContent = text;
    clearTimeout(showToast.timer);
    showToast.timer = setTimeout(() => toast?.remove(), 1800);
  }

  function normalizeText(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function trimText(value, maxLength) {
    const text = normalizeText(value);
    return text.length > maxLength ? text.slice(0, maxLength) : text;
  }

  function cleanNoteTitle(value) {
    return trimText(
      String(value || "")
        .replace(/\s*[-–—|]\s*小红书(?:\s*[-–—|].*)?\s*$/i, "")
        .replace(/\s*[-–—|]\s*你的生活兴趣社区(?:.*)?\s*$/i, ""),
      240
    );
  }

  function uniqueStrings(values, limit = 30) {
    return Array.from(new Set((values || []).map(normalizeText).filter(Boolean))).slice(0, limit);
  }

  function extractTags(value) {
    const text = String(value || "");
    const matches = text.match(/#[^\s#，。！？、；：,.!?]{1,40}/g) || [];
    return uniqueStrings(matches.map(item => item.slice(1)), 30);
  }

  function parseMetricNumber(value) {
    const text = normalizeText(value).replace(/,/g, "").toLowerCase();
    if (!text) return null;
    const match = text.match(/(\d+(?:\.\d+)?)\s*(万|w|k)?/i);
    if (!match) return null;
    let number = Number(match[1]);
    if (!Number.isFinite(number)) return null;
    const unit = String(match[2] || "").toLowerCase();
    if (unit === "万" || unit === "w") number *= 10000;
    if (unit === "k") number *= 1000;
    return Math.round(number);
  }

  function getCardNativeText(card) {
    if (!card) return "";
    const parts = [];
    for (const node of card.childNodes) {
      if (node instanceof Element && node.classList.contains("yanzi-xhs-custom-card")) continue;
      parts.push(node.textContent || "");
    }
    return normalizeText(parts.join(" "));
  }

  function ruleId(type, value) {
    const normalized = normalizeText(value).toLowerCase();
    let hash = 2166136261;
    for (let i = 0; i < normalized.length; i++) {
      hash ^= normalized.charCodeAt(i);
      hash = Math.imul(hash, 16777619);
    }
    return `${type}-${(hash >>> 0).toString(16)}`;
  }

  function findCard(start) {
    if (!(start instanceof Element)) return null;
    for (const selector of CARD_SELECTORS) {
      const found = start.closest(selector);
      if (found) return found;
    }

    let current = start;
    for (let depth = 0; current && depth < 7; depth++, current = current.parentElement) {
      const link = current.querySelector?.("a[href*='/explore/'], a[href*='/discovery/item/']");
      if (link) return current;
    }
    return null;
  }

  function firstText(card, selectors) {
    for (const selector of selectors) {
      const element = card.querySelector(selector);
      const text = normalizeText(element?.innerText || element?.textContent);
      if (text) return text;
    }
    return "";
  }

  function extractIdFromHref(href, marker) {
    if (!href) return "";
    try {
      const url = new URL(href, location.origin);
      const index = url.pathname.indexOf(marker);
      if (index < 0) return "";
      return url.pathname.slice(index + marker.length).split("/")[0] || "";
    } catch {
      return "";
    }
  }

  function extractCardMeta(card) {
    if (!card) return null;
    const title = firstText(card, TITLE_SELECTORS);
    const author = firstText(card, AUTHOR_SELECTORS);

    let authorHref = "";
    const authorLink = card.querySelector("a[href*='/user/profile/']");
    if (authorLink) authorHref = authorLink.href || authorLink.getAttribute("href") || "";

    let noteHref = "";
    for (const selector of NOTE_LINK_SELECTORS) {
      const link = card.querySelector(selector);
      if (link) {
        noteHref = link.href || link.getAttribute("href") || "";
        if (noteHref) break;
      }
    }

    const nativeText = getCardNativeText(card);
    const likeText = firstText(card, [
      ".like-wrapper .count",
      "[class*='like'] [class*='count']",
      "[class*='like'] .count"
    ]);

    return {
      title,
      author,
      authorId: extractIdFromHref(authorHref, "/user/profile/"),
      noteId:
        extractIdFromHref(noteHref, "/explore/") ||
        extractIdFromHref(noteHref, "/discovery/item/"),
      noteHref,
      text: nativeText,
      tags: extractTags(nativeText),
      mediaType: card.querySelector("video")
        ? "video"
        : card.querySelector("img")
          ? "image"
          : "unknown",
      likeText,
      likeCount: parseMetricNumber(likeText)
    };
  }

  function matchesRule(meta, rule) {
    if (!meta || !rule || rule.enabled === false) return false;
    const value = normalizeText(rule.value).toLowerCase();
    if (!value) return false;

    if (rule.type === "keyword") {
      if (rule.scope === "title") {
        return meta.title.toLowerCase().includes(value);
      }
      const haystack = `${meta.title} ${meta.author} ${meta.text}`.toLowerCase();
      return haystack.includes(value);
    }

    if (rule.type === "author") {
      if (rule.authorId && meta.authorId) return rule.authorId === meta.authorId;
      return meta.author.toLowerCase().includes(value);
    }

    if (rule.type === "note") {
      if (rule.noteId && meta.noteId) return rule.noteId === meta.noteId;
      return meta.noteHref.toLowerCase().includes(value);
    }

    return false;
  }

  function collectCards(root = document) {
    const result = new Set();
    for (const selector of CARD_SELECTORS) {
      if (root instanceof Element && root.matches(selector)) result.add(root);
      root.querySelectorAll?.(selector).forEach(card => result.add(card));
    }
    return Array.from(result);
  }

  function getCoverElement(card) {
    if (!card) return null;
    return card.querySelector("a.cover, .cover, a[href*='/explore/'], a[href*='/discovery/item/']");
  }

  function ensureHoverBlockButton(card, meta) {
    if (!card) return;

    if (
      card.classList.contains("yanzi-xhs-custom-host") ||
      card.querySelector(":scope > .yanzi-xhs-custom-card")
    ) {
      card.querySelectorAll(
        ".yanzi-xhs-hover-block, .yanzi-xhs-hover-manage"
      ).forEach(action => action.remove());
      card.classList.remove("yanzi-xhs-filter-card");
      delete card.dataset.yanziHover;
      return;
    }

    if (!meta?.author) return;

    const cover = getCoverElement(card);
    card.classList.add("yanzi-xhs-filter-card");

    card.querySelectorAll(".yanzi-xhs-hover-block, .yanzi-xhs-hover-manage")
      .forEach(action => {
        action.style.removeProperty("display");
        action.style.removeProperty("visibility");
        action.style.removeProperty("opacity");
        action.style.removeProperty("pointer-events");
      });

    const existingButton = card.querySelector(".yanzi-xhs-hover-block");
    if (existingButton) {
      existingButton.title = `屏蔽作者：${meta.author}`;
      existingButton.setAttribute("aria-label", `屏蔽作者 ${meta.author}`);
      return;
    }

    const button = document.createElement("div");
    button.className = "yanzi-xhs-hover-block";
    button.setAttribute("role", "button");
    button.setAttribute("tabindex", "0");
    button.textContent = "屏蔽";
    button.title = `屏蔽作者：${meta.author}`;
    button.setAttribute("aria-label", `屏蔽作者 ${meta.author}`);

    const stop = event => {
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
    };

    button.addEventListener("pointerdown", event => {
      if (event.button !== 2) stop(event);
    }, true);
    button.addEventListener("mousedown", event => {
      if (event.button !== 2) stop(event);
    }, true);
    const activateBlock = async event => {
      stop(event);

      const latestMeta = extractCardMeta(card) || meta;
      if (!latestMeta.author) {
        showToast("没有识别到作者");
        return;
      }

      const added = await addRule({
        type: "author",
        value: latestMeta.authorId || latestMeta.author,
        label: latestMeta.author,
        authorId: latestMeta.authorId
      });

      // addRule() schedules one full-page filter pass on the next frame.
      // Do not hide/reflow this single card separately: that races the site's own
      // masonry update and can leave a whole column on stale offsets.
      if (added) {
        showToast(`已屏蔽作者：${latestMeta.author}`);
      }
    };

    button.addEventListener("click", activateBlock, true);
    button.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        void activateBlock(event);
      }
    }, true);

    card.appendChild(button);

    const manage = document.createElement("div");
    manage.className = "yanzi-xhs-hover-manage";
    manage.setAttribute("role", "button");
    manage.setAttribute("tabindex", "0");
    manage.textContent = "⋯";
    manage.title = "屏蔽设置";
    manage.setAttribute("aria-label", "屏蔽设置");
    manage.addEventListener("pointerdown", stop, true);
    manage.addEventListener("mousedown", stop, true);
    manage.addEventListener("click", event => {
      stop(event);
      const rect = manage.getBoundingClientRect();
      showFilterMenu(rect.left, rect.bottom + 6);
    }, true);
    manage.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        stop(event);
        const rect = manage.getBoundingClientRect();
        showFilterMenu(rect.left, rect.bottom + 6);
      }
    }, true);
    card.appendChild(manage);
  }

  function parseNativeTranslate(card) {
    const transform = getComputedStyle(card).transform;
    if (!transform || transform === "none") return { x: 0, y: 0 };

    try {
      const matrix = new DOMMatrixReadOnly(transform);
      return { x: matrix.m41 || 0, y: matrix.m42 || 0 };
    } catch {
      const match = transform.match(/matrix\([^,]+,[^,]+,[^,]+,[^,]+,\s*([-\d.]+),\s*([-\d.]+)\)/);
      return match
        ? { x: Number(match[1]) || 0, y: Number(match[2]) || 0 }
        : { x: 0, y: 0 };
    }
  }

  function rememberNativeGeometry(card) {
    if (!(card instanceof HTMLElement) ||
        card.classList.contains("yanzi-xhs-filter-hidden")) return;

    const native = parseNativeTranslate(card);
    const rect = card.getBoundingClientRect();
    card.dataset.yanziNativeX = String(native.x);
    card.dataset.yanziNativeY = String(native.y);
    if (rect.height > 0) card.dataset.yanziNativeHeight = String(rect.height);
  }

  function readLayoutItem(card) {
    const hidden = card.classList.contains("yanzi-xhs-filter-hidden");
    const current = parseNativeTranslate(card);
    const storedX = Number(card.dataset.yanziNativeX);
    const storedY = Number(card.dataset.yanziNativeY);
    const storedHeight = Number(card.dataset.yanziNativeHeight);
    const rect = card.getBoundingClientRect();

    return {
      card,
      nativeX: hidden && Number.isFinite(storedX) ? storedX : current.x,
      nativeY: hidden && Number.isFinite(storedY) ? storedY : current.y,
      height: hidden && storedHeight > 0
        ? storedHeight
        : (card.offsetHeight || rect.height || storedHeight || 0),
      hidden
    };
  }

  function estimateVerticalGap(items) {
    const gaps = [];
    const byColumn = new Map();

    for (const item of items) {
      const key = Math.round(item.nativeX * 10) / 10;
      if (!byColumn.has(key)) byColumn.set(key, []);
      byColumn.get(key).push(item);
    }

    for (const column of byColumn.values()) {
      column.sort((a, b) => a.nativeY - b.nativeY);
      for (let i = 1; i < column.length; i++) {
        const previous = column[i - 1];
        const current = column[i];
        const gap = current.nativeY - (previous.nativeY + previous.height);
        if (gap >= 0 && gap <= 64) gaps.push(gap);
      }
    }

    if (!gaps.length) return 16;
    gaps.sort((a, b) => a - b);
    return gaps[Math.floor(gaps.length / 2)];
  }

  function restoreNativeWaterfall(cards) {
    const parents = new Set();
    for (const card of cards) {
      card.style.removeProperty("translate");
      if (card.parentElement) parents.add(card.parentElement);
    }
    for (const parent of parents) {
      if (parent.dataset.yanziCompactHeight === "1") {
        parent.style.removeProperty("height");
        delete parent.dataset.yanziCompactHeight;
      }
    }
  }

  function compactWaterfallLayout(cards) {
    const allCards = cards.filter(card => card instanceof HTMLElement && card.isConnected);
    if (!allCards.length) return;

    const hiddenCount = allCards.filter(card => card.classList.contains("yanzi-xhs-filter-hidden")).length;
    if (!hiddenCount) {
      restoreNativeWaterfall(allCards);
      return;
    }

    const items = allCards.map(readLayoutItem);

    const gap = estimateVerticalGap(items);
    const columns = new Map();

    for (const item of items) {
      const key = Math.round(item.nativeX * 10) / 10;
      if (!columns.has(key)) columns.set(key, []);
      columns.get(key).push(item);
    }

    for (const column of columns.values()) {
      column.sort((a, b) => a.nativeY - b.nativeY);

      // A block in one column must never perturb another column. This is the key
      // difference from the old "repack every column" algorithm.
      if (!column.some(item => item.hidden)) {
        for (const item of column) item.card.style.removeProperty("translate");
        continue;
      }

      let removedSpan = 0;

      for (let index = 0; index < column.length; index++) {
        const item = column[index];

        if (item.hidden) {
          item.card.style.removeProperty("translate");

          const next = column[index + 1];
          // Remove exactly the native slot occupied by this hidden card. Using the
          // site's own Y coordinates preserves all irregular masonry gaps.
          let span = next ? next.nativeY - item.nativeY : item.height + gap;
          if (!Number.isFinite(span) || span < 0) span = 0;

          // If Xiaohongshu has already natively compacted the next card into this
          // slot, span is ~0, so we do not double-shift it.
          removedSpan += span;
          continue;
        }

        if (removedSpan > 0.5) {
          item.card.style.setProperty("translate", `0px ${-removedSpan}px`, "important");
        } else {
          item.card.style.removeProperty("translate");
        }
      }
    }

    // Never override the masonry container height. Xiaohongshu owns scroll extent
    // and bottom-sentinel geometry for infinite loading.
  }

  function requestWaterfallReflow(cards) {
    const snapshot = Array.from(new Set(cards.filter(Boolean)));
    requestAnimationFrame(() => {
      requestAnimationFrame(() => compactWaterfallLayout(snapshot));
    });
  }

  function normalizeCustomCardsValue(value) {
    const items = Array.isArray(value?.items) ? value.items : [];
    return items
      .map(item => ({
        id: normalizeText(item?.id),
        title: trimText(item?.title, 160),
        body: trimText(item?.body || item?.summary || item?.description, 1200),
        author: trimText(item?.author || "燕子", 80),
        badge: trimText(item?.badge || "燕子", 24),
        imageUrl: normalizeText(item?.imageUrl || item?.coverUrl),
        url: normalizeText(item?.url),
        enabled: item?.enabled !== false,
        priority: Number(item?.priority || 0),
        createdAt: Number(item?.createdAt || 0),
        updatedAt: Number(item?.updatedAt || 0)
      }))
      .filter(item => item.id && item.title && item.enabled)
      .sort((a, b) =>
        Number(b.priority || 0) - Number(a.priority || 0) ||
        Number(b.updatedAt || b.createdAt || 0) - Number(a.updatedAt || a.createdAt || 0)
      );
  }

  function normalizeLocalFeedNote(item, index = 0) {
    const state = ["ready", "assigned", "seen"].includes(item?.state)
      ? item.state
      : "ready";
    return {
      id: normalizeText(item?.id) ||
        "local-note-" + Date.now().toString(36) + "-" + index,
      title: trimText(item?.title, 160),
      body: trimText(item?.body, 1200),
      topic: trimText(item?.topic, 80),
      author: trimText(item?.author || "燕子 · 本地", 80),
      badge: trimText(item?.badge || (item?.source === "chatgpt" ? "AI 生成" : "本地测试"), 24),
      imageUrl: normalizeText(item?.imageUrl),
      url: normalizeText(item?.url),
      enabled: item?.enabled !== false,
      localTest: true,
      localFeed: true,
      source: item?.source === "chatgpt" ? "chatgpt" : "seed",
      state,
      assignedAt: Number(item?.assignedAt || 0),
      seenAt: Number(item?.seenAt || 0),
      feedback: item?.feedback === "like" || item?.feedback === "dislike"
        ? item.feedback
        : "",
      feedbackAt: Number(item?.feedbackAt || 0),
      createdAt: Number(item?.createdAt || Date.now()),
      updatedAt: Number(item?.updatedAt || Date.now())
    };
  }

  function createInitialLocalFeedState() {
    return {
      version: 3,
      updatedAt: Date.now(),
      notes: LOCAL_TEST_NOTES.map((note, index) =>
        normalizeLocalFeedNote({
          ...note,
          localFeed: true,
          source: "seed",
          state: "ready"
        }, index)
      ),
      feedback: [],
      evaluations: [],
      preferences: {
        interests: [],
        updatedAt: 0
      },
      generation: {
        inFlight: false,
        startedAt: 0,
        lastCompletedAt: 0,
        lastReturnedCount: 0,
        lastElapsedMs: 0,
        lastContext: null,
        lastError: ""
      }
    };
  }

  function normalizeLocalFeedState(value) {
    if (!value || !Array.isArray(value.notes)) {
      return createInitialLocalFeedState();
    }

    const now = Date.now();
    const notes = value.notes
      .map((item, index) => normalizeLocalFeedNote(item, index))
      .filter(item => item.title && item.body)
      .map(item => {
        if (
          item.state === "assigned" &&
          !item.seenAt &&
          item.assignedAt > 0 &&
          now - item.assignedAt > 10 * 60 * 1000
        ) {
          return { ...item, state: "ready", assignedAt: 0 };
        }
        return item;
      });

    const feedback = (Array.isArray(value.feedback) ? value.feedback : [])
      .filter(item =>
        item &&
        (item.value === "like" || item.value === "dislike")
      )
      .slice(-LOCAL_FEED_MAX_FEEDBACK);

    const evaluations = (Array.isArray(value.evaluations) ? value.evaluations : [])
      .map(item => ({
        id: normalizeText(item?.id) ||
          "evaluation-" + ruleId("evaluation", String(item?.at || Date.now()) + "|" + String(item?.text || "")),
        noteId: normalizeText(item?.noteId),
        text: trimText(item?.text, 500),
        scope: item?.scope === "selection" ? "selection" : "note",
        selectionText: trimText(item?.selectionText, 240),
        title: trimText(item?.title, 160),
        body: trimText(item?.body, 500),
        topic: trimText(item?.topic, 80),
        at: Number(item?.at || Date.now()),
        usedAt: Number(item?.usedAt || 0)
      }))
      .filter(item => item.text)
      .slice(-LOCAL_FEED_MAX_EVALUATIONS);

    const interests = (Array.isArray(value.preferences?.interests)
      ? value.preferences.interests
      : [])
      .map(item => trimText(item, 40))
      .filter(Boolean)
      .filter((item, index, list) =>
        list.findIndex(other => other.toLowerCase() === item.toLowerCase()) === index
      )
      .slice(0, LOCAL_FEED_MAX_INTERESTS);

    const preferences = {
      interests,
      updatedAt: Number(value.preferences?.updatedAt || 0)
    };

    const generation = {
      inFlight: value.generation?.inFlight === true,
      startedAt: Number(value.generation?.startedAt || 0),
      lastCompletedAt: Number(value.generation?.lastCompletedAt || 0),
      requestPreferencesUpdatedAt: Number(
        value.generation?.requestPreferencesUpdatedAt || 0
      ),
      lastReturnedCount: Number(value.generation?.lastReturnedCount || 0),
      lastElapsedMs: Number(value.generation?.lastElapsedMs || 0),
      lastContext: value.generation?.lastContext &&
        typeof value.generation.lastContext === "object"
          ? value.generation.lastContext
          : null,
      lastError: trimText(value.generation?.lastError, 300)
    };

    if (
      generation.inFlight &&
      (!generation.startedAt || now - generation.startedAt > 6 * 60 * 1000)
    ) {
      generation.inFlight = false;
      generation.startedAt = 0;
    }

    return {
      version: 3,
      updatedAt: Number(value.updatedAt || 0),
      notes,
      feedback,
      evaluations,
      preferences,
      generation
    };
  }

  async function persistLocalFeedState() {
    localFeedState.updatedAt = Date.now();

    const seen = localFeedState.notes
      .filter(note => note.state === "seen")
      .sort((a, b) => Number(b.seenAt || 0) - Number(a.seenAt || 0));
    const future = localFeedState.notes
      .filter(note => note.state !== "seen");

    localFeedState.notes = [
      ...future,
      ...seen.slice(0, Math.max(20, LOCAL_FEED_MAX_NOTES - future.length))
    ].slice(0, LOCAL_FEED_MAX_NOTES);

    localFeedState.feedback = (localFeedState.feedback || [])
      .slice(-LOCAL_FEED_MAX_FEEDBACK);
    localFeedState.evaluations = (localFeedState.evaluations || [])
      .slice(-LOCAL_FEED_MAX_EVALUATIONS);
    localFeedState.preferences = {
      interests: (localFeedState.preferences?.interests || [])
        .slice(0, LOCAL_FEED_MAX_INTERESTS),
      updatedAt: Number(localFeedState.preferences?.updatedAt || 0)
    };

    await chrome.storage.local.set({
      [LOCAL_FEED_STORAGE_KEY]: localFeedState
    });
  }

  function scheduleLocalFeedSave() {
    clearTimeout(localFeedSaveTimer);
    localFeedSaveTimer = setTimeout(() => {
      localFeedSaveTimer = null;
      void persistLocalFeedState();
    }, 180);
  }

  async function loadLocalFeedState() {
    const stored = await chrome.storage.local.get(LOCAL_FEED_STORAGE_KEY);
    localFeedState = normalizeLocalFeedState(stored?.[LOCAL_FEED_STORAGE_KEY]);
    localFeedLoaded = true;
    ensureInterestNavButton();
    renderInterestPanel();
    updateInterestNavButton();

    if (!stored?.[LOCAL_FEED_STORAGE_KEY]) {
      await persistLocalFeedState();
    }

    reconcileLocalFeedAssignmentsWithDom();
    scheduleFilter(document);
    markVisibleLocalFeedCards();
    void maybeReplenishLocalFeed();
  }

  function localFeedNoteById(id) {
    return localFeedState.notes.find(note => note.id === id) || null;
  }

  function localFeedFeedbackFor(id) {
    const direct = localFeedState.feedback
      .filter(item =>
        item.noteId === id &&
        item.scope !== "selection"
      )
      .sort((a, b) => Number(b.at || 0) - Number(a.at || 0))[0];
    return direct?.value || localFeedNoteById(id)?.feedback || "";
  }

  function getActiveLocalFeedAssignmentIds() {
    return new Set(
      Array.from(document.querySelectorAll(
        '.yanzi-xhs-custom-card[data-local-feed="1"][data-card-id]'
      ))
        .filter(overlay => overlay instanceof HTMLElement && overlay.isConnected)
        .map(overlay => normalizeText(overlay.dataset.cardId))
        .filter(Boolean)
    );
  }

  function reconcileLocalFeedAssignmentsWithDom() {
    const activeIds = getActiveLocalFeedAssignmentIds();
    let changed = false;
    const now = Date.now();

    for (const note of localFeedState.notes || []) {
      if (
        note.state !== "assigned" ||
        note.seenAt ||
        activeIds.has(note.id)
      ) {
        continue;
      }

      note.state = "ready";
      note.assignedAt = 0;
      note.updatedAt = now;
      changed = true;
    }

    if (changed) scheduleLocalFeedSave();
    return changed;
  }

  function getLocalFeedFutureSupply() {
    const activeIds = getActiveLocalFeedAssignmentIds();
    return localFeedState.notes.filter(note =>
      note.enabled !== false &&
      (
        note.state === "ready" ||
        (
          note.state === "assigned" &&
          !note.seenAt &&
          activeIds.has(note.id)
        )
      )
    ).length;
  }

  function getReadyLocalFeedNotes() {
    return localFeedState.notes
      .filter(note =>
        note.enabled !== false &&
        note.state === "ready"
      )
      .sort((a, b) =>
        Number(a.createdAt || 0) - Number(b.createdAt || 0)
      );
  }

  function assignLocalFeedNote(note) {
    if (!note || note.state !== "ready") return;
    note.state = "assigned";
    note.assignedAt = Date.now();
    note.updatedAt = Date.now();
    scheduleLocalFeedSave();
  }

  function releaseLocalFeedAssignment(cardId) {
    const note = localFeedNoteById(cardId);
    if (!note || note.state !== "assigned" || note.seenAt) return;
    note.state = "ready";
    note.assignedAt = 0;
    note.updatedAt = Date.now();
    scheduleLocalFeedSave();
  }

  function markVisibleLocalFeedCards() {
    let changed = false;
    const now = Date.now();

    document.querySelectorAll(
      '.yanzi-xhs-custom-card[data-local-feed="1"]'
    ).forEach(overlay => {
      const rect = overlay.getBoundingClientRect();
      const visibleHeight =
        Math.min(window.innerHeight, rect.bottom) -
        Math.max(0, rect.top);
      const visibleWidth =
        Math.min(window.innerWidth, rect.right) -
        Math.max(0, rect.left);
      const visible =
        visibleHeight > Math.min(80, rect.height * 0.2) &&
        visibleWidth > Math.min(80, rect.width * 0.2);

      if (!visible) return;

      const note = localFeedNoteById(overlay.dataset.cardId || "");
      if (!note || note.seenAt) return;

      note.state = "seen";
      note.seenAt = now;
      note.updatedAt = now;
      changed = true;
    });

    if (changed) {
      scheduleLocalFeedSave();
      void maybeReplenishLocalFeed();
    }
  }

  function localFeedPromptContext() {
    const feedback = (localFeedState.feedback || [])
      .slice()
      .sort((a, b) => Number(a.at || 0) - Number(b.at || 0));

    const mapFeedbackItem = item => ({
      title: item.title || "",
      body: item.body || "",
      topic: item.topic || "",
      scope: item.scope === "selection" ? "selection" : "note",
      selectionText: item.scope === "selection"
        ? normalizeText(item.selectionText)
        : ""
    });

    const liked = feedback
      .filter(item => item.value === "like")
      .slice(-16)
      .map(mapFeedbackItem);

    const disliked = feedback
      .filter(item => item.value === "dislike")
      .slice(-16)
      .map(mapFeedbackItem);

    const evaluations = (localFeedState.evaluations || [])
      .filter(item => !item.usedAt)
      .sort((a, b) => Number(a.at || 0) - Number(b.at || 0))
      .slice(-12)
      .map(item => ({
        id: item.id,
        text: item.text || "",
        title: item.title || "",
        topic: item.topic || "",
        scope: item.scope === "selection" ? "selection" : "note",
        selectionText: item.scope === "selection"
          ? normalizeText(item.selectionText)
          : ""
      }));

    const recent = localFeedState.notes
      .filter(note => note.seenAt)
      .sort((a, b) => Number(a.seenAt || 0) - Number(b.seenAt || 0))
      .slice(-60)
      .map(note => ({
        title: note.title,
        body: note.body,
        topic: note.topic || ""
      }));

    const interests = (localFeedState.preferences?.interests || []).slice(
      0,
      LOCAL_FEED_MAX_INTERESTS
    );

    return { liked, disliked, evaluations, recent, interests };
  }

  async function maybeReplenishLocalFeed() {
    if (!LOCAL_TEST_NOTES_ENABLED) return;
    reconcileLocalFeedAssignmentsWithDom();
    if (getLocalFeedFutureSupply() > LOCAL_FEED_REPLENISH_THRESHOLD) return;

    const generation = localFeedState.generation || {};
    const now = Date.now();
    if (
      generation.inFlight &&
      generation.startedAt &&
      now - generation.startedAt < 8 * 60 * 1000
    ) {
      return;
    }

    clearTimeout(localFeedRetryTimer);
    localFeedRetryTimer = null;

    const requestPreferencesUpdatedAt = Number(
      localFeedState.preferences?.updatedAt || 0
    );

    localFeedState.generation = {
      ...localFeedState.generation,
      inFlight: true,
      startedAt: now,
      requestPreferencesUpdatedAt,
      lastError: ""
    };
    await persistLocalFeedState();

    try {
      const context = localFeedPromptContext();
      const response = await chrome.runtime.sendMessage({
        type: "yanzi_xhs_generate_local_notes",
        count: LOCAL_FEED_BATCH_SIZE,
        ...context
      });

      if (!response?.ok || !Array.isArray(response.notes)) {
        throw new Error(response?.error || "本地笔记生成失败");
      }

      const currentPreferencesUpdatedAt = Number(
        localFeedState.preferences?.updatedAt || 0
      );
      if (currentPreferencesUpdatedAt !== requestPreferencesUpdatedAt) {
        localFeedState.generation = {
          ...localFeedState.generation,
          inFlight: false,
          startedAt: 0,
          requestPreferencesUpdatedAt: 0,
          lastError: "兴趣已在生成期间更新，旧批次已丢弃"
        };
        await persistLocalFeedState();

        clearTimeout(localFeedRetryTimer);
        localFeedRetryTimer = setTimeout(() => {
          localFeedRetryTimer = null;
          void maybeReplenishLocalFeed();
        }, 500);
        return;
      }

      const existingTitles = new Set(
        localFeedState.notes.map(note => normalizeText(note.title).toLowerCase())
      );
      const added = [];

      for (const item of response.notes) {
        const note = normalizeLocalFeedNote({
          ...item,
          localFeed: true,
          localTest: true,
          source: "chatgpt",
          state: "ready",
          assignedAt: 0,
          seenAt: 0,
          feedback: "",
          feedbackAt: 0
        }, localFeedState.notes.length + added.length);

        const key = normalizeText(note.title).toLowerCase();
        if (!key || existingTitles.has(key)) continue;
        existingTitles.add(key);
        added.push(note);
      }

      localFeedState.notes.push(...added);

      const usedEvaluationIds = new Set(
        Array.isArray(response.contextUsed?.evaluationIds)
          ? response.contextUsed.evaluationIds
          : []
      );
      if (usedEvaluationIds.size) {
        const usedAt = Date.now();
        for (const item of localFeedState.evaluations || []) {
          if (usedEvaluationIds.has(item.id) && !item.usedAt) {
            item.usedAt = usedAt;
            if (item.scope !== "selection") {
              document.querySelectorAll(
                '.yanzi-xhs-custom-card[data-card-id="' +
                CSS.escape(item.noteId || "") +
                '"] .yanzi-xhs-local-feedback button[data-action="evaluate"]'
              ).forEach(button => {
                button.dataset.active = "0";
                button.title = "写下对这类内容的具体评价";
              });
            }
          }
        }
      }

      localFeedState.generation = {
        ...localFeedState.generation,
        inFlight: false,
        startedAt: 0,
        lastCompletedAt: Date.now(),
        requestPreferencesUpdatedAt: 0,
        lastReturnedCount: added.length,
        lastElapsedMs: Number(response.elapsedMs || 0),
        lastContext: response.contextUsed || null,
        lastError: ""
      };
      await persistLocalFeedState();

      scheduleFilter(document);
    } catch (error) {
      localFeedState.generation = {
        ...localFeedState.generation,
        inFlight: false,
        startedAt: 0,
        requestPreferencesUpdatedAt: 0,
        lastError: trimText(error?.message || String(error), 300)
      };
      await persistLocalFeedState();

      clearTimeout(localFeedRetryTimer);
      localFeedRetryTimer = setTimeout(() => {
        localFeedRetryTimer = null;
        void maybeReplenishLocalFeed();
      }, LOCAL_FEED_GENERATION_RETRY_MS);
    }
  }

  async function setLocalFeedFeedback(card, value) {
    if (!card?.id || (value !== "like" && value !== "dislike")) return;

    const now = Date.now();
    const note = localFeedNoteById(card.id);
    if (note) {
      note.feedback = value;
      note.feedbackAt = now;
      note.updatedAt = now;
    }

    localFeedState.feedback = (localFeedState.feedback || [])
      .filter(item =>
        !(item.noteId === card.id && item.scope !== "selection")
      );
    localFeedState.feedback.push({
      noteId: card.id,
      value,
      scope: "note",
      selectionText: "",
      title: trimText(card.title, 160),
      body: trimText(card.body, 500),
      topic: trimText(card.topic, 80),
      source: card.source || "",
      at: now
    });

    await persistLocalFeedState();

    document.querySelectorAll(
      '.yanzi-xhs-custom-card[data-card-id="' +
      CSS.escape(card.id) +
      '"] .yanzi-xhs-local-feedback button[data-feedback]'
    ).forEach(button => {
      button.dataset.active =
        button.dataset.feedback === value ? "1" : "0";
    });
  }

  function pendingEvaluationForCard(cardId) {
    return (localFeedState.evaluations || [])
      .filter(item =>
        item.noteId === cardId &&
        item.scope !== "selection" &&
        !item.usedAt
      )
      .sort((a, b) => Number(b.at || 0) - Number(a.at || 0))[0] || null;
  }

  function pendingSelectionEvaluation(cardId, selectionText) {
    const selected = normalizeText(selectionText);
    return (localFeedState.evaluations || [])
      .filter(item =>
        item.noteId === cardId &&
        item.scope === "selection" &&
        normalizeText(item.selectionText) === selected &&
        !item.usedAt
      )
      .sort((a, b) => Number(b.at || 0) - Number(a.at || 0))[0] || null;
  }

  async function setLocalFeedSelectionFeedback(card, selectionText, value) {
    const selected = trimText(selectionText, 240);
    if (
      !card?.id ||
      !selected ||
      (value !== "like" && value !== "dislike")
    ) {
      return false;
    }

    const now = Date.now();
    localFeedState.feedback = (localFeedState.feedback || [])
      .filter(item =>
        !(
          item.noteId === card.id &&
          item.scope === "selection" &&
          normalizeText(item.selectionText) === normalizeText(selected)
        )
      );

    localFeedState.feedback.push({
      noteId: card.id,
      value,
      scope: "selection",
      selectionText: selected,
      title: trimText(card.title, 160),
      body: trimText(card.body, 500),
      topic: trimText(card.topic, 80),
      source: card.source || "",
      at: now
    });

    await persistLocalFeedState();
    return true;
  }

  async function setLocalFeedEvaluation(card, text, options = {}) {
    const value = trimText(text, 500);
    if (!card?.id || !value) return false;

    const now = Date.now();
    const selectionText = trimText(options.selectionText, 240);
    const scope = selectionText ? "selection" : "note";
    const existing = selectionText
      ? pendingSelectionEvaluation(card.id, selectionText)
      : pendingEvaluationForCard(card.id);

    if (existing) {
      existing.text = value;
      existing.scope = scope;
      existing.selectionText = selectionText;
      existing.title = trimText(card.title, 160);
      existing.body = trimText(card.body, 500);
      existing.topic = trimText(card.topic, 80);
      existing.at = now;
    } else {
      localFeedState.evaluations = localFeedState.evaluations || [];
      localFeedState.evaluations.push({
        id: "evaluation-" + ruleId(
          "evaluation",
          card.id + "|" + selectionText + "|" + now + "|" + value
        ),
        noteId: card.id,
        text: value,
        scope,
        selectionText,
        title: trimText(card.title, 160),
        body: trimText(card.body, 500),
        topic: trimText(card.topic, 80),
        at: now,
        usedAt: 0
      });
    }

    await persistLocalFeedState();

    if (scope === "note") {
      document.querySelectorAll(
        '.yanzi-xhs-custom-card[data-card-id="' +
        CSS.escape(card.id) +
        '"] .yanzi-xhs-local-feedback button[data-action="evaluate"]'
      ).forEach(button => {
        button.dataset.active = "1";
        button.title = "已评价；将在下一轮生成时使用";
      });
    }

    return true;
  }

  async function updateLocalFeedInterests(nextItems) {
    const interests = (Array.isArray(nextItems) ? nextItems : [])
      .map(item => trimText(item, 40))
      .filter(Boolean)
      .filter((item, index, list) =>
        list.findIndex(other => other.toLowerCase() === item.toLowerCase()) === index
      )
      .slice(0, LOCAL_FEED_MAX_INTERESTS);

    localFeedState.preferences = {
      interests,
      updatedAt: Date.now()
    };
    await persistLocalFeedState();
    renderInterestPanel();
    updateInterestNavButton();
  }

  function ensureInterestPanel() {
    let panel = document.querySelector(".yanzi-xhs-interest-panel");
    if (panel) return panel;

    panel = document.createElement("div");
    panel.className = "yanzi-xhs-interest-panel";
    panel.hidden = true;
    panel.innerHTML = `
      <div class="yanzi-xhs-interest-head">
        <strong>兴趣主题</strong>
        <button type="button" class="yanzi-xhs-interest-close" aria-label="关闭">×</button>
      </div>
      <p class="yanzi-xhs-interest-help">决定下一轮 AI 笔记的主要内容。留空时会跨领域探索，不再默认偏向开发或 AI。</p>
      <div class="yanzi-xhs-interest-chips"></div>
      <div class="yanzi-xhs-interest-input-row">
        <input class="yanzi-xhs-interest-input" type="text" maxlength="120" placeholder="例如：摄影、社会观察、汽车、投资">
        <button type="button" class="yanzi-xhs-interest-add">添加</button>
      </div>
      <div class="yanzi-xhs-interest-foot">
        <span class="yanzi-xhs-interest-status"></span>
        <button type="button" class="yanzi-xhs-interest-clear">清空</button>
      </div>
    `;

    const addFromInput = async () => {
      const input = panel.querySelector(".yanzi-xhs-interest-input");
      const parts = String(input.value || "")
        .split(/[，,;；\n]+/)
        .map(item => normalizeText(item))
        .filter(Boolean);
      if (!parts.length) return;

      input.value = "";
      await updateLocalFeedInterests([
        ...(localFeedState.preferences?.interests || []),
        ...parts
      ]);
      input.focus();
    };

    panel.querySelector(".yanzi-xhs-interest-close").addEventListener("click", () => {
      panel.hidden = true;
    });
    panel.querySelector(".yanzi-xhs-interest-add").addEventListener("click", () => {
      void addFromInput();
    });
    panel.querySelector(".yanzi-xhs-interest-input").addEventListener("keydown", event => {
      if (event.key !== "Enter") return;
      event.preventDefault();
      void addFromInput();
    });
    panel.querySelector(".yanzi-xhs-interest-clear").addEventListener("click", () => {
      void updateLocalFeedInterests([]);
    });

    document.documentElement.appendChild(panel);
    return panel;
  }

  function renderInterestPanel() {
    const panel = document.querySelector(".yanzi-xhs-interest-panel");
    if (!panel) return;

    const interests = localFeedState.preferences?.interests || [];
    const chips = panel.querySelector(".yanzi-xhs-interest-chips");
    chips.replaceChildren();

    if (!interests.length) {
      const empty = document.createElement("span");
      empty.style.color = "#8b8b93";
      empty.style.fontSize = "12px";
      empty.textContent = "暂未设置：下一轮将跨领域探索";
      chips.appendChild(empty);
    } else {
      for (const interest of interests) {
        const chip = document.createElement("span");
        chip.className = "yanzi-xhs-interest-chip";

        const label = document.createElement("span");
        label.textContent = interest;

        const remove = document.createElement("button");
        remove.type = "button";
        remove.setAttribute("aria-label", "移除 " + interest);
        remove.textContent = "×";
        remove.addEventListener("click", event => {
          event.preventDefault();
          event.stopPropagation();
          void updateLocalFeedInterests(
            interests.filter(item => item !== interest)
          );
        });

        chip.appendChild(label);
        chip.appendChild(remove);
        chips.appendChild(chip);
      }
    }

    panel.querySelector(".yanzi-xhs-interest-status").textContent =
      interests.length
        ? `已设置 ${interests.length} 个主题 · 下一轮补货生效`
        : "未设置主题";
  }

  function findMessageNavItem() {
    const candidates = Array.from(
      document.querySelectorAll("a, button, li, div, span")
    )
      .filter(element => {
        if (element.closest(".yanzi-xhs-interest-nav")) return false;
        if (normalizeText(element.textContent) !== "消息") return false;
        const rect = element.getBoundingClientRect();
        return rect.width > 0 &&
          rect.height > 0 &&
          rect.left < 240 &&
          rect.top >= 0 &&
          rect.top < window.innerHeight;
      })
      .sort((a, b) => {
        const ar = a.getBoundingClientRect();
        const br = b.getBoundingClientRect();
        return (ar.width * ar.height) - (br.width * br.height);
      });

    const label = candidates[0];
    if (!label) return null;

    let node = label;
    let fallback = label.parentElement || label;
    for (let depth = 0; depth < 6 && node; depth += 1) {
      const rect = node.getBoundingClientRect();
      if (
        rect.width >= 54 &&
        rect.width <= 220 &&
        rect.height >= 32 &&
        rect.height <= 76
      ) {
        fallback = node;
      }
      if (node.matches("a, button, li, [role='button']")) return node;
      node = node.parentElement;
    }

    return fallback;
  }

  function updateInterestNavButton() {
    const button = document.querySelector(".yanzi-xhs-interest-nav");
    if (!button) return;
    const count = localFeedState.preferences?.interests?.length || 0;
    const badge = button.querySelector(".yanzi-xhs-interest-nav-count");
    badge.textContent = count ? String(count) : "";
    badge.hidden = count === 0;
    button.title = count
      ? "已设置 " + count + " 个兴趣主题"
      : "设置 AI 本地笔记的兴趣主题";
  }

  function showInterestPanel(anchor) {
    const panel = ensureInterestPanel();
    renderInterestPanel();
    panel.hidden = false;
    panel.style.left = "0px";
    panel.style.top = "0px";

    const anchorRect = anchor?.getBoundingClientRect();
    const panelRect = panel.getBoundingClientRect();
    const left = Math.min(
      Math.max(12, Number(anchorRect?.right || 76) + 12),
      Math.max(12, window.innerWidth - panelRect.width - 12)
    );
    const top = Math.min(
      Math.max(12, Number(anchorRect?.top || 80) - 8),
      Math.max(12, window.innerHeight - panelRect.height - 12)
    );

    panel.style.left = left + "px";
    panel.style.top = top + "px";
    panel.querySelector(".yanzi-xhs-interest-input")?.focus();
  }

  function ensureInterestNavButton() {
    let button = document.querySelector(".yanzi-xhs-interest-nav");
    if (button?.isConnected) {
      updateInterestNavButton();
      return button;
    }

    const messageItem = findMessageNavItem();
    if (!messageItem?.parentElement) return null;

    const messageRow = messageItem.closest("li") || messageItem;
    const list = messageRow.parentElement;
    if (!list) return null;

    const row = document.createElement(
      messageRow.tagName?.toLowerCase() === "li" ? "li" : "div"
    );
    row.className = "yanzi-xhs-interest-nav-item";

    button = document.createElement("div");
    button.className = "yanzi-xhs-interest-nav";
    button.setAttribute("role", "button");
    button.setAttribute("tabindex", "0");
    button.innerHTML = `
      <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
        <path d="M12 3.5c1.1 2.7 2.9 4.5 5.5 5.5-2.6 1-4.4 2.8-5.5 5.5-1.1-2.7-2.9-4.5-5.5-5.5 2.6-1 4.4-2.8 5.5-5.5Z" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/>
        <path d="M18.5 14.5c.5 1.2 1.3 2 2.5 2.5-1.2.5-2 1.3-2.5 2.5-.5-1.2-1.3-2-2.5-2.5 1.2-.5 2-1.3 2.5-2.5Z" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>
      </svg>
      <span class="yanzi-xhs-interest-nav-label">兴趣</span>
      <span class="yanzi-xhs-interest-nav-count" hidden></span>
    `;

    button.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      const panel = ensureInterestPanel();
      if (!panel.hidden) panel.hidden = true;
      else showInterestPanel(button);
    });
    button.addEventListener("keydown", event => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      button.click();
    });

    row.appendChild(button);
    messageRow.insertAdjacentElement("afterend", row);
    updateInterestNavButton();
    return button;
  }

  function ensureEvaluationPanel() {
    let panel = document.querySelector(".yanzi-xhs-evaluation-panel");
    if (panel) return panel;

    panel = document.createElement("div");
    panel.className = "yanzi-xhs-evaluation-panel";
    panel.hidden = true;
    panel.innerHTML = `
      <div class="yanzi-xhs-evaluation-head">
        <strong>评价这类内容</strong>
        <button type="button" class="yanzi-xhs-evaluation-close" aria-label="关闭">×</button>
      </div>
      <p class="yanzi-xhs-evaluation-help">写具体一点，例如“开发内容太多，多一点社会观察和摄影”。这条评价只用于下一轮生成。</p>
      <textarea class="yanzi-xhs-evaluation-text" maxlength="500" placeholder="告诉 AI 下一批应该怎么调整……"></textarea>
      <div class="yanzi-xhs-evaluation-foot">
        <span>最多 500 字 · 下一轮使用一次</span>
        <button type="button" class="yanzi-xhs-evaluation-save">保存评价</button>
      </div>
    `;

    panel.querySelector(".yanzi-xhs-evaluation-close").addEventListener("click", () => {
      panel.hidden = true;
      evaluationPanelContext = null;
    });

    panel.querySelector(".yanzi-xhs-evaluation-save").addEventListener("click", async () => {
      const context = evaluationPanelContext;
      const card = context?.card || context;
      const selectionText = normalizeText(context?.selectionText || "");
      const input = panel.querySelector(".yanzi-xhs-evaluation-text");
      const value = normalizeText(input.value);
      if (!card || !value) {
        input.focus();
        return;
      }

      const saved = await setLocalFeedEvaluation(card, value, {
        selectionText
      });
      if (!saved) return;
      panel.hidden = true;
      evaluationPanelContext = null;
    });

    panel.querySelector(".yanzi-xhs-evaluation-text").addEventListener("keydown", event => {
      if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
        event.preventDefault();
        panel.querySelector(".yanzi-xhs-evaluation-save").click();
      }
    });

    document.documentElement.appendChild(panel);
    return panel;
  }

  function showEvaluationPanel(card, anchor, selectionText = "") {
    const panel = ensureEvaluationPanel();
    const selected = trimText(selectionText, 240);
    evaluationPanelContext = { card, selectionText: selected };

    const existing = selected
      ? pendingSelectionEvaluation(card.id, selected)
      : pendingEvaluationForCard(card.id);
    const input = panel.querySelector(".yanzi-xhs-evaluation-text");
    const title = panel.querySelector(".yanzi-xhs-evaluation-head strong");
    const help = panel.querySelector(".yanzi-xhs-evaluation-help");

    if (title) title.textContent = selected ? "评论这段内容" : "评价这类内容";
    if (help) {
      help.textContent = selected
        ? "你的评论只针对刚才框选的文字，下一轮生成会把它当作局部反馈。"
        : "写具体一点，例如“开发内容太多，多一点社会观察和摄影”。这条评价只用于下一轮生成。";
    }

    input.value = existing?.text || "";

    panel.hidden = false;
    panel.style.left = "0px";
    panel.style.top = "0px";

    const anchorRect = anchor?.getBoundingClientRect();
    const panelRect = panel.getBoundingClientRect();
    const left = Math.min(
      Math.max(12, Number(anchorRect?.right || window.innerWidth / 2)),
      Math.max(12, window.innerWidth - panelRect.width - 12)
    );
    const top = Math.min(
      Math.max(12, Number(anchorRect?.bottom || 100) + 8),
      Math.max(12, window.innerHeight - panelRect.height - 12)
    );

    panel.style.left = left + "px";
    panel.style.top = top + "px";
    input.focus();
    input.select();
  }

  function getNativeCardIdentity(card) {
    const meta = extractCardMeta(card);
    if (meta?.noteId) return "id:" + meta.noteId;
    if (meta?.noteHref) return "url:" + meta.noteHref;
    if (meta?.title || meta?.author) {
      return "text:" + ruleId(
        "native-card",
        String(meta?.title || "") + "|" + String(meta?.author || "")
      );
    }
    return "";
  }

  function clearCustomCardHost(host) {
    if (!(host instanceof HTMLElement)) return;
    const overlay = host.querySelector(":scope > .yanzi-xhs-custom-card");
    if (overlay?.dataset.localFeed === "1") {
      releaseLocalFeedAssignment(overlay.dataset.cardId || "");
    }
    overlay?.remove();
    host.classList.remove("yanzi-xhs-custom-host");
    delete host.dataset.yanziCustomCardId;
    delete host.dataset.yanziCustomCardUpdatedAt;
    delete host.dataset.yanziCustomNativeKey;
    delete host.dataset.yanziCustomPinned;
    if (host.dataset.yanziCustomPositionPatched === "1") {
      host.style.removeProperty("position");
      delete host.dataset.yanziCustomPositionPatched;
    }
  }

  function recordCustomCardInteraction(card, type) {
    if (!card?.id || card.localTest === true) return;

    noteStats.customCards = Array.isArray(noteStats.customCards)
      ? noteStats.customCards
      : [];

    let item = noteStats.customCards.find(entry => entry.id === card.id);
    if (!item) {
      item = {
        id: card.id,
        title: trimText(card.title, 160),
        author: trimText(card.author || "燕子", 80),
        impressions: 0,
        clicks: 0,
        firstSeenAt: Date.now(),
        lastSeenAt: 0,
        lastClickedAt: 0
      };
      noteStats.customCards.push(item);
    }

    item.title = trimText(card.title, 160) || item.title;
    item.author = trimText(card.author || "燕子", 80) || item.author;

    const now = Date.now();
    if (type === "impression") {
      item.impressions = Number(item.impressions || 0) + 1;
      item.lastSeenAt = now;
    } else if (type === "click") {
      item.clicks = Number(item.clicks || 0) + 1;
      item.lastClickedAt = now;
    }

    appendNoteEvent({
      type: "custom_card_" + type,
      cardId: card.id,
      title: trimText(card.title, 160),
      at: now
    });
    markNoteStatsDirty();
  }

  function renderCustomCard(host, card) {
    if (!(host instanceof HTMLElement) || !card?.id) return;

    const nativeKey = getNativeCardIdentity(host);
    const sameCard =
      host.dataset.yanziCustomCardId === card.id &&
      Number(host.dataset.yanziCustomCardUpdatedAt || 0) === Number(card.updatedAt || 0) &&
      host.querySelector(":scope > .yanzi-xhs-custom-card");

    if (sameCard) return;

    const existingOverlay = host.querySelector(":scope > .yanzi-xhs-custom-card");
    const wasCustomHost = host.classList.contains("yanzi-xhs-custom-host");

    if (getComputedStyle(host).position === "static") {
      host.style.setProperty("position", "relative", "important");
      host.dataset.yanziCustomPositionPatched = "1";
    }

    host.querySelectorAll(
      ":scope > .yanzi-xhs-hover-block, :scope > .yanzi-xhs-hover-manage"
    ).forEach(action => action.remove());
    host.classList.remove("yanzi-xhs-filter-card");
    host.classList.remove("yanzi-xhs-filter-hidden");
    delete host.dataset.yanziHover;
    delete host.dataset.yanziFiltered;

    const overlay = document.createElement("div");
    overlay.className = "yanzi-xhs-custom-card";
    overlay.dataset.cardId = card.id;
    overlay.dataset.localFeed = card.localFeed === true ? "1" : "0";
    overlay.dataset.clickable = card.url ? "1" : "0";
    overlay.dataset.textOnly = card.imageUrl ? "0" : "1";
    overlay.setAttribute("role", "article");
    overlay.setAttribute("aria-label", "燕子自定义卡片：" + card.title);

    let cover = null;

    const badge = document.createElement("div");
    badge.className = "yanzi-xhs-custom-badge";
    badge.textContent = card.badge || "燕子";

    if (card.imageUrl) {
      cover = document.createElement("div");
      cover.className = "yanzi-xhs-custom-cover";
      cover.appendChild(badge);

      const image = document.createElement("img");
      image.src = card.imageUrl;
      image.alt = "";
      image.loading = "lazy";
      image.referrerPolicy = "no-referrer";
      cover.appendChild(image);
    } else {
      overlay.appendChild(badge);
    }

    const info = document.createElement("div");
    info.className = "yanzi-xhs-custom-info";

    const title = document.createElement("div");
    title.className = "yanzi-xhs-custom-heading";
    title.textContent = card.title;
    info.appendChild(title);

    if (card.body) {
      const body = document.createElement("div");
      body.className = "yanzi-xhs-custom-body";
      body.textContent = card.body;
      info.appendChild(body);
    }

    const author = document.createElement("div");
    author.className = "yanzi-xhs-custom-author";
    author.textContent = card.author || "燕子";
    info.appendChild(author);

    if (card.localFeed === true) {
      const feedback = document.createElement("div");
      feedback.className = "yanzi-xhs-local-feedback";

      const current = localFeedFeedbackFor(card.id);
      const like = document.createElement("button");
      like.type = "button";
      like.dataset.feedback = "like";
      like.dataset.active = current === "like" ? "1" : "0";
      like.textContent = "♡ 感兴趣";

      const dislike = document.createElement("button");
      dislike.type = "button";
      dislike.dataset.feedback = "dislike";
      dislike.dataset.active = current === "dislike" ? "1" : "0";
      dislike.textContent = "× 我不感兴趣";

      const evaluate = document.createElement("button");
      evaluate.type = "button";
      evaluate.dataset.action = "evaluate";
      evaluate.dataset.active = pendingEvaluationForCard(card.id) ? "1" : "0";
      evaluate.textContent = "评价";
      evaluate.title = pendingEvaluationForCard(card.id)
        ? "已评价；将在下一轮生成时使用"
        : "写下对这类内容的具体评价";

      feedback.appendChild(like);
      feedback.appendChild(dislike);
      feedback.appendChild(evaluate);
      info.appendChild(feedback);

      feedback.addEventListener("click", event => {
        const button = event.target.closest("button");
        if (!button) return;

        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();

        if (button.dataset.action === "evaluate") {
          showEvaluationPanel(card, button);
          return;
        }

        if (button.dataset.feedback) {
          void setLocalFeedFeedback(card, button.dataset.feedback);
        }
      }, true);
    }

    if (cover) overlay.appendChild(cover);
    overlay.appendChild(info);

    overlay.addEventListener("click", event => {
      if (event.target.closest?.(".yanzi-xhs-local-feedback button")) return;

      const selection = window.getSelection();
      if (
        selection &&
        !selection.isCollapsed &&
        selection.rangeCount > 0 &&
        normalizeText(selection.toString())
      ) {
        const range = selection.getRangeAt(0);
        if (
          overlay.contains(range.startContainer) &&
          overlay.contains(range.endContainer)
        ) {
          event.preventDefault();
          event.stopPropagation();
          event.stopImmediatePropagation();
          return;
        }
      }

      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();

      recordCustomCardInteraction(card, "click");

      window.postMessage({
        source: "yanzi-browser-helper",
        type: "xiaohongshu-custom-card-click",
        cardId: card.id,
        url: card.url || ""
      }, location.origin);

      if (card.url) {
        window.open(card.url, "_blank", "noopener,noreferrer");
      }
    }, true);

    // Build first, switch visibility last. This avoids a paint where the
    // native note has already been hidden but the Yanzi overlay is not ready yet.
    if (existingOverlay) {
      existingOverlay.replaceWith(overlay);
    } else {
      host.appendChild(overlay);
    }

    host.dataset.yanziCustomCardId = card.id;
    host.dataset.yanziCustomCardUpdatedAt = String(Number(card.updatedAt || 0));
    if (nativeKey) host.dataset.yanziCustomNativeKey = nativeKey;
    if (!wasCustomHost) {
      host.classList.add("yanzi-xhs-custom-host");
    }

    if (!customCardImpressionsThisPage.has(card.id)) {
      customCardImpressionsThisPage.add(card.id);
      recordCustomCardInteraction(card, "impression");
    }
  }

  function applyCustomCards(cards) {
    const syncedCards = customCards.filter(card => card.enabled !== false);
    const allLocalNotes = LOCAL_TEST_NOTES_ENABLED && localFeedLoaded
      ? localFeedState.notes.filter(note => note.enabled !== false)
      : [];
    const readyLocalNotes = getReadyLocalFeedNotes();

    const allowedIds = new Set([
      ...allLocalNotes.map(card => card.id),
      ...syncedCards.map(card => card.id)
    ]);

    const mutationGuard =
      Math.max(360, Math.round(window.innerHeight * 0.6));

    const allExistingHosts = Array.from(document.querySelectorAll(".yanzi-xhs-custom-host"))
      .filter(host => host instanceof HTMLElement && host.isConnected);

    if (!localFeedLoaded && !syncedCards.length) return;

    if (!allowedIds.size) {
      allExistingHosts.forEach(clearCustomCardHost);
      return;
    }

    // Xiaohongshu virtualizes the feed and may reuse an old DOM element for a new
    // note. Detect that only outside the protected viewport zone, restore the
    // real note there, then allow a new safe slot to be prepared further below.
    for (const host of allExistingHosts) {
      const rect = host.getBoundingClientRect();
      const farOutsideViewport =
        rect.bottom < -mutationGuard ||
        rect.top > window.innerHeight + mutationGuard;
      const currentNativeKey = getNativeCardIdentity(host);
      const assignedNativeKey = host.dataset.yanziCustomNativeKey || "";

      if (!assignedNativeKey && currentNativeKey) {
        host.dataset.yanziCustomNativeKey = currentNativeKey;
      }

      const nativeRecycled =
        assignedNativeKey &&
        currentNativeKey &&
        assignedNativeKey !== currentNativeKey;
      const cardRemoved = !allowedIds.has(host.dataset.yanziCustomCardId || "");

      if (farOutsideViewport && (nativeRecycled || cardRemoved)) {
        clearCustomCardHost(host);
      }
    }

    const existingHosts = Array.from(document.querySelectorAll(".yanzi-xhs-custom-host"))
      .filter(host => host instanceof HTMLElement && host.isConnected);

    if (!customCardSequenceInitialized) {
      customCardSequenceIndex = existingHosts.length;
      customCardSequenceInitialized = true;
    }

    const eligible = cards
      .filter(card =>
        card instanceof HTMLElement &&
        card.isConnected &&
        !card.classList.contains("yanzi-xhs-filter-hidden") &&
        card.getBoundingClientRect().height >= 180
      )
      .map(card => ({ card, rect: card.getBoundingClientRect() }))
      .sort((a, b) =>
        Math.abs(a.rect.top - b.rect.top) > 1
          ? a.rect.top - b.rect.top
          : a.rect.left - b.rect.left
      );

    const slotSize = LOCAL_TEST_REAL_NOTES_PER_CARD + 1;
    if (eligible.length < slotSize) {
      markVisibleLocalFeedCards();
      void maybeReplenishLocalFeed();
      return;
    }

    const targetCount = Math.floor(eligible.length / slotSize);
    const missingCount = Math.max(0, targetCount - existingHosts.length);

    if (!missingCount) {
      markVisibleLocalFeedCards();
      void maybeReplenishLocalFeed();
      return;
    }

    // Only prepare new cards well below the viewport. By the time the user reaches
    // them, the replacement has already been stable for at least half a screen.
    const safeTop = window.innerHeight + mutationGuard;
    const candidates = eligible.filter(item =>
      !item.card.classList.contains("yanzi-xhs-custom-host") &&
      item.rect.top >= safeTop
    );

    if (!candidates.length) {
      markVisibleLocalFeedCards();
      void maybeReplenishLocalFeed();
      return;
    }

    let added = 0;
    let localIndex = 0;
    let candidateIndex = Math.min(slotSize - 1, candidates.length - 1);

    while (added < missingCount && candidateIndex < candidates.length) {
      const host = candidates[candidateIndex].card;
      let card = readyLocalNotes[localIndex] || null;

      if (card) {
        localIndex += 1;
      } else if (syncedCards.length) {
        card = syncedCards[customCardSequenceIndex % syncedCards.length];
        customCardSequenceIndex += 1;
      } else {
        break;
      }

      renderCustomCard(host, card);
      host.dataset.yanziCustomPinned = "1";

      if (card.localFeed === true) {
        assignLocalFeedNote(card);
      }

      added += 1;
      candidateIndex += slotSize;
    }

    markVisibleLocalFeedCards();
    void maybeReplenishLocalFeed();
  }

  function normalizeNoteStatsValue(value) {
    return {
      version: 1,
      updatedAt: Number(value?.updatedAt || 0),
      items: Array.isArray(value?.items) ? value.items : [],
      customCards: Array.isArray(value?.customCards) ? value.customCards : [],
      events: Array.isArray(value?.events) ? value.events : []
    };
  }

  function compactNoteStats() {
    noteStats.items = (Array.isArray(noteStats.items) ? noteStats.items : [])
      .sort((a, b) =>
        Number(b.lastOpenedAt || b.updatedAt || 0) -
        Number(a.lastOpenedAt || a.updatedAt || 0)
      )
      .slice(0, NOTE_STATS_MAX_ITEMS);

    noteStats.customCards = (Array.isArray(noteStats.customCards) ? noteStats.customCards : [])
      .sort((a, b) =>
        Number(b.lastClickedAt || b.lastSeenAt || 0) -
        Number(a.lastClickedAt || a.lastSeenAt || 0)
      )
      .slice(0, 200);

    noteStats.events = (Array.isArray(noteStats.events) ? noteStats.events : [])
      .slice(-NOTE_STATS_MAX_EVENTS);
  }

  async function saveNoteStatsLocal() {
    compactNoteStats();
    noteStats.updatedAt = Date.now();
    await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_set",
      appId: APP_ID,
      key: NOTE_STATS_KEY,
      value: noteStats,
      syncNow: false
    });
  }

  async function syncNoteStatsNow() {
    clearTimeout(noteStatsSaveTimer);
    noteStatsSaveTimer = null;
    try {
      await saveNoteStatsLocal();
      await chrome.runtime.sendMessage({
        type: "yanzi_webapp_storage_sync_key",
        appId: APP_ID,
        key: NOTE_STATS_KEY
      });
    } catch {
      // Local dirty data will be reconciled when the browser reconnects.
    }
  }

  function markNoteStatsDirty(syncSoon = false) {
    clearTimeout(noteStatsSaveTimer);
    noteStatsSaveTimer = setTimeout(() => {
      noteStatsSaveTimer = null;
      void saveNoteStatsLocal();
    }, NOTE_STATS_LOCAL_SAVE_DELAY_MS);

    clearTimeout(noteStatsSyncTimer);
    noteStatsSyncTimer = setTimeout(() => {
      noteStatsSyncTimer = null;
      void syncNoteStatsNow();
    }, syncSoon ? 3000 : NOTE_STATS_CLOUD_SYNC_DELAY_MS);
  }

  function appendNoteEvent(event) {
    noteStats.events = Array.isArray(noteStats.events) ? noteStats.events : [];
    noteStats.events.push(event);
    if (noteStats.events.length > NOTE_STATS_MAX_EVENTS + 100) {
      noteStats.events = noteStats.events.slice(-NOTE_STATS_MAX_EVENTS);
    }
  }

  function noteIdentity(meta) {
    if (meta?.noteId) return "id:" + meta.noteId;
    if (meta?.noteHref) return "url:" + meta.noteHref;
    return ruleId("note-stat", String(meta?.title || "") + "|" + String(meta?.author || ""));
  }

  function upsertNoteStat(meta, incrementOpen = false) {
    if (!meta) return null;
    noteStats.items = Array.isArray(noteStats.items) ? noteStats.items : [];

    const id = noteIdentity(meta);
    let item = noteStats.items.find(entry => entry.id === id);
    if (!item) {
      item = {
        id,
        noteId: "",
        url: "",
        title: "",
        author: "",
        authorId: "",
        description: "",
        detailText: "",
        tags: [],
        mediaType: "unknown",
        likeText: "",
        likeCount: null,
        collectText: "",
        collectCount: null,
        commentText: "",
        commentCount: null,
        firstOpenedAt: 0,
        lastOpenedAt: 0,
        lastClosedAt: 0,
        openCount: 0,
        totalDwellMs: 0,
        maxDwellMs: 0,
        lastDwellMs: 0,
        sourceUrl: "",
        openedFrom: "",
        updatedAt: 0
      };
      noteStats.items.push(item);
    }

    const assignText = (key, value, maxLength) => {
      const next = trimText(value, maxLength);
      if (next) item[key] = next;
    };

    assignText("noteId", meta.noteId, 160);
    assignText("url", meta.noteHref || meta.url, 1200);
    const cleanTitle = cleanNoteTitle(meta.title);
    if (cleanTitle) item.title = cleanTitle;
    assignText("author", meta.author, 120);
    assignText("authorId", meta.authorId, 200);
    assignText("description", meta.description, 1600);
    assignText("detailText", meta.detailText, 2400);
    assignText("mediaType", meta.mediaType, 32);
    assignText("likeText", meta.likeText, 40);
    assignText("collectText", meta.collectText, 40);
    assignText("commentText", meta.commentText, 40);

    const openedFrom = trimText(meta.openedFrom, 40);
    const sourceUrl = trimText(meta.sourceUrl, 1200);
    if (openedFrom === "feed_click") {
      item.openedFrom = "feed_click";
      if (sourceUrl) item.sourceUrl = sourceUrl;
    } else {
      if (!item.openedFrom && openedFrom) item.openedFrom = openedFrom;
      if (!item.sourceUrl && sourceUrl) item.sourceUrl = sourceUrl;
    }

    if (meta.likeCount !== null && meta.likeCount !== undefined) item.likeCount = meta.likeCount;
    if (meta.collectCount !== null && meta.collectCount !== undefined) item.collectCount = meta.collectCount;
    if (meta.commentCount !== null && meta.commentCount !== undefined) item.commentCount = meta.commentCount;

    item.tags = uniqueStrings([...(item.tags || []), ...(meta.tags || [])], 40);
    item.updatedAt = Date.now();

    if (incrementOpen) {
      const now = Date.now();
      item.openCount = Number(item.openCount || 0) + 1;
      item.firstOpenedAt = Number(item.firstOpenedAt || 0) || now;
      item.lastOpenedAt = now;
    }

    return item;
  }

  function getNoteIdFromLocation() {
    try {
      const path = new URL(location.href).pathname;
      const match = path.match(/\/(?:explore|discovery\/item)\/([^/?#]+)/i);
      return match?.[1] || "";
    } catch {
      return "";
    }
  }

  function isVisibleElement(element) {
    if (!(element instanceof Element)) return false;
    const rect = element.getBoundingClientRect();
    if (rect.width < 20 || rect.height < 20) return false;
    const style = getComputedStyle(element);
    return style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity || 1) > 0;
  }

  function findDetailRoot() {
    const selectors = [
      ".note-detail-mask",
      "[class*='note-detail-mask']",
      ".note-detail-container",
      "[class*='note-detail-container']",
      "[class*='note-detail']"
    ];

    let best = null;
    let bestArea = 0;
    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (!isVisibleElement(element)) continue;
        const rect = element.getBoundingClientRect();
        const area = rect.width * rect.height;
        if (area > bestArea && rect.width >= 420 && rect.height >= 260) {
          best = element;
          bestArea = area;
        }
      }
    }
    return best;
  }

  function readMetricFromRoot(root, selectors) {
    if (!root?.querySelector) return { text: "", count: null };
    const text = firstText(root, selectors);
    return { text, count: parseMetricNumber(text) };
  }

  function extractOpenedNoteMeta(noteId, detailRoot) {
    const pending = pendingOpenedNote;
    const pendingMeta = pending?.meta || {};
    const pendingMatches =
      pending &&
      Date.now() - Number(pending.clickedAt || 0) < 10000 &&
      (!noteId || !pendingMeta.noteId || pendingMeta.noteId === noteId);

    const base = pendingMatches ? pendingMeta : {};
    const root = detailRoot || findDetailRoot();
    const pageTitle = cleanNoteTitle(document.title || "");

    const ogTitle = cleanNoteTitle(
      document.querySelector("meta[property='og:title']")?.content
    );
    const ogDescription = trimText(
      document.querySelector("meta[property='og:description']")?.content ||
      document.querySelector("meta[name='description']")?.content,
      1600
    );

    const authorLink = root?.querySelector?.("a[href*='/user/profile/']");
    const authorHref = authorLink?.href || authorLink?.getAttribute?.("href") || "";
    const author = trimText(
      base.author || authorLink?.innerText || authorLink?.textContent,
      120
    );

    const description = trimText(
      base.description ||
      ogDescription ||
      (root ? firstText(root, [
        ".note-text",
        ".desc",
        "[class*='desc']",
        "[class*='content']"
      ]) : ""),
      1600
    );

    const detailText = root && root !== document
      ? trimText(root.textContent, 2400)
      : trimText(base.text || description, 2400);

    const like = readMetricFromRoot(root, [
      ".like-wrapper .count",
      "[class*='like'] [class*='count']"
    ]);
    const collect = readMetricFromRoot(root, [
      ".collect-wrapper .count",
      "[class*='collect'] [class*='count']"
    ]);
    const comment = readMetricFromRoot(root, [
      ".chat-wrapper .count",
      ".comment-wrapper .count",
      "[class*='comment'] [class*='count']"
    ]);

    return {
      noteId: noteId || base.noteId || "",
      noteHref: base.noteHref || location.href,
      title: base.title || ogTitle || pageTitle,
      author,
      authorId: base.authorId || extractIdFromHref(authorHref, "/user/profile/"),
      description,
      detailText,
      tags: uniqueStrings([
        ...(base.tags || []),
        ...extractTags(description)
      ], 40),
      mediaType:
        base.mediaType && base.mediaType !== "unknown"
          ? base.mediaType
          : root?.querySelector?.("video")
            ? "video"
            : root?.querySelector?.("img")
              ? "image"
              : "unknown",
      likeText: like.text || base.likeText || "",
      likeCount: like.count ?? base.likeCount ?? null,
      collectText: collect.text || "",
      collectCount: collect.count,
      commentText: comment.text || "",
      commentCount: comment.count,
      sourceUrl: pendingMatches ? pending.sourceUrl : "",
      openedFrom: pendingMatches ? "feed_click" : "direct"
    };
  }

  function pauseActiveNoteVisibility() {
    if (!activeNoteSession?.visibleSince) return;
    activeNoteSession.visibleAccumulatedMs += Math.max(
      0,
      Date.now() - activeNoteSession.visibleSince
    );
    activeNoteSession.visibleSince = 0;
  }

  function resumeActiveNoteVisibility() {
    if (!activeNoteSession || activeNoteSession.visibleSince || document.hidden) return;
    activeNoteSession.visibleSince = Date.now();
  }

  function finalizeActiveNote(reason) {
    if (!activeNoteSession) return;
    pauseActiveNoteVisibility();

    const session = activeNoteSession;
    const item = noteStats.items.find(entry => entry.id === session.id);
    const dwellMs = Math.max(0, Number(session.visibleAccumulatedMs || 0));

    if (item) {
      item.lastDwellMs = dwellMs;
      item.totalDwellMs = Number(item.totalDwellMs || 0) + dwellMs;
      item.maxDwellMs = Math.max(Number(item.maxDwellMs || 0), dwellMs);
      item.lastClosedAt = Date.now();
      item.updatedAt = Date.now();
    }

    appendNoteEvent({
      type: "close",
      noteId: session.noteId || "",
      id: session.id,
      at: Date.now(),
      dwellMs,
      reason: reason || "closed"
    });

    activeNoteSession = null;
    markNoteStatsDirty(true);
  }

  function beginNoteSession(meta) {
    const id = noteIdentity(meta);
    if (activeNoteSession?.id === id) {
      upsertNoteStat(meta, false);
      return;
    }

    if (activeNoteSession) finalizeActiveNote("switch_note");

    const item = upsertNoteStat(meta, true);
    if (!item) return;

    activeNoteSession = {
      id,
      noteId: meta.noteId || "",
      openedAt: Date.now(),
      visibleSince: document.hidden ? 0 : Date.now(),
      visibleAccumulatedMs: 0
    };

    appendNoteEvent({
      type: "open",
      noteId: meta.noteId || "",
      id,
      at: Date.now(),
      title: trimText(meta.title, 240),
      author: trimText(meta.author, 120),
      sourceUrl: trimText(meta.sourceUrl, 1200),
      openedFrom: meta.openedFrom || ""
    });
    markNoteStatsDirty();
  }

  function detectOpenedNote() {
    const urlNoteId = getNoteIdFromLocation();
    const detailRoot = findDetailRoot();

    if (urlNoteId) {
      return { noteId: urlNoteId, detailRoot };
    }

    if (
      detailRoot &&
      pendingOpenedNote &&
      Date.now() - Number(pendingOpenedNote.clickedAt || 0) < 10000
    ) {
      return {
        noteId: pendingOpenedNote.meta?.noteId || "",
        detailRoot
      };
    }

    return null;
  }

  function inspectOpenedNote() {
    analyticsInspectTimer = null;
    const detected = detectOpenedNote();

    if (!detected) {
      if (activeNoteSession) finalizeActiveNote("detail_closed");
      if (
        pendingOpenedNote &&
        Date.now() - Number(pendingOpenedNote.clickedAt || 0) > 10000
      ) {
        pendingOpenedNote = null;
      }
      return;
    }

    const meta = extractOpenedNoteMeta(detected.noteId, detected.detailRoot);
    beginNoteSession(meta);

    if (activeNoteSession) {
      upsertNoteStat(meta, false);
      markNoteStatsDirty();
    }

    if (
      pendingOpenedNote &&
      Date.now() - Number(pendingOpenedNote.clickedAt || 0) < 10000
    ) {
      pendingOpenedNote = null;
    }
  }

  function scheduleAnalyticsInspect(delay = 140) {
    clearTimeout(analyticsInspectTimer);
    analyticsInspectTimer = setTimeout(inspectOpenedNote, delay);
  }

  function applyRules(root = document) {
    if (!enabled) return;
    ensureInterestNavButton();
    const cards = collectCards(root);
    if (!cards.length) return;

    let changed = false;
    for (const card of cards) {
      if (
        card.classList.contains("yanzi-xhs-custom-host") ||
        card.querySelector(":scope > .yanzi-xhs-custom-card")
      ) {
        ensureHoverBlockButton(card, null);
        if (card.classList.contains("yanzi-xhs-filter-hidden")) {
          card.classList.remove("yanzi-xhs-filter-hidden");
          delete card.dataset.yanziFiltered;
          changed = true;
        }
        continue;
      }

      const meta = extractCardMeta(card);
      ensureHoverBlockButton(card, meta);
      const blocked = filteringEnabled && rules.some(rule => matchesRule(meta, rule));
      if (blocked && !card.classList.contains("yanzi-xhs-filter-hidden")) {
        rememberNativeGeometry(card);
        card.classList.add("yanzi-xhs-filter-hidden");
        card.dataset.yanziFiltered = "1";
        changed = true;
      } else if (!blocked && card.classList.contains("yanzi-xhs-filter-hidden")) {
        card.classList.remove("yanzi-xhs-filter-hidden");
        delete card.dataset.yanziFiltered;
        changed = true;
      }
    }

    if (changed || cards.some(card => card.classList.contains("yanzi-xhs-filter-hidden"))) {
      requestWaterfallReflow(cards);
    } else {
      restoreNativeWaterfall(cards);
    }

    applyCustomCards(cards);
  }

  function scheduleFilter(root = document) {
    if (filterScheduled) return;
    filterScheduled = true;
    requestAnimationFrame(() => {
      filterScheduled = false;
      applyRules(root);
    });
  }

  async function loadRules() {
    const response = await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_get",
      appId: APP_ID,
      key: RULES_KEY,
      fallback: { version: 1, items: [] }
    });
    const value = response?.value;
    rules = Array.isArray(value?.items) ? value.items : [];
    scheduleFilter(document);
    renderRulePanel();
  }

  async function saveRules() {
    const value = { version: 1, items: rules };
    await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_set",
      appId: APP_ID,
      key: RULES_KEY,
      value
    });
  }

  async function loadSettings() {
    const response = await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_get",
      appId: APP_ID,
      key: SETTINGS_KEY,
      fallback: { filteringEnabled: true }
    });
    filteringEnabled = response?.value?.filteringEnabled !== false;
    scheduleFilter(document);
    renderRulePanel();
  }

  async function loadNoteStats() {
    const response = await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_get",
      appId: APP_ID,
      key: NOTE_STATS_KEY,
      fallback: {
        version: 1,
        updatedAt: 0,
        items: [],
        customCards: [],
        events: []
      }
    });
    noteStats = normalizeNoteStatsValue(response?.value);
    scheduleAnalyticsInspect(0);
  }

  async function loadCustomCards() {
    const response = await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_get",
      appId: APP_ID,
      key: CUSTOM_CARDS_KEY,
      fallback: { version: 1, items: [] }
    });
    customCards = normalizeCustomCardsValue(response?.value);
    scheduleFilter(document);
  }

  async function saveSettings() {
    await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_set",
      appId: APP_ID,
      key: SETTINGS_KEY,
      value: { filteringEnabled }
    });
  }

  async function setFilteringEnabled(value) {
    const next = Boolean(value);
    if (filteringEnabled === next) return;
    filteringEnabled = next;
    await saveSettings();
    scheduleFilter(document);
    renderRulePanel();
    showToast(filteringEnabled ? "屏蔽已开启" : "屏蔽已暂停");
  }

  async function removeRule(ruleId) {
    const before = rules.length;
    rules = rules.filter(rule => rule.id !== ruleId);
    if (rules.length === before) return false;
    await saveRules();
    scheduleFilter(document);
    renderRulePanel();
    return true;
  }

  async function addRule(rule) {
    const value = normalizeText(rule.value);
    if (!value) return false;

    const id = ruleId(rule.type, rule.authorId || rule.noteId || value);
    if (rules.some(existing => existing.id === id)) {
      showToast("已经在屏蔽规则中");
      return false;
    }

    rules.push({
      id,
      type: rule.type,
      value,
      label: normalizeText(rule.label || value),
      authorId: rule.authorId || "",
      noteId: rule.noteId || "",
      scope: rule.scope || "",
      createdAt: Date.now(),
      enabled: true
    });
    await saveRules();
    scheduleFilter(document);
    renderRulePanel();
    return true;
  }

  async function handleCommand(message) {
    if (message.action === "disable") {
      enabled = false;
      observer?.disconnect();
      observer = null;
      const allCards = collectCards(document);
      document.querySelectorAll(".yanzi-xhs-filter-hidden").forEach(card => {
        card.classList.remove("yanzi-xhs-filter-hidden");
        delete card.dataset.yanziFiltered;
      });
      restoreNativeWaterfall(allCards);
      return;
    }

    if (message.action === "block-selection") {
      const selected = normalizeText(message.selectionText || window.getSelection()?.toString());
      if (!selected) {
        showToast("请先选中标题、作者或关键词");
        return;
      }
      if (await addRule({ type: "keyword", value: selected, label: selected })) {
        showToast(`已屏蔽关键词：${selected}`);
      }
      return;
    }

    const card = findCard(lastContextTarget);
    const meta = extractCardMeta(card);
    if (!card || !meta) {
      showToast("没有识别到当前笔记");
      return;
    }

    if (message.action === "block-author") {
      if (!meta.author) {
        showToast("没有识别到作者");
        return;
      }
      if (await addRule({
        type: "author",
        value: meta.authorId || meta.author,
        label: meta.author,
        authorId: meta.authorId
      })) {
        showToast(`已屏蔽作者：${meta.author}`);
      }
      return;
    }

    if (message.action === "block-note") {
      const value = meta.noteId || meta.noteHref || meta.title;
      if (!value) {
        showToast("没有识别到笔记");
        return;
      }
      if (await addRule({
        type: "note",
        value,
        label: meta.title || "当前笔记",
        noteId: meta.noteId
      })) {
        showToast("已屏蔽这篇笔记");
      }
    }
  }

  function ensureFilterMenu() {
    let menu = document.querySelector(".yanzi-xhs-filter-menu");
    if (menu) return menu;

    menu = document.createElement("div");
    menu.className = "yanzi-xhs-filter-menu";
    menu.hidden = true;
    menu.innerHTML = `
      <div class="yanzi-xhs-menu-item" data-action="toggle">
        <span>屏蔽功能</span>
        <span class="yanzi-xhs-menu-state"></span>
      </div>
      <div class="yanzi-xhs-menu-item" data-action="list">
        <span>屏蔽列表</span>
        <span class="yanzi-xhs-menu-state yanzi-xhs-menu-count"></span>
      </div>
    `;

    menu.addEventListener("click", async event => {
      const item = event.target.closest(".yanzi-xhs-menu-item");
      if (!item) return;
      if (item.dataset.action === "toggle") {
        await setFilteringEnabled(!filteringEnabled);
        hideFilterMenu();
      } else if (item.dataset.action === "list") {
        hideFilterMenu();
        showRulePanel("all");
      }
    });

    document.documentElement.appendChild(menu);
    return menu;
  }

  function showFilterMenu(x, y) {
    const menu = ensureFilterMenu();
    menu.querySelector(".yanzi-xhs-menu-state").textContent =
      filteringEnabled ? "已开启" : "已暂停";
    menu.querySelector(".yanzi-xhs-menu-count").textContent = `${rules.length} 条`;

    menu.hidden = false;
    menu.style.left = "0px";
    menu.style.top = "0px";

    const rect = menu.getBoundingClientRect();
    const left = Math.min(Math.max(8, x), window.innerWidth - rect.width - 8);
    const top = Math.min(Math.max(8, y), window.innerHeight - rect.height - 8);
    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;
  }

  function hideFilterMenu() {
    const menu = document.querySelector(".yanzi-xhs-filter-menu");
    if (menu) menu.hidden = true;
  }

  function ensureRulePanel() {
    let panel = document.querySelector(".yanzi-xhs-filter-panel");
    if (panel) return panel;

    panel = document.createElement("div");
    panel.className = "yanzi-xhs-filter-panel";
    panel.hidden = true;
    panel.innerHTML = `
      <div class="yanzi-xhs-panel-head">
        <strong class="yanzi-xhs-panel-title">屏蔽列表</strong>
        <div class="yanzi-xhs-panel-close" title="关闭">×</div>
      </div>
      <div class="yanzi-xhs-panel-toolbar">
        <span class="yanzi-xhs-panel-summary"></span>
        <div class="yanzi-xhs-panel-toggle"></div>
      </div>
      <div class="yanzi-xhs-rule-list"></div>
    `;

    panel.querySelector(".yanzi-xhs-panel-close").addEventListener("click", () => {
      panel.hidden = true;
    });

    panel.querySelector(".yanzi-xhs-panel-toggle").addEventListener("click", async () => {
      await setFilteringEnabled(!filteringEnabled);
    });

    panel.querySelector(".yanzi-xhs-rule-list").addEventListener("click", async event => {
      const remove = event.target.closest("[data-remove-rule]");
      if (!remove) return;
      if (await removeRule(remove.dataset.removeRule)) {
        showToast("已移出屏蔽列表");
      }
    });

    document.documentElement.appendChild(panel);
    return panel;
  }

  function renderRulePanel() {
    const panel = document.querySelector(".yanzi-xhs-filter-panel");
    if (!panel) return;

    const title = panel.querySelector(".yanzi-xhs-panel-title");
    const summary = panel.querySelector(".yanzi-xhs-panel-summary");
    const toggle = panel.querySelector(".yanzi-xhs-panel-toggle");
    const list = panel.querySelector(".yanzi-xhs-rule-list");

    const visibleRules = rulePanelMode === "keyword"
      ? rules.filter(rule => rule.type === "keyword")
      : rules;

    if (title) title.textContent = rulePanelMode === "keyword" ? "屏蔽关键词" : "屏蔽列表";
    summary.textContent = rulePanelMode === "keyword"
      ? `${visibleRules.length} 个关键词`
      : `${visibleRules.length} 条规则`;
    toggle.textContent = filteringEnabled ? "屏蔽已开启" : "屏蔽已暂停";

    if (!visibleRules.length) {
      list.innerHTML = rulePanelMode === "keyword"
        ? '<div class="yanzi-xhs-rule-empty">暂无屏蔽关键词</div>'
        : '<div class="yanzi-xhs-rule-empty">暂无屏蔽规则</div>';
      return;
    }

    const typeName = type =>
      type === "author" ? "作者" : type === "note" ? "笔记" : "关键词";

    list.innerHTML = visibleRules.map(rule => `
      <div class="yanzi-xhs-rule-row">
        <div class="yanzi-xhs-rule-main">
          <div class="yanzi-xhs-rule-label">${escapeHtml(rule.label || rule.value || "")}</div>
          <div class="yanzi-xhs-rule-type">${typeName(rule.type)}</div>
        </div>
        <div class="yanzi-xhs-rule-remove" data-remove-rule="${escapeHtml(rule.id || "")}">移出</div>
      </div>
    `).join("");
  }

  function showRulePanel(mode = "all") {
    rulePanelMode = mode === "keyword" ? "keyword" : "all";
    const panel = ensureRulePanel();
    renderRulePanel();
    panel.hidden = false;
  }

  function findRangeContainer(card, range, selectors) {
    if (!card || !range) return null;
    for (const selector of selectors) {
      const elements = card.querySelectorAll(selector);
      for (const element of elements) {
        if (element.contains(range.startContainer) &&
            element.contains(range.endContainer)) {
          return element;
        }
      }
    }
    return null;
  }

  function getSelectionRect(range) {
    const rect = range.getBoundingClientRect();
    if (rect.width > 0 || rect.height > 0) return rect;
    const rects = range.getClientRects();
    return rects.length ? rects[rects.length - 1] : null;
  }

  function readSelectionContext() {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null;

    const selectedText = normalizeText(selection.toString());
    if (!selectedText) return null;

    const range = selection.getRangeAt(0);
    const startElement = range.startContainer instanceof Element
      ? range.startContainer
      : range.startContainer.parentElement;
    if (!startElement) return null;

    const localOverlay = startElement.closest(
      '.yanzi-xhs-custom-card[data-local-feed="1"]'
    );
    if (
      localOverlay &&
      localOverlay.contains(range.startContainer) &&
      localOverlay.contains(range.endContainer)
    ) {
      const textContainer = findRangeContainer(localOverlay, range, [
        ".yanzi-xhs-custom-heading",
        ".yanzi-xhs-custom-body"
      ]);
      const note = localFeedNoteById(localOverlay.dataset.cardId || "");
      const rect = getSelectionRect(range);

      if (
        textContainer &&
        note &&
        rect &&
        selectedText.length <= 240
      ) {
        return {
          kind: "local-selection",
          text: selectedText,
          card: note,
          overlay: localOverlay,
          rect
        };
      }

      return null;
    }

    if (selectedText.length > 120) return null;

    const card = findCard(startElement);
    if (!card) return null;

    const titleElement = findRangeContainer(card, range, TITLE_SELECTORS);
    if (titleElement) {
      const rect = getSelectionRect(range);
      return rect ? {
        kind: "title",
        text: selectedText,
        card,
        rect
      } : null;
    }

    const authorElement = findRangeContainer(card, range, AUTHOR_SELECTORS);
    if (authorElement) {
      const rect = getSelectionRect(range);
      return rect ? {
        kind: "author",
        text: selectedText,
        card,
        rect
      } : null;
    }

    return null;
  }

  function ensureSelectionPopover() {
    let popover = document.querySelector(".yanzi-xhs-selection-popover");
    if (popover) return popover;

    popover = document.createElement("div");
    popover.className = "yanzi-xhs-selection-popover";
    popover.hidden = true;

    const keepSelection = event => {
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
    };

    popover.addEventListener("pointerdown", keepSelection, true);
    popover.addEventListener("mousedown", keepSelection, true);

    popover.addEventListener("click", async event => {
      const action = event.target.closest(".yanzi-xhs-selection-action")?.dataset.action;
      const context = selectionPopoverContext;
      if (!action || !context) return;

      event.preventDefault();
      event.stopPropagation();

      if (
        context.kind === "local-selection" &&
        (action === "selection-like" || action === "selection-dislike")
      ) {
        const value = action === "selection-like" ? "like" : "dislike";
        const saved = await setLocalFeedSelectionFeedback(
          context.card,
          context.text,
          value
        );
        if (saved) {
          showToast(
            value === "like"
              ? "已标记这段内容为感兴趣"
              : "已标记这段内容为不感兴趣"
          );
        }
        hideSelectionPopover();
        return;
      }

      if (
        context.kind === "local-selection" &&
        action === "selection-evaluate"
      ) {
        const popoverRect = popover.getBoundingClientRect();
        const anchor = {
          getBoundingClientRect: () => popoverRect
        };
        showEvaluationPanel(
          context.card,
          anchor,
          context.text
        );
        hideSelectionPopover();
        return;
      }

      if (action === "block-keyword") {
        const keyword = normalizeText(context.text);
        if (keyword && await addRule({
          type: "keyword",
          value: keyword,
          label: keyword,
          scope: "title"
        })) {
          showToast(`已屏蔽关键词：${keyword}`);
        }
        hideSelectionPopover();
        return;
      }

      if (action === "keyword-settings") {
        hideSelectionPopover();
        showRulePanel("keyword");
        return;
      }

      if (action === "block-author") {
        const meta = extractCardMeta(context.card);
        if (!meta?.author) {
          showToast("没有识别到作者");
          hideSelectionPopover();
          return;
        }
        if (await addRule({
          type: "author",
          value: meta.authorId || meta.author,
          label: meta.author,
          authorId: meta.authorId
        })) {
          showToast(`已屏蔽作者：${meta.author}`);
        }
        hideSelectionPopover();
      }
    }, true);

    document.documentElement.appendChild(popover);
    return popover;
  }

  function hideSelectionPopover() {
    selectionPopoverContext = null;
    const popover = document.querySelector(".yanzi-xhs-selection-popover");
    if (popover) popover.hidden = true;
  }

  function showSelectionPopover(context) {
    if (!context) {
      hideSelectionPopover();
      return;
    }

    selectionPopoverContext = context;
    const popover = ensureSelectionPopover();

    if (context.kind === "local-selection") {
      popover.innerHTML =
        '<div class="yanzi-xhs-selection-action" data-action="selection-like">感兴趣</div>' +
        '<div class="yanzi-xhs-selection-action" data-action="selection-dislike">不感兴趣</div>' +
        '<div class="yanzi-xhs-selection-action" data-action="selection-evaluate">评论</div>';
    } else {
      popover.innerHTML = context.kind === "title"
        ? '<div class="yanzi-xhs-selection-action" data-action="block-keyword">屏蔽关键词</div><div class="yanzi-xhs-selection-action" data-action="keyword-settings">设置</div>'
        : '<div class="yanzi-xhs-selection-action" data-action="block-author">屏蔽</div>';
    }

    popover.hidden = false;
    popover.style.left = "0px";
    popover.style.top = "0px";

    const bubbleRect = popover.getBoundingClientRect();
    const selectionRect = context.rect;
    let left = selectionRect.left + selectionRect.width / 2 - bubbleRect.width / 2;
    let top = selectionRect.top - bubbleRect.height - 8;

    left = Math.max(8, Math.min(left, window.innerWidth - bubbleRect.width - 8));
    if (top < 8) {
      top = selectionRect.bottom + 8;
    }
    top = Math.max(8, Math.min(top, window.innerHeight - bubbleRect.height - 8));

    popover.style.left = `${Math.round(left)}px`;
    popover.style.top = `${Math.round(top)}px`;
  }

  function refreshSelectionPopover() {
    showSelectionPopover(readSelectionContext());
  }

  const HOVER_HOTZONE_WIDTH = 104;
  const HOVER_HOTZONE_HEIGHT = 54;

  function setHoveredCard(card) {
    const next = card instanceof Element ? card : null;

    document.querySelectorAll('.yanzi-xhs-filter-card[data-yanzi-hover="1"]')
      .forEach(activeCard => {
        if (activeCard !== next) delete activeCard.dataset.yanziHover;
      });

    hoveredCard = next;
    if (hoveredCard) {
      hoveredCard.dataset.yanziHover = "1";
    }
  }

  function isInsideTopRightHotzone(card, clientX, clientY) {
    if (!(card instanceof Element)) return false;
    const rect = card.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return false;

    const zoneLeft = Math.max(rect.left, rect.right - HOVER_HOTZONE_WIDTH);
    const zoneBottom = Math.min(rect.bottom, rect.top + HOVER_HOTZONE_HEIGHT);

    return clientX >= zoneLeft &&
      clientX <= rect.right &&
      clientY >= rect.top &&
      clientY <= zoneBottom;
  }

  function findCardAtPoint(clientX, clientY) {
    const cards = collectCards(document);
    for (const card of cards) {
      if (!(card instanceof Element)) continue;
      const rect = card.getBoundingClientRect();
      if (clientX >= rect.left &&
          clientX <= rect.right &&
          clientY >= rect.top &&
          clientY <= rect.bottom) {
        return card;
      }
    }
    return null;
  }

  function updateHoveredCardFromEvent(event) {
    const card = findCardAtPoint(event.clientX, event.clientY);
    const next = card && isInsideTopRightHotzone(card, event.clientX, event.clientY)
      ? card
      : null;
    setHoveredCard(next);
  }

  document.addEventListener("pointermove", updateHoveredCardFromEvent, true);
  document.addEventListener("mousemove", updateHoveredCardFromEvent, true);

  window.addEventListener("scroll", () => {
    hideSelectionPopover();
    markVisibleLocalFeedCards();
    scheduleFilter(document);
  }, { passive: true });

  document.addEventListener("pointerdown", event => {
    if (!event.target.closest?.(".yanzi-xhs-filter-menu")) {
      hideFilterMenu();
    }
    if (!event.target.closest?.(".yanzi-xhs-selection-popover")) {
      hideSelectionPopover();
    }
    if (!event.target.closest?.(".yanzi-xhs-interest-panel, .yanzi-xhs-interest-nav")) {
      const panel = document.querySelector(".yanzi-xhs-interest-panel");
      if (panel) panel.hidden = true;
    }
    if (!event.target.closest?.(".yanzi-xhs-evaluation-panel, .yanzi-xhs-local-feedback")) {
      const panel = document.querySelector(".yanzi-xhs-evaluation-panel");
      if (panel) panel.hidden = true;
      evaluationPanelContext = null;
    }
  }, true);

  document.addEventListener("mouseup", event => {
    if (event.button !== 0) return;
    if (event.target.closest?.(".yanzi-xhs-selection-popover")) return;

    // Capture the finished selection synchronously. The browser may dispatch a link
    // click immediately after mouseup, before a setTimeout callback has a chance to run.
    const context = readSelectionContext();
    if (context) selectionPopoverContext = context;
    setTimeout(() => showSelectionPopover(context), 0);
  }, true);

  document.addEventListener("keyup", event => {
    if (event.key === "Shift" ||
        event.key.startsWith("Arrow") ||
        event.key === "Home" ||
        event.key === "End") {
      setTimeout(refreshSelectionPopover, 0);
    }
  }, true);

  document.addEventListener("selectionchange", () => {
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) {
      hideSelectionPopover();
    }
  });

  document.addEventListener("click", event => {
    if (event.target.closest?.(".yanzi-xhs-selection-popover")) return;

    const selection = window.getSelection();
    if (!selection ||
        selection.rangeCount === 0 ||
        selection.isCollapsed ||
        !normalizeText(selection.toString())) {
      return;
    }

    const target = event.target instanceof Element ? event.target : null;
    const card = findCard(target);
    if (!card) return;

    const range = selection.getRangeAt(0);
    const inTitle = Boolean(findRangeContainer(card, range, TITLE_SELECTORS));
    const inAuthor = Boolean(findRangeContainer(card, range, AUTHOR_SELECTORS));
    if (!inTitle && !inAuthor) return;

    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();
  }, true);

  document.addEventListener("click", event => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;
    if (target.closest(".yanzi-xhs-custom-card")) return;
    if (target.closest(".yanzi-xhs-hover-block, .yanzi-xhs-hover-manage, .yanzi-xhs-filter-menu, .yanzi-xhs-filter-panel, .yanzi-xhs-selection-popover")) return;
    if (target.closest("a[href*='/user/profile/']")) return;

    const card = findCard(target);
    if (!card || card.classList.contains("yanzi-xhs-filter-hidden")) return;

    const noteTrigger = target.closest(
      "a.cover, a[href*='/explore/'], a[href*='/discovery/item/'], .title, [class*='title']"
    );
    if (!noteTrigger) return;

    const meta = extractCardMeta(card);
    if (!meta?.noteId && !meta?.noteHref && !meta?.title) return;

    pendingOpenedNote = {
      meta,
      clickedAt: Date.now(),
      sourceUrl: location.href
    };

    for (const delay of [80, 320, 900, 1800]) {
      setTimeout(() => scheduleAnalyticsInspect(0), delay);
    }
  }, true);

  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      pauseActiveNoteVisibility();
      if (activeNoteSession) markNoteStatsDirty();
    } else {
      resumeActiveNoteVisibility();

      // Edge may heavily throttle requestAnimationFrame/timers while this tab is
      // in the background. Treat becoming visible as a recovery checkpoint:
      // reconcile stale assignments, rerun the feed pass, and resume supply.
      reconcileLocalFeedAssignmentsWithDom();
      scheduleFilter(document);
      markVisibleLocalFeedCards();
      void maybeReplenishLocalFeed();
      scheduleAnalyticsInspect(0);

      // Xiaohongshu can hydrate/recycle its waterfall immediately after the tab
      // becomes active. Run a few delayed passes so cards created in that window
      // also receive local-note overlays without requiring user scrolling.
      for (const delay of [120, 450, 1200]) {
        setTimeout(() => {
          scheduleFilter(document);
          markVisibleLocalFeedCards();
        }, delay);
      }
    }
  });

  window.addEventListener("popstate", () => scheduleAnalyticsInspect(0));
  window.addEventListener("hashchange", () => scheduleAnalyticsInspect(0));
  window.addEventListener("focus", () => scheduleAnalyticsInspect(0));

  window.addEventListener("pagehide", () => {
    if (activeNoteSession) finalizeActiveNote("pagehide");
    void saveNoteStatsLocal();
    void persistLocalFeedState();
  });

  window.addEventListener("resize", hideSelectionPopover, { passive: true });
  document.addEventListener("mouseover", updateHoveredCardFromEvent, true);
  document.addEventListener("pointerleave", () => setHoveredCard(null), true);

  document.addEventListener("contextmenu", event => {
    lastContextTarget = event.target instanceof Element ? event.target : null;
  }, true);

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.appId !== APP_ID) return;

    if (message.type === "yanzi_webapp_probe") {
      const cards = collectCards(document);
      let debugLayoutTest = null;

      if (message.debugLayoutTest && cards.length) {
        const snapshots = cards.map(card => ({
          card,
          hidden: card.classList.contains("yanzi-xhs-filter-hidden"),
          filtered: card.dataset.yanziFiltered,
          hover: card.dataset.yanziHover,
          nativeX: card.dataset.yanziNativeX,
          nativeY: card.dataset.yanziNativeY,
          nativeHeight: card.dataset.yanziNativeHeight,
          translate: card.style.getPropertyValue("translate"),
          translatePriority: card.style.getPropertyPriority("translate")
        }));

        try {
          const visibleItems = cards
            .filter(card => !card.classList.contains("yanzi-xhs-filter-hidden"))
            .map(card => ({ card, ...parseNativeTranslate(card) }));

          const columns = [...new Set(
            visibleItems.map(item => Math.round(item.x * 10) / 10)
          )].sort((a, b) => a - b);

          const requestedIndex = Number(message.debugLayoutTest.columnIndex ?? 2);
          const columnIndex = Math.max(0, Math.min(columns.length - 1, requestedIndex));
          const targetX = columns[columnIndex];
          const target = visibleItems
            .filter(item => Math.abs(item.x - targetX) < 0.2)
            .sort((a, b) => a.y - b.y)[0]?.card;

          if (!target) {
            debugLayoutTest = { ok: false, error: "no_target_card", columns };
          } else {
            const beforeTranslate = new Map(cards.map(card => [
              card,
              getComputedStyle(card).translate
            ]));

            rememberNativeGeometry(target);
            target.classList.add("yanzi-xhs-filter-hidden");
            target.dataset.yanziFiltered = "debug";

            compactWaterfallLayout(cards);

            const changedColumns = new Set();
            const translatedColumns = new Set();

            for (const card of cards) {
              const native = parseNativeTranslate(card);
              const afterTranslate = getComputedStyle(card).translate;
              const before = beforeTranslate.get(card);

              if (afterTranslate !== before) {
                changedColumns.add(Math.round(native.x * 10) / 10);
              }
              if (afterTranslate && afterTranslate !== "none" && afterTranslate !== "0px") {
                translatedColumns.add(Math.round(native.x * 10) / 10);
              }
            }

            debugLayoutTest = {
              ok: true,
              targetX,
              targetTitle: extractCardMeta(target)?.title || "",
              changedColumns: [...changedColumns].sort((a, b) => a - b),
              translatedColumns: [...translatedColumns].sort((a, b) => a - b),
              untouchedColumnsStayedUntouched:
                [...changedColumns].every(x => Math.abs(x - targetX) < 0.2)
            };
          }
        } finally {
          for (const snapshot of snapshots) {
            const { card } = snapshot;

            if (snapshot.hidden) card.classList.add("yanzi-xhs-filter-hidden");
            else card.classList.remove("yanzi-xhs-filter-hidden");

            if (snapshot.filtered === undefined) delete card.dataset.yanziFiltered;
            else card.dataset.yanziFiltered = snapshot.filtered;

            if (snapshot.hover === undefined) delete card.dataset.yanziHover;
            else card.dataset.yanziHover = snapshot.hover;

            const restoreDataset = (key, value) => {
              if (value === undefined) delete card.dataset[key];
              else card.dataset[key] = value;
            };
            restoreDataset("yanziNativeX", snapshot.nativeX);
            restoreDataset("yanziNativeY", snapshot.nativeY);
            restoreDataset("yanziNativeHeight", snapshot.nativeHeight);

            if (snapshot.translate) {
              card.style.setProperty(
                "translate",
                snapshot.translate,
                snapshot.translatePriority || ""
              );
            } else {
              card.style.removeProperty("translate");
            }
          }
        }
      }

      let debugSelectionTest = null;
      if (message.debugSelectionTest) {
        const kind = message.debugSelectionTest.kind === "author" ? "author" : "title";
        const sourceCards = cards.filter(card => !card.classList.contains("yanzi-xhs-filter-hidden"));
        let targetCard = null;
        let targetElement = null;

        for (const card of sourceCards) {
          const selectors = kind === "author" ? AUTHOR_SELECTORS : TITLE_SELECTORS;
          for (const selector of selectors) {
            const candidate = card.querySelector(selector);
            if (normalizeText(candidate?.textContent)) {
              targetCard = card;
              targetElement = candidate;
              break;
            }
          }
          if (targetElement) break;
        }

        if (!targetElement) {
          debugSelectionTest = { ok: false, error: "selection_target_not_found", kind };
        } else {
          const walker = document.createTreeWalker(
            targetElement,
            NodeFilter.SHOW_TEXT,
            {
              acceptNode(node) {
                return normalizeText(node.nodeValue)
                  ? NodeFilter.FILTER_ACCEPT
                  : NodeFilter.FILTER_REJECT;
              }
            }
          );
          const textNode = walker.nextNode();
          if (!textNode) {
            debugSelectionTest = { ok: false, error: "text_node_not_found", kind };
          } else {
            const text = textNode.nodeValue || "";
            const start = Math.max(0, Number(message.debugSelectionTest.start || 0));
            const requestedLength = Math.max(1, Number(message.debugSelectionTest.length || 4));
            const end = Math.min(text.length, start + requestedLength);
            const range = document.createRange();
            range.setStart(textNode, Math.min(start, text.length));
            range.setEnd(textNode, Math.max(Math.min(end, text.length), Math.min(start + 1, text.length)));

            const selection = window.getSelection();
            selection.removeAllRanges();
            selection.addRange(range);
            refreshSelectionPopover();

            const popover = document.querySelector(".yanzi-xhs-selection-popover");
            const rect = popover?.getBoundingClientRect();
            debugSelectionTest = {
              ok: Boolean(popover && !popover.hidden),
              kind,
              selectedText: normalizeText(selection.toString()),
              actions: Array.from(popover?.querySelectorAll(".yanzi-xhs-selection-action") || [])
                .map(action => ({
                  text: normalizeText(action.textContent),
                  action: action.dataset.action || ""
                })),
              popoverRect: rect ? {
                left: rect.left,
                top: rect.top,
                width: rect.width,
                height: rect.height
              } : null,
              cardTitle: extractCardMeta(targetCard)?.title || "",
              cardAuthor: extractCardMeta(targetCard)?.author || ""
            };
          }
        }
      }

      let debugLocalFeedTest = null;
      if (message.debugLocalFeedTest) {
        const action = normalizeText(message.debugLocalFeedTest.action);

        if (action === "reset") {
          localFeedState = createInitialLocalFeedState();
          localFeedLoaded = true;
          customCardSequenceIndex = 0;
          customCardSequenceInitialized = false;

          document.querySelectorAll(".yanzi-xhs-custom-host")
            .forEach(clearCustomCardHost);

          void persistLocalFeedState();
          scheduleFilter(document);
          debugLocalFeedTest = {
            action,
            ok: true,
            futureSupply: getLocalFeedFutureSupply()
          };
        } else if (action === "force-supply") {
          const target = Math.max(
            0,
            Math.min(
              LOCAL_FEED_BATCH_SIZE,
              Number(message.debugLocalFeedTest.count ?? 2)
            )
          );

          const notes = localFeedState.notes
            .slice()
            .sort((a, b) =>
              Number(a.createdAt || 0) - Number(b.createdAt || 0)
            );
          const keepIds = new Set(notes.slice(-target).map(note => note.id));
          const now = Date.now();

          for (const note of localFeedState.notes) {
            if (keepIds.has(note.id)) {
              note.state = "ready";
              note.assignedAt = 0;
              note.seenAt = 0;
            } else {
              note.state = "seen";
              note.seenAt = note.seenAt || now;
              note.assignedAt = 0;
            }
            note.updatedAt = now;
          }

          localFeedState.generation = {
            ...localFeedState.generation,
            inFlight: false,
            startedAt: 0,
            lastCompletedAt: Number(localFeedState.generation?.lastCompletedAt || 0),
            lastError: ""
          };

          document.querySelectorAll(".yanzi-xhs-custom-host")
            .forEach(clearCustomCardHost);

          void persistLocalFeedState();
          scheduleFilter(document);

          if (message.debugLocalFeedTest.triggerGeneration !== false) {
            setTimeout(() => void maybeReplenishLocalFeed(), 0);
          }

          debugLocalFeedTest = {
            action,
            ok: true,
            target,
            futureSupply: getLocalFeedFutureSupply(),
            triggered: message.debugLocalFeedTest.triggerGeneration !== false
          };
        }
      }

      sendResponse({
        ok: true,
        appId: APP_ID,
        version: "0.7.5",
        enabled,
        filteringEnabled,
        url: location.href,
        cards: cards.length,
        hoverButtons: document.querySelectorAll(".yanzi-xhs-hover-block").length,
        hoveredCards: document.querySelectorAll('.yanzi-xhs-filter-card[data-yanzi-hover="1"]').length,
        visibleHoverButtons: Array.from(document.querySelectorAll(".yanzi-xhs-hover-block")).filter(button => {
          const style = getComputedStyle(button);
          return style.display !== "none" && style.visibility !== "hidden" && style.opacity !== "0";
        }).length,
        visibleManageButtons: Array.from(document.querySelectorAll(".yanzi-xhs-hover-manage")).filter(button => {
          const style = getComputedStyle(button);
          return style.display !== "none" && style.visibility !== "hidden" && style.opacity !== "0";
        }).length,
        hiddenCards: document.querySelectorAll(".yanzi-xhs-filter-hidden").length,
        debugCards: cards.map(card => {
          const meta = extractCardMeta(card);
          const native = parseNativeTranslate(card);
          const style = getComputedStyle(card);
          return {
            title: meta?.title || "",
            author: meta?.author || "",
            nativeX: native.x,
            nativeY: native.y,
            hidden: card.classList.contains("yanzi-xhs-filter-hidden"),
            translate: style.translate,
            display: style.display
          };
        }),
        layout: (() => {
          const sample = collectCards(document).slice(0, 6).map(card => {
            const style = getComputedStyle(card);
            const rect = card.getBoundingClientRect();
            return {
              position: style.position,
              transform: style.transform,
              top: style.top,
              left: style.left,
              marginBottom: style.marginBottom,
              width: rect.width,
              height: rect.height,
              x: rect.x,
              y: rect.y
            };
          });
          const parent = collectCards(document)[0]?.parentElement;
          const parentStyle = parent ? getComputedStyle(parent) : null;
          const parentRect = parent?.getBoundingClientRect?.();
          return {
            sample,
            parent: parentStyle ? {
              position: parentStyle.position,
              display: parentStyle.display,
              height: parentStyle.height,
              width: parentRect?.width || 0
            } : null
          };
        })(),
        authorRules: rules.filter(rule => rule.type === "author" && rule.enabled !== false).length,
        keywordRules: rules.filter(rule => rule.type === "keyword" && rule.enabled !== false).length,
        noteRules: rules.filter(rule => rule.type === "note" && rule.enabled !== false).length,
        noteStatsCount: Array.isArray(noteStats.items) ? noteStats.items.length : 0,
        noteStatsEvents: Array.isArray(noteStats.events) ? noteStats.events.length : 0,
        customCardsCount: customCards.length,
        localTestNotesEnabled: LOCAL_TEST_NOTES_ENABLED,
        localTestNotesCount: LOCAL_TEST_NOTES.length,
        localTestRealNotesPerCard: LOCAL_TEST_REAL_NOTES_PER_CARD,
        localFeedLoaded,
        localFeedReady: localFeedState.notes.filter(note => note.state === "ready").length,
        localFeedAssigned: localFeedState.notes.filter(note => note.state === "assigned" && !note.seenAt).length,
        localFeedActiveAssignments: getActiveLocalFeedAssignmentIds().size,
        localFeedGhostAssignments: localFeedState.notes.filter(note =>
          note.state === "assigned" &&
          !note.seenAt &&
          !getActiveLocalFeedAssignmentIds().has(note.id)
        ).length,
        localFeedSeen: localFeedState.notes.filter(note => Boolean(note.seenAt)).length,
        localFeedGenerated: localFeedState.notes.filter(note => note.source === "chatgpt").length,
        localFeedFutureSupply: getLocalFeedFutureSupply(),
        localFeedFeedbackCount: localFeedState.feedback.length,
        localFeedLikes: localFeedState.feedback.filter(item => item.value === "like").length,
        localFeedDislikes: localFeedState.feedback.filter(item => item.value === "dislike").length,
        localFeedBatchSize: LOCAL_FEED_BATCH_SIZE,
        localFeedInterests: (localFeedState.preferences?.interests || []).slice(),
        localFeedEvaluationsPending: (localFeedState.evaluations || [])
          .filter(item => !item.usedAt)
          .map(item => ({
            id: item.id,
            noteId: item.noteId,
            text: item.text,
            at: item.at
          })),
        localFeedEvaluationsUsed: (localFeedState.evaluations || [])
          .filter(item => Boolean(item.usedAt)).length,
        interestNavPresent: Boolean(document.querySelector(".yanzi-xhs-interest-nav")),
        interestPanelPresent: Boolean(document.querySelector(".yanzi-xhs-interest-panel")),
        evaluationPanelPresent: Boolean(document.querySelector(".yanzi-xhs-evaluation-panel")),
        localFeedGeneration: {
          inFlight: localFeedState.generation?.inFlight === true,
          startedAt: Number(localFeedState.generation?.startedAt || 0),
          lastCompletedAt: Number(localFeedState.generation?.lastCompletedAt || 0),
          lastReturnedCount: Number(localFeedState.generation?.lastReturnedCount || 0),
          lastElapsedMs: Number(localFeedState.generation?.lastElapsedMs || 0),
          lastContext: localFeedState.generation?.lastContext || null,
          lastError: localFeedState.generation?.lastError || ""
        },
        visibleCustomCards: document.querySelectorAll(".yanzi-xhs-custom-card").length,
        activeNoteSession: activeNoteSession ? {
          id: activeNoteSession.id,
          noteId: activeNoteSession.noteId || "",
          openedAt: activeNoteSession.openedAt || 0,
          visibleAccumulatedMs: activeNoteSession.visibleAccumulatedMs || 0,
          visibleSince: activeNoteSession.visibleSince || 0
        } : null,
        debugLayoutTest,
        debugSelectionTest,
        debugLocalFeedTest
      });
      return true;
    }

    if (message.type === "yanzi_webapp_command") {
      void handleCommand(message);
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === RULES_KEY) {
      rules = Array.isArray(message.value?.items) ? message.value.items : [];
      scheduleFilter(document);
      renderRulePanel();
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === SETTINGS_KEY) {
      filteringEnabled = message.value?.filteringEnabled !== false;
      scheduleFilter(document);
      renderRulePanel();
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === CUSTOM_CARDS_KEY) {
      customCards = normalizeCustomCardsValue(message.value);
      scheduleFilter(document);
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === NOTE_STATS_KEY) {
      const incoming = normalizeNoteStatsValue(message.value);
      if (Number(incoming.updatedAt || 0) >= Number(noteStats.updatedAt || 0)) {
        noteStats = incoming;
      }
    }
  });

  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== "local") return;
    const change = changes[LOCAL_FEED_STORAGE_KEY];
    if (!change?.newValue) return;

    const incoming = normalizeLocalFeedState(change.newValue);
    if (Number(incoming.updatedAt || 0) <= Number(localFeedState.updatedAt || 0)) return;

    localFeedState = incoming;
    localFeedLoaded = true;
    renderInterestPanel();
    updateInterestNavButton();
    scheduleFilter(document);
    markVisibleLocalFeedCards();
  });

  ensureStyle();

  function startObserver() {
    if (observer) return;
    observer = new MutationObserver(mutations => {
      if (mutations.some(mutation => mutation.addedNodes.length > 0)) {
        // Xiaohongshu often fills card skeletons in several DOM mutations.
        // Batch them into one full feed pass so late-loaded author/title nodes are not missed.
        scheduleFilter(document);
        scheduleAnalyticsInspect(180);
      }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
  }

  startObserver();

  globalThis.__yanziXiaohongshuFilterRefresh = () => {
    enabled = true;
    startObserver();
    void Promise.all([
      loadRules(),
      loadSettings(),
      loadNoteStats(),
      loadCustomCards(),
      loadLocalFeedState()
    ]).then(() => {
      scheduleFilter(document);
      scheduleAnalyticsInspect(0);
    });
  };

  void Promise.all([
    loadRules(),
    loadSettings(),
    loadNoteStats(),
    loadCustomCards(),
    loadLocalFeedState()
  ]).then(() => {
    scheduleFilter(document);
    scheduleAnalyticsInspect(0);
  });
})();
