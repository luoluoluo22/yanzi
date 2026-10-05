import assert from 'node:assert/strict';
import {mkdtempSync, readFileSync, statSync, writeFileSync} from 'node:fs';
import os from 'node:os';
import {join} from 'node:path';
import test from 'node:test';
import {
  ServerCapabilityRuntime,
  executeCapabilityMessage
} from '../src/capabilities.mjs';

function fixture() {
  const root = mkdtempSync(join(os.tmpdir(), 'yanzi-server-node-'));
  const runtime = new ServerCapabilityRuntime({workspaceRoot: root});
  return {root, runtime};
}

test('status exposes bounded host and workspace information', async () => {
  const {runtime} = fixture();
  const status = await runtime.invoke('server.status.get');
  assert.equal(status.schemaVersion, 1);
  assert.ok(status.host.hostname);
  assert.ok(status.cpuCount >= 1);
  assert.ok(status.memory.totalBytes > 0);
  assert.ok(status.workspace.totalBytes > 0);
});

test('workspace list and SHA-256 hash are deterministic', async () => {
  const {root, runtime} = fixture();
  writeFileSync(join(root, 'hello.txt'), 'hello\n');
  const listed = await runtime.invoke('server.files.list', {path: '.'});
  assert.equal(listed.items.some(item => item.name === 'hello.txt' && item.type === 'file'), true);

  const hashed = await runtime.invoke('server.files.hash', {path: 'hello.txt'});
  assert.equal(hashed.sha256, '5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03');
  assert.equal(hashed.size, 6);
});

test('workspace traversal is rejected', async () => {
  const {runtime} = fixture();
  await assert.rejects(() => runtime.invoke('server.files.hash', {path: '../etc/passwd'}));
  await assert.rejects(() => runtime.invoke('server.files.hash', {path: '/etc/passwd'}));
});

test('archive creation stays inside workspace', async () => {
  const {root, runtime} = fixture();
  writeFileSync(join(root, 'payload.txt'), 'archive me');
  const result = await runtime.invoke('server.archive.create', {
    source: 'payload.txt',
    output: 'archives/payload.tar.gz'
  });
  assert.equal(result.output, 'archives/payload.tar.gz');
  assert.ok(statSync(join(root, result.output)).size > 0);
});

test('device execution requires the matching scope', async () => {
  const {root, runtime} = fixture();
  writeFileSync(join(root, 'scope.txt'), 'scoped');

  const allowed = await executeCapabilityMessage(runtime, {
    kind: 'capability.invoke',
    payload: {
      name: 'server.files.hash',
      arguments: {path: 'scope.txt'},
      authorization: {
        type: 'device-grant',
        scopes: ['capability.invoke:server.files.hash']
      }
    }
  });
  assert.equal(allowed.success, true);
  assert.match(allowed.output, /sha256/);

  await assert.rejects(() => executeCapabilityMessage(runtime, {
    kind: 'capability.invoke',
    payload: {
      name: 'server.files.hash',
      arguments: {path: 'scope.txt'},
      authorization: {type: 'device-grant', scopes: []}
    }
  }), /capability_scope_denied/);
});
