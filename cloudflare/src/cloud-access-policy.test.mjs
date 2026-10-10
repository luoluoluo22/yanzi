import test from 'node:test';
import assert from 'node:assert/strict';
import {isCloudServicePath, isVipActive, readVipEntitlement, passesLoginRateLimit} from './cloud-access-policy.js';
test('cloud entitlement applies to account-owned sync, messages and delegated access', () => {
  for (const p of ['/v1/sync','/v1/sync/objects','/v1/me/devices','/v1/me/messages','/v1/device/command','/v1/applications/access-requests','/v1/extension-data/notes','/v1/external-access'])
    assert.equal(isCloudServicePath(p),true,p);
});
test('account access, activation, health, downloads, and public resources stay reachable', () => {
  for (const p of ['/health','/downloads/app.zip','/v1/auth/login','/v1/auth/me','/v1/user/vip-status','/v1/licenses/redeem','/ai','/v1/wishes'])
    assert.equal(isCloudServicePath(p),false,p);
  assert.equal(isCloudServicePath('/v1/synced'),false);
});
test('VIP decisions recognize lifetime, valid expiry and expired or absent records', () => {
  const now = Date.parse('2026-10-11T00:00:00Z');
  assert.equal(isVipActive({vip_type:'lifetime'},now),true);
  assert.equal(isVipActive({vip_type:'month',vip_expire_at:'2026-10-12T00:00:00Z'},now),true);
  assert.equal(isVipActive({vip_type:'year',vip_expire_at:'2026-10-10T00:00:00Z'},now),false);
  assert.equal(isVipActive({vip_type:'month',vip_expire_at:'invalid'},now),false);
  assert.equal(isVipActive(null,now),false);
});

test('entitlement query is read-only, user scoped, and expiry sensitive', async () => {
  const calls=[];
  const db={prepare(sql){calls.push(sql);return {bind(id){calls.push(id);return {first:async()=>({vip_type:'month',vip_expire_at:'2026-10-12T00:00:00Z'})}}}}};
  const result=await readVipEntitlement(db,'account-a',Date.parse('2026-10-11T00:00:00Z'));
  assert.equal(result.active,true);
  assert.equal(calls.length,2);
  assert.match(calls[0],/^select vip_type, vip_expire_at from users where user_id = \?$/i);
  assert.equal(calls[1],'account-a');
});
test('per-identity login limiter denies requests before D1 and allows unbound local tests', async () => {
  let called=0;
  const limiter={limit:async ({key})=>{assert.equal(key,'anonymous-hmac');called++;return {success:false};}};
  assert.equal(await passesLoginRateLimit(limiter,'anonymous-hmac'),false);
  assert.equal(called,1);
  assert.equal(await passesLoginRateLimit(null,'anonymous-hmac'),true);
  assert.equal(await passesLoginRateLimit(limiter,''),true);
  assert.equal(called,1);
});
