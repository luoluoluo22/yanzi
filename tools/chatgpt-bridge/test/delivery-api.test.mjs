import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createBridge } from '../server.mjs';

const sha = c => c.repeat(40);
const checks = Object.fromEntries(['e2e', 'standards', 'spec'].map(name => [
  name, { status: 'pass', evidence: 'CI log for ' + name, findings: [] },
]));

test('opt-in Bridge delivery API uses existing auth, bounded leases and persistence', async t => {
  const directory = mkdtempSync(join(tmpdir(), 'yanzi-ledger-http-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const bridge = await createBridge({ directory, port: 0, enableDeliveryLedger: true, integrationVerifier: async () => ({ ciPassed: true, proof: 'Trusted test verifier confirmed CI and merged PR' }) });
  t.after(() => bridge.close());
  const path = '/api/delivery-runs';
  const request = async (suffix, method = 'GET', value, extraHeaders = {}) => {
    const response = await fetch(bridge.origin + suffix, {
      method,
      headers: { Authorization: 'Bearer ' + bridge.token, 'X-Bridge-Request': '1',
        'Content-Type': 'application/json', ...extraHeaders },
      body: value ? JSON.stringify(value) : undefined,
    });
    return { status: response.status, body: await response.json() };
  };
  assert.equal((await fetch(bridge.origin + path)).status, 401);
  assert.equal((await request(path, 'POST', { runId: 'r1', tasks: [{ id: 'a', dependsOn: [] }] },
    { 'X-Bridge-Request': '' })).status, 403);
  assert.equal((await request(path, 'POST', { runId: 'r1', tasks: [{ id: 'a', dependsOn: [] }] })).status, 201);
  let response = await request(path + '/r1/claim', 'POST', { role: 'development', workerId: 'developer' });
  assert.equal(response.status, 200);
  assert.equal(response.body.lease.taskId, 'a');
  const development = response.body.lease;
  response = await request(path + '/r1/development', 'POST', { taskId: 'a',
    leaseId: development.id, workerId: 'developer', commitSha: sha('a'), evidence: 'tested locally' });
  assert.equal(response.body.phase, 'review');
  assert.equal((await request(path + '/r1/claim', 'POST', {
    role: 'reviewer', workerId: 'developer',
  })).body.lease, null);
  response = await request(path + '/r1/claim', 'POST', { role: 'reviewer', workerId: 'qa' });
  const review = response.body.lease;
  response = await request(path + '/r1/review', 'POST', { taskId: 'a',
    workerId: 'qa', leaseId: review.id, reviewedCommitSha: sha('a'), checks });
  assert.equal(response.body.phase, 'ready_for_integration');
  const integration = (await request(path + '/r1/claim', 'POST',
    { role: 'integration', workerId: 'release' })).body.lease;
  response = await request(path + '/r1/integration', 'POST', { taskId: 'a',
    workerId: 'release', leaseId: integration.id, reviewedCommitSha: sha('a'),
    mergeCommitSha: sha('b'), ciPassed: true, proof: 'CI id 123 verified externally' });
  assert.equal(response.body.phase, 'completed');
  assert.equal((await request(path + '/r1')).body.tasks.a.phase, 'completed');
  const reopened = await createBridge({ directory: join(directory, 'other'),
    port: 0, enableDeliveryLedger: false });
  t.after(() => reopened.close());
  const unauthorisedRoute = await fetch(reopened.origin + path,
    { headers: { Authorization: 'Bearer ' + reopened.token } });
  assert.equal(unauthorisedRoute.status, 404);
});
