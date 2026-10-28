import assert from 'node:assert/strict';
import {mkdtempSync, rmSync} from 'node:fs';
import os from 'node:os';
import {join} from 'node:path';
import test from 'node:test';
import {DeviceClient, DeviceProtocolError} from '../../../protocol/sdk/device-client.mjs';
import {AtomicJsonDeviceStore} from '../src/store.mjs';

function fixture(t, handler, receive = async () => ({success: true})) {
  const root = mkdtempSync(join(os.tmpdir(), 'yanzi-cursor-test-'));
  t.after(() => rmSync(root, {recursive: true, force: true}));
  const store = new AtomicJsonDeviceStore(root);
  const clock = {value: 1_000_000_000};
  const observed = [];
  const makeClient = (activeStore = store) => new DeviceClient({
    baseUrl: 'https://example.invalid',
    accountId: 'account-test',
    deviceId: 'server-test',
    platform: 'server',
    store: activeStore,
    getToken: async () => 'test-token-not-real',
    now: () => clock.value,
    receive,
    fetchImpl: async (url, init) => {
      const requestUrl = new URL(url);
      const after = requestUrl.searchParams.get('after');
      observed.push({path: requestUrl.pathname, method: init.method, after: after === null ? null : Number(after)});
      if (requestUrl.pathname.endsWith('/ack')) return Response.json({ok: true});
      const result = await handler(Number(after), init);
      return result instanceof Response ? result : Response.json(result);
    }
  });
  return {root, store, clock, observed, makeClient};
}

function empty(nextCursor, extra = {}) {
  return {items: [], nextCursor, hasMore: false, ...extra};
}

test('bootstrap recovery then persistent cursor across polls and restart', async t => {
  const f = fixture(t, after => empty(after === 0 ? 1064 : after));
  const key = 'account-test/server-test/';
  await f.store.set(key + 'cursor', 1064);
  await f.makeClient().sync();
  await f.makeClient().sync();
  await f.store.close();
  const reopened = new AtomicJsonDeviceStore(f.root);
  await f.makeClient(reopened).sync();
  assert.deepEqual(f.observed.map(x => x.after), [0, 1064, 1064]);
  assert.equal(await reopened.get(key + 'cursor'), 1064);
  assert.equal(await reopened.get(key + 'recoveryCompletedAt'), f.clock.value);
  await reopened.close();
});

test('hourly recovery replays old pending message without duplicate execution', async t => {
  let received = 0;
  const message = {messageId: 'message-1', sourceDeviceId: 'phone-test', kind: 'text', sequence: 1};
  const f = fixture(t, after => after === 0
    ? {items: [message], nextCursor: 10, hasMore: false}
    : empty(10), async () => {received++; return {success: true};});
  const key = 'account-test/server-test/';
  await f.store.set(key + 'cursor', 10);
  await f.makeClient().sync();
  assert.equal(received, 1);
  await f.makeClient().sync();
  f.clock.value += 60 * 60 * 1000 + 1;
  await f.makeClient().sync();
  assert.equal(received, 1);
  assert.deepEqual(f.observed.filter(x => x.after !== null).map(x => x.after), [0, 10, 0]);
  assert.equal(await f.store.get(key + 'cursor'), 10);
  await f.store.close();
});

test('failed recovery throttled while incremental polling continues', async t => {
  let attempts = 0;
  const f = fixture(t, after => {
    if (after === 0 && ++attempts === 1) return Response.json({error: 'unavailable'}, {status: 503});
    return empty(after === 0 ? 200 : after);
  });
  const key = 'account-test/server-test/';
  await f.store.set(key + 'cursor', 200);
  await assert.rejects(f.makeClient().sync(), error => error instanceof DeviceProtocolError && error.status === 503);
  f.clock.value += 30_000;
  await f.makeClient().sync();
  f.clock.value += 5 * 60 * 1000 + 1;
  await f.makeClient().sync();
  assert.deepEqual(f.observed.map(x => x.after), [0, 200, 0]);
  assert.equal(attempts, 2);
  await f.store.close();
});

test('recovery pagination does not roll back the incremental checkpoint', async t => {
  const message = {messageId: 'old-message', sourceDeviceId: 'phone', kind: 'text', sequence: 1};
  const f = fixture(t, after => {
    if (after === 0) return {items: [message], nextCursor: 1, hasMore: true};
    if (after === 1) return empty(99);
    return empty(after);
  });
  const key = 'account-test/server-test/';
  await f.store.set(key + 'cursor', 99);
  await f.makeClient().sync();
  assert.equal(await f.store.get(key + 'cursor'), 99);
  await f.makeClient().sync();
  assert.deepEqual(f.observed.filter(x => x.after !== null).map(x => x.after), [0, 1, 99]);
  await f.store.close();
});

test('server cursorReset replaces a stale checkpoint', async t => {
  const f = fixture(t, () => empty(5, {cursorReset: true}));
  const key = 'account-test/server-test/';
  await f.store.set(key + 'cursor', 100);
  await f.store.set(key + 'recoveryCompletedAt', f.clock.value);
  await f.store.set(key + 'recoveryAttemptAt', f.clock.value);
  await f.makeClient().sync();
  assert.deepEqual(f.observed.map(x => x.after), [100]);
  assert.equal(await f.store.get(key + 'cursor'), 5);
  await f.store.close();
});

test('incremental sync picks up a new message and advances the checkpoint', async t => {
  let effects = 0;
  const next = {messageId: 'new-201', sourceDeviceId: 'phone', kind: 'text', sequence: 201};
  const f = fixture(t, after => after === 200
    ? {items: [next], nextCursor: 201, hasMore: false}
    : empty(after), async () => {effects++;});
  const key = 'account-test/server-test/';
  await f.store.set(key + 'cursor', 200);
  await f.store.set(key + 'recoveryCompletedAt', f.clock.value);
  await f.store.set(key + 'recoveryAttemptAt', f.clock.value);
  await f.makeClient().sync();
  assert.equal(effects, 1);
  assert.equal(await f.store.get(key + 'cursor'), 201);
  await f.makeClient().sync();
  assert.deepEqual(f.observed.filter(x => x.after !== null).map(x => x.after), [200, 201]);
  assert.equal(effects, 1);
  await f.store.close();
});
