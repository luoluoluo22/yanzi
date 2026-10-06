import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM } from 'jsdom';
import FakeTimers from '@sinonjs/fake-timers';
const source = readFileSync(new URL('../../../browser-extension/chatgpt-content.js', import.meta.url), 'utf8');
function page(t, html) {
  const dom = new JSDOM(html, { url: 'https://chatgpt.com/c/test', runScripts: 'outside-only' });
  const win = dom.window;
  Object.defineProperty(win.HTMLElement.prototype, 'innerText', { get() { return this.textContent; } });
  const clock = FakeTimers.withGlobal(win).install({ now: 1000 });
  const listeners = [];
  win.chrome = { runtime: { onMessage: { addListener(fn) { listeners.push(fn); } } } };
  win.eval(source); win.eval(source);
  t.after(() => { clock.uninstall(); dom.window.close(); });
  return { win, clock, listeners, run: task => new Promise(resolve => listeners.forEach(listener => listener({ type: 'yanzi_chatgpt_task', task }, {}, resolve))) };
}
const html = '<div id="prompt-textarea" contenteditable="true"></div><button data-testid="send-button">send</button><div data-message-author-role="assistant">旧回复</div>';
function add(win, role, text) {
  const element = win.document.createElement('div'); element.dataset.messageAuthorRole = role; element.textContent = text; win.document.body.append(element); return element;
}
test('injection is idempotent and reads messages with conversation metadata', async t => {
  const p = page(t, html);
  assert.equal(p.listeners.length, 2);
  const result = await p.run({ action: 'chatgpt_messages' });
  assert.equal(result.data.conversationId, 'test');
  assert.equal(result.data.messages[0].text, '旧回复');
});
test('rejects drafts without clicking send', async t => {
  const p = page(t, html);
  p.win.document.querySelector('#prompt-textarea').textContent = '未发送的草稿';
  let clicked = false; p.win.document.querySelector('button').onclick = () => { clicked = true; };
  const result = await p.run({ action: 'chatgpt_send', prompt: '测试' });
  assert.equal(result.status, 'error'); assert.match(result.message, /草稿/); assert.equal(clicked, false);
});
test('recognizes the current ChatGPT textbox without an ID', async t => {
  const p = page(t, '<main><div role="textbox" contenteditable="true"></div></main>');
  const result = await p.run({ action: 'chatgpt_new_chat' });
  assert.equal(result.status, 'success');
  const read = await p.run({ action: 'chatgpt_messages' });
  assert.equal(read.data.diagnostics.inputs[0].role, 'textbox');
});
test('temporary request refuses to send when only an inactive toggle or message mentions temporary chat', async t => {
  const p = page(t, html + '<button aria-pressed="false">Temporary chat</button><div data-message-author-role="assistant"><h1>临时聊天</h1></div>');
  let clicks = 0; p.win.document.querySelector('[data-testid="send-button"]').onclick = () => clicks++;
  const pending = p.run({ action: 'chatgpt_send', prompt: 'must not send', temporary: true, timeoutSeconds: 20 });
  await p.clock.tickAsync(16000);
  const result = await pending;
  assert.equal(result.status, 'error'); assert.match(result.message, /临时聊天/); assert.equal(clicks, 0);
  assert.equal(p.win.document.querySelector('#prompt-textarea').textContent, '');
});
test('temporary heading confirms new chat and allows sending', async t => {
  const p = page(t, '<header><h1>临时聊天</h1></header>' + html);
  assert.equal((await p.run({ action: 'chatgpt_new_chat', temporary: true })).data.temporary, true);
  p.win.document.querySelector('[data-testid="send-button"]').onclick = () => { add(p.win, 'user', 'test'); add(p.win, 'assistant', 'OK'); };
  const pending = p.run({ action: 'chatgpt_send', prompt: 'test', temporary: true });
  await p.clock.tickAsync(4000);
  assert.equal((await pending).data.text, 'OK'); assert.equal((await pending).data.temporary, true);
});
test('active temporary toggle confirms mode independently of URL', async t => {
  const p = page(t, html + '<button aria-label="Temporary chat" aria-pressed="true"></button>');
  assert.equal((await p.run({ action: 'chatgpt_new_chat', temporary: true })).data.temporaryEvidence, 'active-control');
});
test('temporary continuation requires a current UI indicator after initial heading disappears', async t => {
  const p = page(t, '<h1>临时聊天</h1>' + html);
  p.win.history.replaceState(null, '', '/?temporary-chat=true');
  assert.equal((await p.run({ action: 'chatgpt_new_chat', temporary: true })).data.temporary, true);
  p.win.document.querySelector('h1').remove();
  p.win.history.replaceState(null, '', '/c/temp-id?temporary-chat=true');
  assert.equal((await p.run({ action: 'chatgpt_messages' })).data.temporary, false);
  const control = p.win.document.createElement('button'); control.setAttribute('aria-label', 'Save chat'); p.win.document.body.append(control);
  assert.equal((await p.run({ action: 'chatgpt_messages' })).data.temporaryEvidence, 'save-chat-control');
  p.win.history.replaceState(null, '', '/c/formal-id');
  assert.equal((await p.run({ action: 'chatgpt_messages' })).data.temporary, false);
});
test('temporary URL without prior UI confirmation is never trusted', async t => {
  const p = page(t, html); p.win.history.replaceState(null, '', '/?temporary-chat=true');
  assert.equal((await p.run({ action: 'chatgpt_messages' })).data.temporary, false);
});
test('current post-reply Save chat control and mode flag confirm temporary conversation', async t => {
  const p = page(t, html + '<button aria-label="Save chat"></button>');
  p.win.history.replaceState(null, '', '/c/temp-id?temporary-chat=true');
  assert.equal((await p.run({ action: 'chatgpt_messages' })).data.temporaryEvidence, 'save-chat-control');
  p.win.history.replaceState(null, '', '/c/formal-id');
  assert.equal((await p.run({ action: 'chatgpt_messages' })).data.temporary, false);
});
test('reads current ChatGPT search-unit messages in DOM order', async t => {
  const p = page(t, '<main><div data-chatgpt-search-unit-key="fallback-turn-0:0:user" data-chatgpt-search-message-ids="u1"><div data-content-search-unit-key="fallback-turn-0:0:user">测试提示</div></div><div data-chatgpt-search-unit-key="fallback-turn-0:0:assistant" data-chatgpt-search-message-ids="a1"><div data-content-search-unit-key="fallback-turn-0:0:assistant">测试回复</div></div></main>');
  const result = await p.run({ action: 'chatgpt_messages' });
  assert.deepEqual(Array.from(result.data.messages, m => m.role), ['user', 'assistant']);
  assert.equal(result.data.messages[1].text, '测试回复');
  assert.equal(result.data.messages[1].id, 'a1');
});
test('strips accessible speaker labels and deduplicates message IDs', async t => {
  const p = page(t, '<main><div data-chatgpt-search-unit-key="turn:assistant" data-chatgpt-search-message-ids="a1 a1"><h4 class="sr-only">ChatGPT 说：</h4><p>YANZI_FINAL_OK</p></div></main>');
  const result = await p.run({ action: 'chatgpt_messages' });
  assert.equal(result.data.messages[0].text, 'YANZI_FINAL_OK');
  assert.equal(result.data.messages[0].id, 'a1');
});
test('current UI waits for reply controls even if partial text is stable', async t => {
  const p = page(t, '<main><div role="textbox" contenteditable="true"></div><button aria-label="发送">send</button><section id="turn"></section></main>');
  const turn = p.win.document.querySelector('#turn');
  p.win.document.querySelector('button').onclick = () => {
    turn.innerHTML = '<div data-chatgpt-search-unit-key="turn:user">测试</div><div data-chatgpt-search-unit-key="turn:assistant">部分回复</div>';
  };
  let settled = false;
  const result = p.run({ action: 'chatgpt_send', prompt: '测试' }).then(value => { settled = true; return value; });
  await p.clock.tickAsync(6000); assert.equal(settled, false);
  const button = p.win.document.createElement('button'); button.setAttribute('aria-label', '复制'); const controls = p.win.document.createElement('div'); controls.className = 'turn-action-controls'; controls.append(button); turn.append(controls);
  await p.clock.tickAsync(1000);
  assert.equal((await result).status, 'success');
});
test('sends an explicitly requested matching restored draft without overwriting it', async t => {
  const p = page(t, '<main><div role="textbox" contenteditable="true">测试</div><button aria-label="发送">send</button></main>');
  p.win.document.querySelector('button').onclick = () => { add(p.win, 'user', '测试'); add(p.win, 'assistant', '收到'); };
  const result = p.run({ action: 'chatgpt_send', prompt: '测试' });
  await p.clock.tickAsync(4000);
  assert.equal((await result).status, 'success');
  assert.equal((await result).data.text, '收到');
});
test('never returns old assistant text or a stable partial while generating', async t => {
  const p = page(t, html);
  let assistant, stop;
  p.win.document.querySelector('button').onclick = () => {
    add(p.win, 'user', '测试');
    assistant = add(p.win, 'assistant', '部分回复');
    stop = p.win.document.createElement('button'); stop.dataset.testid = 'stop-button'; p.win.document.body.append(stop);
  };
  let settled = false;
  const result = p.run({ action: 'chatgpt_send', prompt: '测试', timeoutSeconds: 30 }).then(value => { settled = true; return value; });
  await p.clock.tickAsync(6000); assert.equal(settled, false);
  assistant.textContent = '完整回复'; stop.remove();
  await p.clock.tickAsync(4000);
  assert.equal((await result).data.text, '完整回复');
});
test('times out when submission is not acknowledged, instead of returning old reply', async t => {
  const p = page(t, html);
  const result = p.run({ action: 'chatgpt_send', prompt: '测试', timeoutSeconds: 10 });
  await p.clock.tickAsync(11000);
  assert.equal((await result).status, 'error'); assert.match((await result).message, /已发送.*超时/);
});
