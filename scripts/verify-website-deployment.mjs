import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
const parse = (path) => JSON.parse(readFileSync(resolve(root, path), 'utf8'));

test('public website is deployed as static assets on its own Worker', () => {
  const site = parse('website/wrangler.jsonc');
  assert.equal(site.name, 'yanzi');
  assert.equal(site.assets?.directory, './');
  assert.equal(site.main, undefined, 'website must not bundle the sync API');
  assert.equal(site.d1_databases, undefined, 'website must not bind the sync database');
  assert.equal(site.durable_objects, undefined, 'website must not bind sync device relays');
  for (const file of ['index.html', 'store.html', 'robots.txt', 'assets/auth.js', 'docs/getting-started.html']) {
    assert.ok(existsSync(resolve(root, 'website', file)), `missing public site asset: ${file}`);
  }
});

test('the sync Worker and public Worker cannot accidentally share a deployment', () => {
  const sync = parse('wrangler.jsonc');
  assert.equal(sync.name, 'yanzi-sync');
  assert.equal(sync.main, 'cloudflare/src/index.js');
  assert.equal(sync.routes?.[0]?.pattern, 'sync.luoluoluo.cc.cd');
  assert.ok(!sync.routes?.some((r) => r.pattern.includes('yanzi.luoluoluo.cc.cd')));
});

test('deployment internals cannot be served as public static files', () => {
  const ignore = readFileSync(resolve(root, 'website/.assetsignore'), 'utf8').split(/\r?\n/);
  for (const pattern of ['.wrangler/', 'wrangler.jsonc', '.assetsignore']) {
    assert.ok(ignore.includes(pattern), `missing required static asset exclusion: ${pattern}`);
  }
});
