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
  const pending = context.yanziOpenCliPerformRead('xiaohongshu', 'profile');
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
  const pending = context.yanziOpenCliPerformRead('xiaohongshu', 'profile');
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
