import {readFileSync} from 'node:fs';
import {join} from 'node:path';
import {randomUUID} from 'node:crypto';
import {DeviceClient} from '../../../protocol/sdk/device-client.mjs';
import {AtomicJsonDeviceStore} from './store.mjs';
import {loadConfig} from './config.mjs';

// Run as the yanzi-server user with a scoped device credential loaded by systemd.
// Read the message from stdin so its contents never appear in process arguments.
const config = loadConfig();
const targetDeviceId = String(process.env.YANZI_MESSAGE_TARGET_DEVICE_ID || '').trim();
const text = readFileSync(0, 'utf8').trim();
const clientMessageId = String(process.env.YANZI_MESSAGE_CLIENT_ID || randomUUID()).trim();

if (!config.cloudConfigured) throw new Error('server_cloud_not_configured');
if (!/^android-[a-f0-9-]{36}$/.test(targetDeviceId)) throw new Error('android_target_not_configured');
if (!text || Buffer.byteLength(text) > 24000) throw new Error('invalid_message_length');
if (!/^[A-Za-z0-9_-]{1,128}$/.test(clientMessageId)) throw new Error('invalid_message_client_id');

// This outbox is separate from the running server node's durable inbox and outbox.
const store = new AtomicJsonDeviceStore(join(config.stateDirectory, 'notification-outbox'));
try {
  const client = new DeviceClient({
    baseUrl: config.baseUrl,
    accountId: config.accountId,
    deviceId: config.deviceId,
    platform: 'server',
    displayName: config.displayName,
    store,
    getToken: async () => config.token,
    fetchImpl: (url, options) => fetch(url, {
      ...options,
      headers: {...options.headers, 'User-Agent': 'YanziClient-Server/1.0', 'X-Yanzi-Client': 'server'}
    })
  });
  const sent = await client.send({
    kind: 'text',
    title: 'YanziChat',
    text,
    targetDeviceId,
    targetPlatform: 'android',
    clientMessageId,
    payload: {source: 'server', purpose: 'notification'}
  });
  if (sent.state !== 'accepted' || !sent.messageId) throw new Error('cloud_send_' + (sent.errorCode || sent.state));
  const status = await client.request('/v1/me/mobile/messages/' + encodeURIComponent(sent.messageId));
  console.log(JSON.stringify({ok: true, messageId: sent.messageId, cloudStatus: status.status || 'unknown', targetDeviceId}));
} finally {
  await store.close();
}
