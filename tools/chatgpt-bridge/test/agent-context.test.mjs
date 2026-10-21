import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { createBridge } from '../server.mjs';
import { agentContext, bootstrapTask, verifyBootstrapAck } from '../agent-context.mjs';

const parentConversation = 'aa111111-bb22-cc33-dd44-eeeeeeeeeeee';
const url = id => 'https://chatgpt.com/c/' + id;
const childConversations = [
  'bb111111-bb22-cc33-dd44-eeeeeeeeeeee',
  'cc111111-bb22-cc33-dd44-eeeeeeeeeeee',
  'dd111111-bb22-cc33-dd44-eeeeeeeeeeee'
];

test('bootstrap includes verified self identity and never any local token', () => {
  const record = { id:'ee111111-bb22-cc33-dd44-eeeeeeeeeeee',
    taskId:'managed-parent:test',conversationId:parentConversation,
    url:url(parentConversation),bootstrap:{enabled:true,status:'queued'} };
  const ctx = agentContext('supervisor',record);
  const task = bootstrapTask(ctx);
  assert.equal(task.expectedConversationId,parentConversation);
  assert.equal(task.action,'chatgpt_subagent_continue');
  assert.match(task.prompt, /agentId/);
  assert.match(task.prompt, /api\/agents\//);
  assert.equal(task.prompt.includes('Bearer'),false);
  assert.equal(verifyBootstrapAck('YANZI_BOOTSTRAP_ACK:'+record.id,record.id),true);
  assert.equal(verifyBootstrapAck('YANZI_BOOTSTRAP_ACK:another',record.id),false);
});

test('supervisor and three children automatically register, bootstrap and return results to exact supervisor', async t => {
  const directory=mkdtempSync(join(tmpdir(),'yanzi-agent-context-'));
  const bridge=await createBridge({directory,port:0,allowTestSocket:true});
  t.after(()=>bridge.close());
  const headers={Authorization:'Bearer '+bridge.token,'X-Bridge-Request':'1','Content-Type':'application/json'};
  async function api(path,method='GET',body){
    const response=await fetch(bridge.origin+path,{method,headers,body:body?JSON.stringify(body):undefined});
    return {status:response.status,data:await response.json()};
  }
  const ws=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(ws,'open');
  t.after(()=>{if(ws.readyState===WebSocket.OPEN)ws.close()});
  const requests=[];
  ws.on('message', raw=>requests.push(JSON.parse(raw.toString())));
  ws.send(JSON.stringify({type:'register',version:'0.5.65'}));
  async function next(predicate){
    const deadline=Date.now()+3500;
    while(Date.now()<deadline){
      const item=requests.find(predicate);
      if(item)return item;
      await new Promise(r=>setTimeout(r,10));
    }
    throw new Error('no next websocket task');
  }
  function complete(task,conversationId,text,tabId){
    ws.send(JSON.stringify({type:'task_response',taskId:task.taskId,status:'success',
      data:{conversationId,url:url(conversationId),tabId,temporary:false,text}}));
  }
  const p=(await api('/api/managed-parents','POST',{
    requestKey:'ctx-supervisor-test-01',prompt:'Supervisor: only say READY',bootstrap:true
  })).data;
  const initial=await next(x=>x.taskId===p.initialJobId);
  assert.equal(initial.action,'chatgpt_send');
  complete(initial,parentConversation,'READY',310);
  const pBootstrap=await next(x=>x.action==='chatgpt_subagent_continue' &&
    x.prompt?.includes('[YANZI_AGENT_BOOTSTRAP_V1]') && x.prompt?.includes(p.id));
  assert.equal(pBootstrap.expectedUrl,url(parentConversation));
  assert.equal((await api('/api/managed-parents/'+p.id)).data.status,'bootstrapping');
  assert.equal((await api('/api/subagents','POST',{requestKey:'too-early-child',
    prompt:'No',parentManagedId:p.id})).status,400);
  complete(pBootstrap,parentConversation,'YANZI_BOOTSTRAP_ACK:'+p.id,310);
  const parentReady=async()=> (await api('/api/managed-parents/'+p.id)).data.status==='ready';
  for(let i=0;i<120 && !(await parentReady());i++)await new Promise(r=>setTimeout(r,8));
  assert.equal(await parentReady(),true);
  const supervisorContext=await api('/api/agents/'+p.id+'/context');
  assert.equal(supervisorContext.status,200);
  assert.equal(supervisorContext.data.agentId,p.id);
  assert.equal(supervisorContext.data.bootstrap.status,'confirmed');
  assert.equal((await api('/api/agents/'+p.id+'/context','POST',{})).status,404);

  const children=[];
  for(let i=0;i<3;i++) {
    const response=await api('/api/subagents','POST',{
      requestKey:'ctx-child-'+i+'-test',
      title:'child-'+i,
      prompt:'CHILD_'+i+': only answer RESULT_'+i,
      parentManagedId:p.id
    });
    assert.equal(response.status,202);
    assert.equal(response.data.bootstrap.enabled,true);
    children.push(response.data);
  }
  for(let i=0;i<3;i++){
    const child=children[i],cid=childConversations[i];
    const init=await next(x=>x.taskId===child.initialJobId);
    assert.match(init.prompt,new RegExp('CHILD_'+i));
    complete(init,cid,'RESULT_'+i,400+i);
  }
  const childBootstrapTasks=[];
  for(let i=0;i<3;i++){
    const child=children[i];
    const job=await next(x=>x.action==='chatgpt_subagent_continue' &&
      x.expectedConversationId===childConversations[i] &&
      x.prompt?.includes('[YANZI_AGENT_BOOTSTRAP_V1]'));
    childBootstrapTasks.push(job);
    assert.match(job.prompt,new RegExp(child.id));
    const info=await api('/api/agents/'+child.id+'/context');
    assert.equal(info.status,200);
    assert.equal(info.data.parentAgentId,p.id);
    assert.equal(info.data.replyTo,p.id);
    assert.equal(info.data.conversationId,childConversations[i]);
    complete(job,childConversations[i],'YANZI_BOOTSTRAP_ACK:'+child.id,400+i);
  }
  for(const child of children){
    for(let i=0;i<150;i++){
      const result=(await api('/api/subagents/'+child.id)).data;
      if(result.status==='ready')break;
      await new Promise(r=>setTimeout(r,8));
    }
    assert.equal((await api('/api/subagents/'+child.id)).data.bootstrap.status,'confirmed');
  }
  const receiptJobs=[];
  for(let i=0;i<3;i++){
    const parentRecord=(await api('/api/managed-parents/'+p.id)).data;
    const delivery=parentRecord.deliveries.find(d=>d.subagentId===children[i].id);
    assert.ok(delivery?.returnJobId,'child delivery should be durably enqueued');
    receiptJobs.push(delivery.returnJobId);
  }
  for(let i=0;i<3;i++){
    const task=await next(x=>x.taskId===receiptJobs[i]);
    assert.equal(task.expectedConversationId,parentConversation);
    assert.equal(task.expectedUrl,url(parentConversation));
    assert.match(task.prompt,new RegExp('RESULT_'+i));
    complete(task,parentConversation,'SUPERVISOR_ACK_'+i,310);
  }
  for(let i=0;i<120;i++){
    const state=(await api('/api/managed-parents/'+p.id)).data;
    if(state.status==='ready' && state.deliveries.every(d=>d.status==='success'))break;
    await new Promise(r=>setTimeout(r,10));
  }
  const ended=(await api('/api/managed-parents/'+p.id)).data;
  assert.equal(ended.status,'ready');
  assert.equal(ended.deliveries.length,3);
  assert.deepEqual(ended.deliveries.map(x=>x.status),['success','success','success']);
  assert.equal((await api('/api/agents')).data.length,4);
  assert.equal(requests.filter(x=>x.prompt?.includes('[YANZI_AGENT_BOOTSTRAP_V1]')).length,4);
  assert.equal(requests.filter(x=>x.taskId===p.initialJobId).length,1);
});
