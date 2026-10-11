import test from 'node:test';
import assert from 'node:assert/strict';
import {webcrypto,createHmac} from 'node:crypto';
import {register} from 'node:module';
if(!globalThis.crypto) Object.defineProperty(globalThis,'crypto',{value:webcrypto});
register(new URL('./vip-cloud-worker.loader.mjs',import.meta.url));
const {default:worker}=await import('../src/index.js');
const signingKey='integration-fixture-secret';
const jsonBase64=value=>Buffer.from(JSON.stringify(value)).toString('base64url');
function bearer(account){
 const body=jsonBase64({alg:'HS256',typ:'JWT'})+'.'+jsonBase64({sub:account,username:'fixture',exp:Math.floor(Date.now()/1000)+600});
 return body+'.'+createHmac('sha256',signingKey).update(body).digest('base64url');
}
function fixture(membership){
 const queries=[];
 const DB={prepare(sql){
   queries.push(sql);
   const first=async()=>{
     if(/select\s+vip_type,\s*vip_expire_at\s+from users/i.test(sql))return membership;
     if(/from user_sync_revisions/i.test(sql))return {revision:0};
     if(/sqlite_master/i.test(sql))return {name:'user_sync_objects'};
     throw new Error('Unexpected SQL in VIP worker integration: '+sql.slice(0,95));
   };
   return {first,bind(...params){return {first,all:async()=>({results:[]})}}};
 }};
 const env={AUTH_TOKEN_SECRET:signingKey,CLOUD_VIP_ONLY:'true',DB,PACKAGES:{get:async()=>null}};
 const request=(path,account=null,options={})=>worker.fetch(new Request('https://unit.invalid'+path,{
   ...options,headers:{...(account?{authorization:'Bearer '+bearer(account)}:{}),...options.headers}
 }),env,{waitUntil(){}});
 return {request,queries,env};
}
test('anonymous cloud catalogue/wish/API denied before any D1 reads',async()=>{
 const f=fixture(null);
 for(const path of ['/v1/extensions','/v1/wishes','/v1/wishes/leaderboard','/v1/applications/catalog','/v1/debug/list-packages','/v1/sync/capabilities','/downloads/applications/test.zip']){
   const response=await f.request(path);
   assert.equal(response.status,401,path);
 }
 assert.deepEqual(f.queries,[]);
});
test('expired VIP is rejected before sync queries; free membership and historical export remain usable',async()=>{
 const f=fixture({vip_type:'year',vip_expire_at:'2025-01-01T00:00:00Z'});
 const denied=await f.request('/v1/sync/capabilities','expired-fixture');
 assert.equal(denied.status,403);
 const deniedJson=await denied.json();
 assert.equal(deniedJson.error,'vip_required');
 assert.equal(deniedJson.details.exportPath,'/v1/user/cloud-export');
 const info=await f.request('/v1/user/cloud-access','expired-fixture');
 assert.equal(info.status,200);
 assert.equal((await info.json()).canUseCloud,false);
 const exported=await f.request('/v1/user/cloud-export?cursor=0','expired-fixture');
 assert.equal(exported.status,200);
 const body=await exported.json();
 assert.equal(body.format,'yanzi-cloud-sync-objects-v1');
 assert.deepEqual(body.objects,[]);
 assert.equal(body.hasMore,false);
 assert.equal(f.queries.some(sql=>/update\s+|insert\s+|delete\s+/i.test(sql)),false);
});
test('expired member account-me avoids wish-wall D1 writes during VIP-only service',async()=>{
 const f=fixture({vip_type:'year',vip_expire_at:'2025-01-01T00:00:00Z'});
 const response=await f.request('/v1/auth/me','free-auth-fixture');
 assert.equal(response.status,200);
 const info=await response.json();
 assert.equal(info.isVip,false);
 assert.equal(info.points,0);
 assert.equal(f.queries.length,1);
 assert.equal(f.queries.some(sql=>/create\s+table|insert\s+into|user_points/i.test(sql)),false);
});
test('lifetime VIP can access the cloud and membership discovery',async()=>{
 const f=fixture({vip_type:'lifetime',vip_expire_at:null});
 const sync=await f.request('/v1/sync/capabilities','lifetime-fixture');
 assert.equal(sync.status,200);
 const status=await f.request('/v1/user/cloud-access','lifetime-fixture');
 assert.equal(status.status,200);
 assert.equal((await status.json()).canUseCloud,true);
});
test('login limiter rejects before auth-users D1 query and gives retry-after',async()=>{
 const f=fixture(null);
 f.env.LOGIN_RATE_LIMITER={limit:async()=>({success:false})};
 const response=await f.request('/v1/auth/login',null,{
   method:'POST',headers:{'content-type':'application/json'},
   body:JSON.stringify({email:'account@example.invalid',password:'synthetic-1234'})
 });
 assert.equal(response.status,429);
 assert.equal(response.headers.get('retry-after'),'60');
 assert.deepEqual(f.queries,[]);
});
test('health, public upgrade and Android update bundle discovery do not query D1',async()=>{
 const f=fixture(null);
 const health=await f.request('/health');
 assert.equal(health.status,200);
 assert.equal((await health.json()).ok,true);
 const upgrade=await f.request('/upgrade');
 assert.equal(upgrade.status,200);
 assert.match(await upgrade.text(),/VIP/);
 const publicRelease=await f.request('/downloads/android/releases.json');
 assert.equal(publicRelease.status,404);
 assert.deepEqual(f.queries,[]);
});
