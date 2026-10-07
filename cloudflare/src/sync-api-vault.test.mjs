import test from 'node:test';
import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { createSyncApi } from './sync-api.js';
import { HttpError } from './http-error.js';

function fixture() {
  const api = createSyncApi({
    HttpError,
    hmacSha256: async (secret, data) =>
      createHmac('sha256', secret).update(data).digest('base64url'),
    json: value => Response.json(value),
    requireAuth: async request => {
      const token = request.headers.get('authorization');
      if (token === 'Bearer account-a') return { userId: 'account-a' };
      if (token === 'Bearer account-b') return { userId: 'account-b' };
      throw new HttpError(401, 'unauthorized', 'Authentication required');
    }
  });
  return api.handleSyncApi;
}

test('vault recovery key requires authentication and is stable per account', async () => {
  const handle = fixture();
  const env = { AUTH_TOKEN_SECRET: 'verification-auth-secret' };
  const url = 'https://sync.example.test/v1/sync/vault-key';

  await assert.rejects(
    () => handle(new Request(url), env, {}),
    error => error instanceof HttpError && error.status === 401
  );

  const first = await (await handle(new Request(url, {
    headers: { authorization: 'Bearer account-a' }
  }), env, {})).json();
  const second = await (await handle(new Request(url, {
    headers: { authorization: 'Bearer account-a' }
  }), env, {})).json();
  const other = await (await handle(new Request(url, {
    headers: { authorization: 'Bearer account-b' }
  }), env, {})).json();

  assert.equal(first.ok, true);
  assert.equal(first.version, 1);
  assert.equal(first.key, second.key);
  assert.notEqual(first.key, other.key);
  assert.equal(Buffer.from(first.key, 'base64url').length, 32);
});

test('dedicated recovery secret takes precedence over auth token signing secret', async () => {
  const handle = fixture();
  const request = new Request('https://sync.example.test/v1/sync/vault-key', {
    headers: { authorization: 'Bearer account-a' }
  });
  const first = await (await handle(request.clone(), {
    AUTH_TOKEN_SECRET: 'auth-a',
    VAULT_RECOVERY_SECRET: 'stable-recovery'
  }, {})).json();
  const second = await (await handle(request.clone(), {
    AUTH_TOKEN_SECRET: 'auth-b',
    VAULT_RECOVERY_SECRET: 'stable-recovery'
  }, {})).json();
  assert.equal(first.key, second.key);
});

test('vault recovery refuses to operate without a server recovery secret', async () => {
  const handle = fixture();
  await assert.rejects(
    () => handle(new Request('https://sync.example.test/v1/sync/vault-key', {
      headers: { authorization: 'Bearer account-a' }
    }), {}, {}),
    error => error instanceof HttpError &&
      error.status === 503 &&
      error.code === 'vault_recovery_unavailable'
  );
});
