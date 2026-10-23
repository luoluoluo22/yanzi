import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {once} from 'node:events';
import {WebSocket} from 'ws';
import {createBridge} from '../server.mjs';
import {validateAgentRun} from '../agent-console.mjs';
const delay=ms=>new Promise(r=>setTimeout(r,ms));
async function harness(t) {
  const bridge=await createBridge({port:0,directory:mkdtempSync(join(tmpdir(),'yanzi-agent-console-')),allowTestSocket:true});
  t.after(()=>bridge.close());
  const headers={Authorization:'Bearer '+bridge.token,'Content-Type':'application/json','X-Bridge-Request':'1'};
  const http=async(path,method='GET',body)=>{
    const r=await fetch(bridge.origin+path,{method,headers,body:body?JSON.stringify(body):undefined});
    return {code:r.status,data:await r.json()};
  };
  const socket=new WebSocket(bridge.origin.replace('http','ws')+'/v1/browser/ws');
  await once(socket,'open');t.after(()=>socket.close());
  const messages=[];
  socket.on('message',raw=>messages.push(JSON.parse(raw.toString())));
  socket.send(JSON.stringify({type:'register',version:'0.5.74'}));
  let nextId=0;
  async function rpc(action,payload) {
    const requestId='agent_rpc_'+(++nextId).toString().padStart(3,'0');
    socket.send(JSON.stringify({type:'agent_console_request',requestId,action,payload}));
    return waitFor(x=>x.type==='agent_console_response'&&x.requestId===requestId);
  }
  async function waitFor(predicate) {
    const deadline=Date.now()+4500;
    while(Date.now()<deadline) {
      const found=messages.find(predicate);
      if(found)return found;
      await delay(10);
    }
    throw new Error('timeout waiting for expected bridge message');
  }
  return {bridge,http,rpc,waitFor,socket,messages};
}
function ack(socket,task,marker,num) {
  const conversationId='abcdef01-0000-4000-8000-'+String(num).padStart(12,'0');
  socket.send(JSON.stringify({type:'task_response',taskId:task.taskId,status:'success',
    data:{url:'https://chatgpt.com/c/'+conversationId,conversationId,tabId:500+num,
      temporary:false,text:marker,messageId:'assistant-'+num}}));
}
test('agent run validates input, rejects shell-like and oversized instructions before dispatch',()=>{
  assert.throws(()=>validateAgentRun({goal:'hello',children:['foo'],requestKey:'x'}),/request_key/);
  assert.throws(()=>validateAgentRun({goal:'hello',children:[],requestKey:'valid-001'}),/requires_1_to_3/);
  assert.throws(()=>validateAgentRun({goal:'hello',children:['1','2','3','4'],requestKey:'valid-002'}),/requires_1_to_3/);
  assert.deepEqual(validateAgentRun({goal:' test ',children:[' child 1 '],requestKey:'valid-003'}).children,['child 1']);
});
test('extension WebSocket launches durable managed parent and 2 children; results actually return to parent',async t=>{
  const {http,rpc,waitFor,socket,messages}=await harness(t);
  const payload={requestKey:'popup-run-1',goal:'验证子任务回流',children:['计算5+7','翻译蓝色']};
  const first=await rpc('launch',payload);
  assert.equal(first.ok,true,JSON.stringify(first));
  assert.equal(first.result.children.length,2);
  assert.equal(first.result.parent.status,'starting');
  const id=first.result.id;
  const duplicate=await rpc('launch',payload);
  assert.equal(duplicate.result.id,id);
  const bad=await rpc('launch',{...payload,goal:'另一个目标'});
  assert.equal(bad.ok,false);assert.match(bad.error,/conflict/);
  const queued=await rpc('list');
  assert.equal(queued.result.length,1);
  const parentTask=await waitFor(x=>x.type==='task_request'&&x.taskId===
    (messages.find(m=>m.type==='task_request')?.taskId));
  assert.match(parentTask.prompt,/验证子任务回流/);
  assert.match(parentTask.prompt,/2 个子 Agent/);
  assert.equal(messages.filter(m=>m.type==='task_request'&&m.action==='chatgpt_send').length,1);
  ack(socket,parentTask,'主管已就绪',1);
  const childOne=await waitFor(m=>m.type==='task_request'&&m.prompt===payload.children[0]);
  assert.equal(childOne.action,'chatgpt_send');
  assert.equal(messages.filter(m=>m.type==='task_request'&&m.action==='chatgpt_send').length,2);
  ack(socket,childOne,'CHILD_SUM_12',2);
  const firstReturn=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue');
  assert.match(firstReturn.prompt,/燕子子 Agent 结果回传/);
  assert.equal(firstReturn.expectedConversationId,'abcdef01-0000-4000-8000-000000000001');
  ack(socket,firstReturn,'收到第一条回流',1);
  const childTwo=await waitFor(m=>m.type==='task_request'&&m.prompt===payload.children[1]);
  assert.equal(childTwo.action,'chatgpt_send');
  ack(socket,childTwo,'CHILD_COLOR_BLUE',3);
  const secondReturn=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_subagent_continue'&&x.taskId!==firstReturn.taskId);
  ack(socket,secondReturn,'收到第二条回流',1);
  let run;
  for(let i=0;i<100;i++) {
    run=(await rpc('detail',{id})).result;
    if(run.status==='completed')break;
    await delay(20);
  }
  assert.equal(run.status,'completed',JSON.stringify(run));
  assert.equal(run.parent.deliveries.length,2);
  assert.ok(run.parent.deliveries.every(d=>d.status==='success'));
  const byHttp=await http('/api/agent-runs/'+id);
  assert.equal(byHttp.code,200);assert.equal(byHttp.data.status,'completed');
  assert.equal(messages.filter(m=>m.type==='task_request'&&m.action==='chatgpt_send').length,3);
});

test('failed managed parent parks unsent child tasks without retrying ambiguous ChatGPT messages',async t=>{
  const {http,rpc,waitFor,socket}=await harness(t);
  const data=await rpc('launch',{requestKey:'popup-failure-1',goal:'需要先有主管',children:['任务A','任务B']});
  assert.equal(data.ok,true);
  const task=await waitFor(x=>x.type==='task_request'&&x.action==='chatgpt_send');
  socket.send(JSON.stringify({type:'task_response',taskId:task.taskId,status:'error',
    message:'uncertain_completion_after_send'}));
  let result;
  for(let i=0;i<100;i++){
    result=(await rpc('detail',{id:data.result.id})).result;
    if(result.status==='needs_review')break;
    await delay(10);
  }
  assert.equal(result.status,'needs_review');
  assert.equal(result.parent.status,'needs_review');
  assert.equal(result.children.length,2);
  assert.ok(result.children.every(x=>x.status==='needs_review'));
  const jobs=(await http('/api/jobs')).data;
  assert.equal(jobs.filter(j=>j.source.startsWith('subagent:')&&j.status==='queued').length,0);
});
