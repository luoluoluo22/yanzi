const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { runJob } = require('../worker.cjs');

async function fixture(t, prompt = '中文🙂') {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'agy-bridge-test-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const id = '12345678901234567890123456789012';
  const file = path.join(root, id + '.json');
  fs.writeFileSync(file, JSON.stringify({ id, prompt, status: 'pending' }));
  fs.writeFileSync(path.join(root, 'active.json'), JSON.stringify({ id }));
  const cli = path.join(root, 'cli.cjs');
  const cancelFile = path.join(root, id + '.cancel');
  fs.writeFileSync(cli, `const fs=require('fs'); const a=process.argv; const p=a[a.indexOf('-p')+1];
    if(p==='hang')setTimeout(()=>{},60000);
    else if(p==='error'){console.log(JSON.stringify({status:'ERROR',error:'fake failure'}));process.exitCode=1;}
    else if(p==='cancel-before-delivery'){console.log(JSON.stringify({status:'SUCCESS',conversation_id:'conversation-test',response:'should-not-deliver'}));fs.writeFileSync(${JSON.stringify(cancelFile)},'cancel');}
    else {const prev=a.indexOf('--conversation');
      console.log(JSON.stringify({status:'SUCCESS',conversation_id:'conversation-test',response:prev>=0?'remembered:'+a[prev+1]:p}));}`);
  const requests = [];
  const server = http.createServer((req, res) => {
    let body=''; req.on('data', d => body+=d); req.on('end',()=>{
      requests.push(JSON.parse(body)); res.setHeader('Content-Type','application/json');
      res.end(JSON.stringify({success:true,data:{messageId:'message-test',status:'pending'}}));
    });
  });
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  return { root, id, file, requests, options:{cliPath:process.execPath,cliPrefix:[cli],apiUrl:`http://127.0.0.1:${server.address().port}`,apiToken:'test-token'} };
}
test('Unicode reply, durable session and stable delivery id', async t => {
  const f = await fixture(t); const r = await runJob(f.file,f.options);
  assert.equal(r.response,'中文🙂'); assert.equal(r.status,'completed');
  assert.equal(r.deliveryStatus,'submitted'); assert.equal(f.requests[0].payload.requestId,`agy-${f.id}--conv--def--0`);
  assert.equal(JSON.parse(fs.readFileSync(path.join(f.root,'session.json'))).conversationId,'conversation-test');
  assert.equal(fs.existsSync(path.join(f.root,'active.json')),false);
  await assert.rejects(runJob(f.file,f.options));
});
test('Follow-up resumes exact conversation', async t => {
  const f=await fixture(t);
  fs.writeFileSync(path.join(f.root,'session.json'),JSON.stringify({conversationId:'previous-session'}));
  const r=await runJob(f.file,f.options); assert.equal(r.response,'remembered:previous-session');
});
test('CLI failure preserves old session and reports error', async t => {
  const f=await fixture(t,'error');
  fs.writeFileSync(path.join(f.root,'session.json'),JSON.stringify({conversationId:'previous-session'}));
  const r=await runJob(f.file,f.options); assert.equal(r.status,'failed'); assert.equal(r.error,'fake failure');
  assert.equal(JSON.parse(fs.readFileSync(path.join(f.root,'session.json'))).conversationId,'previous-session');
});
test('Timeout terminates child and unlocks task', async t => {
  const f=await fixture(t,'hang'); const r=await runJob(f.file,{...f.options,timeoutMs:300});
  assert.equal(r.status,'failed'); assert.match(r.error,/超时/); assert.equal(fs.existsSync(path.join(f.root,'active.json')),false);
});
test('Cancellation terminates child promptly and does not deliver an AI reply', async t => {
  const f=await fixture(t,'hang');
  const started=Date.now();
  const timer=setTimeout(()=>fs.writeFileSync(path.join(f.root,f.id+'.cancel'),'cancel'),100);
  const r=await runJob(f.file,f.options);clearTimeout(timer);
  assert.equal(r.status,'cancelled');assert.match(r.error,/取消/);
  assert.equal(r.deliveryStatus,'cancelled');
  assert.equal(f.requests.length,0);
  assert.ok(Date.now()-started < 5000, 'cancel should kill the CLI process instead of waiting for it');
  assert.equal(fs.existsSync(path.join(f.root,'active.json')),false);
});
test('Cancellation after CLI output but before delivery suppresses the reply', async t => {
  const f=await fixture(t,'cancel-before-delivery');
  const r=await runJob(f.file,f.options);
  assert.equal(r.status,'cancelled');
  assert.equal(r.deliveryStatus,'cancelled');
  assert.equal(f.requests.length,0);
  assert.equal(fs.existsSync(path.join(f.root,'active.json')),false);
});

test('Delivery failure retains completed response without rerunning CLI', async t => {
  const f=await fixture(t); const r=await runJob(f.file,{...f.options,apiUrl:'http://127.0.0.1:1'});
  assert.equal(r.status,'completed');assert.equal(r.deliveryStatus,'failed');
  assert.equal(fs.readFileSync(path.join(f.root,'last-result.txt'),'utf8'),'中文🙂');
});
test('Caller phone and conversation metadata override legacy target',async t=>{
  const f=await fixture(t);
  const job=JSON.parse(fs.readFileSync(f.file));job.replyDeviceId='android-caller';job.conversationId='agent:antigravity:desktop-test';
  fs.writeFileSync(f.file,JSON.stringify(job));
  await runJob(f.file,f.options);
  assert.equal(f.requests[0].payload.target,'android-caller');
  assert.equal(f.requests[0].payload.conversationId,job.conversationId);
});
test('CLI passes --dangerously-skip-permissions flag', async t => {
  const f = await fixture(t);
  fs.writeFileSync(path.join(f.root, 'cli.cjs'), `const a=process.argv;
    const hasSkip = a.includes('--dangerously-skip-permissions');
    console.log(JSON.stringify({status:'SUCCESS',conversation_id:'conversation-test',response:hasSkip?'skip-ok':'no-skip'}));`);
  const r = await runJob(f.file, f.options);
  assert.equal(r.response, 'skip-ok');
});
test('Empty response triggers clear diagnostic error', async t => {
  const f = await fixture(t);
  fs.writeFileSync(path.join(f.root, 'cli.cjs'), `console.log(JSON.stringify({status:'SUCCESS',conversation_id:'conversation-test',response:''}));`);
  const r = await runJob(f.file, f.options);
  assert.equal(r.status, 'failed');
  assert.match(r.error, /CLI (未生成文本回复|返回了空回复)/);
});


test('Uses per-device CLI path stored in job, without relying on the default install directory', async t => {
  const f = await fixture(t);
  const job = JSON.parse(fs.readFileSync(f.file, 'utf8'));
  job.cliPath = process.execPath;
  fs.writeFileSync(f.file, JSON.stringify(job));
  const { cliPath, ...options } = f.options;
  const result = await runJob(f.file, options);
  assert.equal(result.status, 'completed');
  assert.equal(result.response, '中文🙂');
});

test('stream-json publishes incremental snapshots before final durable message', async t => {
  const f = await fixture(t);
  const job = JSON.parse(fs.readFileSync(f.file, 'utf8'));
  job.replyDeviceId = 'android-stream-phone';
  job.conversationId = 'agent:antigravity:desktop-test:stream-session';
  fs.writeFileSync(f.file, JSON.stringify(job));
  fs.writeFileSync(path.join(f.root, 'cli.cjs'), `
    const emit=v=>process.stdout.write(JSON.stringify(v)+'\\n');
    emit({event:'init',conversation_id:'conversation-stream',init:{}});
    setTimeout(()=>emit({event:'step_update',step_update:{step_type:'agent_response',state:'ACTIVE',text_delta:'你'}}),20);
    setTimeout(()=>emit({event:'step_update',step_update:{step_type:'agent_response',state:'ACTIVE',text_delta:'好'}}),140);
    setTimeout(()=>emit({event:'result',result:{status:'SUCCESS',conversation_id:'conversation-stream',response:'你好'}}),260);
  `);
  const result = await runJob(f.file, {...f.options, streamIntervalMs:40});
  assert.equal(result.status, 'completed');
  const streamCalls = f.requests.filter(x => x.name === 'chat.stream');
  const finalCalls = f.requests.filter(x => x.name === 'chat.send');
  assert.ok(streamCalls.length >= 2, 'expected more than one transient stream snapshot');
  assert.equal(streamCalls[0].payload.state, 'start');
  assert.equal(streamCalls.at(-1).payload.text, '你好');
  assert.equal(finalCalls.length, 1);
  assert.equal(finalCalls[0].payload.target, 'android-stream-phone');
  assert.match(finalCalls[0].payload.text, /你好/);
  assert.ok(f.requests.findIndex(x => x.name === 'chat.stream') < f.requests.findIndex(x => x.name === 'chat.send'));
});
