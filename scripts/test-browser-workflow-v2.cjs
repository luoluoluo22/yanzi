const http = require('http');
const assert = require('assert');
const {chromium} = require('F:/Desktop/kaifa/ego-evaluation/node_modules/playwright-core');
global.chrome={};
const {storeFor}=require('../browser-extension/workflow-checkpoints.js');
global.yanziWorkflowCheckpointsFactory=storeFor;
const {createController}=require('../browser-extension/workflow-orchestrator.js');
const port=8886,base='http://127.0.0.1:'+port;
const server=http.createServer((req,res)=>{
 const u=new URL(req.url,base),id=u.searchParams.get('id')||'unknown';
 res.setHeader('Content-Type','text/html; charset=utf-8');res.setHeader('Cache-Control','no-store');
 if(u.pathname==='/start')return res.end('<!doctype html><h1>Start '+id+'</h1><a id="next" href="/next?id='+id+'">Next</a>');
 if(u.pathname==='/next')return res.end('<!doctype html><h1>Next '+id+'</h1><form id="form"><input id="token"><button id="submit">Finish</button></form><div id="result"></div><script>document.querySelector("form").onsubmit=e=>{e.preventDefault();document.querySelector("#result").textContent=document.querySelector("#token").value}</script>');
 if(u.pathname==='/snapshot')return res.end('<!doctype html><title>Snapshot test</title><h1>Semantic snapshot</h1><button id="plain" aria-label="Save draft">Save</button><input id="private" type="password" value="very-secret-password-666"><label for="field">Query</label><input id="field" value="another-private-value-999"><div id="counter">0</div><div id="host"></div><iframe src="/next?id=frame" title="Embedded data"></iframe><script>const root=document.querySelector("#host").attachShadow({mode:"open"});root.innerHTML="<button id=shadow-action>Shadow action</button>";root.querySelector("button").onclick=()=>document.querySelector("#counter").textContent="1";</script>');
 if(u.pathname==='/spa')return res.end('<!doctype html><h1>SPA home</h1><button id="spaButton">Go</button><script>document.querySelector("#spaButton").onclick=()=>{history.pushState({},"","/spa-done");document.querySelector("h1").innerText="SPA DONE"}</script>');
 if(u.pathname==='/spa-done')return res.end('<h1>SPA DONE</h1>');
 res.statusCode=404;res.end('Not found');
});
async function main(){
 await new Promise(resolve=>server.listen(port,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
 const context=await browser.newContext(),pages=new Map();let idSeed=1;
 const memory={};
 const chromeApi={
  storage:{local:{async get(key){return {[key]:memory[key]};},async set(obj){Object.assign(memory,obj);}}},
  tabs:{
   async create({url}){const id=idSeed++,page=await context.newPage();pages.set(id,page);await page.goto(url,{waitUntil:'domcontentloaded'});return {id,url};},
   async get(id){const page=pages.get(id);if(!page||page.isClosed())throw new Error('No tab');return{id,url:page.url()};},
   async update(id,{url}){const page=pages.get(id);await page.goto(url,{waitUntil:'domcontentloaded'});return{id,url};},
   async remove(id){const page=pages.get(id);if(page&&!page.isClosed())await page.close();pages.delete(id);}
  },
  scripting:{async executeScript({target,func,args}){const page=pages.get(target.tabId);if(!page)throw new Error('No page');return[{result:await page.evaluate(func,args[0])}];}}
 };
 const engine=createController(chromeApi),ids=['alpha','beta','gamma'];
 const task=id=>({taskId:id,url:base+'/start?id='+id,closeOnComplete:true,steps:[
  {type:'wait',selector:'h1'},{type:'click',selector:'#next',timeout:5000},
  {type:'waitForUrl',urlIncludes:'/next?id='+id},
  {type:'assert',selector:'h1',contains:'Next '+id,urlIncludes:'/next?id='+id},
  {type:'fill',selector:'#token',value:'TOKEN_'+id},
  {type:'click',selector:'#submit',expectNavigation:false},
  {type:'wait',selector:'#result'},{type:'assert',selector:'#result',contains:'TOKEN_'+id},
  {type:'scrape',selectors:{heading:'h1',token:'#result',missing:'.nothing'}}
 ]});
 try{
  const started=Date.now(),parallel=await Promise.all(ids.map(task).map(t=>engine.run(t)));
  console.log('PARALLEL',JSON.stringify({durationMs:Date.now()-started,result:parallel.map((r,i)=>({id:ids[i],status:r.status,reason:r.message,token:r.data?.token,steps:r.data?._workflow?.completedSteps}))}));
  assert(parallel.every((r,i)=>r.status==='success'&&r.data?.token?.[0]==='TOKEN_'+ids[i]&&r.data?._workflow?.completedSteps===9));
  assert.equal(pages.size,0,'closeOnComplete must clean all tabs');
  const failed=await engine.run({taskId:'failure',url:base+'/start?id=bad',closeOnComplete:true,steps:[
   {type:'click',selector:'#next',timeout:4000},{type:'assert',selector:'h1',contains:'This must fail'}
  ]});
  assert.equal(failed.status,'error');assert(failed.message.includes('Step 2'));
  assert.equal(pages.size,0);
  const spa=await engine.run({taskId:'spa',url:base+'/spa',closeOnComplete:true,steps:[
   {type:'click',selector:'#spaButton',expectNavigation:false},
   {type:'waitForUrl',urlIncludes:'/spa-done'},
   {type:'assert',selector:'h1',contains:'SPA DONE'}
  ]});
  assert.equal(spa.status,'success',spa.message);
  const navigated=await engine.run({taskId:'navigate',url:base+'/start?id=test',closeOnComplete:true,steps:[
   {type:'navigate',url:base+'/next?id=redirect',selector:'h1'},
   {type:'assert',selector:'h1',contains:'Next redirect'}
  ]});
  assert.equal(navigated.status,'success',navigated.message);
  const snap=await engine.run({taskId:'snapshot-private',url:base+'/snapshot?secret=token-in-query',closeOnComplete:true,steps:[
    {type:'wait',selector:'h1'},
    {type:'snapshot',limit:30,key:'semantic'},
    {type:'click',selector:'#host >>> #shadow-action',expectNavigation:false},
    {type:'assert',selector:'#counter',contains:'1'}
  ]});
  assert.equal(snap.status,'success',snap.message);
  assert(snap.data.semantic.controls.some(x=>x.role==='button'&&x.name==='Save draft'));
  assert(snap.data.semantic.controls.some(x=>x.name==='Shadow action'));
  assert.equal(snap.data.semantic.openShadowRoots,1);
  const shadowSelector=snap.data.semantic.controls.find(x=>x.name==='Shadow action')?.selector;
  assert(shadowSelector && shadowSelector.includes('>>>'),'Shadow selector must be generated');
  const replay=await engine.run({taskId:'shadow-generated',url:base+'/snapshot',closeOnComplete:true,steps:[
    {type:'click',selector:shadowSelector,expectNavigation:false},
    {type:'assert',selector:'#counter',contains:'1'}
  ]});
  assert.equal(replay.status,'success',replay.message);

  assert.equal(snap.data.semantic.frames.length,1);
  const json=JSON.stringify(snap.data.semantic);
  assert(!json.includes('very-secret-password-666')&&!json.includes('another-private-value-999'));
  assert.equal(pages.size,0);
  const saved=await engine.inspect('snapshot-private');
  assert.equal(saved.tasks[0].state,'success');
  const failedCheckpoint=await engine.inspect('failure');
  assert.equal(failedCheckpoint.tasks[0].state,'error');
  assert(!JSON.stringify(memory).includes('token-in-query'),'Checkpoint must never persist URL query tokens');
  assert(!JSON.stringify(memory).includes('very-secret-password-666'),'Checkpoint must not store DOM');
  const journal=storeFor(chromeApi);
  await journal.begin('orphan', {tabId:4321,origin:'https://example.org',totalSteps:4});
  await journal.progress('orphan',2,{index:3,type:'click'});
  const afterRestart=createController(chromeApi);
  const orphan=await afterRestart.inspect('orphan');
  assert.equal(orphan.tasks[0].state,'interrupted','Unfinished jobs must be interrupted after restart');
  assert.equal(orphan.tasks[0].completedSteps,2);
  assert(!JSON.stringify(memory).includes('TOKEN_alpha'),'Journal must not persist form values');
  const duplicateTask={taskId:'duplicate',url:base+'/start?id=dup',closeOnComplete:true,
    steps:[{type:'wait',selector:'h1'},{type:'scrape',selectors:{heading:'h1'}}]};
  const dups=await Promise.all([engine.run(duplicateTask),engine.run(duplicateTask)]);
  assert.equal(dups.filter(x=>x.status==='success').length,1,'Only one duplicate job should start');
  assert.equal(dups.filter(x=>x.status==='error').length,1,'Concurrent same-ID jobs must be rejected');
  assert.equal(pages.size,0);
  console.log('PASS: three parallel cross-page workflows + strict error + SPA + explicit navigation');
  console.log('PASS: semantic snapshot including open shadow, no password leakage, shadow click');
  console.log('PASS: 3-agent checkpoint journal, interruption detection, no write replay');
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
}
main().catch(e=>{console.error('TEST_FAILED',e);process.exitCode=1;server.close();});
