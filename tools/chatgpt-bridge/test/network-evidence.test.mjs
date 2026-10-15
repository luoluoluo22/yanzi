import test from 'node:test';
import assert from 'node:assert/strict';
import {classifyJobNetwork,sanitizeNetworkEvidence,isAllowedProgressStage}
  from '../network-evidence.mjs';
const observation=(started,conversation=0,stage='page_dispatch_started')=>({
  stage,latest:{tabId:71,available:true,started,
    classes:{conversation_api:conversation},methods:{POST:conversation}}
});
test('network metadata is limited to allowlisted categories, never URLs or secrets',()=>{
  const result=sanitizeNetworkEvidence({
    tabId:71,available:true,started:5,completed:4,failed:1,
    pending:0,classes:{conversation_api:2,evil:'private'},methods:{POST:5,secret:10},
    statusBands:{'2xx':4},requestUrl:'https://chatgpt.com/?token=secret',
    headers:{authorization:'top-secret'},requestBody:'private prompt'
  });
  assert.equal(result.started,5);
  assert.equal(result.classes.conversation_api,2);
  assert.equal(result.methods.POST,5);
  const serialized=JSON.stringify(result);
  for(const s of ['token','private','secret','requestUrl','authorization','headers','requestBody','evil'])
    assert.equal(serialized.includes(s),false);
  assert.throws(()=>sanitizeNetworkEvidence({tabId:0}),/network_evidence_invalid/);
});
test('successful webpage reply is distinguishable from mere network traffic',()=>{
  assert.equal(classifyJobNetwork({status:'success'}).status,'reply_confirmed');
  assert.equal(classifyJobNetwork({status:'running',networkObservation:observation(5,3)}).next,'wait');
  const pending=classifyJobNetwork({status:'timeout',networkObservation:observation(5,3)});
  assert.equal(pending.status,'conversation_network_activity');
  assert.equal(pending.next,'read_only_reconcile');
  assert.notEqual(pending.status,'reply_confirmed');
});
test('no traffic never proves no send, and a draft guard blocks any resubmission',()=>{
  const nothing=classifyJobNetwork({status:'error',message:'unknown'});
  assert.equal(nothing.status,'no_network_evidence');
  assert.equal(nothing.next,'stop_needs_review');
  const guard=classifyJobNetwork({status:'error',message:'输入框已有其他草稿，请先处理草稿',
    networkObservation:observation(22,4)});
  assert.equal(guard.next,'stop_preserve_draft');
  assert.equal(classifyJobNetwork({status:'timeout',networkObservation:observation(0)}).status,
    'dispatch_outcome_unknown');
});
test('unresponsive page and ordinary resource traffic are not promoted to message delivery',()=>{
  const hang=classifyJobNetwork({status:'error',message:'ChatGPT 页面渲染进程无响应'});
  assert.equal(hang.status,'renderer_or_load_blocked');
  const resource=classifyJobNetwork({status:'error',networkObservation:observation(4,0,'watch_started')});
  assert.equal(resource.status,'unattributed_network_activity');
  assert.equal(resource.next,'stop_needs_review');
  assert.equal(isAllowedProgressStage('page_reply_received'),true);
  assert.equal(isAllowedProgressStage('fetch_auth_header'),false);
});
