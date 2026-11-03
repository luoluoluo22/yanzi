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
async function withEphemeralTab(urlPattern, targetUrl, extractFn, timeoutMs = 7000) {
  // 1. 优先复用用户已打开的未休眠标签页
  try {
    const existingTabs = await chrome.tabs.query({ url: urlPattern });
    const reusableTab = existingTabs.find(t => !t.discarded);
    if (reusableTab) {
      return await extractFn(reusableTab.id, false);
    }
  } catch {}

  // 2. 用户未打开该网页：扩展在后台静默打开一个临时标签页 (不抢占前台焦点)
  let tempTab = null;
  try {
    tempTab = await chrome.tabs.create({
      url: targetUrl,
      active: false // 关键：静默后台打开，用户前台完全不受打扰
    });

    await waitForTabComplete(tempTab.id, timeoutMs);
    // 稍微等待页面脚本完成水合渲染
    await new Promise(r => setTimeout(r, 600));

    return await extractFn(tempTab.id, true);
  } finally {
    // 3. 提取完成后，自动关闭临时后台标签页，不留垃圾页面
    if (tempTab && tempTab.id) {
      try {
        await chrome.tabs.remove(tempTab.id);
      } catch {}
    }
  }
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

// 3. 抖音读取：自动后台静默打开临时标签页，提取作品或用户信息后自动秒关！
async function readDouyinDirect(operation, page = 1) {
  try {
    return await withEphemeralTab(
      "*://*.douyin.com/*",
      "https://www.douyin.com/",
      async (tabId) => {
        const results = await chrome.scripting.executeScript({
          target: { tabId },
          func: (op) => {
            try {
              if (op === "profile") {
                const name = document.querySelector(".Nu4AIdPl, .account-name, [data-e2e='user-title'], .user-info-name")?.textContent?.trim();
                const desc = document.querySelector(".user-desc, [data-e2e='user-desc']")?.textContent?.trim();
                const follows = document.querySelector("[data-e2e='follow-count']")?.textContent?.trim();
                const fans = document.querySelector("[data-e2e='fans-count']")?.textContent?.trim();
                const likes = document.querySelector("[data-e2e='liked-count']")?.textContent?.trim();
                if (name) {
                  return {
                    nickname: name,
                    desc: desc || "",
                    follows: follows || 0,
                    fans: fans || 0,
                    likes: likes || 0
                  };
                }
              }
              if (op === "videos") {
                const items = Array.from(document.querySelectorAll("ul[data-e2e='user-post-list'] li, .flow-item, .video-card"));
                if (items.length) {
                  return items.slice(0, 20).map(li => ({
                    title: li.querySelector("img")?.alt || li.querySelector(".title")?.textContent?.trim() || "未命名视频",
                    likes: li.querySelector(".like-count, .like")?.textContent?.trim() || "0",
                    link: li.querySelector("a")?.href || ""
                  }));
                }
              }
              if (document.querySelector("[data-e2e='login-panel'], .login-mask")) {
                return { error: "当前浏览器尚未登录抖音账号，请先完成登录" };
              }
              return { error: "未在当前抖音页面中提取到数据" };
            } catch (e) {
              return { error: e.message };
            }
          },
          args: [operation]
        });

        const output = results?.[0]?.result;
        if (output && !output.error) return { ok: true, data: output };
        if (output?.error) return { ok: false, error: output.error };
        return { ok: false, error: "未能提取到抖音数据" };
      }
    );
  } catch (err) {
    return { ok: false, error: "自动读取抖音失败: " + (err?.message || err) };
  }
}

function sendOpenCliHostRequest(site, operation, page, profile, timeoutMs) {
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
      globalThis.yanziBrowserHost.send({ type: "opencli_request", requestId, site, operation, page, profile });
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
    if (!globalThis.yanziBrowserHost?.isConnected())
      return { ok: false, error: "燕子主程序未连接，请先启动燕子。" };
    const capabilities = await sendOpenCliHostRequest(site, "capabilities", 1, "", 5000);
    if (!capabilities.ok) return capabilities;
    return sendOpenCliHostRequest(site, operation, page, profile, 100000);
  }

  // 1. 优先使用浏览器会话直读（支持后台临时标签页自动开合，秒级无感、不卡顿）
  const directResult = await readDirectFromBrowser(site, operation, page);
  if (directResult.ok) {
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
