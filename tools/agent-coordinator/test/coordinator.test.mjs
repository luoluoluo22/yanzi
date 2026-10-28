import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createCoordinator } from '../server.mjs';
import { cloudWorkerClient } from '../client.mjs';

const sha = x => x.repeat(40);
const checks = status => Object.fromEntries(['e2e','standards','spec'].map(name => [
  name, { status, evidence: name + ' confirmed by separate trusted executor', findings: [] }]));
const ADMIN = 'f'.repeat(64);
async function setup(t, { now = () => Date.now(), dbPath = null } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'yanzi-cloud-agents-'));
  const app = await createCoordinator({ databasePath: dbPath || join(dir, 'queue.sqlite'),
    adminToken: ADMIN, clock: now });
  t.after(async () => { if (app.server.listening) await app.close(); rmSync(dir, { recursive: true, force: true }); });
  const request = async (path, method = 'GET', payload, token = ADMIN, csrf = true) => {
    const res = await fetch(app.url + path, { method, headers: {
      Authorization: 'Bearer ' + token, 'Content-Type': 'application/json',
      ...(csrf ? { 'X-Yanzi-Agent-Request': '1' } : {}),
    }, body: payload ? JSON.stringify(payload) : undefined });
    return { status: res.status, data: await res.json() };
  };
  const enroll = async (deviceId, grantedRoles) => {
    const response = await request('/v1/admin/devices', 'POST', { deviceId, grantedRoles });
    assert.equal(response.status, 201);
    return response.data;
  };
  return { app, request, enroll, dir };
}
async function createRun(request, tasks = [
  { id: 'a', dependsOn: [], prompt: 'Do a safe task A' },
  { id: 'b', dependsOn: [], prompt: 'Do a safe task B' },
  { id: 'c', dependsOn: ['a','b'], prompt: 'Depends on a and b' },
]) {
  assert.equal((await request('/v1/admin/runs', 'POST', {
    runId: 'offline-device-case', tasks })).status, 201);
}

test('two authenticated devices claim distinct ready tasks and prevent duplicate claims', async t => {
  const { app, request, enroll } = await setup(t);
  await createRun(request);
  const a = await enroll('desktop-01', ['development']);
  const b = await enroll('company-pc', ['development']);
  const clientA = cloudWorkerClient(app.url, a.token);
  const clientB = cloudWorkerClient(app.url, b.token);
  const [one, two] = await Promise.all([
    clientA.send('offline-device-case', 'claim', {
      role: 'development', workerId: a.deviceId, leaseMs: 60000 }),
    clientB.send('offline-device-case', 'claim', {
      role: 'development', workerId: b.deviceId, leaseMs: 60000 }),
  ]);
  assert.notEqual(one.lease.taskId, two.lease.taskId);
  assert.equal((await clientA.send('offline-device-case', 'claim',
    { role: 'development', workerId: a.deviceId, leaseMs: 60000 })).lease, null);
  const state = (await request('/v1/admin/runs/offline-device-case')).data;
  assert.equal(Object.values(state.tasks).filter(x => x.phase === 'developing').length, 2);
  assert.equal(state.tasks.c.phase, 'queued');
});

test('restarted coordinator preserves ledger and enforces lease fencing', async t => {
  const { app, request, enroll, dir } = await setup(t);
  await createRun(request, [{ id: 'a', dependsOn: [], prompt: 'One' }]);
  const a = await enroll('desktop-01', ['development']);
  const client = cloudWorkerClient(app.url, a.token);
  const claimed = (await client.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'desktop-01', leaseMs: 60000 })).lease;
  await app.close();
  const reopened = await createCoordinator({ databasePath: join(dir, 'queue.sqlite'), adminToken: ADMIN });
  const next = cloudWorkerClient(reopened.url, a.token);
  assert.equal((await next.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'desktop-01', leaseMs: 60000 })).lease, null);
  assert.equal((await next.send('offline-device-case', 'renew',
    { taskId: claimed.taskId, workerId: 'desktop-01', leaseId: claimed.id, leaseMs: 60000 })).id,
    claimed.id);
  await assert.rejects(() => next.send('offline-device-case', 'development', {
    taskId: claimed.taskId, workerId: 'desktop-01', leaseId: 'fake',
    commitSha: sha('a'), evidence: 'evidence' }), /403/);
  const written = await next.send('offline-device-case', 'development', {
    taskId: claimed.taskId, workerId: 'desktop-01', leaseId: claimed.id,
    commitSha: sha('a'), evidence: 'Real git commit evidence' });
  assert.equal(written.phase, 'review');
  await reopened.close();
});

test('device revocation, CSRF and identity/role matching are enforced', async t => {
  const { app, request, enroll } = await setup(t);
  await createRun(request, [{ id: 'a', dependsOn: [], prompt: 'One' }]);
  const dev = await enroll('dev', ['development']);
  const reviewer = await enroll('qa', ['reviewer']);
  assert.equal((await fetch(app.url + '/v1/admin/devices')).status, 401);
  assert.equal((await request('/v1/admin/runs', 'POST',
    { runId:'bad',tasks:[] }, ADMIN, false)).status, 403);
  assert.equal((await request('/v1/admin/devices', 'GET', null, dev.token)).status, 404);
  const devClient = cloudWorkerClient(app.url, dev.token);
  const qaClient = cloudWorkerClient(app.url, reviewer.token);
  await assert.rejects(() => qaClient.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'qa' }), /403/);
  await assert.rejects(() => devClient.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'fake' }), /403/);
  const lease = (await devClient.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'dev' })).lease;
  assert.ok(lease);
  const revoke = await request('/v1/admin/devices/dev', 'DELETE');
  assert.equal(revoke.data.revoked, true);
  await assert.rejects(() => devClient.send('offline-device-case', 'renew',
    { taskId: 'a', workerId: 'dev', leaseId: lease.id }), /401/);
});

test('offline expiry blocks ambiguous work; no automatic retry; manual recovery increments generation', async t => {
  let now = 1000;
  const { app, request, enroll } = await setup(t, { now: () => now });
  await createRun(request, [{ id: 'a', dependsOn: [], prompt: 'One' }]);
  const dev = await enroll('dev', ['development']);
  const client = cloudWorkerClient(app.url, dev.token);
  const first = (await client.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'dev', leaseMs: 1000 })).lease;
  now = 2001;
  const second = await client.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'dev', leaseMs: 1000 });
  assert.equal(second.lease, null);
  const old = (await request('/v1/admin/runs/offline-device-case')).data.tasks.a;
  assert.equal(old.phase, 'blocked');
  assert.equal(old.blockedReason, 'external_outcome_uncertain');
  assert.equal(old.generation, first.generation);
  const recovered = await request('/v1/admin/runs/offline-device-case/retry',
    'POST', { taskId: 'a', reason: 'verified no external side effects' });
  assert.equal(recovered.status, 200);
  assert.equal(recovered.data.generation, first.generation + 1);
  const reissued = (await client.send('offline-device-case', 'claim',
    { role: 'development', workerId: 'dev', leaseMs: 1000 })).lease;
  assert.equal(reissued.generation, first.generation + 1);
  await assert.rejects(() => client.send('offline-device-case','development',{
    workerId:'dev',taskId:'a',leaseId:first.id,commitSha:sha('a'),evidence:'stale'}),
  /403/);
});

test('reviewer cannot equal developer; QA accepts exact commit and blocks without verification', async t => {
  const { app, request, enroll } = await setup(t);
  await createRun(request, [{ id: 'a', dependsOn: [], prompt: 'One' }]);
  const dev = await enroll('dev', ['development', 'reviewer']);
  const qa = await enroll('qa', ['reviewer']);
  const d = cloudWorkerClient(app.url, dev.token), q = cloudWorkerClient(app.url, qa.token);
  const lease = (await d.send('offline-device-case','claim', { role:'development',workerId:'dev' })).lease;
  await d.send('offline-device-case','development',{taskId:'a',workerId:'dev',leaseId:lease.id,commitSha:sha('a'),evidence:'git evidence'});
  assert.equal((await d.send('offline-device-case','claim',{role:'reviewer',workerId:'dev'})).lease,null);
  const review = (await q.send('offline-device-case','claim',{role:'reviewer',workerId:'qa'})).lease;
  const result = await q.send('offline-device-case','review',{taskId:'a',workerId:'qa',leaseId:review.id,
    reviewedCommitSha:sha('a'),checks:checks('blocked')});
  assert.equal(result.phase,'blocked');
});

test('remote worker client refuses plain HTTP and URL credential injection', () => {
  assert.throws(() => cloudWorkerClient('http://example.com/', ADMIN), /requires_tls/);
  assert.throws(() => cloudWorkerClient('https://u:p@example.com/', ADMIN), /url_invalid/);
});


test('two independent coordinator processes sharing one SQLite database do not double-claim', async t => {
  const { app, request, enroll, dir } = await setup(t);
  await createRun(request, [{ id: 'a', dependsOn: [], prompt: 'One only' }]);
  const a = await enroll('station-a', ['development']);
  const b = await enroll('station-b', ['development']);
  const second = await createCoordinator({ databasePath: join(dir, 'queue.sqlite'),
    adminToken: ADMIN });
  const one = cloudWorkerClient(app.url, a.token);
  const two = cloudWorkerClient(second.url, b.token);
  const responses = await Promise.all([
    one.send('offline-device-case','claim',{role:'development',workerId:'station-a'}),
    two.send('offline-device-case','claim',{role:'development',workerId:'station-b'}),
  ]);
  assert.equal(responses.filter(x => x.lease !== null).length, 1);
  const snapshot = (await request('/v1/admin/runs/offline-device-case')).data;
  assert.equal(snapshot.tasks.a.phase, 'developing');
  await second.close();
});
