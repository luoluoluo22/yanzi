import test from 'node:test';
import assert from 'node:assert/strict';
import {runVerifiedCartAction} from '../src/transaction.mjs';
const target={name:'正新原味烤肠',weightG:500};
const snap=n=>({visible:true,verified:true,complete:true,outOfStockCount:2,rows:[{...target,quantity:n,price:5.99,verified:true}]});
test('confirmed only after exactly one matching cart change',async()=>{
 let actions=0,reads=0;
 const result=await runVerifiedCartAction({type:'add',target,
  readSnapshot:async()=>{reads++;return snap(reads===1?1:2);},
  perform:async()=>{actions++;}
 });
 assert.equal(result.status,'confirmed');assert.equal(actions,1);assert.equal(reads,2);
});
test('ambiguous UI or dropped acknowledgment never retries',async()=>{
 let actions=0;
 const a=await runVerifiedCartAction({type:'add',target,
  readSnapshot:async()=>snap(1),perform:async()=>{actions++;throw new Error('network timeout');}
 });
 assert.equal(a.status,'uncertain');assert.equal(actions,1);
 const b=await runVerifiedCartAction({type:'add',target,
  readSnapshot:async()=>snap(1),perform:async()=>{actions++;}
 });
 assert.equal(b.status,'uncertain');assert.equal(actions,2);
});
test('unverified cart prevents side effects',async()=>{
 let actions=0;
 await assert.rejects(runVerifiedCartAction({type:'add',target,
  readSnapshot:async()=>({...snap(1),verified:false}),perform:async()=>{actions++;}
 }),/unverified/);
 assert.equal(actions,0);
});
