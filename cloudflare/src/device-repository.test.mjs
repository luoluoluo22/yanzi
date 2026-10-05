import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { createDeviceRepository } from './device-repository.js';
import { HttpError } from './http-error.js';

function fixture() {
  const sqlite = new DatabaseSync(':memory:');
  sqlite.exec('PRAGMA foreign_keys=ON');
  for (const migration of ['0001_init.sql', '0008_mobile_devices.sql', '0019_account_chat_receipts.sql', '0022_device_message_lifecycle.sql', '0025_d1_hotpath_indexes.sql'])
    sqlite.exec(readFileSync(new URL('../migrations/' + migration, import.meta.url), 'utf8'));
  const DB = {
    prepare(sql) {
      let parameters = [];
      const statement = sqlite.prepare(sql);
      return {
        bind(...values) { parameters = values; return this; },
        first() { return statement.get(...parameters) ?? null; },
        all() { return { results: statement.all(...parameters) }; },
        run() { return { meta: { changes: Number(statement.run(...parameters).changes) } }; }
      };
    }
  };
  const repository = createDeviceRepository({
    DEVICE_MESSAGE_PROTOCOL: { version: 1 },
    DEVICE_ONLINE_WINDOW_MS: 120000,
    HttpError,
    acceptsAccountChat: ({platform, capabilities}) => platform === 'desktop' || capabilities?.receiveAccountChat === true,
    ensureUser: async () => {},
    isoNow: () => '2026-10-05T00:00:00.000Z'
  });
  sqlite.prepare("INSERT INTO users(user_id,created_at,updated_at) VALUES('u','now','now')").run();
  sqlite.prepare("INSERT INTO user_devices(device_id,user_id,platform,display_name,capabilities_json,last_seen_at,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?)")
    .run('desktop-a','u','desktop','Desktop A','{"receiveAccountChat":true}','now','now','now');
  return { sqlite, env: { DB }, repository };
}

function insertMessage(sqlite, id, target, platform = null, accountChat = false) {
  sqlite.prepare("INSERT INTO device_messages(message_id,user_id,source_device_id,target_device_id,target_platform,kind,title,body_text,payload_json,status,created_at,expires_at) VALUES(?,?,?,?,?,'text','t','b',?,'pending','2026-10-05T00:00:00.000Z','2026-11-05T00:00:00.000Z')")
    .run(id, 'u', 'source', target, platform, JSON.stringify(accountChat ? {accountChat:true} : {}));
}

test('cursor page advances to account high-water even when other devices own the skipped messages', async () => {
  const f = fixture();
  try {
    insertMessage(f.sqlite, 'msg_other_0000000000000000000001', 'desktop-b');
    insertMessage(f.sqlite, 'msg_ours_00000000000000000000001', 'desktop-a');

    const first = await f.repository.getPendingDeviceMessagePage(f.env, 'u', 'desktop-a', 'desktop', 20, {receiveAccountChat:true}, 0, true);
    assert.equal(first.items.length, 1);
    assert.equal(first.items[0].messageId, 'msg_ours_00000000000000000000001');
    assert.equal(first.nextCursor, 2);
    assert.equal(first.highWater, 2);

    f.sqlite.prepare("UPDATE device_messages SET status='acked' WHERE message_id=?").run('msg_ours_00000000000000000000001');
    insertMessage(f.sqlite, 'msg_other_0000000000000000000002', 'desktop-b');

    const empty = await f.repository.getPendingDeviceMessagePage(f.env, 'u', 'desktop-a', 'desktop', 20, {receiveAccountChat:true}, 2, true);
    assert.deepEqual(empty.items, []);
    assert.equal(empty.nextCursor, 3);
    assert.equal(empty.highWater, 3);
    assert.equal(empty.hasMore, false);

    const stable = await f.repository.getPendingDeviceMessagePage(f.env, 'u', 'desktop-a', 'desktop', 20, {receiveAccountChat:true}, empty.nextCursor, true);
    assert.deepEqual(stable.items, []);
    assert.equal(stable.nextCursor, 3);
  } finally { f.sqlite.close(); }
});

test('legacy inbox still returns current pending messages without requiring a cursor', async () => {
  const f = fixture();
  try {
    insertMessage(f.sqlite, 'msg_legacy_000000000000000000001', 'desktop-a');
    insertMessage(f.sqlite, 'msg_other_0000000000000000000003', 'desktop-b');
    const page = await f.repository.getPendingDeviceMessagePage(f.env, 'u', 'desktop-a', 'desktop', 20, {receiveAccountChat:true}, 0, false);
    assert.equal(page.items.length, 1);
    assert.equal(page.items[0].messageId, 'msg_legacy_000000000000000000001');
    assert.equal(page.nextCursor, page.highWater);
  } finally { f.sqlite.close(); }
});

test('cursor beyond current database high-water resets safely after database recreation', async () => {
  const f = fixture();
  try {
    insertMessage(f.sqlite, 'msg_reset_0000000000000000000001', 'desktop-a');
    const page = await f.repository.getPendingDeviceMessagePage(f.env, 'u', 'desktop-a', 'desktop', 20, {receiveAccountChat:true}, 999, true);
    assert.equal(page.cursorReset, true);
    assert.equal(page.items.length, 1);
    assert.equal(page.nextCursor, 1);
  } finally { f.sqlite.close(); }
});
