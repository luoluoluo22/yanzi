import test from 'node:test';
import assert from 'node:assert/strict';
import { verifyMergedPR } from '../github-integration-verifier.mjs';
import { createBridge } from '../server.mjs';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
const candidate = 'a'.repeat(40), merged = 'b'.repeat(40);
const input = { reviewedCommitSha: candidate, mergeCommitSha: merged };
const config = { repository: 'sample/owner', prNumber: 42, requiredChecks: ['CI / Tests'] };
const response = (data) => ({ ok: true, json: async () => data });
const fake = async url => {
  if (url.endsWith('/pulls/42'))
    return response({ merged: true, state: 'closed', base: { ref: 'main' },
      head: { sha: candidate }, merge_commit_sha: merged });
  if (url.includes('/check-runs?'))
    return response({ check_runs: [{ name: 'CI / Tests', status: 'completed', conclusion: 'success' }] });
  return response({ statuses: [] });
};

test('independent GitHub readback checks PR merge identity and required CI', async () => {
  const receipt = await verifyMergedPR(input, { ...config, fetchImpl: fake });
  assert.equal(receipt.ciPassed, true);
  assert.match(receipt.proof, /PR#42/);
});
test('verifier rejects fabricated candidate, missing checks, and no required CI policy', async () => {
  await assert.rejects(() => verifyMergedPR({ ...input, mergeCommitSha: 'c'.repeat(40) },
    { ...config, fetchImpl: fake }), /github_pr_merge_identity_mismatch/);
  await assert.rejects(() => verifyMergedPR(input,
    { ...config, requiredChecks: ['Security Audit'], fetchImpl: fake }), /required_ci_not_successful/);
  await assert.rejects(() => verifyMergedPR(input,
    { ...config, requiredChecks: [], fetchImpl: fake }), /github_verifier_policy_invalid/);
});
test('delivery integration endpoint denies approval without independent verifier', async t => {
  const dir = mkdtempSync(join(tmpdir(), 'yanzi-integration-guard-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const bridge = await createBridge({ directory: dir, port: 0, enableDeliveryLedger: true });
  t.after(() => bridge.close());
  const response = await fetch(bridge.origin + '/api/delivery-runs/r1/integration', {
    method: 'POST', headers: { Authorization: 'Bearer ' + bridge.token,
      'X-Bridge-Request': '1', 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...input, ciPassed: true, proof: 'Agent says passed' }),
  });
  assert.equal(response.status, 403);
  assert.deepEqual(await response.json(), { error: 'trusted_integration_verifier_required' });
});
