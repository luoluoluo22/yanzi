/**
 * Wake the configured Windows Edge browser only for queued tasks.
 * Never kills existing browser sessions, bypasses login or retries a sent job.
 */
import { spawn, execFile } from 'node:child_process';
import { accessSync, constants } from 'node:fs';
import { join } from 'node:path';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
const DEFAULT_COOLDOWN_MS = 90_000;

export function findEdgeExecutable(env = process.env, exists = path => {
  try { accessSync(path, constants.F_OK); return true; } catch { return false; }
}) {
  const roots = [env['PROGRAMFILES(X86)'], env.ProgramFiles, env.PROGRAMFILES, env.LOCALAPPDATA]
    .filter(path => typeof path === 'string' && path.length > 0);
  for (const root of roots) {
    for (const suffix of [
      ['Microsoft', 'Edge', 'Application', 'msedge.exe'],
      ['Microsoft', 'Edge Beta', 'Application', 'msedge.exe']
    ]) {
      const executable = join(root, ...suffix);
      if (exists(executable)) return executable;
    }
  }
  return null;
}

export function tasklistHasEdge(text) {
  return typeof text === 'string' && text.split(/\r?\n/).some(line =>
    /^\s*"?msedge\.exe"?\s*[,\t]/i.test(line));
}

async function edgeRunning() {
  const { stdout } = await execFileAsync('tasklist.exe',
    ['/FI', 'IMAGENAME eq msedge.exe', '/FO', 'CSV', '/NH'],
    { timeout: 5000, windowsHide: true, maxBuffer: 256 * 1024 });
  return tasklistHasEdge(stdout);
}

function launchEdge(executable) {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, [
      '--start-minimized', '--no-first-run', 'https://chatgpt.com/'
    ], { detached: true, stdio: 'ignore', windowsHide: true });
    child.once('error', reject);
    child.once('spawn', () => { child.unref(); resolve(); });
  });
}

/**
 * request() is nonblocking from the job queue; overlapping calls share work.
 * Cooldown applies even when the browser is found running to avoid tasklist
 * storms. The next queued submission can retry after the cooldown.
 */
export function createBrowserWake({
  platform = process.platform,
  now = Date.now,
  cooldownMs = DEFAULT_COOLDOWN_MS,
  running = edgeRunning,
  findExecutable = findEdgeExecutable,
  launch = launchEdge
} = {}) {
  let lastCheckedAt = -Infinity;
  let pending = null;
  return {
    request() {
      if (pending) return pending;
      if (platform !== 'win32') return Promise.resolve({ status: 'unsupported_platform' });
      const current = now();
      if (current - lastCheckedAt < cooldownMs)
        return Promise.resolve({ status: 'cooldown' });
      lastCheckedAt = current;
      pending = (async () => {
        if (await running()) return { status: 'already_running' };
        const executable = findExecutable();
        if (!executable) return { status: 'edge_not_found' };
        await launch(executable);
        return { status: 'launched' };
      })().finally(() => { pending = null; });
      return pending;
    }
  };
}
