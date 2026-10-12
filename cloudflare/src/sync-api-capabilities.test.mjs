import test from 'node:test';
import assert from 'node:assert/strict';
import {createSyncApi} from './sync-api.js';

function mockDb(exists = true) {
  let count = 0;
  return {
    get count() { return count; },
    prepare(sql) {
      count++;
      return {first: async () => exists ? {name: sql.includes('history') ? 'user_sync_object_history' : 'user_sync_objects'} : null};
    }
  };
}

test('capabilities checks schema once per D1 binding, not on every poll', async () => {
  const api = createSyncApi({
    requireAuth: async () => ({userId:'u'}),
    json: value => new Response(JSON.stringify(value), {headers: {'content-type':'application/json'}})
  });
  const request = new Request('https://example.com/v1/sync/capabilities');
  const one = mockDb();
  const two = mockDb(false);
  for (let i=0; i<10; i++) {
    const response = await api.handleSyncApi(request, {DB:one}, {});
    assert.equal((await response.json()).objectHistoryAvailable, true);
  }
  assert.equal(one.count, 2);
  assert.equal((await (await api.handleSyncApi(request, {DB:two}, {})).json()).objectSyncAvailable, false);
  assert.equal(two.count, 2, 'cache must not share state across different bindings');
});
