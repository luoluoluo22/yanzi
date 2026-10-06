(() => {
  let busy = false;
  const inputSelector = '#prompt-textarea, main [contenteditable="true"][role="textbox"]';
  const sendSelector = 'button[data-testid="send-button"], button[aria-label="Send prompt"], button[aria-label="发送提示"], main button[aria-label="发送"], main button[aria-label="Send"]';
  const stopSelector = 'button[data-testid="stop-button"], button[aria-label="Stop generating"], button[aria-label="停止生成"], main button[aria-label="停止"], main button[aria-label="Stop"]';
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  function extractContent(element) {
    const root = element.querySelector('[data-markdown-text-style], .markdown') || element;
    const blocks = [];
    const blockSelector = '[data-markdown-copy="code-block"], pre';
    const blockElements = Array.from(root.querySelectorAll(blockSelector)).filter(el => !el.parentElement?.closest(blockSelector));
    for (const block of blockElements) {
      const code = block.querySelector('code');
      const header = block.querySelector('[data-markdown-copy="exclude"]')?.cloneNode(true);
      header?.querySelectorAll('button, svg, [hidden], .sr-only').forEach(node => node.remove());
      const label = header?.textContent.trim();
      const language = code?.className.match(/language-([\w+-]+)/)?.[1] || label?.match(/^[\w+#.-]+$/)?.[0]?.toLowerCase() || '';
      blocks.push({ language, text: (code || block).textContent });
    }
    const blockMap = new Map(blockElements.map((el, i) => [el, blocks[i]]));
    const ignored = el => el.matches('[data-markdown-copy="exclude"], .sr-only, .turn-action-controls, button, svg, [hidden]');
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
    const text = walk(root, false).trim();
    const markdown = walk(root, true).trim();
    let json = null, jsonError = null;
    const jsonBlocks = blocks.filter(block => block.language === 'json');
    const candidate = jsonBlocks.length === 1 ? jsonBlocks[0].text : blocks.length === 0 && /^[\[{]/.test(text) ? text : null;
    if (candidate != null) { try { json = JSON.parse(candidate); } catch (error) { jsonError = error.message; } }
    return { text, markdown, codeBlocks: blocks, json, jsonError, markdownSource: 'reconstructed-from-dom' };
  }

  function extractImages(element) {
    if (!(element instanceof Element)) return [];

    const seen = new Set();
    const images = [];

    for (const img of element.querySelectorAll('img')) {
      const raw = img.currentSrc || img.getAttribute('src') || '';
      if (!raw || raw.startsWith('data:image/svg+xml')) continue;

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

  function usableGeneratedImages(message) {
    return (message?.images || []).filter(image =>
      image.complete === true &&
      (
        image.width >= 256 ||
        image.height >= 256 ||
        image.renderedWidth >= 256 ||
        image.renderedHeight >= 256 ||
        /oaiusercontent|backend-api\/files|imagegen|generated/i.test(image.src)
      )
    );
  }

  const messages = () => {
    const legacy = Array.from(document.querySelectorAll('[data-message-author-role]'));
    const searchUnits = legacy.length
      ? []
      : Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]'))
          .filter(el => /:(user|assistant)$/.test(el.getAttribute('data-chatgpt-search-unit-key')));

    if (legacy.length || searchUnits.length) {
      const nodes = legacy.length ? legacy : searchUnits;
      return nodes.map(el => {
        const content = el.querySelector('.markdown, [data-content-search-unit-key]') || el;
        return {
          id: (el.getAttribute('data-message-id') || el.getAttribute('data-chatgpt-search-message-ids') || '').trim().split(/\s+/)[0] || null,
          role: el.getAttribute('data-message-author-role') ||
            el.getAttribute('data-chatgpt-search-unit-key').split(':').at(-1),
          ...extractContent(content),
          images: extractImages(el)
        };
      });
    }

    // ChatGPT 2026-10 DOM:
    // - user turns are rendered as [data-user-message-bubble="true"]
    // - assistant text/code lives in a MarkdownRoot-* container.
    // Keep this fallback structural instead of relying on transient hashed suffixes.
    const modern = [
      ...Array.from(document.querySelectorAll('[data-user-message-bubble="true"]'))
        .map(el => ({ el, role: 'user' })),
      ...Array.from(document.querySelectorAll('[class*="MarkdownRoot-"]'))
        .filter(el => !el.closest('[data-user-message-bubble="true"]'))
        .map(el => ({ el, role: 'assistant' }))
    ];

    modern.sort((a, b) => {
      if (a.el === b.el) return 0;
      const pos = a.el.compareDocumentPosition(b.el);
      if (pos & Node.DOCUMENT_POSITION_FOLLOWING) return -1;
      if (pos & Node.DOCUMENT_POSITION_PRECEDING) return 1;
      return 0;
    });

    return modern.map(({ el, role }, index) => ({
      id: el.getAttribute('data-message-id') ||
        el.getAttribute('data-chatgpt-search-message-ids') ||
        'modern-' + role + '-' + index,
      role,
      ...extractContent(el),
      images: extractImages(el)
    }));
  };
  function replyHasCompletionControls() {
    const legacy = document.querySelector('[data-message-author-role="assistant"]');
    if (legacy) return true;

    const units = Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]'))
      .filter(el => /:assistant$/.test(el.getAttribute('data-chatgpt-search-unit-key')));
    let node = units.at(-1);
    for (let depth = 0; depth < 8 && node; depth++, node = node.parentElement) {
      if (node.querySelector('.turn-action-controls button[aria-label="复制"], .turn-action-controls button[aria-label="Copy"], .turn-action-controls button[aria-label="评价回复"]')) return true;
    }

    // Current ChatGPT DOM no longer exposes the old role/unit attributes.
    // A stable MarkdownRoot with no active stop control is sufficient; the
    // caller already requires the payload to remain unchanged for 3 seconds.
    return Boolean(
      document.querySelector('[class*="MarkdownRoot-"]') &&
      !document.querySelector(stopSelector)
    );
  }
  function temporaryEvidence() {
    const labels = /^(临时聊天|Temporary(?: chat)?)$/i;
    const heading = Array.from(document.querySelectorAll('h1, h2, header [role="heading"]')).find(el => !el.closest('[data-message-author-role], [data-chatgpt-search-unit-key], [inert], [aria-hidden="true"], [hidden]') && !el.classList.contains('invisible') && labels.test(el.textContent.trim()));
    if (heading) return 'heading';
    const control = Array.from(document.querySelectorAll('button, [role="switch"]')).find(el => {
      if (el.closest('[data-message-author-role], [data-chatgpt-search-unit-key]')) return false;
      const label = el.getAttribute('aria-label') || el.textContent.trim();
      return /^(关闭|退出)临时聊天$|^(Turn off|Exit|Close) temporary chat$/i.test(label) || (labels.test(label) && (el.getAttribute('aria-pressed') === 'true' || el.getAttribute('aria-checked') === 'true'));
    });
    if (control) return 'active-control';
    // In the current UI an unsaved temporary conversation shows "Save chat"
    // instead of the initial heading. Require both this control and the mode flag.
    const saveControl = Array.from(document.querySelectorAll('button')).find(el => !el.closest('[data-message-author-role], [data-chatgpt-search-unit-key]') && /^(Save chat|保存聊天|保存对话)$/.test(el.getAttribute('aria-label') || el.textContent.trim()));
    if (saveControl && new URL(location.href).searchParams.get('temporary-chat') === 'true') return 'save-chat-control';
    return null;
  }
  const meta = () => ({ url: location.href, conversationId: location.pathname.match(/^\/c\/([^/]+)/)?.[1] || null, visibility: document.visibilityState, temporary: Boolean(temporaryEvidence()), temporaryEvidence: temporaryEvidence() });
  const pageSnapshots = () => {
    const legacy = Array.from(document.querySelectorAll(
      '[data-message-author-role="assistant"], [data-chatgpt-search-unit-key$=":assistant"]'
    ));
    const nodes = legacy.length
      ? legacy
      : Array.from(document.querySelectorAll('[class*="MarkdownRoot-"]'))
          .filter(el => !el.closest('[data-user-message-bubble="true"]'));

    return nodes.map((el, index) => ({
      id: (el.getAttribute('data-message-id') || el.getAttribute('data-chatgpt-search-message-ids') || '').trim().split(/\s+/)[0] || 'modern-assistant-' + index,
      renderedText: el.innerText,
      html: el.outerHTML,
      codeBlocks: Array.from(el.querySelectorAll('[data-markdown-copy="code-block"] code, pre code')).map(code => ({ text: code.textContent }))
    }));
  };

  const pageImages = () => Array.from(document.querySelectorAll('main img')).map((img, index) => {
    const rect = img.getBoundingClientRect();
    const ancestors = [];
    let node = img.parentElement;
    for (let depth = 0; depth < 6 && node && node.tagName !== 'MAIN'; depth++, node = node.parentElement) {
      ancestors.push({
        tag: node.tagName,
        className: String(node.className || '').slice(0, 240),
        testId: node.getAttribute('data-testid'),
        role: node.getAttribute('role'),
        ariaLabel: node.getAttribute('aria-label')
      });
    }
    const messageNode = img.closest(
      '[data-message-author-role], [data-chatgpt-search-unit-key]'
    );
    const messageRole =
      messageNode?.getAttribute('data-message-author-role') ||
      (messageNode?.getAttribute('data-chatgpt-search-unit-key') || '')
        .split(':')
        .at(-1) ||
      '';

    return {
      index,
      src: img.currentSrc || img.getAttribute('src') || '',
      alt: img.getAttribute('alt') || '',
      messageRole,
      width: Number(img.naturalWidth || img.width || 0),
      height: Number(img.naturalHeight || img.height || 0),
      renderedWidth: Math.round(rect.width || 0),
      renderedHeight: Math.round(rect.height || 0),
      complete: img.complete === true,
      className: String(img.className || '').slice(0, 240),
      testId: img.getAttribute('data-testid'),
      ancestors,
      html: img.outerHTML.slice(0, 1800)
    };
  });

  const generatedPageImages = () => pageImages().filter(image => {
    if (image.complete !== true) return false;
    if (image.messageRole === 'user') return false;

    const explicitGenerated =
      image.ancestors.some(node =>
        node.testId === 'generated-image-preview' ||
        node.testId === 'generated-image-gallery' ||
        /generated[-_ ]?image|image[-_ ]?generation/i.test(
          [node.testId, node.className, node.ariaLabel]
            .filter(Boolean)
            .join(' ')
        )
      ) ||
      /已生成图像|generated image|image generated/i.test(image.alt || '');

    const assistantLarge =
      image.messageRole === 'assistant' &&
      Math.max(image.width, image.height) >= 512 &&
      Math.max(image.renderedWidth, image.renderedHeight) >= 220;

    const pageLargeFallback =
      !image.messageRole &&
      image.width >= 768 &&
      image.height >= 768 &&
      image.renderedWidth >= 220 &&
      image.renderedHeight >= 220;

    return explicitGenerated || assistantLarge || pageLargeFallback;
  });

  async function imageToDataUrl(image) {
    let fetchError = null;
    try {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 6000);
      try {
        const response = await fetch(image.src, { signal: controller.signal });
        if (!response.ok) throw new Error('HTTP ' + response.status);

        const blob = await response.blob();
        if (!blob.type.startsWith('image/')) {
          throw new Error('生成结果不是图片：' + (blob.type || 'unknown'));
        }
        if (blob.size > 12 * 1024 * 1024) {
          throw new Error('生成图片超过 12MB，暂不缓存');
        }

        const dataUrl = await new Promise((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result || ''));
          reader.onerror = () => reject(reader.error || new Error('读取图片失败'));
          reader.readAsDataURL(blob);
        });

        return {
          ...image,
          mimeType: blob.type,
          byteLength: blob.size,
          dataUrl,
          captureMethod: 'fetch'
        };
      } finally {
        clearTimeout(timer);
      }
    } catch (error) {
      fetchError = error;
    }

    // blob: URLs created by the page can be inaccessible from an extension
    // isolated world. The already-rendered <img> is still drawable, so fall
    // back to canvas without depending on the blob URL being fetchable.
    const element = Array.from(document.querySelectorAll('main img')).find(img =>
      (img.currentSrc || img.getAttribute('src') || '') === image.src
    );
    if (!element || !element.complete || !element.naturalWidth || !element.naturalHeight) {
      throw new Error('读取生成图片失败：' + (fetchError?.message || '图片节点未就绪'));
    }

    try {
      const canvas = document.createElement('canvas');
      canvas.width = element.naturalWidth;
      canvas.height = element.naturalHeight;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('无法创建 canvas');
      context.drawImage(element, 0, 0);
      const dataUrl = canvas.toDataURL('image/png');
      const base64Length = Math.max(0, dataUrl.length - dataUrl.indexOf(',') - 1);
      const byteLength = Math.floor(base64Length * 3 / 4);
      if (byteLength > 12 * 1024 * 1024) {
        throw new Error('生成图片超过 12MB，暂不缓存');
      }
      return {
        ...image,
        mimeType: 'image/png',
        byteLength,
        dataUrl,
        captureMethod: 'canvas'
      };
    } catch (canvasError) {
      throw new Error(
        '读取生成图片失败：' +
        (fetchError?.message || 'blob 不可读') +
        '；canvas：' +
        (canvasError?.message || String(canvasError))
      );
    }
  }

  const diagnostics = () => ({
    ...meta(), title: document.title, readyState: document.readyState,
    inputs: Array.from(document.querySelectorAll('textarea, [contenteditable="true"]')).map(el => ({ tag: el.tagName, id: el.id, role: el.getAttribute('role'), placeholder: el.getAttribute('placeholder') })),
    buttons: Array.from((document.querySelector('main') || document).querySelectorAll('button')).map(el => ({ label: el.getAttribute('aria-label'), testId: el.getAttribute('data-testid'), disabled: el.disabled })).slice(-20),
    notice: document.querySelector(inputSelector) ? '' : (document.querySelector('main')?.innerText || document.body.innerText || '').slice(0, 600)
  });
  // A separate synchronous read handler also upgrades diagnostics on already-injected pages.
  // It never sends a prompt, so the legacy handler cannot cause duplicate submissions.
  if (!globalThis.__yanziChatGptDiagnosticsInstalled) {
    globalThis.__yanziChatGptDiagnosticsInstalled = true;
    chrome.runtime.onMessage.addListener((message, sender, respond) => {
      if (message.type !== 'yanzi_chatgpt_task' || message.task.action !== 'chatgpt_messages') return;
      try { checkPage(); respond({ status: 'success', data: { ...meta(), messages: messages(), ...(message.task.includePageSnapshot ? { pageSnapshots: pageSnapshots(), pageImages: pageImages() } : {}), diagnostics: diagnostics() } }); }
      catch (error) { respond({ status: 'error', data: { diagnostics: diagnostics() }, message: error.message }); }
    });
  }
  if (globalThis.__yanziChatGptInstalled) return;
  globalThis.__yanziChatGptInstalled = true;
  function checkPage() {
    if (document.querySelector('a[href*="/auth/login"], button[data-testid="login-button"]')) throw new Error("ChatGPT 未登录，请在浏览器中登录");
    if (document.querySelector('iframe[src*="challenges.cloudflare.com"], #challenge-running')) throw new Error("ChatGPT 需要人工完成验证");
  }
  async function waitFor(find, deadline, description) {
    while (Date.now() < deadline) {
      checkPage();
      const value = find();
      if (value) return value;
      await pause(250);
    }
    throw new Error(description + "超时");
  }
  function attachmentButton() {
    return Array.from(document.querySelectorAll('button')).find(button => {
      if (button.closest('[data-message-author-role], [data-chatgpt-search-unit-key]')) return false;
      const label = [
        button.getAttribute('aria-label'),
        button.getAttribute('title'),
        button.textContent
      ].filter(Boolean).join(' ').trim();
      return /添加文件等内容|添加文件|附件|上传文件|attach files|add files|upload files|add photos/i.test(label);
    }) || null;
  }

  function decodeAttachmentBase64(value) {
    const clean = String(value || '').replace(/\s+/g, '');
    const binary = atob(clean);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) {
      bytes[index] = binary.charCodeAt(index);
    }
    return bytes;
  }

  async function uploadAttachments(attachments, deadline) {
    const items = Array.isArray(attachments) ? attachments : [];
    if (!items.length) return [];

    let fileInput = document.querySelector('input[type="file"]');
    if (!fileInput) {
      const button = attachmentButton();
      if (button) {
        button.click();
        await pause(250);
      }

      fileInput = await waitFor(
        () => document.querySelector('input[type="file"]'),
        Math.min(deadline, Date.now() + 5000),
        '等待 ChatGPT 文件上传控件'
      );
    }

    const transfer = new DataTransfer();
    const uploaded = [];

    for (const item of items) {
      const name = String(item?.name || '').trim();
      const mimeType = String(item?.mimeType || 'application/octet-stream').trim();
      const bytes = decodeAttachmentBase64(item?.base64);

      if (!name || !bytes.length) throw new Error('附件数据无效');

      transfer.items.add(new File([bytes], name, {
        type: mimeType,
        lastModified: Date.now()
      }));

      uploaded.push({
        name,
        mimeType,
        byteLength: Number(item?.byteLength || bytes.length)
      });
    }

    const filesDescriptor =
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'files');
    if (filesDescriptor?.set) {
      filesDescriptor.set.call(fileInput, transfer.files);
    } else {
      fileInput.files = transfer.files;
    }

    fileInput.dispatchEvent(new Event('input', {
      bubbles: true,
      composed: true
    }));
    fileInput.dispatchEvent(new Event('change', {
      bubbles: true,
      composed: true
    }));

    // React may replace the hidden <input> after consuming FileList. Do not
    // require the same node to retain files; wait for upload indicators to
    // settle and for the composer to become sendable instead.
    await pause(700);
    await waitFor(() => {
      const scope =
        document.querySelector('form') ||
        document.querySelector('[data-type="unified-composer"]') ||
        document.querySelector('main') ||
        document.body;
      const text = scope.innerText || '';
      if (/上传失败|文件上传失败|upload failed|unsupported file|不支持.*文件/i.test(text)) {
        throw new Error('ChatGPT 文件上传失败');
      }

      const uploading =
        /上传中|正在上传|正在处理文件|uploading|processing file/i.test(text);
      const send = document.querySelector(sendSelector);
      return !uploading && send && !send.disabled ? true : null;
    }, Math.min(deadline, Date.now() + 45000), '等待附件上传完成');

    return uploaded;
  }

  async function execute(task) {
    if (busy) throw new Error("页面正在执行其他请求");
    busy = true;
    try {
      const defaultTimeoutSeconds = task.action === "chatgpt_image" ? 900 : 180;
      const deadline = Date.now() + Math.min(
        1200,
        Math.max(10, Number(task.timeoutSeconds) || defaultTimeoutSeconds)
      ) * 1000;
      if (task.action === "chatgpt_messages") {
        checkPage();
        return { ...meta(), messages: messages() };
      }
      if (task.action === "chatgpt_capture_images") {
        checkPage();
        const generated = generatedPageImages();
        if (!generated.length) throw new Error("当前聊天没有可抓取的生成图片");
        const images = [];
        for (const image of generated.slice(0, 4)) {
          images.push(await imageToDataUrl(image));
        }
        return { ...meta(), images };
      }
      const input = await waitFor(() => document.querySelector(inputSelector), deadline, "等待 ChatGPT 输入框");
      if (task.temporary === true) await waitFor(() => temporaryEvidence(), Math.min(deadline, Date.now() + 15000), '确认临时聊天模式（未确认时不会发送）');
      if (task.action === "chatgpt_new_chat") return meta();
      if (typeof task.prompt !== "string" || !task.prompt.trim()) throw new Error("消息内容不能为空");
      const draft = (input.value || input.innerText || "").trim();
      if (draft && draft !== task.prompt.trim()) throw new Error("输入框已有其他草稿，请先处理草稿");
      if (document.querySelector(stopSelector)) throw new Error("ChatGPT 正在生成回复");
      const previous = messages();
      const userCount = previous.filter(m => m.role === "user").length;
      const assistantCount = previous.filter(m => m.role === "assistant").length;
      const previousGeneratedImages = new Set(
        generatedPageImages().map(image => image.src)
      );
      if (draft) {
        // ChatGPT restores the same draft across new tabs. Submit only the exact requested text.
      } else if (input.tagName === "TEXTAREA") {
        Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value").set.call(input, task.prompt);
      } else {
        input.replaceChildren(...task.prompt.split("\n").map(line => {
          const p = document.createElement("p");
          p.textContent = line || "\u200b";
          return p;
        }));
      }
      input.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: task.prompt }));

      const uploadedFiles = await uploadAttachments(task.attachments, deadline);

      const send = await waitFor(() => {
        const button = document.querySelector(sendSelector);
        return button && !button.disabled ? button : null;
      }, Math.min(deadline, Date.now() + 10000), "等待发送按钮");
      if (task.temporary === true && !temporaryEvidence()) throw new Error('临时聊天状态已改变，未发送消息');
      send.click();
      await waitFor(() => messages().filter(m => m.role === "user").length > userCount, Math.min(deadline, Date.now() + 15000), "确认消息已发送");
      let last = "", lastImages = "", changedAt = Date.now();
      while (Date.now() < deadline) {
        checkPage();
        const current = messages().filter(m => m.role === "assistant");
        const latest = current.length > assistantCount ? current.at(-1) : null;
        const generating = document.querySelector(stopSelector) || document.querySelector('main button[aria-label*="停止"], main button[aria-label^="Stop"], [data-is-streaming="true"]');
        const newPageImages = task.action === "chatgpt_image"
          ? generatedPageImages().filter(image => !previousGeneratedImages.has(image.src))
          : [];
        const responseImages = task.action === "chatgpt_image"
          ? newPageImages
          : usableGeneratedImages(latest);
        const imageSignature = JSON.stringify(responseImages.map(image => [
          image.src,
          image.width,
          image.height,
          image.complete
        ]));
        if (latest?.text !== last || imageSignature !== lastImages) {
          last = latest?.text || "";
          lastImages = imageSignature;
          changedAt = Date.now();
        }

        if (
          task.action === "chatgpt_image" &&
          responseImages.length > 0 &&
          !generating &&
          Date.now() - changedAt >= 1500
        ) {
          const images = [];
          for (const image of responseImages.slice(0, 4)) {
            images.push(await imageToDataUrl(image));
          }
          return {
            ...meta(),
            ...(latest || {}),
            images,
            uploadedFiles,
            messageId: latest?.id || null
          };
        }

        const hasExpectedPayload = Boolean(last || responseImages.length);
        // Never return an old reply, nor stable partial text/image while a stop control exists.
        if (
          task.action !== "chatgpt_image" &&
          hasExpectedPayload &&
          !generating &&
          replyHasCompletionControls() &&
          Date.now() - changedAt >= 3000
        ) {
          if (task.temporary === true && !temporaryEvidence()) throw new Error('回复完成后无法确认临时状态，已保留页面供检查');
          return {
            ...meta(),
            ...latest,
            images: latest?.images || [],
            uploadedFiles,
            messageId: latest.id
          };
        }
        const error = document.querySelector('[data-testid="conversation-turn-error"]');
        if (error) throw new Error(error.innerText || "ChatGPT 返回错误");
        await pause(500);
      }
      throw new Error("等待回复超时；消息可能已发送，请查询聊天后再决定是否重试");
    } finally { busy = false; }
  }
  chrome.runtime.onMessage.addListener((message, sender, respond) => {
    if (message.type !== "yanzi_chatgpt_task" || message.task.action === 'chatgpt_messages') return;
    execute(message.task).then(data => respond({ status: "success", data }))
      .catch(error => respond({ status: "error", data: { diagnostics: diagnostics() }, message: error.message }));
    return true;
  });
})();
