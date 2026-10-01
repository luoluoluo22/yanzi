import assert from 'node:assert/strict';
import { sendProviderPush } from '../src/mobile-push.js';
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
console.log('Push adapter tests PASSED (mocked provider transport, no live push credentials).');
