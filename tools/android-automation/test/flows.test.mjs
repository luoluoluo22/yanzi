import test from 'node:test';
import assert from 'node:assert/strict';
import {AndroidFlows} from '../src/flows.mjs';
test('preview lists goods without side effects',async()=>{
 const calls=[];
 const flow=new AndroidFlows({ensureConnected:async()=>{},openApp:async()=>calls.push('app')},{});
 assert.deepEqual(await flow.prepareCart({items:[{name:'土豆'},{name:'正新烤肠'}]}),{dryRun:true,history:[{item:'土豆',action:'plan',executed:false},{item:'正新烤肠',action:'plan',executed:false}]});
 assert.equal(calls.length,0);
});
test('transaction and unsupported removal are disabled',async()=>{
 const flow=new AndroidFlows({},{});
 await assert.rejects(flow.submitOrder(),/disabled/);
 await assert.rejects(flow.addProduct({name:'正新烤肠'}),/disabled/);
 await assert.rejects(flow.removeProduct({name:'安井'}),/Safe removal/);
 await assert.rejects(flow.prepareCart({items:[{name:'西红柿'}],dryRun:false}),/Batch cart mutation disabled/);
});
test('search without verified Chinese input adapter blocked',async()=>{
 const taps=[];
 const flow=new AndroidFlows({}, {tapText:async t=>taps.push(t)});
 await assert.rejects(flow.searchProducts('正新'),/Chinese input adapter/);
 assert.deepEqual(taps,['搜索','搜索您要的商品']);
});
