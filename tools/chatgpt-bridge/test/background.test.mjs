import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const source = readFileSync(new URL('../../../browser-extension/chatgpt-background.js', import.meta.url), 'utf8');
function worker(tabs = [], options = {}) {
  tabs.forEach(tab => { tab.status ||= 'complete'; });
  const created = [], injected = [], commands = [], removed = [], updated = [];
  const storage = { chatgptManagedTabs: options.managed || {} };
  const context = vm.createContext({ console, URL, setTimeout: () => 0, setInterval: () => 0,
    WebSocket: class { static OPEN = 1; readyState = 0; },
    chrome: {
      runtime: { getManifest: () => ({ version: '0.2.0' }) },
      storage: { local: { set() {} }, session: { get: async () => storage, set: async value => Object.assign(storage, value) } }, alarms: { create() {}, onAlarm: { addListener() {} } },
      tabs: {
        query: async () => tabs,
        create: async options => { created.push(options); const tab = { id: 99 + created.length, status: 'complete', ...options }; tabs.push(tab); return tab; },
        get: async id => tabs.find(tab => tab.id === id),
        update: async (id, changes) => { updated.push({ id, changes }); return Object.assign(tabs.find(tab => tab.id === id), changes); },
        remove: async id => { removed.push(id); tabs.splice(tabs.findIndex(tab => tab.id === id), 1); },
        sendMessage: async (id, task) => { commands.push({ id, task }); const tab = tabs.find(tab => tab.id === id); if (task.task.action === 'chatgpt_send') tab.url = 'https://chatgpt.com/c/test-' + id; return { status: options.failure ? 'error' : 'success', data: { text: 'OK', visibility: 'hidden', url: tab.url } }; }
      },
      scripting: { executeScript: async request => { if (request.func) return [{ result: !options.draft }]; injected.push(request); } }
    }
  });
  vm.runInContext(source, context);
  return { created, injected, commands, removed, updated, storage, run: task => context.runChatGptTask(task) };
}
test('creates inactive new-chat tab and returns its ID', async () => {
  const w = worker([{ id: 1, url: 'https://chatgpt.com/c/user' }]);
  const result = await w.run({ taskId: 'a', action: 'chatgpt_new_chat' });
  assert.equal(w.created.length, 1); assert.equal(w.created[0].active, false);
  assert.equal(result.data.tabId, 100);
  assert.equal(w.created[0].url, 'https://chatgpt.com/?temporary-chat=true');
});
test('formal chat requires explicit opt out of temporary default', async () => {
  const w = worker();
  await w.run({ action: 'chatgpt_new_chat', temporary: false });
  assert.equal(w.created[0].url, 'https://chatgpt.com/');
  assert.equal(w.commands[0].task.task.temporary, false);
});
test('continues explicit tab without any activate/update calls', async () => {
  const w = worker([{ id: 7, url: 'https://chatgpt.com/c/test' }]);
  const result = await w.run({ taskId: 'a', action: 'chatgpt_send', tabId: 7, prompt: 'test' });
  assert.equal(result.status, 'success'); assert.equal(w.created.length, 0);
  assert.equal(w.commands[0].id, 7); assert.equal(w.injected.length, 1);
});
test('rejects ambiguous tabs and cross-site tab IDs', async () => {
  const w = worker([{ id: 1 }, { id: 2 }]);
  assert.equal((await w.run({ action: 'chatgpt_messages' })).status, 'error');
  assert.equal((await w.run({ action: 'chatgpt_send', tabId: 500 })).status, 'error');
  assert.equal(w.commands.length, 0);
});
test('successive fresh chats reuse one owned inactive tab and reset conversation without activation', async () => {
  const w = worker();
  const first = await w.run({ action: 'chatgpt_send', prompt: 'one' });
  const second = await w.run({ action: 'chatgpt_send', prompt: 'two' });
  assert.equal(first.data.tabId, second.data.tabId); assert.equal(w.created.length, 1);
  assert.equal(second.data.lifecycle.reused, true);
  assert.deepEqual(JSON.parse(JSON.stringify(w.updated[0].changes)), { url: 'https://chatgpt.com/?temporary-chat=true' });
});
test('adopts no user tabs, and continues existing conversation only when explicitly requested', async () => {
  const w = worker([{ id: 7, url: 'https://chatgpt.com/c/user', status: 'complete', active: false }]);
  const continued = await w.run({ action: 'chatgpt_send', newChat: false, prompt: 'continue' });
  assert.equal(continued.data.tabId, 7); assert.equal(w.created.length, 0);
  const fresh = await w.run({ action: 'chatgpt_send', prompt: 'fresh' });
  assert.equal(fresh.data.lifecycle.created, true); assert.equal(w.updated.length, 0);
});
test('closeAfter closes owned successful work but preserves result and all user tabs', async () => {
  const w = worker([{ id: 7, url: 'https://chatgpt.com/c/user', status: 'complete', active: false }]);
  const result = await w.run({ action: 'chatgpt_send', prompt: 'one', closeAfter: true });
  assert.equal(result.data.text, 'OK'); assert.equal(result.data.lifecycle.closed, true);
  assert.deepEqual(w.removed, [100]);
  await w.run({ action: 'chatgpt_send', tabId: 7, prompt: 'two', closeAfter: true });
  assert.deepEqual(w.removed, [100]);
});
test('failed task keeps managed tab for inspection', async () => {
  const w = worker([], { failure: true });
  assert.equal((await w.run({ action: 'chatgpt_send', prompt: 'one', closeAfter: true })).status, 'error');
  assert.equal(w.removed.length, 0);
});
test('cleanup preserves active, pinned, drafted, and manually navigated tabs', async () => {
  for (const patch of [{ active: true }, { pinned: true }, { url: 'https://chatgpt.com/c/changed' }, {}]) {
    const w = worker([{ id: 7, status: 'complete', url: 'https://chatgpt.com/c/owned', ...patch }], { managed: { 7: { url: 'https://chatgpt.com/c/owned' } }, draft: Object.keys(patch).length === 0 });
    await w.run({ action: 'chatgpt_cleanup' }); assert.equal(w.removed.length, 0);
  }
});
test('cleanup closes idle owned tabs, status identifies ownership, stale records are removed', async () => {
  const w = worker([{ id: 7, status: 'complete', url: 'https://chatgpt.com/c/owned' }], { managed: { 7: { url: 'https://chatgpt.com/c/owned' }, 8: { url: 'https://chatgpt.com/' } } });
  const status = await w.run({ action: 'chatgpt_status' });
  assert.equal(status.data.exists, true); assert.equal(status.data.tabs[0].managed, true); assert.equal(w.storage.chatgptManagedTabs[8], undefined);
  await w.run({ action: 'chatgpt_close', tabId: 7 }); assert.deepEqual(w.removed, [7]);
  assert.equal((await w.run({ action: 'chatgpt_status' })).data.exists, false);
});
test('explicit new policy bypasses pool, drafts are never reset', async () => {
  const w = worker();
  await w.run({ action: 'chatgpt_send', prompt: 'one' });
  await w.run({ action: 'chatgpt_send', prompt: 'two', tabPolicy: 'new' });
  assert.equal(w.created.length, 2);
  const draft = worker([{ id: 7, status: 'complete', url: 'https://chatgpt.com/c/owned' }], { managed: { 7: { url: 'https://chatgpt.com/c/owned' } }, draft: true });
  assert.equal((await draft.run({ action: 'chatgpt_new_chat', tabId: 7 })).status, 'error');
  assert.equal(draft.updated.length, 0);
});
