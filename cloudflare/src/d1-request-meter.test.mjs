import test from 'node:test';
import assert from 'node:assert/strict';
import { meterD1 } from './d1-request-meter.js';
const meta = { rows_read: 7, rows_written: 2 };
function fake() {
  const stmt = { bind() { return this; }, async all() { return {meta,results:[]}; }, async first() { return {id:1}; } };
  return { prepare() { return stmt; }, async batch(s) { assert.equal(s[0],stmt); return [{meta}]; } };
}
test('counts request-local D1 metadata without logging SQL', async () => {
  const a=meterD1(fake()); const b=meterD1(fake());
  await a.db.prepare('secret').bind('private').all();
  await a.db.batch([a.db.prepare('private')]);
  await a.db.prepare('other').first();
  assert.deepEqual(a.usage,{queries:3,rows_read:14,rows_written:4,unmetered_queries:1});
  assert.equal(b.usage.queries,0);
});
