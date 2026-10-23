import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const source = readFileSync(new URL('../../../browser-extension/chatgpt-background.js', import.meta.url), 'utf8');
function worker(tabs = [], options = {}) {
  tabs.forEach(tab => { tab.status ||= 'complete'; });
  const created = [], injected = [], commands = [], removed = [], updated = [], reloaded = [];
  let remainingAccountFailures = options.accountFailures || 0;
  const storage = { chatgptManagedTabs: options.managed || {} };
  const context = vm.createContext({ console, URL, setTimeout: options.scriptHang || options.networkProbe || options.accountFailures || options.alwaysAccountError || options.disappearingTab ? fn => setTimeout(fn,0) : () => 0,
    clearTimeout: () => {}, clearInterval:()=>{}, setInterval: () => 0,
    WebSocket: class { static OPEN = 1; readyState = 0; },
    chrome: {
      runtime: { getManifest: () => ({ version: '0.2.0' }), onInstalled: {addListener(){}}, lastError:null },
      contextMenus: { remove: (_id, callback) => callback?.(), create: (_item, callback) => callback?.(), onClicked: {addListener(){} } },
      action: { setBadgeText: async () => {}, setBadgeBackgroundColor: async () => {} },
      storage: { local: { set() {} }, session: { get: async () => storage, set: async value => Object.assign(storage, value) } }, alarms: { create() {}, onAlarm: { addListener() {} } },
      tabs: {
        query: async () => tabs,
        create: async input => { created.push(input); const tab = { id: 99 + created.length, status: 'complete', ...input }; if (!(options.disappearingTab && created.length===1 && input.active===false)) tabs.push(tab); return tab; },
        get: async id => { const tab=tabs.find(tab=>tab.id===id); if(!tab && options.disappearingTab)throw new Error('No tab with id: '+id);return tab; },
        update: async (id, changes) => { updated.push({ id, changes }); if(changes.active===true)tabs.forEach(tab=>{tab.active=tab.id===id});return Object.assign(tabs.find(tab => tab.id === id), changes); },
        remove: async id => { removed.push(id); tabs.splice(tabs.findIndex(tab => tab.id === id), 1); },
        reload: async id => { reloaded.push(id); const tab = tabs.find(tab => tab.id === id); tab.status = 'complete'; return tab; },
        sendMessage: async (id, task) => { commands.push({ id, task }); const tab = tabs.find(tab => tab.id === id);
          if (options.alwaysAccountError || remainingAccountFailures-- > 0) return {
            status: 'error', errorCode: 'account_load_failed',
            deliveryPhase: options.afterSend ? 'after_send' : 'before_send',
            data: { diagnostics: { notice: '无法加载你的账户' } }, message: 'account failure'
          };
          if (task.task.action === 'chatgpt_send') tab.url = 'https://chatgpt.com/c/test-' + id;
          return { status: options.failure ? 'error' : 'success', data: { text: 'OK', visibility: 'hidden', url: tab.url } }; }
      },
      scripting: { executeScript: async request => {
          if(options.scriptHang)return new Promise(()=>{});
          if (request.func) return [{ result: options.accountFailures || options.alwaysAccountError ? true : !options.draft }];
          injected.push(request);
        } }
    }
  });
  vm.runInContext(source, context);
  return { created, injected, commands, removed, updated, reloaded, storage,
    setObserver: value=>{context.yanziChatGptNetworkObserver=value;},
    run: task => context.runChatGptTask(task) };
}
test('owned background account failure refreshes once and retries only before send', async () => {
  const w = worker([], { accountFailures: 1 });
  const r = await w.run({ taskId: 'recovery-once', action: 'chatgpt_send', prompt: 'PING', timeoutSeconds: 50 });
  assert.equal(r.status, 'success');
  assert.deepEqual(w.reloaded, [100]);
  assert.equal(w.commands.length, 2);
  assert.equal(r.data.accountPageRecovery.refreshes, 1);
  assert.equal(r.data.accountPageRecovery.recovered, true);
});

test('persistent account failure stops after two refreshes and preserves the tab', async () => {
  const w = worker([], { alwaysAccountError: true });
  const r = await w.run({ taskId: 'recovery-limit', action: 'chatgpt_send', prompt: 'PING', timeoutSeconds: 50 });
  assert.equal(r.status, 'error');
  assert.equal(w.reloaded.length, 2);
  assert.equal(w.commands.length, 3);
  assert.match(r.message, /自动刷新 2 次后仍加载失败/);
  assert.deepEqual(w.removed, []);
});

test('never refresh after possible delivery, even on an owned background tab', async () => {
  const w = worker([], { accountFailures: 1, afterSend: true });
  const r = await w.run({ taskId: 'post-submit', action: 'chatgpt_send', prompt: 'PING' });
  assert.equal(r.status, 'error');
  assert.equal(w.reloaded.length, 0);
  assert.equal(w.commands.length, 1);
});

test('never refresh a user-controlled tab with an account error', async () => {
  const w = worker([{ id: 17, url: 'https://chatgpt.com/c/user', active: true }], { accountFailures: 1 });
  const r = await w.run({ taskId: 'manual-tab', action: 'chatgpt_send', tabId: 17, prompt: 'PING' });
  assert.equal(r.status, 'error');
  assert.equal(w.reloaded.length, 0);
  assert.equal(w.commands.length, 1);
});

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

test('feedback must resolve the original conversation URL exactly; no tab fallbacks',async()=>{
  const url='https://chatgpt.com/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee';
  const w=worker([
    {id:7,url:'https://chatgpt.com/c/another-conversation'},
    {id:8,url}
  ]);
  const task={action:'chatgpt_feedback_send',prompt:'反馈结果',expectedUrl:url,
    expectedConversationId:'af123456-bb22-cc33-dd44-eeeeeeeeeeee',
    newChat:false,temporary:false,closeAfter:false};
  const result=await w.run(task);
  assert.equal(result.status,'success');
  assert.equal(w.commands.at(-1).id,8);
  assert.equal(w.updated.length,0);
  assert.equal(w.created.length,0);
  const missing=await w.run({...task,expectedUrl:'https://chatgpt.com/c/missing-conversation'});
  assert.equal(missing.status,'error');
  assert.equal(w.commands.length,1);
  const ambiguous=worker([{id:11,url},{id:12,url}]);
  assert.equal((await ambiguous.run(task)).status,'error');
  assert.equal(ambiguous.commands.length,0);
});

test('subagent continue restores exact recorded conversation to inactive new tab',async()=>{
  const url='https://chatgpt.com/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee';
  const other='https://chatgpt.com/c/user-other';
  const w=worker([{id:4,url:other,active:true}]);
  const task={action:'chatgpt_subagent_continue',prompt:'第二轮',expectedUrl:url,
    expectedConversationId:'af123456-bb22-cc33-dd44-eeeeeeeeeeee',
    newChat:false,temporary:false,closeAfter:false};
  const r=await w.run(task);
  assert.equal(r.status,'success');
  assert.equal(w.created.length,1);
  assert.equal(w.created[0].url,url);
  assert.equal(w.created[0].active,false);
  assert.equal(w.commands[0].id,100);
  assert.equal(r.data.lifecycle.managed,true);
  assert.equal(w.storage.chatgptManagedTabs[100].url,url);
  assert.equal(w.updated.length,0);
  const existing=worker([{id:4,url},{id:9,url:other,active:true}]);
  const s=await existing.run(task);
  assert.equal(s.status,'success');
  assert.equal(existing.commands[0].id,4);
  assert.equal(existing.created.length,0);
  const amb=worker([{id:5,url},{id:6,url}]);
  assert.equal((await amb.run(task)).status,'error');
  assert.equal(amb.commands.length,0);
});

test('read-only page diagnostics inspects an exact tab without sending messages',async()=>{
  const url='https://chatgpt.com/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee';
  const w=worker([{id:77,url,active:false,status:'complete'}]);
  const response=await w.run({action:'chatgpt_diagnostics',tabId:77});
  assert.equal(response.status,'success');
  assert.equal(response.data.tabId,77);
  assert.equal(response.data.pageType,'conversation');
  assert.equal(response.data.status,'complete');
  assert.equal(w.commands.length,0);
  assert.equal(w.updated.length,0);
  assert.equal(w.created.length,0);
  assert.equal((await w.run({action:'chatgpt_diagnostics',tabId:78})).status,'error');
});

test('hung ChatGPT renderer is diagnosed without sending or retrying prompts',async()=>{
  const url='https://chatgpt.com/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee';
  const w=worker([{id:86,url,active:false,status:'complete'}],{scriptHang:true});
  const diagnostic=await w.run({action:'chatgpt_diagnostics',tabId:86});
  assert.equal(diagnostic.status,'success');
  assert.match(diagnostic.data.error,/页面渲染进程无响应/);
  assert.equal(w.commands.length,0);
  const send=await w.run({action:'chatgpt_send',tabId:86,newChat:false,prompt:'DO NOT SEND'});
  assert.equal(send.status,'error');
  assert.match(send.message,/页面渲染进程无响应/);
  assert.equal(w.commands.length,0);
});

test('network probe accepts only inactive Yanzi-owned tabs and never sends a chat message',async()=>{
  const url='https://chatgpt.com/c/network-test-1234';
  const managed={'71':{url}};
  const w=worker([{id:71,url,active:false,status:'complete'}],{managed,networkProbe:true});
  let stopped=0;
  w.setObserver({supported:true,start(tabId){
    assert.equal(tabId,71);
    return {stop(){stopped++;return {available:true,tabId,started:2,completed:1,
      failed:1,methods:{GET:2},classes:{page_resource:2}};}};
  }});
  const result=await w.run({action:'chatgpt_network_probe',tabId:71,durationMs:1000});
  assert.equal(result.status,'success');
  assert.equal(result.data.started,2);
  assert.equal(result.data.failed,1);
  assert.equal(stopped,2); // explicit stop plus finalizer
  assert.equal(w.commands.length,0);
  assert.equal(w.updated.length,0);
  const bad=await w.run({action:'chatgpt_network_probe',tabId:72,durationMs:1000});
  assert.equal(bad.status,'error');
});

test('network metadata is correlated with the correct send task without response bodies',async()=>{
  const url='https://chatgpt.com/c/network-test-2345';
  const w=worker([{id:72,url,active:false,status:'complete'}],
    {managed:{'72':{url}},networkProbe:true});
  w.setObserver({supported:true,start:tabId=>({
    snapshot:()=>({available:true,tabId,started:1,completed:1,failed:0}),
    stop:()=>({available:true,tabId,started:1,completed:1,failed:0,
      statusBands:{'2xx':1},methods:{POST:1}})
  })});
  const res=await w.run({action:'chatgpt_send',tabId:72,newChat:false,prompt:'safely submitted'});
  assert.equal(res.status,'success');
  assert.equal(res.data.network.tabId,72);
  assert.equal(res.data.network.started,1);
  assert.equal(w.commands.length,1);
});

test('Ego missing inactive NEW tab retries creation before send without replay or takeover',async()=>{
  const w=worker([{id:7,url:'https://chatgpt.com/c/user',active:true}],{disappearingTab:true});
  const result=await w.run({taskId:'ego-tab-probe',action:'chatgpt_send',
    prompt:'one-only-send',temporary:false,newChat:true,tabPolicy:'new',
    closeAfter:true});
  assert.equal(result.status,'success',result.message);
  assert.equal(w.created.length,2);
  assert.equal(w.created[0].active,false);
  assert.equal(w.created[1].active,true);
  assert.equal(w.commands.length,1,'must send the prompt only once');
  assert.equal(w.commands[0].id,101);
  assert.ok(w.updated.some(u=>u.id===7&&u.changes.active===true),'restores user tab focus');
  assert.deepEqual(w.removed,[101],'successful owned child tab is safely reclaimed');
});
