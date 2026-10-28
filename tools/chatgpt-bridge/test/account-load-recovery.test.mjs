import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM } from 'jsdom';

const source = readFileSync(new URL('../../../browser-extension/chatgpt-content.js', import.meta.url), 'utf8');
function makePage(t, markup) {
  const dom = new JSDOM(markup, { url: 'https://chatgpt.com/?temporary-chat=true', runScripts: 'outside-only' });
  const win = dom.window;
  Object.defineProperty(win.HTMLElement.prototype, 'innerText', { get() { return this.textContent; } });
  const callbacks = [];
  win.chrome = { runtime: { onMessage: { addListener(fn) { callbacks.push(fn); } } } };
  win.eval(source);
  t.after(() => dom.window.close());
  return {
    win,
    run(task) {
      return new Promise(resolve => callbacks.forEach(fn => fn({ type: 'yanzi_chatgpt_task', task }, {}, resolve)));
    }
  };
}

test('account-load failure is detected promptly even when navigation has finished', async t => {
  const p = makePage(t, '<main><h1>无法加载你的账户</h1><p>请重试，或退出登录后重新登录</p><button>重试</button><button>重新加载</button></main>');
  const result = await p.run({ action: 'chatgpt_send', prompt: 'DO NOT SEND' });
  assert.equal(result.status, 'error');
  assert.equal(result.errorCode, 'account_load_failed');
  assert.equal(result.deliveryPhase, 'before_send');
  assert.match(result.message, /无法加载账户页面/);
});

test('text in a normal conversation is not misclassified as account load failure', async t => {
  const p = makePage(t, '<main><div role="textbox" contenteditable="true"></div><div data-message-author-role="assistant">无法加载你的账户：这是示例错误文案</div></main>');
  const result = await p.run({ action: 'chatgpt_new_chat' });
  assert.equal(result.status, 'success');
});

test('a post-click account error must never receive the pre-send retry permission', async t => {
  const p = makePage(t, '<main><div role="textbox" contenteditable="true"></div><button aria-label="发送">发送</button></main>');
  let clicks = 0;
  p.win.document.querySelector('button').onclick = () => {
    clicks++;
    p.win.document.querySelector('main').innerHTML = '<h1>无法加载你的账户</h1><p>请重试，或退出登录后重新登录</p>';
  };
  const result = await p.run({ action: 'chatgpt_send', prompt: 'ONLY ONCE', temporary: false });
  assert.equal(clicks, 1);
  assert.equal(result.status, 'error');
  assert.equal(result.errorCode, 'account_load_failed');
  assert.equal(result.deliveryPhase, 'after_send');
  assert.match(result.message, /禁止自动刷新或重发/);
});
