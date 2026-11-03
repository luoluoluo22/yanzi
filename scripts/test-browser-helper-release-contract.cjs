'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..', 'browser-extension');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'));
const version = manifest.version.split('.').map(Number);
assert.ok(version.length === 3 && version.every(Number.isInteger), 'invalid helper version');
assert.ok(version[0] > 0 || version[1] > 5 || (version[1] === 5 && version[2] >= 88), 'old browser helper payload');
assert.equal(manifest.action.default_popup, 'popup/popup.html');
assert.equal(manifest.background.service_worker, 'background.js');
const catalog = JSON.parse(fs.readFileSync(path.join(root, 'webapps/catalog.json'), 'utf8'));
const required = ['xiaohongshu.filter', 'douyin.filter', 'chatgpt.auto-allow', 'chatgpt.keep-going'];
for (const id of required) {
  const app = catalog.apps.find(x => x.id === id);
  assert.ok(app, 'missing webapp: ' + id);
  for (const name of [app.contentScript, ...(app.contentScripts || [])].filter(Boolean))
    assert.ok(fs.existsSync(path.join(root, name)), 'missing script: ' + name);
}
assert.ok(fs.existsSync(path.join(root, 'webapps/common/webapp-bridge.js')), 'missing webapp bridge');
const html = fs.readFileSync(path.join(root, 'popup/popup.html'), 'utf8');
assert.ok(html.includes('网页 API') && html.includes('market-top-bar'), 'old extension popup');
const scripts = [
  'background.js', 'webapps/runtime-background.js',
  'webapps/common/webapp-bridge.js',
  'popup/popup.js', 'popup/opencli-panel.js',
  ...catalog.apps.map(x => x.contentScript).filter(Boolean)
];
for (const name of scripts) execFileSync(process.execPath, ['--check', path.join(root, name)], {stdio:'pipe'});
console.log('BROWSER_HELPER_CONTRACT_OK version=' + manifest.version + ' apps=' + catalog.apps.length + ' scripts=' + scripts.length);