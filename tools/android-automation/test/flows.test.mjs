import test from 'node:test';
import assert from 'node:assert/strict';
import {AndroidFlows} from '../src/flows.mjs';
test('dry-run plans have no side effects',async()=>{
 const calls=[];
 const flow=new AndroidFlows({ensureConnected:async()=>{},openApp:async()=>calls.push('app')},{});
 const r=await flow.prepareCart({items:[{name:'土豆'},{name:'正新烤肠'}]});
 assert.equal(r.history.length,2);assert.equal(calls.length,0);
});
test('unsafe transactions disabled',async()=>{
 const f=new AndroidFlows({},{});
 for(const op of [()=>f.submitOrder(),()=>f.addProduct(),()=>f.removeProduct({name:'安井'}),()=>f.prepareCart({items:[{name:'土豆'}],dryRun:false})])await assert.rejects(op(),/disabled|Safe removal|Batch cart mutation/);
});
test('search requires adapter before any tap',async()=>{
 const calls=[];
 const flow=new AndroidFlows({}, {tapText:async t=>calls.push(t)});
 await assert.rejects(flow.searchProducts('正新'),/Chinese input adapter/);
 assert.deepEqual(calls,[]);
});
