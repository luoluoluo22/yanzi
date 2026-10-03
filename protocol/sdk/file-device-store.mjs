import {mkdirSync, chmodSync} from 'node:fs';
import {resolve, join} from 'node:path';
import {DatabaseSync} from 'node:sqlite';

/** SQLite transactions survive process exit. Requires Node 22.13+. */
export class FileDeviceStore {
  constructor(directory) {
    this.directory = resolve(directory); mkdirSync(this.directory, {recursive:true, mode:0o700});
    const file = join(this.directory, 'device.sqlite');
    this.db = new DatabaseSync(file);
    if (process.platform !== 'win32') chmodSync(file, 0o600);
    this.db.exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS records(key TEXT PRIMARY KEY, value TEXT NOT NULL)');
  }
  async get(key) { const row = this.db.prepare('SELECT value FROM records WHERE key = ?').get(key); return row ? JSON.parse(row.value) : undefined; }
  async set(key, value) { this.db.prepare('INSERT INTO records VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run(key, JSON.stringify(value)); }
  async delete(key) { this.db.prepare('DELETE FROM records WHERE key = ?').run(key); }
  async list(prefix, limit = 1000) {
    return this.db.prepare('SELECT key, value FROM records WHERE substr(key, 1, ?) = ? ORDER BY key LIMIT ?').all(prefix.length, prefix, limit)
      .map(row => [row.key, JSON.parse(row.value)]);
  }
  async compareExchange(key, expectedState, value) {
    this.db.exec('BEGIN IMMEDIATE');
    try {
      const row = this.db.prepare('SELECT value FROM records WHERE key = ?').get(key);
      if ((row ? JSON.parse(row.value).state : null) !== expectedState) { this.db.exec('ROLLBACK'); return false; }
      this.db.prepare('INSERT INTO records VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run(key, JSON.stringify(value));
      this.db.exec('COMMIT'); return true;
    } catch (error) { this.db.exec('ROLLBACK'); throw error; }
  }
  close() { this.db.close(); }
}
