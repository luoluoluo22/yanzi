import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createBrowserWake, findEdgeExecutable, tasklistHasEdge } from '../browser-wake.mjs';
import { createBridge } from '../server.mjs';

test('tasklist process detection and safe Edge resolution', () => {
  assert.equal(tasklistHasEdge('"msedge.exe","1548","Console","1","120,812 K"'), true);
  assert.equal(tasklistHasEdge('INFO: No tasks are running which match the specified criteria.'), false);
  assert.equal(tasklistHasEdge('"chrome.exe","111","Console","1","120 K"'), false);
  const env = { 'PROGRAMFILES(X86)': 'C:/Program Files (x86)' };
  assert.match(findEdgeExecutable(env, p => p.includes('Edge') && !p.includes('Beta')), /msedge\.exe$/);
  assert.equal(findEdgeExecutable(env, () => false), null);
});

test('wakes only on Windows when browser not running and observes cooldown', async () => {
  let ticks = 1_000_000, launched = 0, checks = 0;
  const wake = createBrowserWake({ platform: 'win32', now: () => ticks,
    cooldownMs: 1000, running: async () => { checks++; return false; },
    findExecutable: () => 'C:/dummy/msedge.exe',
    launch: async () => { launched++; }
  });
  const simultaneous = await Promise.all([wake.request(), wake.request(), wake.request()]);
  assert.equal(simultaneous[0].status, 'launched');
  assert.equal(simultaneous[1].status, 'launched');
  assert.equal(launched, 1);
  assert.equal(checks, 1);
  assert.equal((await wake.request()).status, 'cooldown');
  ticks += 1000;
  assert.equal((await wake.request()).status, 'launched');
  assert.equal(launched, 2);
});

test('never launches when browser is running, missing or platform unsupported', async () => {
  let launched = 0;
  const args = { platform: 'win32', now: () => 1000,
    running: async () => true, findExecutable: () => 'any',
    launch: () => { launched++; } };
  assert.equal((await createBrowserWake(args).request()).status, 'already_running');
  assert.equal((await createBrowserWake({ ...args, running: async () => false,
    findExecutable: () => null }).request()).status, 'edge_not_found');
  assert.equal((await createBrowserWake({ ...args, platform: 'darwin' }).request()).status, 'unsupported_platform');
  assert.equal(launched, 0);
});

test('queue uses injected wake handler once; real Chrome stays untouched in port-zero tests', async t => {
  let launched = 0;
  const bridge = await createBridge({
    directory: mkdtempSync(join(tmpdir(), 'yanzi-browser-wake-test-')),
    port: 0, allowTestSocket: true, browserWake: { request: () => { launched++; } }
  });
  t.after(() => bridge.close());
  bridge.enqueue({ action: 'chatgpt_send', prompt: 'queued only' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(launched, 1);
  assert.equal(bridge.state.jobs[0].status, 'queued');
});
