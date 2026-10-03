import test from 'node:test';
import assert from 'node:assert/strict';
import {webcrypto} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import {readFile} from 'node:fs/promises';
import {handleExternalAccess} from './external-access.js';
import {handleRecordApi} from './record-api.js';
import {handleApplicationPlatform} from './application-platform.js';
globalThis.crypto??=webcrypto;
const migration=await readFile(new URL('../migrations/0020_external_access.sql',import.meta.url),'utf8');
const scopeMigration=await readFile(new URL('../migrations/0021_external_access_scopes.sql',import.meta.url),'utf8');
const profile=JSON.parse(await readFile(new URL('../tests/fixtures/calendar-record-schema.json',import.meta.url),'utf8'));
class HttpError extends Error {constructor(status,code,message){super(message);this.status=status;this.code=code;}}
function fixture(){
  const db=new DatabaseSync(':memory:');db.exec(migration);db.exec(scopeMigration);
  db.exec('CREATE TABLE user_sync_objects(user_id TEXT,object_id TEXT,deleted INTEGER,payload_json TEXT)');
  db.prepare('INSERT INTO user_sync_objects VALUES(?,?,0,?)').run('owner-account','extensionData.v1.notes',JSON.stringify({extensionId:'quick-notes',key:'notes.v1.json'}));
  const objects=new Map(),tokens=new Map();let revision=0;
  const env={DB:{prepare(sql){const statement=db.prepare(sql);let params=[];return{bind(...p){params=p;return this;},async first(){return statement.get(...params)||null;},async all(){return{results:statement.all(...params)};},async run(){return{meta:statement.run(...params)};}}},async batch(statements){return Promise.all(statements.map(s=>s.run()));}},
    PACKAGES:{get:async()=>({size:4096,text:async()=>JSON.stringify({applications:[{applicationId:'taskbar-calendar',apiSchema:profile}]})})}};
  const api={HttpError,json:(data,status=200)=>Response.json(data,{status}),
    requireAuth:async request=>{if(request.headers.get('authorization')!=='Bearer owner')throw new HttpError(401,'unauthorized','Owner required');return{userId:'owner-account'};},
    verifyToken:async(_,token)=>{if(token==='owner')return{sub:'owner-account',username:'owner'};const claims=tokens.get(token);if(!claims)throw new HttpError(401,'unauthorized','Invalid token');return claims;},
    signToken:async(_,claims)=>{const token=crypto.randomUUID();tokens.set(token,claims);return token;},
    read:async(_,user,id)=>objects.get(user+'/'+id)||null,
    write:async(_,user,id,input)=>{const old=objects.get(user+'/'+id);if((old?.revision||0)!==input.expectedRevision)throw new HttpError(409,'conflict','CAS');const result=structuredClone({...input,revision:++revision});objects.set(user+'/'+id,result);return result;}};
  async function request(path,method='GET',body,token){try{
    const req=new Request(path.startsWith('https:')?path:'https://test'+path,{method,headers:token?{authorization:'Bearer '+token}:{},...(body?{body:JSON.stringify(body)}:{})});
    return await handleExternalAccess(req,env,api)||await handleRecordApi(req,env,api)||await handleApplicationPlatform(req,env,api);
  }catch(e){if(e.status)return Response.json({error:e.code},{status:e.status});throw e;}}
  return{request,db,objects,tokens};
}
async function invite(f,access='read-write'){const result=await f.request('/v1/applications/access-invites','POST',{extensionId:'taskbar-calendar',key:'calendar.v1.json',access},'owner');assert.equal(result.status,200);return(await result.json()).address;}
async function pending(f,address,access='read-write'){const result=await f.request(address+'/requests','POST',{clientName:'Test AI',access});assert.equal(result.status,202);return result.json();}
async function approve(f,p){assert.equal((await f.request('/v1/applications/access-requests/'+p.requestId+'/decision','POST',{approve:true},'owner')).status,200);const result=await f.request(p.poll.url,'GET',null,p.requestSecret);assert.equal(result.status,200);return result.json();}
test('discovery requires no credentials and never creates requests; private polling, owner-only consent, first device wins',async()=>{
  const f=fixture(),address=await invite(f);assert.equal((await f.request(address)).status,200);assert.equal(f.db.prepare('SELECT count(*) AS n FROM external_access_requests').get().n,0);
  const p=await pending(f,address);
  assert.equal((await f.request(p.poll.url)).status,401);
  assert.equal((await f.request(p.poll.url,'GET',null,'a'.repeat(64))).status,404);
  assert.equal((await f.request('/v1/applications/access-requests/'+p.requestId+'/decision','POST',{approve:true})).status,401);
  assert.equal((await f.request(p.poll.url,'GET',null,p.requestSecret)).status,202);
  assert.equal((await f.request(p.poll.url,'GET',null,p.requestSecret)).status,429);
  f.db.prepare('UPDATE external_access_requests SET last_polled_at=0').run();
  const approved=await approve(f,p);assert.equal(approved.accessToken.includes('owner'),false);assert.equal(approved.access,'read-write');
  assert.equal((await f.request('/v1/applications/access-requests/'+p.requestId+'/decision','POST',{approve:false},'owner')).status,409);
  assert.equal((await f.request('/v1/extension-data/taskbar-calendar?key=other.json','GET',null,approved.accessToken)).status,403);
  assert.equal((await f.request('/v1/extension-data/quick-notes?key=calendar.v1.json','GET',null,approved.accessToken)).status,403);
});
test('calendar CRUD shares legacy document, checks per-record versions and leaves deletion tombstones',async()=>{
  const f=fixture(),p=await pending(f,await invite(f)),grant=await approve(f,p),token=grant.accessToken,url=grant.data.records;
  let r=await f.request(url,'POST',{id:'test-calendar',title:'Public API test',date:'2026-10-01'},token);assert.equal(r.status,201);const created=(await r.json()).record;
  const document=await(await f.request(grant.data.document,'GET',null,token)).json();const stored=JSON.parse(document.content).records[created.id];assert.equal(stored.item.Id,created.id);assert.equal(stored.item.TargetDate,'2026-10-01T00:00:00');
  const itemUrl=url.replace('/records?','/records/'+created.id+'?');
  assert.equal((await f.request(itemUrl,'PATCH',{title:'blind overwrite'},token)).status,409);
  r=await f.request(itemUrl,'PATCH',{expectedVersion:created.version,title:'Edited'},token);assert.equal(r.status,200);const edited=(await r.json()).record;
  assert.equal((await f.request(itemUrl,'DELETE',{expectedVersion:created.version},token)).status,409);
  assert.equal((await f.request(itemUrl,'DELETE',{expectedVersion:edited.version},token)).status,200);
  assert.equal((await f.request(itemUrl,'GET',null,token)).status,404);
  assert.equal(JSON.parse((await(await f.request(grant.data.document,'GET',null,token)).json()).content).records[created.id].deleted,true);
});
test('read-only, rejected, expired, revoked invitations and grants cannot confer write permission',async()=>{
  const f=fixture(),address=await invite(f,'read');assert.equal((await f.request(address+'/requests','POST',{clientName:'AI',access:'read-write'})).status,403);
  const p=await pending(f,address,'read'),grant=await approve(f,p);
  assert.equal((await f.request(grant.data.records,'POST',{title:'no',date:'2026-10-01'},grant.accessToken)).status,403);
  assert.equal((await f.request('/v1/applications/taskbar-calendar/grants/'+p.requestId,'DELETE',null,'owner')).status,200);
  assert.equal((await f.request(grant.data.records,'GET',null,grant.accessToken)).status,403);
  const denied=await pending(f,address,'read');await f.request('/v1/applications/access-requests/'+denied.requestId+'/decision','POST',{approve:false},'owner');assert.equal((await f.request(denied.poll.url,'GET',null,denied.requestSecret)).status,403);
  const expired=await pending(f,address,'read');f.db.prepare('UPDATE external_access_requests SET expires_at=0 WHERE request_id=?').run(expired.requestId);assert.equal((await f.request(expired.poll.url,'GET',null,expired.requestSecret)).status,410);
  const raw=address.split('/').at(-1);await f.request('/v1/applications/access-invites/'+raw,'DELETE',null,'owner');assert.equal((await f.request(address)).status,404);
});
test('list metadata, request subset or all snapshot; approval may narrow permissions; unselected and future resources denied',async()=>{
  const f=fixture();const inventory=await(await f.request('/v1/applications/access-resources','GET',null,'owner')).json();assert.equal(inventory.resources.length,2);assert.equal(JSON.stringify(inventory).includes('content'),false);
  const invitation=await(await f.request('/v1/applications/access-invites','POST',{resources:'all',access:'read-write'},'owner')).json();
  const list=await(await f.request(invitation.address+'/resources')).json();assert.equal(list.resources.length,2);
  let r=await f.request(invitation.address+'/requests','POST',{clientName:'AI',scopes:[{extensionId:'private',key:'secret'}]});assert.equal(r.status,403);
  r=await f.request(invitation.address+'/requests','POST',{clientName:'AI',scopes:'all'});assert.equal(r.status,202);const p=await r.json();
  const decision='/v1/applications/access-requests/'+p.requestId+'/decision';assert.equal((await f.request(decision,'POST',{approve:true},'owner')).status,400);
  const allowed=[{extensionId:'taskbar-calendar',key:'calendar.v1.json',access:'read'}];assert.equal((await f.request(decision,'POST',{approve:true,scopes:allowed},'owner')).status,200);
  const grant=await(await f.request(p.poll.url,'GET',null,p.requestSecret)).json();assert.equal(grant.resources.length,1);assert.equal(grant.resources[0].access,'read');
  assert.equal((await f.request(grant.resources[0].data.records,'GET',null,grant.accessToken)).status,200);
  assert.equal((await f.request(grant.resources[0].data.records,'POST',{title:'no',date:'2026-10-01'},grant.accessToken)).status,403);
  assert.equal((await f.request('/v1/extension-data/quick-notes?key=notes.v1.json','GET',null,grant.accessToken)).status,403);
  assert.equal((await f.request('/v1/extension-data/new-app?key=future.json','GET',null,grant.accessToken)).status,403);
  const grants=await(await f.request('/v1/applications/access-grants','GET',null,'owner')).json();assert.equal(grants.grants.length,1);
  assert.equal((await f.request('/v1/applications/access-grants/'+p.requestId,'DELETE',null,'owner')).status,200);
  assert.equal((await f.request(grant.resources[0].data.records,'GET',null,grant.accessToken)).status,403);
  r=await f.request(invitation.address+'/requests','POST',{clientName:'Subset AI',scopes:allowed});assert.equal(r.status,202);
  const rows=await(await f.request('/v1/applications/access-requests','GET',null,'owner')).json();assert.equal(rows.requests[0].scopes.length,1);
});
