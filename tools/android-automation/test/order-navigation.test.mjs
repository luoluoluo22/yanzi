import test from 'node:test';
import assert from 'node:assert/strict';
import {AndroidFlows} from '../src/flows.mjs';
import {UnsafeTargetError} from '../src/vision.mjs';
const line=(text,y=230,x=760)=>({text,box:{x,y,width:125,height:42}});
const order=[line('我的订单',110,40),line('全部',210,50),line('2026/09/24',600,35),line('已提货',680),line('商品2件',780),line('实付¥34.5',900)];
test('order navigation only taps uniquely identified shortcut',async()=>{
 let taps=0,reads=0;
 const device={size:async()=>({width:1080,height:2400}),tap:async()=>{taps++}};
 const vision={recognize:async()=>++reads===1?{lines:[line('三订单')]}:{lines:order}};
 const flow=new AndroidFlows(device,vision);
 const outcome=await flow.openOrderHistory();
 assert.equal(taps,1); assert.equal(outcome.navigated,true);
});
test('order navigation cannot claim success after a failed page transition',async()=>{
 let taps=0;
 const device={size:async()=>({width:1080,height:2400}),tap:async()=>{taps++}};
 const vision={recognize:async()=>({lines:[line('三订单')]})};
 const flow=new AndroidFlows(device,vision);
 await assert.rejects(flow.openOrderHistory(),/did not open/);
 assert.equal(taps,1);
});
test('home without a unique order shortcut does not click or swipe',async()=>{
 let taps=0;
 const flow=new AndroidFlows({size:async()=>({width:1080,height:2400}),tap:async()=>{taps++}},
 {recognize:async()=>({lines:[line('商品',800,350)]})});
 await assert.rejects(flow.openOrderHistory(),UnsafeTargetError);
 assert.equal(taps,0);
});
