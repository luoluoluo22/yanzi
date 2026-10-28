import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {DatabaseSync} from 'node:sqlite';

test('server pending platform branch uses targeted index', () => {
  const source = readFileSync(new URL('./device-repository.js', import.meta.url), 'utf8');
  assert.match(source, /FROM device_messages m INDEXED BY idx_device_messages_user_target\s+JOIN device_message_sequence q ON q\.user_id = m\.user_id AND q\.message_id = m\.message_id\s+WHERE m\.user_id = \? AND m\.status = 'pending' AND m\.target_device_id IS NULL/);
  const db = new DatabaseSync(':memory:');
  try {
    db.exec(`CREATE TABLE device_messages(message_id TEXT PRIMARY KEY,user_id TEXT,status TEXT,target_device_id TEXT,target_platform TEXT,payload_json TEXT,expires_at TEXT);
      CREATE TABLE device_message_sequence(sequence INTEGER PRIMARY KEY,user_id TEXT,message_id TEXT UNIQUE);
      CREATE INDEX idx_device_messages_user_target ON device_messages(user_id,status,target_device_id,target_platform);
      CREATE INDEX device_message_sequence_user ON device_message_sequence(user_id,sequence);`);
    const plan = db.prepare(`EXPLAIN QUERY PLAN SELECT m.*, q.sequence FROM device_messages m INDEXED BY idx_device_messages_user_target
      JOIN device_message_sequence q ON q.user_id = m.user_id AND q.message_id = m.message_id
      WHERE m.user_id = ? AND m.status = 'pending' AND m.target_device_id IS NULL
      AND m.target_platform = ? AND q.sequence <= ?
      AND coalesce(json_extract(m.payload_json, '$.accountChat'), 0) <> 1
      AND (m.expires_at IS NULL OR m.expires_at > ?)`)
      .all('u', 'server', 999, '2026-10-18');
    assert.ok(plan.some(row => String(row.detail).includes('idx_device_messages_user_target') && String(row.detail).includes('target_platform=?')), JSON.stringify(plan));
  } finally { db.close(); }
});
