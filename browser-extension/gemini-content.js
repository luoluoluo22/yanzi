(() => {
  // Avoid duplicate message listeners when a task re-injects this script.
  if (window.__yanziGeminiContentInstalled) return;
  window.__yanziGeminiContentInstalled = true;
  let busy = false;
  const inputSelector = 'rich-textarea .ql-editor, rich-textarea [contenteditable="true"], div[contenteditable="true"][role="textbox"], div.ql-editor[contenteditable="true"], [data-placeholder][contenteditable="true"], textarea';
  const sendSelector = 'button[aria-label*="发送" i], button[aria-label*="Send" i], button.send-button, [data-test-id="send-button"], button[mattooltip*="Send" i], button[mattooltip*="发送" i], button.send-button-container';
  const stopSelector = 'button[aria-label*="停止" i], button[aria-label*="Stop" i], [data-test-id="stop-button"], button.stop-generating-button';
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

  function extractContent(element) {
    if (!element) return { text: '', markdown: '', codeBlocks: [], json: null, jsonError: null, markdownSource: 'empty' };
    const root = element.querySelector('.markdown, message-content, .model-response-text') || element;
    const blocks = [];
    const blockSelector = 'pre, code-block, [data-test-id="code-block"]';
    const blockElements = Array.from(root.querySelectorAll(blockSelector)).filter(el => !el.parentElement?.closest(blockSelector));

    for (const block of blockElements) {
      const code = block.querySelector('code') || block;
      const header = block.querySelector('.code-header, [data-test-id="code-header"]')?.cloneNode(true);
      header?.querySelectorAll('button, svg, [hidden], .sr-only').forEach(node => node.remove());
      const label = header?.textContent?.trim() || '';
      const language = code?.className.match(/language-([\w+-]+)/)?.[1] || label?.match(/^[\w+#.-]+$/)?.[0]?.toLowerCase() || '';
      blocks.push({ language, text: (code || block).textContent });
    }

    const blockMap = new Map(blockElements.map((el, i) => [el, blocks[i]]));
    const ignored = el => el.matches('.sr-only, button, svg, [hidden], mat-icon, .action-buttons, response-container-footer, .header, .avatar, h5, h6, [class*="response-header"], [class*="tts-container"], [class*="sparkle"]');
    const fenceFor = text => '`'.repeat(Math.max(3, ...Array.from(text.matchAll(/`+/g), m => m[0].length + 1)));

    function walk(node, markdown) {
      if (node.nodeType === 3) return node.textContent;
      if (node.nodeType !== 1 || ignored(node)) return '';
      const block = blockMap.get(node);
      if (block) {
        if (!markdown) return block.text + '\n\n';
        const fence = fenceFor(block.text);
        return fence + block.language + '\n' + block.text + (block.text.endsWith('\n') ? '' : '\n') + fence + '\n\n';
      }
      const children = () => Array.from(node.childNodes, child => walk(child, markdown)).join('');
      const tag = node.tagName;
      if (tag === 'BR') return '\n';
      if (/^H[1-6]$/.test(tag)) return (markdown ? '#'.repeat(Number(tag[1])) + ' ' : '') + children() + '\n\n';
      if (tag === 'P') return children() + '\n\n';
      if (tag === 'BLOCKQUOTE') return children().trim().split('\n').map(line => '> ' + line).join('\n') + '\n\n';
      if (tag === 'UL' || tag === 'OL') {
        const start = Number(node.getAttribute('start') || 1);
        return Array.from(node.children).filter(el => el.tagName === 'LI').map((el, i) => {
          const marker = tag === 'OL' ? (start + i) + '. ' : '- ';
          return marker + walk(el, markdown).trim().split('\n').join('\n' + ' '.repeat(marker.length));
        }).join('\n') + '\n\n';
      }
      if (tag === 'TABLE') {
        const rows = Array.from(node.querySelectorAll('tr'), tr => Array.from(tr.children, cell => walk(cell, markdown).trim()));
        if (!markdown) return rows.map(row => row.join('\t')).join('\n') + '\n\n';
        const lines = rows.map(row => '| ' + row.map(cell => cell.replace(/\|/g, '\\|').replace(/\n/g, '<br>')).join(' | ') + ' |');
        if (lines.length) lines.splice(1, 0, '| ' + rows[0].map(() => '---').join(' | ') + ' |');
        return lines.join('\n') + '\n\n';
      }
      if (markdown && tag === 'A') return '[' + children() + '](' + (node.getAttribute('href') || '').replace(/\)/g, '\\)') + ')';
      if (markdown && tag === 'CODE') {
        const value = node.textContent, fence = '`'.repeat(Math.max(1, ...Array.from(value.matchAll(/`+/g), m => m[0].length + 1)));
        return fence + value + fence;
      }
      if (markdown && ['STRONG', 'B', 'EM', 'I', 'DEL', 'S'].includes(tag)) {
        const marker = ['STRONG', 'B'].includes(tag) ? '**' : ['EM', 'I'].includes(tag) ? '*' : '~~';
        return marker + children() + marker;
      }
      if (tag === 'HR') return markdown ? '\n---\n\n' : '\n';
      return children();
    }

    let text = walk(root, false).trim();
    let markdown = walk(root, true).trim();

    // 剔除 "Gemini said" 或 "You said" 等无用前缀
    text = text.replace(/^(Gemini|Bard|You)\s+said\s*/i, '').trim();
    markdown = markdown.replace(/^#*\s*(Gemini|Bard|You)\s+said\s*/i, '').trim();

    let json = null, jsonError = null;
    const jsonBlocks = blocks.filter(block => /^json$/i.test(block.language) || (!block.language && /^\s*[\[{]/.test(block.text)));
    const candidate = jsonBlocks.length === 1 ? jsonBlocks[0].text : (blocks.length === 0 && /^\s*[\[{]/.test(text) ? text : null);
    if (candidate != null) {
      try { json = JSON.parse(candidate.trim()); } catch (error) { jsonError = error.message; }
    }
    return { text, markdown, codeBlocks: blocks, json, jsonError, markdownSource: 'reconstructed-from-dom' };
  }

  function extractImages(element) {
    if (!(element instanceof Element)) return [];
    const seen = new Set();
    const images = [];

    for (const img of element.querySelectorAll('img')) {
      const raw = img.currentSrc || img.getAttribute('src') || '';
      if (!raw || raw.startsWith('data:image/svg+xml')) continue;
      // Filter out user avatar or icons
      if (img.classList.contains('avatar') || img.closest('.user-avatar, .logo, .header-avatar')) continue;

      let src = raw;
      try {
        if (!raw.startsWith('blob:') && !raw.startsWith('data:')) {
          src = new URL(raw, location.href).href;
        }
      } catch {}

      if (seen.has(src)) continue;
      seen.add(src);

      const width = Number(img.naturalWidth || img.width || 0);
      const height = Number(img.naturalHeight || img.height || 0);
      const rect = img.getBoundingClientRect();

      images.push({
        src,
        alt: (img.getAttribute('alt') || '').trim(),
        width,
        height,
        renderedWidth: Math.round(rect.width || 0),
        renderedHeight: Math.round(rect.height || 0),
        complete: img.complete === true && width > 0 && height > 0
      });
    }

    return images;
  }

  function messages(includeNode = false) {
    let queryNodes = Array.from(document.querySelectorAll('user-query, [data-test-id="user-query"], .user-query'));
    let responseNodes = Array.from(document.querySelectorAll('model-response, [data-test-id="model-response"], .model-response, response-container'));

    // 过滤掉包含在其他相同候选内部的子元素，防止父子重复嵌套匹配
    queryNodes = queryNodes.filter(el => !queryNodes.some(other => other !== el && other.contains(el)));
    responseNodes = responseNodes.filter(el => !responseNodes.some(other => other !== el && other.contains(el)));

    const allNodes = [
      ...queryNodes.map(el => ({ el, role: 'user' })),
      ...responseNodes.map(el => ({ el, role: 'assistant' }))
    ];

    allNodes.sort((a, b) => {
      if (a.el === b.el) return 0;
      const pos = a.el.compareDocumentPosition(b.el);
      if (pos & Node.DOCUMENT_POSITION_FOLLOWING) return -1;
      if (pos & Node.DOCUMENT_POSITION_PRECEDING) return 1;
      return 0;
    });

    const parsed = allNodes.map(({ el, role }, index) => {
      const content = extractContent(el);
      const item = {
        id: el.getAttribute('id') || `gemini-${role}-${index}`,
        role,
        ...content,
        images: extractImages(el)
      };
      if (includeNode) item.node = el;
      return item;
    });

    // 过滤掉内容为空的无效占位容器
    return parsed.filter(m => (m.text && m.text.length > 0) || m.images.length > 0);
  }

  function replyHasCompletionControls(replyNode) {
    if (!replyNode) return false;
    const scope = replyNode.closest('response-container, .response-container, model-response, [data-test-id*="response"]')
      || replyNode.parentElement?.parentElement
      || replyNode.parentElement
      || replyNode;
    const actionLabels = /(复制|copy|赞|踩|good response|bad response|share|分享|重试|redo|retry|modify|修改)/i;
    return Array.from(scope.querySelectorAll('button, [role="button"]')).some(button =>
      actionLabels.test((button.getAttribute('aria-label') || button.getAttribute('mattooltip') || button.textContent || '').trim())
    );
  }

  function detectCurrentModel() {
    const modePicker = document.querySelector('button[aria-label*="mode picker" i], button[aria-label*="模式" i]');
    if (modePicker) {
      const label = modePicker.getAttribute('aria-label') || '';
      const match = label.match(/currently\s+([^,]+)/i) || label.match(/当前为?\s*([^,，]+)/i);
      if (match && match[1].trim()) return match[1].trim();
      const text = (modePicker.innerText || modePicker.textContent || '').trim();
      if (text) return text;
    }

    const candidates = [
      document.querySelector('button[aria-label*="模型" i], button[aria-label*="Model" i], [data-test-id="bard-mode-menu-button"], [data-test-id="model-selector"], .model-selector-button'),
      document.querySelector('header .model-name, .current-model-title, [class*="model-pill"], .model-title'),
      document.querySelector('[aria-haspopup="menu"][aria-label*="Gemini" i], button[mattooltip*="Model" i]')
    ];
    for (const el of candidates) {
      if (el) {
        const text = (el.innerText || el.textContent || el.getAttribute('aria-label') || '').trim();
        if (text) return text;
      }
    }
    return null;
  }

  function matchModelOption(optionText, targetModel) {
    const normOption = String(optionText || '').toLowerCase();
    const normTarget = String(targetModel || '').toLowerCase().trim();

    // 智能识别 3.5 Flash-Lite / Lite
    if (/lite/i.test(normTarget)) {
      return /lite/i.test(normOption);
    }
    // 智能识别 3.8 Flash / Flash (必须排除 Lite)
    if (/flash/i.test(normTarget)) {
      return /flash/i.test(normOption) && !/lite/i.test(normOption);
    }
    // 智能识别 Gemini 4 / Argon
    if (/(^|\W)(4|gemini\s*4|argon)($|\W)/i.test(normTarget)) {
      return /(^|\W)(4|argon)($|\W)/i.test(normOption);
    }
    // 智能识别 3.1 Pro / Pro
    if (/(^|\W)(pro|3\.1\s*pro)($|\W)/i.test(normTarget)) {
      return /(^|\W)pro($|\W)/i.test(normOption);
    }
    return normOption.includes(normTarget);
  }

  async function switchModel(targetModel) {
    if (!targetModel || typeof targetModel !== 'string') return null;
    const current = detectCurrentModel();
    if (current && matchModelOption(current, targetModel)) {
      return { currentModel: current, switched: false, match: 'already_selected' };
    }

    // 若在旧会话中找不到模式选择器，自动点击新建聊天返回新会话
    let selectorBtn = document.querySelector('button[aria-label*="mode picker" i], button[aria-label*="模式" i], button[aria-label*="模型" i], button[aria-label*="Model" i], [data-test-id="bard-mode-menu-button"], [data-test-id="model-selector"], .model-selector-button');
    if (!selectorBtn && location.pathname.includes('/app/')) {
      const newChatButton = document.querySelector('button[aria-label*="新建聊天" i], button[aria-label*="New chat" i], a[href*="/app"]');
      if (newChatButton) {
        newChatButton.click();
        await pause(800);
      }
    }

    selectorBtn = document.querySelector('button[aria-label*="mode picker" i], button[aria-label*="模式" i], button[aria-label*="模型" i], button[aria-label*="Model" i], [data-test-id="bard-mode-menu-button"], [data-test-id="model-selector"], .model-selector-button')
      || Array.from(document.querySelectorAll('button')).find(btn => {
        const testId = btn.dataset?.testId || '';
        const label = ((btn.getAttribute('aria-label') || '') + ' ' + (btn.textContent || '')).toLowerCase();
        return testId.includes('model') || label.includes('mode picker') || (label.includes('flash') || label.includes('pro') || label.includes('argon'));
      });

    if (!selectorBtn) {
      return { currentModel: current, switched: false, warning: '未找到模型切换下拉按钮，继续使用当前模型' };
    }

    selectorBtn.click();
    await pause(500);

    const optionSelector = '[role="menuitem"], [role="option"], mat-option, .menu-item, [data-test-id*="model"], .mat-mdc-menu-item, button[mat-menu-item]';
    const options = Array.from(document.querySelectorAll(optionSelector));
    const targetOption = options.find(opt => {
      const text = opt.textContent || opt.getAttribute('aria-label') || '';
      return matchModelOption(text, targetModel);
    });

    if (targetOption) {
      targetOption.focus();
      targetOption.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, cancelable: true }));
      targetOption.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
      targetOption.dispatchEvent(new MouseEvent('pointerup', { bubbles: true, cancelable: true }));
      targetOption.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
      targetOption.click();
      await pause(400);
      (document.activeElement || window).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true }));
      document.querySelectorAll('.cdk-overlay-backdrop').forEach(el => {
        try { el.click(); } catch {}
      });
      await pause(300);
      const mainInput = document.querySelector(inputSelector);
      if (mainInput) mainInput.focus();
      const after = detectCurrentModel() || targetModel;
      return { currentModel: after, switched: true };
    } else {
      const foundLabels = options.map(opt => (opt.innerText || opt.textContent || opt.getAttribute('aria-label') || '').trim()).filter(Boolean);
      const backdrop = document.querySelector('.cdk-overlay-backdrop');
      if (backdrop) backdrop.click();
      selectorBtn.click();
      (document.activeElement || window).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true }));
      await pause(300);
      return {
        currentModel: current,
        switched: false,
        warning: `下拉菜单中未找到指定模型: ${targetModel}`,
        availableOptions: foundLabels
      };
    }
  }

  const meta = () => {
    const matchAccount = location.pathname.match(/\/u\/(\d+)\//);
    const accountIndex = matchAccount ? matchAccount[1] : null;
    const matchConv = location.pathname.match(/\/app\/([a-zA-Z0-9_-]+)/);
    const conversationId = matchConv ? matchConv[1] : null;

    return {
      url: location.href,
      accountIndex,
      conversationId,
      model: detectCurrentModel(),
      visibility: document.visibilityState,
      automationRevision: '2026-10-14-gemini-v2-models'
    };
  };

  const diagnostics = () => {
    const inputEl = document.querySelector(inputSelector);
    const container = inputEl?.closest('rich-textarea') || inputEl?.parentElement;
    let quillMethods = [];
    if (inputEl?.__quill) quillMethods.push('inputEl.__quill');
    if (container?.__quill) quillMethods.push('container.__quill');
    if (window.Quill) quillMethods.push('window.Quill');

    return {
      ...meta(),
      title: document.title,
      readyState: document.readyState,
      currentModel: detectCurrentModel(),
      messagesCount: messages().length,
      quillProbes: quillMethods,
      generatingIndicators: Array.from(document.querySelectorAll('mat-progress-bar, [aria-label*="generating" i], .loading-indicator, button[aria-label*="停止" i], button[aria-label*="Stop" i], [data-test-id="stop-button"], button.stop-generating-button')).map(el => ({ tag: el.tagName, aria: el.getAttribute('aria-label'), class: el.className.slice(0, 80) })),
      latestHasControls: messages(true).filter(m => m.role === 'assistant').length > 0 ? replyHasCompletionControls(messages(true).filter(m => m.role === 'assistant').at(-1)?.node) : false,
      sendButtonHtml: document.querySelector('button[aria-label*="send" i], button[aria-label*="发送" i]')?.outerHTML?.slice(0, 500) || null,
      cdkOverlays: Array.from(document.querySelectorAll('.cdk-overlay-backdrop, .cdk-overlay-pane')).map(el => ({
        tag: el.tagName,
        className: el.className,
        style: el.getAttribute('style')
      })),
      debug: window.__yanziGeminiDebug || null,
      editorEngine: window.__yanziGeminiEditorEngine || null,
      traceLog: window.__yanziLog || [],
      responseNodes: Array.from(document.querySelectorAll('model-response, .model-response, response-container, div[class*="response"], [data-test-id*="response"]')).map(el => ({ tag: el.tagName, className: el.className.slice(0, 100), textLen: el.textContent?.length || 0 })),
      userQueryNodes: Array.from(document.querySelectorAll('user-query, .user-query, div[class*="user-query"], [data-test-id*="user-query"]')).map(el => ({ tag: el.tagName, className: el.className.slice(0, 100), textLen: el.textContent?.length || 0 })),
      inputs: Array.from(document.querySelectorAll('textarea, rich-textarea, [contenteditable="true"]')).map(el => ({
        tag: el.tagName,
        role: el.getAttribute('role'),
        placeholder: el.getAttribute('placeholder') || el.getAttribute('data-placeholder'),
        classes: el.className,
        innerText: (el.innerText || '').slice(0, 50)
      })),
      buttons: Array.from((document.querySelector('main') || document.body).querySelectorAll('button')).map(el => ({
        label: el.getAttribute('aria-label') || el.getAttribute('mattooltip') || el.textContent?.trim().slice(0, 20),
        disabled: el.disabled || el.getAttribute('aria-disabled') === 'true'
      })).slice(-20)
    };
  };

  function checkPage() {
    // Check if redirected to Google sign-in
    if (location.hostname.includes("accounts.google.com") || document.querySelector('a[href*="accounts.google.com/ServiceLogin"], a[href*="accounts.google.com/signin"]')) {
      throw new Error("Gemini 未登录，请先在浏览器中登录 Google 账号");
    }
  }

  async function waitFor(find, deadline, description) {
    while (Date.now() < deadline) {
      checkPage();
      const value = find();
      if (value) return value;
      await pause(250);
    }
    throw new Error(description + " 超时");
  }

  function submitByEnterKey(input) {
    input.focus();
    const eventInit = {
      key: "Enter",
      code: "Enter",
      keyCode: 13,
      which: 13,
      bubbles: true,
      cancelable: true,
      composed: true
    };
    input.dispatchEvent(new KeyboardEvent("keydown", eventInit));
    input.dispatchEvent(new KeyboardEvent("keypress", eventInit));
    input.dispatchEvent(new KeyboardEvent("keyup", eventInit));

    const host = input.closest('rich-textarea') || input.parentElement;
    if (host && host !== input) {
      host.dispatchEvent(new KeyboardEvent("keydown", eventInit));
      host.dispatchEvent(new KeyboardEvent("keypress", eventInit));
      host.dispatchEvent(new KeyboardEvent("keyup", eventInit));
    }
  }

  function isVisible(el) {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function isActivelyGenerating() {
    const candidates = Array.from(document.querySelectorAll(
      'button[aria-label*="停止" i], button[aria-label*="Stop" i], [data-test-id="stop-button"], button.stop-generating-button, mat-progress-bar, [aria-label*="generating" i], .loading-indicator'
    ));
    return candidates.some(isVisible);
  }

  async function setPromptInput(input, prompt) {
    // Run against Gemini's actual page world, not the isolated extension world.
    // Quill's internal model can otherwise remain empty despite visible DOM text.
    try {
      const mainResult = await chrome.runtime.sendMessage({
        type: 'yanzi_gemini_editor_input',
        prompt
      });
      window.__yanziGeminiEditorEngine = mainResult?.engine || mainResult?.reason || 'worker-handler-unavailable';
      if (mainResult?.accepted) {
        input.focus();
        await pause(300);
        return;
      }
    } catch (_) {}
    // Fallback for earlier extension service workers.
    input.focus();
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(input);
    selection.removeAllRanges();
    selection.addRange(range);
    const inserted = document.execCommand('insertText', false, prompt);
    await pause(300);
    const actual = (input.innerText || input.textContent || input.value || '').replace(/\r/g, '').trim();
    if (!inserted || actual !== prompt.trim()) {
      throw new Error('Gemini 富文本编辑器未接受原生输入，已停止发送以防止重复或误操作');
    }
    input.focus();
    await pause(250);
  }

  async function execute(task) {
    if (task.action === "gemini_diagnostics") {
      return diagnostics();
    }
    if (task.action === "gemini_messages") {
      return { ...meta(), messages: messages() };
    }

    const waitBusyDeadline = Date.now() + 6000;
    while (busy && Date.now() < waitBusyDeadline) {
      await pause(150);
    }
    if (busy) throw new Error("Gemini 页面正在执行其他请求");
    busy = true;
    try {
      checkPage();
      const defaultTimeoutSeconds = 180;
      const requestedTimeoutSeconds = Number(task.timeoutSeconds);
      const deadline = requestedTimeoutSeconds === 0
        ? Number.POSITIVE_INFINITY
        : Date.now() + Math.min(1200, Math.max(10, requestedTimeoutSeconds || defaultTimeoutSeconds)) * 1000;

      if (!window.__yanziLog) window.__yanziLog = [];
      const log = msg => {
        window.__yanziLog.push(`[${new Date().toISOString().slice(11, 19)}] ${msg}`);
        if (window.__yanziLog.length > 200) window.__yanziLog.shift();
      };
      log("execute_start: " + task.action);

      if (task.action === "gemini_new_chat") {
        const newChatButton = document.querySelector('button[aria-label*="新建聊天" i], button[aria-label*="New chat" i], a[href*="/app"]');
        if (newChatButton) {
          newChatButton.click();
          await pause(500);
        } else {
          const accountPrefix = meta().accountIndex ? `/u/${meta().accountIndex}` : '';
          location.href = `https://gemini.google.com${accountPrefix}/app`;
        }
        return meta();
      }

      if (task.action === "gemini_model_switch") {
        const switchResult = await switchModel(task.model);
        return { ...meta(), ...switchResult };
      }

      let modelSwitchResult = null;
      if (task.model) {
        log("switching_model: " + task.model);
        modelSwitchResult = await switchModel(task.model);
        log("switch_done: " + JSON.stringify(modelSwitchResult));
      }

      if (typeof task.prompt !== "string" || !task.prompt.trim()) {
        throw new Error("消息内容不能为空");
      }

      log("received_prompt: " + task.prompt);
      log("waiting_for_input");
      const input = await waitFor(() => document.querySelector(inputSelector), deadline, "等待 Gemini 输入框");
      const draft = (input.innerText || input.textContent || input.value || "").trim();
      if (draft) {
        input.replaceChildren();
        input.dispatchEvent(new InputEvent("input", { bubbles: true, composed: true }));
        await pause(200);
      }

      const generatingNow = isActivelyGenerating();
      if (generatingNow) throw new Error("Gemini 正在生成回复");

      const previous = messages();
      const userCount = previous.filter(m => m.role === "user").length;
      const assistantCount = previous.filter(m => m.role === "assistant").length;
      log(`counts_before: user=${userCount}, assistant=${assistantCount}`);

      // 写入 Prompt
      log("setPromptInput_start");
      await setPromptInput(input, task.prompt);
      log("setPromptInput_done");

      function clickButton(btn) {
        // Angular sends on button click. Double-clicking child and parent can duplicate submissions.
        btn.click();
      }

      // 尝试寻找激活的发送按钮并点击，或通过回车键提交
      let sent = false;
      const sendDeadline = Math.min(deadline, Date.now() + 10000);
      while (Date.now() < sendDeadline) {
        const buttons = Array.from(document.querySelectorAll('button, [role="button"]'));
        const activeBtn = buttons.find(b => {
          const str = ((b.getAttribute('aria-label') || '') + ' ' + (b.getAttribute('mattooltip') || '') + ' ' + (b.textContent || '')).toLowerCase();
          const testId = (b.dataset?.testId || '').toLowerCase();
          const cls = (typeof b.className === 'string' ? b.className : '').toLowerCase();
          return (str.includes('send message') || str.includes('发送消息') || testId.includes('send') || cls.includes('send-button')) && !b.disabled && b.getAttribute('aria-disabled') !== 'true' && isVisible(b);
        });
        if (activeBtn) {
          log("clicking_send_button: " + (activeBtn.getAttribute('aria-label') || 'send'));
          clickButton(activeBtn);
          sent = true;
          // Never click again while submission is unconfirmed.
          break;
        } else {
          input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
          await pause(250);
        }
      }

      await pause(200);
      if (!sent) {
        log("dispatching_enter_key");
        submitByEnterKey(input);
      }

      log("button_or_enter_sent: " + sent);
      log("waitFor_sent_confirm_start");

      // 等待发送已被接收
      await waitFor(() => {
        const curDraft = (input.innerText || input.textContent || input.value || "").trim();
        const currentMessages = messages();
        if (currentMessages.filter(m => m.role === "user").length > userCount) return true;
        if (currentMessages.filter(m => m.role === "assistant").length > assistantCount) return true;
        if (isActivelyGenerating()) return true;
        return !curDraft;
      }, Math.min(deadline, Date.now() + 20000), "确认消息已发送");
      log("waitFor_sent_confirm_done");

      // 监控生成完成
      log("generation_loop_start");
      let sawGenerating = false;
      let lastText = "", changedAt = Date.now();
      let tickCount = 0;
      while (Date.now() < deadline) {
        checkPage();
        const current = messages(true).filter(m => m.role === "assistant");
        const latest = current.length > assistantCount ? current.at(-1) : null;
        const isGenerating = isActivelyGenerating();

        if (isGenerating) {
          sawGenerating = true;
        }

        const currentText = (latest?.text || "").trim();
        if (currentText && currentText !== lastText) {
          lastText = currentText;
          changedAt = Date.now();
          log(`text_changed: len=${lastText.length}`);
        }

        const hasPayload = Boolean(lastText.length > 0);
        const hasControls = Boolean(latest?.node && replyHasCompletionControls(latest.node));

        tickCount++;
        if (tickCount % 10 === 0) {
          log(`loop_heartbeat: isGen=${isGenerating}, hasPayload=${hasPayload}, hasControls=${hasControls}, sawGen=${sawGenerating}, idleMs=${Date.now() - changedAt}`);
        }

        window.__yanziGeminiDebug = {
          isGenerating,
          hasPayload,
          hasControls,
          sawGenerating,
          timeDiff: Date.now() - changedAt,
          lastTextLen: lastText.length,
          currentCount: current.length,
          assistantCount,
          latestIsNull: !latest
        };

        if (hasPayload && !isGenerating) {
          if (hasControls || (sawGenerating && !isGenerating) || (Date.now() - changedAt >= 1500)) {
            log("generation_loop_success: payload_ready");
            const { node, ...cleanLatest } = latest || {};
            return {
              ...meta(),
              ...cleanLatest,
              sentPrompt: task.prompt,
              images: cleanLatest?.images || [],
              modelSwitch: modelSwitchResult,
              messageId: cleanLatest?.id || null
            };
          }
        }

        await pause(300);
      }

      throw new Error("等待 Gemini 回复超时；消息可能已发送，请刷新或查询历史");
    } finally {
      busy = false;
    }
  }

  // 挂载消息监听与全局辅助钩子
  window.__yanziGeminiGetMessages = messages;
  window.__yanziGeminiDiagnostics = diagnostics;
  window.__yanziGeminiMeta = meta;
  window.__yanziGeminiIsGenerating = isActivelyGenerating;

  chrome.runtime.onMessage.addListener((message, sender, respond) => {
    if (message.type !== "yanzi_gemini_task") return;
    execute(message.task)
      .then(data => respond({ status: "success", data }))
      .catch(error => respond({ status: "error", data: { diagnostics: diagnostics() }, message: error.message }));
    return true;
  });
})();
