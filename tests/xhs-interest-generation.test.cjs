'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const base = path.resolve(__dirname, '..', 'browser-extension');
const contentSource = fs.readFileSync(path.join(base, 'webapps/xiaohongshu-filter/content.js'), 'utf8');
const backgroundSource = fs.readFileSync(path.join(base, 'chatgpt-background.js'), 'utf8');
const section = (source, start, end) => {
  const a = source.indexOf(start);
  const b = source.indexOf(end, a + start.length);
  assert.ok(a >= 0 && b > a, 'missing implementation section: ' + start);
  return source.slice(a, b);
};

async function testContentToggle() {
  const calls = [];
  let filters = 0;
  let saved = 0;
  const sandbox = {
    chrome: { runtime: { sendMessage: async message => { calls.push(message.type); return { ok: true }; } } },
    clearTimeout,
    persistLocalFeedState: async () => { saved++; },
    renderInterestPanel: () => {},
    updateInterestNavButton: () => {},
    showToast: () => {},
    reconcileLocalFeedAssignmentsWithDom: () => { filters++; },
    getLocalFeedFutureSupply: () => 50,
    LOCAL_FEED_REPLENISH_THRESHOLD: 8
  };
  vm.createContext(sandbox);
  vm.runInContext(`
    var localFeedState = { preferences: { generationEnabled: true, updatedAt: 1 }, generation: { inFlight: true, startedAt: 3 } };
    var localFeedRetryTimer = null;
    var localFeedLoaded = true;
    var LOCAL_TEST_NOTES_ENABLED = true;
  ` + section(contentSource, '  function isLocalFeedGenerationEnabled()', '  async function maybeReplenishLocalFeed()') +
    section(contentSource, '  async function maybeReplenishLocalFeed()', '  async function setLocalFeedFeedback(') +
    section(contentSource, '  async function setLocalFeedGenerationEnabled(', '  async function updateLocalFeedSystemPrompt('), sandbox);

  await sandbox.setLocalFeedGenerationEnabled(false);
  assert.equal(sandbox.isLocalFeedGenerationEnabled(), false, 'toggle should disable generation');
  assert.equal(sandbox.localFeedState.generation.inFlight, false, 'in-flight UI state should be reset');
  assert.ok(calls.includes('yanzi_xhs_cancel_local_notes'), 'disable should cancel current GPT task');
  assert.equal(saved, 1, 'disable setting should be persisted');
  await sandbox.maybeReplenishLocalFeed();
  assert.equal(filters, 0, 'disabled mode should exit before replenishment');

  await sandbox.setLocalFeedGenerationEnabled(true);
  assert.equal(sandbox.isLocalFeedGenerationEnabled(), true, 'toggle should re-enable generation');
  assert.equal(saved, 2, 're-enabled setting should be persisted');
  assert.ok(filters > 0, 'enable should resume inventory check');
}

async function testBackgroundGateAndCancel() {
  let onMessage;
  let onChanged;
  let enabled = false;
  let launched = 0;
  let resolveTask;
  const removed = [];
  const sandbox = {
    chrome: {
      storage: {
        local: { get: async key => ({ [key]: { preferences: { generationEnabled: enabled } } }) },
        onChanged: { addListener: callback => { onChanged = callback; } }
      },
      runtime: { onMessage: { addListener: callback => { onMessage = callback; } } },
      tabs: { remove: async id => { removed.push(id); } }
    },
    generateXhsLocalFeedNotes: () => {
      launched++;
      return new Promise(resolve => { resolveTask = resolve; });
    }
  };
  vm.createContext(sandbox);
  vm.runInContext(`
    let xhsLocalFeedGenerationPromise = null;
    let xhsLocalFeedGenerationTabId = null;
    let xhsLocalFeedGenerationCancelled = false;
    let xhsLocalFeedGenerationPaused = false;
    const XHS_LOCAL_FEED_STORAGE_KEY = 'yanzi.xhs.local-feed.v2';
  ` + section(backgroundSource, 'function cancelXhsLocalFeedGeneration()', 'const XHS_EVALUATION_TAB_TTL_MS') +
    section(backgroundSource, 'function queueXhsLocalFeedGeneration(message)', 'chrome.storage?.onChanged?.addListener?.') +
    section(backgroundSource, 'chrome.storage?.onChanged?.addListener?.', '// Feedback origin must be captured'), sandbox);

  const request = () => new Promise(resolve => {
    onMessage({ type: 'yanzi_xhs_generate_local_notes' }, {}, resolve);
  });
  const blocked = await request();
  assert.equal(blocked.ok, false, 'background should reject while disabled');
  assert.equal(launched, 0, 'paused background must not open GPT');

  enabled = true;
  onChanged({ 'yanzi.xhs.local-feed.v2': { newValue: { preferences: { generationEnabled: true } } } }, 'local');
  const pending = request();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(launched, 1, 'enabled background may launch generation');
  vm.runInContext('xhsLocalFeedGenerationTabId = 27;', sandbox);

  let cancelReply;
  onMessage({ type: 'yanzi_xhs_cancel_local_notes' }, {}, reply => { cancelReply = reply; });
  assert.equal(cancelReply.ok, true);
  assert.deepEqual(removed, [27], 'cancel should close active GPT tab');
  assert.equal(vm.runInContext('xhsLocalFeedGenerationCancelled', sandbox), true);

  resolveTask({ notes: [] });
  await pending;
  onChanged({ 'yanzi.xhs.local-feed.v2': { newValue: { preferences: { generationEnabled: false } } } }, 'local');
  const again = await request();
  assert.equal(again.ok, false);
  assert.equal(launched, 1, 'disabled again must block further calls');
}

(async () => {
  await testContentToggle();
  await testBackgroundGateAndCancel();
  console.log('PASS xhs-interest-generation: off/on persistence, no replenishment when off, background gate, GPT tab cancellation');
})().catch(error => { console.error(error); process.exitCode = 1; });
