import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';

// Execute the exact SQL used by the production Cloudflare routes in a
// real SQLite engine, rather than reimplementing the rules in the tests.
const worker = readFileSync(new URL('./index.js', import.meta.url), 'utf8');
const upsertSql = worker.match(/const upsertResult = await env\.DB\.prepare\(\s*`([\s\S]*?)`\s*\)/)?.[1];
const deleteSql = worker.match(/const deletedAt = isoNow\(\);\s*const result = await env\.DB\.prepare\(\s*`([\s\S]*?)`\s*\)/)?.[1];
assert.ok(upsertSql, 'Account extension upsert statement is present');
assert.ok(deleteSql, 'Account extension delete statement is present');

function setup() {
  const db = new DatabaseSync(':memory:');
  db.exec(`
    PRAGMA foreign_keys = ON;
    CREATE TABLE extensions (extension_id text primary key);
    CREATE TABLE user_extensions (
      user_id text not null,
      extension_id text not null REFERENCES extensions(extension_id),
      installed_version text not null,
      enabled integer not null default 1,
      settings_json text not null default '{}',
      updated_at text not null,
      primary key (user_id, extension_id)
    );`);
  db.prepare('insert into extensions(extension_id) values (?)').run('demo');
  const upsert = db.prepare(upsertSql);
  const remove = db.prepare(deleteSql);
  const save = (account, restore = false, version = '1') =>
    upsert.run(account, 'demo', version, 1,
      JSON.stringify({ displayName: 'Example' }), new Date().toISOString(), restore ? 1 : 0);
  const erase = account =>
    remove.run(account, JSON.stringify({ _yanziDeleted: true, deletedAt: new Date().toISOString() }),
      new Date().toISOString(), 'demo');
  const row = account => db.prepare('select * from user_extensions where user_id = ? and extension_id = ?').get(account, 'demo');
  return { db, save, erase, row };
}

test('deletion persists across stale automatic uploads and is isolated per account', () => {
  const f = setup();
  f.save('A');
  f.save('B');
  assert.equal(f.erase('A').changes, 1);
  assert.equal(f.row('A').enabled, 0);
  assert.equal(JSON.parse(f.row('A').settings_json)._yanziDeleted, true);
  assert.equal(f.save('A', false, '2').changes, 0);
  assert.equal(f.row('A').enabled, 0);
  assert.equal(f.row('A').installed_version, '1');
  assert.equal(f.row('B').enabled, 1);
  f.db.close();
});

test('explicit restore re-enables the extension and clears deletion marker', () => {
  const f = setup();
  f.save('A');
  f.erase('A');
  assert.equal(f.save('A', true, '3').changes, 1);
  assert.equal(f.row('A').enabled, 1);
  assert.equal(JSON.parse(f.row('A').settings_json)._yanziDeleted, undefined);
  f.db.close();
});

test('delete of absent account association creates a tombstone, blocking a later offline upload', () => {
  const f = setup();
  assert.equal(f.row('A'), undefined);
  assert.equal(f.erase('A').changes, 1);
  assert.equal(f.row('A').enabled, 0);
  assert.equal(f.save('A').changes, 0);
  assert.equal(JSON.parse(f.row('A').settings_json)._yanziDeleted, true);
  f.db.close();
});

test('ordinary disabled extension does not become a deletion tombstone', () => {
  const f = setup();
  f.save('A');
  f.db.prepare('update user_extensions set enabled = 0 where user_id = ?').run('A');
  assert.equal(f.save('A').changes, 1);
  assert.equal(f.row('A').enabled, 1);
  f.db.close();
});
