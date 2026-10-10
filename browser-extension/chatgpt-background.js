// Dedicated bridge leaves the existing Yanzi connection independent.
let chatGptSocket = null;
const CHATGPT_MAX_CONCURRENT_TASKS = 4;
let chatGptActiveTaskCount = 0;
const chatGptExecutionWaiters = [];
const chatGptReservedTabs = new Set();
const chatGptTabWaiters = new Map();
let xhsLocalFeedGenerationPromise = null;
const XHS_EVALUATION_TAB_TTL_MS = 60 * 1000;
const XHS_EVALUATION_ALARM_PREFIX = "xhs-evaluation-close:";
const chatGptAssetWaiters = new Map();
const chatGptOperations = new Set(["chatgpt_status", "chatgpt_new_chat", "chatgpt_send", "chatgpt_feedback_send", "chatgpt_subagent_continue", "chatgpt_image", "chatgpt_capture_images", "chatgpt_messages", "chatgpt_diagnostics", "chatgpt_network_probe", "chatgpt_close", "chatgpt_cleanup"]);

async function acquireChatGptExecutionSlot() {
  if (chatGptActiveTaskCount < CHATGPT_MAX_CONCURRENT_TASKS) {
    chatGptActiveTaskCount += 1;
  } else {
    await new Promise(resolve => chatGptExecutionWaiters.push(resolve));
  }

  let released = false;
  return () => {
    if (released) return;
    released = true;

    const next = chatGptExecutionWaiters.shift();
    if (next) {
      // Transfer this occupied slot directly to the next waiter. Keep the
      // active count unchanged so a newly arriving task cannot steal it.
      next();
      return;
    }

    chatGptActiveTaskCount = Math.max(0, chatGptActiveTaskCount - 1);
  };
}

function tryReserveChatGptTab(tabId) {
  const id = Number(tabId || 0);
  if (!id || chatGptReservedTabs.has(id)) return false;
  chatGptReservedTabs.add(id);
  return true;
}

async function reserveChatGptTab(tabId) {
  const id = Number(tabId || 0);
  if (!id) throw new Error("tabId 无效");
  if (tryReserveChatGptTab(id)) return;

  await new Promise(resolve => {
    const waiters = chatGptTabWaiters.get(id) || [];
    waiters.push(resolve);
    chatGptTabWaiters.set(id, waiters);
  });
  // The releasing owner transfers the reservation directly to us.
}

function releaseChatGptTab(tabId) {
  const id = Number(tabId || 0);
  if (!id) return;

  const waiters = chatGptTabWaiters.get(id);
  const next = waiters?.shift();

  if (waiters && !waiters.length) {
    chatGptTabWaiters.delete(id);
  }

  if (next) {
    // Keep the tab marked reserved while ownership is handed to the waiter.
    next();
    return;
  }

  chatGptReservedTabs.delete(id);
}
// Session storage survives worker suspension, but never carries tab ownership across browser restarts.
const managedTabsKey = 'chatgptManagedTabs';
let managedTabsMutation = Promise.resolve();

async function mutateManagedTabs(mutator) {
  const previous = managedTabsMutation;
  let release;
  managedTabsMutation = new Promise(resolve => {
    release = resolve;
  });

  await previous;
  try {
    const stored =
      (await chrome.storage.session.get(managedTabsKey))[managedTabsKey] || {};
    const managed = { ...stored };
    const value = await mutator(managed);
    await chrome.storage.session.set({ [managedTabsKey]: managed });
    return value === undefined ? managed : value;
  } finally {
    release();
  }
}

async function readManagedTabs() {
  await managedTabsMutation;
  return {
    ...((await chrome.storage.session.get(managedTabsKey))[managedTabsKey] || {})
  };
}
async function inspectChatGptTab(tab) {
  if (tab.active || tab.pinned || tab.discarded || tab.status !== 'complete') return false;
  const [probe] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: () => {
    const input = document.querySelector('#prompt-textarea, main [contenteditable="true"][role="textbox"]');
    return Boolean(input) && !(input.value || input.innerText || '').trim() && !document.querySelector('button[data-testid="stop-button"], main button[aria-label*="停止"], main button[aria-label^="Stop"]');
  } });
  return probe?.result === true;
}


async function withChatGptPageScriptTimeout(operation, waitMs=12000) {
  let timeoutId;
  try {
    return await Promise.race([
      Promise.resolve().then(operation),
      new Promise((_,reject) => {
        timeoutId = setTimeout(() => reject(new Error("ChatGPT 页面渲染进程无响应，未重新发送任务")), waitMs);
      })
    ]);
  } finally {
    if(timeoutId !== undefined) clearTimeout(timeoutId);
  }
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

function reportChatGptNetworkProgress(taskId,stage,snapshot=null) {
  if(typeof taskId!=="string" || !taskId ||
     chatGptSocket?.readyState!==WebSocket.OPEN) return;
  try {
    chatGptSocket.send(JSON.stringify({
      type:"job_network_progress",taskId,stage,
      ...(snapshot ? {snapshot}: {})
    }));
  } catch { /* Diagnostics must never interrupt task execution. */ }
}

// Only inspect the original reserved tab after Chrome loses the async response
// port. Never click Send, inject a prompt, or navigate during reconciliation.
async function recoverChatGptPageAfterChannelDrop(tabId, prompt, { maxWaitMs = 90000 } = {}) {
  const normalize = text => String(text || "").replace(/\s+/g, " ").trim();
  const expected = normalize(prompt);
  if (!Number.isInteger(tabId) || !expected) return null;
  const deadline = Date.now() + maxWaitMs;
  let stableText = "";
  let stableSince = 0;
  while (Date.now() < deadline) {
    try {
      const tab = await chrome.tabs.get(tabId);
      if (!/^https:\/\/chatgpt\.com\//.test(tab.url || "") ||
          tab.active || tab.pinned || tab.discarded) return null;
      if (tab.status !== "complete") {
        await new Promise(resolve => setTimeout(resolve, 1200));
        continue;
      }
      const [reading] = await withChatGptPageScriptTimeout(
        () => chrome.scripting.executeScript({
          target: { tabId },
          func: () => {
            const legacy = Array.from(document.querySelectorAll("[data-message-author-role]"));
            const units = legacy.length ? [] : Array.from(document.querySelectorAll("[data-chatgpt-search-unit-key]"))
              .filter(el => /:(user|assistant)$/.test(el.getAttribute("data-chatgpt-search-unit-key") || ""));
            const modern = legacy.length || units.length ? [] : [
              ...Array.from(document.querySelectorAll('[data-user-message-bubble="true"]')).map(el => ({ el, role: "user" })),
              ...Array.from(document.querySelectorAll('[class*="MarkdownRoot-"]'))
                .filter(el => !el.closest('[data-user-message-bubble="true"]')).map(el => ({ el, role: "assistant" }))
            ].sort((a, b) => a.el.compareDocumentPosition(b.el) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1);
            const turns = (legacy.length ? legacy : units).map(el => ({
              role: el.getAttribute("data-message-author-role") || (el.getAttribute("data-chatgpt-search-unit-key") || "").split(":").at(-1),
              text: (el.innerText || "").trim()
            }));
            if (!turns.length) turns.push(...modern.map(({ el, role }) => ({ role, text: (el.innerText || "").trim() })));
            const lastUserIndex = turns.findLastIndex(turn => turn.role === "user");
            const reply = lastUserIndex >= 0 ? turns.slice(lastUserIndex + 1).filter(turn => turn.role === "assistant").at(-1) : null;
            const stop = Boolean(document.querySelector('[data-testid="stop-button"], main button[aria-label*="停止"], main button[aria-label^="Stop"], [data-is-streaming="true"]'));
            const actionLabels = /^(复制|Copy|重新生成|Regenerate|重试|Retry|赞|踩|Good response|Bad response|Share|分享|评价回复)$/i;
            const settled = !stop && Array.from((document.querySelector("main") || document).querySelectorAll("button"))
              .some(button => actionLabels.test((button.getAttribute("aria-label") || button.textContent || "").trim()));
            return {
              url: location.href,
              lastUserText: lastUserIndex < 0 ? null : turns[lastUserIndex].text,
              replyText: reply?.text || "",
              settled
            };
          }
        }), 12000);
      const snapshot = reading?.result;
      // Match the most recent user turn exactly. A different turn must never be
      // claimed as this task's answer even if it happens to have an assistant reply.
      if (normalize(snapshot?.lastUserText) === expected && snapshot?.replyText && snapshot.settled) {
        const text = snapshot.replyText.trim();
        if (text === stableText) {
          if (Date.now() - stableSince >= 3500) {
            return {
              status: "success",
              data: {
                text,
                markdown: text,
                url: snapshot.url,
                conversationId: new URL(snapshot.url).pathname.match(/^\/c\/([^/]+)/)?.[1] || null,
                tabId,
                recoveredFromChannelDrop: true
              }
            };
          }
        } else {
          stableText = text;
          stableSince = Date.now();
        }
      } else {
        stableText = "";
        stableSince = 0;
      }
    } catch {
      // Reloads and temporary missing content scripts are normal while the
      // page is changing routes; keep this path read-only.
    }
    await new Promise(resolve => setTimeout(resolve, 1500));
  }
  return null;
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

  const result = {
    type: "task_response",
    taskId: task.taskId,
    status: "error",
    data: null
  };

  const needsExecutionSlot = ![
    "chatgpt_status",
    "chatgpt_close",
    "chatgpt_cleanup"
  ].includes(task.action);

  const releaseExecutionSlot = needsExecutionSlot
    ? await acquireChatGptExecutionSlot()
    : null;

  let reservedTabId = null;
  let networkWatch = null;
  let networkTimer = null;

  try {
    if (!chatGptOperations.has(task.action)) {
      throw new Error("不支持的 ChatGPT 操作");
    }

    let tabs = await chrome.tabs.query({ url: "https://chatgpt.com/*" });
    let managed = await mutateManagedTabs(current => {
      for (const [id, record] of Object.entries(current)) {
        const tabId = Number(id);
        if (chatGptReservedTabs.has(tabId)) continue;

        const tab = tabs.find(item => item.id === tabId);
        if (!tab || tab.url !== record.url) delete current[id];
      }
      return { ...current };
    });

    const refreshManaged = async () => {
      managed = await readManagedTabs();
      return managed;
    };

    const owns = tab =>
      Boolean(managed[tab.id] && managed[tab.id].url === tab.url);
    const canClose = tab => owns(tab) && !tab.active && !tab.pinned;


    if (task.action === "chatgpt_network_probe") {
      const tabId = task.tabId;
      if (!Number.isInteger(tabId) || tabId < 1)
        throw new Error("network_probe_tab_id_required");
      const target = tabs.find(item => item.id === tabId);
      if (!target || !owns(target) || target.active || target.pinned)
        throw new Error("network_probe_owned_inactive_tab_required");
      const observer=globalThis.yanziChatGptNetworkObserver;
      if (!observer?.supported)
        return {...result,status:"success",data:{available:false,tabId,reason:"webRequest_unavailable"}};
      const session=observer.start(tabId);
      if (!session) throw new Error("network_probe_tab_already_observed");
      let publicProbe={requested:false};
      try {
        if(task.publicProbe===true) {
          // Harmless public GET; never includes credentials, cookies or message content.
          try {
            const [probe] = await withChatGptPageScriptTimeout(() =>
              chrome.scripting.executeScript({
                target:{tabId},
                func:async()=>{
                  try {
                    const response=await fetch("https://chatgpt.com/robots.txt",
                      {method:"GET",credentials:"omit",cache:"no-store"});
                    return {completed:true,ok:response.ok};
                  } catch {return {completed:false};}
                }
              }),8000);
            publicProbe={requested:true,completed:probe?.result?.completed===true,
              ok:probe?.result?.ok===true};
          }catch {publicProbe={requested:true,completed:false};}
        }
        await new Promise(resolve=>setTimeout(resolve,
          Number.isInteger(task.durationMs) ? Math.max(1000,Math.min(15000,task.durationMs)) : 4000));
        return {...result,status:"success",data:{...session.stop(),publicProbe}};
      } finally {session.stop();}
    }

    if (task.action === "chatgpt_diagnostics") {
      if (!Number.isInteger(task.tabId) || task.tabId <= 0)
        throw new Error("diagnostics_tab_id_required");
      const target = tabs.find(item => item.id === task.tabId);
      if (!target) throw new Error("diagnostics_target_not_open");
      const snapshot = {
        tabId: target.id,
        status: target.status || "unknown",
        discarded: Boolean(target.discarded),
        active: Boolean(target.active),
        managed: owns(target),
        pageType: /^https:\/\/chatgpt\.com\/c\//.test(target.url || "") ? "conversation" : "root_or_other",
        dom: null,
        error: null
      };
      try {
        const [probe] = await withChatGptPageScriptTimeout(() => chrome.scripting.executeScript({
          target: { tabId: target.id },
          func: () => {
            const send = Array.from(document.querySelectorAll("button")).filter(el => {
              const label = ((el.getAttribute("aria-label") || "") + " " +
                (el.getAttribute("data-testid") || "")).toLowerCase();
              return /send|发送|submit/.test(label);
            });
            const editor=document.querySelector("#prompt-textarea, main [contenteditable='true'][role='textbox']");
            const draft=(editor?.value || editor?.innerText || "").trim();
            return {
              draftPresent: draft.length > 0,
              draftLength: draft.length,
              readyState: document.readyState,
              composerExists: Boolean(editor),
              sendButtons: send.length,
              sendEnabled: send.some(button => !button.disabled),
              generating: Boolean(document.querySelector("button[data-testid='stop-button']")),
              loginRequired: Boolean(document.querySelector("a[href*='/auth/login'], button[data-testid='login-button']")),
              challenge: Boolean(document.querySelector("iframe[src*='challenges.cloudflare.com'], #challenge-running")),
              pageTitle: document.title.slice(0,90)
            };
          }
        }), 10000);
        snapshot.dom = probe?.result || null;
      } catch (error) {
        snapshot.error = String(error?.message || error);
      }
      return { ...result, status: "success", data: snapshot };
    }

    if (task.action === "chatgpt_status") {
      return {
        ...result,
        status: "success",
        data: {
          exists: tabs.length > 0,
          extensionVersion: chrome.runtime.getManifest().version,
          concurrency: {
            active: chatGptActiveTaskCount,
            queued: chatGptExecutionWaiters.length,
            max: CHATGPT_MAX_CONCURRENT_TASKS
          },
          tabs: tabs.map(tab => ({
            tabId: tab.id,
            url: tab.url,
            active: tab.active,
            discarded: tab.discarded,
            managed: owns(tab),
            reserved: chatGptReservedTabs.has(tab.id),
            canClose: canClose(tab)
          }))
        }
      };
    }

    if (
      task.tabId != null &&
      !tabs.some(tab => tab.id === task.tabId)
    ) {
      throw new Error("指定标签页不是已打开的 ChatGPT 标签页");
    }

    if (
      task.action === "chatgpt_close" ||
      task.action === "chatgpt_cleanup"
    ) {
      const candidates = task.action === "chatgpt_close"
        ? tabs.filter(tab => tab.id === task.tabId)
        : tabs.filter(owns);

      if (task.action === "chatgpt_close" && task.tabId == null) {
        throw new Error("关闭标签页必须指定 tabId");
      }

      const closed = [];
      const skipped = [];

      for (const candidate of candidates) {
        if (chatGptReservedTabs.has(candidate.id)) {
          skipped.push(candidate.id);
          continue;
        }

        const current = await chrome.tabs.get(candidate.id);
        if (!canClose(current) || !(await inspectChatGptTab(current))) {
          skipped.push(candidate.id);
          continue;
        }

        await chrome.tabs.remove(candidate.id);
        await mutateManagedTabs(map => {
          delete map[candidate.id];
        });
        delete managed[candidate.id];
        closed.push(candidate.id);
      }

      return {
        ...result,
        status: "success",
        data: {
          closedTabIds: closed,
          skippedTabIds: skipped
        }
      };
    }

    // Rebind by exact conversation URL; never guess based on an active tab.
    let tab = ["chatgpt_feedback_send", "chatgpt_subagent_continue"].includes(task.action)
      ? (() => {
          const matches = tabs.filter(item => item.url === task.expectedUrl);
          if (matches.length !== 1 && !(task.action === "chatgpt_subagent_continue" && matches.length === 0))
            throw new Error(matches.length ? "相同对话存在多个标签页，禁止盲目发送" : "原 ChatGPT 对话标签页未打开，反馈保留待处理");
          if (!matches.length) return null;
          if (task.tabId != null && matches[0].id !== task.tabId)
            throw new Error("回传目标标签页不匹配");
          return matches[0];
        })()
      : task.tabId != null
      ? tabs.find(item => item.id === task.tabId)
      : null;
    let created = false;
    let reused = false;

    if (task.action === "chatgpt_subagent_continue" && !tab) {
      // Recover the exact child conversation on a new inactive tab,
      // without taking over any user-controlled tab.
      tab = await chrome.tabs.create({url:task.expectedUrl,active:false});
      created = true;
      await reserveChatGptTab(tab.id);
      reservedTabId = tab.id;
      await mutateManagedTabs(map => { map[tab.id] = { url: task.expectedUrl }; });
      await refreshManaged();
      await waitChatGptTab(tab.id);
    } else if (tab) {
      await reserveChatGptTab(tab.id);
      reservedTabId = tab.id;
    }

    const freshChat =
      task.action === "chatgpt_new_chat" ||
      (
        !tab &&
        ["chatgpt_send", "chatgpt_image"].includes(task.action) &&
        task.newChat !== false
      );

    if (
      task.action === "chatgpt_new_chat" &&
      tab &&
      !owns(tab)
    ) {
      throw new Error("不能在用户标签页上重置聊天");
    }

    if (freshChat) {
      const newChatUrl = task.temporary
        ? "https://chatgpt.com/?temporary-chat=true"
        : "https://chatgpt.com/";

      if (!tab && task.tabPolicy !== "new") {
        for (const candidate of tabs.filter(owns)) {
          if (!tryReserveChatGptTab(candidate.id)) continue;

          let accepted = false;
          try {
            if (await inspectChatGptTab(candidate)) {
              tab = candidate;
              reservedTabId = candidate.id;
              accepted = true;
              break;
            }
          } finally {
            if (!accepted) releaseChatGptTab(candidate.id);
          }
        }
      }

      if (tab) {
        if (
          !owns(tab) ||
          !(await inspectChatGptTab(await chrome.tabs.get(tab.id)))
        ) {
          throw new Error("标签页正在使用或含有草稿，不能重置聊天");
        }

        tab = await chrome.tabs.update(tab.id, { url: newChatUrl });
        reused = true;
      } else {
        tab = await chrome.tabs.create({
          url: newChatUrl,
          active: false
        });
        created = true;
        await reserveChatGptTab(tab.id);
        reservedTabId = tab.id;
      }

      await mutateManagedTabs(map => {
        map[tab.id] = { url: newChatUrl };
      });
      await refreshManaged();
    } else if (!tab) {
      const candidates = tabs.filter(
        candidate => !chatGptReservedTabs.has(candidate.id)
      );

      if (candidates.length !== 1) {
        throw new Error(
          "请指定 tabId（当前没有唯一可用的 ChatGPT 页面）"
        );
      }

      tab = candidates[0];
      await reserveChatGptTab(tab.id);
      reservedTabId = tab.id;
    }

    if (["chatgpt_feedback_send", "chatgpt_subagent_continue"].includes(task.action)) {
      const resolved = await chrome.tabs.get(tab.id);
      if (resolved.url !== task.expectedUrl || resolved.discarded)
        throw new Error("发送前页面身份变化，禁止回传");
    }
    if (tab.discarded) {
      await chrome.tabs.reload(tab.id);
    }

    await waitChatGptTab(tab.id);
    if (["chatgpt_feedback_send", "chatgpt_subagent_continue"].includes(task.action)) {
      const resolved = await chrome.tabs.get(tab.id);
      if (resolved.url !== task.expectedUrl || resolved.discarded) {
        throw new Error("对话标识变化，拒绝向子 Agent 发送");
      }
    }
    await withChatGptPageScriptTimeout(() => chrome.scripting.executeScript({
      target: { tabId: tab.id },
      files: ["chatgpt-content.js"]
    }), 12000);

    const targetForNetwork=await chrome.tabs.get(tab.id);
    if(owns(targetForNetwork) && !targetForNetwork.active)
      networkWatch=globalThis.yanziChatGptNetworkObserver?.start(tab.id)||null;
    if(networkWatch) {
      reportChatGptNetworkProgress(task.taskId,"watch_started",networkWatch.snapshot());
      reportChatGptNetworkProgress(task.taskId,"page_dispatch_started",networkWatch.snapshot());
      networkTimer=setInterval(()=>{
        reportChatGptNetworkProgress(task.taskId,"page_dispatch_started",
          networkWatch?.snapshot()||null);
      },1500);
    }
    let response;
    try {
      response = await chrome.tabs.sendMessage(tab.id, {
        type: "yanzi_chatgpt_task",
        task
      });
    } catch (error) {
      if (task.action !== "chatgpt_send" ||
          !/message channel closed before a response|message port closed before a response|Extension context invalidated/i.test(error?.message || ""))
        throw error;
      const recovered = await recoverChatGptPageAfterChannelDrop(tab.id, task.prompt);
      if (!recovered) {
        return {
          ...result,
          data: { tabId: tab.id, deliveryUncertain: true },
          message: "ChatGPT 页面异步消息通道中断，原消息可能已发送；只读核对未取得最终回复，已保留原标签页，请核查后再重试"
        };
      }
      response = recovered;
    }

    if(networkTimer!==null){clearInterval(networkTimer);networkTimer=null;}
    if(networkWatch)reportChatGptNetworkProgress(task.taskId,"page_reply_received",
      networkWatch.snapshot());
    const networkTrace=networkWatch?.stop()||null;
    if (!response) {
      throw new Error("ChatGPT 页面未返回结果");
    }

    if (
      ["chatgpt_image", "chatgpt_capture_images"].includes(task.action) &&
      response.status === "success"
    ) {
      const sourceImages = Array.isArray(response.data?.images)
        ? response.data.images
        : [];

      if (!sourceImages.length) {
        throw new Error("ChatGPT 未返回生成图片");
      }

      const cachedImages = [];
      for (const source of sourceImages) {
        if (!source?.dataUrl) continue;

        const asset = await cacheChatGptImageAsset(source.dataUrl);
        const {
          dataUrl,
          src: sourceSrc,
          ...metadata
        } = source;

        cachedImages.push({
          ...metadata,
          sourceSrc,
          src: asset.url,
          localUrl: asset.url,
          assetId: asset.id,
          fileName: asset.fileName,
          mimeType: asset.mimeType || metadata.mimeType || "",
          byteLength: Number(
            asset.byteLength || metadata.byteLength || 0
          )
        });
      }

      if (!cachedImages.length) {
        throw new Error("生成图片缓存失败");
      }

      response.data.images = cachedImages;
    }

    const current = await chrome.tabs.get(tab.id);

    await mutateManagedTabs(map => {
      const record = map[tab.id];
      if (!record) return;

      if (response.data?.url === current.url) {
        record.url = current.url;
      } else if (record.url !== current.url) {
        delete map[tab.id];
      }
    });
    await refreshManaged();

    let closed = false;
    let closeSkipped = null;

    if (task.closeAfter && response.status === "success") {
      if (canClose(current) && await inspectChatGptTab(current)) {
        await chrome.tabs.remove(tab.id);
        await mutateManagedTabs(map => {
          delete map[tab.id];
        });
        delete managed[tab.id];
        closed = true;
      } else {
        closeSkipped = "页面不是空闲的工作台后台标签页，已保留";
      }
    }

    return {
      ...result,
      ...response,
      data: {
        ...response.data,
        ...(networkTrace ? {network:networkTrace}:{}),
        tabId: tab.id,
        lifecycle: {
          created,
          reused,
          managed: owns(current),
          closed,
          closeSkipped
        }
      }
    };
  } catch (error) {
    const networkTrace=networkWatch?.stop()||null;
    return {
      ...result,
      data:networkTrace ? {network:networkTrace}:null,
      message: error?.message || String(error)
    };
  } finally {
    if(networkTimer!==null)clearInterval(networkTimer);
    networkWatch?.stop();
    if (reservedTabId) {
      releaseChatGptTab(reservedTabId);
    }
    releaseExecutionSlot?.();
  }
}

async function runIsolatedChatGptTask(task) {
  const result = {
    type: "task_response",
    taskId: task.taskId,
    status: "error",
    data: null
  };
  const releaseExecutionSlot = await acquireChatGptExecutionSlot();
  let tab = null;

  try {
    if (!chatGptOperations.has(task.action)) {
      throw new Error("不支持的 ChatGPT 操作");
    }

    const newChatUrl = task.temporary === false
      ? "https://chatgpt.com/"
      : "https://chatgpt.com/?temporary-chat=true";

    tab = await chrome.tabs.create({
      url: newChatUrl,
      active: false
    });

    await waitChatGptTab(tab.id);
    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      files: ["chatgpt-content.js"]
    });

    const response = await chrome.tabs.sendMessage(tab.id, {
      type: "yanzi_chatgpt_task",
      task
    });

    if (!response) {
      throw new Error("ChatGPT 页面未返回结果");
    }

    return {
      ...result,
      ...response,
      data: {
        ...response.data,
        tabId: tab.id,
        lifecycle: {
          created: true,
          reused: false,
          managed: false,
          isolated: true
        }
      }
    };
  } catch (error) {
    return {
      ...result,
      message: error?.message || String(error)
    };
  } finally {
    if (tab?.id) {
      try {
        await chrome.tabs.remove(tab.id);
      } catch {}
    }
    releaseExecutionSlot();
  }
}

function normalizeXhsGeneratedNotes(value, requestedCount, frontier = []) {
  const source = Array.isArray(value?.notes) ? value.notes : [];
  const allowedParentIds = new Set(
    (Array.isArray(frontier) ? frontier : [])
      .map(item => String(item?.id || "").trim())
      .filter(Boolean)
  );
  const seen = new Set();
  const notes = [];

  for (const item of source) {
    const title = String(item?.title || "").replace(/\s+/g, " ").trim().slice(0, 80);
    const body = String(item?.body || "").replace(/\s+/g, " ").trim().slice(0, 500);
    const topic = String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 40);
    const parentNodeId = String(item?.parentNodeId || "").trim().slice(0, 120);
    if (!title || !body || !topic || !allowedParentIds.has(parentNodeId)) continue;

    const key = title.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);

    notes.push({
      id: "local-ai-" + Date.now().toString(36) + "-" + notes.length + "-" +
        crypto.randomUUID().slice(0, 8),
      title,
      body,
      topic,
      parentNodeId,
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
      body: String(item?.body || "").replace(/\s+/g, " ").trim().slice(0, 160),
      topic: String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 60),
      scope: item?.scope === "selection" ? "selection" : "note",
      selectionText: String(item?.selectionText || "")
        .replace(/\s+/g, " ")
        .trim()
        .slice(0, 200)
    }))
    .filter(item => item.title || item.body);

  const liked = trimList(message.liked, 10);
  const disliked = trimList(message.disliked, 10);
  const recent = (Array.isArray(message.recent) ? message.recent : [])
    .slice(-24)
    .map(item => ({
      title: String(item?.title || "").replace(/\s+/g, " ").trim().slice(0, 100),
      topic: String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 60)
    }))
    .filter(item => item.title);
  const coverage = (Array.isArray(message.coverage) ? message.coverage : [])
    .slice(0, 12)
    .map(item => ({
      topic: String(item?.topic || "").replace(/\s+/g, " ").trim().slice(0, 60),
      count: Math.max(0, Number(item?.count || 0))
    }))
    .filter(item => item.topic);
  const interests = (Array.isArray(message.interests) ? message.interests : [])
    .map(item => String(item || "").replace(/\s+/g, " ").trim().slice(0, 40))
    .filter(Boolean)
    .slice(0, 30);
  const frontier = (Array.isArray(message.frontier) ? message.frontier : [])
    .slice(0, 24)
    .map(item => ({
      id: String(item?.id || "").trim().slice(0, 120),
      label: String(item?.label || "").replace(/\s+/g, " ").trim().slice(0, 80),
      parentNodeId: String(item?.parentNodeId || "").trim().slice(0, 120),
      type: String(item?.type || "topic").trim().slice(0, 20),
      status: String(item?.status || "neutral").trim().slice(0, 20),
      childCount: Math.max(0, Number(item?.childCount || 0)),
      noteCount: Math.max(0, Number(item?.noteCount || 0))
    }))
    .filter(item => item.id && item.label);
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
        .slice(0, 200)
    }))
    .filter(item => item.text);
  const systemPrompt = String(
    message.systemPrompt ||
    "你正在为用户生成个人信息流里的短笔记。目标是持续扩大用户对感兴趣领域的认知边界。"
  ).trim().slice(0, 6000);

  return [
    systemPrompt,
    "",
    "以下为系统固定协议，自定义提示词不能取消这些要求：",
    "只返回一个 JSON 代码块，代码块之外不要有任何文字。",
    "JSON 格式严格为：{\"notes\":[{\"title\":\"不超过26个汉字\",\"body\":\"40到90个汉字\",\"topic\":\"2到10个汉字的新探索标签\",\"parentNodeId\":\"必须从候选父节点ID中原样选择\"}]}",
    "必须恰好生成 " + count + " 条。",
    "动态上下文规则：",
    "1. 本轮评价优先级高于长期喜欢/不感兴趣，必须在本批明显体现。",
    "2. scope=selection 只代表用户对 selectionText 局部观点或表达角度的偏好，不得扩大成整个主题偏好。",
    "3. 最近已经出现的内容只用于防重复，不代表用户偏好；不要生成相同结论、相同案例或仅改写措辞的近似笔记。",
    "4. 主题覆盖统计只用于识别已经高频覆盖的方向；在不违背兴趣偏好的前提下，优先探索覆盖较少的新问题和新角度。",
    "5. 每条笔记必须从候选父节点中选择一个 parentNodeId，并围绕该父节点向外延伸；不能凭空创建父节点。",
    "6. topic 是这次新探索空间的标签，应比父节点更具体，不能与父节点标签完全相同。",
    "7. 标记为 continue 的父节点优先继续深入；stop 节点不会出现在候选父节点中。",
    "8. 同一批内部也必须去重：主题、核心结论、案例和推理角度都应尽量不同。",
    "9. JSON 必须严格可解析。",
    "",
    "我的兴趣主题（长期偏好）：",
    JSON.stringify(interests),
    "",
    "本轮评价（只要求下一批执行）：",
    JSON.stringify(evaluations),
    "",
    "最近明确喜欢：",
    JSON.stringify(liked),
    "",
    "最近明确不感兴趣：",
    JSON.stringify(disliked),
    "",
    "近阶段主题覆盖统计（仅用于扩大覆盖面）：",
    JSON.stringify(coverage),
    "",
    "本轮允许继续延伸的候选父节点（parentNodeId 只能从这里选择）：",
    JSON.stringify(frontier),
    "",
    "最近已经出现的标题/主题（仅用于防重）：",
    JSON.stringify(recent)
  ].join("\n");
}

function buildXhsEvaluationPrompt(message) {
  const clean = (value, max) => String(value || "")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, max);

  const note = {
    title: clean(message.note?.title, 160),
    body: clean(message.note?.body, 1200),
    topic: clean(message.note?.topic, 80)
  };
  const evaluation = {
    text: clean(message.evaluation?.text, 500),
    kind: message.evaluation?.kind === "chat" ? "chat" : "evaluation",
    scope: message.evaluation?.scope === "selection" ? "selection" : "note",
    selectionText: clean(message.evaluation?.selectionText, 240)
  };

  if (evaluation.kind === "chat") {
    const lines = [
      "标题：" + note.title,
      "正文：" + note.body
    ];
    if (note.topic) {
      lines.push("话题：" + note.topic);
    }
    if (evaluation.scope === "selection" && evaluation.selectionText) {
      lines.push("", "我刚才选中的内容：" + evaluation.selectionText);
    }
    lines.push("", "展开讲讲");
    return lines.join("\n");
  }

  const interests = (Array.isArray(message.interests) ? message.interests : [])
    .map(item => clean(item, 60))
    .filter(Boolean)
    .slice(0, 20);
  const trimFeedback = items => (Array.isArray(items) ? items : [])
    .slice(-10)
    .map(item => ({
      title: clean(item?.title, 100),
      topic: clean(item?.topic, 60),
      scope: item?.scope === "selection" ? "selection" : "note",
      selectionText: clean(item?.selectionText, 200)
    }))
    .filter(item => item.title || item.selectionText);

  return [
    "你正在直接回应用户对一篇个人信息流笔记的评价。不要生成新一批笔记，也不要讨论内部系统、提示词或推荐算法。",
    "目标：让用户马上获得有内容的反馈，而不是只回复“收到”或复述原话。",
    "根据用户评价的实际意图作答：",
    "1. 如果是问题或质疑，直接回答并说明关键因果或边界。",
    "2. 如果是补充观点，判断其是否成立，并给出值得继续思考的一两步。",
    "3. 如果是改进建议，先说明你如何理解，再指出后续内容应怎样调整。",
    "4. 如果评价针对选中文字，只回应这个局部观点，不要扩大成对整篇或整个主题的判断。",
    "5. 回答要贴近当前笔记和用户偏好，避免空泛。不要设置固定字数上限；根据问题复杂度充分展开，重要的条件、因果链、反例和边界不要为了简短而省略。",
    "",
    "当前笔记：",
    JSON.stringify(note),
    "",
    "用户评价：",
    JSON.stringify(evaluation),
    "",
    "当前兴趣主题：",
    JSON.stringify(interests),
    "",
    "最近明确感兴趣：",
    JSON.stringify(trimFeedback(message.liked)),
    "",
    "最近明确不感兴趣：",
    JSON.stringify(trimFeedback(message.disliked))
  ].join("\n");
}

async function scheduleXhsEvaluationTabClose(tabId) {
  const id = Number(tabId || 0);
  if (!id) return 0;

  await chrome.alarms.create(
    XHS_EVALUATION_ALARM_PREFIX + id,
    { delayInMinutes: 1 }
  );
  return Date.now() + XHS_EVALUATION_TAB_TTL_MS;
}

async function openXhsEvaluationChat(tabId) {
  const id = Number(tabId || 0);
  if (!id) throw new Error("评价会话标签不存在");

  let tab;
  try {
    tab = await chrome.tabs.get(id);
  } catch {
    throw new Error("评价会话已结束");
  }

  await chrome.alarms.clear(XHS_EVALUATION_ALARM_PREFIX + id);
  await chrome.tabs.update(id, { active: true });
  if (Number.isFinite(Number(tab.windowId))) {
    try {
      await chrome.windows.update(tab.windowId, { focused: true });
    } catch {}
  }

  return {
    tabId: id,
    chatUrl: tab.url || "",
    keptOpen: true
  };
}

chrome.alarms.onAlarm.addListener(alarm => {
  if (!alarm?.name?.startsWith(XHS_EVALUATION_ALARM_PREFIX)) return;
  const tabId = Number(alarm.name.slice(XHS_EVALUATION_ALARM_PREFIX.length));
  if (!tabId) return;

  void (async () => {
    try {
      const tab = await chrome.tabs.get(tabId);
      if (tab.active || tab.pinned) return;

      const managed =
        (await chrome.storage.session.get(managedTabsKey))[managedTabsKey] || {};
      const record = managed[tabId];
      if (!record) return;

      if (!(await inspectChatGptTab(tab))) return;

      await chrome.tabs.remove(tabId);
      delete managed[tabId];
      await chrome.storage.session.set({ [managedTabsKey]: managed });
    } catch {
      // The tab may already be gone; cleanup is best-effort.
    }
  })();
});

async function evaluateXhsLocalFeedNote(message) {
  const startedAt = Date.now();
  const response = await runChatGptTask({
    taskId: "xhs-evaluation-" + crypto.randomUUID(),
    action: "chatgpt_send",
    prompt: buildXhsEvaluationPrompt(message),
    temporary: true,
    tabPolicy: "new",
    closeAfter: false,
    timeoutSeconds: 240,
    allowConcurrent: true
  });

  if (response?.status !== "success") {
    throw new Error(response?.message || "ChatGPT 评价回复失败");
  }

  const answer = String(
    response.data?.text ||
    response.data?.markdown ||
    ""
  ).trim();

  if (!answer) {
    throw new Error("ChatGPT 没有返回可展示的评价回复");
  }

  const tabId = Number(response.data?.tabId || 0);
  const tabExpiresAt = await scheduleXhsEvaluationTabClose(tabId);

  return {
    answer,
    elapsedMs: Date.now() - startedAt,
    tabId,
    chatUrl: String(response.data?.url || ""),
    tabExpiresAt
  };
}

function queueXhsEvaluation(message) {
  return evaluateXhsLocalFeedNote(message);
}

async function generateXhsLocalFeedNotes(message) {
  const requestedCount = Math.max(1, Math.min(30, Number(message.count || 30)));
  const startedAt = Date.now();
  const response = await runIsolatedChatGptTask({
    taskId: "xhs-local-feed-" + crypto.randomUUID(),
    action: "chatgpt_send",
    prompt: buildXhsLocalFeedPrompt({ ...message, count: requestedCount }),
    temporary: true,
    timeoutSeconds: 360
  });

  if (response?.status !== "success") {
    throw new Error(response?.message || "ChatGPT 本地笔记生成失败");
  }

  const notes = normalizeXhsGeneratedNotes(
    response.data?.json,
    requestedCount,
    message.frontier
  );
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
      frontier: (Array.isArray(message.frontier) ? message.frontier : [])
        .slice(0, 24)
        .map(item => ({
          id: String(item?.id || "").trim(),
          label: String(item?.label || "").trim(),
          status: String(item?.status || "neutral").trim()
        }))
        .filter(item => item.id && item.label),
      coverage: (Array.isArray(message.coverage) ? message.coverage : [])
        .slice(0, 12)
        .map(item => ({
          topic: String(item?.topic || "").trim(),
          count: Math.max(0, Number(item?.count || 0))
        }))
        .filter(item => item.topic),
      systemPromptLength: String(message.systemPrompt || "").length,
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

function queueXhsLocalFeedGeneration(message) {
  if (xhsLocalFeedGenerationPromise) {
    return xhsLocalFeedGenerationPromise;
  }

  const run = generateXhsLocalFeedNotes(message);
  xhsLocalFeedGenerationPromise = run.finally(() => {
    xhsLocalFeedGenerationPromise = null;
  });
  return xhsLocalFeedGenerationPromise;
}

chrome.runtime?.onMessage?.addListener?.((message, sender, sendResponse) => {
  if (message?.type === "yanzi_xhs_generate_local_notes") {
    queueXhsLocalFeedGeneration(message)
      .then(data => sendResponse({ ok: true, ...data }))
      .catch(error => sendResponse({
        ok: false,
        error: error?.message || String(error)
      }));
    return true;
  }

  if (message?.type === "yanzi_xhs_evaluate_note") {
    queueXhsEvaluation(message)
      .then(data => sendResponse({ ok: true, ...data }))
      .catch(error => sendResponse({
        ok: false,
        error: error?.message || String(error)
      }));
    return true;
  }

  if (message?.type === "yanzi_xhs_open_evaluation_chat") {
    openXhsEvaluationChat(message.tabId)
      .then(data => sendResponse({ ok: true, ...data }))
      .catch(error => sendResponse({
        ok: false,
        error: error?.message || String(error)
      }));
    return true;
  }
});


// Feedback origin must be captured from the actual ChatGPT page, not guessed
// from whichever tab happens to be active when an asynchronous job completes.
const CHATGPT_FEEDBACK_ORIGIN_MENU = "yanzi-chatgpt-bind-feedback-origin";
function installChatGptFeedbackOriginMenu() {
  chrome.contextMenus.remove(CHATGPT_FEEDBACK_ORIGIN_MENU, () => {
    void chrome.runtime.lastError;
    chrome.contextMenus.create({
      id: CHATGPT_FEEDBACK_ORIGIN_MENU,
      title: "燕子：将此 ChatGPT 对话设为开发反馈回传目标",
      contexts: ["page"],
      documentUrlPatterns: ["https://chatgpt.com/*"]
    }, () => { void chrome.runtime.lastError; });
  });
}
chrome.runtime.onInstalled.addListener(installChatGptFeedbackOriginMenu);
installChatGptFeedbackOriginMenu();

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== CHATGPT_FEEDBACK_ORIGIN_MENU) return;
  void (async () => {
    const sendStatus = async value => {
      await chrome.storage.local.set({ chatgptFeedbackOriginStatus: value });
      await chrome.action.setBadgeText({ text: value.pending ? "…" : value.ok ? "↩" : "!" });
      await chrome.action.setBadgeBackgroundColor({ color: value.pending ? "#687480" : value.ok ? "#287B54" : "#B74B4B" });
    };
    try {
      if (!tab?.id || !/^https:\/\/chatgpt\.com\/c\/[A-Za-z0-9_-]{8,120}\/?(?:[?#].*)?$/.test(tab.url || "")) {
        throw new Error("请先进入已有的正式 ChatGPT 对话，再执行绑定");
      }
      if (chatGptSocket?.readyState !== WebSocket.OPEN)
        throw new Error("ChatGPT 本地工作台连接不可用");
      const [probe] = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => ({
          url: location.href,
          temporary: Boolean(
            document.querySelector('button[aria-label="Save chat"], [data-testid="save-chat-button"]') ||
            (document.body?.innerText || "").slice(0, 1200).includes("Temporary Chat"))
        })
      });
      const actual = probe?.result;
      if (!actual || actual.temporary || actual.url.split(/[?#]/)[0].replace(/\/$/,"") !==
          tab.url.split(/[?#]/)[0].replace(/\/$/,"")) {
        throw new Error("对话位置或临时模式无法确认，绑定已取消");
      }
      const requestId=crypto.randomUUID();
      chatGptSocket.send(JSON.stringify({
        type:"origin_bind",requestId,tabId:tab.id,url:actual.url,
        conversationId:new URL(actual.url).pathname.split("/")[2],
        temporary:false
      }));
      await sendStatus({ok:true,pending:true,tabId:tab.id,message:"已提交绑定请求，等待工作台确认"});
    } catch(error) {
      await sendStatus({ok:false,message:error?.message || String(error)});
    }
  })();
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
      if (task.type === 'origin_bind_ack') {
        const ok = task.ok === true;
        await chrome.storage.local.set({
          chatgptFeedbackOriginStatus: ok
            ? {ok:true,pending:false,binding:task.binding,at:Date.now()}
            : {ok:false,pending:false,message:task.error,at:Date.now()}
        });
        await chrome.action.setBadgeText({text:ok ? "↩" : "!"});
        await chrome.action.setBadgeBackgroundColor({color:ok ? "#287B54" : "#B74B4B"});
        return;
      }
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
