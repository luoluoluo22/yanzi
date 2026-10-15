import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createDeviceApi } from './device-api.js';

const PREFIX = '\u001eYANZI_STREAM:';
const makeControl = (text = '你') => PREFIX + JSON.stringify({
  v:1, id:'agy-123456789abcdef', conversationId:'agent:antigravity:desktop-1',
  state:'update', seq:2, text
});
class HttpError extends Error {
  constructor(status, code, message) { super(message); this.status=status;this.code=code; }
}
function createHarness() {
  const events = [];
  const api = createDeviceApi({
    HttpError,
    requireAuth: async () => ({ userId:'account-1' }),
    readJson: async req => req.json(),
    normalizeDeviceId: value => {
      if (!/^[a-z0-9-]{1,128}$/.test(value || '')) throw new Error('invalid-device-id');
      return value;
    },
    ensureOwnedDevice: async (_env, owner, id) => {
      assert.equal(owner,'account-1');
      if (!['desktop-1','android-1'].includes(id)) throw new HttpError(403,'not_owned','Not an owned device');
      return {platform:id.startsWith('android')?'android':'desktop'};
    },
    json: (value,status=200) => Response.json(value,{status})
  });
  const env = {
    DB: { prepare() { throw new Error('Streaming must not touch D1'); } },
    DEVICE_RELAY: {
      idFromName: value => value,
      get: _id => ({ publish: async event => { events.push(event); return {delivered:1}; } })
    }
  };
  const send = async content => api.handleDeviceApi(new Request('https://example.com/v1/me/mobile/messages/stream',{
    method:'POST',headers:{'content-type':'application/json'},
    body:JSON.stringify({sourceDeviceId:'desktop-1',targetDeviceId:'android-1',content})
  }),env,{});
  return { send,events };
}
test('Transient WebSocket publishing skips D1 message and durable outbox',async()=>{
  const h=createHarness();
  const response=await h.send(makeControl());
  assert.equal(response.status,200);
  const result=await response.json();
  assert.equal(result.durable,false);
  assert.equal(result.delivered,1);
  assert.equal(h.events.length,1);
  assert.equal(h.events[0].type,'agent-stream');
  assert.equal(h.events[0].targetDeviceId,'android-1');
});
test('Malformed transient stream does not publish',async()=>{
  const h=createHarness();
  await assert.rejects(h.send(makeControl().replace('"seq":2','"seq":-1')), /Invalid stream control fields/);
  await assert.rejects(h.send('plain chat text'), /Invalid transient stream content/);
  assert.equal(h.events.length,0);
});
