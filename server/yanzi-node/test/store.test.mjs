import assert from 'node:assert/strict';
import {mkdtempSync} from 'node:fs';
import os from 'node:os';
import {join} from 'node:path';
import test from 'node:test';
import {AtomicJsonDeviceStore} from '../src/store.mjs';

test('durable store compareExchange persists execution state', async () => {
  const root = mkdtempSync(join(os.tmpdir(), 'yanzi-store-'));
  const store = new AtomicJsonDeviceStore(root);
  assert.equal(await store.compareExchange('job', null, {state: 'saved'}), true);
  assert.equal(await store.compareExchange('job', null, {state: 'wrong'}), false);
  assert.equal(await store.compareExchange('job', 'saved', {state: 'executing'}), true);
  await store.close();

  const reopened = new AtomicJsonDeviceStore(root);
  assert.deepEqual(await reopened.get('job'), {state: 'executing'});
  await reopened.close();
});
