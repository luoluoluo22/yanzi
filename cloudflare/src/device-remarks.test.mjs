import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { handleDeviceRemark, normalizeDeviceRemark } from './device-remarks.js';
import { createDeviceRepository } from './device-repository.js';
import { createDeviceApi } from './device-api.js';
import { HttpError } from './http-error.js';

function fixture() {
  const db = new DatabaseSync(':memory:');
  db.exec(`CREATE TABLE user_devices(device_id TEXT PRIMARY KEY, user_id TEXT, display_name TEXT, capabilities_json TEXT, updated_at TEXT,
    platform TEXT, push_token TEXT, last_seen_at TEXT, created_at TEXT);
    INSERT INTO user_devices(device_id,user_id,display_name,capabilities_json,updated_at) VALUES('pc','a','Windows · MODEL','{"keep":true}','old');`);
  const env = { DB: { prepare(sql) {
    let args = [];
    return { bind(...values) { args = values; return this; },
      run() { return db.prepare(sql).run(...args); },
      first() { return db.prepare(sql).get(...args) ?? null; },
      all() { return { results: db.prepare(sql).all(...args) }; } };
  } } };
  const api = { HttpError, normalizeDeviceId: value => value, readJson: request => request.json(),
    ensureUser: async () => {}, normalizeDevicePayload: value => value,
    deviceNetworkLocation: () => null, parseJsonObject: value => JSON.parse(value || '{}'),
    serializeDeviceRecord: createDeviceRepository({ DEVICE_ONLINE_WINDOW_MS: 120000 }).serializeDeviceRecord,
    isoNow: () => '2026-10-19T00:00:00Z', json: value => Response.json(value),
    requireAuth: async request => {
      const user = request.headers.get('Authorization');
      if (!user) throw new HttpError(401, 'unauthorized', 'Login required');
      return { userId: user, ...(user === 'grant' ? { grant: {} } : {}) };
    },
    ensureOwnedDevice: async (_, user, device) => {
      if (!db.prepare('SELECT 1 FROM user_devices WHERE user_id=? AND device_id=?').get(user, device))
        throw new HttpError(404, 'device_not_found', 'Not owned');
    }
  };
  return { db, env, api, async set(remark, user = 'a') {
    return handleDeviceRemark(new Request('https://fixture.test/v1/me/devices/pc/remark', {
      method: 'PUT', headers: { Authorization: user }, body: JSON.stringify({ remark })
    }), env, api);
  } };
}

test('offline remark is account-owned, persistent, clearable and preserves device capabilities', async () => {
  const f = fixture();
  try {
    await f.set('  公司电脑  ');
    const row = f.db.prepare('SELECT * FROM user_devices').get();
    assert.deepEqual(JSON.parse(row.capabilities_json), {
      keep: true, accountRemark: '公司电脑', accountRemarkUpdatedAt: '2026-10-19T00:00:00Z'
    });
    const repo = createDeviceRepository({ DEVICE_ONLINE_WINDOW_MS: 120000 });
    const output = repo.serializeDeviceRecord({ ...row, last_seen_at: '2000-01-01', platform: 'desktop' });
    assert.equal(output.displayName, '公司电脑');
    assert.equal(output.modelName, 'Windows · MODEL');
    assert.equal(output.online, false);
    await assert.rejects(f.set('别人的电脑', 'b'), /Not owned/);
    await assert.rejects(f.set('forbidden', 'grant'), /Account login/);
    assert.equal(JSON.parse(f.db.prepare('SELECT capabilities_json FROM user_devices').get().capabilities_json).accountRemark, '公司电脑');
    await f.set('');
    assert.equal(repo.serializeDeviceRecord(f.db.prepare('SELECT * FROM user_devices').get()).displayName, 'Windows · MODEL');
  } finally { f.db.close(); }
});

test('another client reads the shared name and heartbeat cannot overwrite it', async () => {
  const f = fixture();
  try {
    await f.set('家里台式机');
    const { handleDeviceApi } = createDeviceApi(f.api);
    await handleDeviceApi(new Request('https://fixture.test/v1/me/devices', {
      method: 'POST', headers: { Authorization: 'a' }, body: JSON.stringify({
        deviceId: 'pc', platform: 'desktop', displayName: 'Windows · MODEL', pushToken: null,
        capabilities: { receiveAccountChat: true, accountRemark: 'hijacked', accountRemarkUpdatedAt: null }
      })
    }), f.env, {});
    const response = await handleDeviceApi(new Request('https://fixture.test/v1/me/devices', {
      headers: { Authorization: 'a' }
    }), f.env, {});
    const device = (await response.json()).items[0];
    assert.equal(device.remark, '家里台式机');
    assert.equal(device.displayName, '家里台式机');
    assert.equal(device.modelName, 'Windows · MODEL');
    assert.equal(device.capabilities.receiveAccountChat, true);
    const otherAccount = await handleDeviceApi(new Request('https://fixture.test/v1/me/devices', {
      headers: { Authorization: 'b' }
    }), f.env, {});
    assert.deepEqual((await otherAccount.json()).items, []);
  } finally { f.db.close(); }
});

test('remark validation normalizes Unicode and rejects oversized, control and non-string input', () => {
  assert.equal(normalizeDeviceRemark(' e\u0301 ', HttpError), 'é');
  assert.equal(normalizeDeviceRemark('😀'.repeat(40), HttpError).length, 80);
  for (const value of ['a'.repeat(41), 'x\ny', null, 42, {}])
    assert.throws(() => normalizeDeviceRemark(value, HttpError));
});
