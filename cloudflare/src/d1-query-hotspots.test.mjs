import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';

const migration = fs.readFileSync(new URL('../migrations/0026_d1_query_hotspots.sql', import.meta.url), 'utf8');

test('extension install counter uses a covering index', () => {
  const db = new DatabaseSync(':memory:');
  try {
    db.exec("CREATE TABLE user_extensions (user_id TEXT, extension_id TEXT, enabled INTEGER); CREATE TABLE device_messages (user_id TEXT, target_device_id TEXT, payload_json TEXT, expires_at TEXT, message_id TEXT);");
    db.exec(migration);
    const explain = db.prepare("EXPLAIN QUERY PLAN SELECT count(*) FROM user_extensions WHERE extension_id=? AND enabled=1").all('my-extension');
    assert.ok(explain.some(row => String(row.detail).includes('COVERING INDEX idx_user_extensions_extension_enabled')), JSON.stringify(explain));
    db.prepare("INSERT INTO user_extensions VALUES (?, ?, ?)").run('a','my-extension',1);
    db.prepare("INSERT INTO user_extensions VALUES (?, ?, ?)").run('b','my-extension',0);
    db.prepare("INSERT INTO user_extensions VALUES (?, ?, ?)").run('c','my-extension',1);
    assert.equal(db.prepare("SELECT count(*) AS n FROM user_extensions WHERE extension_id=? AND enabled=1").get('my-extension').n,2);
  } finally { db.close(); }
});

test('legacy account-chat inbox can filter expired history with a partial index', () => {
  const db = new DatabaseSync(':memory:');
  try {
    db.exec("CREATE TABLE user_extensions (user_id TEXT, extension_id TEXT, enabled INTEGER); CREATE TABLE device_messages (user_id TEXT, target_device_id TEXT, payload_json TEXT, expires_at TEXT, message_id TEXT, source_device_id TEXT);");
    db.exec(migration);
    const explain = db.prepare(`EXPLAIN QUERY PLAN
      SELECT message_id FROM device_messages m
      WHERE m.user_id = ? AND m.target_device_id IS NULL
        AND json_extract(m.payload_json, '$.accountChat') = 1
        AND m.source_device_id <> ?
        AND (m.expires_at IS NULL OR m.expires_at > ?)`).all('account','android-1','2026-10-08T00:00:00Z');
    assert.ok(explain.some(row => String(row.detail).includes('idx_device_messages_account_chat_unreceived_expiry')), JSON.stringify(explain));
    db.prepare('INSERT INTO device_messages VALUES (?,?,?,?,?,?)').run('account',null,'{"accountChat":1}','2026-09-01T00:00:00Z','expired','desktop-1');
    db.prepare('INSERT INTO device_messages VALUES (?,?,?,?,?,?)').run('account',null,'{"accountChat":1}','2026-11-01T00:00:00Z','active','desktop-1');
    const results = db.prepare(`SELECT message_id FROM device_messages m
      WHERE m.user_id = ? AND m.target_device_id IS NULL
        AND json_extract(m.payload_json, '$.accountChat') = 1
        AND m.source_device_id <> ? AND (m.expires_at IS NULL OR m.expires_at > ?)`).all('account','android-1','2026-10-08T00:00:00Z');
    assert.deepEqual(results.map(x=>x.message_id),['active']);
  } finally { db.close(); }
});
