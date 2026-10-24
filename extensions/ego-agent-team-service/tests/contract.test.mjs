import test from 'node:test';
import assert from 'node:assert/strict';
const base='http://127.0.0.1:43830';
test('health identifies live Ego API',async()=>{const r=await fetch(base+'/health');assert.equal(r.status,200);const b=await r.json();assert.equal(b.ok,true);assert.equal(b.egoConnected,true);});
test('workbench renders controls',async()=>{const r=await fetch(base+'/');assert.equal(r.status,200);assert.match(await r.text(),/Agent 团队工作台/);});
test('refuses unsafe origin',async()=>{const r=await fetch(base+'/api/runs',{headers:{Origin:'https://other.example'}});assert.equal(r.status,403);});
test('refuses writes without explicit request header',async()=>{const r=await fetch(base+'/api/runs',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({requestKey:'test-safe-absent',goal:'test',children:['a']})});assert.equal(r.status,403);});
test('validates team cardinality',async()=>{const r=await fetch(base+'/api/runs',{method:'POST',headers:{'Content-Type':'application/json','X-Yanzi-Request':'1'},body:JSON.stringify({requestKey:'test-invalid-children',goal:'test',children:[]})});assert.equal(r.status,400);});
test('duplicate requestKey returns same live run without resending',async()=>{const old={requestKey:'ego-agent-team-run-20261024-1705',goal:'用真实浏览器构建主管和两个子Agent，由燕子汇总并核对输出。',children:['计算 5+7，只回复 CHILD_SUM_12','翻译 蓝色 为英文，只回复 CHILD_COLOR_BLUE']};const r=await fetch(base+'/api/runs',{method:'POST',headers:{'Content-Type':'application/json','X-Yanzi-Request':'1'},body:JSON.stringify(old)});assert.equal(r.status,200);const body=await r.json();assert.equal(body.id,'1f22b580-2b03-4887-8f97-e423a662b086');});
