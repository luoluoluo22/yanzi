const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function setup() {
  const sent = [], timers = new Map();
  let sequence = 0, listener;
  const context = vm.createContext({
    crypto: { randomUUID: () => `request-${++sequence}` },
    setTimeout: (fn, delay) => { timers.set(sequence, { fn, delay }); return sequence; },
    clearTimeout: id => timers.delete(id),
    chrome: { runtime: { id: 'own-extension', onMessage: { addListener: fn => { listener = fn; } } } },
    yanziBrowserHost: { isConnected: () => true, send: message => sent.push(message) }
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../browser-extension/opencli-background.js'), 'utf8'), context);
  return { context, sent, timers, listener };
}
test('XHS uses host capability probe then CLI; returns provenance and never reads page DOM', async () => {
  const { context, sent, timers } = setup();
  const pending = context.yanziOpenCliPerformRead('xiaohongshu', 'stats', 1, 'work');
  assert.equal(sent[0].operation, 'capabilities');
  context.handleYanziOpenCliResponse({ requestId: sent[0].requestId, ok: true, data: { available: true } });
  await Promise.resolve();
  assert.equal(sent[1].operation, 'stats');
  assert.equal(sent[1].profile, 'work');
  context.handleYanziOpenCliResponse({ requestId: sent[1].requestId, ok: true,
    result: { data: [{ metric: 'views', total: 710 }], source: 'opencli', profile: 'work', period: 'seven', collectedAt: '2026-10-27T00:00:00Z' } });
  const result = await pending;
  assert.equal(result.data[0].total, 710);
  assert.equal(result.source, 'opencli');
  assert.equal(result.period, 'seven');
  assert.equal(timers.size, 0);
});
test('old host gets actionable error without guessed/fabricated data', async () => {
  const { context, timers, sent } = setup();
  const pending = context.yanziOpenCliPerformRead('xiaohongshu', 'profile', 1, 'work');
  assert.equal([...timers.values()][0].delay, 5000);
  [...timers.values()][0].fn();
  assert.match((await pending).error, /更新宿主/);
  assert.equal(sent.length, 1);
});
test('offline host and write operations do not send requests', async () => {
  const { context, sent } = setup();
  context.yanziBrowserHost.isConnected = () => false;
  assert.equal((await context.yanziOpenCliPerformRead('xiaohongshu', 'notes')).ok, false);
  assert.equal((await context.yanziOpenCliPerformRead('xiaohongshu', 'publish')).ok, false);
  assert.equal(sent.length, 0);
});
test('disconnect settles pending request and ignores late replies', async () => {
  const { context, sent, timers } = setup();
  const pending = context.yanziOpenCliPerformRead('xiaohongshu', 'profile', 1, 'work');
  context.yanziOpenCliOnDisconnect();
  assert.equal((await pending).ok, false);
  context.handleYanziOpenCliResponse({ requestId: sent[0].requestId, ok: true, data: {} });
  assert.equal(timers.size, 0);
});
test('extension message rejects a foreign sender', () => {
  const { listener, sent } = setup();
  let response;
  listener({ action: 'yanzi_opencli_read' }, { id: 'other-extension' }, value => { response = value; });
  assert.equal(response.ok, false);
  assert.equal(sent.length, 0);
});

function configureCreatorTab(context, site, fetchResponses, { existing = true } = {}) {
  const removed = [];
  const host = site === 'douyin' ? 'creator.douyin.com' : 'creator.xiaohongshu.com';
  context.location = { hostname: host };
  context.AbortController = AbortController;
  context.setTimeout = setTimeout;
  context.clearTimeout = clearTimeout;
  context.fetch = async url => ({
    ok: true,
    json: async () => fetchResponses[String(url)]
  });
  context.chrome.tabs = {
    query: async () => existing ? [{ id: 17, discarded: false }] : [],
    create: async () => ({ id: 23 }),
    get: async () => ({ status: 'complete' }),
    remove: async id => { removed.push(id); },
    onUpdated: { addListener() {}, removeListener() {} }
  };
  context.chrome.scripting = {
    executeScript: async ({ func, args, world }) => {
      assert.equal(world, 'MAIN');
      return [{ result: await func(...args) }];
    }
  };
  return removed;
}

test('Douyin profile uses authenticated creator API, never homepage CSS or fabricated zeros', async () => {
  const { context, sent } = setup();
  configureCreatorTab(context, 'douyin', {
    'https://creator.douyin.com/web/api/media/user/info/?aid=1128': {
      user_info: { uid: 'testuid', nickname: 'TestCreator', follower_count: 38 }
    }
  });
  const result = await context.yanziOpenCliPerformRead('douyin', 'profile');
  assert.equal(result.ok, true);
  assert.equal(result.data.nickname, 'TestCreator');
  assert.equal(result.data.follower_count, 38);
  assert.equal(result.data.likes, undefined);
  assert.equal(result.data.following_count, undefined);
  assert.equal(result.source, 'browser-session');
  assert.equal(sent.length, 0);
});

test('Douyin must return a real error when no verified creator identity exists', async () => {
  const { context } = setup();
  configureCreatorTab(context, 'douyin', {
    'https://creator.douyin.com/web/api/media/user/info/?aid=1128': { user_info: null }
  });
  const result = await context.yanziOpenCliPerformRead('douyin', 'profile');
  assert.equal(result.ok, false);
  assert.match(result.error, /登录身份/);
  assert.equal(result.data, undefined);
});

test('inactive same-window tab is removed after read, even when creator API is empty', async () => {
  const { context } = setup();
  const removed = configureCreatorTab(context, 'douyin', {
    'https://creator.douyin.com/web/api/media/user/info/?aid=1128': { user_info: null }
  }, { existing: false });
  const result = await context.yanziOpenCliPerformRead('douyin', 'profile');
  assert.equal(result.ok, false);
  assert.deepEqual(removed, [23]);
  assert.equal(result.temporaryTabClosed, true);
});

test('XHS profile direct read does not contact OpenCLI host or spawn another window', async () => {
  const { context, sent } = setup();
  configureCreatorTab(context, 'xiaohongshu', {
    '/api/galaxy/creator/home/personal_info': {
      data: { name: 'TestAuthor', fans_count: 17, follow_count: 4, faved_count: 29 }
    }
  });
  const result = await context.yanziOpenCliPerformRead('xiaohongshu', 'profile');
  assert.equal(result.ok, true);
  assert.equal(result.data[0].value, 'TestAuthor');
  assert.equal(result.data[1].value, 17);
  assert.equal(result.source, 'browser-session');
  assert.equal(sent.length, 0);
});

test('XHS notes capture runs only on owned temporary tab, which is removed', async () => {
  const { context, sent } = setup();
  const removed = configureCreatorTab(context, 'xiaohongshu', {}, { existing: false });
  context.window = {
    fetch: async () => ({ ok: true, clone: () => ({ json: async () => ({
      code: 0, data: { total: 1, note_infos: [
        { id: 'note-example', title: 'Test post', read_count: 12, like_count: 3 }
      ] }
    }) }) }),
    XMLHttpRequest: class { addEventListener() {} open() {} },
    dispatchEvent() {
      Promise.resolve().then(() => context.window.fetch('/api/galaxy/creator/datacenter/note/analyze/list?page_num=1'));
    }
  };
  context.history = { pushState() {} };
  context.PopStateEvent = class {};
  const result = await context.yanziOpenCliPerformRead('xiaohongshu', 'notes');
  assert.equal(result.ok, true);
  assert.equal(result.data[0].id, 'note-example');
  assert.equal(result.data[0].views, 12);
  assert.deepEqual(removed, [23]);
  assert.equal(sent.length, 0);
});

test('existing creator tab execution errors must not spawn a second tab', async () => {
  const { context } = setup();
  configureCreatorTab(context, 'douyin', {});
  let created = 0;
  context.chrome.tabs.create = async () => { created++; return { id: 24 }; };
  context.chrome.scripting.executeScript = async () => { throw Error('script execution blocked'); };
  const result = await context.yanziOpenCliPerformRead('douyin', 'profile');
  assert.equal(result.ok, false);
  assert.equal(created, 0);
});
