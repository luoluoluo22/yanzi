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

// 辅助函数：等待标签页加载就绪
function waitForTabComplete(tabId, timeoutMs = 8000) {
  return new Promise(resolve => {
    let timer = null;
    const onUpdated = (id, changeInfo) => {
      if (id === tabId && changeInfo.status === "complete") {
        cleanup();
        resolve(true);
      }
    };
    const cleanup = () => {
      clearTimeout(timer);
      chrome.tabs.onUpdated.removeListener(onUpdated);
    };
    timer = setTimeout(() => {
      cleanup();
      resolve(false);
    }, timeoutMs);

    chrome.tabs.onUpdated.addListener(onUpdated);

    chrome.tabs.get(tabId).then(tab => {
      if (tab?.status === "complete") {
        cleanup();
        resolve(true);
      }
    }).catch(() => {});
  });
}

// 辅助函数：静默后台临时标签页调度器
// 若已有打开网页则直接复用；若未打开则在后台静默打开 (active: false)，提取完后自动关闭！
async function withEphemeralTab(urlPattern, targetUrl, extractFn, timeoutMs = 7000, preferExisting = true) {
  // Query failures should not disable the read, but an existing-tab extraction
  // failure must NOT create another browser tab behind the user's back.
  let existingTabs = [];
  if (preferExisting) {
    try { existingTabs = await chrome.tabs.query({ url: urlPattern }); }
    catch (error) { console.warn("[Yanzi] creator tab lookup failed:", error?.name || "error"); }
  }
  const reusable = existingTabs.find(tab => !tab.discarded);
  if (reusable) return extractFn(reusable.id, false);

  // This creates an INACTIVE TAB in the existing browser window, NOT a window.
  let tempTab = null, result = null, closed = false;
  try {
    tempTab = await chrome.tabs.create({ url: targetUrl, active: false });
    if (!tempTab?.id) throw new Error("未能创建临时标签页");
    await waitForTabComplete(tempTab.id, timeoutMs);
    await new Promise(resolve => setTimeout(resolve, 600));
    result = await extractFn(tempTab.id, true);
  } finally {
    if (tempTab?.id) {
      try {
        await chrome.tabs.remove(tempTab.id);
        closed = true;
      } catch (error) {
        console.warn("[Yanzi] creator tab cleanup failed:", error?.name || "error");
        if (result && typeof result === "object")
          result.cleanupWarning = "临时后台标签页未能关闭，请在浏览器中检查";
      }
    }
  }
  if (result && typeof result === "object")
    result.temporaryTabClosed = closed;
  return result;
}

// 浏览器内直接读取已登录的会话数据 (无需依赖外部终端 CLI，几毫秒内秒级响应)
async function readDirectFromBrowser(site, operation, page = 1) {
  if (site === "bilibili") {
    return await readBilibiliDirect(operation, page);
  }
  if (site === "douyin") {
    return await readDouyinDirect(operation, page);
  }
  return { ok: false, error: "暂不支持该站点直读" };
}

// 1. B站 Web 接口带 Cookie 直读 (纯原生接口，完全无需开网页)
async function readBilibiliDirect(operation, page = 1) {
  try {
    if (operation === "profile") {
      const res = await fetch("https://api.bilibili.com/x/web-interface/nav", { credentials: "include" });
      const json = await res.json();
      if (json.code === 0 && json.data?.isLogin) {
        return {
          ok: true,
          data: {
            mid: json.data.mid,
            nickname: json.data.uname,
            level: json.data.level_info?.current_level,
            coins: json.data.money,
            vipStatus: json.data.vipStatus === 1 ? "大会员" : "普通用户",
            face: json.data.face
          }
        };
      }
      return { ok: false, error: "未检测到 B 站登录态，请先在当前浏览器登录 bilibili.com" };
    }
    if (operation === "history") {
      const res = await fetch(`https://api.bilibili.com/x/v2/history?pn=${page}&ps=20`, { credentials: "include" });
      const json = await res.json();
      if (json.code === 0 && Array.isArray(json.data)) {
        return {
          ok: true,
          data: json.data.map(item => ({
            id: item.bvid || item.aid,
            title: item.title,
            author: item.owner?.name,
            view_at: new Date(item.view_at * 1000).toLocaleString(),
            progress: item.progress === -1 ? "已看完" : `已看至 ${item.progress}秒`
          }))
        };
      }
      return { ok: false, error: json.message || "获取观看历史失败，请确认是否登录" };
    }
    if (operation === "following") {
      const navRes = await fetch("https://api.bilibili.com/x/web-interface/nav", { credentials: "include" });
      const navJson = await navRes.json();
      if (navJson.code !== 0 || !navJson.data?.mid) {
        return { ok: false, error: "未检测到 B 站登录态，请先登录 bilibili.com" };
      }
      const mid = navJson.data.mid;
      const res = await fetch(`https://api.bilibili.com/x/relation/followings?vmid=${mid}&pn=${page}&ps=20`, { credentials: "include" });
      const json = await res.json();
      if (json.code === 0 && json.data?.list) {
        return {
          ok: true,
          data: json.data.list.map(f => ({
            id: f.mid,
            name: f.uname,
            sign: f.sign,
            face: f.face
          }))
        };
      }
      return { ok: false, error: json.message || "获取关注列表失败" };
    }
  } catch (err) {
    return { ok: false, error: "读取 B 站数据异常: " + (err?.message || err) };
  }
}

// Read creator data in the existing browser profile, not an OpenCLI-owned
// window. Temporary inactive tabs always close in withEphemeralTab's finally.
async function readCreatorDashboard(site, operation, page = 1) {
  const xhs = site === "xiaohongshu";
  const expectedHost = xhs ? "creator.xiaohongshu.com" : "creator.douyin.com";
  const url = xhs ? (operation === "notes" ? "https://creator.xiaohongshu.com/statistics"
                                           : "https://creator.xiaohongshu.com/new/home")
                  : "https://creator.douyin.com/creator-micro/home";
  try {
    return await withEphemeralTab("*://" + expectedHost + "/*", url,
      async (tabId, temporary) => {
        const replies = await chrome.scripting.executeScript({
          target: { tabId },
          world: "MAIN",
          func: async (platform, requested, requestedPage) => {
            const host = platform === "xiaohongshu" ? "creator.xiaohongshu.com" : "creator.douyin.com";
            if (location.hostname !== host)
              return { ok: false, error: "创作者中心未打开，请确认已登录 " + host };
            async function readJson(api) {
              const ctrl = new AbortController();
              const timer = setTimeout(() => ctrl.abort(), 12000);
              try {
                const response = await fetch(api, { credentials: "include", signal: ctrl.signal });
                if (!response.ok) throw Error("HTTP " + response.status);
                const json = await response.json();
                if (!json || typeof json !== "object") throw Error("接口返回格式异常");
                if (Number.isInteger(json.status_code) && json.status_code !== 0)
                  throw Error("接口状态码 " + json.status_code);
                if (Number.isInteger(json.code) && json.code !== 0)
                  throw Error("接口状态码 " + json.code);
                return json;
              } finally { clearTimeout(timer); }
            }
            try {
              if (platform === "douyin") {
                // Account-scoped read-only APIs from the OpenCLI Douyin adapter.
                // A homepage nickname alone must never count as logged-in proof.
                const details = await readJson("https://creator.douyin.com/web/api/media/user/info/?aid=1128");
                const user = details.user_info ?? details.user;
                if (!user || typeof user.nickname !== "string" || !user.nickname.trim())
                  throw Error("无法确认抖音创作者登录身份，请先登录 creator.douyin.com");
                if (requested === "profile") {
                  const fields = ["uid","nickname","follower_count","following_count","aweme_count"];
                  const data = Object.fromEntries(fields.filter(k => user[k] !== undefined && user[k] !== null)
                    .map(k => [k, user[k]]));
                  if (!("follower_count" in data) && !("following_count" in data) && !("uid" in data))
                    throw Error("创作者身份字段不完整，拒绝将缺失数据当作零");
                  return { ok: true, data };
                }
                const list = await readJson("https://creator.douyin.com/janus/douyin/creator/pc/work_list?page_size=20&page_num=" + requestedPage + "&status=0");
                const works = list.data?.work_list ?? list.aweme_list;
                if (!Array.isArray(works)) throw Error("作品列表结构异常，不能将缺失数据当作空列表");
                return { ok: true, data: works.slice(0,20).map(v => ({
                  aweme_id: v.aweme_id,
                  title: v.desc ?? "",
                  status: v.status ?? "unknown",
                  play_count: v.statistics?.play_count ?? null,
                  digg_count: v.statistics?.digg_count ?? null,
                  create_time: v.create_time ?? v.public_time ?? null
                })) };
              }
              if (requested === "notes") {
                // Signed XHS analytics responses are generated by its own SPA.
                // Capture only this temporary tab's responses; never modify
                // user-owned tabs or attempt to synthesize x-s signatures.
                const captures = new Map();
                const matchUrl = value => String(value || "").includes("/api/galaxy/creator/datacenter/note/analyze/list");
                const origFetch = window.fetch;
                window.fetch = async function(...args) {
                  const response = await origFetch.apply(this, args);
                  try {
                    const url = typeof args[0] === "string" ? args[0] : args[0]?.url;
                    if (matchUrl(url)) {
                      const parsed = await response.clone().json().catch(() => null);
                      if (parsed) captures.set(url, parsed);
                    }
                  } catch {}
                  return response;
                };
                const origXhr = window.XMLHttpRequest;
                class CapturingXHR extends origXhr {
                  open(method, url, ...args) {
                    this.__yanziCapturedUrl = url;
                    return super.open(method, url, ...args);
                  }
                  constructor() {
                    super();
                    this.addEventListener("load", () => {
                      try {
                        if (matchUrl(this.__yanziCapturedUrl))
                          captures.set(this.__yanziCapturedUrl, JSON.parse(this.responseText));
                      } catch {}
                    });
                  }
                }
                window.XMLHttpRequest = CapturingXHR;
                try {
                  history.pushState({}, "", "/statistics/data-analysis?source=official");
                  window.dispatchEvent(new PopStateEvent("popstate"));
                  for (let i = 0; i < 24 && captures.size === 0; i++)
                    await new Promise(resolve => setTimeout(resolve, 400));
                  if (!captures.size)
                    throw Error("创作者中心未产生笔记分析响应；请确认已登录且页面已加载");
                  const rows = [];
                  let total = null;
                  const seen = new Set();
                  for (const payload of captures.values()) {
                    if (payload.code != null && payload.code !== 0) continue;
                    if (!Array.isArray(payload?.data?.note_infos)) continue;
                    if (Number.isInteger(payload.data.total)) total = payload.data.total;
                    for (const v of payload.data.note_infos) {
                      if (!v.id || seen.has(v.id)) continue;
                      seen.add(v.id);
                      rows.push({
                        id: v.id, title: v.title ?? "",
                        date: v.post_time ?? null,
                        views: v.read_count ?? null,
                        likes: v.like_count ?? null,
                        collects: v.fav_count ?? null,
                        comments: v.comment_count ?? null
                      });
                    }
                  }
                  if (total === null && rows.length === 0)
                    throw Error("创作者中心响应无有效笔记信息");
                  return { ok: true, data: rows.slice(0, 20), total, incomplete: total > rows.length };
                } finally {
                  window.fetch = origFetch;
                  window.XMLHttpRequest = origXhr;
                }
              }
              if (requested === "profile") {
                const json = await readJson("/api/galaxy/creator/home/personal_info");
                const d = json.data, growth = d?.grow_info ?? {};
                if (!d || (!d.name && d.fans_count === undefined))
                  throw Error("小红书创作者信息不存在，请先登录 creator.xiaohongshu.com");
                return { ok: true, data: [
                  { field: "Name", value: d.name ?? "" },
                  { field: "Followers", value: d.fans_count ?? null },
                  { field: "Following", value: d.follow_count ?? null },
                  { field: "Likes & Collects", value: d.faved_count ?? null },
                  { field: "Creator Level", value: growth.level ?? null },
                  { field: "Level Progress", value: growth.fans_count == null ? null : growth.fans_count + "/" + (growth.max_fans_count ?? "?") },
                  { field: "Bio", value: d.personal_desc ?? "" }
                ] };
              }
              if (requested === "stats") {
                const json = await readJson("/api/galaxy/creator/data/note_detail_new");
                const d = json.data?.seven;
                if (!d || typeof d !== "object" || !Object.keys(d).some(k => k.endsWith("_count")))
                  throw Error("近七天统计接口未返回有效数据");
                const metrics = [
                  ["观看数","view_count","view_list"], ["点赞数","like_count","like_list"],
                  ["收藏数","collect_count","collect_list"], ["评论数","comment_count","comment_list"],
                  ["分享数","share_count","share_list"], ["新增粉丝","rise_fans_count","rise_fans_list"]
                ];
                return { ok: true, data: metrics.map(([metric,key,trend]) => ({
                  metric, total: d[key] ?? null,
                  trend: Array.isArray(d[trend]) ? d[trend].map(x => x.count).join(" → ") : "-"
                })) };
              }
              return { ok: false, error: "此操作暂不能在浏览器当前窗口直读" };
            } catch (error) {
              return { ok: false, error: "创作者数据读取失败：" + (error?.message || String(error)) };
            }
          },
          args: [site, operation, page]
        });
        const result = replies?.[0]?.result;
        if (!result || typeof result.ok !== "boolean")
          return { ok: false, error: "创作者页面未返回可信的读取结果" };
        return result.ok ? { ...result, source: "browser-session", temporaryTabClosed: temporary }
                         : result;
      }, 7000, !xhs || operation !== "notes"
    );
  } catch (error) {
    return { ok: false, error: "浏览器直读失败：" + (error?.message || String(error)) };
  }
}

// The old homepage CSS selectors could extract a navigation handle as
// nickname and silently replace every missing account metric with 0.
async function readDouyinDirect(operation, page = 1) {
  return readCreatorDashboard("douyin", operation, page);
}

async function readXiaohongshuDirect(operation) {
  return readCreatorDashboard("xiaohongshu", operation, 1);
}

function sendOpenCliHostRequest(site, operation, page, profile, timeoutMs, mediaArgs = null) {
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
      globalThis.yanziBrowserHost.send({ type: "opencli_request", requestId, site, operation, page, profile, ...(mediaArgs || {}) });
    } catch (error) {
      clearTimeout(timer);
      yanziOpenCliPending.delete(requestId);
      resolve({ ok: false, error: error?.message || "无法发送到燕子主程序" });
    }
  });
}

async function requestYanziOpenCliRead(site, operation, page = 1, profile = "") {
  try {
    validateOpenCliRead(site, operation, page);
  } catch (error) {
    return { ok: false, error: error.message };
  }

  if (site === "xiaohongshu") {
    // Empty profile means use the logged-in browser profile without a new
    // automation window. An explicitly chosen OpenCLI profile uses the legacy
    // managed-bridge path for cross-profile access.
    if (!profile)
      return readXiaohongshuDirect(operation);
    if (!globalThis.yanziBrowserHost?.isConnected())
      return { ok: false, error: "燕子主程序未连接，请先启动燕子。" };
    const capabilities = await sendOpenCliHostRequest(site, "capabilities", 1, "", 5000);
    if (!capabilities.ok) return capabilities;
    return sendOpenCliHostRequest(site, operation, page, profile, 100000);
  }

  // 1. 优先使用浏览器会话直读（支持后台临时标签页自动开合，秒级无感、不卡顿）
  const directResult = await readDirectFromBrowser(site, operation, page);
  if (directResult.ok || site === "douyin") {
    // Douyin is handled by validated creator API calls. Never silently
    // substitute a second, unsupported host path for a failed account read.
    return directResult;
  }

  // 2. 如果直读未命中，且燕子主程序已连接，尝试通过燕子宿主转发，设置 3 秒短超时防止死锁
  if (globalThis.yanziBrowserHost?.isConnected()) {
    const hostPromise = new Promise(resolve => {
      const requestId = crypto.randomUUID();
      const timer = setTimeout(() => {
        yanziOpenCliPending.delete(requestId);
        resolve(null);
      }, 3000);
      yanziOpenCliPending.set(requestId, { resolve, timer });
      try {
        globalThis.yanziBrowserHost.send({
          type: "opencli_request", requestId, site, operation, page
        });
      } catch {
        clearTimeout(timer);
        yanziOpenCliPending.delete(requestId);
        resolve(null);
      }
    });

    const hostRes = await hostPromise;
    if (hostRes && hostRes.ok) {
      return hostRes;
    }
  }

  return directResult;
}

globalThis.yanziMediaReadHost = (site, operation, value, limit, profile = "") =>
  sendOpenCliHostRequest(site, "media." + operation, 1, profile, 95000, { value, limit });
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
