import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {YanziBridge} from '../src/bridge.js';
const cap={name:'wechat.fileTransfer.sendText',audience:'user',description:'Send to file transfer assistant',permissions:['device.message.send'],inputSchema:{type:'object',properties:{text:{type:'string',minLength:1}},required:['text'],additionalProperties:false}};
async function fixture(t,fetchImpl) {
  const dir=await fs.mkdtemp(path.join(os.tmpdir(),'yanzi-mcp-test-'));
  t.after(()=>fs.rm(dir,{recursive:true,force:true}));
  const settingsFile=path.join(dir,'settings.json');
  await fs.writeFile(settingsFile,JSON.stringify({agentApiPort:42980,agentApiToken:'private-test-token'}));
  return {bridge:new YanziBridge({settingsFile,fetchImpl}),settingsFile};
}
test('validates payload before sending, forwards Chinese exactly, never retries uncertain sends',async t=> {
  let calls=0;
  const {bridge}=await fixture(t,async(url,opts)=> {
    if(url.endsWith('/v1/agent/catalog'))return Response.json({hostCapabilities:[cap],extensions:[]});
    calls++;
    assert.deepEqual(JSON.parse(opts.body),{name:cap.name,payload:{text:'你好燕子'}});
    throw new Error('uncertain write with private-test-token');
  });
  await assert.rejects(()=>bridge.call('yanzi_wechat_fileTransfer_sendText',{}),/Invalid arguments/);
  assert.equal(calls,0);
  await assert.rejects(()=>bridge.call('yanzi_wechat_fileTransfer_sendText',{text:'你好燕子'}),e=>!e.message.includes('private-test-token'));
  assert.equal(calls,1);
});
test('normalizes empty schemas, filters non-user capabilities and rejects name collisions',async t=> {
  const {bridge}=await fixture(t,async()=>Response.json({hostCapabilities:[{...cap,name:'music.next',inputSchema:{}},{...cap,name:'admin.hidden',audience:'internal'}],extensions:[]}));
  const found=await bridge.discover();
  assert.equal(found.size,1);
  assert.equal(found.get('yanzi_music_next').tool.inputSchema.type,'object');
  bridge.fetch=async()=>Response.json({hostCapabilities:[{...cap,name:'a.b'},{...cap,name:'a_b'}],extensions:[]});
  await assert.rejects(()=>bridge.discover(true),/collision/);
});

test('discovers stopped extension declarations and calls the unified endpoint with their real schema',async t=> {
  const extensionCap={...cap,name:'wechat.messages.attachmentStatus',providerExtensionId:'wechat-annotator',available:false,onDemand:true,inputSchema:{type:'object',properties:{requestId:{type:'string',minLength:8}},required:['requestId'],additionalProperties:false}};
  let invokes=0;
  const {bridge}=await fixture(t,async(url,opts)=> {
    if(url.endsWith('/v1/agent/catalog'))return Response.json({hostCapabilities:[cap],extensions:[{id:'wechat-annotator',isRunning:false,capabilities:[extensionCap]}]});
    assert.ok(url.endsWith('/v1/capabilities/invoke'));
    assert.deepEqual(JSON.parse(opts.body),{name:extensionCap.name,payload:{requestId:'receipt-123'}});
    invokes++;
    return Response.json({success:true,data:{status:'confirmed'}});
  });
  const found=await bridge.discover();
  assert.equal(found.size,2);
  assert.deepEqual(found.get('yanzi_wechat_messages_attachmentStatus').tool.inputSchema,extensionCap.inputSchema);
  await assert.rejects(()=>bridge.call('yanzi_wechat_messages_attachmentStatus',{}),/Invalid arguments/);
  assert.equal(invokes,0);
  assert.equal((await bridge.call('yanzi_wechat_messages_attachmentStatus',{requestId:'receipt-123'})).data.status,'confirmed');
  assert.equal(invokes,1);
});

test('a newly installed capability refreshes discovery without retrying its invocation',async t=> {
  let installed=false, discoveries=0, invokes=0;
  const fresh={...cap,name:'example.new'};
  const {bridge}=await fixture(t,async(url)=> {
    if(url.endsWith('/v1/agent/catalog')){discoveries++;return Response.json({hostCapabilities:[cap],extensions:installed?[{capabilities:[fresh]}]:[]});}
    invokes++;throw new Error('uncertain call');
  });
  await bridge.discover();installed=true;
  await assert.rejects(()=>bridge.call('yanzi_example_new',{text:'test'}),/uncertain call/);
  assert.equal(discoveries,2);
  assert.equal(invokes,1);
});
test('uses rotated host port and token without restart; rejects invalid ports',async t=> {
  const {bridge,settingsFile}=await fixture(t,async(url,opts)=>Response.json({url,token:opts.headers['X-Yanzi-Token']}));
  await fs.writeFile(settingsFile,JSON.stringify({agentApiPort:12345,agentApiToken:'rotated-private-token'}));
  const result=await bridge.call('yanzi_ping');
  assert.match(result.url,/127.0.0.1:12345/);
  assert.equal(result.token,'[redacted]');
  await fs.writeFile(settingsFile,JSON.stringify({agentApiPort:0,agentApiToken:'private-test-token'}));
  await assert.rejects(()=>bridge.call('yanzi_ping'),/incomplete/);
});

test('assigns a stable request number before a native send and preserves it on uncertain transport failure',async t=> {
  let requestId,invokes=0;
  const send={...cap,name:'chat.send',inputSchema:{...cap.inputSchema,properties:{...cap.inputSchema.properties,requestId:{type:'string',minLength:8}}}};
  const {bridge}=await fixture(t,async(url,opts)=> {
    if(url.endsWith('/v1/agent/catalog'))return Response.json({hostCapabilities:[send],extensions:[]});
    invokes++;requestId=JSON.parse(opts.body).payload.requestId;
    assert.match(requestId,/^[a-f0-9-]{36}$/);
    throw new Error('connection lost');
  });
  await assert.rejects(()=>bridge.call('yanzi_chat_send',{text:'fixture'}),e=>e.message.includes(requestId)&&e.message.includes('task.status'));
  assert.equal(invokes,1);
});
