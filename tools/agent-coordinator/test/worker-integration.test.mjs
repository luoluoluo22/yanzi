import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createCoordinator } from '../server.mjs';
import { cloudWorkerClient } from '../client.mjs';
import { runOnce } from '../../chatgpt-bridge/delivery-worker.mjs';

const ADMIN = 'e'.repeat(64);
const git = (cwd, ...args) => execFileSync('git', ['-C', cwd, ...args],
  { encoding: 'utf8', timeout: 10000, windowsHide: true }).trim();
const auth = token => ({ Authorization: 'Bearer ' + token,
  'X-Yanzi-Agent-Request': '1', 'Content-Type': 'application/json' });

test('cloud authority coordinates real Git commit, separate device QA and exact SHA', async t => {
  const root = mkdtempSync(join(tmpdir(), 'yanzi-cloud-worker-git-'));
  const main = join(root, 'source'), worktree = join(root, 'isolated');
  mkdirSync(main);
  git(main, 'init', '-q');
  git(main, 'config', 'user.name', 'Yanzi Test');
  git(main, 'config', 'user.email', 'test@local.invalid');
  writeFileSync(join(main, 'README.md'), '# fixture\n');
  git(main, 'add', 'README.md'); git(main, 'commit', '-qm', 'initial');
  git(main, 'worktree', 'add', '-qb', 'feat/isolated-qa', worktree);

  const server = await createCoordinator({
    databasePath: join(root, 'database.sqlite'), adminToken: ADMIN });
  t.after(async () => {
    await server.close();
    rmSync(root, { recursive: true, force: true });
  });
  async function adminPost(path, data) {
    const response = await fetch(server.url + path, { method: 'POST',
      headers: auth(ADMIN), body: JSON.stringify(data) });
    assert.ok(response.ok, path + ':' + response.status);
    return response.json();
  }
  const dev = await adminPost('/v1/admin/devices',
    { deviceId: 'desktop-01', grantedRoles: ['development'] });
  const qa = await adminPost('/v1/admin/devices',
    { deviceId: 'company-qa', grantedRoles: ['reviewer'] });
  await adminPost('/v1/admin/runs', {
    runId: 'cross-device',
    tasks: [{ id: 'change', dependsOn: [], prompt: 'Add specific text to README' }],
  });
  const devClient = cloudWorkerClient(server.url, dev.token);
  const qaClient = cloudWorkerClient(server.url, qa.token);

  const developed = await runOnce({
    client: devClient, runId: 'cross-device', workerId: dev.deviceId,
    workspace: worktree, role: 'development', backend: 'agy',
    invoke: async () => {
      writeFileSync(join(worktree, 'README.md'), '# fixture\nYANZI_CLOUD_SHARED\n');
      git(worktree, 'add', 'README.md');
      git(worktree, 'commit', '-qm', 'add requested marker');
      return { exitCode: 0, stdout: 'committed', stderr: '',
        timedOut: false, overflow: false, backend: 'agy' };
    },
  });
  assert.equal(developed.state, 'review');
  assert.equal(developed.commitSha, git(worktree, 'rev-parse', 'HEAD'));

  const reviewed = await runOnce({
    client: qaClient, runId: 'cross-device', workerId: qa.deviceId,
    workspace: worktree, role: 'reviewer', backend: 'agy',
    invoke: async () => ({
      exitCode: 0, stderr: '', timedOut: false, overflow: false, backend: 'agy',
      stdout: JSON.stringify({ event: 'result', result: {
        response: JSON.stringify({ status: 'pass', evidence: 'QA independently read README',
          findings: [] }),
      } }),
    }),
    e2eVerifier: async ({ workspace, commitSha }) => {
      const passed = readFileSync(join(workspace, 'README.md'), 'utf8')
        .includes('YANZI_CLOUD_SHARED') && commitSha === developed.commitSha;
      return { status: passed ? 'pass' : 'fail',
        evidence: 'Independent file assertion for exact commit',
        findings: passed ? [] : ['marker_missing'] };
    },
  });
  assert.equal(reviewed.state, 'ready_for_integration');
  const response = await fetch(server.url + '/v1/admin/runs/cross-device',
    { headers: auth(ADMIN) });
  const state = await response.json();
  assert.equal(state.tasks.change.candidate.developer, 'desktop-01');
  assert.equal(state.tasks.change.checks.e2e.status, 'pass');
  assert.equal(state.tasks.change.phase, 'ready_for_integration');
  assert.equal((await qaClient.send('cross-device','claim',
    { workerId: 'company-qa', role: 'reviewer' })).lease, null);
});
