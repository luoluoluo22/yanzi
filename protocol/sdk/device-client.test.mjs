import test from 'node:test';
import assert from 'node:assert/strict';
import {DeviceClient, DeviceProtocolError, canonicalJson} from './device-client.mjs';

class Store {
  records = new Map();
  async get(key) { return structuredClone(this.records.get(key)); }
  async set(key, value) { this.records.set(key, structuredClone(value)); }
  async list(prefix, limit) { return [...this.records].filter(([key]) => key.startsWith(prefix)).slice(0, limit).map(x => structuredClone(x)); }
  async compareExchange(key, expectedState, value) {
    if ((this.records.get(key)?.state ?? null) !== expectedState) return false;
    this.records.set(key, structuredClone(value)); return true;
  }
}
const reply = (body, status = 200) => new Response(JSON.stringify(body), {status});
function fixture(overrides = {}) {
  return new DeviceClient({baseUrl:'http://127.0.0.1:8080', accountId:'account-a', deviceId:'device-a', platform:'linux',
    displayName:'SDK fixture', store:new Store(), getToken:async()=>'disposable', ...overrides});
}
test('offline outbox survives client recreation and retries the same logical message', async () => {
  const store = new Store(); let time = 100000, calls = [];
  const first = fixture({store, now:()=>time, fetchImpl:async(_, request)=>{calls.push(JSON.parse(request.body));throw Error('offline');}});
  const queued = await first.send({kind:'text', targetDeviceId:'peer-a', text:'hello', clientMessageId:'stable-message'});
  assert.equal(queued.state, 'queued'); time += 3000;
  const restarted = fixture({store, now:()=>time, fetchImpl:async(_, request)=>{calls.push(JSON.parse(request.body));return reply({messageId:'server-message'});}});
  await restarted.flush();
  assert.equal((await store.get('account-a/device-a/outbox/stable-message')).state, 'accepted');
  assert.equal(canonicalJson(calls[0]), canonicalJson(calls[1]));
});
test('expired commands and ambiguous device targets never reach transport', async () => {
  let calls = 0;
  const client = fixture({fetchImpl:async()=>{calls++;return reply({});}, now:()=>100000});
  await assert.rejects(client.send({kind:'run-shell'}), e=>e.code === 'target_device_required');
  const result = await client.send({kind:'run-shell', targetDeviceId:'peer-a', expiresAt:new Date(99999).toISOString()});
  assert.equal(result.state, 'expired'); assert.equal(calls, 0);
});
test('lost execution ACK and restart repeat receipts, never repeat effects', async () => {
  const store = new Store(); let effects = 0, failAck = true;
  const fetchImpl = async(url) => {
    if (url.endsWith('/claim')) return reply({acquired:true, expiresAt:new Date(Date.now()+60000).toISOString()});
    if (failAck) throw Error('ACK lost'); return reply({ok:true});
  };
  const message = {messageId:'command-a', sourceDeviceId:'sender-a', targetDeviceId:'device-a', kind:'run-shell',
    operationId:'operation-a', expiresAt:new Date(Date.now()+60000).toISOString()};
  await assert.rejects(fixture({store, fetchImpl, execute:async()=>{effects++;return {output:'done'};}}).consume(message));
  failAck = false;
  const completed = await fixture({store, fetchImpl, execute:async()=>{effects++;}}).consume(message);
  assert.equal(effects, 1); assert.equal(completed.state, 'completed');
});
test('crash during execution is uncertain and never automatically re-executed', async () => {
  const store = new Store(); let effects = 0, state;
  const message = {messageId:'command-b', sourceDeviceId:'sender-a', kind:'run-shell', operationId:'operation-b'};
  await store.set('account-a/device-a/inbox/sender-a/operation-b', {state:'executing', message});
  const client = fixture({store, execute:async()=>{effects++;}, fetchImpl:async(_, request)=>{state=JSON.parse(request.body).resultState;return reply({ok:true});}});
  const result = await client.consume(message);
  assert.equal(effects, 0); assert.equal(result.state, 'unknown'); assert.equal(state, 'unknown');
});
test('shared transactional inbox prevents two device runtimes applying the same chat effect', async () => {
  const store = new Store(); let effects = 0;
  const options = {store, receive:async()=>{effects++;}, fetchImpl:async()=>reply({ok:true})};
  const message = {messageId:'chat-a', sourceDeviceId:'sender-a', kind:'text', operationId:'chat-operation'};
  await Promise.all([fixture(options).consume(message), fixture(options).consume(message)]);
  assert.equal(effects, 1);
});
test('pending pagination starts below saved cursor to recover lost acknowledgements', async () => {
  let pages = [], effects = 0;
  const store = new Store(); await store.set('account-a/device-a/cursor', 100);
  const client = fixture({store, receive:async()=>{effects++;}, fetchImpl:async(url)=>{
    if (url.includes('?')) {pages.push(url); return reply({items:pages.length===1?[{messageId:'pending-old',kind:'text',sourceDeviceId:'peer'}]:[], nextCursor:pages.length===1?2:3, hasMore:pages.length===1});}
    return reply({ok:true});
  }});
  await client.sync(); assert.equal(effects, 1); assert.ok(pages[0].endsWith('after=0')); assert.ok(pages[1].endsWith('after=2'));
});
test('cleartext remote servers and nontransactional receiver storage are rejected', () => {
  assert.throws(()=>fixture({baseUrl:'http://remote.example'}), DeviceProtocolError);
  assert.throws(()=>fixture({store:{}, receive:async()=>{}}), e=>e.code==='transactional_inbox_store_required');
});

test('a concurrent duplicate waits for the active effect and never reports it as crashed', async () => {
  const store = new Store(); let release, entered, effects = 0, acknowledgements = [];
  const started = new Promise(resolve => entered = resolve);
  const gate = new Promise(resolve => release = resolve);
  const options = {store, receive:async()=>{effects++; entered(); await gate; return {output:'saved'};},
    fetchImpl:async(_, request)=>{acknowledgements.push(JSON.parse(request.body));return reply({ok:true});}};
  const message = {messageId:'active-chat', sourceDeviceId:'sender-a', kind:'text', operationId:'active-operation'};
  const first = fixture(options).consume(message);
  await started;
  const second = fixture(options).consume(message);
  release();
  const results = await Promise.all([first, second]);
  assert.equal(effects, 1);
  assert.ok(results.every(result => result.state === 'completed'));
  assert.equal(acknowledgements.length, 1);
  assert.equal((await store.get('account-a/device-a/inbox/sender-a/active-operation')).state, 'completed');
});

test('invalid expiry and absent receiver do not transmit or acknowledge data', async () => {
  let calls = 0;
  const client = fixture({fetchImpl:async()=>{calls++;return reply({ok:true});}});
  await assert.rejects(client.send({kind:'run-shell', targetDeviceId:'peer', expiresAt:'broken'}), e=>e.code==='invalid_expiration');
  await assert.rejects(client.consume({messageId:'unhandled', kind:'text', sourceDeviceId:'peer'}), e=>e.code==='receive_adapter_required');
  assert.equal(calls, 0);
});

test('HTML authorization errors keep HTTP status; HTML outages remain retryable', async () => {
  for (const [status, state] of [[403, 'failed'], [503, 'queued']]) {
    const client = fixture({fetchImpl:async()=>new Response('<html>gateway</html>', {status})});
    const result = await client.send({kind:'text', targetDeviceId:'peer', text:'hello'});
    assert.equal(result.state, state);
    assert.equal(result.errorCode, 'http_error');
  }
});

test('a claim without a valid deadline never invokes an execution adapter', async () => {
  let effects = 0;
  const client = fixture({execute:async()=>{effects++;}, fetchImpl:async url=>reply(url.endsWith('/claim') ? {acquired:true} : {ok:true})});
  const result = await client.consume({messageId:'invalid-claim', sourceDeviceId:'peer', kind:'run-shell', expiresAt:new Date(Date.now()+60000).toISOString()});
  assert.equal(result.state, 'expired');
  assert.equal(effects, 0);
});

test('an ambiguous successful gateway response keeps the original queued envelope for recovery', async () => {
  for (const response of [() => reply({}), () => new Response('<html>gateway</html>')]) {
    const client = fixture({fetchImpl:async()=>response()});
    const result = await client.send({kind:'text', targetDeviceId:'peer', clientMessageId:'stable-recovery', text:'preserve me'});
    assert.equal(result.state, 'queued');
    assert.equal(result.errorCode, 'invalid_response');
    assert.equal(result.envelope.clientMessageId, 'stable-recovery');
  }
});
