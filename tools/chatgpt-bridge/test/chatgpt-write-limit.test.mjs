import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { createBridge } from '../server.mjs';

const wait=async(predicate,ms=1600)=>{
 const end=Date.now()+ms;
 while(Date.now()<end){if(predicate())return;await new Promise(r=>setTimeout(r,8))}
 throw new Error('test_wait_timeout');
};
test('production-style write limit serializes three ChatGPT writes without blocking read status',async t=>{
 const bridge=await createBridge({directory:mkdtempSync(join(tmpdir(),'yanzi-account-serial-')),
   port:0,allowTestSocket:true,maxChatGptWrites:1});
 t.after(()=>bridge.close());
 const ws=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
 await once(ws,'open');t.after(()=>{if(ws.readyState===WebSocket.OPEN)ws.close()});
 const inbox=[];ws.on('message',raw=>inbox.push(JSON.parse(raw.toString())));
 ws.send(JSON.stringify({type:'register',version:'0.5.66'}));
 const jobs=[];
 for(let i=0;i<3;i++)jobs.push(bridge.enqueue({action:'chatgpt_send',
   prompt:'SERIAL_CHILD_'+i,temporary:false}));
 const read=bridge.enqueue({action:'chatgpt_status'});
 await wait(()=>inbox.some(x=>x.taskId===read.id));
 assert.equal(inbox.filter(x=>jobs.some(j=>j.id===x.taskId)).length,1);
 assert.equal(bridge.state.jobs.find(j=>j.id===jobs[1].id).status,'queued');
 for(let i=0;i<3;i++){
  await wait(()=>inbox.some(x=>x.taskId===jobs[i].id));
  assert.equal(inbox.filter(x=>jobs.some(j=>j.id===x.taskId)).length,i+1);
  ws.send(JSON.stringify({type:'task_response',taskId:jobs[i].id,
    status:'success',data:{text:'ACK_'+i}}));
 }
 await wait(()=>jobs.every(j=>bridge.state.jobs.find(x=>x.id===j.id)?.status==='success'));
 assert.equal(inbox.filter(x=>jobs.some(j=>j.id===x.taskId)).length,3);
});
test('blocked supervisor delivery is explicitly parked, not left queued forever',async t=>{
 const bridge=await createBridge({directory:mkdtempSync(join(tmpdir(),'yanzi-parent-blocked-')),
   port:0,allowTestSocket:true,maxChatGptWrites:1});
 t.after(()=>bridge.close());
 const parentId='aa111111-bb22-cc33-dd44-eeeeeeeeeeee';
 const jobId='bb111111-bb22-cc33-dd44-eeeeeeeeeeee';
 const delivery={returnJobId:jobId,status:'queued'};
 bridge.state.managedParents.push({id:parentId,status:'needs_review',deliveries:[delivery]});
 bridge.state.jobs.push({id:jobId,action:'chatgpt_subagent_continue',
   task:{action:'chatgpt_subagent_continue',prompt:'result',expectedConversationId:'some-conv',
     expectedUrl:'https://chatgpt.com/c/some-conv'},
   status:'queued',source:'managed-parent-return:'+parentId+':child-1'});
 const ws=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
 await once(ws,'open');t.after(()=>{if(ws.readyState===WebSocket.OPEN)ws.close()});
 ws.send(JSON.stringify({type:'register',version:'0.5.66'}));
 bridge.enqueue({action:'chatgpt_status'});
 await wait(()=>bridge.state.jobs.find(j=>j.id===jobId).status==='needs_review');
 assert.equal(delivery.status,'needs_review');
 assert.equal(bridge.state.jobs.find(j=>j.id===jobId).message,'parent_needs_review_delivery_not_sent');
});
