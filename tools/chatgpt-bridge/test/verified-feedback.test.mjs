import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {once} from 'node:events';
import {WebSocket} from 'ws';
import {createBridge} from '../server.mjs';
const conv='af123456-bb22-cc33-dd44-eeeeeeeeeeee';
const url='https://chatgpt.com/c/'+conv;
const childUrl='https://chatgpt.com/c/b1234567-bb22-cc33-dd44-eeeeeeeeeeee';
const childConv=childUrl.split('/').at(-1);
const delay=ms=>new Promise(r=>setTimeout(r,ms));

test('verified feedback: real child identity, idempotency, and parent queue serialization',async t=>{
  const directory=mkdtempSync(join(tmpdir(),'yanzi-verified-parent-'));
  t.after(()=>rmSync(directory,{recursive:true,force:true}));
  const bridge=await createBridge({directory,port:0,allowTestSocket:true});
  t.after(()=>bridge.close());
  const headers={Authorization:'Bearer '+bridge.token,'X-Bridge-Request':'1','Content-Type':'application/json'};
  const api=async(path,method='GET',obj)=>{
    const response=await fetch(bridge.origin+path,{method,headers,body:obj?JSON.stringify(obj):undefined});
    return {status:response.status,data:await response.json()};
  };
  const socket=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(socket,'open');
  t.after(()=>{if(socket.readyState===WebSocket.OPEN)socket.close()});
  const messages=[];socket.on('message',data=>messages.push(JSON.parse(data.toString())));
  socket.send(JSON.stringify({type:'register',version:'0.5.55'}));
  const next=async predicate=>{
    for(let i=0;i<200;i++){
      const x=messages.find(predicate);
      if(x)return x;
      await delay(8);
    }
    throw new Error('missing_websocket_task');
  };
  const parent=(await api('/api/managed-parents','POST',{
    requestKey:'parent:verified-smoke',prompt:'Return a JSON decision'})).data;
  const init=await next(x=>x.type==='task_request'&&x.taskId===parent.initialJobId);
  assert.equal(init.action,'chatgpt_send');
  socket.send(JSON.stringify({type:'task_response',taskId:init.taskId,status:'success',
    data:{url,tabId:300,conversationId:conv,temporary:false,text:'ready'}}));
  for(let i=0;i<150;i++){
    if((await api('/api/managed-parents/'+parent.id)).data.status==='ready')break;
    await delay(8);
  }
  const child=(await api('/api/subagents','POST',{
    requestKey:'verified:child-001',title:'first child',prompt:'generate patch',
    parentTaskId:'managed-parent:'+parent.id})).data;
  const childRequest=await next(x=>x.type==='task_request'&&x.taskId===child.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:childRequest.taskId,status:'success',
    data:{url:childUrl,tabId:301,conversationId:childConv,
      temporary:false,text:'UNVERIFIED_RAW_CHILD_CLAIM'}}));
  for(let i=0;i<150;i++){
    if((await api('/api/subagents/'+child.id)).data.status==='ready')break;
    await delay(8);
  }
  const route='/api/managed-parents/'+parent.id+'/verified-feedback';
  const proof={requestKey:'proof:verified-child-001',childId:child.id,
    childJobId:child.initialJobId,task:'title',round:1,
    verification:{status:'failed_rolled_back',testPassed:false,testsUnchanged:true,
      rollback:true,sourceSha256Before:'A'.repeat(64),sourceSha256After:'A'.repeat(64),
      reason:'test_fail',testLog:'node fixed test failed'}};
  assert.equal((await api(route,'POST',{...proof,verification:{
    ...proof.verification,status:'passed',testPassed:false}})).status,400);
  const sent=await api(route,'POST',proof);
  assert.equal(sent.status,202);
  const feedbackTask=await next(x=>x.type==='task_request'&&x.taskId===sent.data.jobId);
  assert.equal(feedbackTask.action,'chatgpt_subagent_continue');
  assert.equal(feedbackTask.expectedUrl,url);
  assert.match(feedbackTask.prompt,/failed_rolled_back/);
  assert.doesNotMatch(feedbackTask.prompt,/UNVERIFIED_RAW_CHILD_CLAIM/);
  const duplicate=await api(route,'POST',proof);
  assert.equal(duplicate.status,200);
  assert.equal(duplicate.data.jobId,sent.data.jobId);
  assert.equal((await api(route,'POST',{...proof,childJobId:'0'.repeat(36)})).status,400);
  assert.equal((await api(route,'POST',{...proof,verification:{...proof.verification,reason:'different'}})).status,400);
  const secondChild=(await api('/api/subagents','POST',{
    requestKey:'verified:child-002',title:'second child',prompt:'different patch',
    parentTaskId:'managed-parent:'+parent.id})).data;
  const secondRequest=await next(x=>x.type==='task_request'&&x.taskId===secondChild.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:secondRequest.taskId,status:'success',
    data:{url:'https://chatgpt.com/c/c1234567-bb22-cc33-dd44-eeeeeeeeeeee',
      tabId:302,conversationId:'c1234567-bb22-cc33-dd44-eeeeeeeeeeee',
      temporary:false,text:'RAW_NOT_VERIFIED'}}));
  for(let i=0;i<150;i++){
    if((await api('/api/subagents/'+secondChild.id)).data.status==='ready')break;
    await delay(8);
  }
  const second=await api(route,'POST',{
    ...proof,requestKey:'proof:verified-child-002',childId:secondChild.id,
    childJobId:secondChild.initialJobId});
  assert.equal(second.status,202);
  await delay(40);
  assert.equal(messages.filter(x=>x.type==='task_request'&&
    x.action==='chatgpt_subagent_continue'&&x.expectedUrl===url).length,1);
  const checking=(await api('/api/managed-parents/'+parent.id)).data;
  assert.equal(checking.deliveries.length,2);
  assert.equal(checking.status,'processing');
  socket.send(JSON.stringify({type:'task_response',taskId:feedbackTask.taskId,status:'success',
    data:{url,tabId:303,conversationId:conv,temporary:false,
      text:'{"decision":"retry","reason":"the tests failed"}'}}));
  const secondDelivery=await next(x=>x.type==='task_request' && x.taskId===second.data.jobId);
  assert.equal(secondDelivery.expectedUrl,url);
  socket.send(JSON.stringify({type:'task_response',taskId:secondDelivery.taskId,status:'success',
    data:{url,tabId:304,conversationId:conv,temporary:false,
      text:'{"decision":"stop","reason":"done"}'}}));
  for(let i=0;i<150;i++){
    const p=(await api('/api/managed-parents/'+parent.id)).data;
    if(p.status==='ready'&&p.deliveries.every(x=>x.status==='success'))break;
    await delay(8);
  }
  const ended=(await api('/api/managed-parents/'+parent.id)).data;
  assert.equal(ended.status,'ready');
  assert.deepEqual(ended.deliveries.map(x=>x.status),['success','success']);
});
