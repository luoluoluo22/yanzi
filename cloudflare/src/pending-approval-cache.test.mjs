import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createPendingApprovalCache} from './pending-approval-cache.js';

test('same authenticated account shares 15-second cache without sharing across users', async () => {
  let now = 0;
  const cache = createPendingApprovalCache({clock: () => now, ttlMs: 15_000});
  let queries = 0;
  const query = async () => ({records: [++queries]});
  assert.deepEqual(await cache.load('user-a', query), {records: [1]});
  assert.deepEqual(await cache.load('user-a', query), {records: [1]});
  assert.deepEqual(await cache.load('user-b', query), {records: [2]});
  now = 15001;
  assert.deepEqual(await cache.load('user-a', query), {records: [3]});
});

test('an approval or cancellation invalidates only the affected account', async () => {
  const cache = createPendingApprovalCache();
  let updates = 0;
  const query = () => ++updates;
  assert.equal(await cache.load('a', query), 1);
  assert.equal(await cache.load('b', query), 2);
  cache.invalidate('a');
  assert.equal(await cache.load('a', query), 3);
  assert.equal(await cache.load('b', query), 2);
});

test('failed query never becomes a cached success or suppresses later recovery', async () => {
  const cache = createPendingApprovalCache();
  await assert.rejects(cache.load('user', () => {throw new Error('offline');}), /offline/);
  assert.equal(await cache.load('user', () => 42), 42);
});

test('bounded cache evicts oldest account and deduplicates concurrent queries', async () => {
  const cache = createPendingApprovalCache({maxUsers: 2});
  let calls = 0;
  const query = async () => {calls++; return calls;};
  const values = await Promise.all([cache.load('a', query), cache.load('a', query)]);
  assert.deepEqual(values, [1,1]);
  await cache.load('b', query);
  await cache.load('c', query);
  assert.equal(await cache.load('a', query), 4);
});
