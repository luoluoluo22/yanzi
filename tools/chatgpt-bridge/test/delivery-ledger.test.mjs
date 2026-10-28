import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, existsSync, writeFileSync, unlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DeliveryLedger } from '../delivery-ledger.mjs';

function setup(t, options = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'yanzi-delivery-ledger-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const path = join(dir, 'ledger.json');
  return { ledger: new DeliveryLedger(path, options), path };
}
const sha = char => char.repeat(40);
const checks = status => Object.fromEntries(['e2e', 'standards', 'spec'].map(name => [name,
  { status, evidence: name + ' test log', findings: status === 'fail' ? [name + ' problem'] : [] }]));
const tasks = () => [
  { id: 'ui', dependsOn: [] },
  { id: 'api', dependsOn: [] },
  { id: 'qa', dependsOn: ['ui', 'api'] },
];
const develop = (ledger, worker, commit = sha('a'), runId = 'run-001') => {
  const lease = ledger.claim({ runId, role: 'development', workerId: worker });
  assert.ok(lease);
  ledger.completeDevelopment({ runId, taskId: lease.taskId, leaseId: lease.id,
    workerId: worker, commitSha: commit, evidence: 'build and unit test logs' });
  return lease.taskId;
};
const review = (ledger, taskId, status = 'pass', commit = sha('a'), runId = 'run-001') => {
  const lease = ledger.claim({ runId, role: 'reviewer', workerId: 'independent-qa' });
  assert.equal(lease?.taskId, taskId);
  return ledger.completeReview({ runId, taskId, leaseId: lease.id,
    workerId: 'independent-qa', reviewedCommitSha: commit, checks: checks(status) });
};
const integrate = (ledger, taskId, runId = 'run-001') => {
  const lease = ledger.claim({ runId, role: 'integration', workerId: 'release-agent' });
  assert.equal(lease?.taskId, taskId);
  return ledger.completeIntegration({ runId, taskId, leaseId: lease.id,
    workerId: 'release-agent', reviewedCommitSha: sha('a'),
    mergeCommitSha: sha('b'), ciPassed: true, proof: 'CI run 181 approved' });
};

test('DAG frontier allows independent development; dependent waits for both merges', t => {
  const { ledger } = setup(t);
  ledger.createRun({ runId: 'run-001', tasks: tasks() });
  const first = develop(ledger, 'dev-one');
  assert.equal(first, 'ui');
  const second = develop(ledger, 'dev-two');
  assert.equal(second, 'api');
  assert.equal(ledger.claim({ runId: 'run-001', role: 'development', workerId: 'dev-three' }), null);
  review(ledger, first);
  integrate(ledger, first);
  assert.equal(ledger.claim({ runId: 'run-001', role: 'development', workerId: 'dev-three' }), null);
  review(ledger, second);
  integrate(ledger, second);
  assert.equal(ledger.claim({ runId: 'run-001', role: 'development', workerId: 'dev-three' }).taskId, 'qa');
});

test('rejects circular or unknown dependencies and run id drift', t => {
  const { ledger } = setup(t);
  assert.throws(() => ledger.createRun({ runId: 'bad', tasks: [
    { id: 'a', dependsOn: ['b'] }, { id: 'b', dependsOn: ['a'] },
  ] }), /dependency_cycle/);
  assert.throws(() => ledger.createRun({ runId: 'bad', tasks: [
    { id: 'a', dependsOn: ['missing'] },
  ] }), /dependency_invalid/);
  ledger.createRun({ runId: 'run-001', tasks: tasks() });
  assert.equal(Object.keys(ledger.createRun({ runId: 'run-001', tasks: tasks() }).tasks).length, 3);
  assert.throws(() => ledger.createRun({ runId: 'run-001', tasks: [
    { id: 'changed', dependsOn: [] },
  ] }), /run_id_conflict/);
});

test('review must be independent, bound to exact submitted SHA and carry evidence', t => {
  const { ledger } = setup(t);
  ledger.createRun({ runId: 'run-001', tasks: [{ id: 'a', dependsOn: [] }] });
  develop(ledger, 'developer');
  assert.equal(ledger.claim({ runId: 'run-001', role: 'reviewer', workerId: 'developer' }), null);
  const lease = ledger.claim({ runId: 'run-001', role: 'reviewer', workerId: 'independent-qa' });
  assert.throws(() => ledger.completeReview({ runId: 'run-001', taskId: 'a',
    leaseId: lease.id, workerId: 'independent-qa', reviewedCommitSha: sha('c'),
    checks: checks('pass') }), /review_target_stale/);
  assert.throws(() => ledger.completeReview({ runId: 'run-001', taskId: 'a',
    leaseId: lease.id, workerId: 'independent-qa', reviewedCommitSha: sha('a'),
    checks: { e2e: { status: 'pass' } } }), /checks_invalid_/);
  ledger.completeReview({ runId: 'run-001', taskId: 'a', leaseId: lease.id,
    workerId: 'independent-qa', reviewedCommitSha: sha('a'), checks: checks('pass') });
  assert.equal(ledger.snapshot('run-001').tasks.a.phase, 'ready_for_integration');
});

test('failed review routes back to development and stops after configured budget', t => {
  const { ledger } = setup(t, { reviewRounds: 2 });
  ledger.createRun({ runId: 'run-001', tasks: [{ id: 'a', dependsOn: [] }] });
  develop(ledger, 'developer');
  assert.equal(review(ledger, 'a', 'fail').phase, 'queued');
  develop(ledger, 'developer');
  const result = review(ledger, 'a', 'fail');
  assert.equal(result.phase, 'blocked');
  assert.equal(result.blockedReason, 'review_budget_exhausted');
  assert.equal(ledger.claim({ runId: 'run-001', role: 'development', workerId: 'developer' }), null);
});

test('single integration seat serializes merges; CI proof and exact SHA required', t => {
  const { ledger } = setup(t);
  ledger.createRun({ runId: 'run-001', tasks: tasks().slice(0, 2) });
  develop(ledger, 'dev-one');
  develop(ledger, 'dev-two');
  review(ledger, 'ui');
  review(ledger, 'api');
  const first = ledger.claim({ runId: 'run-001', role: 'integration', workerId: 'release-agent' });
  assert.equal(first.taskId, 'ui');
  assert.equal(ledger.claim({ runId: 'run-001', role: 'integration', workerId: 'release-agent' }), null);
  assert.throws(() => ledger.completeIntegration({ runId: 'run-001', taskId: 'ui',
    leaseId: first.id, workerId: 'release-agent', reviewedCommitSha: sha('c'),
    mergeCommitSha: sha('b'), ciPassed: true, proof: 'CI green' }), /integration_gate_failed/);
  assert.throws(() => ledger.completeIntegration({ runId: 'run-001', taskId: 'ui',
    leaseId: first.id, workerId: 'release-agent', reviewedCommitSha: sha('a'),
    mergeCommitSha: sha('b'), ciPassed: false, proof: 'CI red' }), /merge_evidence_invalid/);
  integrateNoClaim(ledger, 'ui', first);
  assert.equal(ledger.claim({ runId: 'run-001', role: 'integration', workerId: 'release-agent' }).taskId, 'api');
});
function integrateNoClaim(ledger, taskId, lease) {
  return ledger.completeIntegration({ runId: 'run-001', taskId, leaseId: lease.id,
    workerId: 'release-agent', reviewedCommitSha: sha('a'),
    mergeCommitSha: sha('b'), ciPassed: true, proof: 'CI run 181 approved' });
}

test('expired external call blocks instead of silently duplicating work; explicit retry creates new generation', t => {
  let now = 1000;
  const { ledger } = setup(t, { clock: () => now });
  ledger.createRun({ runId: 'run-001', tasks: [{ id: 'a', dependsOn: [] }] });
  const claim = ledger.claim({ runId: 'run-001', role: 'development', workerId: 'agent', leaseMs: 1000 });
  now = 2500;
  assert.equal(ledger.claim({ runId: 'run-001', role: 'development', workerId: 'other' }), null);
  const blocked = ledger.snapshot('run-001').tasks.a;
  assert.equal(blocked.blockedReason, 'external_outcome_uncertain');
  assert.throws(() => ledger.completeDevelopment({ runId: 'run-001', taskId: 'a',
    leaseId: claim.id, workerId: 'agent', commitSha: sha('a'), evidence: 'log' }), /lease_mismatch/);
  const repaired = ledger.retryBlocked({ runId: 'run-001', taskId: 'a', reason: 'confirmed no commit was written' });
  assert.equal(repaired.generation, 2);
  assert.equal(ledger.claim({ runId: 'run-001', role: 'development', workerId: 'other' }).generation, 2);
});

test('state survives new process object and refuses concurrent writer lock', t => {
  const { ledger, path } = setup(t);
  ledger.createRun({ runId: 'run-001', tasks: [{ id: 'a', dependsOn: [] }] });
  const other = new DeliveryLedger(path);
  assert.equal(other.snapshot('run-001').tasks.a.phase, 'queued');
  writeFileSync(path + '.lock', 'simulated orphan');
  assert.throws(() => other.claim({ runId: 'run-001', role: 'development', workerId: 'agent' }), /ledger_writer_busy/);
  unlinkSync(path + '.lock');
  assert.ok(other.claim({ runId: 'run-001', role: 'development', workerId: 'agent' }));
  assert.equal(existsSync(path + '.lock'), false);
});

test('oversized evidence is rejected before persistence', t => {
  const { ledger } = setup(t);
  ledger.createRun({ runId: 'run-001', tasks: [{ id: 'a', dependsOn: [] }] });
  const lease = ledger.claim({ runId: 'run-001', role: 'development', workerId: 'dev' });
  assert.throws(() => ledger.completeDevelopment({ runId: 'run-001', taskId: 'a',
    leaseId: lease.id, workerId: 'dev', commitSha: sha('a'), evidence: 'x'.repeat(4097) }),
  /development_evidence_invalid/);
  assert.equal(ledger.snapshot('run-001').tasks.a.phase, 'developing');
});
