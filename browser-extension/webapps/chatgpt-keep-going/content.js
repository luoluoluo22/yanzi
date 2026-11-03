/* Yanzi ChatGPT Keep Going (自动续聊) 网页小程序
   采用 Yanzi WebApp SDK 标准生命周期规范。
   外浮层设计：挂载在 document.body，独立漂浮于输入框正上方外侧 10px，对原生界面实现零侵入、零高度干扰。
*/

(() => {
  'use strict';

  const APP_ID = 'chatgpt.keep-going';
  const APP_VERSION = '0.2.0';

  const DEFAULTS = {
    message: '继续',
    delaySeconds: 60,
    stopText: '',
    stopMode: 'contains', // contains | equals | regex
    maxSends: 0,
    stopAt: 0
  };

  const SEL = {
    editor: '#prompt-textarea, main [contenteditable="true"][role="textbox"], textarea[placeholder*="Message"], textarea[placeholder*="发送消息"]',
    send: 'button[data-testid="send-button"], button[data-testid="composer-submit-button"], button[aria-label="Send prompt"], button[aria-label="发送提示"], main button[aria-label="发送"], main button[aria-label="Send"], form button[type="submit"]',
    stop: 'button[data-testid="stop-button"], button[aria-label="Stop generating"], button[aria-label="停止生成"], main button[aria-label="停止"], [data-is-streaming="true"], .result-streaming',
    voice: 'button[data-testid="composer-speech-button"], button[data-testid="voice-mode-button"], button[aria-label="Start voice mode"], button[aria-label="开始语音对话"]'
  };

  // 核心业务定义
  defineYanziWebApp({
    id: APP_ID,
    name: '不要停',
    version: APP_VERSION,
    defaultSettings: DEFAULTS,
    syncKeys: ['settings'],

    async onActivate(ctx) {
      let settings = { ...DEFAULTS, ...ctx.settings };
      let enabled = false;
      let sending = false;
      let awaitingReply = false;
      let sentCount = 0;
      let chatPath = '';
      let beforeReply = '';
      let waitingSince = 0;
      let deadline = 0;
      let seenReply = '';
      let replyChangedAt = 0;
      let knownUserTurn = '';
      let host = null;
      let button = null;
      let dialogHost = null;
      let toastTimeout = null;
      let popupOpen = false;
      let lastVerifiedReply = '';
      let lastStopCheck = '尚未检查';
      let lastStopReason = '';

      const editor = () => document.querySelector(SEL.editor);
      const sendButton = () => document.querySelector(SEL.send);
      const generating = () => !!document.querySelector(SEL.stop);
      const pathKey = () => location.pathname.replace(/\/+$/, '') || '/';
      const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

      function lastReply() {
        const legacy = Array.from(document.querySelectorAll('[data-message-author-role="assistant"]'));
        const units = legacy.length ? [] : Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]'))
          .filter(el => /:assistant$/.test(el.getAttribute('data-chatgpt-search-unit-key') || ''));
        const modern = legacy.length || units.length ? [] : Array.from(document.querySelectorAll('[class*="MarkdownRoot-"]'))
          .filter(el => !el.closest('[data-user-message-bubble="true"]') && !el.parentElement?.closest('[class*="MarkdownRoot-"]'));
        const list = legacy.length ? legacy : units.length ? units : modern;
        const node = list.at(-1);
        if (!node) return null;

        // 若最新一条发言是用户本人发出的，表示 AI 尚未开始/完成回复
        if (legacy.length) {
          const turns = document.querySelectorAll('[data-message-author-role]');
          if (turns[turns.length - 1]?.getAttribute('data-message-author-role') === 'user') return null;
        } else if (units.length) {
          const turns = Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]'));
          if (/:user$/.test(turns.at(-1)?.getAttribute('data-chatgpt-search-unit-key') || '')) return null;
        } else {
          const users = document.querySelectorAll('[data-user-message-bubble="true"]');
          const newestUser = users[users.length - 1];
          if (newestUser && (node.compareDocumentPosition(newestUser) & Node.DOCUMENT_POSITION_FOLLOWING)) return null;
        }

        const content = node.querySelector('.markdown, [data-content-search-unit-key], [class*="MarkdownRoot-"]') || node;
        const text = (content.innerText || content.textContent || '').trim();
        const id = node.getAttribute('data-message-id') || node.getAttribute('data-chatgpt-search-message-ids') || '';
        return { text, fingerprint: [list.length, id, text.length, text.slice(0, 120), text.slice(-120)].join('|') };
      }

      function latestUserTurn() {
        const users = Array.from(document.querySelectorAll('[data-message-author-role="user"], [data-user-message-bubble="true"], [data-chatgpt-search-unit-key$=":user"]'));
        const last = users.at(-1);
        return users.length + '|' + (last?.getAttribute('data-message-id') || '') + '|' + (last?.textContent || '').slice(-180);
      }

      function draft() {
        const el = editor();
        return el ? (el.tagName === 'TEXTAREA' ? el.value : el.innerText || el.textContent || '').trim() : '';
      }

      function normalizeReply(value) {
        return String(value || '')
          .normalize('NFKC')
          .replace(/[\u200B-\u200D\u2060\uFEFF]/g, '')
          .replace(/\u00a0/g, ' ')
          .replace(/\r\n?/g, '\n')
          .trim();
      }

      function stopMatches(text) {
        const target = normalizeReply(settings.stopText);
        if (!target) return false;
        const actual = normalizeReply(text);
        if (settings.stopMode === 'equals') return actual === target;
        if (settings.stopMode === 'regex') {
          try {
            return new RegExp(target, 'i').test(actual);
          } catch {
            return false;
          }
        }
        return actual.includes(target);
      }

      function timeReached() {
        return settings.stopAt > 0 && Date.now() >= settings.stopAt;
      }

      function verifyStop(reply) {
        if (timeReached()) {
          lastStopCheck = '已到达指定停止时间';
          lastStopReason = lastStopCheck;
          return lastStopReason;
        }
        if (!reply || !normalizeReply(reply.text)) {
          lastStopCheck = '尚未检测到 AI 完整回复';
          return '';
        }
        lastVerifiedReply = normalizeReply(reply.text);
        const matched = stopMatches(lastVerifiedReply);
        lastStopCheck = matched ? '最新回复命中停止关键词' : '最新回复未命中停止关键词';
        if (matched) lastStopReason = lastStopCheck;
        return matched ? lastStopReason : '';
      }

      function localDatetime(value) {
        if (!value || !Number.isFinite(value)) return '';
        const d = new Date(value);
        const two = x => String(x).padStart(2, '0');
        return [d.getFullYear(), two(d.getMonth() + 1), two(d.getDate())].join('-') + 'T' + two(d.getHours()) + ':' + two(d.getMinutes());
      }

      function paint(mode, label, hint) {
        if (!button) return;
        button.dataset.mode = mode;
        button.textContent = label;
        button.title = hint || '左键启动/停止；右键设置';
        button.setAttribute('aria-label', '不要停：' + label);
        button.setAttribute('aria-pressed', String(enabled));
      }

      function notice(message) {
        const node = host?.shadowRoot?.querySelector('.notice');
        if (!node) return;
        node.textContent = message;
        node.classList.add('show');
        clearTimeout(toastTimeout);
        toastTimeout = setTimeout(() => node.classList.remove('show'), 5500);
      }

      function halt(reason) {
        enabled = false;
        sending = false;
        awaitingReply = false;
        deadline = 0;
        paint('idle', '不要停', '已停止：' + reason + '（右键设置）');
        lastStopReason = reason;
        if (reason && reason !== '手动关闭') notice(reason);
      }

      function enable() {
        enabled = true;
        sending = false;
        awaitingReply = false;
        sentCount = 0;
        chatPath = pathKey();
        seenReply = '';
        replyChangedAt = Date.now();
        deadline = 0;
        knownUserTurn = latestUserTurn();
        lastVerifiedReply = '';
        lastStopCheck = '等待 AI 最新回复';
        lastStopReason = '';
        if (timeReached()) {
          halt('已到达指定停止时间，请重新设置');
          return;
        }
        paint('waiting', '不要停 · 待回复');
        tick();
      }

      async function fill(value) {
        const input = editor();
        if (!input) throw new Error('找不到输入框');
        if (draft()) throw new Error('输入框已有草稿，停止以避免覆盖');
        input.focus();
        if (input.tagName === 'TEXTAREA') {
          const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
          if (setter) setter.call(input, value);
          else input.value = value;
          input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
        } else {
          let ok = false;
          try {
            ok = document.execCommand('insertText', false, value) === true;
          } catch {}
          if (!ok) {
            input.replaceChildren(...value.split('\n').map(line => {
              const p = document.createElement('p');
              p.textContent = line || '\u200b';
              return p;
            }));
            input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
          }
        }
        if (draft() !== value.trim()) throw new Error('输入框内容不一致，未发送');
      }

      async function sendNext() {
        if (!ctx.isActive || !enabled || sending || awaitingReply) return;
        sending = true;
        deadline = 0;
        paint('waiting', '不要停 · 发送中');
        try {
          if (pathKey() !== chatPath) throw new Error('聊天已切换');
          if (timeReached()) {
            halt('已到达指定停止时间');
            return;
          }
          if (generating() || draft()) throw new Error('正在回复或有手动草稿');
          const reply = lastReply();
          if (!reply) throw new Error('尚无 AI 回复');
          const stopReason = verifyStop(reply);
          if (stopReason) {
            halt(stopReason);
            return;
          }
          if (settings.maxSends > 0 && sentCount >= settings.maxSends) {
            halt('已达到最多续发次数');
            return;
          }
          beforeReply = reply.fingerprint;
          await fill(settings.message);
          let submit = null;
          for (let i = 0; i < 25 && enabled && ctx.isActive; i++) {
            submit = sendButton();
            if (submit && !submit.disabled && !generating()) break;
            await sleep(200);
          }
          if (!enabled || !ctx.isActive) return;
          if (!submit || submit.disabled || generating() || pathKey() !== chatPath) {
            throw new Error('发送按钮未就绪，草稿已保留供检查');
          }
          if (timeReached()) {
            halt('已到达指定停止时间；草稿已保留');
            return;
          }
          if (popupOpen) {
            halt('设置界面已打开，暂停本次发送；草稿已保留');
            return;
          }
          submit.click();
          sentCount++;
          awaitingReply = true;
          waitingSince = Date.now();
          let accepted = false;
          for (let i = 0; i < 40 && enabled && ctx.isActive; i++) {
            await sleep(250);
            const fresh = lastReply();
            if (!draft() || generating() || (fresh && fresh.fingerprint !== beforeReply)) {
              accepted = true;
              break;
            }
          }
          if (enabled && !accepted) throw new Error('无法确认发送成功；已停止以防重复提交');
          knownUserTurn = latestUserTurn();
        } catch (error) {
          if (enabled) halt(error.message || '发送失败');
        } finally {
          sending = false;
        }
      }

      function tick() {
        if (!ctx.isActive) return;
        mount();
        if (!enabled) return;
        if (timeReached()) {
          halt('已到达指定停止时间');
          return;
        }
        if (sending) return;
        if (popupOpen) {
          deadline = 0;
          paint('waiting', '不要停 · 设置中', '保存设置后再重新倒计时');
          return;
        }
        const currentPath = pathKey();
        if (currentPath !== chatPath) {
          if (awaitingReply && chatPath === '/' && currentPath.startsWith('/c/')) chatPath = currentPath;
          else {
            halt('检测到切换聊天，已停止');
            return;
          }
        }
        if (!editor()) {
          deadline = 0;
          paint('waiting', '不要停 · 等待页面');
          return;
        }
        if (!awaitingReply && latestUserTurn() !== knownUserTurn) {
          halt('检测到手动发送，已停止自动续发');
          return;
        }
        if (document.querySelector('[data-testid="conversation-turn-error"]')) {
          halt('AI 回复错误，已停止');
          return;
        }
        const reply = lastReply();
        if (!reply) {
          deadline = 0;
          paint('waiting', '不要停 · 等待回复');
          return;
        }
        if (reply.fingerprint !== seenReply) {
          seenReply = reply.fingerprint;
          replyChangedAt = Date.now();
          deadline = 0;
        }
        if (generating() || Date.now() - replyChangedAt < 2200) {
          deadline = 0;
          paint('waiting', '不要停 · 等待回复');
          return;
        }
        if (awaitingReply) {
          if (reply.fingerprint === beforeReply) {
            if (Date.now() - waitingSince > 15 * 60 * 1000) halt('等待新回复超时');
            else paint('waiting', '不要停 · 等新回复');
            return;
          }
          awaitingReply = false;
          deadline = 0;
        }
        const reason = verifyStop(reply);
        if (reason) {
          halt(reason);
          return;
        }
        if (draft()) {
          halt('检测到手动草稿，已停止');
          return;
        }
        if (settings.maxSends > 0 && sentCount >= settings.maxSends) {
          halt('已达到最多续发次数');
          return;
        }
        if (!deadline) deadline = Date.now() + settings.delaySeconds * 1000;
        if (settings.stopAt) deadline = Math.min(deadline, settings.stopAt);
        if (timeReached()) {
          halt('已到达指定停止时间');
          return;
        }
        const remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        paint('ready', '不要停 ' + remaining + 's', '已就绪；' + remaining + ' 秒后自动发送；左键停止，右键设置');
        if (remaining === 0) void sendNext();
      }

      function findComposer() {
        const input = editor();
        if (!input || !input.isConnected) return null;

        let cur = input;
        let composer = null;
        while (cur && cur !== document.body && cur.tagName !== 'MAIN') {
          if (cur.tagName === 'FORM') {
            composer = cur;
            break;
          }
          const hasAction = cur.querySelector(SEL.send) || cur.querySelector(SEL.voice);
          if (hasAction && cur.contains(input)) {
            composer = cur;
            if (cur.parentElement && (cur.parentElement.tagName === 'FORM' || cur.parentElement.querySelector('form') === cur.parentElement)) {
              composer = cur.parentElement;
            }
            break;
          }
          cur = cur.parentElement;
        }
        if (!composer) {
          composer = input.closest('form') || input.parentElement?.parentElement || input.parentElement;
        }
        return composer;
      }

      function ensureHostCreated() {
        if (host) return;
        host = document.createElement('div');
        host.id = 'yanzi-keep-going-host';
        // 关键：注册至 SDK 上下文，停用时 SDK 自动销毁，无需手写清理
        ctx.registerElement(host);

        const sh = host.attachShadow({ mode: 'open' });
        sh.innerHTML = [
          '<style>',
          ':host{all:initial;position:fixed!important;z-index:2147483640!important;pointer-events:auto!important;box-sizing:border-box!important;transition:opacity .15s ease,bottom .1s ease,right .1s ease;display:none}',
          '.controls{display:inline-flex;align-items:stretch;overflow:hidden;border-radius:999px;min-height:28px;background:rgba(15,23,42,0.88);backdrop-filter:blur(10px);-webkit-backdrop-filter:blur(10px);border:1px solid rgba(148,163,184,0.36);color:#fff;box-shadow:0 4px 14px rgba(0,0,0,0.22);transition:all .18s ease}',
          'button{font-family:inherit;font-size:11px;font-weight:600;white-space:nowrap;cursor:pointer;border:0;background:transparent;color:#e2e8f0;min-height:28px;padding:0;box-sizing:border-box;line-height:1.2;transition:all .15s ease}',
          '.settings{width:30px;min-width:30px;display:inline-flex;align-items:center;justify-content:center;border-right:1px solid rgba(255,255,255,0.12)}',
          '.settings:hover{background:rgba(255,255,255,0.12);color:#fff}',
          '.settings svg{width:13px;height:13px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;pointer-events:none}',
          '.toggle{min-width:84px;padding:0 11px;text-align:center;display:inline-flex;align-items:center;justify-content:center;gap:4px}',
          '.toggle:hover{background:rgba(255,255,255,0.08);color:#fff}',
          '.toggle[data-mode=ready]{background:linear-gradient(135deg,rgba(22,163,74,0.95),rgba(21,128,61,0.95));color:#fff;font-weight:700}',
          '.toggle[data-mode=waiting]{background:linear-gradient(135deg,rgba(180,83,9,0.92),rgba(217,119,6,0.92));color:#fff}',
          '.controls:has(.toggle[data-mode=ready]){border-color:rgba(34,197,94,0.7);box-shadow:0 0 0 2px rgba(34,197,94,0.25),0 4px 16px rgba(22,163,74,0.35)}',
          '.controls:has(.toggle[data-mode=waiting]){border-color:rgba(245,158,11,0.7);box-shadow:0 0 0 2px rgba(245,158,11,0.2)}',
          'button:focus-visible{outline:2px solid #38bdf8;outline-offset:-2px}',
          '.notice{display:none;position:fixed;right:20px;bottom:100px;padding:9px 14px;border-radius:10px;background:#0f172a;color:#f8fafc;border:1px solid rgba(148,163,184,0.25);font:12px/1.5 sans-serif;max-width:320px;z-index:2147483647;box-shadow:0 8px 24px rgba(0,0,0,0.4)}.notice.show{display:block}',
          '</style>',
          '<span class="controls">',
          '  <button type="button" class="settings" title="打开不要停设置" aria-label="打开不要停设置">',
          '    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z"/><path d="M19.4 15a1.6 1.6 0 0 0 .32 1.77l.06.06-2 2-.06-.06a1.6 1.6 0 0 0-1.77-.32l-.27.12a1.6 1.6 0 0 0-.95 1.46V20h-2.83v-.07a1.6 1.6 0 0 0-.95-1.46l-.27-.12a1.6 1.6 0 0 0-1.77.32l-.06.06-2-2 .06-.06A1.6 1.6 0 0 0 7.23 15l-.12-.27a1.6 1.6 0 0 0-1.46-.95H5.6v-2.83h.05a1.6 1.6 0 0 0 1.46-.95l.12-.27a1.6 1.6 0 0 0-.32-1.77l-.06-.06 2-2 .06.06a1.6 1.6 0 0 0 1.77.32l.27-.12a1.6 1.6 0 0 0 .95-1.46V4.6h2.83v.05a1.6 1.6 0 0 0 .95 1.46l.27.12a1.6 1.6 0 0 0 1.77-.32l.06-.06 2 2-.06.06A1.6 1.6 0 0 0 19.4 9.7l.12.27a1.6 1.6 0 0 0 1.46.95H21v2.83h-.02a1.6 1.6 0 0 0-1.46.95z"/></svg>',
          '  </button>',
          '  <button type="button" class="toggle" data-mode="idle" aria-pressed="false">✦ 不要停</button>',
          '</span>',
          '<span class="notice" role="status"></span>'
        ].join('');

        button = sh.querySelector('.toggle');
        button.addEventListener('click', e => {
          e.preventDefault();
          e.stopPropagation();
          if (enabled) halt('手动关闭');
          else enable();
        });

        sh.querySelector('.settings').addEventListener('click', e => {
          e.preventDefault();
          e.stopPropagation();
          openSettings();
        });

        sh.querySelector('.controls').addEventListener('contextmenu', e => {
          e.preventDefault();
          e.stopPropagation();
          openSettings();
        });
      }

      function mount() {
        if (!ctx.isActive) return;
        ensureHostCreated();
        if (host && host.parentNode !== document.body) {
          document.body.appendChild(host);
        }
        updatePosition();
      }

      function updatePosition() {
        if (!host || !ctx.isActive) return;
        const input = editor();
        if (!input || !input.isConnected) {
          host.style.display = 'none';
          return;
        }
        const composer = findComposer();
        if (!composer || !composer.isConnected) {
          host.style.display = 'none';
          return;
        }
        const rect = composer.getBoundingClientRect();
        if (rect.width === 0 || rect.height === 0 || rect.bottom <= 0 || rect.top >= window.innerHeight) {
          host.style.display = 'none';
          return;
        }

        host.style.display = 'block';
        const bottomDistance = Math.round(window.innerHeight - rect.top + 10);
        const rightDistance = Math.round(Math.max(12, window.innerWidth - rect.right + 4));

        host.style.bottom = bottomDistance + 'px';
        host.style.right = rightDistance + 'px';
      }

      function openSettings() {
        if (popupOpen) return;
        popupOpen = true;
        if (enabled && !sending) {
          deadline = 0;
          paint('waiting', '不要停 · 设置中');
        }
        dialogHost = document.createElement('div');
        dialogHost.id = 'yanzi-keep-going-dialog-host';
        ctx.registerElement(dialogHost);

        const sh = dialogHost.attachShadow({ mode: 'open' });
        sh.innerHTML = [
          '<style>',
          ':host{all:initial;position:fixed;inset:0;z-index:2147483647;font:14px/1.45 system-ui,"Microsoft YaHei",sans-serif;color:#1e293b}',
          '.veil{position:absolute;inset:0;background:rgba(15,23,42,0.65);backdrop-filter:blur(3px);display:grid;place-items:center;padding:16px;box-sizing:border-box}',
          '.panel{box-sizing:border-box;width:min(440px,calc(100vw - 32px));max-height:calc(100vh - 32px);overflow:auto;background:#ffffff;border-radius:16px;padding:22px;box-shadow:0 20px 48px rgba(0,0,0,0.24);border:1px solid rgba(226,232,240,0.8)}',
          'h3{margin:0 0 6px;font-size:18px;font-weight:700;color:#0f172a;display:flex;align-items:center;gap:6px}',
          '.hint{font-size:12px;color:#64748b;margin-bottom:14px;line-height:1.5}',
          'label{display:block;font-size:12px;font-weight:600;color:#334155;margin:10px 0 5px}',
          'textarea,input,select{box-sizing:border-box;width:100%;border:1px solid #cbd5e1;border-radius:8px;background:#fff;color:#0f172a;font:13px/1.5 system-ui;padding:7px 10px;outline:none;transition:border-color .15s}',
          'textarea:focus,input:focus,select:focus{border-color:#2563eb;box-shadow:0 0 0 2px rgba(37,99,235,0.12)}',
          'textarea{min-height:60px;resize:vertical}.row{display:grid;grid-template-columns:1fr 1fr;gap:10px}',
          '.actions{display:flex;justify-content:flex-end;gap:10px;margin-top:18px}',
          'button{cursor:pointer;border:1px solid #cbd5e1;border-radius:8px;background:#fff;padding:8px 14px;color:#334155;font:13px/1.4 system-ui;font-weight:600}',
          'button.primary{background:#16a34a;border-color:#16a34a;color:#fff}',
          'button.primary:hover{background:#15803d}',
          'button.cancel:hover{background:#f1f5f9}',
          '.error{color:#dc2626;font-size:12px;min-height:16px;margin-top:4px}',
          '</style>',
          '<div class="veil"><form class="panel" aria-label="不要停设置">',
          '<h3><span style="color:#16a34a">✦</span> 不要停 · 自动续聊设置</h3>',
          '<div class="hint">只在当前标签页手动开启。点击“保存设置”后生效；设置窗口打开期间暂停自动发送。切换对话或页面异常时会自动停止保护。</div>',
          '<label>自动发送内容</label><textarea name="message" required></textarea>',
          '<div class="row">',
          '  <div><label>回复完成后等待（秒）</label><input name="delaySeconds" type="number" min="1" max="86400" required></div>',
          '  <div><label>最多续发次数（0 = 不限）</label><input name="maxSends" type="number" min="0" max="10000" required></div>',
          '</div>',
          '<label>到指定时间停止（本机时间，留空 = 不限）</label>',
          '<input name="stopAtLocal" type="datetime-local" step="60">',
          '<label>停止条件：AI 最新回复的文本</label><select name="stopMode">',
          '  <option value="contains">包含指定内容</option>',
          '  <option value="equals">全文完全相同</option>',
          '  <option value="regex">正则表达式（忽略大小写）</option>',
          '</select>',
          '<textarea name="stopText" placeholder="如：已完成全部任务。留空则不按回复内容停止"></textarea>',
          '<label>最近一次识别与复核</label>',
          '<div class="hint" data-check="true" style="white-space:pre-wrap;word-break:break-word;background:#f8fafc;padding:8px;border-radius:6px;border:1px solid #e2e8f0;"></div>',
          '<div class="error" role="alert"></div>',
          '<div class="actions">',
          '  <button type="button" class="cancel">取消</button>',
          '  <button type="submit" class="primary">保存设置</button>',
          '</div></form></div>'
        ].join('');

        const form = sh.querySelector('form');
        for (const [key, val] of Object.entries(settings)) {
          if (form.elements.namedItem(key)) form.elements.namedItem(key).value = String(val);
        }
        form.elements.namedItem('stopAtLocal').value = localDatetime(settings.stopAt);

        const reply = lastReply();
        sh.querySelector('[data-check]').textContent =
          'AI 最新回复：' + JSON.stringify(normalizeReply(reply?.text || lastVerifiedReply || '（暂无）').slice(0, 200)) +
          '\n复核状态：' + lastStopCheck +
          '\n上次停止原因：' + (lastStopReason || '无');

        const close = () => {
          dialogHost?.remove();
          dialogHost = null;
          popupOpen = false;
          if (enabled) {
            deadline = 0;
            tick();
          }
        };

        sh.querySelector('.cancel').addEventListener('click', close);
        sh.querySelector('.veil').addEventListener('click', e => {
          if (e.target.classList.contains('veil')) close();
        });
        sh.addEventListener('keydown', e => {
          if (e.key === 'Escape') close();
        });

        form.addEventListener('submit', async e => {
          e.preventDefault();
          const message = form.elements.namedItem('message').value.trim();
          const stopText = form.elements.namedItem('stopText').value.trim();
          const stopMode = form.elements.namedItem('stopMode').value;
          const delaySeconds = Number(form.elements.namedItem('delaySeconds').value);
          const maxSends = Number(form.elements.namedItem('maxSends').value);
          const stopAtLocal = form.elements.namedItem('stopAtLocal').value;
          const stopAt = stopAtLocal ? new Date(stopAtLocal).getTime() : 0;
          const err = sh.querySelector('.error');

          if (!message || message.length > 5000) {
            err.textContent = '发送内容需为 1–5000 字';
            return;
          }
          if (!Number.isInteger(delaySeconds) || delaySeconds < 1 || delaySeconds > 86400) {
            err.textContent = '等待需为 1–86400 秒';
            return;
          }
          if (!Number.isInteger(maxSends) || maxSends < 0 || maxSends > 10000) {
            err.textContent = '续发次数需为 0–10000';
            return;
          }
          if (stopMode === 'regex' && stopText) {
            try {
              new RegExp(stopText, 'i');
            } catch {
              err.textContent = '正则表达式无效';
              return;
            }
          }
          if (stopAtLocal && (!Number.isFinite(stopAt) || stopAt <= Date.now())) {
            err.textContent = '停止时间必须晚于当前时间';
            return;
          }

          settings = { message, delaySeconds, maxSends, stopText, stopMode, stopAt };

          try {
            await ctx.updateSettings(settings);
          } catch {
            err.textContent = '扩展设置保存失败';
            return;
          }

          deadline = 0;
          close();
          notice('设置已保存');
          if (enabled && !generating()) tick();
        });

        document.body.appendChild(dialogHost);
        form.elements.namedItem('message').focus();
      }

      // 绑定生命周期安全定时器与监听器（停用时 SDK 自动清理）
      ctx.setInterval(tick, 500);
      ctx.addEventListener(window, 'resize', updatePosition, { passive: true });
      ctx.addEventListener(window, 'scroll', updatePosition, { passive: true });
      ctx.addEventListener(document, 'input', e => {
        if (e.target && (e.target.id === 'prompt-textarea' || e.target.closest?.('#prompt-textarea'))) {
          updatePosition();
        }
      }, { passive: true });

      // 安全 DOM 观察者（停用时 SDK 自动断开）
      let scheduled = false;
      const observer = ctx.createObserver(() => {
        if (scheduled) return;
        scheduled = true;
        setTimeout(() => {
          scheduled = false;
          mount();
        }, 150);
      });
      if (document.body) {
        observer.observe(document.body, { childList: true, subtree: true });
      }

      mount();
    },

    onDeactivate(ctx) {
      // 业务停用回调：DOM、Timer、Observer、Listener 会由 SDK 全自动清理
    },

    onSettingsChanged(newSettings, ctx) {
      // 设置变更实时响应
    }
  });
})();
