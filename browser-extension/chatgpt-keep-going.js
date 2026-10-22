/* Yanzi ChatGPT Keep Going. Active sessions are intentionally tab-local and opt-in. */
(() => {
  'use strict';
  if (globalThis.__yanziKeepGoingV1) return;
  globalThis.__yanziKeepGoingV1 = true;
  const KEY = 'yanzi.chatgpt.keepGoing.settings.v1';
  const DEFAULTS = {message:'继续', delaySeconds:60, stopText:'', stopMode:'contains', maxSends:0};
  const SEL = {
    editor:'#prompt-textarea, main [contenteditable="true"][role="textbox"]',
    send:'button[data-testid="send-button"], button[data-testid="composer-submit-button"], button[aria-label="Send prompt"], button[aria-label="发送提示"], main button[aria-label="发送"], main button[aria-label="Send"]',
    stop:'button[data-testid="stop-button"], button[aria-label="Stop generating"], button[aria-label="停止生成"], main button[aria-label="停止"], [data-is-streaming="true"]',
    voice:'button[data-testid="composer-speech-button"], button[data-testid="voice-mode-button"], button[aria-label="Start voice mode"], button[aria-label="开始语音对话"]'
  };
  let settings = {...DEFAULTS}, enabled = false, sending = false, awaitingReply = false, sentCount = 0;
  let chatPath = '', beforeReply = '', waitingSince = 0, deadline = 0, seenReply = '', replyChangedAt = 0, knownUserTurn = '';
  let host, button, dialogHost, toastTimeout, popupOpen = false;
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
      .filter(el => !el.closest('[data-user-message-bubble="true"]'));
    const list = legacy.length ? legacy : units.length ? units : modern;
    const node = list.at(-1);
    if (!node) return null;
    // A submitted user turn after the last assistant reply is not a completed AI response.
    if (legacy.length) {
      const turns = document.querySelectorAll('[data-message-author-role]');
      if (turns[turns.length-1]?.getAttribute('data-message-author-role') === 'user') return null;
    } else if (units.length) {
      const turns = Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]'));
      if (/:user$/.test(turns.at(-1)?.getAttribute('data-chatgpt-search-unit-key') || '')) return null;
    } else {
      const users = document.querySelectorAll('[data-user-message-bubble="true"]');
      const newestUser = users[users.length-1];
      if (newestUser && (node.compareDocumentPosition(newestUser) & Node.DOCUMENT_POSITION_FOLLOWING)) return null;
    }
    const content = node.querySelector('.markdown, [data-content-search-unit-key]') || node;
    const text = (content.innerText || content.textContent || '').trim();
    const id = node.getAttribute('data-message-id') || node.getAttribute('data-chatgpt-search-message-ids') || '';
    return {text, fingerprint:[list.length,id,text.length,text.slice(0,120),text.slice(-120)].join('|')};
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
  function stopMatches(text) {
    const target = settings.stopText.trim();
    if (!target) return false;
    if (settings.stopMode === 'equals') return text.trim() === target;
    if (settings.stopMode === 'regex') {
      try { return new RegExp(target, 'i').test(text); } catch { return false; }
    }
    return text.includes(target);
  }
  function paint(mode, label, hint) {
    if (!button) return;
    button.dataset.mode = mode;
    button.textContent = label;
    button.title = hint || '左键启动/停止；右键设置';
    button.setAttribute('aria-label','不要停：'+label);
    button.setAttribute('aria-pressed',String(enabled));
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
    paint('idle','不要停','已停止：'+reason+'（右键设置）');
    if (reason && reason !== '手动关闭') notice(reason);
  }
  function enable() {
    enabled = true; sending = false; awaitingReply = false; sentCount = 0;
    chatPath = pathKey(); seenReply = ''; replyChangedAt = Date.now(); deadline = 0; knownUserTurn = latestUserTurn();
    paint('waiting','不要停 · 待回复');
    tick();
  }
  async function fill(value) {
    const input = editor();
    if (!input) throw new Error('找不到输入框');
    if (draft()) throw new Error('输入框已有草稿，停止以避免覆盖');
    input.focus();
    if (input.tagName === 'TEXTAREA') {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value')?.set;
      if (setter) setter.call(input,value); else input.value = value;
      input.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:value}));
    } else {
      let ok = false;
      try {ok = document.execCommand('insertText',false,value) === true;} catch {}
      if (!ok) {
        input.replaceChildren(...value.split('\n').map(line => {
          const p = document.createElement('p'); p.textContent = line || '\u200b'; return p;
        }));
        input.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:value}));
      }
    }
    if (draft() !== value.trim()) throw new Error('输入框内容不一致，未发送');
  }
  async function sendNext() {
    if (!enabled || sending || awaitingReply) return;
    sending = true; deadline = 0;
    paint('waiting','不要停 · 发送中');
    try {
      if (pathKey() !== chatPath) throw new Error('聊天已切换');
      if (generating() || draft()) throw new Error('正在回复或有手动草稿');
      const reply = lastReply();
      if (!reply) throw new Error('尚无 AI 回复');
      if (stopMatches(reply.text)) {halt('已命中停止条件');return;}
      if (settings.maxSends > 0 && sentCount >= settings.maxSends) {halt('已达到最多续发次数');return;}
      beforeReply = reply.fingerprint;
      await fill(settings.message);
      let submit = null;
      for (let i=0;i<25 && enabled;i++) {
        submit = sendButton();
        if (submit && !submit.disabled && !generating()) break;
        await sleep(200);
      }
      if (!enabled) return;
      if (!submit || submit.disabled || generating() || pathKey() !== chatPath)
        throw new Error('发送按钮未就绪，草稿已保留供检查');
      submit.click();
      sentCount++;
      awaitingReply = true;
      waitingSince = Date.now();
      let accepted = false;
      for (let i=0;i<40 && enabled;i++) {
        await sleep(250);
        const fresh = lastReply();
        if (!draft() || generating() || (fresh && fresh.fingerprint !== beforeReply)) {
          accepted = true;break;
        }
      }
      if (enabled && !accepted) throw new Error('无法确认发送成功；已停止以防重复提交');
      knownUserTurn = latestUserTurn();
    } catch (error) {
      if (enabled) halt(error.message || '发送失败');
    } finally {sending = false;}
  }
  function tick() {
    mount();
    if (!enabled || sending) return;
    const currentPath = pathKey();
    if (currentPath !== chatPath) {
      if (awaitingReply && chatPath === '/' && currentPath.startsWith('/c/')) chatPath = currentPath;
      else {halt('检测到切换聊天，已停止');return;}
    }
    if (!editor()) {deadline=0;paint('waiting','不要停 · 等待页面');return;}
    if (!awaitingReply && latestUserTurn() !== knownUserTurn) {halt('检测到手动发送，已停止自动续发');return;}
    if (document.querySelector('[data-testid="conversation-turn-error"]')) {halt('AI 回复错误，已停止');return;}
    const reply = lastReply();
    if (!reply) {deadline=0;paint('waiting','不要停 · 等待回复');return;}
    if (reply.fingerprint !== seenReply) {
      seenReply = reply.fingerprint;
      replyChangedAt = Date.now();
      deadline = 0;
    }
    if (generating() || Date.now()-replyChangedAt < 2200) {
      deadline=0;paint('waiting','不要停 · 等待回复');return;
    }
    if (awaitingReply) {
      if (reply.fingerprint === beforeReply) {
        if (Date.now()-waitingSince > 15*60*1000) halt('等待新回复超时');
        else paint('waiting','不要停 · 等新回复');
        return;
      }
      awaitingReply=false;deadline=0;
    }
    if (draft()) {halt('检测到手动草稿，已停止');return;}
    if (stopMatches(reply.text)) {halt('AI 回复命中停止条件');return;}
    if (settings.maxSends > 0 && sentCount >= settings.maxSends) {halt('已达到最多续发次数');return;}
    if (!deadline) deadline=Date.now()+settings.delaySeconds*1000;
    const remaining=Math.max(0,Math.ceil((deadline-Date.now())/1000));
    paint('ready','不要停 '+remaining+'s','已就绪；'+remaining+' 秒后自动发送；左键停止，右键设置');
    if (remaining===0) void sendNext();
  }
  function mount() {
    if (!host) {
      host = document.createElement('span');
      host.id = 'yanzi-keep-going-host';
      const sh = host.attachShadow({mode:'open'});
      sh.innerHTML = [
        '<style>',
        ':host{display:inline-flex;align-items:center;flex:none;vertical-align:middle;margin-left:6px;z-index:20}',
        'button{font-family:inherit;font-size:12px;font-weight:650;white-space:nowrap;cursor:pointer;border:1px solid #8894a3;background:#374151;color:white;border-radius:999px;min-height:34px;padding:0 11px;box-shadow:0 1px 2px #0002}',
        'button[data-mode=ready]{background:#16a34a;border-color:#15803d;color:white;box-shadow:0 0 0 2px #22c55e30}',
        'button[data-mode=waiting]{background:#b7791f;border-color:#9a661a;color:white}',
        'button:hover{filter:brightness(1.08)}',
        '.notice{display:none;position:fixed;right:16px;bottom:100px;padding:9px 12px;border-radius:10px;background:#1f2937;color:white;font:12px/1.5 sans-serif;max-width:310px;z-index:2147483647;box-shadow:0 4px 18px #0005}.notice.show{display:block}',
        '</style><button type="button" data-mode="idle" aria-pressed="false">不要停</button><span class="notice" role="status"></span>'
      ].join('');
      button=sh.querySelector('button');
      button.addEventListener('click',e=>{e.preventDefault();e.stopPropagation();if(enabled)halt('手动关闭');else enable();});
      button.addEventListener('contextmenu',e=>{e.preventDefault();e.stopPropagation();openSettings();});
    }
    const input=editor();
    if (!input || !input.isConnected) return;
    const target=sendButton() || document.querySelector(SEL.voice) ||
      input.closest('form')?.querySelector('button[type="submit"]') ||
      Array.from(input.closest('form')?.querySelectorAll('button') || []).at(-1);
    if (!target?.parentNode) return;
    if (host.parentNode !== target.parentNode || host.previousElementSibling !== target)
      target.parentNode.insertBefore(host,target.nextSibling);
  }
  function openSettings() {
    if (popupOpen) return;
    popupOpen=true;
    dialogHost=document.createElement('div');
    dialogHost.id='yanzi-keep-going-dialog-host';
    const sh=dialogHost.attachShadow({mode:'open'});
    sh.innerHTML=[
      '<style>:host{all:initial;position:fixed;inset:0;z-index:2147483647;font:14px/1.45 system-ui,"Microsoft YaHei",sans-serif;color:#202938}',
      '.veil{position:absolute;inset:0;background:#0008;display:grid;place-items:center}',
      '.panel{box-sizing:border-box;width:min(440px,calc(100vw - 30px));max-height:calc(100vh - 30px);overflow:auto;background:#fff;border-radius:16px;padding:20px;box-shadow:0 15px 45px #0005}',
      'h3{margin:0 0 8px;font-size:19px}.hint{font-size:12px;color:#64748b;margin-bottom:12px}',
      'label{display:block;font-size:13px;font-weight:600;margin:10px 0 5px}',
      'textarea,input,select{box-sizing:border-box;width:100%;border:1px solid #b8c4d4;border-radius:8px;background:#fff;color:#111827;font:14px/1.5 system-ui;padding:8px}',
      'textarea{min-height:70px;resize:vertical}.row{display:grid;grid-template-columns:1fr 1fr;gap:12px}',
      '.actions{display:flex;justify-content:flex-end;gap:10px;margin-top:18px}',
      'button{cursor:pointer;border:1px solid #cbd5e1;border-radius:9px;background:#fff;padding:9px 15px;color:#111827;font:inherit}',
      'button.primary{background:#15803d;border-color:#15803d;color:#fff}.error{color:#b91c1c;font-size:12px;min-height:17px}</style>',
      '<div class="veil"><form class="panel" aria-label="不要停设置"><h3>不要停 · 自动续聊</h3>',
      '<div class="hint">只在当前标签页手动开启。切换聊天、出现草稿或发送异常会自动停止。</div>',
      '<label>自动发送内容</label><textarea name="message" required></textarea>',
      '<div class="row"><div><label>回复完成后等待（秒）</label><input name="delaySeconds" type="number" min="1" max="86400" required></div>',
      '<div><label>最多续发次数（0 = 不限）</label><input name="maxSends" type="number" min="0" max="10000" required></div></div>',
      '<label>停止条件：AI 最新回复的文本</label><select name="stopMode">',
      '<option value="contains">包含指定内容</option><option value="equals">全文完全相同</option>',
      '<option value="regex">正则表达式（忽略大小写）</option></select>',
      '<textarea name="stopText" placeholder="如：任务全部完成。留空则不按回复内容停止"></textarea>',
      '<div class="error" role="alert"></div><div class="actions">',
      '<button type="button" class="cancel">取消</button><button type="submit" class="primary">保存设置</button>',
      '</div></form></div>'
    ].join('');
    const form=sh.querySelector('form');
    for (const [key,val] of Object.entries(settings)) if(form.elements.namedItem(key))form.elements.namedItem(key).value=String(val);
    const close=()=>{dialogHost?.remove();dialogHost=null;popupOpen=false;};
    sh.querySelector('.cancel').addEventListener('click',close);
    sh.querySelector('.veil').addEventListener('click',e=>{if(e.target.classList.contains('veil'))close();});
    sh.addEventListener('keydown',e=>{if(e.key==='Escape')close();});
    form.addEventListener('submit',async e=>{
      e.preventDefault();
      const message=form.elements.namedItem('message').value.trim();
      const stopText=form.elements.namedItem('stopText').value.trim();
      const stopMode=form.elements.namedItem('stopMode').value;
      const delaySeconds=Number(form.elements.namedItem('delaySeconds').value);
      const maxSends=Number(form.elements.namedItem('maxSends').value);
      const err=sh.querySelector('.error');
      if(!message || message.length>5000){err.textContent='发送内容需为 1–5000 字';return;}
      if(!Number.isInteger(delaySeconds)||delaySeconds<1||delaySeconds>86400){err.textContent='等待需为 1–86400 秒';return;}
      if(!Number.isInteger(maxSends)||maxSends<0||maxSends>10000){err.textContent='续发次数需为 0–10000';return;}
      if(stopMode==='regex'&&stopText){try{new RegExp(stopText,'i');}catch{err.textContent='正则表达式无效';return;}}
      settings={message,delaySeconds,maxSends,stopText,stopMode};
      try {await chrome.storage.local.set({[KEY]:settings});}
      catch {err.textContent='扩展设置保存失败';return;}
      deadline=0;close();notice('设置已保存');
    });
    document.body.appendChild(dialogHost);
    form.elements.namedItem('message').focus();
  }
  chrome.storage.local.get(KEY).then(data=>{
    const s=data?.[KEY];
    if(s&&typeof s==='object')settings={
      message:typeof s.message==='string'&&s.message.trim()?s.message:DEFAULTS.message,
      delaySeconds:Number.isInteger(s.delaySeconds)&&s.delaySeconds>=1&&s.delaySeconds<=86400?s.delaySeconds:60,
      stopText:typeof s.stopText==='string'?s.stopText:'',
      stopMode:['contains','equals','regex'].includes(s.stopMode)?s.stopMode:'contains',
      maxSends:Number.isInteger(s.maxSends)&&s.maxSends>=0?s.maxSends:0
    };
  }).catch(()=>{});
  let scheduled=false;
  const observer=new MutationObserver(()=>{
    if(scheduled)return;
    scheduled=true;
    setTimeout(()=>{scheduled=false;mount();},150);
  });
  const start=()=>{
    if(!document.body)return;
    observer.observe(document.body,{childList:true,subtree:true});
    mount();
    setInterval(tick,500);
    window.addEventListener('pagehide',()=>{enabled=false;deadline=0;});
  };
  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',start,{once:true});
  else start();
})();