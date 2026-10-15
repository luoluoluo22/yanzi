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

test('capabilities caches schema per Worker isolate, not request-local D1 proxy', async () => {
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
  assert.equal((await (await api.handleSyncApi(request, {DB:two}, {})).json()).objectSyncAvailable, true);
  assert.equal(two.count, 0, 'a new env.DB proxy should reuse the isolate schema snapshot');

  // A freshly deployed Worker instance has its own cache and can observe
  // a different database schema after a migration or binding change.
  const nextIsolate = createSyncApi({
    requireAuth: async () => ({userId:'u'}),
    json: value => new Response(JSON.stringify(value), {headers: {'content-type':'application/json'}})
  });
  assert.equal((await (await nextIsolate.handleSyncApi(request, {DB:two}, {})).json()).objectSyncAvailable, false);
  assert.equal(two.count, 2);
});
