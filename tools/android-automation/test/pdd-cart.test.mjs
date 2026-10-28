import test from 'node:test';
import assert from 'node:assert/strict';
import {PddCartAutomation} from '../src/pdd-cart.mjs';
import {UnsafeTargetError} from '../src/vision.mjs';
const sugar={name:'宸欢白砂糖(一级带嘴)',weightG:468};
const old={name:'安井原味火山石烤肠',weightG:700};
const next={name:'正新原味脆皮爆汁烤肠',weightG:500};
const snapshot=(withOld=true,withNew=false)=>({visible:true,verified:true,complete:true,outOfStockCount:2,rows:[
  ...(withOld?[{...old,quantity:1,price:16.99}]:[]),
  ...(withNew?[{...next,quantity:1,price:5.99}]:[]),
  {name:'豆腐',weightG:400,quantity:2,price:2.49}
]});
test('test cycle requires explicit approval',async()=>{
 const automation=new PddCartAutomation({},{});
 await assert.rejects(automation.testCycle({target:sugar,maxPrice:6}),/authorization/);
});
test('already present target cannot be silently duplicated',async()=>{
 const a=new PddCartAutomation({},{});
 a.ensureCartOpen=async()=>({visible:true,verified:true,complete:true,outOfStockCount:2,rows:[{...sugar,quantity:1,price:5.99}]});
 await assert.rejects(a.add({target:sugar,maxPrice:6}),/already in cart/);
});
test('cart locator refuses to tap when no unique checkout bar',async()=>{
 let taps=0;
 const device={foreground:async()=> 'Window{ com.tencent.mm.plugin.appbrand.ui.AppBrandUI00 }',size:async()=>({width:1080,height:2400}),tap:async()=>taps++};
 const vision={recognize:async()=>({lines:[{text:'综合',box:{x:30,y:300,width:100,height:30}}],text:'综合'})};
 const a=new PddCartAutomation(device,vision);
 await assert.rejects(a.ensureCartOpen(),/bottom CTA/);
 assert.equal(taps,0);
});
test('cart locator selects only primary 0元下单 even if deferred payment is visible',async()=>{
 const taps=[];
 const a=new PddCartAutomation({size:async()=>({width:1080,height:2400}),tap:async(x,y)=>taps.push([x,y])},{});
 let reads=0;
 a.cartSnapshot=async()=>++reads===1?{snapshot:{visible:false,verified:false},frame:{lines:[
  {text:'0元下单',box:{x:653,y:2225,width:151,height:42}},
  {text:'先用后付，确认收货后付款¥5.99',box:{x:475,y:2290,width:300,height:35}}
 ]}}:{snapshot:{verified:true,visible:true,rows:[],outOfStockCount:2}};
 const found=await a.ensureCartOpen();
 assert.equal(found.verified,true);
 assert.equal(taps.length,1);
 assert.ok(taps[0][0]<100&&taps[0][1]>2200);
});
test('replacement refuses unspecific or preexisting inventory',async()=>{
 const a=new PddCartAutomation({},{});
 await assert.rejects(a.replaceCartItem({from:old,to:next,maxPrice:7}),/approval/);
 a.ensureCartOpen=async()=>snapshot(false,false);
 await assert.rejects(a.replaceCartItem({from:old,to:next,maxPrice:7,approved:true}),/Original item/);
});
test('replacement adds first and removes second, preserving other goods',async()=>{
 const a=new PddCartAutomation({},{});
 let n=0; const steps=[];
 a.ensureCartOpen=async()=>n++===0?snapshot(true,false):snapshot(false,true);
 a.add=async args=>{steps.push('add');return {status:'confirmed',verification:{verified:true}};};
 a.remove=async args=>{steps.push('remove');return {status:'confirmed',verification:{verified:true}};};
 const done=await a.replaceCartItem({from:old,to:next,maxPrice:7,approved:true});
 assert.equal(done.status,'confirmed');
 assert.deepEqual(steps,['add','remove']);
 assert.equal(done.unavailablePreserved,2);
});
test('replacement stops if add uncertain; no removal attempted',async()=>{
 const a=new PddCartAutomation({},{});
 a.ensureCartOpen=async()=>snapshot(true,false);
 let removed=0;
 a.add=async()=>({status:'uncertain'});
 a.remove=async()=>{removed++;};
 const result=await a.replaceCartItem({from:old,to:next,maxPrice:7,approved:true});
 assert.equal(result.status,'uncertain');assert.equal(removed,0);
});
