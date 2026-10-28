// Explicit manual E2E exercise; never runs during npm test or Bridge startup.
// Creates a disposable local git repository / linked Worktree, then runs real
// Antigravity development and review via the same HTTP ledger used by workers.
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createBridge } from './server.mjs';
import { bridgeClient, runOnce } from './delivery-worker.mjs';

if (!process.argv.includes('--confirm-real-cli')) {
  console.error('Manual-only: pass --confirm-real-cli. Uses your installed Antigravity quota.');
  process.exitCode = 2;
} else {
  const root = mkdtempSync(join(tmpdir(), 'yanzi-live-agent-e2e-'));
  const repo = join(root, 'repo'), workspace = join(root, 'isolated-worktree');
  const git = (...a) => execFileSync('git', ['-C', repo, ...a],
    { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  let bridge;
  try {
    mkdirSync(repo);
    git('init', '-q');
    git('config', 'user.name', 'Yanzi Smoke');
    git('config', 'user.email', 'yanzi-smoke@example.invalid');
    writeFileSync(join(repo, 'README.md'), '# Isolated Fixture\n\nInitial version.\n');
    git('add', 'README.md'); git('commit', '-qm', 'initial fixture');
    git('worktree', 'add', '-qb', 'feat/live-agent-smoke', workspace);
    bridge = await createBridge({ directory: join(root, 'bridge'), port: 0, enableDeliveryLedger: true });
    const client = bridgeClient(bridge.origin, bridge.token);
    const prompt = 'In README.md append a new paragraph with the exact marker YANZI_REAL_AGENT_FILE_OK. ' +
      'Do not change anything else. Git add README.md and git commit -m "test: add safe marker". ' +
      'You are inside a disposable test git worktree. Do not push or contact external services.';
    const start = await fetch(bridge.origin + '/api/delivery-runs', { method: 'POST',
      headers: { Authorization: 'Bearer ' + bridge.token, 'X-Bridge-Request': '1',
        'Content-Type': 'application/json' },
      body: JSON.stringify({ runId: 'live-agent-check', tasks: [{
        id: 'readme-marker', dependsOn: [], prompt }] }) });
    if (!start.ok) throw new Error('create_live_run_failed_' + start.status);
    console.log('STARTED_REAL_CLI_IN_DISPOSABLE_WORKTREE');
    const dev = await runOnce({ client, runId: 'live-agent-check',
      workerId: 'agy-development', workspace, role: 'development', backend: 'agy',
      timeoutMs: 90000 });
    console.log('DEV_RESULT=' + JSON.stringify(dev));
    if (dev.state === 'review') {
      const qa = await runOnce({ client, runId: 'live-agent-check',
        workerId: 'agy-independent-qa', workspace, role: 'reviewer', backend: 'agy',
        timeoutMs: 90000, e2eVerifier: async ({ workspace: where }) => {
          const passed = readFileSync(join(where, 'README.md'), 'utf8')
            .includes('YANZI_REAL_AGENT_FILE_OK');
          return { status: passed ? 'pass' : 'fail',
            evidence: 'Independent file-content assertion in isolated git worktree',
            findings: passed ? [] : ['marker_missing'] };
        } });
      console.log('QA_RESULT=' + JSON.stringify({
        state: qa.state, checks: qa.checks &&
          Object.fromEntries(Object.entries(qa.checks).map(([k,v]) => [k, v.status])),
      }));
    }
    console.log('FINAL_PHASE=' + (await fetch(bridge.origin + '/api/delivery-runs/live-agent-check',
      { headers: { Authorization: 'Bearer ' + bridge.token } }).then(x => x.json()))
      .tasks['readme-marker'].phase);
  } finally {
    await bridge?.close();
    rmSync(root, { recursive: true, force: true });
  }
}
