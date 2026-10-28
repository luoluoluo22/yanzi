'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.join(__dirname, '..');
const content = fs.readFileSync(path.join(root, 'browser-extension', 'chatgpt-content.js'), 'utf8');
const bg = fs.readFileSync(path.join(root, 'browser-extension', 'chatgpt-background.js'), 'utf8');
const installedIdle = process.env.LOCALAPPDATA
  ? path.join(process.env.LOCALAPPDATA, 'OpenQuickHost', 'Extensions', 'idle-chatgpt-tasks', 'IdleTaskApp.cs')
  : null;
const idle = fs.readFileSync(installedIdle && fs.existsSync(installedIdle)
  ? installedIdle : path.join(root, 'tools', 'idle-chatgpt-tasks', 'IdleTaskApp.cs'), 'utf8');
let passed = 0;
function test(label, fn) { fn(); console.log('PASS ' + label); passed++; }
const match = content.match(/  function verifiedTemporaryReplyContext\([\s\S]*?\r?\n  }\r?\n(?=  const meta)/);
assert.ok(match, 'production helper is present near meta()');
const scope = {URL, location: {href: 'https://chatgpt.com/c/test-123?temporary-chat=true'}};
vm.runInNewContext(match[0] + '\nthis.verify = verifiedTemporaryReplyContext;', scope);
function verify(verified, id, url) {scope.location.href = url; return scope.verify(verified, id);}
test('temporary mode confirmed at send, UI marker disappears, same URL and chat => success', () =>
  assert.equal(verify(true, 'test-123', 'https://chatgpt.com/c/test-123?temporary-chat=true'), true));
test('missing pre-send confirmation => reject', () =>
  assert.equal(verify(false, 'test-123', 'https://chatgpt.com/c/test-123?temporary-chat=true'), false));
test('switch to normal mode => reject', () =>
  assert.equal(verify(true, 'test-123', 'https://chatgpt.com/c/test-123'), false));
test('switch to different conversation => reject', () =>
  assert.equal(verify(true, 'test-123', 'https://chatgpt.com/c/another?temporary-chat=true'), false));
test('switch to another host => reject', () =>
  assert.equal(verify(true, 'test-123', 'https://evil.test/c/test-123?temporary-chat=true'), false));
test('missing ID allowed only when no confirmed conversation ID was observed', () => {
  assert.equal(verify(true, null, 'https://chatgpt.com/?temporary-chat=true'), true);
  assert.equal(verify(true, 'test-123', 'https://chatgpt.com/?temporary-chat=true'), false);
});
test('content script verifies temporary mode before send and checks the same context after reply', () => {
  assert.match(content, /if \(task\.temporary === true && !temporaryEvidence\(\)\) throw new Error\('临时聊天状态已改变，未发送消息'\)/);
  assert.match(content, /if \(task\.temporary === true && !replyModeEvidence\s*&& !verifiedSession\)/);
});
test('idle ChatGPT creates independent temporary tab and does not close on error', () => {
  assert.match(idle, /tabPolicy\s*=\s*"new"/);
  assert.match(idle, /temporary\s*=\s*true/);
  assert.match(bg, /task\.closeAfter && response\.status === "success"/);
  assert.match(bg, /deliveryUncertain: true/);
});
test('completed reply reports verified temporary mode even after the DOM label disappears', () => {
  assert.match(content, /temporary: Boolean\(replyModeEvidence \|\| verifiedSession\)/);
  assert.match(content, /temporaryEvidence: replyModeEvidence \|\| \(verifiedSession/);
});
console.log('GPT_TEMP_MODE_CHECKS=' + passed);
