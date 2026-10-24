import test from 'node:test';
import assert from 'node:assert/strict';

const base='http://127.0.0.1:43830';
test('health identifies live Ego API',async()=>{
  const r=await fetch(base+'/health');assert.equal(r.status,200);
  const b=await r.json();assert.equal(b.ok,true);assert.equal(b.egoConnected,true);
});
test('workbench renders controls',async()=>{
  const r=await fetch(base+'/');assert.equal(r.status,200);
  assert.match(await r.text(),/Agent 团队工作台/);
});
test('refuses unsafe origin',async()=>{
  const r=await fetch(base+'/api/runs',{headers:{Origin:'https://other.example'}});
  assert.equal(r.status,403);
});
test('refuses writes without explicit request header',async()=>{
  const r=await fetch(base+'/api/runs',{method:'POST',
    headers:{'Content-Type':'application/json'},
    body:JSON.stringify({requestKey:'test-safe-absent',goal:'test',children:['a']})});
  assert.equal(r.status,403);
});
test('validates team cardinality',async()=>{
  const r=await fetch(base+'/api/runs',{method:'POST',
    headers:{'Content-Type':'application/json','X-Yanzi-Request':'1'},
    body:JSON.stringify({requestKey:'test-invalid-children',goal:'test',children:[]})});
  assert.equal(r.status,400);
});
test('duplicate requestKey is idempotent and cannot resend',async(t)=>{
  const runs=await (await fetch(base+'/api/runs')).json();
  const previous=runs.find(r=>r.requestKey&&r.goal&&Array.isArray(r.seeds));
  if(!previous){t.skip('No existing run; refusing to submit a new ChatGPT task for a test');return;}
  const r=await fetch(base+'/api/runs',{method:'POST',
    headers:{'Content-Type':'application/json','X-Yanzi-Request':'1'},
    body:JSON.stringify({requestKey:previous.requestKey,goal:previous.goal,children:previous.seeds})});
  assert.equal(r.status,200);
  const result=await r.json();assert.equal(result.id,previous.id);
});
test('complete-summary verification refuses unauthorized writes',async()=>{
  const r=await fetch(base+'/api/runs/00000000-0000-0000-0000-000000000000/verify-summary',
    {method:'POST'});
  assert.equal(r.status,403);
});
test('complete-summary verification rejects missing run',async()=>{
  const r=await fetch(base+'/api/runs/00000000-0000-0000-0000-000000000000/verify-summary',
    {method:'POST',headers:{'X-Yanzi-Request':'1'}});
  assert.equal(r.status,404);
});
