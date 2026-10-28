import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { DatabaseSync } from 'node:sqlite';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../cloudflare/src/index.js', import.meta.url), 'utf8');
const slice = (start, end) => source.slice(source.indexOf(start), source.indexOf(end, source.indexOf(start)));
const functions = slice('async function getUserSyncRevision(', 'async function readUserSyncObjectHistory(');
const db = new DatabaseSync(':memory:');
db.exec(`CREATE TABLE user_sync_revisions(user_id TEXT PRIMARY KEY, revision INTEGER);
CREATE TABLE user_sync_objects(user_id TEXT, object_id TEXT, schema_version INTEGER, object_revision INTEGER,
updated_at TEXT, updated_by_device_id TEXT, updated_by_device_name TEXT, deleted INTEGER, payload_json TEXT);`);
db.exec("INSERT INTO user_sync_revisions VALUES ('test', 1)");
const insert = revision => db.prepare('INSERT INTO user_sync_objects VALUES (?, ?, 1, ?, ?, ?, ?, 0, ?)')
  .run('test', `object-${revision}`, revision, '2026-09-22', 'device', 'Device', '{}');
insert(1);
let afterRead;
const env = { DB: { prepare(sql) { return { bind(...args) { return {
  async first() { return db.prepare(sql).get(...args); },
  async all() { const results = db.prepare(sql).all(...args); if (afterRead) { const hook = afterRead; afterRead = null; hook(); } return { results }; }
}; } }; } } };
const read = runInNewContext(`${functions}\nreadUserSyncObjects`, {
  ensureUser: async () => {}, serializeUserSyncObject: row => ({ objectId: row.object_id, revision: row.object_revision })
});
afterRead = () => { insert(2); db.exec("UPDATE user_sync_revisions SET revision=2"); };
const first = await read(env, 'test', 0, 1);
assert.equal(first.currentRevision, 1, 'A write arriving after the query must not enter the response watermark');
assert.equal(first.cursorRevision, 1);
const second = await read(env, 'test', first.cursorRevision, 1);
assert.equal(second.objects[0].revision, 2, 'Next page must receive the interleaved device update');
afterRead = () => { insert(3); db.exec("UPDATE user_sync_revisions SET revision=3"); };
const empty = await read(env, 'test', 2, 1);
assert.equal(empty.objects.length, 0);
assert.equal(empty.cursorRevision, 2, 'An empty page must not jump over a concurrent update');
const next = await read(env, 'test', empty.cursorRevision, 1);
assert.equal(next.objects[0].revision, 3);
const paged = await read(env, 'test', 0, 1);
assert.equal(paged.hasMore, true);
assert.equal(paged.cursorRevision, 1);
db.close();
console.log('Sync cursor regression passed: interleaved writes, empty pages, pagination.');
