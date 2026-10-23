import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {once} from 'node:events';
import {WebSocket} from 'ws';
import {createBridge} from '../server.mjs';
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const parentId='01234567-1234-4678-8abc-123456789001';
const childId='01234567-1234-4678-8abc-123456789002';
const parentUrl='https://chatgpt.com/c/'+parentId;
const childUrl='https://chatgpt.com/c/'+childId;
async function setup(t) {
 let now=Date.now();
 const bridge=await createBridge({port:0,directory:mkdtempSync(join(tmpdir(),'longrun-reconcile-')),
   allowTestSocket:true,now:()=>now,jobTimeoutMs:task=>task.action==='chatgpt_subagent_continue'?140:5000});
 t.after(()=>bridge.close());
 const headers={'Authorization':'Bearer '+bridge.token,'Content-Type':'application/json','X-Bridge-Request':'1'};
 async function api(path,method='GET',body) {
  const res=await fetch(bridge.origin+path,{method,headers,body:body?JSON.stringify(body):undefined});
  return {status:res.status,data:await res.json()};
 }
 const socket=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
 await once(socket,'open');t.after(()=>socket.close());
 const messages=[];socket.on('message',raw=>messages.push(JSON.parse(raw.toString())));
 socket.send(JSON.stringify({type:'register',version:'0.5.76-test'}));
 async function waitFor(match) {
  for(let n=0;n<200;n++){const found=messages.find(match);if(found)return found;await delay(10)}
  throw new Error('waitFor: missing request '+JSON.stringify(messages.map(x=>x.action||x.type)));
 }
 function ack(job,url,id,text) {
  socket.send(JSON.stringify({type:'task_response',taskId:job.taskId,status:'success',
   data:{url,conversationId:id,tabId:77,temporary:false,text}}));
 }
 return {bridge,api,socket,messages,waitFor,ack,setNow:value=>now=value,getNow:()=>now};
}
async function createParentAndChild(h) {
 const parent=(await h.api('/api/managed-parents','POST',{
  requestKey:'longrun-parent-'+Date.now(),prompt:'主管等待回流'})).data;
 const pJob=await h.waitFor(x=>x.type==='task_request'&&x.taskId===parent.initialJobId);
 h.ack(pJob,parentUrl,parentId,'主管就绪');
 for(let i=0;i<100;i++){const p=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(p.status==='ready')break;await delay(10);}
 const child=(await h.api('/api/subagents','POST',{
  requestKey:'longrun-child-'+Date.now(),prompt:'只回复 LONGRUN_R1_GREEN',parentManagedId:parent.id})).data;
 const cJob=await h.waitFor(x=>x.type==='task_request'&&x.taskId===child.initialJobId);
 assert.equal(cJob.closeAfter,true,'completed child tabs should be closed by the browser extension');
 h.ack(cJob,childUrl,childId,'LONGRUN_R1_GREEN');
 const returnJob=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue');
 return {parent,child,returnJob};
}
test('realistically timeout after send then verify original conversation and resume dispatch without replay',async t=>{
 const h=await setup(t);
 const {parent,child,returnJob}=await createParentAndChild(h);
 const verification=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_verify_delivery');
 assert.equal(verification.expectedUrl,parentUrl);
 assert.equal(verification.marker,child.initialJobId);
 assert.equal(verification.action,'chatgpt_verify_delivery');
 assert.equal(verification.newChat,false);
 const pBefore=(await h.api('/api/managed-parents/'+parent.id)).data;
 assert.equal(pBefore.status,'needs_review');
 assert.equal(pBefore.deliveries[0].status,'timeout');
 // The original write timed out, but the original conversation now contains
 // the exact delivery marker followed by a completed supervisor reply.
 h.socket.send(JSON.stringify({type:'task_response',taskId:verification.taskId,status:'success',data:{
  url:parentUrl,conversationId:parentId,marker:child.initialJobId,
  verified:true,observed:'completed',replyText:'已收到 LONGRUN_R1_GREEN',readOnly:true
 }}));
 for(let i=0;i<100;i++){const p=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(p.status==='ready')break;await delay(10)}
 const pAfter=(await h.api('/api/managed-parents/'+parent.id)).data;
 assert.equal(pAfter.status,'ready');
 assert.equal(pAfter.deliveries[0].status,'verified_late');
 assert.equal(pAfter.lastResponse.verifiedLate,true);
 const original=(await h.api('/api/jobs/'+returnJob.taskId)).data;
 assert.equal(original.status,'timeout','historical timeout must never be overwritten');
 h.ack(returnJob,parentUrl,parentId,'late webhook should be ignored');
 const next=(await h.api('/api/subagents','POST',{
  requestKey:'longrun-next-'+Date.now(),prompt:'R2: BLUE',parentManagedId:parent.id})).data;
 assert.equal(next.status,'starting');
 const dispatched=await h.waitFor(x=>x.type==='task_request'&&x.taskId===next.initialJobId);
 assert.equal(dispatched.prompt,'R2: BLUE');
 assert.equal(h.messages.filter(x=>x.taskId===returnJob.taskId).length,1,'no duplicate original send');
});
test('uncertain timeout does not forge success; bounded read-only re-verification survives extra wait',async t=>{
 const h=await setup(t);
 const {parent,child}=await createParentAndChild(h);
 const verify=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_verify_delivery');
 h.socket.send(JSON.stringify({type:'task_response',taskId:verify.taskId,status:'success',data:{
  url:parentUrl,conversationId:parentId,marker:child.initialJobId,
  verified:false,observed:'sent_waiting_reply',readOnly:true
 }}));
 for(let i=0;i<100;i++){const p=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(p.deliveries[0]?.verificationStatus==='inconclusive')break;await delay(10)}
 let p=(await h.api('/api/managed-parents/'+parent.id)).data;
 assert.equal(p.status,'needs_review');
 assert.equal(p.deliveries[0].status,'needs_review');
 assert.ok(p.deliveries[0].nextVerificationAt);
 // Advance the injected clock; periodic retry uses the same original marker.
 h.setNow(p.deliveries[0].nextVerificationAt+1);
 h.bridge.tick();
 const next=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_verify_delivery'&&x.taskId!==verify.taskId);
 assert.equal(next.marker,verify.marker);
 assert.equal(h.messages.filter(x=>x.taskId===verify.taskId).length,1);
 h.socket.send(JSON.stringify({type:'task_response',taskId:next.taskId,status:'success',data:{
  url:parentUrl,conversationId:parentId,marker:child.initialJobId,
  verified:true,observed:'completed',replyText:'LONGRUN_R1_GREEN 已收到',readOnly:true
 }}));
 for(let i=0;i<100;i++){p=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(p.status==='ready')break;await delay(10)}
 assert.equal(p.status,'ready');
 assert.equal(p.deliveries[0].verificationAttempts,2);
});
test('two sibling returns recover safely after first late verification without losing queued second delivery',async t=>{
 const h=await setup(t);
 const parent=(await h.api('/api/managed-parents','POST',{
  requestKey:'sibling-parent-'+Date.now(),prompt:'Supervisor'})).data;
 const parentSend=await h.waitFor(x=>x.type==='task_request'&&x.taskId===parent.initialJobId);
 h.ack(parentSend,parentUrl,parentId,'主管就绪');
 for(let i=0;i<100;i++){if((await h.api('/api/managed-parents/'+parent.id)).data.status==='ready')break;await delay(10)}
 const a=(await h.api('/api/subagents','POST',{requestKey:'sibling-a-'+Date.now(),
  parentManagedId:parent.id,prompt:'child A'})).data;
 const b=(await h.api('/api/subagents','POST',{requestKey:'sibling-b-'+Date.now(),
  parentManagedId:parent.id,prompt:'child B'})).data;
 const taskA=await h.waitFor(x=>x.type==='task_request'&&x.taskId===a.initialJobId);
 const taskB=await h.waitFor(x=>x.type==='task_request'&&x.taskId===b.initialJobId);
 h.ack(taskA,childUrl,childId,'CHILD_A_RESULT');
 h.socket.send(JSON.stringify({type:'task_response',taskId:taskB.taskId,status:'success',
  data:{url:'https://chatgpt.com/c/01234567-1234-4678-8abc-123456789003',
  conversationId:'01234567-1234-4678-8abc-123456789003',tabId:77,
  temporary:false,text:'CHILD_B_RESULT'}}));
 const first=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue');
 const proof=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_verify_delivery');
 assert.equal(proof.marker,a.initialJobId);
 let parentState=(await h.api('/api/managed-parents/'+parent.id)).data;
 assert.equal(parentState.status,'needs_review');
 const secondRecord=parentState.deliveries.find(d=>d.childJobId===b.initialJobId);
 assert.ok(secondRecord,'second child must have a delivery record');
 const queuedSecondId=secondRecord.returnJobId;
 h.socket.send(JSON.stringify({type:'task_response',taskId:proof.taskId,status:'success',
  data:{url:parentUrl,conversationId:parentId,marker:a.initialJobId,
   verified:true,observed:'completed',replyText:'CHILD_A_RESULT 已确认',readOnly:true}}));
 const second=await h.waitFor(x=>x.type==='task_request'&&
  x.action==='chatgpt_subagent_continue'&&x.taskId!==first.taskId);
 assert.equal(second.taskId,queuedSecondId,'unsent sibling must use its original queued Job');
 assert.match(second.prompt,/CHILD_B_RESULT/);
 h.ack(second,parentUrl,parentId,'CHILD_B_RESULT 已确认');
 for(let i=0;i<100;i++){
  parentState=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(parentState.status==='ready'&&parentState.deliveries.every(d=>['success','verified_late'].includes(d.status)))break;
  await delay(10);
 }
 assert.equal(parentState.status,'ready');
 assert.deepEqual(parentState.deliveries.map(d=>d.status),['verified_late','success']);
 assert.equal(h.messages.filter(m=>m.taskId===first.taskId).length,1);
 assert.equal(h.messages.filter(m=>m.taskId===second.taskId).length,1);
 const original=(await h.api('/api/jobs/'+first.taskId)).data;
 assert.equal(original.status,'timeout','historic uncertain write must remain a timeout');
});

test('proven before-send failure safely retries a return once and preserves original job',async t=>{
 const h=await setup(t);
 const {parent,returnJob}=await createParentAndChild(h);
 h.socket.send(JSON.stringify({type:'task_response',taskId:returnJob.taskId,
  status:'error',message:'等待发送按钮超时',
  deliveryPhase:'before_send',errorCode:'send_button_unavailable',
  data:{diagnostics:{editor:{lastInsertion:{sendDisabledImmediately:true}}}}
 }));
 const retry=await h.waitFor(x=>x.type==='task_request'&&
   x.action==='chatgpt_subagent_continue'&&x.taskId!==returnJob.taskId);
 assert.equal(retry.expectedUrl,parentUrl);
 assert.equal(retry.expectedConversationId,parentId);
 const original=(await h.api('/api/jobs/'+returnJob.taskId)).data;
 assert.equal(original.status,'error');
 assert.equal(original.deliveryPhase,'before_send');
 assert.equal(original.errorCode,'send_button_unavailable');
 let p=(await h.api('/api/managed-parents/'+parent.id)).data;
 assert.equal(p.deliveries[0].preSendRetryCount,1);
 assert.deepEqual(p.deliveries[0].preSendPreviousJobIds,[returnJob.taskId]);
 h.ack(retry,parentUrl,parentId,'replayed safely after proven unsent');
 for(let i=0;i<100;i++){
  p=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(p.status==='ready'&&p.deliveries[0].status==='success')break;
  await delay(10);
 }
 assert.equal(p.status,'ready');
 assert.equal(p.deliveries[0].status,'success');
 assert.equal(h.messages.filter(x=>x.taskId===returnJob.taskId).length,1);
});
test('after-send uncertainty must never replay the same return',async t=>{
 const h=await setup(t);
 const {parent,returnJob}=await createParentAndChild(h);
 h.socket.send(JSON.stringify({type:'task_response',taskId:returnJob.taskId,
  status:'error',message:'等待发送按钮超时',
  deliveryPhase:'after_send',errorCode:'send_button_unavailable',
  data:{diagnostics:{editor:{lastInsertion:{sendDisabledImmediately:true}}}}
 }));
 await delay(180);
 const p=(await h.api('/api/managed-parents/'+parent.id)).data;
 assert.equal(p.status,'needs_review');
 assert.equal(p.deliveries[0].preSendRetryCount||0,0);
 assert.equal(p.deliveries[0].returnJobId,returnJob.taskId);
 assert.equal(h.messages.filter(x=>x.action==='chatgpt_subagent_continue').length,1);
});

test('after-send response timeout error is verified read-only without duplicate return',async t=>{
 const h=await setup(t);
 const {parent,child,returnJob}=await createParentAndChild(h);
 h.socket.send(JSON.stringify({type:'task_response',taskId:returnJob.taskId,
  status:'error',message:'等待回复超时；消息可能已发送，请查询聊天后再决定是否重试',
  data:{deliveryUncertain:true}}));
 const verify=await h.waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_verify_delivery');
 assert.equal(verify.expectedUrl,parentUrl);
 assert.equal(verify.marker,child.initialJobId);
 assert.equal(h.messages.filter(x=>x.action==='chatgpt_subagent_continue').length,1);
 h.socket.send(JSON.stringify({type:'task_response',taskId:verify.taskId,status:'success',
  data:{url:parentUrl,conversationId:parentId,marker:child.initialJobId,
    verified:true,observed:'completed',replyText:'真实主管确认了原结果',readOnly:true}}));
 let p;
 for(let n=0;n<100;n++){
  p=(await h.api('/api/managed-parents/'+parent.id)).data;
  if(p.status==='ready')break;
  await delay(10);
 }
 assert.equal(p.status,'ready');
 assert.equal(p.deliveries[0].status,'verified_late');
 const original=(await h.api('/api/jobs/'+returnJob.taskId)).data;
 assert.equal(original.status,'error');
 assert.equal(h.messages.filter(x=>x.action==='chatgpt_subagent_continue').length,1);
});
