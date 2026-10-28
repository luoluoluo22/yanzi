// One-shot cloud worker runner. Use Task Scheduler/systemd with an explicit
// safe schedule; it does not run indefinitely or modify production services.
import { readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cloudWorkerClient } from './client.mjs';
import { runOnce } from '../chatgpt-bridge/delivery-worker.mjs';

export function readDeviceToken(path) {
  const absolute = resolve(path);
  const stat = statSync(absolute);
  if (!stat.isFile() || stat.size > 256) throw new Error('device_token_file_invalid');
  if (process.platform !== 'win32' && (stat.mode & 0o077) !== 0)
    throw new Error('device_token_file_permissions_too_open');
  const token = readFileSync(absolute, 'utf8').trim();
  if (!/^[a-f0-9]{64}$/i.test(token)) throw new Error('device_token_invalid');
  return token;
}
export async function main(argv = process.argv.slice(2)) {
  const args = {};
  for (let i = 0; i < argv.length; i += 2) {
    if (!/^--[a-z-]+$/.test(argv[i]) || !argv[i + 1])
      throw new Error('worker_arguments_invalid');
    args[argv[i]] = argv[i + 1];
  }
  for (const key of ['--server','--token-file','--run-id',
    '--device-id','--workspace','--role','--backend'])
    if (!args[key]) throw new Error('missing_' + key);
  if (!['agy','codex'].includes(args['--backend'])) throw new Error('backend_not_supported');
  if (!['development','reviewer'].includes(args['--role'])) throw new Error('role_invalid');
  const client = cloudWorkerClient(args['--server'], readDeviceToken(args['--token-file']));
  const result = await runOnce({
    client, runId: args['--run-id'], workerId: args['--device-id'],
    workspace: resolve(args['--workspace']), role: args['--role'],
    backend: args['--backend'],
  });
  // Never print arbitrary model output; only operation metadata.
  console.log(JSON.stringify({ state: result.state, taskId: result.taskId,
    commitSha: result.commitSha, reason: result.reason }));
  if (result.state === 'blocked') process.exitCode = 2;
}
if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url)))
  main().catch(error => {
    console.error('cloud_worker_failed:' + String(error.message || error).slice(0, 160));
    process.exitCode = 1;
  });
