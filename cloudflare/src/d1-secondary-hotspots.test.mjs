import test from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import {createSyncApi} from './sync-api.js';
import {createDeviceRepository} from './device-repository.js';

test('capabilities schema metadata is cached across fresh D1 binding proxies', async () => {
  let queries = 0;
  const api = createSyncApi({
    requireAuth: async () => ({userId:'account-1'}),
    json: (body, status = 200) => Response.json(body, {status})
  });
  const fakeDb = () => ({
    prepare(sql) {
      ++queries;
      return {first: async () => ({name: sql.includes('history') ? 'user_sync_object_history' : 'user_sync_objects'})};
    }
  });
  const request = new Request('https://example.test/v1/sync/capabilities');
  const first = await api.handleSyncApi(request, {DB: fakeDb(), SYNC_OBJECTS_AUTHORITATIVE:'true'});
  const second = await api.handleSyncApi(request, {DB: fakeDb(), SYNC_OBJECTS_AUTHORITATIVE:'true'});
  assert.equal(first.status, 200);
  assert.equal(second.status, 200);
  assert.equal((await second.json()).objectsAuthoritative, true);
  assert.equal(queries, 2, 'schema is stable across different env.DB objects in the same isolate');
});

function mockDeviceRepository(acceptChat, highWater = 1064) {
  const statements = [];
  const repo = createDeviceRepository({
    acceptsAccountChat: () => acceptChat,
    isoNow: () => '2026-10-13T02:00:00Z',
    DEVICE_MESSAGE_PROTOCOL: {version:1}
  });
  const env = {DB:{prepare(sql) {
    statements.push(sql);
    return {
      bind() {
        return {
          first: async () => ({sequence:highWater}),
          all: async () => ({results:[]})
        };
      }
    };
  }}};
  return {repo, env, statements};
}

test('caught-up cursor does not execute a message-history join', async () => {
  const {repo,env,statements} = mockDeviceRepository(false);
  const result = await repo.getPendingDeviceMessagePage(env,'account-1','server-1','server',20,{},1064,true);
  assert.equal(result.nextCursor, 1064);
  assert.equal(result.items.length,0);
  assert.equal(result.hasMore,false);
  assert.equal(statements.length,1, 'only read the high-water index');
});

test('non-chat bootstrap uses targeted pending-message branches', async () => {
  const {repo,env,statements} = mockDeviceRepository(false);
  const result = await repo.getPendingDeviceMessagePage(env,'account-1','server-1','server',20,{},0,true);
  assert.equal(result.nextCursor,1064);
  assert.equal(statements.length,2);
  assert.match(statements[1],/target_device_id = \?/);
  assert.match(statements[1],/target_platform = \?/);
  assert.doesNotMatch(statements[1],/account_chat_receipts/);
  assert.doesNotMatch(statements[1],/m\.source_device_id/);
});

test('chat-capable bootstrap retains receipt recovery semantics', async () => {
  const {repo,env,statements} = mockDeviceRepository(true);
  await repo.getPendingDeviceMessagePage(env,'account-1','desktop-1','desktop',20,{},0,true);
  assert.match(statements[1],/account_chat_receipts/);
});

test('device listing index supports account-wide last-update ordering', () => {
  const db = new DatabaseSync(':memory:');
  try {
    db.exec('CREATE TABLE user_devices (user_id TEXT, updated_at TEXT, capabilities_json TEXT);');
    db.exec(readFileSync(new URL('../migrations/0027_d1_secondary_read_indexes.sql',import.meta.url),'utf8'));
    const plan = db.prepare(
      "EXPLAIN QUERY PLAN SELECT * FROM user_devices WHERE user_id=? AND coalesce(json_extract(capabilities_json, '$.disabled'),0) <> 1 ORDER BY updated_at DESC"
    ).all('account-1');
    assert.ok(plan.some(x => String(x.detail).includes('idx_user_devices_account_order')), JSON.stringify(plan));
    assert.ok(plan.every(x => !String(x.detail).includes('TEMP B-TREE')), JSON.stringify(plan));
  } finally {db.close();}
});
