const http = require('http');
const assert = require('assert');
const {chromium} = require('F:/Desktop/kaifa/ego-evaluation/node_modules/playwright-core');
global.chrome={};
const {createController}=require('../browser-extension/workflow-orchestrator.js');
const port=8777,base='http://127.0.0.1:'+port;
const server=http.createServer((req,res)=>{
 const u=new URL(req.url,base),id=u.searchParams.get('id')||'unknown';
 res.setHeader('Content-Type','text/html; charset=utf-8');res.setHeader('Cache-Control','no-store');
 if(u.pathname==='/start')return res.end('<!doctype html><h1>Start '+id+'</h1><a id="next" href="/next?id='+id+'">Next</a>');
 if(u.pathname==='/next')return res.end('<!doctype html><h1>Next '+id+'</h1><form id="form"><input id="token"><button id="submit">Finish</button></form><div id="result"></div><script>document.querySelector("form").onsubmit=e=>{e.preventDefault();document.querySelector("#result").textContent=document.querySelector("#token").value}</script>');
 if(u.pathname==='/spa')return res.end('<!doctype html><h1>SPA home</h1><button id="spaButton">Go</button><script>document.querySelector("#spaButton").onclick=()=>{history.pushState({},"","/spa-done");document.querySelector("h1").innerText="SPA DONE"}</script>');
 if(u.pathname==='/spa-done')return res.end('<h1>SPA DONE</h1>');
 res.statusCode=404;res.end('Not found');
});
async function main(){
 await new Promise(resolve=>server.listen(port,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
 const context=await browser.newContext(),pages=new Map();let idSeed=1;
 const chromeApi={
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
  console.log('PASS: three parallel cross-page workflows + strict error + SPA + explicit navigation');
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
}
main().catch(e=>{console.error('TEST_FAILED',e);process.exitCode=1;server.close();});
