import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createBridge } from '../server.mjs';
import { bridgeClient, runOnce } from '../delivery-worker.mjs';
import { createAgentArgs, validateIsolatedWorkspace, sanitizedCliEnvironment } from '../agent-cli-runner.mjs';

const runGit = (directory, ...args) => execFileSync('git', ['-C', directory, ...args],
  { encoding: 'utf8', timeout: 10000, windowsHide: true }).trim();
async function setup(t, tasks = [{ id: 'first', dependsOn: [],
  prompt: 'Make a minimal documented improvement and commit it.' }]) {
  const dir = mkdtempSync(join(tmpdir(), 'yanzi-real-git-worker-'));
  const repo = join(dir, 'repo'), worktree = join(dir, 'worktree');
  mkdirSync(repo);
  runGit(repo, 'init', '-q');
  runGit(repo, 'config', 'user.email', 'sandbox@example.invalid');
  runGit(repo, 'config', 'user.name', 'Sandbox Test');
  writeFileSync(join(repo, 'README.md'), 'initial\n');
  runGit(repo, 'add', '.');
  runGit(repo, 'commit', '-qm', 'init');
  runGit(repo, 'worktree', 'add', '-qb', 'feat/worker-validation', worktree);
  const bridge = await createBridge({ directory: join(dir, 'bridge'), port: 0,
    enableDeliveryLedger: true });
  t.after(async () => { await bridge.close(); rmSync(dir, { recursive: true, force: true }); });
  const client = bridgeClient(bridge.origin, bridge.token);
  const response = await fetch(bridge.origin + '/api/delivery-runs', {
    method: 'POST', headers: { Authorization: 'Bearer ' + bridge.token,
      'X-Bridge-Request': '1', 'Content-Type': 'application/json' },
    body: JSON.stringify({ runId: 'sample', tasks }),
  });
  assert.equal(response.status, 201);
  return { dir, repo, worktree, client, bridge };
}
const goodCli = (stdout = '{"status":"pass","evidence":"Read target README and compared requested text","findings":[]}') =>
  ({ exitCode: 0, timedOut: false, overflow: false, stdout, stderr: '', backend: 'agy' });

test('real Git worktree + HTTP ledger + simulated CLI output completes dev, independent QA and merge-ready state', async t => {
  const { worktree, client, bridge } = await setup(t);
  assert.ok(validateIsolatedWorkspace(worktree).branch.startsWith('feat/'));
  const before = validateIsolatedWorkspace(worktree);
  const development = await runOnce({ client, runId: 'sample', workerId: 'developer-one',
    workspace: worktree, backend: 'agy', role: 'development', invoke: async () => {
      writeFileSync(join(worktree, 'README.md'), 'initial\nhello independent qa\n');
      runGit(worktree, 'add', 'README.md');
      runGit(worktree, 'commit', '-qm', 'add an observable improvement');
      return goodCli('written and committed');
    } });
  assert.equal(development.state, 'review');
  assert.notEqual(development.commitSha, before.head);
  assert.equal(runGit(worktree, 'rev-parse', 'HEAD'), development.commitSha);
  const samePerson = await runOnce({ client, runId: 'sample', workerId: 'developer-one',
    workspace: worktree, role: 'reviewer', invoke: async () => {
      throw new Error('reviewer should not execute');
    } });
  assert.equal(samePerson.state, 'no_work');
  const review = await runOnce({ client, runId: 'sample', workerId: 'independent-qa',
    workspace: worktree, role: 'reviewer', invoke: async () => goodCli(JSON.stringify({ event: 'result', result: { response: JSON.stringify({ status: 'pass', evidence: 'Independent reviewer compared README with task', findings: [] }) } })),
    e2eVerifier: async ({ workspace, commitSha }) => {
      assert.equal(commitSha, runGit(workspace, 'rev-parse', 'HEAD'));
      const ok = readFileSync(join(workspace, 'README.md'), 'utf8').includes('hello independent qa');
      return { status: ok ? 'pass' : 'fail', evidence: 'Read README from checked-out commit',
        findings: ok ? [] : ['target_text_missing'] };
    } });
  assert.equal(review.state, 'ready_for_integration');
  assert.equal(review.checks.e2e.status, 'pass');
  const snapshot = await fetch(bridge.origin + '/api/delivery-runs/sample', {
    headers: { Authorization: 'Bearer ' + bridge.token },
  }).then(res => res.json());
  assert.equal(snapshot.tasks.first.candidate.commitSha, development.commitSha);
  assert.equal(snapshot.tasks.first.phase, 'ready_for_integration');
  assert.equal(snapshot.tasks.first.checks.spec.status, 'pass');
});

test('model success without a committed change is blocked, not reported as delivery', async t => {
  const { worktree, client } = await setup(t);
  const result = await runOnce({ client, runId: 'sample', workerId: 'developer-one',
    workspace: worktree, invoke: async () => goodCli('claimed complete but no commit') });
  assert.equal(result.state, 'blocked');
  assert.match(result.reason, /development_missing_commit/);
});

test('review without trusted E2E blocks, even if independent AI reports pass', async t => {
  const { worktree, client } = await setup(t);
  await runOnce({ client, runId: 'sample', workerId: 'developer-one', workspace: worktree,
    invoke: async () => {
      writeFileSync(join(worktree, 'README.md'), 'new committed contents\n');
      runGit(worktree, 'add', 'README.md'); runGit(worktree, 'commit', '-qm', 'change');
      return goodCli('completed');
    } });
  const result = await runOnce({ client, runId: 'sample', workerId: 'qa-one',
    role: 'reviewer', workspace: worktree, invoke: async () => goodCli() });
  assert.equal(result.state, 'blocked');
  assert.equal(result.checks.e2e.status, 'blocked');
});

test('agent invocation arguments remain non-shell and sandboxed', () => {
  const agy = createAgentArgs('agy', { role: 'development',
    prompt: 'fix one bug', workspace: 'C:\\tmp\\safe', timeoutMs: 90000 });
  assert.ok(agy.args.includes('--sandbox'));
  assert.ok(!agy.args.includes('--dangerously-skip-permissions'));
  assert.ok(agy.args.includes('accept-edits'));
  const codex = createAgentArgs('codex', { role: 'reviewer',
    prompt: 'review', workspace: 'C:\\tmp\\safe', timeoutMs: 90000 });
  assert.ok(codex.args.includes('read-only'));
  assert.ok(!codex.args.includes('--dangerously-bypass-approvals-and-sandbox'));
  assert.throws(() => bridgeClient('https://external.example', 'a'.repeat(64)), /local_authenticated_bridge_only/);
  const env = sanitizedCliEnvironment({ PATH: '/bin', LOCALAPPDATA: 'C:/profile',
    GH_TOKEN: 'private', CLOUDFLARE_API_TOKEN: 'private2', OPENAI_API_KEY: 'private3',
    NODE_OPTIONS: '--inspect' });
  assert.equal(env.PATH, '/bin');
  assert.equal(env.GH_TOKEN, undefined);
  assert.equal(env.CLOUDFLARE_API_TOKEN, undefined);
  assert.equal(env.OPENAI_API_KEY, undefined);
  assert.equal(env.NODE_OPTIONS, undefined);
});

test('renew and explicit block keep identity fencing and preserve manually recoverable task', async t => {
  const { client, bridge } = await setup(t);
  const { lease } = await client.send('sample', 'claim', {
    role: 'development', workerId: 'dev', leaseMs: 10000 });
  assert.equal(lease.prompt, 'Make a minimal documented improvement and commit it.');
  const renewed = await client.send('sample', 'renew', {
    taskId: lease.taskId, workerId: 'dev', leaseId: lease.id, leaseMs: 20000 });
  assert.equal(renewed.id, lease.id);
  await assert.rejects(() => client.send('sample', 'block', {
    taskId: lease.taskId, workerId: 'other', leaseId: lease.id, reason: 'bad' }), /lease_mismatch/);
  const result = await client.send('sample', 'block', {
    taskId: lease.taskId, workerId: 'dev', leaseId: lease.id, reason: 'worker unavailable' });
  assert.equal(result.phase, 'blocked');
  const current = await fetch(bridge.origin + '/api/delivery-runs/sample', {
    headers: { Authorization: 'Bearer ' + bridge.token },
  }).then(x => x.json());
  assert.equal(current.tasks.first.blockedReason, 'worker unavailable');
});
