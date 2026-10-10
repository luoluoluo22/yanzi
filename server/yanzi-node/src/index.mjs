import http from 'node:http';
import os from 'node:os';
import {DeviceClient} from '../../../protocol/sdk/device-client.mjs';
import {AtomicJsonDeviceStore} from './store.mjs';
import {
  SERVER_CAPABILITY_PROTOCOL,
  ServerCapabilityRuntime,
  executeCapabilityMessage
} from './capabilities.mjs';
import {loadConfig} from './config.mjs';

const config = loadConfig();
const store = new AtomicJsonDeviceStore(config.stateDirectory);
const runtime = new ServerCapabilityRuntime({workspaceRoot: config.workspaceRoot});
let cloudClient = null;
let cloudState = config.cloudConfigured ? 'starting' : 'not-configured';
let lastCloudError = null;
let lastRegistrationAt = null;
let lastSyncAt = null;
let stopping = false;

function log(event, details = {}) {
  process.stdout.write(JSON.stringify({time: new Date().toISOString(), event, ...details}) + '\n');
}

function capabilityAdvertisement(stateSnapshot) {
  return {
    app: 'yanzi-server-node',
    os: os.platform() + ' ' + os.release(),
    serverCapabilityProtocol: SERVER_CAPABILITY_PROTOCOL,
    capabilityCatalog: runtime.catalog(),
    stateSnapshot,
    deviceMessageProtocolVersions: [1],
    receiveMobileMessages: true,
    receiveAccountChat: false,
    realtime: false
  };
}

async function registerCloud() {
  if (!cloudClient || stopping) return;
  try {
    cloudClient.capabilities = capabilityAdvertisement(await runtime.invoke('server.status.get'));
    await cloudClient.register();
    lastRegistrationAt = new Date().toISOString();
    cloudState = 'online';
    lastCloudError = null;
  } catch (error) {
    cloudState = 'degraded';
    lastCloudError = error.code || error.message;
    log('cloud_register_failed', {error: lastCloudError});
  }
}

async function syncCloud() {
  if (!cloudClient || stopping) return;
  try {
    await cloudClient.flush();
    await cloudClient.sync();
    lastSyncAt = new Date().toISOString();
    cloudState = 'online';
    lastCloudError = null;
  } catch (error) {
    cloudState = 'degraded';
    lastCloudError = error.code || error.message;
    log('cloud_sync_failed', {error: lastCloudError});
  }
}

if (config.cloudConfigured) {
  cloudClient = new DeviceClient({
    baseUrl: config.baseUrl,
    accountId: config.accountId,
    deviceId: config.deviceId,
    platform: 'server',
    displayName: config.displayName,
    capabilities: capabilityAdvertisement(await runtime.invoke('server.status.get')),
    store,
    getToken: async () => config.token,
    // Identify this server without weakening Cloudflare WAF protections.
    fetchImpl: (url, options) => fetch(url, {...options, headers: {...options.headers, 'User-Agent': 'YanziClient-Server/1.0', 'X-Yanzi-Client': 'server'}}),
    execute: message => executeCapabilityMessage(runtime, message)
  });

  await registerCloud();
  await syncCloud();
  setInterval(registerCloud, config.heartbeatMs).unref();
  setInterval(syncCloud, config.syncMs).unref();
}

const localServer = http.createServer(async (request, response) => {
  const url = new URL(request.url || '/', 'http://127.0.0.1');
  response.setHeader('Content-Type', 'application/json; charset=utf-8');

  try {
    if (request.method === 'GET' && url.pathname === '/health') {
      response.statusCode = 200;
      response.end(JSON.stringify({
        ok: true,
        service: 'yanzi-server-node',
        version: 1,
        deviceId: config.deviceId,
        displayName: config.displayName,
        cloudConfigured: config.cloudConfigured,
        cloudState,
        lastRegistrationAt,
        lastSyncAt,
        lastCloudError,
        capabilityCount: runtime.catalog().length
      }));
      return;
    }

    if (request.method === 'GET' && url.pathname === '/capabilities') {
      response.statusCode = 200;
      response.end(JSON.stringify({items: runtime.catalog()}));
      return;
    }

    if (request.method === 'POST' && url.pathname === '/invoke') {
      let body = '';
      for await (const chunk of request) {
        body += chunk;
        if (body.length > 65536) throw Object.assign(new Error('request_too_large'), {code: 'request_too_large'});
      }
      const input = body ? JSON.parse(body) : {};
      const result = await runtime.invoke(String(input.name || ''), input.arguments || {});
      response.statusCode = 200;
      response.end(JSON.stringify({ok: true, data: result}));
      return;
    }

    response.statusCode = 404;
    response.end(JSON.stringify({ok: false, error: 'not_found'}));
  } catch (error) {
    response.statusCode = 400;
    response.end(JSON.stringify({ok: false, error: error.code || 'invoke_failed', message: error.message}));
  }
});

localServer.listen(config.localPort, '127.0.0.1', () => {
  log('local_api_ready', {
    address: '127.0.0.1:' + config.localPort,
    cloudConfigured: config.cloudConfigured,
    capabilityCount: runtime.catalog().length
  });
});

async function shutdown(signal) {
  if (stopping) return;
  stopping = true;
  log('shutdown', {signal});
  await new Promise(resolve => localServer.close(resolve));
  await store.close();
  process.exit(0);
}

process.on('SIGTERM', () => void shutdown('SIGTERM'));
process.on('SIGINT', () => void shutdown('SIGINT'));
