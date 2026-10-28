// Worker-side adapter for a trusted, isolated Git worktree. This module is
// NOT loaded by the Bridge server and is never triggered by an HTTP request.
// An operator must explicitly launch the worker with its workspace and backend.
import { spawn, spawnSync } from 'node:child_process';
import { lstatSync, realpathSync, statSync } from 'node:fs';
import { join } from 'node:path';

export const BACKENDS = new Set(['agy', 'codex']);

// Avoid forwarding Bridge/GitHub/Cloudflare secrets into a model-driven process.
// CLI provider login uses its own normal user profile, not our integration tokens.
export function sanitizedCliEnvironment(source = process.env) {
  return Object.fromEntries(Object.entries(source).filter(([key]) =>
    !/(TOKEN|PASSWORD|SECRET|API_KEY|COOKIE|CREDENTIAL|AUTH|^NODE_OPTIONS$|^GIT_ASKPASS$|^SSH_ASKPASS$)/i.test(key)));
}

const text = value => typeof value === 'string' ? value : '';
const truncate = (value, size) => text(value).slice(-size);

function git(workspace, args) {
  const result = spawnSync('git', ['-C', workspace, ...args], { encoding: 'utf8', windowsHide: true,
    timeout: 8000, maxBuffer: 64 * 1024, shell: false });
  if (result.status !== 0 || result.error)
    throw new Error('git_validation_failed:' + truncate(result.stderr || result.error?.message || '', 160));
  return text(result.stdout).trim();
}

export function validateIsolatedWorkspace(workspace, { clean = true } = {}) {
  if (typeof workspace !== 'string' || !workspace.trim()) throw new Error('workspace_required');
  const root = realpathSync(workspace);
  if (!lstatSync(join(root, '.git')).isFile()) throw new Error('linked_worktree_required');
  const actual = realpathSync(git(root, ['rev-parse', '--show-toplevel']));
  // On Windows a temp directory can resolve to the 8.3 ADMINI~1 alias while
  // Git prints the long Administrator path. Compare physical directory ID.
  const left = statSync(root), right = statSync(actual);
  if (left.dev !== right.dev || left.ino !== right.ino)
    throw new Error('workspace_git_root_mismatch');
  const branch = git(root, ['symbolic-ref', '--quiet', '--short', 'HEAD']);
  if (!branch || ['main', 'master'].includes(branch.toLowerCase()))
    throw new Error('isolated_feature_branch_required');
  if (clean && git(root, ['status', '--porcelain']))
    throw new Error('workspace_dirty');
  return { root, branch, head: git(root, ['rev-parse', '--verify', 'HEAD']) };
}

export function createAgentArgs(backend, { prompt, role, workspace, timeoutMs = 180000 } = {}) {
  if (!BACKENDS.has(backend)) throw new Error('backend_unsupported');
  if (!['development', 'reviewer'].includes(role)) throw new Error('role_unsupported');
  if (typeof prompt !== 'string' || !prompt.trim() || prompt.length > 8000) throw new Error('prompt_invalid');
  if (!Number.isInteger(timeoutMs) || timeoutMs < 1000 || timeoutMs > 900000)
    throw new Error('timeout_invalid');
  const fullPrompt = role === 'development'
    ? 'Work only inside this isolated Git worktree. Do not push or merge, do not read secrets, do not install dependencies. Finish by committing your changes locally. Task: ' + prompt
    : 'Review this checked-out commit independently. Do not write files, commit, merge or push. Give specific evidence and state whether requirements are met. Task: ' + prompt;
  if (backend === 'agy') return {
    executable: process.env.LOCALAPPDATA
      ? join(process.env.LOCALAPPDATA, 'agy', 'bin', 'agy.exe') : 'agy',
    args: ['--print', fullPrompt, '--mode', role === 'reviewer' ? 'plan' : 'accept-edits',
      '--sandbox', '--output-format', 'stream-json', '--print-timeout', String(Math.ceil(timeoutMs / 1000)) + 's'],
    stdin: null,
  };
  return { executable: 'codex',
    args: ['exec', '--cd', workspace, '--sandbox', role === 'reviewer' ? 'read-only' : 'workspace-write',
      '--json', '-'],
    stdin: fullPrompt,
  };
}

export function runAgentCli(backend, options, { spawnProcess = spawn } = {}) {
  const command = createAgentArgs(backend, options);
  const { workspace, timeoutMs = 180000 } = options;
  return new Promise((resolve, reject) => {
    let proc;
    try {
      proc = spawnProcess(command.executable, command.args, {
        cwd: workspace, env: sanitizedCliEnvironment(), shell: false,
        windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
      });
    } catch (error) { reject(error); return; }
    let output = '', errorText = '', settled = false, timedOut = false, overflow = false;
    const finish = (code, signal, reason) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      if (reason) { reject(new Error(reason)); return; }
      resolve({ exitCode: code, signal, stdout: output, stderr: errorText,
        timedOut, overflow, backend });
    };
    // Untrusted AI output must not exhaust the Bridge memory.
    proc.stdout.on('data', chunk => {
      if (output.length < 256000) output += chunk.toString().slice(0, 256000 - output.length);
      else overflow = true;
    });
    proc.stderr.on('data', chunk => {
      if (errorText.length < 16000) errorText += chunk.toString().slice(0, 16000 - errorText.length);
    });
    proc.on('error', err => finish(null, null, 'cli_spawn_failed:' + truncate(err.message, 200)));
    proc.on('close', (code, signal) => finish(code, signal, timedOut ? 'cli_timeout_unknown_outcome' : overflow ? 'cli_output_overflow' : null));
    if (command.stdin != null) proc.stdin.end(command.stdin);
    else proc.stdin.end();
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === 'win32' && proc.pid) {
        const killer = spawn('taskkill', ['/PID', String(proc.pid), '/T', '/F'],
          { stdio: 'ignore', windowsHide: true, shell: false });
        killer.on('error', () => proc.kill());
      } else proc.kill('SIGKILL');
    }, timeoutMs);
    timer.unref?.();
  });
}

export function compactCliEvidence(result) {
  if (result.exitCode !== 0 || result.timedOut || result.overflow)
    throw new Error('cli_not_successful');
  // Do not persist raw AI output (which may contain secrets) to the ledger.
  return result.backend + ' exited 0; structured response bytes=' + Buffer.byteLength(result.stdout) +
    '; stderr bytes=' + Buffer.byteLength(result.stderr);
}

export function verifyDevelopmentCommit(workspace, before) {
  const after = validateIsolatedWorkspace(workspace);
  if (after.branch !== before.branch) throw new Error('branch_switched');
  if (after.head === before.head) throw new Error('development_missing_commit');
  const ancestor = spawnSync('git', ['-C', after.root, 'merge-base', '--is-ancestor', before.head, after.head],
    { encoding: 'utf8', timeout: 8000, shell: false, windowsHide: true });
  if (ancestor.status !== 0) throw new Error('development_rewrote_history');
  if (!/^[0-9a-f]{40}$/i.test(after.head)) throw new Error('invalid_git_head');
  return after.head;
}

export function verifyReviewHead(workspace, expectedSha) {
  const info = validateIsolatedWorkspace(workspace);
  if (info.head !== expectedSha) throw new Error('review_head_mismatch');
  return info;
}
