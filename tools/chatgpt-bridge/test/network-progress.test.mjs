import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,readFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {once} from 'node:events';
import {WebSocket} from 'ws';
import {createBridge} from '../server.mjs';
const wait=async(predicate,timeout=2500)=>{
  const end=Date.now()+timeout;
  while(Date.now()<end){
    if(await predicate())return;
    await new Promise(r=>setTimeout(r,12));
  }
  throw new Error('timed_out_in_test');
};
test('mid-flight network snapshots survive lost final reply without leaking secrets or retrying',async t=>{
  const dir=mkdtempSync(join(tmpdir(),'yanzi-midflight-'));
  t.after(()=>rmSync(dir,{recursive:true,force:true}));
  const b=await createBridge({directory:dir,port:0,allowTestSocket:true});
  t.after(()=>b.close());
  const headers={Authorization:'Bearer '+b.token,'X-Bridge-Request':'1','Content-Type':'application/json'};
  const api=async(path,method='GET',input)=>{
    const res=await fetch(b.origin+path,{method,headers,body:input?JSON.stringify(input):undefined});
    assert.ok(res.ok);
    return res.json();
  };
  const job=await api('/api/jobs','POST',{action:'chatgpt_send',prompt:'safe test',tabId:72,
    newChat:false,temporary:true,timeoutSeconds:10});
  const socket=new WebSocket(b.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(socket,'open');
  let dispatched=null;
  socket.on('message',raw=>{
    const payload=JSON.parse(raw.toString());
    if(payload.type==='task_request')dispatched=payload;
  });
  socket.send(JSON.stringify({type:'register',version:'test-network-observer'}));
  await wait(()=>dispatched?.taskId===job.id);
  // Deliberately do NOT return task_response: simulate a hung browser renderer.
  socket.send(JSON.stringify({type:'job_network_progress',taskId:job.id,
    stage:'page_dispatch_started',snapshot:{
      tabId:72,available:true,started:6,completed:3,failed:1,pending:2,
      methods:{POST:2,GET:4},classes:{conversation_api:2,page_resource:4},
      statusBands:{'2xx':3},url:'https://chatgpt.com/backend-api/foo?token=PRIVATE',
      authorization:'PRIVATE',body:'PRIVATE_CHAT'
    }}));
  await wait(async()=>Boolean((await api('/api/jobs/'+job.id)).networkObservation));
  const inFlight=await api('/api/jobs/'+job.id);
  assert.equal(inFlight.status,'running');
  assert.equal(inFlight.networkObservation.latest.started,6);
  const localState=readFileSync(join(dir,'state.json'),'utf8');
  assert.ok(localState.includes('conversation_api'));
  assert.ok(!localState.includes('PRIVATE'));
  const diagnosticJob=await api('/api/jobs','POST',{action:'chatgpt_status'});
  socket.send(JSON.stringify({type:'job_network_progress',taskId:diagnosticJob.id,
    stage:'invalid_network_stage',snapshot:{tabId:77,started:10}}));
  const unknown=await api('/api/jobs/'+diagnosticJob.id);
  assert.equal(unknown.networkObservation,undefined);
  socket.close();await once(socket,'close');
  await wait(async()=>(await api('/api/jobs/'+job.id)).status==='interrupted');
  const final=await api('/api/jobs/'+job.id);
  assert.equal(final.status,'interrupted');
  assert.equal(final.networkObservation.latest.classes.conversation_api,2);
  assert.equal(final.networkDiagnosis.status,'conversation_network_activity');
  assert.equal(final.networkDiagnosis.next,'read_only_reconcile');
  assert.equal(b.state.jobs.filter(j=>j.id===job.id).length,1);
  assert.ok(!readFileSync(join(dir,'state.json'),'utf8').includes('PRIVATE'));
});
