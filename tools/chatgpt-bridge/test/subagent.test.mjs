import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,readFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {once} from 'node:events';
import {WebSocket} from 'ws';
import {createBridge} from '../server.mjs';
import {validateSubagentCreate, validateSubagentMessage, childIdentityFromReply,
  makeContinuationTask} from '../subagent-routing.mjs';

const convId='af123456-bb22-cc33-dd44-eeeeeeeeeeee';
const url='https://chatgpt.com/c/'+convId;
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function setup(t) {
  const directory=mkdtempSync(join(tmpdir(),'yanzi-subagent-test-'));
  const bridge=await createBridge({directory,port:0,allowTestSocket:true});
  t.after(()=>bridge.close());
  const headers={Authorization:'Bearer '+bridge.token,'X-Bridge-Request':'1',
    'Content-Type':'application/json'};
  async function api(path,method='GET',payload) {
    const response=await fetch(bridge.origin+path,{
      method,headers,body:payload?JSON.stringify(payload):undefined
    });
    return {status:response.status,data:await response.json()};
  }
  const socket=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(socket,'open');
  t.after(()=>{if(socket.readyState===WebSocket.OPEN)socket.close()});
  const inbox=[];
  socket.on('message',raw=>inbox.push(JSON.parse(raw.toString())));
  socket.send(JSON.stringify({type:'register',version:'0.5.51'}));
  async function waitFor(predicate){
    const limit=Date.now()+2500;
    while(Date.now()<limit){
      const item=inbox.find(predicate);
      if(item)return item;
      await pause(10);
    }
    throw new Error('no expected websocket message');
  }
  return {bridge,api,socket,inbox,waitFor,directory};
}

test('validates child task creation, exact conversation identity and request keys',()=>{
  assert.throws(()=>validateSubagentCreate({prompt:'hi'}),/request_key/);
  assert.throws(()=>validateSubagentCreate({prompt:'hi',requestKey:'valid:001',parentOriginId:'invalid'}),/parent_origin/);
  assert.throws(()=>validateSubagentMessage({prompt:'hello',requestKey:'x'}),/request_key/);
  assert.equal(childIdentityFromReply({url,conversationId:convId,tabId:42,temporary:false}).url,url);
  assert.equal(childIdentityFromReply({url,conversationId:'different',tabId:42,temporary:false}),null);
  assert.equal(childIdentityFromReply({url,conversationId:convId,tabId:42,temporary:true}),null);
  assert.throws(()=>makeContinuationTask({status:'ready',url,conversationId:'wrong'},'go'),/unbound/);
});

test('dedicated new tab first, then exact same conversation on followup; persisted after restart',async t=>{
  const {bridge,api,socket,waitFor,inbox}=await setup(t);
  const create={title:'测试代理-A',prompt:'只回复：子代理一号',
    requestKey:'spawn:test-001',timeoutSeconds:180};
  const first=await api('/api/subagents','POST',create);
  assert.equal(first.status,202);
  assert.equal(first.data.status,'starting');
  assert.equal(first.data.tabId,null);
  const id=first.data.id;
  const task=await waitFor(m=>m.type==='task_request'&&m.taskId===first.data.initialJobId);
  assert.equal(task.action,'chatgpt_send');
  assert.equal(task.tabPolicy,'new');
  assert.equal(task.newChat,true);
  assert.equal(task.temporary,false);
  assert.equal(task.closeAfter,false);
  assert.equal(task.prompt,create.prompt);
  const duplicate=await api('/api/subagents','POST',create);
  assert.equal(duplicate.status,200);
  assert.equal(duplicate.data.id,id);
  assert.equal(inbox.filter(m=>m.type==='task_request').length,1);
  socket.send(JSON.stringify({type:'task_response',taskId:task.taskId,status:'success',
    data:{url,conversationId:convId,tabId:512,temporary:false,text:'子代理一号',messageId:'response-1'}}));
  let status;
  for(let i=0;i<100;i++){status=(await api('/api/subagents/'+id)).data;if(status.status==='ready')break;await pause(10)}
  assert.equal(status.status,'ready');
  assert.equal(status.tabId,512);
  assert.equal(status.url,url);
  assert.equal(status.lastResult.text,'子代理一号');
  assert.equal(status.jobs[0].job.data.tabId,512);
  const next=await api('/api/subagents/'+id+'/messages','POST',{
    prompt:'继续：本次输出第二轮',requestKey:'followup:test-001'});
  assert.equal(next.status,202);
  const second=await waitFor(m=>m.type==='task_request'&&m.taskId===next.data.jobId);
  assert.equal(second.action,'chatgpt_subagent_continue');
  assert.equal(second.expectedUrl,url);
  assert.equal(second.expectedConversationId,convId);
  assert.equal(second.newChat,false);
  assert.equal(second.temporary,false);
  assert.equal(second.tabId,undefined);
  assert.equal(second.prompt,'继续：本次输出第二轮');
  const retry=await api('/api/subagents/'+id+'/messages','POST',{
    prompt:'继续：本次输出第二轮',requestKey:'followup:test-001'});
  assert.equal(retry.status,200);
  assert.equal(retry.data.jobId,next.data.jobId);
  assert.equal(inbox.filter(m=>m.type==='task_request').length,2);
  assert.equal((await api('/api/subagents/'+id+'/messages','POST',{
    prompt:'其他消息',requestKey:'followup:test-002'})).status,400);
  socket.send(JSON.stringify({type:'task_response',taskId:second.taskId,status:'success',
    data:{url,conversationId:convId,tabId:624,temporary:false,text:'第二轮完成'}}));
  for(let i=0;i<100;i++){status=(await api('/api/subagents/'+id)).data;if(status.status==='ready')break;await pause(10)}
  assert.equal(status.tabId,624);
  assert.equal(status.rounds.length,2);
  assert.equal(status.lastResult.text,'第二轮完成');
  assert.equal((await api('/api/subagents')).data[0].id,id);
});

test('invalid reply identity fails closed; no continuation into a guessed tab',async t=>{
  const {api,socket,waitFor}=await setup(t);
  const created=(await api('/api/subagents','POST',{requestKey:'guard:test-1',prompt:'测试'})).data;
  const dispatched=await waitFor(x=>x.type==='task_request'&&x.taskId===created.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:dispatched.taskId,status:'success',
    data:{url:'https://chatgpt.com/c/other-conversation',tabId:70,conversationId:'wrong',temporary:false,text:'invalid'}}));
  let child;
  for(let i=0;i<100;i++){child=(await api('/api/subagents/'+created.id)).data;if(child.status==='needs_review')break;await pause(10)}
  assert.equal(child.status,'needs_review');
  assert.equal(child.conversationId,null);
  assert.equal((await api('/api/subagents/'+created.id+'/messages','POST',{
    prompt:'should not send',requestKey:'guard:retry'})).status,400);
});

test('child response automatically returns to explicitly registered parent; no manual tab guess',async t=>{
  const {api,socket,waitFor}=await setup(t);
  const source='https://chatgpt.com/c/01234567-1234-4567-89ab-cdef01234567';
  socket.send(JSON.stringify({type:'origin_bind',requestId:'parent-test-1',tabId:21,url:source}));
  const binding=(await waitFor(x=>x.type==='origin_bind_ack')).binding;
  const created=(await api('/api/subagents','POST',{
    requestKey:'parent:child-001',prompt:'生成一份短报告',parentOriginId:binding.id,
    title:'自动回传验证'})).data;
  const task=await waitFor(x=>x.type==='task_request'&&x.taskId===created.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:task.taskId,status:'success',
    data:{url,conversationId:convId,tabId:33,temporary:false,text:'报告已经完成'}}));
  const returned=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_feedback_send');
  assert.equal(returned.expectedUrl,source);
  assert.match(returned.prompt,/报告已经完成/);
  assert.match(returned.prompt,/自动回传验证/);
  assert.equal(returned.newChat,false);
  assert.equal(returned.temporary,false);
  const status=(await api('/api/subagents/'+created.id)).data;
  assert.equal(status.rounds[0].returnJobId,returned.taskId);
  assert.equal((await api('/api/subagents/'+created.id+'/return','POST')).data.returnJobId,returned.taskId);
});

test('subagent creation refuses nonexistent parent binding',async t=>{
  const {api}=await setup(t);
  const result=await api('/api/subagents','POST',{
    requestKey:'reject:parent-01',prompt:'no parent',
    parentOriginId:'00000000-0000-4000-8000-000000000000'});
  assert.equal(result.status,400);
  assert.equal((await api('/api/subagents')).data.length,0);
});

test('parent task gets durable start/finish events, long-poll delivery, and file snapshot',async t=>{
  const {api,socket,waitFor,directory}=await setup(t);
  const parentTaskId='yanzi-parent-task-smoke-01';
  const initial=await api('/api/parent-tasks/'+parentTaskId+'/events?after=0');
  assert.equal(initial.status,200);
  assert.equal(initial.data.events.length,0);
  const payload={requestKey:'spawn:parent-test01',prompt:'Return ONLY PARENT_EVENT_OK',
    title:'event-probe',parentTaskId};
  const created=await api('/api/subagents','POST',payload);
  assert.equal(created.status,202);
  assert.equal(created.data.parentTaskId,parentTaskId);
  const dispatched=await api('/api/parent-tasks/'+parentTaskId+'/events?after=0');
  assert.equal(dispatched.status,200);
  assert.equal(dispatched.data.events.length,1);
  assert.equal(dispatched.data.events[0].kind,'subagent_dispatched');
  const cursor=dispatched.data.lastSeq;
  const nextPromise=api('/api/parent-tasks/'+parentTaskId+'/events?after='+cursor+'&waitMs=3000');
  const request=await waitFor(x=>x.type==='task_request'&&x.taskId===created.data.initialJobId);
  await pause(40);
  socket.send(JSON.stringify({type:'task_response',taskId:request.taskId,status:'success',
    data:{url,conversationId:convId,tabId:142,temporary:false,text:'PARENT_EVENT_OK'}}));
  const notified=await nextPromise;
  assert.equal(notified.status,200);
  assert.equal(notified.data.events.length,1);
  assert.equal(notified.data.events[0].kind,'subagent_finished');
  assert.equal(notified.data.events[0].text,'PARENT_EVENT_OK');
  assert.equal(notified.data.events[0].networkDiagnosis.status,'reply_confirmed');
  assert.equal(notified.data.events[0].subagentId,created.data.id);
  assert.ok(notified.data.lastSeq>cursor);
  assert.equal((await api('/api/parent-tasks/'+parentTaskId+'/events?after='+notified.data.lastSeq)).data.events.length,0);
  const file=join(directory,'parent-feedback',encodeURIComponent(parentTaskId)+'.json');
  assert.ok(existsSync(file));
  const snapshot=JSON.parse(readFileSync(file,'utf8'));
  assert.equal(snapshot.parentTaskId,parentTaskId);
  assert.equal(snapshot.events.length,2);
  assert.equal(snapshot.events[1].text,'PARENT_EVENT_OK');
  assert.equal(snapshot.lastEventSeq,notified.data.lastSeq);
  assert.equal((await api('/api/subagents','POST',payload)).status,200);
  assert.equal((await api('/api/parent-tasks/'+parentTaskId+'/events?after=0')).data.events.length,2);
  const continued=await api('/api/subagents/'+created.data.id+'/messages','POST',{
    prompt:'second round',requestKey:'continue:parent-test01'});
  assert.equal(continued.status,202);
  const progress=await api('/api/parent-tasks/'+parentTaskId+'/events?after='+notified.data.lastSeq);
  assert.equal(progress.data.events[0].kind,'subagent_continued');
  const secondRequest=await waitFor(x=>x.type==='task_request'&&x.taskId===continued.data.jobId);
  socket.send(JSON.stringify({type:'task_response',taskId:secondRequest.taskId,status:'success',
    data:{url,conversationId:convId,tabId:142,temporary:false,text:'SECOND_PARENT_EVENT'}}));
  for(let i=0;i<120;i++){
    if((await api('/api/subagents/'+created.data.id)).data.status==='ready')break;
    await pause(10);
  }
  const events=(await api('/api/parent-tasks/'+parentTaskId+'/events?after=0')).data.events;
  assert.deepEqual(events.map(e=>e.kind),[
    'subagent_dispatched','subagent_finished','subagent_continued','subagent_finished']);
  assert.equal(events.at(-1).text,'SECOND_PARENT_EVENT');
});

test('parent event cursor validates bounds, long poll times out without spin',async t=>{
  const {api}=await setup(t);
  const parent='parent-check-validation-01';
  assert.equal((await api('/api/parent-tasks/'+parent+'/events?after=-1')).status,400);
  assert.equal((await api('/api/parent-tasks/'+parent+'/events?waitMs=25001')).status,400);
  const started=Date.now();
  const waited=await api('/api/parent-tasks/'+parent+'/events?waitMs=105');
  assert.equal(waited.status,200);
  assert.deepEqual(waited.data.events,[]);
  assert.ok(Date.now()-started>=85);
  assert.ok(Date.now()-started<1800);
});

test('parent mailbox survives bridge service restart; old cursor can replay events', async t=>{
  const directory=mkdtempSync(join(tmpdir(),'yanzi-parent-restart-'));
  const parentTaskId='parent:restart-check-001';
  const first=await createBridge({directory,port:0,allowTestSocket:true});
  const headers={Authorization:'Bearer '+first.token,'X-Bridge-Request':'1','Content-Type':'application/json'};
  const response=await fetch(first.origin+'/api/subagents',{
    method:'POST',headers,
    body:JSON.stringify({title:'persist parent mailbox',requestKey:'persist:parent-001',
      parentTaskId,prompt:'test'})
  });
  assert.equal(response.status,202);
  const created=await response.json();
  assert.equal(created.parentTaskId,parentTaskId);
  await first.close();
  const second=await createBridge({directory,port:0,allowTestSocket:true});
  t.after(()=>second.close());
  const events=await fetch(second.origin+'/api/parent-tasks/parent%3Arestart-check-001/events?after=0',{
    headers:{Authorization:'Bearer '+second.token}
  });
  assert.equal(events.status,200);
  const data=await events.json();
  assert.equal(data.events.length,1);
  assert.equal(data.events[0].subagentId,created.id);
  assert.equal(data.events[0].kind,'subagent_dispatched');
  assert.equal(data.lastSeq,1);
  const file=join(directory,'parent-feedback',encodeURIComponent(parentTaskId)+'.json');
  assert.ok(existsSync(file));
  assert.equal(JSON.parse(readFileSync(file,'utf8')).lastEventSeq,1);
  const cursor=await fetch(second.origin+'/api/parent-tasks/parent%3Arestart-check-001/events?after=1',{
    headers:{Authorization:'Bearer '+second.token}
  });
  assert.equal((await cursor.json()).events.length,0);
});

test('managed parent starts in a private conversation, then receives child result automatically',async t=>{
  const {api,socket,waitFor,inbox}=await setup(t);
  const parentInput={requestKey:'parent:managed-001',title:'测试父 Agent',
    prompt:'你是父 Agent，只等待子 Agent 汇报。'};
  const created=await api('/api/managed-parents','POST',parentInput);
  assert.equal(created.status,202);
  assert.equal(created.data.status,'starting');
  const parentId=created.data.id;
  const parentTaskId=created.data.taskId;
  const initial=await waitFor(x=>x.type==='task_request'&&x.taskId===created.data.initialJobId);
  assert.equal(initial.action,'chatgpt_send');
  assert.equal(initial.tabPolicy,'new');
  assert.equal(initial.newChat,true);
  assert.equal(initial.temporary,false);
  const duplicate=await api('/api/managed-parents','POST',parentInput);
  assert.equal(duplicate.status,200);
  assert.equal(duplicate.data.id,parentId);
  socket.send(JSON.stringify({type:'task_response',taskId:initial.taskId,status:'success',
    data:{url,tabId:50,conversationId:convId,temporary:false,text:'父 Agent 就绪'}}));
  let parent;
  for(let i=0;i<100;i++){
    parent=(await api('/api/managed-parents/'+parentId)).data;
    if(parent.status==='ready')break;await pause(10);
  }
  assert.equal(parent.status,'ready');
  assert.equal(parent.url,url);
  const childInput={requestKey:'parent:managed-child-001',prompt:'生成报告',
    title:'自动回传用例',parentManagedId:parentId};
  const child=await api('/api/subagents','POST',childInput);
  assert.equal(child.status,202);
  assert.equal(child.data.parentTaskId,parentTaskId);
  assert.equal(child.data.parentManagedId,parentId);
  const childJob=await waitFor(x=>x.type==='task_request'&&x.taskId===child.data.initialJobId);
  assert.equal(childJob.newChat,true);
  const cid2='b1234567-1234-4567-89ab-cdef01234567';
  const childUrl='https://chatgpt.com/c/'+cid2;
  socket.send(JSON.stringify({type:'task_response',taskId:childJob.taskId,status:'success',
    data:{url:childUrl,tabId:51,conversationId:cid2,temporary:false,text:'子 Agent 检查通过'}}));
  const returnJob=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue' &&
    x.expectedConversationId===convId);
  assert.equal(returnJob.expectedUrl,url);
  assert.equal(returnJob.newChat,false);
  assert.equal(returnJob.closeAfter,false);
  assert.match(returnJob.prompt,/子 Agent 检查通过/);
  assert.match(returnJob.prompt,/自动回传用例/);
  assert.match(returnJob.prompt,/b1234567/);
  parent=(await api('/api/managed-parents/'+parentId)).data;
  assert.equal(parent.status,'processing');
  assert.equal(parent.deliveries.length,1);
  assert.equal(parent.deliveries[0].childJobId,childJob.taskId);
  assert.equal(parent.deliveries[0].returnJobId,returnJob.taskId);
  socket.send(JSON.stringify({type:'task_response',taskId:returnJob.taskId,status:'success',
    data:{url,tabId:52,conversationId:convId,temporary:false,
      text:'父 Agent 收到反馈，下一轮应测试剪贴板状态'}}));
  for(let i=0;i<100;i++){
    parent=(await api('/api/managed-parents/'+parentId)).data;
    if(parent.status==='ready')break;await pause(10);
  }
  assert.equal(parent.status,'ready');
  assert.equal(parent.lastResponse.text,'父 Agent 收到反馈，下一轮应测试剪贴板状态');
  assert.equal(parent.deliveries[0].status,'success');
  const events=await api('/api/parent-tasks/'+encodeURIComponent(parentTaskId)+'/events?after=0');
  assert.deepEqual(events.data.events.map(x=>x.kind),
    ['subagent_dispatched','subagent_finished']);
  assert.equal(events.data.events[1].text,'子 Agent 检查通过');
  assert.equal((await api('/api/subagents','POST',childInput)).status,200);
  assert.equal(inbox.filter(x=>x.type==='task_request' && x.expectedConversationId===convId).length,1);
});

test('managed parent is serialized when two children return concurrently',async t=>{
  const {api,socket,waitFor,inbox}=await setup(t);
  const parent=(await api('/api/managed-parents','POST',{
    requestKey:'parent:serial-001',prompt:'父 Agent'})).data;
  await waitFor(x=>x.type==='task_request'&&x.taskId===parent.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:parent.initialJobId,status:'success',
    data:{url,tabId:56,conversationId:convId,temporary:false,text:'OK'}}));
  for(let i=0;i<100;i++){
    if((await api('/api/managed-parents/'+parent.id)).data.status==='ready')break;
    await pause(10);
  }
  const childA=(await api('/api/subagents','POST',{
    requestKey:'parent:serial-child-a',parentManagedId:parent.id,prompt:'task A'})).data;
  const childB=(await api('/api/subagents','POST',{
    requestKey:'parent:serial-child-b',parentManagedId:parent.id,prompt:'task B'})).data;
  await waitFor(x=>x.type==='task_request'&&x.taskId===childA.initialJobId);
  await waitFor(x=>x.type==='task_request'&&x.taskId===childB.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:childA.initialJobId,status:'success',
    data:{url:'https://chatgpt.com/c/aaaaaaa1-1234-4567-89ab-cdef01234567',
      tabId:57,conversationId:'aaaaaaa1-1234-4567-89ab-cdef01234567',
      temporary:false,text:'A'}}));
  const first=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue' &&
    x.expectedConversationId===convId);
  socket.send(JSON.stringify({type:'task_response',taskId:childB.initialJobId,status:'success',
    data:{url:'https://chatgpt.com/c/bbbbbbb1-1234-4567-89ab-cdef01234567',
      tabId:58,conversationId:'bbbbbbb1-1234-4567-89ab-cdef01234567',
      temporary:false,text:'B'}}));
  await pause(60);
  assert.equal(inbox.filter(x=>x.type==='task_request' && x.action==='chatgpt_subagent_continue').length,1);
  const mid=(await api('/api/managed-parents/'+parent.id)).data;
  assert.equal(mid.deliveries.length,2);
  assert.deepEqual(mid.deliveries.map(x=>x.status),['queued','queued']);
  socket.send(JSON.stringify({type:'task_response',taskId:first.taskId,status:'success',
    data:{url,tabId:59,conversationId:convId,temporary:false,text:'已收 A'}}));
  const second=await waitFor(x=>x.type==='task_request' && x.action==='chatgpt_subagent_continue' &&
    x.taskId!==first.taskId);
  assert.equal(second.expectedUrl,url);
  socket.send(JSON.stringify({type:'task_response',taskId:second.taskId,status:'success',
    data:{url,tabId:59,conversationId:convId,temporary:false,text:'已收 B'}}));
  for(let i=0;i<100;i++){
    const snapshot=(await api('/api/managed-parents/'+parent.id)).data;
    if(snapshot.status==='ready' && snapshot.deliveries.every(x=>x.status==='success'))break;
    await pause(10);
  }
  const final=(await api('/api/managed-parents/'+parent.id)).data;
  assert.deepEqual(final.deliveries.map(x=>x.status),['success','success']);
});

test('managed parent invalid source identity is blocked before accepting child',async t=>{
  const {api,socket,waitFor}=await setup(t);
  const parent=(await api('/api/managed-parents','POST',{
    requestKey:'parent:bad-001',prompt:'parent'})).data;
  await waitFor(x=>x.type==='task_request'&&x.taskId===parent.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:parent.initialJobId,status:'success',
    data:{url,tabId:70,conversationId:'wrong',temporary:false,text:'spoofed'}}));
  for(let i=0;i<100;i++){
    if((await api('/api/managed-parents/'+parent.id)).data.status==='needs_review')break;
    await pause(10);
  }
  assert.equal((await api('/api/subagents','POST',{
    requestKey:'bad:child',prompt:'unsafe',parentManagedId:parent.id
  })).status,400);
});

test('managed parent persists across bridge restart and keeps original identity',async t=>{
  const folder=mkdtempSync(join(tmpdir(),'yanzi-managed-restart-'));
  const first=await createBridge({directory:folder,port:0,allowTestSocket:true});
  const h1={Authorization:'Bearer '+first.token,'X-Bridge-Request':'1','Content-Type':'application/json'};
  const conn=new WebSocket(first.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(conn,'open');
  conn.send(JSON.stringify({type:'register',version:'0.5.52'}));
  const task=await fetch(first.origin+'/api/managed-parents',{
    method:'POST',headers:h1,
    body:JSON.stringify({requestKey:'managed:restart-01',title:'Persistent parent',
      prompt:'Parent initialization'})});
  const parent=await task.json();
  assert.equal(task.status,202);
  conn.send(JSON.stringify({type:'task_response',taskId:parent.initialJobId,status:'success',
    data:{url,tabId:120,conversationId:convId,temporary:false,text:'Parent initialized'}}));
  for(let i=0;i<100;i++){
    const check=first.state.managedParents.find(p=>p.id===parent.id);
    if(check.status==='ready')break;
    await pause(10);
  }
  assert.equal(first.state.managedParents.find(p=>p.id===parent.id).status,'ready');
  conn.close();await once(conn,'close');
  await first.close();
  const second=await createBridge({directory:folder,port:0,allowTestSocket:true});
  t.after(()=>second.close());
  const h2={Authorization:'Bearer '+second.token};
  const result=await fetch(second.origin+'/api/managed-parents/'+parent.id,{headers:h2});
  assert.equal(result.status,200);
  const saved=await result.json();
  assert.equal(saved.status,'ready');
  assert.equal(saved.url,url);
  assert.equal(saved.taskId,'managed-parent:'+parent.id);
  assert.equal((await (await fetch(second.origin+'/api/managed-parents',{headers:h2})).json()).length,1);
});

test('interrupted parent result stays needs_review instead of automatically resending',async t=>{
  const {bridge,api,socket,waitFor,inbox}=await setup(t);
  const created=(await api('/api/managed-parents','POST',{
    requestKey:'parent:uncertain-001',prompt:'parent'})).data;
  await waitFor(x=>x.type==='task_request'&&x.taskId===created.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:created.initialJobId,status:'success',
    data:{url,tabId:123,conversationId:convId,temporary:false,text:'ready'}}));
  for(let i=0;i<100;i++){
    if((await api('/api/managed-parents/'+created.id)).data.status==='ready')break;
    await pause(10);
  }
  const child=(await api('/api/subagents','POST',{
    requestKey:'parent:uncertain-child',parentManagedId:created.id,prompt:'task'})).data;
  await waitFor(x=>x.type==='task_request'&&x.taskId===child.initialJobId);
  socket.send(JSON.stringify({type:'task_response',taskId:child.initialJobId,status:'success',
    data:{url:'https://chatgpt.com/c/ccccccc1-1234-4567-89ab-cdef01234567',
      tabId:124,conversationId:'ccccccc1-1234-4567-89ab-cdef01234567',
      temporary:false,text:'hello'}}));
  const returned=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue');
  socket.send(JSON.stringify({type:'task_response',taskId:returned.taskId,status:'error',
    message:'Page may have received message, final status unknown',data:null}));
  for(let i=0;i<100;i++){
    if((await api('/api/managed-parents/'+created.id)).data.status==='needs_review')break;
    await pause(10);
  }
  const parent=(await api('/api/managed-parents/'+created.id)).data;
  assert.equal(parent.status,'needs_review');
  assert.equal(parent.deliveries[0].status,'error');
  assert.equal(inbox.filter(m=>m.type==='task_request'&&m.taskId===returned.taskId).length,1);
  assert.equal((await api('/api/subagents','POST',{
    requestKey:'parent:uncertain-other',parentManagedId:created.id,prompt:'task 2'})).status,400);
});
