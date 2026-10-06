// Dedicated bridge leaves the existing Yanzi connection independent.
let chatGptSocket = null;
let chatGptBusy = false;
const chatGptAssetWaiters = new Map();
const chatGptOperations = new Set(["chatgpt_status", "chatgpt_new_chat", "chatgpt_send", "chatgpt_image", "chatgpt_capture_images", "chatgpt_messages", "chatgpt_close", "chatgpt_cleanup"]);
// Session storage survives worker suspension, but never carries tab ownership across browser restarts.
const managedTabsKey = 'chatgptManagedTabs';
async function inspectChatGptTab(tab) {
  if (tab.active || tab.pinned || tab.discarded || tab.status !== 'complete') return false;
  const [probe] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: () => {
    const input = document.querySelector('#prompt-textarea, main [contenteditable="true"][role="textbox"]');
    return Boolean(input) && !(input.value || input.innerText || '').trim() && !document.querySelector('button[data-testid="stop-button"], main button[aria-label*="停止"], main button[aria-label^="Stop"]');
  } });
  return probe?.result === true;
}

async function waitChatGptTab(tabId, timeout = 30000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const tab = await chrome.tabs.get(tabId);
    if (tab.status === "complete" && !tab.discarded) return tab;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error("ChatGPT 标签页加载超时");
}

function cacheChatGptImageAsset(dataUrl) {
  if (!chatGptSocket || chatGptSocket.readyState !== WebSocket.OPEN) {
    throw new Error('ChatGPT 工作台连接不可用，无法缓存图片');
  }
  if (typeof dataUrl !== 'string' || !dataUrl.startsWith('data:image/')) {
    throw new Error('图片缓存数据无效');
  }

  const requestId = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      chatGptAssetWaiters.delete(requestId);
      reject(new Error('缓存生成图片超时'));
    }, 60000);

    chatGptAssetWaiters.set(requestId, {
      resolve: asset => { clearTimeout(timer); resolve(asset); },
      reject: error => { clearTimeout(timer); reject(error); }
    });

    try {
      chatGptSocket.send(JSON.stringify({
        type: 'asset_upload',
        requestId,
        dataUrl
      }));
    } catch (error) {
      clearTimeout(timer);
      chatGptAssetWaiters.delete(requestId);
      reject(error);
    }
  });
}

async function runChatGptTask(task) {
  task = {
    ...task,
    temporary: task.temporary != null
      ? task.temporary
      : task.action === 'chatgpt_image'
        ? false
        : true
  };
  const result = { type: "task_response", taskId: task.taskId, status: "error", data: null };
  if (chatGptBusy) return { ...result, message: "ChatGPT 正在执行其他任务，请稍后重试" };
  chatGptBusy = true;
  try {
    if (!chatGptOperations.has(task.action)) throw new Error("不支持的 ChatGPT 操作");
    let tabs = await chrome.tabs.query({ url: "https://chatgpt.com/*" });
    let managed = (await chrome.storage.session.get(managedTabsKey))[managedTabsKey] || {};
    managed = Object.fromEntries(Object.entries(managed).filter(([id, record]) => tabs.some(tab => tab.id === Number(id) && tab.url === record.url)));
    const saveManaged = () => chrome.storage.session.set({ [managedTabsKey]: managed });
    await saveManaged();
    const owns = tab => Boolean(managed[tab.id] && managed[tab.id].url === tab.url);
    const canClose = tab => owns(tab) && !tab.active && !tab.pinned;
    if (task.action === "chatgpt_status") {
      return { ...result, status: "success", data: { exists: tabs.length > 0, extensionVersion: chrome.runtime.getManifest().version, tabs: tabs.map(t => ({ tabId: t.id, url: t.url, active: t.active, discarded: t.discarded, managed: owns(t), canClose: canClose(t) })) } };
    }
    if (task.tabId != null && !tabs.some(t => t.id === task.tabId)) throw new Error("指定标签页不是已打开的 ChatGPT 标签页");
    if (task.action === 'chatgpt_close' || task.action === 'chatgpt_cleanup') {
      if (task.action === 'chatgpt_close' && task.tabId == null) throw new Error('关闭标签页必须指定 tabId');
      const candidates = task.action === 'chatgpt_close' ? tabs.filter(t => t.id === task.tabId) : tabs.filter(owns);
      const closed = [], skipped = [];
      for (const candidate of candidates) {
        const current = await chrome.tabs.get(candidate.id);
        if (!canClose(current) || !(await inspectChatGptTab(current))) { skipped.push(candidate.id); continue; }
        await chrome.tabs.remove(candidate.id); delete managed[candidate.id]; await saveManaged(); closed.push(candidate.id);
      }
      return { ...result, status: 'success', data: { closedTabIds: closed, skippedTabIds: skipped } };
    }
    let tab = task.tabId != null ? tabs.find(t => t.id === task.tabId) : null;
    let created = false, reused = false;
    const freshChat = task.action === 'chatgpt_new_chat' || (!tab && ['chatgpt_send', 'chatgpt_image'].includes(task.action) && task.newChat !== false);
    if (task.action === 'chatgpt_new_chat' && tab && !owns(tab)) throw new Error('不能在用户标签页上重置聊天');
    if (freshChat) {
      const newChatUrl = task.temporary ? 'https://chatgpt.com/?temporary-chat=true' : 'https://chatgpt.com/';
      if (!tab && task.tabPolicy !== 'new') {
        for (const candidate of tabs.filter(owns)) {
          if (await inspectChatGptTab(candidate)) { tab = candidate; break; }
        }
      }
      if (tab) {
        if (!owns(tab) || !(await inspectChatGptTab(await chrome.tabs.get(tab.id)))) throw new Error('标签页正在使用或含有草稿，不能重置聊天');
        tab = await chrome.tabs.update(tab.id, { url: newChatUrl }); reused = true;
      } else {
        tab = await chrome.tabs.create({ url: newChatUrl, active: false }); created = true;
      }
      managed[tab.id] = { url: newChatUrl }; await saveManaged();
    } else if (!tab) {
      if (tabs.length !== 1) throw new Error("请指定 tabId（当前没有 ChatGPT 页面或存在多个页面）");
      tab = tabs[0];
    }
    if (tab.discarded) await chrome.tabs.reload(tab.id);
    await waitChatGptTab(tab.id);
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["chatgpt-content.js"] });
    const response = await chrome.tabs.sendMessage(tab.id, { type: "yanzi_chatgpt_task", task });
    if (!response) throw new Error("ChatGPT 页面未返回结果");

    if (['chatgpt_image', 'chatgpt_capture_images'].includes(task.action) && response.status === 'success') {
      const sourceImages = Array.isArray(response.data?.images) ? response.data.images : [];
      if (!sourceImages.length) throw new Error('ChatGPT 未返回生成图片');

      const cachedImages = [];
      for (const source of sourceImages) {
        if (!source?.dataUrl) continue;
        const asset = await cacheChatGptImageAsset(source.dataUrl);
        const { dataUrl, src: sourceSrc, ...metadata } = source;
        cachedImages.push({
          ...metadata,
          sourceSrc,
          src: asset.url,
          localUrl: asset.url,
          assetId: asset.id,
          fileName: asset.fileName,
          mimeType: asset.mimeType || metadata.mimeType || '',
          byteLength: Number(asset.byteLength || metadata.byteLength || 0)
        });
      }
      if (!cachedImages.length) throw new Error('生成图片缓存失败');
      response.data.images = cachedImages;
    }

    const current = await chrome.tabs.get(tab.id);
    // Accept only navigation reported by this page task; unrelated user navigation relinquishes ownership.
    if (managed[tab.id]) {
      if (response.data?.url === current.url) managed[tab.id].url = current.url;
      else if (managed[tab.id].url !== current.url) delete managed[tab.id];
      await saveManaged();
    }
    let closed = false, closeSkipped = null;
    if (task.closeAfter && response.status === 'success') {
      if (canClose(current) && await inspectChatGptTab(current)) {
        await chrome.tabs.remove(tab.id); delete managed[tab.id]; await saveManaged(); closed = true;
      } else closeSkipped = '页面不是空闲的工作台后台标签页，已保留';
    }
    return { ...result, ...response, data: { ...response.data, tabId: tab.id, lifecycle: { created, reused, managed: owns(current), closed, closeSkipped } } };
  } catch (error) {
    return { ...result, message: error.message };
  } finally {
    chatGptBusy = false;
  }
}

function normalizeXhsGeneratedNotes(value, requestedCount) {
  const source = Array.isArray(value?.notes) ? value.notes : [];
  const seen = new Set();
  const notes = [];

  for (const item of source) {
    const title = String(item?.title || "").replace(/\s+/g, " ").trim().slice(0, 80);
    const body = String(item?.body || "").replace(/\s+/g, " ").trim().slice(0, 500);
    const topic = String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 40);
    if (!title || !body) continue;

    const key = title.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);

    notes.push({
      id: "local-ai-" + Date.now().toString(36) + "-" + notes.length + "-" +
        crypto.randomUUID().slice(0, 8),
      title,
      body,
      topic,
      author: "燕子 · AI",
      badge: "AI 生成",
      imageUrl: "",
      url: "",
      enabled: true,
      localTest: true,
      localFeed: true,
      source: "chatgpt",
      createdAt: Date.now(),
      updatedAt: Date.now()
    });

    if (notes.length >= requestedCount) break;
  }

  return notes;
}

function buildXhsLocalFeedPrompt(message) {
  const count = Math.max(1, Math.min(30, Number(message.count || 30)));
  const trimList = (items, max) => (Array.isArray(items) ? items : [])
    .slice(-max)
    .map(item => ({
      title: String(item?.title || "").replace(/\s+/g, " ").trim().slice(0, 100),
      body: String(item?.body || "").replace(/\s+/g, " ").trim().slice(0, 220),
      topic: String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 60),
      scope: item?.scope === "selection" ? "selection" : "note",
      selectionText: String(item?.selectionText || "")
        .replace(/\s+/g, " ")
        .trim()
        .slice(0, 240)
    }))
    .filter(item => item.title || item.body);

  const liked = trimList(message.liked, 16);
  const disliked = trimList(message.disliked, 16);
  const recent = trimList(message.recent, 60);
  const interests = (Array.isArray(message.interests) ? message.interests : [])
    .map(item => String(item || "").replace(/\s+/g, " ").trim().slice(0, 40))
    .filter(Boolean)
    .slice(0, 30);
  const evaluations = (Array.isArray(message.evaluations) ? message.evaluations : [])
    .slice(-12)
    .map(item => ({
      id: String(item?.id || ""),
      text: String(item?.text || "").replace(/\s+/g, " ").trim().slice(0, 500),
      title: String(item?.title || "").replace(/\s+/g, " ").trim().slice(0, 100),
      topic: String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 60),
      scope: item?.scope === "selection" ? "selection" : "note",
      selectionText: String(item?.selectionText || "")
        .replace(/\s+/g, " ")
        .trim()
        .slice(0, 240)
    }))
    .filter(item => item.text);

  return [
    "你正在为用户的个人信息流生成本地短笔记。内容分布必须由用户兴趣和反馈决定，不要默认偏向 AI、开发、编程或任何固定领域。",
    "只返回一个 JSON 代码块，代码块之外不要有任何文字。",
    "JSON 格式严格为：{\"notes\":[{\"title\":\"不超过26个汉字\",\"body\":\"40到90个汉字\",\"topic\":\"不超过10个汉字\"}]}",
    "必须恰好生成 " + count + " 条。",
    "规则：",
    "1. 如果兴趣主题非空，约 70% 内容应围绕这些主题，但要在主题之间分散，不要让单一主题占满；约 30% 用于相邻领域和新鲜探索。",
    "2. 如果兴趣主题为空，不要使用预设领域列表；主动跨生活、文化、科学、社会、消费、旅行、自然、娱乐、知识等不同方向保持多样性。",
    "3. 本轮评价是用户针对上一批内容给出的直接指令，优先级高于喜欢/不感兴趣，必须在这一批明显体现。",
    "4. 反馈或评价中 scope=selection 时，只代表用户对 selectionText 这段具体表述、观点或角度的偏好，不要把它扩大成对整篇笔记或整个主题的判断。",
    "5. scope=note 的喜欢记录代表增加相似的主题、问题意识或表达角度；scope=selection 的喜欢记录只增强相似的具体观点或表述角度，不要改写原文。",
    "6. scope=note 的不感兴趣代表降低相似主题和表达方式；scope=selection 的不感兴趣只降低相似的具体观点或表述，避免过度扩大。",
    "7. 最近出现过的内容只用于去重，不要把它们误当成兴趣主题，也不要生成近似标题。",
    "8. 内容要像值得点开的个人知识、现实观察或兴趣笔记，不要广告、营销、标题党和虚构新闻。",
    "9. 同一批内部也要去重：主题、结论、案例、表达角度都尽量不同。",
    "10. JSON 必须严格可解析。",
    "",
    "我的兴趣主题（长期偏好）：",
    JSON.stringify(interests),
    "",
    "本轮评价（只要求下一批执行）：",
    JSON.stringify(evaluations),
    "",
    "我喜欢：",
    JSON.stringify(liked),
    "",
    "我不感兴趣：",
    JSON.stringify(disliked),
    "",
    "最近已经出现（仅去重）：",
    JSON.stringify(recent)
  ].join("\n");
}

async function generateXhsLocalFeedNotes(message) {
  const requestedCount = Math.max(1, Math.min(30, Number(message.count || 30)));
  const startedAt = Date.now();
  const response = await runChatGptTask({
    taskId: "xhs-local-feed-" + crypto.randomUUID(),
    action: "chatgpt_send",
    prompt: buildXhsLocalFeedPrompt({ ...message, count: requestedCount }),
    temporary: true,
    tabPolicy: "reuse",
    closeAfter: true,
    timeoutSeconds: 360
  });

  if (response?.status !== "success") {
    throw new Error(response?.message || "ChatGPT 本地笔记生成失败");
  }

  const notes = normalizeXhsGeneratedNotes(response.data?.json, requestedCount);
  if (!notes.length) {
    throw new Error(
      response.data?.jsonError ||
      "ChatGPT 回复没有得到可用的 notes JSON"
    );
  }

  return {
    notes,
    requestedCount,
    returnedCount: notes.length,
    elapsedMs: Date.now() - startedAt,
    contextUsed: {
      likedCount: Array.isArray(message.liked) ? message.liked.length : 0,
      dislikedCount: Array.isArray(message.disliked) ? message.disliked.length : 0,
      recentCount: Array.isArray(message.recent) ? message.recent.length : 0,
      interests: (Array.isArray(message.interests) ? message.interests : [])
        .slice(0, 30)
        .map(item => String(item || "").trim())
        .filter(Boolean),
      evaluationIds: (Array.isArray(message.evaluations) ? message.evaluations : [])
        .slice(-12)
        .map(item => String(item?.id || ""))
        .filter(Boolean),
      evaluationTexts: (Array.isArray(message.evaluations) ? message.evaluations : [])
        .slice(-12)
        .map(item => String(item?.text || "").trim())
        .filter(Boolean),
      likedTitles: (Array.isArray(message.liked) ? message.liked : [])
        .slice(-12)
        .map(item => String(item?.title || "").trim())
        .filter(Boolean),
      dislikedTitles: (Array.isArray(message.disliked) ? message.disliked : [])
        .slice(-12)
        .map(item => String(item?.title || "").trim())
        .filter(Boolean)
    }
  };
}

chrome.runtime?.onMessage?.addListener?.((message, sender, sendResponse) => {
  if (message?.type !== "yanzi_xhs_generate_local_notes") return;

  generateXhsLocalFeedNotes(message)
    .then(data => sendResponse({ ok: true, ...data }))
    .catch(error => sendResponse({
      ok: false,
      error: error?.message || String(error)
    }));
  return true;
});

function connectChatGptBridge() {
  if (chatGptSocket && chatGptSocket.readyState < 2) return;
  const socket = new WebSocket("ws://127.0.0.1:53921/v1/browser/ws");
  chatGptSocket = socket;
  socket.onopen = () => {
    chrome.storage.local.set({ chatgptBridgeStatus: "connected" });
    socket.send(JSON.stringify({ type: "register", client: "yanzi-chatgpt", version: chrome.runtime.getManifest().version }));
  };
  socket.onmessage = async event => {
    try {
      const task = JSON.parse(event.data);
      if (task.type === 'asset_response') {
        const waiter = chatGptAssetWaiters.get(task.requestId);
        if (!waiter) return;
        chatGptAssetWaiters.delete(task.requestId);
        if (task.ok) waiter.resolve(task.asset);
        else waiter.reject(new Error(task.error || '图片缓存失败'));
        return;
      }
      if (task.type !== "task_request") return;
      const response = await runChatGptTask(task);
      if (socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(response));
    } catch (error) { console.warn("ChatGPT bridge:", error.message); }
  };
  socket.onclose = () => {
    if (chatGptSocket !== socket) return;
    chatGptSocket = null;
    for (const [requestId, waiter] of chatGptAssetWaiters) {
      waiter.reject(new Error('ChatGPT 工作台连接已断开'));
      chatGptAssetWaiters.delete(requestId);
    }
    chrome.storage.local.set({ chatgptBridgeStatus: "disconnected" });
    setTimeout(connectChatGptBridge, 3000);
  };
  socket.onerror = () => socket.close();
}
setInterval(() => {
  if (chatGptSocket?.readyState === WebSocket.OPEN) chatGptSocket.send(JSON.stringify({ type: "ping" }));
  else connectChatGptBridge();
}, 20000);
chrome.alarms.create("chatgptBridgeReconnect", { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "chatgptBridgeReconnect") connectChatGptBridge();
});
connectChatGptBridge();
