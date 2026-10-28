import test from 'node:test';
import assert from 'node:assert/strict';
import {findReusablePrivateArchive} from './private-archive-reuse.js';
const HASH = 'a'.repeat(64);
const KEY = 'users/account-1/extensions/yanzi-notes/archive-history/7-' + HASH + '.zip';
function envWith(keys, {missing = false, wrongSize = false, fail = false} = {}) {
  const calls = [];
  return {
    calls,
    DB: {prepare: sql => {
      calls.push(sql);
      return {bind: (...args) => {
        calls.push(args);
        return {all: async () => {
          if (fail) throw new Error('db_temporarily_unavailable');
          return {results: keys.map(archive_key => ({archive_key}))};
        }};
      }};
    }},
    PACKAGES: {head: async key => {
      calls.push(key);
      if (missing) return null;
      return {size: wrongSize ? 99 : 1024};
    }}
  };
}
test('reuses exact same-account extension payload without upload', async () => {
  const env = envWith([KEY]);
  assert.equal(await findReusablePrivateArchive(env,'account-1','yanzi-notes',HASH,1024),KEY);
  assert.deepEqual(env.calls[1],['account-1','yanzi-notes',HASH]);
});
test('rejects cross-account, cross-extension and malformed paths', async () => {
  const paths=[
    'users/account-2/extensions/yanzi-notes/archive-history/7-' + HASH + '.zip',
    'users/account-1/extensions/yanzi-capture/archive-history/7-' + HASH + '.zip',
    'users/account-1/extensions/yanzi-notes/other/7-' + HASH + '.zip'
  ];
  assert.equal(await findReusablePrivateArchive(envWith(paths),'account-1','yanzi-notes',HASH,1024),null);
});
test('missing object and size mismatch require ordinary upload', async () => {
  assert.equal(await findReusablePrivateArchive(envWith([KEY],{missing:true}),'account-1','yanzi-notes',HASH,1024),null);
  assert.equal(await findReusablePrivateArchive(envWith([KEY],{wrongSize:true}),'account-1','yanzi-notes',HASH,1024),null);
});
test('lookup failure falls back to the old safe upload behavior', async () => {
  assert.equal(await findReusablePrivateArchive(envWith([KEY],{fail:true}),'account-1','yanzi-notes',HASH,1024),null);
});
test('rejects invalid sha and empty packages', async () => {
  assert.equal(await findReusablePrivateArchive(envWith([KEY]),'account-1','yanzi-notes','wrong',1024),null);
  assert.equal(await findReusablePrivateArchive(envWith([KEY]),'account-1','yanzi-notes',HASH,0),null);
});

test('reusable SHA history lookup uses the compound archive index', async () => {
  const {DatabaseSync} = await import('node:sqlite');
  const {readFileSync} = await import('node:fs');
  const db = new DatabaseSync(':memory:');
  try {
    db.exec('CREATE TABLE user_extension_archive_history (user_id TEXT, extension_id TEXT, revision INTEGER, archive_sha256 TEXT, archive_key TEXT);');
    db.exec(readFileSync(new URL('../migrations/0028_private_archive_sha_lookup.sql', import.meta.url),'utf8'));
    const plan=db.prepare(
      'EXPLAIN QUERY PLAN SELECT archive_key FROM user_extension_archive_history ' +
      'WHERE user_id = ? AND extension_id = ? AND archive_sha256 = ? ' +
      'ORDER BY revision DESC LIMIT 5'
    ).all('user','notes',HASH);
    assert.ok(plan.some(row => String(row.detail).includes('idx_user_extension_archive_history_sha')),JSON.stringify(plan));
  } finally {db.close();}
});
