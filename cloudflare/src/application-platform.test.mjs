import test from 'node:test';
import assert from 'node:assert/strict';
import {webcrypto} from 'node:crypto';
import {readFile} from 'node:fs/promises';
if (!globalThis.crypto) Object.defineProperty(globalThis,'crypto',{value:webcrypto});
// Worker sources use .js without a package.json. Load this dependency-free module as ESM.
const source=await readFile(new URL('./application-platform.js',import.meta.url),'utf8');
const {handleApplicationPlatform,extensionObjectId}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
class HttpError extends Error{constructor(status,code,message){super(message);this.status=status;this.code=code;}}
function fixture(){
  const objects=new Map(),tokens=new Map();let revision=0;
  const api={HttpError,json:(data,status=200)=>new Response(JSON.stringify(data),{status}),
    requireAuth:async request=>{const token=request.headers.get('authorization');if(token!=='Bearer owner')throw new HttpError(401,'unauthorized','Unauthorized');return {userId:'account-a'};},
    verifyToken:async(env,token)=>{if(token==='owner')return {sub:'account-a',username:'owner'};if(!tokens.has(token))throw new HttpError(401,'unauthorized','Invalid token');return tokens.get(token);},
    signToken:async(env,claims)=>{const token=crypto.randomUUID();tokens.set(token,claims);return token;},
    read:async(env,user,id)=>objects.get(user+'/'+id)||null,
    write:async(env,user,id,input)=>{const old=objects.get(user+'/'+id);if((old?.revision||0)!==input.expectedRevision)throw new HttpError(409,'sync_revision_conflict','Conflict');const value={...input,revision:++revision};objects.set(user+'/'+id,value);return value;}};
  const env={PACKAGES:{get:async()=>null}};
  const request=(path,method='GET',body,token='owner')=>handleApplicationPlatform(new Request('https://test'+path,{method,headers:{authorization:'Bearer '+token},...(body?{body:JSON.stringify(body)}:{})}),env,api);
  return {request,api,env};
}
test('scoped grant reads only its extension, read-only cannot write, revoked grants fail',async()=>{
  const {request}=fixture();
  let r=await request('/v1/extension-data/quick-notes?key=notes.v1.json','PUT',{accountId:'account-a',expectedRevision:0,content:'hello'});assert.equal(r.status,200);
  r=await request('/v1/applications/quick-notes/grants','POST',{userConsent:true,clientName:'Web notes',access:'read'});
  const grant=await r.json();assert.equal((await (await request('/v1/extension-data/quick-notes?key=notes.v1.json','GET',null,grant.accessToken)).json()).content,'hello');
  await assert.rejects(request('/v1/extension-data/taskbar-calendar?key=notes.v1.json','GET',null,grant.accessToken),e=>e.status===403);
  await assert.rejects(request('/v1/extension-data/quick-notes?key=notes.v1.json','PUT',{accountId:'account-a',expectedRevision:1,content:'bad'},grant.accessToken),e=>e.status===403);
  await assert.rejects(request('/v1/applications/library','GET',null,grant.accessToken),e=>e.status===401);
  await request('/v1/applications/quick-notes/grants/'+grant.grantId,'DELETE');
  await assert.rejects(request('/v1/extension-data/quick-notes?key=notes.v1.json','GET',null,grant.accessToken),e=>e.status===403);
});
test('CAS and account isolation; library selected state is shared, not installation state',async()=>{
  const {request}=fixture();
  await assert.rejects(request('/v1/extension-data/quick-notes?key=data','PUT',{accountId:'account-b',expectedRevision:0,content:'x'}),e=>e.status===409);
  await request('/v1/applications/library/taskbar-calendar','PUT',{enabled:true,expectedRevision:0});
  const selection=(await (await request('/v1/applications/library')).json()).applications[0];
  assert.equal(selection.applicationId,'taskbar-calendar');assert.equal(selection.enabled,true);assert.equal(selection.installed,undefined);
  await assert.rejects(request('/v1/applications/library/taskbar-calendar','PUT',{enabled:false,expectedRevision:0}),e=>e.status===409);
  await request('/v1/extension-data/quick-notes?key=data','PUT',{accountId:'account-a',expectedRevision:0,content:'x'});
  await assert.rejects(request('/v1/extension-data/quick-notes?key=data','PUT',{accountId:'account-a',expectedRevision:0,content:'y'}),e=>e.status===409);
  assert.match(await extensionObjectId('quick-notes','data'),/^extensionData\.v1\.[a-f0-9]{64}$/);
});
