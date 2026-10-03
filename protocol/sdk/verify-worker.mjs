import {DeviceClient} from './device-client.mjs';
import {FileDeviceStore} from './file-device-store.mjs';
import {createHmac, randomUUID} from 'node:crypto';
import {mkdtemp, writeFile, readFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

// Local fixture only: no production credentials or account data.
const baseUrl = process.argv[2];
if (!/^http:\/\/127\.0\.0\.1:[0-9]+$/.test(baseUrl || '')) throw Error('isolated_worker_required');
const accountId = 'sdk-' + randomUUID(), source = 'web-' + randomUUID(), target = 'node-' + randomUUID();
const header = Buffer.from(JSON.stringify({alg:'HS256', typ:'JWT'})).toString('base64url');
const claims = Buffer.from(JSON.stringify({sub:accountId, username:'SDK fixture', exp:Math.floor(Date.now()/1000)+300})).toString('base64url');
const token = header + '.' + claims + '.' + createHmac('sha256', 'local-phone-message-verification').update(header+'.'+claims).digest('base64url');
async function owner(path, body) {
  const response = await fetch(baseUrl + path, {method:'POST', headers:{Authorization:'Bearer '+token, 'Content-Type':'application/json'}, body:JSON.stringify(body)});
  const result = await response.json(); if (!response.ok) throw Error(JSON.stringify(result)); return result;
}
const root = await mkdtemp(join(tmpdir(), 'yanzi-sdk-worker-')); const stores = [];
try {
  const store = new FileDeviceStore(join(root, 'sender')), receiving = new FileDeviceStore(join(root, 'receiver')); stores.push(store, receiving);
  const sender = new DeviceClient({baseUrl, accountId, deviceId:source, platform:'web', displayName:'Scoped web SDK', store, getToken:async()=>token});
  const receiverOptions = {baseUrl, accountId, deviceId:target, platform:'linux', displayName:'Native SDK adapter', store:receiving,
    capabilities:{receiveAccountChat:true}, getToken:async()=>token};
  const receiver = new DeviceClient(receiverOptions);
  await sender.register(); await receiver.register();
  const sendGrant = await owner('/v1/me/devices/'+source+'/credentials', {applicationId:'sdk-web', scopes:['capability.invoke:verify.write'], targetDeviceIds:[target]});
  const receiveGrant = await owner('/v1/me/devices/'+target+'/credentials', {applicationId:'sdk-adapter', scopes:['messages.receive', 'capability.invoke:verify.write']});
  sender.getToken = async()=>sendGrant.accessToken;
  let effects = 0;
  const execute = async message => {
    if (message.kind !== 'capability.invoke' || message.payload.name !== 'verify.write') throw Error('unsupported_capability');
    effects++; await writeFile(join(root, 'effect.txt'), 'SDK real capability result'); return {success:true, output:'written'};
  };
  const first = new DeviceClient({...receiverOptions, getToken:async()=>receiveGrant.accessToken, execute});
  const second = new DeviceClient({...receiverOptions, getToken:async()=>receiveGrant.accessToken, execute});
  const accepted = await sender.send({kind:'capability.invoke', targetDeviceId:target, payload:{name:'verify.write'}, clientMessageId:randomUUID()});
  if (accepted.state !== 'accepted') throw Error('send_not_accepted');
  await Promise.all([first.sync(), second.sync()]); await first.sync();
  const result = await sender.request('/v1/me/mobile/messages/'+accepted.messageId);
  if (effects !== 1 || result.status !== 'completed' || await readFile(join(root, 'effect.txt'), 'utf8') !== 'SDK real capability result') throw Error('effect_or_result_mismatch');
  const denied = await sender.send({kind:'run-shell', targetDeviceId:target, payload:{command:'must not run'}, clientMessageId:randomUUID()});
  if (denied.state !== 'failed' || denied.errorCode !== 'device_scope_denied' || effects !== 1) throw Error('scope_widening_accepted');
  console.log('GENERIC_SDK_WORKER_SCOPED_WEB_TO_NATIVE_REAL_CAPABILITY_AND_EXACTLY_ONCE_EFFECT=PASSED');
} finally { stores.forEach(store => store.close()); await rm(root, {recursive:true, force:true}); }
