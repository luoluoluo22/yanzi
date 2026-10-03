import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeDevicePayload, normalizeDeviceMessagePayload } from './device-payload.js';
import { readJson } from './request-json.js';
import { HttpError } from './http-error.js';

test('device registration and messages keep legacy aliases and explicit-device precedence', () => {
  const device = normalizeDevicePayload({device_id:'phone-123', platform:'Android', name:' 手机 ', capabilities:{receiveAccountChat:true}});
  assert.equal(device.deviceId, 'phone-123');
  assert.equal(device.platform, 'android');
  assert.equal(device.displayName, '手机');
  const message = normalizeDeviceMessagePayload({source_device_id:'phone-123', target_device_id:'desktop-123', target_platform:'android', body_text:'中文\nmessage', expires_at:'2026-10-03T08:00:00+08:00'});
  assert.equal(message.targetPlatform, null);
  assert.equal(message.bodyText, '中文\nmessage');
  assert.equal(message.expiresAt, '2026-10-03T00:00:00.000Z');
});

test('invalid identities, capabilities and deadlines are client errors', () => {
  for (const input of [{targetDeviceId:'../escape'}, {payload:[]}, {expiresAt:'not-a-date'}, {kind:'run shell'}])
    assert.throws(() => normalizeDeviceMessagePayload(input), e => e instanceof HttpError && e.status === 400);
});

test('malformed JSON and non-object request bodies return 400 rather than server failures', async () => {
  for (const body of ['{', '', 'null', '[]', '1', '"text"']) {
    await assert.rejects(readJson(new Request('https://fixture.invalid', {method:'POST', body})),
      e => e instanceof HttpError && e.status === 400 && e.code === 'invalid_json');
  }
  assert.deepEqual(await readJson(new Request('https://fixture.invalid', {method:'POST', body:'{"text":"中文"}'})), {text:'中文'});
});
