'use strict';
const assert = require('node:assert/strict');
const {test} = require('node:test');
const {Readable} = require('node:stream');
const {TOOLS, executeToolConversation, createHandler, validWorkspacePath} = require('../tool-broker.cjs');

function sse(output) {
  const data = 'data: ' + JSON.stringify({type: 'response.completed', response: {output}}) + '\n\n';
  return {ok: true, body: Readable.from([Buffer.from(data)])};
}
test('tool catalog is read-only and limited', () => {
  assert.deepEqual(TOOLS.map(t => t.name), ['server_status_get','server_files_list']);
  for (const path of ['../etc', '/etc', '.git', '.env', 'x/../y', 'C:\\secret', '\\windows']) {
    assert.equal(validWorkspacePath(path), false, path);
  }
  assert.equal(validWorkspacePath('yanzi/docs'), true);
});
test('model calls tool, tool executes, then model returns answer', async () => {
  let upstream = 0, invoked = 0;
  const result = await executeToolConversation({
    model:'mock-model', input:'列出工作区', accessToken:'mock',
    fetchImpl:async (url,opts) => {
      if (url.includes('127.0.0.1')) {
        invoked++;
        const body=JSON.parse(opts.body);
        assert.deepEqual(body,{name:'server.files.list',arguments:{path:'.'}});
        return {ok:true,json:async()=>({ok:true,data:{path:'.',items:[{name:'yanzi',type:'directory'}]}})};
      }
      upstream++;
      const body=JSON.parse(opts.body);
      assert.equal(body.store,false);
      assert.equal(body.tools.length,2);
      if(upstream===1) return sse([{type:'function_call',name:'server_files_list',arguments:'{"path":"."}',call_id:'call_1'}]);
      assert.equal(body.input.at(-1).type,'function_call_output');
      return sse([{type:'message',role:'assistant',content:[{type:'output_text',text:'工作区包含 yanzi 目录'}]}]);
    }
  });
  assert.equal(result.ok,true);
  assert.equal(result.toolCalls.length,1);
  assert.equal(invoked,1);
  assert.match(result.text,/yanzi/);
});
test('invalid path never reaches server node', async () => {
  let invoked=0;
  const result=await executeToolConversation({model:'mock',input:'list',accessToken:'mock',fetchImpl:async(url,opts)=>{
    if(url.includes('127.0.0.1')) {invoked++;throw Error('invalid call');}
    const body=JSON.parse(opts.body);
    if(body.input.length===1) return sse([{type:'function_call',name:'server_files_list',arguments:'{"path":"../../etc"}',call_id:'call_2'}]);
    assert.match(body.input.at(-1).output,/workspace_path_denied/);
    return sse([{type:'message',role:'assistant',content:[{type:'output_text',text:'已拒绝越界请求'}]}]);
  }});
  assert.equal(invoked,0);
  assert.equal(result.toolCalls[0].ok,false);
});
test('handler stays disabled without flag', async () => {
  const old=process.env.YANZI_GATEWAY_TOOLS_ENABLED;
  delete process.env.YANZI_GATEWAY_TOOLS_ENABLED;
  let result;
  const handler=createHandler({readJson:()=>{throw Error('should not read');},sendJson:(_r,s,p)=>{result={s,p};}});
  await handler({},{});
  assert.equal(result.s,404);
  if(old===undefined) delete process.env.YANZI_GATEWAY_TOOLS_ENABLED;
  else process.env.YANZI_GATEWAY_TOOLS_ENABLED=old;
});

test('enabled handler returns tool result without exposing credentials', async () => {
  const previous = process.env.YANZI_GATEWAY_TOOLS_ENABLED;
  process.env.YANZI_GATEWAY_TOOLS_ENABLED='1';
  let status, responseBody;
  try {
    const handler=createHandler({
      readJson:async()=>({input:'查看服务器状态'}),
      sendJson:(_res,s,payload)=>{status=s;responseBody=payload;},
      usableCredentials:async()=>({access_token:'test-credential'}),
      upstreamModels:async()=>[{slug:'mock-model'}],
      selectDefaultModel:()=> 'mock-model',
      fetchImpl:async(url,opts)=>{
        if(url.includes('127.0.0.1')) return {ok:true,json:async()=>({ok:true,data:{host:{hostname:'sandbox-server'}}})};
        const body=JSON.parse(opts.body);
        if(body.input.length===1) return sse([{type:'function_call',name:'server_status_get',arguments:'{}',call_id:'status_call'}]);
        assert.equal(body.input.at(-1).type,'function_call_output');
        assert.match(body.input.at(-1).output,/sandbox-server/);
        return sse([{type:'message',role:'assistant',content:[{type:'output_text',text:'服务器运行正常'}]}]);
      }
    });
    await handler({},{});
    assert.equal(status,200);
    assert.equal(responseBody.ok,true);
    assert.equal(responseBody.toolCalls[0].name,'server_status_get');
    assert.match(responseBody.text,/服务器运行正常/);
    assert.equal(JSON.stringify(responseBody).includes('test-credential'),false);
  } finally {
    if(previous===undefined) delete process.env.YANZI_GATEWAY_TOOLS_ENABLED;
    else process.env.YANZI_GATEWAY_TOOLS_ENABLED=previous;
  }
});
