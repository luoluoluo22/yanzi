import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'raccoon-names-'));
process.env.RACCOON_SETTINGS_FILE = path.join(dir, 'settings.json');
const { setRuntimeSetting } = await import('../src/runtime-settings.js');
const { localDeviceId, resolveDeviceSelector, findPeer, routeDeviceTool } = await import('../src/peer-manager.js');
test('device name lookup is case-insensitive, direct ID remains supported', async () => {
  await setRuntimeSetting('remoteDevices', [
    { id: 'notebook-01', name: '笔记本', url: 'http://127.0.0.1:39766/mcp' },
    { id: 'desktop-02', name: '备用电脑', url: 'http://127.0.0.1:39767/mcp' }
  ]);
  assert.equal(resolveDeviceSelector('笔记本'), 'notebook-01');
  assert.equal(findPeer('笔记本').name, '笔记本');
  assert.equal(resolveDeviceSelector('notebook-01'), 'notebook-01');
  assert.equal(resolveDeviceSelector(os.hostname().toUpperCase()), localDeviceId());
  assert.equal(await routeDeviceTool('mock', { deviceId: os.hostname() }, async () => 'local'), 'local');
});
test('unknown or duplicate names are rejected rather than silently running locally', async () => {
  await setRuntimeSetting('remoteDevices', [
    { id: 'peer-one', name: 'duplicate', url: 'http://127.0.0.1:39766/mcp' },
    { id: 'peer-two', name: 'duplicate', url: 'http://127.0.0.1:39767/mcp' }
  ]);
  assert.throws(() => resolveDeviceSelector('duplicate'), /Ambiguous/);
  assert.throws(() => resolveDeviceSelector('missing'), /Unknown/);
});
test.after(() => fs.rmSync(dir, { recursive: true, force: true }));
