/* Portable checkpoint validation; no browser or external dependencies. */
const assert=require('node:assert/strict');
const {storeFor}=require('../browser-extension/workflow-checkpoints.js');
async function run(){
 const memory={};
 const fake={storage:{local:{
   async get(key){return {[key]:memory[key]};},
   async set(obj){Object.assign(memory,obj);}
 }}};
 const journal=storeFor(fake);
 await journal.interruptUnfinished();
 await Promise.all(['a','b','c'].map(async (id,i)=>{
   await journal.begin(id,{tabId:i+1,origin:'https://example.org',totalSteps:3});
   await journal.progress(id,2,{index:3,type:'click'});
   await journal.finish(id,i===1?'error':'success',i===1?2:3);
 }));
 const three=await journal.status();
 assert.equal(three.length,3);
 assert.equal(three.find(x=>x.taskId==='a').state,'success');
 assert.equal(three.find(x=>x.taskId==='b').state,'error');
 await journal.begin('orphan',{tabId:17,origin:'https://example.net',totalSteps:6});
 await journal.progress('orphan',4,{index:5,type:'fill'});
 // Simulated service-worker restart.
 const again=storeFor(fake);
 assert.equal(await again.interruptUnfinished(),1);
 const orphan=(await again.status('orphan'))[0];
 assert.equal(orphan.state,'interrupted');
 assert.equal(orphan.completedSteps,4);
 assert.equal(orphan.currentStep.type,'fill');
 const blob=JSON.stringify(memory);
 assert(!blob.includes('password'), 'Journal accidentally persisted a sensitive value');
 for(let i=0;i<45;i++) await again.begin('limited-'+i,{tabId:i,origin:'https://example.net',totalSteps:1});
 const recent=await again.status();
 assert.equal(recent.length,20);
 assert.equal(memory['yanzi.workflow.checkpoints.v1'].length,40);
 console.log('PASS: concurrency-safe checkpoints, interruption, bounded entries, no replay');
}
run().catch(e=>{console.error(e);process.exitCode=1});
