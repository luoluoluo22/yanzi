export function buildSiteDetailsLines(adapter, siteId) {
  const config = JSON.stringify(adapter.detailSelectors || {});
  const site = JSON.stringify(siteId);
  const detailsFunction = () => {
    const config = __DETAIL_SELECTORS__;
    const clean = value => String(value == null ? "" : value).replace(/\s+/g, " ").trim();
    const firstText = selectors => {
      for (const selector of selectors || []) {
        let element = null;
        try { element = document.querySelector(selector); } catch {}
        if (!element) continue;
        const value = clean(element.getAttribute("content") || element.getAttribute("datetime") || element.getAttribute("title") || element.innerText || element.textContent || "");
        if (value) return value.slice(0, 2000);
      }
      return "";
    };
    const meta = names => {
      for (const name of names) {
        const element = document.querySelector('meta[property="' + name + '"]') || document.querySelector('meta[name="' + name + '"]');
        const value = clean(element && element.content);
        if (value) return value.slice(0, 2000);
      }
      return "";
    };
    const title = firstText(config.title) || meta(["og:title", "twitter:title"]) || clean(document.querySelector("h1")?.innerText) || clean(document.title);
    const description = firstText(config.description) || meta(["og:description", "description", "twitter:description"]);
    const author = firstText(config.author) || meta(["author", "article:author"]);
    const publishedAt = firstText(config.publishedAt) || meta(["article:published_time", "datePublished", "uploadDate", "publish_date", "pubdate"]) || clean(document.querySelector("time[datetime]")?.getAttribute("datetime") || document.querySelector("time")?.innerText);
    const metrics = {};
    const firstMetric = selectors => {
      const value = firstText(selectors);
      return /[0-9０-９]|万|亿|千/i.test(value) ? value : "";
    };
    for (const [key, selectors] of Object.entries(config.engagement || {})) metrics[key] = firstMetric(selectors);
    const tags = [];
    const tagSeen = new Set();
    for (const selector of config.tags || []) {
      let elements = [];
      try { elements = [...document.querySelectorAll(selector)]; } catch {}
      for (const element of elements) {
        const value = clean(element.innerText || element.textContent || element.getAttribute("title"));
        if (!value || value.length > 80 || tagSeen.has(value) || ["更多", "查看全部", "展开", "收起"].includes(value)) continue;
        tagSeen.add(value);
        tags.push(value);
        if (tags.length >= 30) break;
      }
      if (tags.length >= 30) break;
    }
    const validHttp = value => typeof value === "string" && /^https?:\/\//i.test(value);
    const images = [...document.querySelectorAll("img")].map(element => ({
      url: element.currentSrc || element.src || "",
      alt: clean(element.alt || element.getAttribute("title")),
      width: Number(element.naturalWidth || element.width || 0),
      height: Number(element.naturalHeight || element.height || 0)
    })).filter(item => validHttp(item.url)).filter((item, index, list) => list.findIndex(other => other.url === item.url) === index).slice(0, 15);
    const videoSources = [...document.querySelectorAll("video, video source")].map(element => element.currentSrc || element.src || element.getAttribute("src") || "").filter(validHttp).filter((url, index, list) => list.indexOf(url) === index).slice(0, 8);
    const videoPosters = [...document.querySelectorAll("video[poster]")].map(element => element.poster || "").filter(validHttp).filter((url, index, list) => list.indexOf(url) === index).slice(0, 8);
    const structuredData = [];
    const visitStructured = (value, depth) => {
      if (depth > 5 || value == null) return;
      if (Array.isArray(value)) {
        value.slice(0, 20).forEach(item => visitStructured(item, depth + 1));
        return;
      }
      if (typeof value !== "object") return;
      const rawType = value["@type"];
      const type = Array.isArray(rawType) ? rawType.join(", ") : String(rawType || "");
      const authorValue = typeof value.author === "string" ? value.author : (value.author && value.author.name) || "";
      const keywordsValue = Array.isArray(value.keywords) ? value.keywords.join(", ") : value.keywords;
      if (type || value.headline || (value.name && (value.description || value.uploadDate))) {
        structuredData.push({
          type: clean(type),
          name: clean(value.name || value.headline).slice(0, 500),
          description: clean(value.description).slice(0, 1000),
          author: clean(authorValue).slice(0, 300),
          datePublished: clean(value.datePublished || value.uploadDate || value.dateCreated),
          keywords: clean(keywordsValue).slice(0, 500),
          url: typeof value.url === "string" ? value.url : null,
          contentUrl: typeof value.contentUrl === "string" ? value.contentUrl : null,
          thumbnailUrl: typeof value.thumbnailUrl === "string" ? value.thumbnailUrl : (Array.isArray(value.thumbnailUrl) ? value.thumbnailUrl[0] : null)
        });
      }
      Object.values(value).slice(0, 30).forEach(item => visitStructured(item, depth + 1));
    };
    [...document.querySelectorAll('script[type="application/ld+json"]')].slice(0, 5).forEach(script => {
      try { visitStructured(JSON.parse(script.textContent || "null"), 0); } catch {}
    });
    const path = location.pathname || "/";
    const pageType = /\/search|search_result|\/all\b/i.test(path) ? "search" : /\/video\/|\/explore\/|\/discovery\/item|\/note\//i.test(path) ? "content" : "page";
    const textPreview = clean(document.body && document.body.innerText).slice(0, 5000);
    const verificationRequired = /请通过验证|扫码验证身份|身份验证|验证码/.test(textPreview);
    const rateLimited = /请求太频繁|一分钟后再试|访问过于频繁/.test(textPreview);
    const gatewayError = /502 bad gateway|503 service unavailable|504 gateway timeout|403 forbidden|429 too many requests/i.test(textPreview);
    const contentUnavailable = /当前笔记暂时无法浏览|当前内容暂时无法浏览|页面不存在|内容不存在/.test(textPreview);
    const pageState = verificationRequired ? "verification_required" : rateLimited ? "rate_limited" : gatewayError ? "upstream_error" : contentUnavailable ? "content_unavailable" : pageType === "content" ? "content_loaded" : pageType === "search" ? "search_loaded" : "page_loaded";
    const contentAccessible = pageType === "content" && pageState === "content_loaded";
    const warnings = [];
    if (verificationRequired) warnings.push("网站要求完成身份验证；自动化未尝试绕过。");
    if (rateLimited) warnings.push("网站提示请求过于频繁；请等待后由已登录用户正常重试。");
    if (gatewayError) warnings.push("网站返回网关或服务错误，未能读取真实内容。");
    if (contentUnavailable) warnings.push("目标内容暂时无法浏览或不存在。");
    if (pageType !== "content" && !verificationRequired && !rateLimited && !gatewayError && !contentUnavailable) warnings.push("当前不是可识别的内容详情页。");
    const missingFields = [];
    if (!title) missingFields.push("title");
    if (!description) missingFields.push("description");
    if (!author) missingFields.push("author");
    if (!publishedAt) missingFields.push("publishedAt");
    if (!Object.values(metrics).some(Boolean)) missingFields.push("engagementMetrics");
    for (const [key, value] of Object.entries(metrics)) if (!value) missingFields.push("metric:" + key);
    if (!images.length && !videoSources.length && !videoPosters.length) missingFields.push("media");
    return {
      canonicalUrl: document.querySelector('link[rel="canonical"]')?.href || location.href,
      pageType,
      pageState,
      contentAccessible,
      warnings,
      title,
      description,
      author,
      publishedAt,
      metrics,
      tags,
      images,
      videoSources,
      videoPosters,
      structuredData: structuredData.slice(0, 8),
      textPreview,
      capturedAt: new Date().toISOString(),
      extractorVersion: "1.0.1",
      missingFields
    };
  };
  const functionSource = detailsFunction.toString().replace('__DETAIL_SELECTORS__', config);
  return [
    '  const details = await page.evaluate(' + functionSource + ');',
    '  const result = { site: ' + site + ', action: "extract_details", url: await page.url(), title: await page.title(), ...details };'
  ];
}
