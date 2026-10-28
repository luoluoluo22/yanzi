import assert from 'node:assert/strict';
import { sendProviderPush, sendScopedWxPusherFallback, sendOfflinePush } from '../src/mobile-push.js';
assert.equal(await sendProviderPush({}, 'fcm', 'test-token', 'test-message'), 'not_configured');
let calls = [];
globalThis.fetch = async (url, options) => {
  calls.push({ url: String(url), options });
  return Response.json(String(url).includes('oauth2') ? { access_token: 'mock-access' } : { ok: true });
};
assert.equal(await sendProviderPush({ PUSH_WEBHOOK_URL: 'https://push.example.test', PUSH_WEBHOOK_TOKEN: 'mock-secret' },
  'webhook', 'test-token', 'test-message'), 'sent');
assert.equal(JSON.parse(calls[0].options.body).messageId, 'test-message');
await assert.rejects(sendProviderPush({ PUSH_WEBHOOK_URL: 'http://push.example.test', PUSH_WEBHOOK_TOKEN: 'mock' }, 'webhook', 'token', 'id'));
const keys = await crypto.subtle.generateKey({ name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: new Uint8Array([1,0,1]), hash: 'SHA-256' }, true, ['sign', 'verify']);
const pem = '-----BEGIN PRIVATE KEY-----\n' + Buffer.from(await crypto.subtle.exportKey('pkcs8', keys.privateKey)).toString('base64') + '\n-----END PRIVATE KEY-----';
calls = [];
assert.equal(await sendProviderPush({ FIREBASE_SERVICE_ACCOUNT: JSON.stringify({ private_key: pem, client_email: 'mock@example.test', project_id: 'test-project' }) }, 'fcm', 'test-token', 'test-message'), 'sent');
const assertion = calls[0].options.body.get('assertion');
const [header, payload, signature] = assertion.split('.');
assert.equal(await crypto.subtle.verify('RSASSA-PKCS1-v1_5', keys.publicKey, Buffer.from(signature, 'base64url'), new TextEncoder().encode(header + '.' + payload)), true);
assert.equal(JSON.parse(calls[1].options.body).message.android.priority, 'HIGH');
globalThis.fetch = async () => new Response('', { status: 503 });
await assert.rejects(sendProviderPush({ PUSH_WEBHOOK_URL: 'https://push.example.test', PUSH_WEBHOOK_TOKEN: 'mock' }, 'webhook', 'token', 'id'));
const fakeSpt = 'S' + 'PT_' + 'X'.repeat(24);
const scopedEnv = {
  YANZI_WXPUSHER_SPT: fakeSpt,
  YANZI_WXPUSHER_USER_ID: 'account-1',
  YANZI_WXPUSHER_DEVICE_ID: 'android-K70',
  DB: { prepare: () => ({ bind: () => ({ all: async () => ({ results: [] }) }) }) }
};
let fallbackRequests = [];
globalThis.fetch = async (url, options) => {
  fallbackRequests.push({ url: String(url), body: JSON.parse(options.body) });
  return Response.json({ code: 1000, success: true });
};
const privateMessage = { targetDeviceId: 'android-K70', messageId: 'scoped-1', text: 'PRIVATE_BODY' };
assert.equal(await sendScopedWxPusherFallback(scopedEnv, 'account-1', privateMessage), 'queued');
assert.equal(fallbackRequests.length, 1);
assert.equal(fallbackRequests[0].body.spt, fakeSpt);
assert.equal(JSON.stringify(fallbackRequests[0].body).includes('PRIVATE_BODY'), false);
assert.equal(await sendScopedWxPusherFallback(scopedEnv, 'another-account', privateMessage), 'not_configured');
assert.equal(await sendScopedWxPusherFallback(scopedEnv, 'account-1', {targetDeviceId:'other-device',messageId:'scoped-2'}), 'not_targeted');
assert.equal(fallbackRequests.length, 1);
fallbackRequests = [];
await sendOfflinePush(scopedEnv, 'account-1', privateMessage);
assert.equal(fallbackRequests.length, 1, 'fallback works even without a native push token in D1');
globalThis.fetch = async () => Response.json({ code: 1001, success: false });
await assert.rejects(sendScopedWxPusherFallback(scopedEnv, 'account-1', privateMessage), /wxpusher_queue_rejected/);
console.log('Scoped WxPusher fallback tests PASSED.');
console.log('Push adapter tests PASSED (mocked provider transport, no live push credentials).');
