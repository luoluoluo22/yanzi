// Explicit one-shot worker. No daemon, deployment or automatic merge.
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runAgentCli, compactCliEvidence, validateIsolatedWorkspace,
  verifyDevelopmentCommit, verifyReviewHead } from './agent-cli-runner.mjs';

export function bridgeClient(origin, token) {
  if (!/^http:\/\/127\.0\.0\.1:\d{2,5}$/.test(origin) || !/^[a-f0-9]{64}$/i.test(token))
    throw new Error('local_authenticated_bridge_only');
  return { async send(runId, action, input) {
    const response = await fetch(origin + '/api/delivery-runs/' + encodeURIComponent(runId) + '/' + action, {
      method: 'POST', headers: { Authorization: 'Bearer ' + token, 'X-Bridge-Request': '1',
        'Content-Type': 'application/json' }, body: JSON.stringify(input),
      signal: AbortSignal.timeout(15000),
    });
    const data = await response.json();
    if (!response.ok) throw new Error('bridge_' + response.status + ':' + String(data.error).slice(0, 90));
    return data;
  }};
}

function readSpec(raw) {
  const options = [raw];
  for (const line of raw.split(/\r?\n/)) {
    try {
      const obj = JSON.parse(line);
      for (const value of [obj.result?.response, obj.result, obj.text, obj.message?.content, obj.item?.text])
        if (typeof value === 'string') options.push(value);
    } catch {}
  }
  for (const option of options.reverse()) {
    try {
      // Some AGY output formats wrap the final JSON in Markdown fences.
      const fence = String.fromCharCode(96).repeat(3);
      let candidate = option.trim();
      if (candidate.startsWith(fence)) {
        candidate = candidate.slice(fence.length).replace(/^json\s*/i, '').trim();
        if (candidate.endsWith(fence)) candidate = candidate.slice(0, -fence.length).trim();
      }
      const value = JSON.parse(candidate);
      const spec = value.spec || value;
      if (['pass','fail','blocked'].includes(spec.status) &&
          typeof spec.evidence === 'string' && spec.evidence.trim() &&
          spec.evidence.length <= 4096 && Array.isArray(spec.findings) &&
          spec.findings.length <= 30 && spec.findings.every(x => typeof x === 'string' && x.length <= 256))
        return spec;
    } catch {}
  }
  return { status: 'blocked', evidence: 'Reviewer did not return verifiable JSON',
    findings: ['review_output_unverified'] };
}

function whitespaceCheck(workspace, sha) {
  const run = spawnSync('git', ['-C', workspace, 'show', '--format=', '--check', sha],
    { encoding: 'utf8', timeout: 10000, windowsHide: true, shell: false });
  return { status: run.status === 0 ? 'pass' : 'fail',
    evidence: 'git show --check ' + sha.slice(0, 12) + ' exit=' + run.status,
    findings: run.status === 0 ? [] : ['whitespace_or_conflict_check_failed'] };
}

export async function runOnce({ client, runId, workerId, workspace, role = 'development',
  backend = 'agy', timeoutMs = 180000, invoke = runAgentCli, e2eVerifier }) {
  if (!['development','reviewer'].includes(role)) throw new Error('role_invalid');
  if (!/^[a-zA-Z0-9_.:-]{1,100}$/.test(runId) || !/^[a-zA-Z0-9_.:-]{1,100}$/.test(workerId))
    throw new Error('worker_identity_invalid');
  const base = validateIsolatedWorkspace(workspace);
  const leaseMs = Math.min(3600000, Math.max(60000, timeoutMs + 45000));
  const result = await client.send(runId, 'claim', { role, workerId, leaseMs });
  const claim = result.lease;
  if (!claim) return { state: 'no_work' };
  let leaseLost = false;
  const heartbeat = setInterval(() => client.send(runId, 'renew', {
    taskId: claim.taskId, workerId, leaseId: claim.id, leaseMs
  }).catch(() => { leaseLost = true; }), Math.min(60000, Math.max(5000, leaseMs / 3)));
  heartbeat.unref?.();
  try {
    if (!claim.prompt) throw new Error('task_prompt_missing');
    if (role === 'reviewer') verifyReviewHead(workspace, claim.candidate?.commitSha);
    const prompt = role === 'reviewer'
      ? claim.prompt + '\nReview only commit ' + claim.candidate.commitSha +
        '. Return only JSON with status(pass/fail/blocked), evidence, findings. Never invent test evidence.'
      : claim.prompt;
    const invocation = await invoke(backend, { role, prompt, workspace: base.root, timeoutMs });
    const proof = compactCliEvidence(invocation);
    if (leaseLost) throw new Error('lease_renewal_failed');
    if (role === 'development') {
      const commitSha = verifyDevelopmentCommit(workspace, base);
      const saved = await client.send(runId, 'development', { taskId: claim.taskId,
        workerId, leaseId: claim.id, commitSha, evidence: proof });
      return { state: saved.phase, taskId: claim.taskId, commitSha };
    }
    verifyReviewHead(workspace, claim.candidate.commitSha);
    const spec = readSpec(invocation.stdout);
    const standards = whitespaceCheck(workspace, claim.candidate.commitSha);
    // E2E requires independent, trusted verification; an AI text claim is not proof.
    let e2e = { status: 'blocked', evidence: 'trusted E2E verification unavailable',
      findings: ['e2e_unverified'] };
    if (e2eVerifier) {
      e2e = await e2eVerifier({ workspace, commitSha: claim.candidate.commitSha });
      if (!['pass','fail'].includes(e2e?.status) || !e2e.evidence)
        throw new Error('invalid_trusted_e2e_result');
    }
    const saved = await client.send(runId, 'review', { taskId: claim.taskId,
      workerId, leaseId: claim.id, reviewedCommitSha: claim.candidate.commitSha,
      checks: { e2e, standards, spec } });
    return { state: saved.phase, taskId: claim.taskId, checks: { e2e, standards, spec } };
  } catch (err) {
    const reason = String(err.message || err).slice(0, 900);
    try { await client.send(runId, 'block', { taskId: claim.taskId,
      workerId, leaseId: claim.id, reason }); } catch {}
    return { state: 'blocked', taskId: claim.taskId, reason };
  } finally { clearInterval(heartbeat); }
}

export async function main(args = process.argv.slice(2)) {
  const params = {};
  for (let i = 0; i < args.length; i += 2) {
    if (!/^--[a-z-]+$/.test(args[i]) || !args[i + 1]) throw new Error('arguments_invalid');
    params[args[i]] = args[i + 1];
  }
  for (const key of ['--bridge','--token-file','--run-id','--worker-id','--workspace','--role','--backend'])
    if (!params[key]) throw new Error('missing_' + key);
  const client = bridgeClient(params['--bridge'],
    readFileSync(resolve(params['--token-file']), 'utf8').trim());
  const output = await runOnce({ client, runId: params['--run-id'],
    workerId: params['--worker-id'], workspace: resolve(params['--workspace']),
    role: params['--role'], backend: params['--backend'] });
  console.log(JSON.stringify(output));
  if (output.state === 'blocked') process.exitCode = 2;
}
if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url)))
  main().catch(err => { console.error('worker_error:' + String(err.message).slice(0, 200)); process.exitCode = 1; });
