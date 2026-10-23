import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {JSDOM} from 'jsdom';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../../..');
test('extension popup sample launches a real agent run request and renders protected conversation links',async t=>{
  const html=readFileSync(resolve(root,'browser-extension/popup/popup.html'),'utf8');
  const script=readFileSync(resolve(root,'browser-extension/popup/agent-console-popup.js'),'utf8');
  const dom=new JSDOM(html,{url:'chrome-extension://abcdefghabcdefghabcdefghabcdefgh/popup/popup.html',runScripts:'outside-only'});
  t.after(()=>dom.window.close());
  const calls=[];
  const run={id:'test-id',goal:'测试燕子多 Agent 自动回流：主管接收两个独立子对话的结果并汇总。',
    status:'running',parent:{id:'parent-id',status:'ready',url:'https://chatgpt.com/c/01234567-0000-4000-8000-000000000001',
      lastResponse:{text:'主管已就绪'},deliveries:[]},children:[
      {id:'child-a',title:'子 Agent 1',status:'ready',url:'https://chatgpt.com/c/01234567-0000-4000-8000-000000000002',lastResult:{text:'CHILD_SUM_12'}},
      {id:'child-b',title:'子 Agent 2',status:'starting',url:null}]};
  const opened=[];
  dom.window.chrome={
    runtime:{sendMessage:async request=>{
      calls.push(request);
      if(request.action==='list')return {ok:true,result:[]};
      if(request.action==='launch')return {ok:true,result:run};
      if(request.action==='detail')return {ok:true,result:run};
      return {ok:false,error:'unknown'};
    }},
    storage:{local:{get:async()=>({yanziAgentLastRunId:null}),set:async()=>{}}},
    tabs:{create:request=>opened.push(request)}
  };
  dom.window.eval(script);
  await new Promise(r=>setTimeout(r,15));
  dom.window.document.getElementById('agent-demo').click();
  assert.match(dom.window.document.getElementById('agent-children').value,/CHILD_SUM_12/);
  const form=dom.window.document.getElementById('agent-launch-form');
  form.dispatchEvent(new dom.window.Event('submit',{bubbles:true,cancelable:true}));
  await new Promise(r=>setTimeout(r,25));
  const launch=calls.find(x=>x.action==='launch');
  assert.equal(launch.payload.children.length,2);
  assert.match(launch.payload.goal,/测试燕子/);
  assert.match(dom.window.document.getElementById('agent-results').textContent,/CHILD_SUM_12/);
  const openButton=[...dom.window.document.querySelectorAll('.agent-run-entry button')].find(x=>!x.disabled);
  assert.ok(openButton);
  openButton.click();
  assert.equal(opened[0].url,run.parent.url);
  assert.equal(dom.window.document.getElementById('agent-run-state').textContent,'执行中');
});
