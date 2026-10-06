(function () {
  "use strict";

  const APP_ID = "douyin.filter";
  const RULES_KEY = "rules";
  const SETTINGS_KEY = "settings";
  const STYLE_ID = "yanzi-douyin-filter-style";
  const PANEL_POSITION_KEY = "yanzi.douyin.filter.panel-position.v1";
  const PANEL_COLLAPSED_KEY = "yanzi.douyin.filter.panel-collapsed.v1";

  const SELECTORS = {
    activeVideo: '[data-e2e="feed-active-video"]',
    accountName: '[data-e2e="feed-video-nickname"]',
    videoDesc: '[data-e2e="video-desc"]',
    video: "video"
  };

  const DEFAULT_RULES = {
    version: 2,
    items: [
      {
        id: "default-shopping",
        type: "keyword",
        value: "购物",
        label: "购物",
        scopes: ["title", "tags", "author", "commerce"],
        enabled: true,
        createdAt: 0
      },
      {
        id: "default-live",
        type: "keyword",
        value: "直播",
        label: "直播",
        scopes: ["title", "tags", "author", "commerce"],
        enabled: true,
        createdAt: 0
      },
      {
        id: "default-ad",
        type: "keyword",
        value: "广告",
        label: "广告",
        scopes: ["ad"],
        enabled: true,
        createdAt: 0
      }
    ]
  };

  if (globalThis.__yanziDouyinFilterLoaded) {
    globalThis.__yanziDouyinFilterRefresh?.();
    return;
  }
  globalThis.__yanziDouyinFilterLoaded = true;

  let enabled = true;
  // Auto-skip starts off until the user explicitly enables it in the new review UI.
  let filteringEnabled = false;
  let rules = [];
  let loopTimer = null;
  let sidebarTimer = null;
  let skipTimer = null;
  let pendingSkipTimer = null;
  let skipAttempts = 0;
  let skipInFlight = false;
  let skipFromIdentity = "";
  let lastEvaluatedIdentity = "";
  let lastMatch = null;
  let currentMatch = null;
  let currentMeta = null;
  let currentIdentity = "";

  function normalizeText(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
      ::highlight(yanzi-douyin-match) {
        color: #ff334c !important;
        background: rgba(255, 36, 66, .26) !important;
        text-decoration: underline !important;
        text-decoration-color: #ff334c !important;
        text-decoration-thickness: 2px !important;
      }
      .yanzi-douyin-filter-control {
        position: fixed !important;
        left: 18px !important;
        bottom: 72px !important;
        z-index: 2147483647 !important;
        min-width: 220px !important;
        width: min(360px, calc(100vw - 36px)) !important;
        max-width: 360px !important;
        padding: 8px 9px !important;
        border: 1px solid rgba(255,255,255,.12) !important;
        border-radius: 10px !important;
        background: rgba(22,23,27,.90) !important;
        color: #fff !important;
        box-shadow: 0 8px 28px rgba(0,0,0,.28) !important;
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        font: 12px/1.35 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        user-select: none !important;
      }
      .yanzi-douyin-filter-control-head {
        display: flex !important;
        align-items: center !important;
        justify-content: space-between !important;
        gap: 8px !important;
        cursor: grab !important;
      }
      .yanzi-douyin-filter-control-head:active {
        cursor: grabbing !important;
      }
      .yanzi-douyin-filter-control[data-collapsed="1"] {
        width: auto !important;
        min-width: 168px !important;
        max-width: 220px !important;
        padding: 7px 9px !important;
      }
      .yanzi-douyin-filter-control[data-collapsed="1"] .yanzi-douyin-filter-body {
        display: none !important;
      }
      .yanzi-douyin-filter-control[data-collapsed="1"] .yanzi-douyin-filter-expand-indicator {
        transform: rotate(-90deg);
      }
      .yanzi-douyin-filter-expand-indicator {
        display: inline-flex !important;
        align-items: center !important;
        justify-content: center !important;
        width: 14px !important;
        height: 14px !important;
        color: rgba(255,255,255,.48) !important;
        font-size: 10px !important;
        line-height: 1 !important;
        transition: transform .12s ease !important;
        flex: 0 0 auto !important;
      }
      .yanzi-douyin-filter-toggle {
        cursor: pointer !important;
      }
      .yanzi-douyin-filter-control-state {
        display: inline-flex !important;
        align-items: center !important;
        gap: 6px !important;
        min-width: 0 !important;
        color: rgba(255,255,255,.88) !important;
        white-space: nowrap !important;
      }
      .yanzi-douyin-filter-control-dot {
        width: 7px !important;
        height: 7px !important;
        border-radius: 50% !important;
        background: #9ca3af !important;
      }
      .yanzi-douyin-filter-control[data-enabled="1"] .yanzi-douyin-filter-control-dot {
        background: #34d399 !important;
      }
      .yanzi-douyin-filter-toggle {
        border: 0 !important;
        border-radius: 7px !important;
        padding: 4px 9px !important;
        background: rgba(255,255,255,.10) !important;
        color: #fff !important;
        cursor: pointer !important;
        font: inherit !important;
      }
      .yanzi-douyin-filter-toggle:hover {
        background: rgba(255,255,255,.17) !important;
      }
      .yanzi-douyin-sidebar-switch-slot {
        width: 100% !important;
        min-height: 40px !important;
        display: flex !important;
        align-items: center !important;
        justify-content: center !important;
        flex: 0 0 auto !important;
        box-sizing: border-box !important;
      }
      .yanzi-douyin-sidebar-switch {
        width: 52px !important;
        height: 38px !important;
        padding: 4px 0 !important;
        border: 0 !important;
        border-radius: 8px !important;
        background: transparent !important;
        color: rgba(255,255,255,.72) !important;
        display: flex !important;
        flex-direction: column !important;
        align-items: center !important;
        justify-content: center !important;
        gap: 3px !important;
        cursor: pointer !important;
        font: 10px/1 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        box-sizing: border-box !important;
      }
      .yanzi-douyin-sidebar-switch:hover {
        background: rgba(255,255,255,.07) !important;
      }
      .yanzi-douyin-sidebar-switch-label {
        color: rgba(255,255,255,.62) !important;
        white-space: nowrap !important;
      }
      .yanzi-douyin-sidebar-switch-track {
        position: relative !important;
        width: 30px !important;
        height: 17px !important;
        border-radius: 999px !important;
        background: rgba(255,255,255,.22) !important;
        box-shadow: inset 0 0 0 1px rgba(255,255,255,.08) !important;
        transition: background .14s ease !important;
      }
      .yanzi-douyin-sidebar-switch-thumb {
        position: absolute !important;
        top: 2px !important;
        left: 2px !important;
        width: 13px !important;
        height: 13px !important;
        border-radius: 50% !important;
        background: #fff !important;
        box-shadow: 0 1px 3px rgba(0,0,0,.32) !important;
        transition: transform .14s ease !important;
      }
      .yanzi-douyin-sidebar-switch[aria-checked="true"] .yanzi-douyin-sidebar-switch-track {
        background: #fe2c55 !important;
      }
      .yanzi-douyin-sidebar-switch[aria-checked="true"] .yanzi-douyin-sidebar-switch-thumb {
        transform: translateX(13px) !important;
      }
      .yanzi-douyin-filter-meta {
        display: grid !important;
        gap: 4px !important;
        margin-top: 8px !important;
        padding-top: 8px !important;
        border-top: 1px solid rgba(255,255,255,.09) !important;
      }
      .yanzi-douyin-filter-meta-row {
        display: grid !important;
        grid-template-columns: 38px minmax(0, 1fr) !important;
        gap: 6px !important;
        align-items: start !important;
      }
      .yanzi-douyin-filter-meta-key {
        color: rgba(255,255,255,.46) !important;
        white-space: nowrap !important;
      }
      .yanzi-douyin-filter-meta-value {
        color: rgba(255,255,255,.82) !important;
        min-width: 0 !important;
        max-height: 36px !important;
        overflow: hidden !important;
        word-break: break-all !important;
      }
      .yanzi-douyin-filter-meta-value[data-empty="1"] {
        color: rgba(255,255,255,.36) !important;
      }
      .yanzi-douyin-filter-meta-match {
        color: #ff334c !important;
        font-weight: 650 !important;
        background: rgba(255,51,76,.14) !important;
        border-radius: 3px !important;
        padding: 0 1px !important;
      }
      .yanzi-douyin-filter-commerce-hit {
        outline: 2px solid #ff334c !important;
        outline-offset: 2px !important;
        border-radius: 6px !important;
      }
      .yanzi-douyin-filter-match {
        display: none;
        margin-top: 7px !important;
        padding-top: 7px !important;
        border-top: 1px solid rgba(255,255,255,.09) !important;
        color: rgba(255,255,255,.68) !important;
        word-break: break-all !important;
      }
      .yanzi-douyin-filter-match[data-visible="1"] {
        display: block !important;
      }
      .yanzi-douyin-filter-match strong {
        color: #ff4960 !important;
        font-weight: 600 !important;
      }
      .yanzi-douyin-filter-toast {
        position: fixed !important;
        left: 50% !important;
        bottom: 88px !important;
        transform: translateX(-50%) !important;
        z-index: 2147483647 !important;
        max-width: min(520px, calc(100vw - 32px)) !important;
        padding: 9px 13px !important;
        border-radius: 9px !important;
        background: rgba(24,24,27,.90) !important;
        color: #fff !important;
        box-shadow: 0 8px 28px rgba(0,0,0,.28) !important;
        backdrop-filter: blur(10px);
        -webkit-backdrop-filter: blur(10px);
        font: 12px/1.35 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif !important;
        pointer-events: none !important;
      }
    `;
    (document.head || document.documentElement).appendChild(style);
  }

  function showToast(text, duration = 1300) {
    let toast = document.querySelector(".yanzi-douyin-filter-toast");
    if (!toast) {
      toast = document.createElement("div");
      toast.className = "yanzi-douyin-filter-toast";
      document.documentElement.appendChild(toast);
    }
    toast.textContent = text;
    toast.hidden = false;
    clearTimeout(showToast.timer);
    showToast.timer = setTimeout(() => {
      toast.hidden = true;
    }, duration);
  }

  function visibleRatio(element) {
    if (!(element instanceof Element)) return 0;
    const rect = element.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return 0;
    const left = Math.max(0, rect.left);
    const top = Math.max(0, rect.top);
    const right = Math.min(window.innerWidth, rect.right);
    const bottom = Math.min(window.innerHeight, rect.bottom);
    const visible = Math.max(0, right - left) * Math.max(0, bottom - top);
    return visible / Math.max(1, rect.width * rect.height);
  }

  function isElementInViewport(element) {
    if (!(element instanceof Element)) return false;
    const rect = element.getBoundingClientRect();
    return rect.bottom > 0 &&
      rect.right > 0 &&
      rect.top < window.innerHeight &&
      rect.left < window.innerWidth;
  }

  function findVisibleTextElement(root, predicate) {
    if (!(root instanceof Element) && root !== document) return null;

    const candidates = Array.from(root.querySelectorAll(
      "[data-e2e],button,a,p,span,div"
    )).filter(element => {
      if (!isElementInViewport(element)) return false;
      const text = normalizeText(element.textContent);
      return text && predicate(text, element);
    });

    return candidates.find(element => {
      const text = normalizeText(element.textContent);
      return !Array.from(element.children || []).some(child =>
        isElementInViewport(child) &&
        normalizeText(child.textContent) === text
      );
    }) || candidates[0] || null;
  }

  function getStrongLiveEvidence(container) {
    if (!(container instanceof Element)) {
      return {
        strong: false,
        statusElement: null,
        enterElement: null,
        statusText: "",
        enterText: ""
      };
    }

    const text = normalizeText(container.textContent);
    const statusElement = findVisibleTextElement(
      container,
      value => value === "直播中" || value === "正在直播"
    );
    const enterElement = findVisibleTextElement(
      container,
      value => value.includes("进入直播间") &&
        value.length <= 60 &&
        (value.includes("点击") || value.includes("按") || value.length <= 28)
    );

    const liveLink = Array.from(container.querySelectorAll("a[href]"))
      .find(element =>
        isElementInViewport(element) &&
        /live\.douyin\.com|\/root\/live\//.test(String(element.href || ""))
      ) || null;

    const hasStatus = Boolean(statusElement) ||
      text.includes("直播中") ||
      text.includes("正在直播");
    const hasEnter = Boolean(enterElement) ||
      text.includes("进入直播间");
    const hasExplicitPrompt =
      /点击.*进入直播间/.test(text) ||
      /按\s*[A-Za-z]\s*进入直播间/i.test(text);

    return {
      strong: hasEnter && (hasStatus || hasExplicitPrompt || Boolean(liveLink)),
      statusElement,
      enterElement,
      statusText: normalizeText(statusElement?.textContent) ||
        (text.includes("直播中") ? "直播中" : text.includes("正在直播") ? "正在直播" : ""),
      enterText: normalizeText(enterElement?.textContent),
      liveLink
    };
  }

  function findVisibleLiveIndicator() {
    return findVisibleTextElement(
      document,
      value => value.includes("进入直播间") &&
        value.length <= 60 &&
        (value.includes("点击") || value.includes("按") || value.length <= 28)
    );
  }

  function getLiveContainer() {
    const indicator = findVisibleLiveIndicator();
    if (!indicator) return null;

    let node = indicator;
    let fallback = indicator.parentElement || indicator;
    while (node && node !== document.body) {
      const rect = node.getBoundingClientRect();
      if (rect.width >= Math.min(420, window.innerWidth * 0.35) &&
          rect.height >= window.innerHeight * 0.45) {
        fallback = node;
        if (getStrongLiveEvidence(node).strong) return node;
      }
      node = node.parentElement;
    }
    return getStrongLiveEvidence(fallback).strong ? fallback : null;
  }

  function getActiveContainer() {
    // Normal feed items are authoritative. A visible live-entry elsewhere on the page
    // must never override the actual active video.
    const candidates = Array.from(document.querySelectorAll(SELECTORS.activeVideo));
    let best = null;
    let bestScore = 0;
    for (const candidate of candidates) {
      const score = visibleRatio(candidate);
      if (score > bestScore) {
        best = candidate;
        bestScore = score;
      }
    }
    if (bestScore > 0.12) return best;

    // Some live preview cards do not expose feed-active-video at all.
    return getLiveContainer();
  }

  function isLiveContainer(container) {
    return getStrongLiveEvidence(container).strong;
  }

  const COMMERCE_TEXT_TERMS = [
    "购物",
    "视频同款",
    "查看详情",
    "立即购买",
    "去购买",
    "优品",
    "小黄车",
    "抢购"
  ];

  function getCommerceEvidence(element, text) {
    const href = String(element.getAttribute?.("href") || "").toLowerCase();
    const dataE2e = String(element.getAttribute?.("data-e2e") || "").toLowerCase();
    const ariaLabel = String(element.getAttribute?.("aria-label") || "").toLowerCase();

    const textTerms = COMMERCE_TEXT_TERMS.filter(term => text.includes(term));

    // Only strong structural evidence is accepted. Generic class names are deliberately
    // excluded because Douyin reuses product/shop-like internal class tokens broadly.
    const hrefEvidence =
      /(?:^|[/?&=_-])(product|goods|commodity|ecom|mall|haohuo|sku)(?:[/?&=_-]|$)/.test(href) ||
      /product_id=|goods_id=|sku_id=|commodity_id=/.test(href);

    const dataEvidence =
      /(?:^|[-_])(product|goods|commodity|ecom|mall|shop-card|shopping-cart)(?:[-_]|$)/.test(dataE2e);

    const ariaEvidence = [
      "购物",
      "购买",
      "小黄车",
      "查看详情"
    ].some(term => ariaLabel.includes(term));

    return {
      textTerms,
      hrefEvidence,
      dataEvidence,
      ariaEvidence,
      strong: textTerms.length > 0 || hrefEvidence || dataEvidence || ariaEvidence
    };
  }

  function commerceElementScore(element, text) {
    let score = 0;
    if (text.includes("购物")) score += 30;
    if (text.includes("视频同款")) score += 26;
    if (text.includes("查看详情")) score += 22;
    if (text.includes("立即购买") || text.includes("去购买")) score += 20;
    if (text.includes("小黄车")) score += 18;
    if (text.includes("优品") || text.includes("抢购")) score += 10;

    const evidence = getCommerceEvidence(element, text);
    if (evidence.hrefEvidence) score += 20;
    if (evidence.dataEvidence) score += 18;
    if (evidence.ariaEvidence) score += 14;
    if (element.matches?.("a,button,[role='button']")) score += 4;
    return score;
  }

  function findCommerceEntries(container) {
    if (!(container instanceof Element)) return [];

    const descElement = container.querySelector(SELECTORS.videoDesc);
    const authorElement = container.querySelector(SELECTORS.accountName);
    const seen = new Set();
    const entries = [];

    const localCandidates = Array.from(container.querySelectorAll(
      "a,button,[role='button'],[data-e2e],span,div"
    ));

    const containerRect = container.getBoundingClientRect();
    const spatialCandidates = Array.from(document.querySelectorAll(
      "a,button,[role='button'],[data-e2e]"
    )).filter(element => {
      if (container.contains(element)) return false;
      if (!isElementInViewport(element)) return false;

      const rect = element.getBoundingClientRect();
      const centerX = rect.left + rect.width / 2;
      const centerY = rect.top + rect.height / 2;
      return centerX >= containerRect.left &&
        centerX <= containerRect.right &&
        centerY >= containerRect.top &&
        centerY <= containerRect.bottom;
    });

    const candidates = Array.from(new Set([
      ...localCandidates,
      ...spatialCandidates
    ]));

    for (const candidate of candidates) {
      if (!isElementInViewport(candidate)) continue;
      if (descElement?.contains(candidate) || authorElement?.contains(candidate)) continue;
      if (descElement && candidate.contains(descElement)) continue;
      if (authorElement && candidate.contains(authorElement)) continue;

      const ownText = normalizeText(candidate.textContent);
      const evidence = getCommerceEvidence(candidate, ownText);

      if (!evidence.strong) continue;
      if (!ownText && !evidence.hrefEvidence && !evidence.dataEvidence && !evidence.ariaEvidence) continue;
      if (ownText.length > 140) continue;

      let element = candidate.closest?.("a,button,[role='button']") || candidate;
      if (!container.contains(element)) element = candidate;
      if (!isElementInViewport(element)) continue;

      const text = normalizeText(element.textContent || ownText);
      const key = text + "::" + String(element.getAttribute?.("href") || "");
      if (seen.has(key)) continue;
      seen.add(key);

      entries.push({
        element,
        text: text || ownText || "商品入口",
        score: commerceElementScore(element, text || ownText)
      });
    }

    return entries
      .sort((a, b) => b.score - a.score || a.text.length - b.text.length)
      .slice(0, 6);
  }

  function getPrimaryCommerceEntry(container) {
    return findCommerceEntries(container)[0] || null;
  }

  function findAdBadge(container) {
    if (!(container instanceof Element)) return null;

    const descElement = container.querySelector(SELECTORS.videoDesc);
    const authorElement = container.querySelector(SELECTORS.accountName) ||
      findLiveAuthorElement?.(container) ||
      null;
    const videoInfo = container.querySelector('[data-e2e="video-info"]');

    const candidates = Array.from(container.querySelectorAll(
      '[data-e2e],span,div,a'
    )).filter(element => {
      if (!isElementInViewport(element)) return false;
      if (normalizeText(element.textContent) !== "广告") return false;
      if (descElement?.contains(element) || element.contains(descElement)) return false;
      if (authorElement?.contains(element)) return false;
      return true;
    });

    const structured = candidates.find(element => {
      const marker = [
        element.getAttribute?.("data-e2e"),
        element.getAttribute?.("aria-label"),
        element.getAttribute?.("role")
      ].map(value => String(value || "").toLowerCase()).join(" ");
      return /(?:^|[-_\s])(ad|advert|advertise|sponsor|promotion)(?:[-_\s]|$)/.test(marker);
    });
    if (structured) return structured;

    if (videoInfo) {
      const inVideoInfo = candidates.find(element => videoInfo.contains(element));
      if (inVideoInfo) return inVideoInfo;
    }

    if (authorElement) {
      const authorRect = authorElement.getBoundingClientRect();
      return candidates.find(element => {
        const rect = element.getBoundingClientRect();
        const sameRow =
          Math.abs((rect.top + rect.bottom) / 2 - (authorRect.top + authorRect.bottom) / 2) <= 24;
        const nearby =
          rect.left >= authorRect.left - 12 &&
          rect.left <= authorRect.right + 140;
        return sameRow && nearby;
      }) || null;
    }

    return null;
  }

  function collectVisibleTextFragments(container) {
    if (!(container instanceof Element)) return [];

    return Array.from(container.querySelectorAll(
      "[data-e2e],a,p,span,div"
    )).filter(element => {
      if (!isElementInViewport(element)) return false;
      const text = normalizeText(element.textContent);
      if (!text || text.length > 180) return false;

      return !Array.from(element.children || []).some(child =>
        isElementInViewport(child) &&
        normalizeText(child.textContent) === text
      );
    });
  }

  function findLiveAuthorElement(container) {
    if (!(container instanceof Element)) return null;

    const preferred = Array.from(container.querySelectorAll(
      '[data-e2e*="nickname"],[data-e2e*="author"],[data-e2e*="anchor"],[data-e2e*="user-name"]'
    )).find(element => {
      if (!isElementInViewport(element)) return false;
      const text = normalizeText(element.textContent);
      return text.startsWith("@") && text.length <= 80;
    });
    if (preferred) return preferred;

    return collectVisibleTextFragments(container)
      .filter(element => {
        const text = normalizeText(element.textContent);
        return text.startsWith("@") &&
          text.length >= 2 &&
          text.length <= 80 &&
          !text.includes("抖音号");
      })
      .sort((a, b) => {
        const ar = a.getBoundingClientRect();
        const br = b.getBoundingClientRect();
        return br.top - ar.top || ar.left - br.left;
      })[0] || null;
  }

  function isLikelyLiveDescriptionText(text) {
    if (!text || text.length < 2 || text.length > 180) return false;
    if (text.startsWith("@")) return false;
    if (/点击.*进入直播间|进入直播间|直播中|正在直播/.test(text)) return false;
    if (/点赞|评论|分享|关注|连播|清屏|倍速|全屏|小窗模式/.test(text)) return false;
    return true;
  }

  function findLiveDescriptionElement(container, authorElement) {
    if (!(container instanceof Element)) return null;

    const preferred = Array.from(container.querySelectorAll(
      '[data-e2e*="desc"],[data-e2e*="intro"],[data-e2e*="title"],[data-e2e*="room-name"]'
    )).find(element =>
      isElementInViewport(element) &&
      isLikelyLiveDescriptionText(normalizeText(element.textContent))
    );
    if (preferred) return preferred;

    const fragments = collectVisibleTextFragments(container)
      .filter(element =>
        isLikelyLiveDescriptionText(normalizeText(element.textContent))
      );

    const hashtagCandidate = fragments.find(element =>
      normalizeText(element.textContent).includes("#")
    );
    if (hashtagCandidate) return hashtagCandidate;

    if (authorElement) {
      const authorRect = authorElement.getBoundingClientRect();
      const nearby = fragments
        .map(element => ({
          element,
          rect: element.getBoundingClientRect(),
          text: normalizeText(element.textContent)
        }))
        .filter(item =>
          item.rect.top >= authorRect.top - 8 &&
          item.rect.top <= authorRect.bottom + 140 &&
          item.rect.left <= authorRect.right + 420 &&
          item.rect.right >= authorRect.left - 40
        )
        .sort((a, b) =>
          Math.abs(a.rect.top - authorRect.bottom) -
          Math.abs(b.rect.top - authorRect.bottom)
        )[0];

      if (nearby) return nearby.element;
    }

    return fragments[0] || null;
  }

  function extractLiveMeta(container, evidence) {
    const authorElement = findLiveAuthorElement(container);
    const descElement = findLiveDescriptionElement(container, authorElement);

    const author = normalizeText(authorElement?.textContent);
    const desc = normalizeText(descElement?.textContent);
    const tags = Array.from(new Set(
      Array.from(desc.matchAll(/#([^#\s]+)/g))
        .map(match => normalizeText(match[1]))
        .filter(Boolean)
    ));

    const title = normalizeText(
      (desc.split("#")[0] || desc).replace(/\s+/g, " ")
    );

    const commerceEntries = findCommerceEntries(container);
    const commerceTexts = Array.from(new Set(
      commerceEntries.map(entry => normalizeText(entry.text)).filter(Boolean)
    ));
    const adBadge = findAdBadge(container);

    return {
      contentType: "live",
      author,
      title,
      tags,
      desc,
      live: true,
      liveStatusText: evidence?.statusText || "直播中",
      livePromptText: evidence?.enterText || "",
      adDetected: Boolean(adBadge),
      adText: normalizeText(adBadge?.textContent),
      commerceDetected: commerceEntries.length > 0,
      commerceText: commerceTexts.join(" · "),
      commerceTexts
    };
  }

  function extractMeta(container) {
    if (!(container instanceof Element)) return null;

    const liveEvidence = getStrongLiveEvidence(container);
    if (liveEvidence.strong) {
      return extractLiveMeta(container, liveEvidence);
    }

    const author = normalizeText(container.querySelector(SELECTORS.accountName)?.textContent);
    const descElement = container.querySelector(SELECTORS.videoDesc);
    const desc = normalizeText(descElement?.textContent);
    const rawDesc = String(descElement?.innerText || descElement?.textContent || "");

    const rawTags = Array.from(desc.matchAll(/#([^#\s]+)/g)).map(match => normalizeText(match[1]));
    const tags = Array.from(new Set(rawTags.filter(Boolean)));
    const commerceEntries = findCommerceEntries(container);
    const commerceTexts = Array.from(new Set(
      commerceEntries.map(entry => normalizeText(entry.text)).filter(Boolean)
    ));
    const adBadge = findAdBadge(container);

    const firstLine = normalizeText(rawDesc.split(/\r?\n+/)[0]);
    const titleSource = firstLine || desc;
    const title = normalizeText(titleSource.replace(/#[^#\s]+/g, " "));

    return {
      contentType: "video",
      author,
      title,
      tags,
      desc,
      live: false,
      liveStatusText: "",
      livePromptText: "",
      adDetected: Boolean(adBadge),
      adText: normalizeText(adBadge?.textContent),
      commerceDetected: commerceEntries.length > 0,
      commerceText: commerceTexts.join(" · "),
      commerceTexts
    };
  }

  function getIdentity(container, meta = null) {
    if (!(container instanceof Element)) return "";
    const link = container.querySelector('a[href*="/video/"], a[href*="/note/"], a[href*="/root/live/"]');
    const href = link?.href || "";
    if (href) return href;

    const video = container.querySelector(SELECTORS.video);
    const src = video?.currentSrc || video?.src || "";
    if (src) return src;

    const value = meta || extractMeta(container);
    return [value?.author || "", value?.title || "", value?.tags?.join("|") || "", value?.live ? "live" : ""].join("::");
  }

  function scopeEnabled(rule, scope) {
    const scopes = Array.isArray(rule.scopes) && rule.scopes.length
      ? rule.scopes
      : ["title", "tags", "author", "commerce"];

    if (scopes.includes(scope)) return true;

    // Rules created before the commerce field existed used exactly the original
    // title/tags/author trio. Treat that legacy trio as "all content fields".
    if (scope === "commerce" &&
        scopes.includes("title") &&
        scopes.includes("tags") &&
        scopes.includes("author")) {
      return true;
    }

    return false;
  }

  function findMatch(meta) {
    if (!meta) return null;

    for (const rule of rules) {
      if (rule?.enabled === false || rule?.type !== "keyword") continue;
      const keyword = normalizeText(rule.value || rule.label).toLowerCase();
      if (!keyword) continue;

      if (keyword === "广告" && scopeEnabled(rule, "ad") && meta.adDetected === true) {
        return {
          rule,
          scope: "广告标签",
          scopeKey: "ad",
          value: meta.adText || "广告",
          semantic: false
        };
      }

      if (keyword === "直播" && meta.live === true) {
        return {
          rule,
          scope: "直播类型",
          scopeKey: "live",
          value: meta.liveStatusText || "直播中",
          semantic: false
        };
      }

      if (scopeEnabled(rule, "title") && meta.title.toLowerCase().includes(keyword)) {
        return { rule, scope: "标题", scopeKey: "title", value: meta.title };
      }
      if (scopeEnabled(rule, "tags") && meta.tags.some(tag => tag.toLowerCase().includes(keyword))) {
        return { rule, scope: "标签", scopeKey: "tags", value: meta.tags.join(" #") };
      }
      if (scopeEnabled(rule, "author") && meta.author.toLowerCase().includes(keyword)) {
        return { rule, scope: "作者", scopeKey: "author", value: meta.author };
      }
      if (scopeEnabled(rule, "commerce") &&
          meta.commerceTexts.some(text => text.toLowerCase().includes(keyword))) {
        return {
          rule,
          scope: "商品",
          scopeKey: "commerce",
          value: meta.commerceText,
          semantic: false
        };
      }

      // "购物" is also a category rule: a real product card is sufficient evidence
      // even when Douyin renders "查看详情" instead of the literal word "购物".
      if (scopeEnabled(rule, "commerce") &&
          (keyword === "购物" || keyword === "商品") &&
          meta.commerceDetected) {
        return {
          rule,
          scope: "商品卡",
          scopeKey: "commerce",
          value: meta.commerceText,
          semantic: true
        };
      }
    }

    return null;
  }

  function clearMatchHighlight() {
    try {
      CSS.highlights?.delete("yanzi-douyin-match");
    } catch {}
    document.querySelectorAll(".yanzi-douyin-filter-commerce-hit")
      .forEach(element => element.classList.remove("yanzi-douyin-filter-commerce-hit"));
  }

  function createKeywordRanges(element, keyword, scopeKey) {
    if (!(element instanceof Element) || !keyword) return [];

    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    const nodes = [];
    let fullText = "";
    let node = walker.nextNode();

    while (node) {
      const value = node.nodeValue || "";
      nodes.push({
        node,
        start: fullText.length,
        end: fullText.length + value.length
      });
      fullText += value;
      node = walker.nextNode();
    }

    const lowerText = fullText.toLowerCase();
    const lowerKeyword = keyword.toLowerCase();
    const firstHash = fullText.indexOf("#");
    const ranges = [];
    let from = 0;

    while (from < lowerText.length) {
      const index = lowerText.indexOf(lowerKeyword, from);
      if (index < 0) break;

      let allowed = true;
      if (scopeKey === "title" && firstHash >= 0) {
        allowed = index < firstHash;
      } else if (scopeKey === "tags") {
        const lastHash = fullText.lastIndexOf("#", index);
        const lastSpace = Math.max(
          fullText.lastIndexOf(" ", index),
          fullText.lastIndexOf("\n", index),
          fullText.lastIndexOf("\t", index)
        );
        allowed = lastHash > lastSpace;
      }

      if (allowed) {
        const endIndex = index + keyword.length;
        const startPart = nodes.find(part => index >= part.start && index < part.end);
        const endPart = nodes.find(part => endIndex > part.start && endIndex <= part.end);

        if (startPart && endPart) {
          const range = document.createRange();
          range.setStart(startPart.node, index - startPart.start);
          range.setEnd(endPart.node, endIndex - endPart.start);
          ranges.push(range);
        }
      }

      from = index + Math.max(1, keyword.length);
    }

    return ranges;
  }

  function applyMatchHighlight(container, match) {
    clearMatchHighlight();
    if (!(container instanceof Element) || !match) return;

    const keyword = normalizeText(match.rule.value || match.rule.label);
    if (!keyword) return;

    let target = null;
    if (match.scopeKey === "ad") {
      target = findAdBadge(container);
    } else if (match.scopeKey === "live") {
      const evidence = getStrongLiveEvidence(container);
      target = evidence.statusElement || evidence.enterElement;
    } else if (match.scopeKey === "author") {
      target = container.querySelector(SELECTORS.accountName) ||
        findLiveAuthorElement(container);
    } else if (match.scopeKey === "commerce") {
      const commerce = getPrimaryCommerceEntry(container);
      target = commerce?.element || null;

      // Semantic commerce matching means the product card is the evidence, even
      // when Douyin does not render the literal word "购物".
      if (target && match.semantic) {
        target.classList.add("yanzi-douyin-filter-commerce-hit");
        return;
      }
    } else {
      target = container.querySelector(SELECTORS.videoDesc) ||
        findLiveDescriptionElement(container, findLiveAuthorElement(container));
    }
    if (!target) return;

    const ranges = createKeywordRanges(target, keyword, match.scopeKey);
    if (!ranges.length || typeof Highlight !== "function" || !CSS.highlights) {
      if (match.scopeKey === "commerce") {
        target.classList.add("yanzi-douyin-filter-commerce-hit");
      }
      return;
    }

    try {
      CSS.highlights.set("yanzi-douyin-match", new Highlight(...ranges));
    } catch {
      if (match.scopeKey === "commerce") {
        target.classList.add("yanzi-douyin-filter-commerce-hit");
      }
    }
  }

  function clampPanelPosition(control, left, top) {
    const rect = control.getBoundingClientRect();
    const margin = 8;
    const maxLeft = Math.max(margin, window.innerWidth - rect.width - margin);
    const maxTop = Math.max(margin, window.innerHeight - rect.height - margin);
    return {
      left: Math.max(margin, Math.min(Number(left) || margin, maxLeft)),
      top: Math.max(margin, Math.min(Number(top) || margin, maxTop))
    };
  }

  function applyPanelPosition(control, left, top, persist = false) {
    const next = clampPanelPosition(control, left, top);
    control.style.setProperty("left", `${Math.round(next.left)}px`, "important");
    control.style.setProperty("top", `${Math.round(next.top)}px`, "important");
    control.style.setProperty("bottom", "auto", "important");
    control.style.setProperty("right", "auto", "important");

    if (persist) {
      try {
        localStorage.setItem(PANEL_POSITION_KEY, JSON.stringify(next));
      } catch {}
    }
  }

  function restorePanelPosition(control) {
    if (control.dataset.yanziPositionReady === "1") return;
    control.dataset.yanziPositionReady = "1";

    try {
      const saved = JSON.parse(localStorage.getItem(PANEL_POSITION_KEY) || "null");
      if (saved && Number.isFinite(Number(saved.left)) && Number.isFinite(Number(saved.top))) {
        requestAnimationFrame(() => {
          applyPanelPosition(control, Number(saved.left), Number(saved.top), false);
        });
      }
    } catch {}
  }

  function resetPanelPosition(control) {
    try {
      localStorage.removeItem(PANEL_POSITION_KEY);
    } catch {}
    control.style.removeProperty("left");
    control.style.removeProperty("top");
    control.style.removeProperty("bottom");
    control.style.removeProperty("right");
  }

  function setPanelCollapsed(control, collapsed, persist = true) {
    control.dataset.collapsed = collapsed ? "1" : "0";
    const indicator = control.querySelector(".yanzi-douyin-filter-expand-indicator");
    if (indicator) {
      indicator.setAttribute("aria-label", collapsed ? "展开观察详情" : "折叠观察详情");
      indicator.title = collapsed ? "点击展开" : "点击折叠";
    }

    if (persist) {
      try {
        localStorage.setItem(PANEL_COLLAPSED_KEY, collapsed ? "1" : "0");
      } catch {}
    }

    requestAnimationFrame(() => {
      const rect = control.getBoundingClientRect();
      const hasCustomPosition = control.style.getPropertyValue("top");
      if (hasCustomPosition) {
        applyPanelPosition(control, rect.left, rect.top, true);
      }
    });
  }

  function restorePanelCollapsed(control) {
    if (control.dataset.yanziCollapsedReady === "1") return;
    control.dataset.yanziCollapsedReady = "1";

    let collapsed = true;
    try {
      const saved = localStorage.getItem(PANEL_COLLAPSED_KEY);
      if (saved === "0") collapsed = false;
      if (saved === "1") collapsed = true;
    } catch {}

    setPanelCollapsed(control, collapsed, false);
  }

  function togglePanelCollapsed(control) {
    setPanelCollapsed(control, control.dataset.collapsed !== "1");
  }

  function enablePanelDragging(control) {
    const head = control.querySelector(".yanzi-douyin-filter-control-head");
    if (!head || head.dataset.yanziDragReady === "1") return;
    head.dataset.yanziDragReady = "1";

    let drag = null;

    head.addEventListener("pointerdown", event => {
      if (event.button !== 0 || event.target.closest(".yanzi-douyin-filter-toggle")) return;

      const rect = control.getBoundingClientRect();
      drag = {
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        offsetX: event.clientX - rect.left,
        offsetY: event.clientY - rect.top,
        moved: false
      };

      try {
        head.setPointerCapture(event.pointerId);
      } catch {}
      event.preventDefault();
      event.stopPropagation();
    }, true);

    head.addEventListener("pointermove", event => {
      if (!drag || event.pointerId !== drag.pointerId) return;

      const distance = Math.hypot(
        event.clientX - drag.startX,
        event.clientY - drag.startY
      );
      if (!drag.moved && distance < 5) return;
      drag.moved = true;

      applyPanelPosition(
        control,
        event.clientX - drag.offsetX,
        event.clientY - drag.offsetY,
        false
      );
      event.preventDefault();
    }, true);

    const finishDrag = event => {
      if (!drag || event.pointerId !== drag.pointerId) return;

      const moved = drag.moved;
      if (moved) {
        const rect = control.getBoundingClientRect();
        applyPanelPosition(control, rect.left, rect.top, true);
      }

      try {
        head.releasePointerCapture(event.pointerId);
      } catch {}
      drag = null;

      if (!moved && event.type === "pointerup") {
        togglePanelCollapsed(control);
      }

      event.preventDefault();
    };

    head.addEventListener("pointerup", finishDrag, true);
    head.addEventListener("pointercancel", finishDrag, true);

    head.addEventListener("dblclick", event => {
      if (event.target.closest(".yanzi-douyin-filter-toggle")) return;
      resetPanelPosition(control);
      event.preventDefault();
      event.stopPropagation();
    }, true);

    window.addEventListener("resize", () => {
      if (!control.isConnected) return;
      const rect = control.getBoundingClientRect();
      const hasCustomPosition = control.style.getPropertyValue("top");
      if (hasCustomPosition) {
        applyPanelPosition(control, rect.left, rect.top, true);
      }
    }, { passive: true });
  }

  function findSidebarUtilityStack() {
    const panelMenu = document.getElementById("panel-menu");
    const feedback = document.getElementById("btn-feelgood");

    if (feedback?.parentElement && panelMenu?.contains(feedback.parentElement)) {
      return feedback.parentElement;
    }

    if (panelMenu) {
      const candidates = Array.from(panelMenu.querySelectorAll("div"))
        .map(element => ({
          element,
          rect: element.getBoundingClientRect(),
          children: Array.from(element.children)
        }))
        .filter(item =>
          item.rect.width > 0 &&
          item.rect.width <= 72 &&
          item.rect.height >= 100 &&
          item.children.filter(child => {
            const rect = child.getBoundingClientRect();
            return rect.width >= 28 && rect.width <= 40 &&
              rect.height >= 28 && rect.height <= 40;
          }).length >= 3
        )
        .sort((a, b) => a.rect.width - b.rect.width);

      if (candidates.length) return candidates[0].element;
    }

    return null;
  }

  function updateSidebarSwitch() {
    const button = document.querySelector(".yanzi-douyin-sidebar-switch");
    if (!button) return;
    button.setAttribute("aria-checked", filteringEnabled ? "true" : "false");
    button.title = filteringEnabled ? "抖音屏蔽已启用" : "抖音屏蔽已关闭";
  }

  async function setFilteringEnabled(nextEnabled, persist = true) {
    filteringEnabled = Boolean(nextEnabled);

    if (!filteringEnabled) {
      clearPendingSkip();
      finishSkip();
    }

    updateSidebarSwitch();

    const control = document.querySelector(".yanzi-douyin-filter-control");
    if (control) {
      updateControl();
    }

    if (persist) {
      await chrome.runtime.sendMessage({
        type: "yanzi_webapp_storage_set",
        appId: APP_ID,
        key: SETTINGS_KEY,
        value: {
          filteringEnabled,
          confirmedAutoSkip: true
        }
      }).catch(() => {});
    }

    lastEvaluatedIdentity = "";
    evaluateCurrent();
  }

  function ensureSidebarSwitch() {
    const stack = findSidebarUtilityStack();
    if (!stack) return null;

    let slot = document.querySelector(".yanzi-douyin-sidebar-switch-slot");
    if (slot && slot.parentElement !== stack) {
      slot.remove();
      slot = null;
    }

    if (!slot) {
      slot = document.createElement("div");
      slot.className = "yanzi-douyin-sidebar-switch-slot";
      slot.innerHTML = `
        <button
          type="button"
          class="yanzi-douyin-sidebar-switch"
          role="switch"
          aria-checked="false"
          aria-label="抖音屏蔽开关"
        >
          <span class="yanzi-douyin-sidebar-switch-label">屏蔽</span>
          <span class="yanzi-douyin-sidebar-switch-track" aria-hidden="true">
            <span class="yanzi-douyin-sidebar-switch-thumb"></span>
          </span>
        </button>
      `;

      const switchButton = slot.querySelector(".yanzi-douyin-sidebar-switch");
      switchButton.addEventListener("click", event => {
        event.preventDefault();
        event.stopPropagation();
        void setFilteringEnabled(!filteringEnabled, true);
      }, true);

      const settingsAnchor = stack.firstElementChild;
      stack.insertBefore(slot, settingsAnchor || null);
    }

    updateSidebarSwitch();
    return slot;
  }

  function startSidebarLoop() {
    if (sidebarTimer) return;
    ensureSidebarSwitch();
    sidebarTimer = setInterval(ensureSidebarSwitch, 1000);
  }

  function stopSidebarLoop() {
    if (sidebarTimer) {
      clearInterval(sidebarTimer);
      sidebarTimer = null;
    }
    document.querySelector(".yanzi-douyin-sidebar-switch-slot")?.remove();
  }

  function setupControlInteraction(control) {
    restorePanelPosition(control);
    restorePanelCollapsed(control);
    enablePanelDragging(control);
  }

  function ensureControl() {
    let control = document.querySelector(".yanzi-douyin-filter-control");
    if (control) {
      setupControlInteraction(control);
      return control;
    }

    control = document.createElement("div");
    control.className = "yanzi-douyin-filter-control";
    control.innerHTML = `
      <div class="yanzi-douyin-filter-control-head">
        <div class="yanzi-douyin-filter-control-state">
          <span class="yanzi-douyin-filter-expand-indicator" aria-label="展开观察详情" title="点击展开">▾</span>
          <span class="yanzi-douyin-filter-control-dot"></span>
          <span class="yanzi-douyin-filter-control-label">观察模式</span>
        </div>
      </div>
      <div class="yanzi-douyin-filter-body">
      <div class="yanzi-douyin-filter-meta">
        <div class="yanzi-douyin-filter-meta-row">
          <span class="yanzi-douyin-filter-meta-key">类型</span>
          <span class="yanzi-douyin-filter-meta-value" data-meta-field="type"></span>
        </div>
        <div class="yanzi-douyin-filter-meta-row">
          <span class="yanzi-douyin-filter-meta-key">作者</span>
          <span class="yanzi-douyin-filter-meta-value" data-meta-field="author"></span>
        </div>
        <div class="yanzi-douyin-filter-meta-row">
          <span class="yanzi-douyin-filter-meta-key">标题</span>
          <span class="yanzi-douyin-filter-meta-value" data-meta-field="title"></span>
        </div>
        <div class="yanzi-douyin-filter-meta-row">
          <span class="yanzi-douyin-filter-meta-key">标签</span>
          <span class="yanzi-douyin-filter-meta-value" data-meta-field="tags"></span>
        </div>
        <div class="yanzi-douyin-filter-meta-row">
          <span class="yanzi-douyin-filter-meta-key">商品</span>
          <span class="yanzi-douyin-filter-meta-value" data-meta-field="commerce"></span>
        </div>
      </div>
      <div class="yanzi-douyin-filter-match"></div>
      </div>
    `;

    document.documentElement.appendChild(control);
    setupControlInteraction(control);
    updateControl();
    return control;
  }

  function renderMetaValue(element, value, shouldHighlight, keyword) {
    if (!element) return;
    element.replaceChildren();

    if (!shouldHighlight || !keyword) {
      element.textContent = value;
      return;
    }

    const lowerValue = value.toLowerCase();
    const lowerKeyword = keyword.toLowerCase();
    let cursor = 0;
    let found = false;

    while (cursor < value.length) {
      const index = lowerValue.indexOf(lowerKeyword, cursor);
      if (index < 0) break;

      if (index > cursor) {
        element.appendChild(document.createTextNode(value.slice(cursor, index)));
      }

      const mark = document.createElement("span");
      mark.className = "yanzi-douyin-filter-meta-match";
      mark.textContent = value.slice(index, index + keyword.length);
      element.appendChild(mark);
      found = true;
      cursor = index + keyword.length;
    }

    if (!found) {
      element.textContent = value;
      return;
    }

    if (cursor < value.length) {
      element.appendChild(document.createTextNode(value.slice(cursor)));
    }
  }

  function updateControl(match = currentMatch, meta = currentMeta) {
    const control = ensureControl();
    control.dataset.enabled = filteringEnabled ? "1" : "0";

    const label = control.querySelector(".yanzi-douyin-filter-control-label");
    const matchEl = control.querySelector(".yanzi-douyin-filter-match");

    label.textContent = filteringEnabled ? "屏蔽已启用" : "观察模式";
    updateSidebarSwitch();

    const values = {
      type: meta ? (meta.live ? "直播" : meta.adDetected ? "广告" : "视频") : "未识别",
      author: meta?.author || "未识别",
      title: meta?.title || "未识别",
      tags: meta?.tags?.length ? meta.tags.map(tag => "#" + tag).join(" ") : "未发现",
      commerce: meta?.commerceDetected
        ? (meta.commerceText || "检测到商品入口")
        : "未发现"
    };

    const keyword = match
      ? normalizeText(match.rule.label || match.rule.value)
      : "";
    const matchedField = match?.scopeKey || "";

    for (const [field, value] of Object.entries(values)) {
      const element = control.querySelector(`[data-meta-field="${field}"]`);
      if (!element) continue;

      const shouldHighlight =
        field === matchedField ||
        (field === "type" && (matchedField === "live" || matchedField === "ad"));

      renderMetaValue(element, value, shouldHighlight, keyword);
      element.dataset.empty = value === "未识别" || value === "未发现" ? "1" : "0";
      element.title = value;
    }

    if (!match) {
      matchEl.dataset.visible = "0";
      matchEl.textContent = "";
      return;
    }

    matchEl.dataset.visible = "1";
    matchEl.innerHTML = `命中：<strong>${keyword.replace(/[&<>"]/g, ch => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      '"': "&quot;"
    }[ch]))}</strong> · ${match.scope}${filteringEnabled ? " · 即将跳过" : ""}`;
  }

  function clearPendingSkip() {
    if (pendingSkipTimer) {
      clearTimeout(pendingSkipTimer);
      pendingSkipTimer = null;
    }
  }

  function scheduleSkip(identity, match) {
    clearPendingSkip();
    if (!filteringEnabled || !identity || !match || skipInFlight) return;

    const container = getActiveContainer();
    const meta = extractMeta(container);
    const nowIdentity = getIdentity(container, meta);
    const nowMatch = findMatch(meta);
    if (!nowIdentity || nowIdentity !== identity || !nowMatch) return;

    startSkip(identity, nowMatch);
  }

  function dispatchArrowDown() {
    if (!document.body) return;
    const init = {
      key: "ArrowDown",
      code: "ArrowDown",
      keyCode: 40,
      which: 40,
      bubbles: true,
      cancelable: true
    };
    document.body.dispatchEvent(new KeyboardEvent("keydown", init));
    document.body.dispatchEvent(new KeyboardEvent("keyup", init));
  }

  function clearSkipTimer() {
    if (skipTimer) {
      clearInterval(skipTimer);
      skipTimer = null;
    }
    skipAttempts = 0;
  }

  function finishSkip() {
    clearSkipTimer();
    skipInFlight = false;
    skipFromIdentity = "";
  }

  function startSkip(identity, match) {
    if (!identity || skipInFlight) return;

    skipInFlight = true;
    skipFromIdentity = identity;
    skipAttempts = 0;
    lastMatch = {
      keyword: match.rule.label || match.rule.value || "",
      scope: match.scope,
      at: Date.now()
    };

    showToast(`正在跳过 · ${match.scope}命中“${match.rule.label || match.rule.value}”`);
    dispatchArrowDown();

    clearSkipTimer();
    skipTimer = setInterval(() => {
      if (!enabled || !filteringEnabled) {
        finishSkip();
        return;
      }

      const current = getActiveContainer();
      const currentMeta = extractMeta(current);
      const currentIdentity = getIdentity(current, currentMeta);

      if (currentIdentity && currentIdentity !== skipFromIdentity) {
        finishSkip();
        lastEvaluatedIdentity = "";
        setTimeout(evaluateCurrent, 60);
        return;
      }

      skipAttempts += 1;
      if (skipAttempts >= 5) {
        showToast("跳过失败，请手动切换", 1800);
        finishSkip();
        return;
      }

      dispatchArrowDown();
    }, 450);
  }

  function evaluateCurrent() {
    if (!enabled || skipInFlight) return;

    const container = getActiveContainer();
    if (!container) {
      clearPendingSkip();
      clearMatchHighlight();
      currentMatch = null;
      currentMeta = null;
      currentIdentity = "";
      lastEvaluatedIdentity = "";
      updateControl(null, null);
      return;
    }

    const meta = extractMeta(container);
    const identity = getIdentity(container, meta);
    if (!identity) return;
    currentMeta = meta;

    if (identity !== currentIdentity) {
      clearPendingSkip();
      clearMatchHighlight();
      currentMatch = null;
      currentIdentity = identity;
      lastEvaluatedIdentity = "";
    }

    // Douyin often fills nickname/description after the video node already exists.
    // Include metadata in the signature so late text still gets evaluated.
    const signature = [
      identity,
      meta?.author || "",
      meta?.desc || "",
      meta?.liveStatusText || "",
      meta?.livePromptText || "",
      meta?.adText || "",
      meta?.commerceText || ""
    ].join("::");
    if (signature === lastEvaluatedIdentity) return;
    lastEvaluatedIdentity = signature;

    const match = findMatch(meta);
    currentMatch = match;
    applyMatchHighlight(container, match);
    updateControl(match, meta);

    if (match && filteringEnabled) {
      scheduleSkip(identity, match);
    } else {
      clearPendingSkip();
    }
  }

  function startLoop() {
    if (loopTimer) return;
    loopTimer = setInterval(evaluateCurrent, 260);
  }

  function stopLoop() {
    if (loopTimer) {
      clearInterval(loopTimer);
      loopTimer = null;
    }
    clearPendingSkip();
    finishSkip();
    clearMatchHighlight();
    currentMatch = null;
    currentMeta = null;
    currentIdentity = "";
    lastEvaluatedIdentity = "";
  }

  function normalizeRuleDocument(value) {
    const sourceItems = Array.isArray(value?.items)
      ? value.items
      : DEFAULT_RULES.items;
    const items = sourceItems.map(item => ({
      ...item,
      scopes: Array.isArray(item?.scopes) ? [...item.scopes] : item?.scopes
    }));
    let version = Number(value?.version || 1);
    let migrated = false;

    if (version < 2) {
      const hasAdRule = items.some(rule =>
        rule?.id === "default-ad" ||
        (normalizeText(rule?.value || rule?.label) === "广告" &&
          Array.isArray(rule?.scopes) &&
          rule.scopes.includes("ad"))
      );

      if (!hasAdRule) {
        const defaultAd = DEFAULT_RULES.items.find(rule => rule.id === "default-ad");
        if (defaultAd) {
          items.push({
            ...defaultAd,
            scopes: [...defaultAd.scopes]
          });
        }
      }

      version = 2;
      migrated = true;
    }

    return { version, items, migrated };
  }

  async function persistRuleDocument(documentValue) {
    await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_set",
      appId: APP_ID,
      key: RULES_KEY,
      value: {
        version: documentValue.version,
        items: documentValue.items
      }
    }).catch(() => {});
  }

  async function loadRules() {
    const response = await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_get",
      appId: APP_ID,
      key: RULES_KEY,
      fallback: DEFAULT_RULES
    });
    const documentValue = normalizeRuleDocument(response?.value || DEFAULT_RULES);
    rules = documentValue.items;
    if (documentValue.migrated) {
      void persistRuleDocument(documentValue);
    }
    lastEvaluatedIdentity = "";
    evaluateCurrent();
  }

  async function loadSettings() {
    const response = await chrome.runtime.sendMessage({
      type: "yanzi_webapp_storage_get",
      appId: APP_ID,
      key: SETTINGS_KEY,
      fallback: {
        filteringEnabled: false,
        confirmedAutoSkip: false
      }
    });

    const confirmed = response?.value?.confirmedAutoSkip === true;
    filteringEnabled = confirmed && response?.value?.filteringEnabled !== false;

    clearPendingSkip();
    if (!filteringEnabled) finishSkip();
    lastEvaluatedIdentity = "";
    ensureControl();
    ensureSidebarSwitch();
    updateSidebarSwitch();
    updateControl();
    startLoop();
    startSidebarLoop();
    evaluateCurrent();
  }

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.appId !== APP_ID) return;

    if (message.type === "yanzi_webapp_command") {
      if (message.action === "disable") {
        enabled = false;
        stopLoop();
        stopSidebarLoop();
        document.querySelector(".yanzi-douyin-filter-toast")?.remove();
        document.querySelector(".yanzi-douyin-filter-control")?.remove();
      }
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === RULES_KEY) {
      const documentValue = normalizeRuleDocument(message.value || {});
      rules = documentValue.items;
      if (documentValue.migrated) {
        void persistRuleDocument(documentValue);
      }
      lastEvaluatedIdentity = "";
      evaluateCurrent();
      return;
    }

    if (message.type === "yanzi_webapp_data_changed" && message.key === SETTINGS_KEY) {
      const confirmed = message.value?.confirmedAutoSkip === true;
      filteringEnabled = confirmed && message.value?.filteringEnabled !== false;
      lastEvaluatedIdentity = "";
      clearPendingSkip();
      if (!filteringEnabled) finishSkip();
      startLoop();
      startSidebarLoop();
      ensureSidebarSwitch();
      updateSidebarSwitch();
      updateControl();
      evaluateCurrent();
      return;
    }

    if (message.type === "yanzi_webapp_probe") {
      const container = getActiveContainer();
      const meta = extractMeta(container);
      const identity = getIdentity(container, meta);
      const match = findMatch(meta);

      if (message.debugSkipTest && container && identity) {
        startSkip(identity, {
          rule: { label: "开发测试", value: "开发测试" },
          scope: "测试"
        });

        setTimeout(() => {
          const afterContainer = getActiveContainer();
          const afterMeta = extractMeta(afterContainer);
          const afterIdentity = getIdentity(afterContainer, afterMeta);
          sendResponse({
            ok: true,
            appId: APP_ID,
            version: "0.2.9",
            debugSkipTest: {
              beforeIdentity: identity,
              afterIdentity,
              changed: Boolean(afterIdentity && afterIdentity !== identity),
              afterMeta
            }
          });
        }, 1100);
        return true;
      }

      let debugLiveDom = null;
      if (message.debugLiveDom) {
        const visibleData = Array.from(document.querySelectorAll("[data-e2e]"))
          .filter(isElementInViewport)
          .map(element => {
            const rect = element.getBoundingClientRect();
            return {
              tag: element.tagName,
              dataE2e: element.getAttribute("data-e2e") || "",
              text: normalizeText(element.textContent).slice(0, 220),
              rect: {
                left: Math.round(rect.left),
                top: Math.round(rect.top),
                width: Math.round(rect.width),
                height: Math.round(rect.height)
              }
            };
          })
          .filter(item => item.text || /live|author|nick|desc|title|room|video/i.test(item.dataE2e))
          .slice(0, 120);

        const visibleLiveLinks = Array.from(document.querySelectorAll("a[href]"))
          .filter(element => {
            if (!isElementInViewport(element)) return false;
            const href = String(element.href || "");
            const text = normalizeText(element.textContent);
            return /live\.douyin\.com|\/live\/|\/root\/live\//.test(href) ||
              text.includes("直播") ||
              text.includes("进入直播间");
          })
          .map(element => ({
            href: element.href || "",
            text: normalizeText(element.textContent).slice(0, 220),
            dataE2e: element.getAttribute("data-e2e") || "",
            className: String(element.className || "").slice(0, 180)
          }))
          .slice(0, 60);

        const currentRect = container?.getBoundingClientRect?.();
        debugLiveDom = {
          container: container ? {
            tag: container.tagName,
            dataE2e: container.getAttribute?.("data-e2e") || "",
            className: String(container.className || "").slice(0, 240),
            text: normalizeText(container.textContent).slice(0, 1200),
            rect: currentRect ? {
              left: Math.round(currentRect.left),
              top: Math.round(currentRect.top),
              width: Math.round(currentRect.width),
              height: Math.round(currentRect.height)
            } : null
          } : null,
          visibleData,
          visibleLiveLinks,
          loadedLiveFeedCandidates: Array.from(
            document.querySelectorAll('[data-e2e="feed-item"],[data-e2e="feed-active-video"]')
          ).map(element => {
            const text = normalizeText(element.textContent);
            const evidence = getStrongLiveEvidence(element);
            const rect = element.getBoundingClientRect();
            return {
              strong: evidence.strong,
              statusText: evidence.statusText,
              enterText: evidence.enterText,
              text: text.slice(0, 700),
              dataE2e: element.getAttribute("data-e2e") || "",
              rect: {
                left: Math.round(rect.left),
                top: Math.round(rect.top),
                width: Math.round(rect.width),
                height: Math.round(rect.height)
              },
              meta: evidence.strong ? extractLiveMeta(element, evidence) : null
            };
          }).filter(item => item.strong).slice(0, 20)
        };
      }

      sendResponse({
        ok: true,
        appId: APP_ID,
        version: "0.2.9",
        enabled,
        filteringEnabled,
        url: location.href,
        hasActiveContainer: Boolean(container),
        identity,
        meta,
        ruleCount: rules.length,
        rules: rules.map(rule => ({
          id: rule.id,
          value: rule.value,
          label: rule.label,
          scopes: rule.scopes,
          enabled: rule.enabled !== false
        })),
        match: match ? {
          keyword: match.rule.label || match.rule.value || "",
          scope: match.scope,
          scopeKey: match.scopeKey
        } : null,
        reviewMode: !filteringEnabled,
        controlVisible: Boolean(document.querySelector(".yanzi-douyin-filter-control")),
        highlightActive: Boolean(CSS.highlights?.get("yanzi-douyin-match")),
        skipInFlight,
        skipAttempts,
        lastMatch,
        debugLiveDom,
        panelCollapsed: document.querySelector(".yanzi-douyin-filter-control")?.dataset.collapsed === "1"
      });
      return true;
    }
  });

  ensureStyle();
  ensureControl();
  ensureSidebarSwitch();
  startLoop();
  startSidebarLoop();

  globalThis.__yanziDouyinFilterRefresh = () => {
    enabled = true;
    ensureStyle();
    ensureControl();
    ensureSidebarSwitch();
    startLoop();
    startSidebarLoop();
    void Promise.all([loadRules(), loadSettings()]);
  };

  void Promise.all([loadRules(), loadSettings()]);
})();