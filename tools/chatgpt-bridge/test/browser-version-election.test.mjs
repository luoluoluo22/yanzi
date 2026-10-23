import {test} from 'node:test';
import assert from 'node:assert/strict';
import {WebSocket} from 'ws';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {once} from 'node:events';
import {createBridge} from '../server.mjs';
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const origin='chrome-extension://hbbaogocjdkfbffpcoiaacdjioamjfef';
async function waitFor(fn,timeout=2200){
 const deadline=Date.now()+timeout;
 while(Date.now()<deadline){if(await fn())return;await delay(20)}
 throw Error('condition timeout');
}
test('same-ID Ego newest extension takes idle Bridge from old Edge, then cannot be downgraded',async t=>{
 const bridge=await createBridge({port:0,allowTestSocket:true,directory:mkdtempSync(join(tmpdir(),'yanzi-sameid-'))});
 t.after(()=>bridge.close());
 const wsurl=bridge.origin.replace('http:','ws:')+'/v1/browser/ws';
 async function newClient(version){
  const client=new WebSocket(wsurl,{origin});await once(client,'open');
  client.send(JSON.stringify({type:'register',version}));
  t.after(()=>client.close());
  return client;
 }
 const health=async()=>await(await fetch(bridge.origin+'/health')).json();
 const older=await newClient('0.5.74');
 await waitFor(async()=>(await health()).extensionVersion==='0.5.74');
 const newer=await newClient('0.5.79');
 await waitFor(async()=>(await health()).extensionVersion==='0.5.79');
 assert.equal((await health()).connected,true);
 const stale=await newClient('0.5.74');
 await once(stale,'close');
 await delay(40);
 assert.equal((await health()).extensionVersion,'0.5.79');
 assert.equal((await health()).connected,true);
 assert.equal(newer.readyState,WebSocket.OPEN);
});
