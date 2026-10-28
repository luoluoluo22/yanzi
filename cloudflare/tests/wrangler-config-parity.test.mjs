import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('../../', import.meta.url));
const root = JSON.parse(readFileSync(resolve(repositoryRoot, 'wrangler.jsonc'), 'utf8'));
const backend = readFileSync(resolve(repositoryRoot, 'cloudflare/wrangler.toml'), 'utf8');

function field(section, name) {
  const match = section.match(new RegExp('(?:^|\\n)\\s*' + name + '\\s*=\\s*(?:"([^"]+)"|(\\d+)|(?:(true|false)))'));
  return match ? (match[1] ?? (match[2] ? Number(match[2]) : match[3] === 'true')) : undefined;
}
test('both deployed Worker configs retain auth rate limiting and device relay', () => {
  assert.equal(root.main, 'cloudflare/' + field(backend, 'main'));
  assert.equal(root.name, field(backend, 'name'));
  assert.equal(root.compatibility_date, field(backend, 'compatibility_date'));
  assert.equal(root.vars?.CLOUD_VIP_ONLY, 'true', 'production must not silently disable VIP entitlement');
  assert.match(backend, /\[vars\]\s*CLOUD_VIP_ONLY\s*=\s*"true"/);

  for (const name of ['LOGIN_RATE_LIMITER', 'EXPORT_RATE_LIMITER']) {
    const config = root.ratelimits?.find(x => x.name === name);
    assert.ok(config, name + ' must survive root config deployment');
    const pattern = new RegExp('\\[\\[ratelimits\\]\\]\\s*name\\s*=\\s*"' + name +
      '"\\s*namespace_id\\s*=\\s*"([0-9]+)"\\s*\\[ratelimits.simple\\]\\s*limit\\s*=\\s*(\\d+)\\s*period\\s*=\\s*(\\d+)');
    const match = backend.match(pattern);
    assert.ok(match, 'missing TOML definition for ' + name);
    assert.equal(config.namespace_id, match[1]);
    assert.equal(config.simple.limit, Number(match[2]));
    assert.equal(config.simple.period, Number(match[3]));
  }
  assert.ok(root.durable_objects?.bindings?.some(x => x.name === 'DEVICE_RELAY' && x.class_name === 'DeviceRelay'));
  assert.match(backend, /\[\[durable_objects\.bindings\]\]\s*name\s*=\s*"DEVICE_RELAY"\s*class_name\s*=\s*"DeviceRelay"/);
  assert.deepEqual(root.triggers?.crons, ['*/30 * * * *']);
  assert.match(backend, /crons\s*=\s*\["\*\/30 \* \* \* \*"\]/);
  assert.equal(root.observability?.enabled, true);
  assert.equal(root.observability?.head_sampling_rate, 1);
  assert.equal(root.d1_databases?.[0]?.binding, 'DB');
  assert.equal(root.r2_buckets?.[0]?.binding, 'PACKAGES');
  assert.ok(root.routes?.some(x => x.pattern === 'sync.luoluoluo.cc.cd'));
});
