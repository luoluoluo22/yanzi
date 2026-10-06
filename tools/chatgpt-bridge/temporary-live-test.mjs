// Explicit real-page test: two temporary prompts, reset, verify empty, then close owned page.
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import assert from 'node:assert/strict';
const token = readFileSync(join(process.env.LOCALAPPDATA, 'OpenQuickHost/ExtensionStorage/chatgpt-bridge/api-token.txt'), 'utf8').trim();
async function api(path, task) {
  const response = await fetch('http://127.0.0.1:53921' + path, { method: task ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json' }, body: task ? JSON.stringify(task) : undefined });
  const result = await response.json(); if (!response.ok) throw new Error(result.error); return result;
}
async function job(task) {
  const created = await api('/api/jobs', task);
  const deadline = Date.now() + 240000;
  while (Date.now() < deadline) {
    const result = await api('/api/jobs/' + created.id);
    if (!['queued', 'running'].includes(result.status)) { assert.equal(result.status, 'success', result.message); return result.data; }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error('Task may already be sent; inspect before retrying');
}
const directory = join(process.cwd(), '.artifacts/chatgpt-temporary-test'); mkdirSync(directory, { recursive: true });
const results = {};
try {
  results.open = await job({ action: 'chatgpt_new_chat' });
  assert.equal(results.open.temporary, true); assert.equal(results.open.visibility, 'hidden');
  console.log('Verified temporary page', results.open.tabId);
  results.json = await job({ action: 'chatgpt_send', tabId: results.open.tabId, prompt: 'Reply with exactly one JSON code block: {"mode":"temporary","marker":"TEMP_JSON_851","unicode":"燕子🦊"}', timeoutSeconds: 180 });
  assert.equal(results.json.temporary, true); assert.equal(results.json.visibility, 'hidden');
  assert.equal(results.json.json?.marker, 'TEMP_JSON_851');
  console.log('Temporary JSON returned');
  results.continued = await job({ action: 'chatgpt_send', tabId: results.open.tabId, prompt: 'What was the marker in your previous JSON? Reply only with that marker.', timeoutSeconds: 180 });
  assert.equal(results.continued.temporary, true); assert.ok(results.continued.text.includes('TEMP_JSON_851'));
  results.reset = await job({ action: 'chatgpt_new_chat', tabId: results.open.tabId });
  assert.equal(results.reset.temporary, true); assert.equal(results.reset.lifecycle.reused, true);
  results.empty = await job({ action: 'chatgpt_messages', tabId: results.open.tabId });
  assert.equal(results.empty.messages.length, 0);
  results.close = await job({ action: 'chatgpt_close', tabId: results.open.tabId });
  assert.ok(results.close.closedTabIds.includes(results.open.tabId));
  results.ok = true;
  console.log(JSON.stringify({ ok: true, tabId: results.open.tabId, temporary: results.json.temporary, json: results.json.json, continued: results.continued.text, resetMessageCount: results.empty.messages.length, closed: results.close.closedTabIds }));
} finally { writeFileSync(join(directory, 'report.json'), JSON.stringify(results, null, 2)); }
