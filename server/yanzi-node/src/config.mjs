import {existsSync, mkdirSync, readFileSync, writeFileSync, chmodSync} from 'node:fs';
import {join, resolve} from 'node:path';
import {randomUUID} from 'node:crypto';
import os from 'node:os';

function text(name, fallback = '') {
  const value = String(process.env[name] ?? fallback).trim();
  return value;
}

function number(name, fallback) {
  const value = Number.parseInt(text(name, String(fallback)), 10);
  return Number.isFinite(value) ? value : fallback;
}

export function loadConfig() {
  const stateDirectory = resolve(text('YANZI_SERVER_STATE_DIR', '/var/lib/yanzi-server-node'));
  const workspaceRoot = resolve(text('YANZI_SERVER_WORKSPACE', '/srv/yanzi-workspace'));
  mkdirSync(stateDirectory, {recursive: true, mode: 0o700});
  if (process.platform !== 'win32') chmodSync(stateDirectory, 0o700);

  const deviceIdFile = join(stateDirectory, 'device-id');
  let deviceId;
  if (existsSync(deviceIdFile)) {
    deviceId = readFileSync(deviceIdFile, 'utf8').trim();
  } else {
    deviceId = 'server-' + randomUUID();
    writeFileSync(deviceIdFile, deviceId + '\n', {encoding: 'utf8', mode: 0o600});
    if (process.platform !== 'win32') chmodSync(deviceIdFile, 0o600);
  }

  const baseUrl = text('YANZI_CLOUD_BASE_URL');
  const accountId = text('YANZI_ACCOUNT_ID');
  const token = text('YANZI_DEVICE_TOKEN');
  const configuredCount = [baseUrl, accountId, token].filter(Boolean).length;
  if (configuredCount !== 0 && configuredCount !== 3) {
    throw new Error('YANZI_CLOUD_BASE_URL, YANZI_ACCOUNT_ID and YANZI_DEVICE_TOKEN must be configured together');
  }

  return {
    stateDirectory,
    workspaceRoot,
    deviceId,
    displayName: text('YANZI_SERVER_DISPLAY_NAME', '燕子云服务器 · ' + os.hostname()),
    baseUrl,
    accountId,
    token,
    cloudConfigured: configuredCount === 3,
    heartbeatMs: Math.max(30000, number('YANZI_SERVER_HEARTBEAT_MS', 60000)),
    syncMs: Math.max(5000, number('YANZI_SERVER_SYNC_MS', 30000)),
    localPort: number('YANZI_SERVER_LOCAL_PORT', 8789)
  };
}
