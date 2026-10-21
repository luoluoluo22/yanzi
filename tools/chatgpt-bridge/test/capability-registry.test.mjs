import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createBridge } from '../server.mjs';
import { listBrowserCapabilities, resolveBrowserCapability } from '../capability-registry.mjs';

test('capability inventory enumerates only implemented browser operations', () => {
  const manifest = listBrowserCapabilities();
  assert.equal(manifest.version, 1);
  assert.deepEqual(manifest.adapters.map(a => a.site), ['chatgpt']);
  assert.deepEqual(manifest.capabilities.map(x => x.id), [
    'chatgpt.ask', 'chatgpt.read', 'chatgpt.new', 'chatgpt.status',
    'chatgpt.image', 'chatgpt.images', 'chatgpt.cleanup'
  ]);
  assert.ok(manifest.capabilities.every(x => x.inputSchema.additionalProperties === false));
  assert.deepEqual(manifest.capabilities.find(x => x.id === 'chatgpt.ask').inputSchema.required, ['prompt']);
});

test('capability resolution never allows arbitrary commands, fields or implicit defaults', () => {
  assert.deepEqual(resolveBrowserCapability({
    site: 'chatgpt', command: 'ask', arguments: { prompt: 'hello', temporary: true }
  }), { capability: 'chatgpt.ask', access: 'write',
    task: { action: 'chatgpt_send', prompt: 'hello', temporary: true } });
  assert.equal(resolveBrowserCapability({ capability: 'chatgpt.status' }).task.action, 'chatgpt_status');
  for (const value of [
    {}, { capability: 'other.exec' },
    { capability: 'chatgpt.ask', arguments: {} },
    { capability: 'chatgpt.ask', arguments: { prompt: '' } },
    { capability: 'chatgpt.ask', arguments: { prompt: 'ok', script: 'alert(1)' } },
    { capability: 'chatgpt.ask', arguments: { prompt: 'ok', temporary: 'true' } },
    { capability: 'chatgpt.ask', arguments: { prompt: 'ok', timeoutSeconds: -1 } },
    { capability: 'chatgpt.images' },
    { capability: 'chatgpt.status', site: 'another' },
    { capability: 'chatgpt.status', arguments: [] },
    { capability: 'chatgpt.status', action: 'chatgpt_close' },
    JSON.parse('{"capability":"chatgpt.ask","arguments":{"prompt":"ok","__proto__":{"admin":true}}}')
  ]) {
    assert.throws(() => resolveBrowserCapability(value), JSON.stringify(value));
  }
});

test('authenticated capability HTTP API maps into existing durable job queue', async t => {
  const dir = mkdtempSync(join(tmpdir(), 'yanzi-capability-test-'));
  const bridge = await createBridge({ directory: dir, port: 0, allowTestSocket: true });
  t.after(() => bridge.close());
  const request = async (route, method = 'GET', value, headers = {}) => {
    const res = await fetch(bridge.origin + route, {
      method,
      headers: { Authorization: 'Bearer ' + bridge.token,
        'X-Bridge-Request': '1', 'Content-Type': 'application/json', ...headers },
      body: value === undefined ? undefined : JSON.stringify(value)
    });
    return { status: res.status, data: await res.json() };
  };
  assert.equal((await fetch(bridge.origin + '/api/capabilities')).status, 401);
  assert.equal((await request('/api/capabilities', 'GET', undefined, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await request('/api/capabilities/execute', 'POST',
    { capability: 'chatgpt.ask', arguments: { prompt: 'test' } },
    { 'X-Bridge-Request': '' })).status, 403);
  const inventory = await request('/api/capabilities');
  assert.equal(inventory.status, 200);
  assert.equal(inventory.data.capabilities.length, 7);

  const submitted = await request('/api/capabilities/execute', 'POST',
    { capability: 'chatgpt.ask', arguments: { prompt: 'hello', temporary: true, closeAfter: true } });
  assert.equal(submitted.status, 202);
  assert.equal(submitted.data.capability, 'chatgpt.ask');
  assert.equal(submitted.data.job.task.action, 'chatgpt_send');
  assert.equal(submitted.data.job.task.temporary, true);
  assert.equal(submitted.data.job.source, 'capability:chatgpt.ask');
  assert.equal(submitted.data.job.status, 'queued');
  const readback = await request('/api/jobs/' + submitted.data.job.id);
  assert.equal(readback.data.id, submitted.data.job.id);

  const before = bridge.state.jobs.length;
  for (const req of [
    { capability: 'chatgpt.image', arguments: { prompt: '' } },
    { capability: 'xiaohongshu.publish', arguments: { text: 'nope' } },
    { capability: 'chatgpt.ask', arguments: { prompt: 'hello', tabId: '3' } }
  ]) {
    assert.equal((await request('/api/capabilities/execute', 'POST', req)).status, 400);
  }
  assert.equal(bridge.state.jobs.length, before, 'invalid requests never enqueue a job');
});
