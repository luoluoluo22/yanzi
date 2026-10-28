import test from 'node:test';
import assert from 'node:assert/strict';
import {accountLanLink} from './account-lan-links.js';
test('account links agree at both ends and isolate accounts, peers and server keys', async () => {
  const pc={device_id:'desktop-test',platform:'desktop',display_name:'PC',capabilities_json:'{"lanPort":42994}'};
  const phone={device_id:'android-test',platform:'android',display_name:'Phone',capabilities_json:'{"lanPort":42982}'};
  const first=await accountLanLink('fixture-secret','owner',pc,phone,1000);
  const reverse=await accountLanLink('fixture-secret','owner',phone,pc,1000);
  assert.equal(first.key,reverse.key); assert.equal(first.pairId,reverse.pairId);
  assert.equal(first.port,42982); assert.equal(reverse.port,42994);
  assert.notEqual(first.key,(await accountLanLink('fixture-secret','other-account',pc,phone)).key);
  assert.notEqual(first.key,(await accountLanLink('fixture-secret','owner',pc,{...phone,device_id:'other-phone'})).key);
  assert.notEqual(first.key,(await accountLanLink('rotated-secret','owner',pc,phone)).key);
  assert.equal(Buffer.from(first.key,'base64').length,32);
});
test('three phone installations keep independent bidirectional pairs and listener ports', async () => {
  const pc={device_id:'pc',platform:'desktop',capabilities_json:'{"lanPort":42980}'};
  const phones=[
    {device_id:'oneplus-release',platform:'android',capabilities_json:'{"lanPort":42981}'},
    {device_id:'oneplus-dev',platform:'android',capabilities_json:'{"lanPort":42982}'},
    {device_id:'redmi-release',platform:'android',capabilities_json:'{"lanPort":42981}'},
  ];
  const ids=new Set(), keys=new Set();
  for (const phone of phones) {
    const toPhone=await accountLanLink('fixture-secret','owner',pc,phone,1000);
    const toPc=await accountLanLink('fixture-secret','owner',phone,pc,1000);
    assert.equal(toPhone.pairId,toPc.pairId);
    assert.equal(toPhone.key,toPc.key);
    assert.equal(toPhone.deviceId,'pc'); assert.equal(toPhone.desktopDeviceId,phone.device_id);
    assert.equal(toPhone.port,phone.device_id.endsWith('dev')?42982:42981);
    ids.add(toPhone.pairId); keys.add(toPhone.key);
  }
  assert.equal(ids.size,3); assert.equal(keys.size,3);
});
