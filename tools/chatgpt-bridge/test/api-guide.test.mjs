import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { JSDOM } from 'jsdom';
import { validateTask } from '../server.mjs';
const source = readFileSync(new URL('../../../browser-extension/popup/api-guide.js', import.meta.url), 'utf8');
function page(t, html = '<section data-api-guide></section>') {
  const dom = new JSDOM(html, { runScripts: 'outside-only' });
  t.after(() => dom.window.close());
  dom.window.eval(source);
  return dom;
}
test('all documented task bodies pass actual API validation and generated scripts parse', t => {
  const dom = page(t), guide = dom.window.YanziApiGuide;
  for (const [key, sample] of Object.entries(guide.samples)) {
    if (sample.path === '/api/jobs' && sample.body) assert.doesNotThrow(() => validateTask(sample.body));
    const python = spawnSync('python', ['-c', 'import ast,sys; ast.parse(sys.stdin.read())'], { input: guide.example(key, 'python'), encoding: 'utf8', timeout: 10000 });
    assert.equal(python.status, 0, key + ': ' + python.stderr);
    const node = spawnSync('node', ['--check', '--input-type=module'], { input: guide.example(key, 'javascript'), encoding: 'utf8', timeout: 10000 });
    assert.equal(node.status, 0, key + ': ' + node.stderr);
    const psSource = Buffer.from(guide.example(key, 'powershell'), 'utf8').toString('base64');
    const parser = `$text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${psSource}')); $tokens = $null; $parseErrors = $null; [void][Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$parseErrors); if ($parseErrors.Count) { $parseErrors | Out-String | Write-Output; exit 1 }`;
    const ps = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', parser], { encoding: 'utf8', timeout: 10000 });
    assert.equal(ps.status, 0, key + ': ' + ps.stdout + ps.stderr);
    for (const lang of ['http', 'python', 'powershell', 'javascript']) {
      const text = guide.example(key, lang);
      assert.ok(text.includes(sample.path)); assert.ok(text.includes('Authorization'));
      if (sample.method !== 'GET') assert.ok(text.includes('X-Bridge-Request'));
    }
  }
});
test('guide switches examples and copies the exact selected code or complete contract', async t => {
  const dom = page(t), win = dom.window;
  const copied = [];
  Object.defineProperty(win.navigator, 'clipboard', { value: { writeText: async value => copied.push(value) } });
  await new Promise(resolve => win.document.addEventListener('DOMContentLoaded', resolve));
  const language = win.document.querySelector('[data-language]'); language.value = 'python'; language.dispatchEvent(new win.Event('change'));
  win.document.querySelector('[data-copy="example"]').click(); await new Promise(setImmediate);
  assert.equal(copied[0], win.YanziApiGuide.example('send', 'python'));
  assert.match(win.document.querySelector('[data-copy-status]').textContent, /已复制/);
  win.document.querySelector('[data-copy="notes"]').click(); await new Promise(setImmediate);
  assert.ok(copied[1].includes('codeBlocks')); assert.ok(copied[1].includes('temporary'));
  assert.ok(copied[1].includes('200')); assert.ok(copied[1].includes('/api/jobs/{id}'));
});
test('popup navigation preserves status controls and opens local workbench', async t => {
  const html = readFileSync(new URL('../../../browser-extension/popup/popup.html', import.meta.url), 'utf8');
  const dom = page(t, html), win = dom.window, opened = [];
  win.chrome = { tabs: { create: options => opened.push(options) }, storage: { local: { get: (defaults, callback) => callback(Array.isArray(defaults) ? {} : defaults), set() {} }, onChanged: { addListener() {} } }, runtime: { sendMessage: (message, callback) => callback?.({ status: 'disconnected' }) } };
  win.eval(readFileSync(new URL('../../../browser-extension/popup/popup.js', import.meta.url), 'utf8'));
  await new Promise(resolve => win.document.addEventListener('DOMContentLoaded', resolve));
  win.document.querySelector('#show-api').click();
  assert.equal(win.document.querySelector('#panel-api').hidden, false); assert.equal(win.document.querySelector('#panel-overview').hidden, true);
  win.document.querySelector('[data-panel="logs"]').click();
  assert.equal(win.document.querySelector('#panel-logs').hidden, false);
  win.document.querySelector('#open-workbench').click(); assert.equal(opened[0].url, 'http://127.0.0.1:53921/');
});
