import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { handleEnvironment, cleanupEnvironment, normalizeEnvironment } from './device-environment.js';
import { HttpError } from './http-error.js';
function fixture() {
  const db=new DatabaseSync(':memory:');
  const env={DB:{prepare(sql){return {bind(...args){this.args=args;return this;},async run(){return db.prepare(sql).run(...(this.args||[]));},async first(){return db.prepare(sql).get(...(this.args||[]))||null;}};}}};
  const api={normalizeDeviceId:id=>id,json:data=>Response.json(data),requireAuth:async request=>{
    const token=request.headers.get('Authorization');if(token==='Bearer grant')return {userId:'a',grant:{}};
    if(!['Bearer a','Bearer b'].includes(token))throw new HttpError(401,'unauthorized','Unauthorized');return {userId:token.slice(7)};},
    ensureOwnedDevice:async(_,user,device)=>{if(device!==user+'-phone')throw new HttpError(404,'device_not_found','Not owned');}};
  const request=(method,body,user='a',device=user+'-phone',extension='yanzi-location')=>handleEnvironment(new Request('https://fixture.invalid/v1/me/devices/'+device+'/environment/'+extension,{method,headers:{Authorization:'Bearer '+user},body:body?JSON.stringify(body):undefined}),env,api);
  return {db,env,request};
}
const sample=(sequence=1)=>({sequence,enabled:true,observedAt:new Date().toISOString(),place:'home',confidence:'high',network:{type:'wifi',wifiConnected:true,internetValidated:true}});
test('latest snapshot excludes undeclared coordinates, home coordinates and Wi-Fi identity',async()=>{
  const f=fixture();await f.request('PUT',{...sample(),location:{latitude:1,longitude:2,accuracy:3},homeLatitude:4,network:{type:'wifi',ssid:'private-home'}});
  const output=await (await f.request('GET')).json();assert.equal(output.exists,true);assert.equal(output.value.location,undefined);assert.equal(output.value.homeLatitude,undefined);assert.equal(output.value.network.ssid,undefined);
  await f.request('PUT',{...sample(2),shareCoordinates:true,location:{latitude:1,longitude:2,accuracy:3}});
  assert.equal((await (await f.request('GET')).json()).value.location.latitude,1);
  assert.equal(f.db.prepare('SELECT count(*) AS n FROM device_environment').get().n,1);f.db.close();
});
test('stop removes sensitive data and stale or changed duplicate requests cannot resurrect it',async()=>{
  const f=fixture();await f.request('PUT',{...sample(1),shareCoordinates:true,location:{latitude:1,longitude:2,accuracy:3}});
  await f.request('PUT',{enabled:false,sequence:2});await f.request('PUT',{enabled:false,sequence:2});
  await assert.rejects(f.request('PUT',sample(1)),e=>e.status===409);await assert.rejects(f.request('PUT',sample(2)),e=>e.status===409);
  assert.equal((await (await f.request('GET')).json()).exists,false);assert.equal(f.db.prepare('SELECT payload_json FROM device_environment').get().payload_json,'{"schemaVersion":1,"sequence":2,"enabled":false}');f.db.close();
});
test('account and device boundaries deny other accounts and externally scoped grants',async()=>{
  const f=fixture();await assert.rejects(f.request('PUT',sample(),'b','a-phone'),e=>e.status===404);
  await assert.rejects(f.request('GET',null,'grant','a-phone'),e=>e.status===403);await assert.rejects(f.request('GET',null,'missing'),e=>e.status===401);f.db.close();
});
test('expired states are unreadable and cron physically deletes them without racing fresh snapshots',async()=>{
  const f=fixture();await f.request('PUT',sample());f.db.prepare('UPDATE device_environment SET expires_at=0').run();
  assert.equal((await (await f.request('GET')).json()).exists,false);await f.request('PUT',sample(2));await cleanupEnvironment(f.env);
  assert.equal(f.db.prepare('SELECT sequence FROM device_environment').get().sequence,2);
  await cleanupEnvironment(f.env,Date.now()+86400001);assert.equal(f.db.prepare('SELECT count(*) AS n FROM device_environment').get().n,0);f.db.close();
});
test('bad sequences, stale/future samples, ambiguous places and invalid coordinates are rejected',()=>{
  for(const body of [{...sample(),sequence:0},{...sample(),observedAt:'1990-01-01'}, {...sample(),observedAt:new Date(Date.now()+600000).toISOString()}, {...sample(),place:'any'}, {...sample(),shareCoordinates:true,location:{latitude:91,longitude:1,accuracy:1}}])
    assert.throws(()=>normalizeEnvironment(body),e=>e.status===400);
});
test('oversized UTF-8 snapshots are rejected before storage',async()=>{
  const f=fixture();
  await assert.rejects(f.request('PUT',{...sample(),extra:'位置'.repeat(2000)}),e=>e.status===413);
  assert.equal(f.db.prepare('SELECT count(*) AS n FROM device_environment').get().n,0);
  f.db.close();
});
