import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { createBridge, validateTask } from '../server.mjs';

async function setup(t, options = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'yanzi-chatgpt-test-'));
  const bridge = await createBridge({ directory, port: 0, allowTestSocket: true, ...options });
  t.after(() => bridge.close());
  const request = async (path, method = 'GET', value, headers = {}) => {
    const response = await fetch(bridge.origin + path, { method, headers: { Authorization: 'Bearer ' + bridge.token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json', ...headers }, body: value ? JSON.stringify(value) : undefined });
    return { status: response.status, data: await response.json() };
  };
  return { bridge, request, directory };
}
const send = { action: 'chatgpt_send', prompt: '只回复测试成功', timeoutSeconds: 10 };

test('validates prompt, operation and timeout bounds', () => {
  assert.throws(() => validateTask({ action: 'execute_script' }));
  assert.throws(() => validateTask({ ...send, prompt: ' ' }));
  assert.throws(() => validateTask({ ...send, timeoutSeconds: 1201 }));
  assert.throws(() => validateTask({ ...send, tabId: '1' }));
  assert.equal(validateTask(send).prompt, send.prompt);
  assert.equal(validateTask(send).tabPolicy, 'reuse');
  assert.equal(validateTask(send).temporary, true);
  assert.equal(validateTask({ ...send, temporary: false }).temporary, false);
  assert.throws(() => validateTask({ ...send, temporary: 'true' }));
  assert.throws(() => validateTask({ ...send, tabPolicy: 'unknown' }));
  assert.throws(() => validateTask({ ...send, closeAfter: 'true' }));
  assert.throws(() => validateTask({ action: 'chatgpt_close' }));
  assert.equal(validateTask({ action: 'chatgpt_cleanup' }).action, 'chatgpt_cleanup');
});
test('rejects unauthenticated, cross-origin and missing CSRF header requests', async t => {
  const { bridge, request } = await setup(t);
  assert.equal((await fetch(bridge.origin + '/api/jobs')).status, 401);
  assert.equal((await request('/api/jobs', 'POST', send, { Origin: 'https://evil.example' })).status, 403);
  assert.equal((await request('/api/jobs', 'POST', send, { 'X-Bridge-Request': '' })).status, 403);
  const page = await fetch(bridge.origin);
  assert.match(page.headers.get('set-cookie'), /HttpOnly; SameSite=Strict/);
  assert.ok(!(await page.text()).includes(bridge.token));
  assert.equal((await request('/api/jobs', 'POST', send)).status, 202);
});
test('queues offline tasks, dispatches concurrently and correlates results', async t => {
  const { bridge, request, directory } = await setup(t);
  const jobs = [];
  for (let i = 0; i < 5; i++) {
    jobs.push((await request('/api/jobs', 'POST', {
      ...send,
      prompt: '并发测试-' + i
    })).data);
  }

  assert.ok(jobs.every(job => job.status === 'queued'));

  const ws = new WebSocket(
    bridge.origin.replace('http:', 'ws:') + '/v1/browser/ws'
  );
  await once(ws, 'open');

  const received = [];
  const collect = raw => {
    const message = JSON.parse(raw.toString());
    if (message.type === 'task_request') received.push(message);
  };
  ws.on('message', collect);

  ws.send(JSON.stringify({ type: 'register', version: '0.5.39' }));

  const waitUntil = async (predicate, timeoutMs = 1000) => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 5));
    }
    throw new Error('test wait timeout');
  };

  await waitUntil(() => received.length === 4);

  const firstFourIds = new Set(received.map(item => item.taskId));
  assert.deepEqual(
    firstFourIds,
    new Set(jobs.slice(0, 4).map(job => job.id))
  );
  assert.equal(
    bridge.state.jobs.filter(job => job.status === 'running').length,
    4
  );
  assert.equal(
    bridge.state.jobs.find(job => job.id === jobs[4].id).status,
    'queued'
  );

  ws.send(JSON.stringify({
    type: 'task_response',
    taskId: 'wrong-id',
    status: 'success'
  }));
  assert.equal(
    bridge.state.jobs.find(job => job.id === jobs[0].id).status,
    'running'
  );

  ws.send(JSON.stringify({
    type: 'task_response',
    taskId: jobs[0].id,
    status: 'success',
    data: { text: '测试成功', tabId: 7 }
  }));

  await waitUntil(() => received.length === 5);
  assert.equal(received[4].taskId, jobs[4].id);
  assert.equal(
    bridge.state.jobs.find(job => job.id === jobs[4].id).status,
    'running'
  );

  const stored = JSON.parse(readFileSync(join(directory, 'state.json')));
  assert.equal(
    stored.jobs.find(job => job.id === jobs[0].id).data.text,
    '测试成功'
  );

  ws.close();
  await once(ws, 'close');
  await new Promise(resolve => setTimeout(resolve, 20));

  for (const job of jobs.slice(1)) {
    assert.equal(
      (await request('/api/jobs/' + job.id)).data.status,
      'interrupted'
    );
  }
});
test('one-off, interval, paused and named event schedules', async t => {
  let time = Date.now();
  const { bridge, request } = await setup(t, { now: () => time });
  const at = (await request('/api/schedules', 'POST', { task: send, at: new Date(time + 1000).toISOString() })).data;
  const interval = (await request('/api/schedules', 'POST', { task: send, intervalSeconds: 60 })).data;
  const event = (await request('/api/schedules', 'POST', { task: send, event: 'file.changed' })).data;
  assert.equal((await request('/api/schedules', 'POST', { task: send, intervalSeconds: 1 })).status, 400);
  assert.equal((await request('/api/schedules', 'POST', { task: send, event: 'x', intervalSeconds: 60 })).status, 400);
  time += 3600000; bridge.tick(); bridge.tick();
  assert.equal(bridge.state.jobs.length, 2);
  assert.equal(bridge.state.schedules.find(s => s.id === at.id).enabled, false);
  assert.ok(bridge.state.schedules.find(s => s.id === interval.id).nextAt > time);
  assert.equal((await request('/api/events', 'POST', { name: 'file.changed' })).data.jobs.length, 1);
  await request('/api/schedules/' + event.id, 'PATCH', { enabled: false });
  assert.equal((await request('/api/events', 'POST', { name: 'file.changed' })).data.jobs.length, 0);
});
test('marks in-flight work interrupted on restart without resending', async t => {
  const directory = mkdtempSync(join(tmpdir(), 'yanzi-chatgpt-recovery-'));
  writeFileSync(join(directory, 'state.json'), JSON.stringify({ jobs: [{ id: 'old', status: 'running', task: send }], schedules: [] }));
  const bridge = await createBridge({ directory, port: 0 });
  t.after(() => bridge.close());
  assert.equal(bridge.state.jobs[0].status, 'interrupted');
});

test('origin binding routes feedback to exact conversation and deduplicates deliveries', async t => {
  const {bridge,request}=await setup(t);
  const url='https://chatgpt.com/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee';
  const ws=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(ws,'open');
  const received=[];
  ws.on('message',raw=>received.push(JSON.parse(raw.toString())));
  ws.send(JSON.stringify({type:'register',version:'0.5.50'}));
  ws.send(JSON.stringify({type:'origin_bind',requestId:'origin-test-1',tabId:44,url,
    conversationId:'af123456-bb22-cc33-dd44-eeeeeeeeeeee',temporary:false}));
  const until=async predicate=>{
    const stop=Date.now()+1800;
    while(Date.now()<stop){
      if(predicate())return;
      await new Promise(r=>setTimeout(r,10));
    }
    throw new Error('test message timeout');
  };
  await until(()=>received.some(x=>x.type==='origin_bind_ack'));
  const ack=received.find(x=>x.type==='origin_bind_ack');
  assert.equal(ack.ok,true);
  assert.equal(ack.binding.url,url);
  assert.equal((await request('/api/origins')).data[0].id,ack.binding.id);

  const feedback={originId:ack.binding.id,deliveryKey:'task-0001:round-1',
    text:'[燕子质量回传] 7/7 通过；请继续下一轮验收。'};
  const posted=await request('/api/feedback','POST',feedback);
  assert.equal(posted.status,202);
  assert.equal(posted.data.duplicate,false);
  await until(()=>received.some(x=>x.type==='task_request' && x.taskId===posted.data.jobId));
  const command=received.find(x=>x.type==='task_request' && x.taskId===posted.data.jobId);
  assert.equal(command.action,'chatgpt_feedback_send');
  assert.equal(command.expectedUrl,url);
  assert.equal(command.newChat,false);
  assert.equal(command.temporary,false);
  assert.equal(command.closeAfter,false);
  assert.equal(command.prompt,feedback.text);
  const duplicate=await request('/api/feedback','POST',feedback);
  assert.equal(duplicate.status,200);
  assert.equal(duplicate.data.duplicate,true);
  assert.equal(duplicate.data.jobId,posted.data.jobId);
  assert.equal(received.filter(x=>x.type==='task_request').length,1);
  assert.equal((await request('/api/feedback','POST',
    {...feedback,text:'different message'})).status,400);
  ws.send(JSON.stringify({type:'task_response',taskId:posted.data.jobId,status:'success',data:{
    tabId:44,url,text:'已收到回传',conversationId:'af123456-bb22-cc33-dd44-eeeeeeeeeeee'
  }}));
  await until(()=>bridge.state.jobs.find(j=>j.id===posted.data.jobId)?.status==='success');
  assert.equal((await request('/api/jobs/'+posted.data.jobId)).data.status,'success');
  ws.close(); await once(ws,'close');
});

test('rejects invalid origin and unbound feedback without dispatch', async t=>{
  const {bridge,request}=await setup(t);
  const originId='b0000000-bb22-cc33-dd44-eeeeeeeeeeee';
  assert.equal((await request('/api/feedback','POST',{
    originId,deliveryKey:'valid-00001',text:'must not send'
  })).status,400);
  const ws=new WebSocket(bridge.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(ws,'open');
  const reply=once(ws,'message');
  ws.send(JSON.stringify({type:'origin_bind',requestId:'bad-url',
    tabId:9,url:'https://evil.example/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee'}));
  const [raw]=await reply;
  assert.equal(JSON.parse(raw.toString()).ok,false);
  assert.equal((await request('/api/origins')).data.length,0);
  ws.close();await once(ws,'close');
});

test('origin route survives bridge restart; feedback is not reissued twice', async t=>{
  const directory=mkdtempSync(join(tmpdir(),'yanzi-feedback-resume-'));
  const first=await createBridge({directory,port:0,allowTestSocket:true});
  const ws=new WebSocket(first.origin.replace('http:','ws:')+'/v1/browser/ws');
  await once(ws,'open');
  const ackPromise=once(ws,'message');
  ws.send(JSON.stringify({type:'origin_bind',requestId:'persist-1',tabId:19,
    url:'https://chatgpt.com/c/af123456-bb22-cc33-dd44-eeeeeeeeeeee'}));
  const [raw]=await ackPromise;
  const binding=JSON.parse(raw.toString()).binding;
  assert.ok(binding?.id);
  ws.close();await once(ws,'close');
  await first.close();

  const second=await createBridge({directory,port:0,allowTestSocket:true});
  t.after(()=>second.close());
  const headers={Authorization:'Bearer '+second.token,'X-Bridge-Request':'1','Content-Type':'application/json'};
  const origins=await fetch(second.origin+'/api/origins',{headers});
  assert.equal((await origins.json())[0].id,binding.id);
  const payload={originId:binding.id,deliveryKey:'persist-round-1',text:'重启后待发送的验收反馈'};
  const create=await fetch(second.origin+'/api/feedback',{
    method:'POST',headers,body:JSON.stringify(payload)
  });
  const firstJob=await create.json();
  assert.equal(create.status,202);
  const retry=await fetch(second.origin+'/api/feedback',{
    method:'POST',headers,body:JSON.stringify(payload)
  });
  assert.equal(retry.status,200);
  assert.equal((await retry.json()).jobId,firstJob.jobId);
  assert.equal(second.state.jobs.filter(j=>j.source.startsWith('quality-feedback:')).length,1);
});

test('network probe validates scoped metadata-only options',()=>{
  const valid=validateTask({action:'chatgpt_network_probe',tabId:71,
    durationMs:3000,publicProbe:true});
  assert.equal(valid.durationMs,3000);
  assert.equal(valid.publicProbe,true);
  assert.equal(valid.tabId,71);
  assert.throws(()=>validateTask({action:'chatgpt_network_probe'}),/tab_id/);
  assert.throws(()=>validateTask({action:'chatgpt_network_probe',tabId:71,
    durationMs:18000}),/duration_invalid/);
  assert.throws(()=>validateTask({action:'chatgpt_network_probe',tabId:71,
    publicProbe:'true'}),/public_probe_invalid/);
});
