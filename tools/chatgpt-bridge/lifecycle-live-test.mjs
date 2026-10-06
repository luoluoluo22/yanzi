// Sends two short prompts, verifies reuse, then closes only the newly managed test tab.
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import assert from 'node:assert/strict';
const token = readFileSync(join(process.env.LOCALAPPDATA, 'OpenQuickHost/ExtensionStorage/chatgpt-bridge/api-token.txt'), 'utf8').trim();
async function api(path, task) {
  const response = await fetch('http://127.0.0.1:53921' + path, { method: task ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json' }, body: task ? JSON.stringify(task) : undefined });
  const data = await response.json(); if (!response.ok) throw new Error(data.error); return data;
}
async function job(task) {
  const created = await api('/api/jobs', task);
  const deadline = Date.now() + 240000;
  while (Date.now() < deadline) {
    const result = await api('/api/jobs/' + created.id);
    if (!['queued', 'running'].includes(result.status)) { assert.equal(result.status, 'success', result.message); return result.data; }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error('Task still pending; inspect before retrying');
}
assert.equal((await api('/health')).extensionVersion, '0.2.2', 'Reload browser extension first');
const before = await job({ action: 'chatgpt_status' });
console.log('START lifecycle test; existing tabs:', before.tabs.length);
// Force the first test tab to be new so no earlier managed work is reset by this test.
const first = await job({ action: 'chatgpt_send', tabPolicy: 'new', prompt: 'Reply exactly LIFE_A_831', timeoutSeconds: 180 });
assert.equal(first.visibility, 'hidden'); assert.ok(first.text.includes('LIFE_A_831'));
const second = await job({ action: 'chatgpt_new_chat', tabId: first.tabId });
assert.equal(second.tabId, first.tabId); assert.equal(second.lifecycle.reused, true);
const reply = await job({ action: 'chatgpt_send', tabId: second.tabId, prompt: 'Reply exactly LIFE_B_833', timeoutSeconds: 180, closeAfter: true });
assert.ok(reply.text.includes('LIFE_B_833')); assert.equal(reply.visibility, 'hidden');
assert.notEqual(reply.conversationId, first.conversationId); assert.equal(reply.lifecycle.closed, true);
const after = await job({ action: 'chatgpt_status' });
assert.ok(!after.tabs.some(tab => tab.tabId === first.tabId));
for (const tab of before.tabs) assert.ok(after.tabs.some(item => item.tabId === tab.tabId), 'Existing tab unexpectedly disappeared: ' + tab.tabId);
const result = { ok: true, before, first, second, reply, after };
const directory = join(process.cwd(), '.artifacts/chatgpt-lifecycle-test'); mkdirSync(directory, { recursive: true });
writeFileSync(join(directory, 'report.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify({ ok: true, reusedTabId: first.tabId, firstConversation: first.conversationId, secondConversation: reply.conversationId, closed: reply.lifecycle.closed, beforeCount: before.tabs.length, afterCount: after.tabs.length }));
