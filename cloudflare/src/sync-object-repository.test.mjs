import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { createSyncObjectRepository } from './sync-object-repository.js';
import { HttpError } from './http-error.js';

function fixture() {
  const sqlite = new DatabaseSync(':memory:');
  sqlite.exec('PRAGMA foreign_keys=ON');
  for (const migration of ['0001_init.sql', '0010_sync_objects.sql', '0011_sync_object_history.sql'])
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
    },
    batch(statements) {
      sqlite.exec('BEGIN');
      try { const results = statements.map(x => x.run()); sqlite.exec('COMMIT'); return results; }
      catch (error) { sqlite.exec('ROLLBACK'); throw error; }
    }
  };
  const repository = createSyncObjectRepository({ HttpError, isoNow: () => new Date().toISOString(),
    scrubAiSecretsFromValue: x => ({ ...x, apiKey: undefined }), textEncoder: new TextEncoder() });
  return { env: { DB }, repository, close: () => sqlite.close() };
}

test('migrated SQL repository preserves CAS, account isolation, tombstones and history', async () => {
  const f = fixture(); const r = f.repository;
  try {
    const first = await r.writeUserSyncObject(f.env, 'a', 'shared', { payload: { text: '中文\nline' }, expectedRevision: 0 });
    assert.equal(first.revision, 1);
    await assert.rejects(r.writeUserSyncObject(f.env, 'a', 'shared', { payload: {}, expectedRevision: 0 }), e => e.status === 409);
    assert.equal(await r.getUserSyncRevision(f.env, 'a'), 1);
    assert.equal(await r.readUserSyncObject(f.env, 'b', 'shared'), null);
    const deleted = await r.writeUserSyncObject(f.env, 'a', 'shared', { payload: {}, deleted: true, expectedRevision: 1 });
    assert.equal(deleted.deleted, true);
    const history = await r.readUserSyncObjectHistory(f.env, 'a', 'shared', 0, 1);
    assert.equal(history.hasMore, true);
    assert.equal(history.versions[0].operation, 'delete');
    const older = await r.readUserSyncObjectHistory(f.env, 'a', 'shared', history.nextBeforeRevision, 1);
    assert.equal(older.versions[0].payload.text, '中文\nline');
    assert.equal(older.hasMore, false);
    const other = await r.writeUserSyncObject(f.env, 'b', 'shared', { payload: { other: true }, expectedRevision: 0 });
    assert.equal(other.revision, 1);
    await assert.rejects(r.writeUserSyncObject(f.env, 'a', 'oversized', { payload: { value: 'x'.repeat(1024 * 1024) } }), e => e.status === 413);
  } finally { f.close(); }
});

test('more than 1000 objects are recovered through strictly advancing download pages', async () => {
  const f = fixture(); const r = f.repository;
  try {
    for (let i = 0; i < 1003; i++)
      await r.writeUserSyncObject(f.env, 'many', 'object.' + i, { payload: { i }, expectedRevision: 0 });
    const first = await r.readUserSyncObjects(f.env, 'many', 0, 1000);
    assert.equal(first.objects.length, 1000);
    assert.equal(first.hasMore, true);
    assert.equal(first.cursorRevision, 1000);
    const last = await r.readUserSyncObjects(f.env, 'many', first.cursorRevision, 500);
    assert.equal(last.objects.length, 3);
    assert.equal(last.hasMore, false);
    assert.equal(last.cursorRevision, 1003);
    assert.equal(new Set([...first.objects, ...last.objects].map(x => x.objectId)).size, 1003);
    const empty = await r.readUserSyncObjects(f.env, 'many', last.cursorRevision, 500);
    assert.equal(empty.objects.length, 0);
    assert.equal(empty.cursorRevision, 1003);
  } finally { f.close(); }
});
