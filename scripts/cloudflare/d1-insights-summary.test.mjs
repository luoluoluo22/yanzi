import test from 'node:test';
import assert from 'node:assert/strict';
import { sanitizeSql, summarizeInsights } from './d1-insights-summary.mjs';

test('hides SQL literals while preserving query structure', () => {
  const sql = "SELECT * FROM device_messages WHERE payload_json = 'secret-token' AND user_id = 123 -- private\nLIMIT 5";
  const redacted = sanitizeSql(sql);
  assert.ok(!redacted.includes('secret-token'));
  assert.ok(!redacted.includes('private'));
  assert.ok(!redacted.includes('123'));
  assert.match(redacted, /device_messages/);
});
test('sorts by reads and emits only sanitized query shape', () => {
  const summary = summarizeInsights([
    { query: "SELECT * FROM users WHERE email='private@example.test'", totalRowsRead: 5, numberOfTimesRun: 1 },
    { query: "SELECT * FROM device_messages WHERE title='hidden'", totalRowsRead: 99, numberOfTimesRun: 10 },
  ]);
  assert.equal(summary[0].rowsRead, 99);
  assert.equal(summary[0].calls, 10);
  assert.ok(!summary[0].pattern.includes('hidden'));
  assert.ok(!summary[1].pattern.includes('private@example.test'));
});
