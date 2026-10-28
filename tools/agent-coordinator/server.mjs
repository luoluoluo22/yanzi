// Bind only to loopback. Deploying this behind authenticated TLS ingress
// requires an explicit production change; never use 0.0.0.0 here.
import http from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { CloudStore } from './store.mjs';

const RUN = /^[a-zA-Z0-9_.:-]{1,100}$/;
function equalSecret(a, b) {
  const x = Buffer.from(String(a || ''));
  const y = Buffer.from(String(b || ''));
  return x.length === y.length && timingSafeEqual(x, y);
}
function bearer(req) {
  const header = req.headers.authorization || '';
  return /^Bearer ([a-f0-9]{64})$/i.test(header) ? header.slice(7) : '';
}
async function parseBody(req) {
  let total = 0;
  const parts = [];
  for await (const part of req) {
    total += part.byteLength;
    if (total > 512000) throw new Error('request_too_large');
    parts.push(part);
  }
  const data = JSON.parse(Buffer.concat(parts).toString('utf8'));
  if (!data || typeof data !== 'object' || Array.isArray(data))
    throw new Error('invalid_request_body');
  return data;
}
function statusFor(error) {
  const code = String(error.message || error);
  if (/not_found/.test(code)) return 404;
  if (/forbidden|mismatch|identity|not_enabled/.test(code)) return 403;
  if (/ledger_writer_busy|capacity_reached/.test(code)) return 409;
  if (/corrupt|SQLITE_|database/.test(code)) return 500;
  return 400;
}

export async function createCoordinator({ databasePath, adminToken,
  port = 0, clock = Date.now, workers = 3 } = {}) {
  if (typeof databasePath !== 'string' || !databasePath ||
      !/^[a-f0-9]{64}$/i.test(adminToken || '') ||
      !Number.isInteger(port) || port < 0 || port > 65535)
    throw new Error('coordinator_configuration_invalid');
  const store = new CloudStore(databasePath, { clock, workers });
  const server = http.createServer(async (req, res) => {
    const reply = (status, data) => {
      res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8',
        'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
      res.end(JSON.stringify(data));
    };
    try {
      if (req.headers.host !== '127.0.0.1:' + server.address().port ||
          req.headers.origin) return reply(403, { error: 'host_or_origin_denied' });
      const method = req.method;
      const path = new URL(req.url, 'http://127.0.0.1').pathname;
      if (path === '/health' && method === 'GET')
        return reply(200, { ok: true, service: 'yanzi-agent-coordinator', transport: 'loopback' });
      const token = bearer(req);
      if (!token) return reply(401, { error: 'authentication_required' });
      const isAdmin = equalSecret(token, adminToken);
      const device = !isAdmin ? store.authenticate(token) : null;
      if (!isAdmin && !device) return reply(401, { error: 'authentication_required' });
      if (method !== 'GET' && req.headers['x-yanzi-agent-request'] !== '1')
        return reply(403, { error: 'request_header_required' });
      if (path === '/v1/admin/devices' && method === 'GET' && isAdmin)
        return reply(200, { devices: store.devices() });
      if (path === '/v1/admin/devices' && method === 'POST' && isAdmin)
        return reply(201, store.enroll(await parseBody(req)));
      const revoke = /^\/v1\/admin\/devices\/([a-zA-Z0-9_.:-]{1,100})$/.exec(path);
      if (revoke && method === 'DELETE' && isAdmin)
        return reply(200, store.disable(revoke[1]));
      if (path === '/v1/admin/runs' && method === 'POST' && isAdmin)
        return reply(201, store.createRun(await parseBody(req)));
      if (path === '/v1/admin/runs' && method === 'GET' && isAdmin)
        return reply(200, { runs: Object.values(store.snapshot().runs) });
      const recovery = /^\/v1\/admin\/runs\/([a-zA-Z0-9_.:-]{1,100})\/retry$/.exec(path);
      if (recovery && method === 'POST' && isAdmin)
        return reply(200, store.retryBlocked({ ...await parseBody(req), runId: recovery[1] }));
      const adminRun = /^\/v1\/admin\/runs\/([a-zA-Z0-9_.:-]{1,100})$/.exec(path);
      if (adminRun && method === 'GET' && isAdmin) {
        const state = store.snapshot(adminRun[1]);
        return reply(state ? 200 : 404, state || { error: 'run_not_found' });
      }
      const match = /^\/v1\/runs\/([a-zA-Z0-9_.:-]{1,100})\/(claim|renew|block|development|review)$/.exec(path);
      if (match && method === 'POST' && device) {
        const [, runId, action] = match;
        const input = { ...await parseBody(req), runId };
        if (action === 'claim')
          return reply(200, { lease: store.claimForDevice(input, device) });
        return reply(200, store.actForDevice(action, input, device));
      }
      return reply(404, { error: 'endpoint_not_available' });
    } catch (error) {
      return reply(statusFor(error), { error: String(error.message || error).slice(0, 180) });
    }
  });
  try {
    await new Promise((resolvePromise, reject) => {
      server.once('error', reject);
      server.listen(port, '127.0.0.1', resolvePromise);
    });
  } catch (error) { store.close(); throw error; }
  return {
    url: 'http://127.0.0.1:' + server.address().port,
    store, server,
    close: async () => {
      await new Promise((resolvePromise, reject) =>
        server.close(error => error ? reject(error) : resolvePromise()));
      store.close();
    },
  };
}
export async function main() {
  const { YANZI_AGENT_DB, YANZI_AGENT_ADMIN_TOKEN, YANZI_AGENT_PORT } = process.env;
  const app = await createCoordinator({ databasePath: YANZI_AGENT_DB,
    adminToken: YANZI_AGENT_ADMIN_TOKEN, port: YANZI_AGENT_PORT ? Number(YANZI_AGENT_PORT) : 58081 });
  const shutdown = async () => { await app.close(); process.exit(0); };
  process.once('SIGTERM', shutdown);
  process.once('SIGINT', shutdown);
  console.log(JSON.stringify({ ready: true, url: app.url, role: 'standalone-loopback-coordinator' }));
}
if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url)))
  main().catch(error => { console.error('coordinator_start_failed:' + error.message); process.exitCode = 1; });
