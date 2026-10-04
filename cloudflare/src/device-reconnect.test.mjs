import test from 'node:test';
import assert from 'node:assert/strict';
import { createDeviceApi } from './device-api.js';
import { HttpError } from './http-error.js';

function fixture({ grant = null, owner = 'account-a', reconnect = false } = {}) {
  let written = null;
  const payload = { deviceId: 'phone-existing', platform: 'android', displayName: 'Phone',
    capabilities: { autoAccountLan: true }, reactivateRemovedDevice: reconnect };
  const api = createDeviceApi({
    HttpError, requireAuth: async () => ({ userId: 'account-a', grant }),
    readJson: async () => payload, ensureUser: async () => {},
    normalizeDevicePayload: x => ({ ...x, capabilities: { ...x.capabilities } }),
    parseJsonObject: x => JSON.parse(x || '{}'), deviceNetworkLocation: () => null,
    isoNow: () => '2026-10-04T00:00:00Z', json: x => x,
  });
  const env = { DB: { prepare: () => ({ bind: (...values) => ({
    first: async () => ({ user_id: owner, capabilities_json: '{"disabled":true}' }),
    run: async () => { written = values; },
  }) }) } };
  return { invoke: () => api.handleDeviceApi(new Request('https://example.test/v1/me/devices', { method: 'POST' }), env, {}),
    written: () => written };
}

test('removed device stays removed on background registration and malformed restore flags', async () => {
  for (const reconnect of [false, 'true']) {
    const f = fixture({ reconnect });
    await assert.rejects(f.invoke(), x => x.status === 403 && x.code === 'device_removed');
    assert.equal(f.written(), null);
  }
});
test('explicit owner reconnect reuses the same registration identity', async () => {
  const f = fixture({ reconnect: true });
  assert.equal((await f.invoke()).device.deviceId, 'phone-existing');
  assert.equal(f.written()[0], 'phone-existing');
  assert.deepEqual(JSON.parse(f.written()[5]), { autoAccountLan: true, disabled: false });
});
test('device grant and other accounts cannot restore a removed registration', async () => {
  for (const options of [{ grant: { credentialId: 'revoked-peer' } }, { owner: 'account-b' }]) {
    const f = fixture({ ...options, reconnect: true });
    await assert.rejects(f.invoke(), x => x.status === (options.grant ? 403 : 409));
    assert.equal(f.written(), null);
  }
});
