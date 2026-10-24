import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { JSDOM } from 'jsdom';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');

test('extension popup routes Agent team to Ego workbench, without legacy Agent console', t => {
  const html = readFileSync(resolve(root, 'browser-extension/popup/popup.html'), 'utf8');
  const script = readFileSync(resolve(root, 'browser-extension/popup/popup.js'), 'utf8');
  const dom = new JSDOM(html, {
    url: 'chrome-extension://abcdefghabcdefghabcdefghabcdefgh/popup/popup.html',
    runScripts: 'outside-only'
  });
  t.after(() => dom.window.close());
  const document = dom.window.document;
  assert.ok(document.getElementById('open-workbench'), 'Ego workbench launch control exists');
  assert.match(document.body.textContent, /Ego API/, 'explains external Agent service');
  assert.equal(document.getElementById('agent-demo'), null, 'legacy demo control was removed');
  assert.equal(document.getElementById('agent-launch-form'), null, 'legacy Agent form was removed');
  assert.equal(existsSync(resolve(root, 'browser-extension/popup/agent-console-popup.js')), false,
    'obsolete popup script must not be packaged');
  assert.match(script, /getElementById\('open-workbench'\)\.onclick\s*=\s*\(\)\s*=>\s*chrome\.tabs\.create\(\{\s*url:\s*'http:\/\/127\.0\.0\.1:43830\/'\s*\}\)/,
    'workbench control must open the local Ego Agent service');
});
