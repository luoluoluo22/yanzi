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

test('quoted comment markers cannot leak SQL string values', () => {
  const sql = "SELECT 'api-key--private-suffix' AS token, 'secret/*in-string*/tail' AS payload, 'a''b' AS x -- real comment\nFROM t";
  const redacted = sanitizeSql(sql);
  for (const secret of ['api-key', 'private-suffix', 'secret', 'in-string', 'tail', 'real comment']) {
    assert.ok(!redacted.includes(secret), redacted);
  }
  assert.match(redacted, /FROM t/);
});

test('redacts SQLite numeric forms and alternative quoted identifiers', () => {
  const sql = 'SELECT 1e123, 1.5E-20, .2e+10, 0xABC, 5., 42, `secret-name`, [private id], "hidden-id" FROM t';
  const redacted = sanitizeSql(sql);
  for (const secret of ['1e123', '1.5E-20', '.2e+10', '0xABC', '42', 'secret-name', 'private id', 'hidden-id']) {
    assert.ok(!redacted.includes(secret), redacted);
  }
  assert.match(redacted, /^SELECT \?, \?, \?, \?, \?, \?, \?, \?, \? FROM t$/);
});

test('unclosed quotes and comments fail closed', () => {
  assert.equal(sanitizeSql("SELECT 'secret--not-closed"), 'SELECT ?');
  assert.equal(sanitizeSql('SELECT /* secret--not-closed'), 'SELECT');
});

test('fingerprints are based on sanitized SQL shape, not guessable literals', () => {
  const a = summarizeInsights([{ query: "SELECT * FROM users WHERE pin = '1234'" }])[0];
  const b = summarizeInsights([{ query: "SELECT * FROM users WHERE pin = '9999'" }])[0];
  assert.equal(a.pattern, b.pattern);
  assert.equal(a.fingerprint, b.fingerprint);
  assert.ok(!a.pattern.includes('1234'));
});
