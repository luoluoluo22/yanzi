import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {FileDeviceStore} from './file-device-store.mjs';
test('native stores survive recreation and atomically claim across two clients', async () => {
  const root = await mkdtemp(join(tmpdir(), 'yanzi-sdk-'));
  const stores = [];
  try {
    const first = new FileDeviceStore(root), second = new FileDeviceStore(root);
    stores.push(first, second);
    await first.set('inbox/op', {state:'saved', output:'汉字'});
    assert.equal((await second.get('inbox/op')).output, '汉字');
    const results = await Promise.all([first, second].map(store => store.compareExchange('inbox/op', 'saved', {state:'executing'})));
    assert.equal(results.filter(Boolean).length, 1);
    const restarted = new FileDeviceStore(root); stores.push(restarted);
    assert.equal((await restarted.get('inbox/op')).state, 'executing');
  } finally { stores.forEach(store => store.close()); await rm(root, {recursive:true, force:true}); }
});
