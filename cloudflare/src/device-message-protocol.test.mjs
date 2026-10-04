import test from 'node:test';
import assert from 'node:assert/strict';
import { DEVICE_MESSAGE_PROTOCOL, acceptsAccountChat, isAccountChat,
  messageMatchesDevice, isExecutionMessage, canonicalMessageJson } from './device-message-protocol.js';
import { sendOfflinePush } from './mobile-push.js';
import { selectImplicitExecutionTarget } from './device-api.js';

test('an explicit device always wins over legacy account chat metadata', () => {
  const message = {sourceDeviceId:'phone-a',targetDeviceId:'desktop-a',targetPlatform:'android',payload:{accountChat:true}};
  assert.equal(isAccountChat(message),false);
  for (const platform of ['desktop','android','ios','web','iot']) {
    assert.equal(messageMatchesDevice(message,{deviceId:'desktop-a',platform}),true);
    assert.equal(messageMatchesDevice(message,{deviceId:'desktop-b',platform,capabilities:{receiveAccountChat:true}}),false);
  }
});
test('account chat excludes its sender and new device classes join by capability', () => {
  const message = {sourceDeviceId:'phone-a',payload:{accountChat:true}};
  assert.equal(messageMatchesDevice(message,{deviceId:'phone-a',platform:'android'}),false);
  assert.equal(messageMatchesDevice(message,{deviceId:'desktop-a',platform:'desktop'}),true);
  assert.equal(messageMatchesDevice(message,{deviceId:'panel-a',platform:'iot'}),false);
  assert.equal(messageMatchesDevice(message,{deviceId:'panel-a',platform:'iot',capabilities:{receiveAccountChat:true}}),true);
  assert.equal(acceptsAccountChat({platform:'iot',capabilities:{receiveAccountChat:'true'}}),false);
});
test('platform routing cannot override a device target; commands are identified separately', () => {
  const message = {targetPlatform:'iot',payload:{}};
  assert.equal(messageMatchesDevice(message,{deviceId:'panel-a',platform:'iot'}),true);
  assert.equal(messageMatchesDevice(message,{deviceId:'phone-a',platform:'android'}),false);
  for(const kind of ['run-shell','run-powershell','run-extension','fs-list','fs-write','capability.invoke'])
    assert.equal(isExecutionMessage(kind),true);
  for(const kind of ['text','photo','file','notify']) assert.equal(isExecutionMessage(kind),false);
});
test('implicit command routing prefers the only online desktop over stale registrations', () => {
  const current={device_id:'desktop-current',online:true};
  const stale={device_id:'desktop-stale',online:false};
  assert.equal(selectImplicitExecutionTarget([current,stale], x=>x.online)?.device_id,'desktop-current');
  assert.equal(selectImplicitExecutionTarget([current,{device_id:'desktop-other',online:true}], x=>x.online),null);
  assert.equal(selectImplicitExecutionTarget([stale], x=>x.online)?.device_id,'desktop-stale');
  assert.equal(selectImplicitExecutionTarget([stale,{device_id:'desktop-old',online:false}], x=>x.online),null);
});

test('idempotency ignores object key order but preserves values and array order', () => {
  assert.equal(canonicalMessageJson({b:{z:1,a:2},a:['x','y']}),canonicalMessageJson({a:['x','y'],b:{a:2,z:1}}));
  assert.notEqual(canonicalMessageJson({a:['x','y']}),canonicalMessageJson({a:['y','x']}));
  assert.notEqual(canonicalMessageJson({a:1}),canonicalMessageJson({a:'1'}));
  assert.deepEqual(DEVICE_MESSAGE_PROTOCOL.supportedVersions,[1]);
});
test('offline push uses the same explicit-device routing even for legacy chat records',async () => {
  const sent=[];
  const env={DB:{prepare:()=>({bind:()=>({all:async()=>({results:[
    {device_id:'phone-a',push_token:'a',capabilities_json:'{}'},
    {device_id:'phone-b',push_token:'b',capabilities_json:'{}'}]})})})},
    DEVICE_RELAY:{idFromName:user=>user,get:()=>({isConnected:async device=>{sent.push(device);return true;}})}};
  await sendOfflinePush(env,'user',{targetDeviceId:'phone-b',payload:{accountChat:true},messageId:'test'});
  assert.deepEqual(sent,['phone-b']);
});
