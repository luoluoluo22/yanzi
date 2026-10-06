import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { createBridge, validateTask } from '../server.mjs';

async function setup(t, options = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'yanzi-chatgpt-test-'));
  const bridge = await createBridge({ directory, port: 0, allowTestSocket: true, ...options });
  t.after(() => bridge.close());
  const request = async (path, method = 'GET', value, headers = {}) => {
    const response = await fetch(bridge.origin + path, { method, headers: { Authorization: 'Bearer ' + bridge.token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json', ...headers }, body: value ? JSON.stringify(value) : undefined });
    return { status: response.status, data: await response.json() };
  };
  return { bridge, request, directory };
}
const send = { action: 'chatgpt_send', prompt: '只回复测试成功', timeoutSeconds: 10 };

test('validates prompt, operation and timeout bounds', () => {
  assert.throws(() => validateTask({ action: 'execute_script' }));
  assert.throws(() => validateTask({ ...send, prompt: ' ' }));
  assert.throws(() => validateTask({ ...send, timeoutSeconds: 1201 }));
  assert.throws(() => validateTask({ ...send, tabId: '1' }));
  assert.equal(validateTask(send).prompt, send.prompt);
  assert.equal(validateTask(send).tabPolicy, 'reuse');
  assert.equal(validateTask(send).temporary, true);
  assert.equal(validateTask({ ...send, temporary: false }).temporary, false);
  assert.throws(() => validateTask({ ...send, temporary: 'true' }));
  assert.throws(() => validateTask({ ...send, tabPolicy: 'unknown' }));
  assert.throws(() => validateTask({ ...send, closeAfter: 'true' }));
  assert.throws(() => validateTask({ action: 'chatgpt_close' }));
  assert.equal(validateTask({ action: 'chatgpt_cleanup' }).action, 'chatgpt_cleanup');
});
test('rejects unauthenticated, cross-origin and missing CSRF header requests', async t => {
  const { bridge, request } = await setup(t);
  assert.equal((await fetch(bridge.origin + '/api/jobs')).status, 401);
  assert.equal((await request('/api/jobs', 'POST', send, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await request('/api/jobs', 'POST', send, { 'X-Bridge-Request': '' })).status, 403);
  const page = await fetch(bridge.origin);
  assert.match(page.headers.get('set-cookie'), /HttpOnly; SameSite=Strict/);
  assert.ok(!(await page.text()).includes(bridge.token));
  assert.equal((await request('/api/jobs', 'POST', send)).status, 202);
});
test('queues offline tasks, serializes commands and correlates results', async t => {
  const { bridge, request, directory } = await setup(t);
  const a = (await request('/api/jobs', 'POST', send)).data;
  const b = (await request('/api/jobs', 'POST', send)).data;
  assert.equal(a.status, 'queued');
  const ws = new WebSocket(bridge.origin.replace('http:', 'ws:') + '/v1/browser/ws');
  await once(ws, 'open');
  const received = once(ws, 'message');
  ws.send(JSON.stringify({ type: 'register', version: '0.2.0' }));
  const first = JSON.parse((await received)[0]);
  assert.equal(first.taskId, a.id);
  ws.send(JSON.stringify({ type: 'task_response', taskId: 'wrong-id', status: 'success' }));
  assert.equal(bridge.state.jobs.find(j => j.id === a.id).status, 'running');
  const next = once(ws, 'message');
  ws.send(JSON.stringify({ type: 'task_response', taskId: a.id, status: 'success', data: { text: '测试成功', tabId: 7 } }));
  assert.equal(JSON.parse((await next)[0]).taskId, b.id);
  const stored = JSON.parse(readFileSync(join(directory, 'state.json')));
  assert.equal(stored.jobs[0].data.text, '测试成功');
  ws.close(); await once(ws, 'close');
  await new Promise(resolve => setTimeout(resolve, 20));
  assert.equal((await request('/api/jobs/' + b.id)).data.status, 'interrupted');
});
test('one-off, interval, paused and named event schedules', async t => {
  let time = Date.now();
  const { bridge, request } = await setup(t, { now: () => time });
  const at = (await request('/api/schedules', 'POST', { task: send, at: new Date(time + 1000).toISOString() })).data;
  const interval = (await request('/api/schedules', 'POST', { task: send, intervalSeconds: 60 })).data;
  const event = (await request('/api/schedules', 'POST', { task: send, event: 'file.changed' })).data;
  assert.equal((await request('/api/schedules', 'POST', { task: send, intervalSeconds: 1 })).status, 400);
  assert.equal((await request('/api/schedules', 'POST', { task: send, event: 'x', intervalSeconds: 60 })).status, 400);
  time += 3600000; bridge.tick(); bridge.tick();
  assert.equal(bridge.state.jobs.length, 2);
  assert.equal(bridge.state.schedules.find(s => s.id === at.id).enabled, false);
  assert.ok(bridge.state.schedules.find(s => s.id === interval.id).nextAt > time);
  assert.equal((await request('/api/events', 'POST', { name: 'file.changed' })).data.jobs.length, 1);
  await request('/api/schedules/' + event.id, 'PATCH', { enabled: false });
  assert.equal((await request('/api/events', 'POST', { name: 'file.changed' })).data.jobs.length, 0);
});
test('marks in-flight work interrupted on restart without resending', async t => {
  const directory = mkdtempSync(join(tmpdir(), 'yanzi-chatgpt-recovery-'));
  writeFileSync(join(directory, 'state.json'), JSON.stringify({ jobs: [{ id: 'old', status: 'running', task: send }], schedules: [] }));
  const bridge = await createBridge({ directory, port: 0 });
  t.after(() => bridge.close());
  assert.equal(bridge.state.jobs[0].status, 'interrupted');
});
