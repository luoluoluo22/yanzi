import test from 'node:test';
import assert from 'node:assert/strict';
import {collectOrderDetailsFromList} from '../src/order-batch.mjs';
const line=(text,x,y,width=300)=>({text,box:{x,y,width,height:35}});
function list(date,amount,count) {
 return {lines:[line('订单列表',440,138),line('全部',30,378),
  line(date,30,600),line('已提货',895,598),
  line(`共${count}件 先用后付 实付:¥${amount}`,600,1000)]};
}
function detail(price,id) {
 return {lines:[line('订单详情',440,138),
  line('500g/袋|农家四季豆',245,700),
  line(`实付:¥${price}`,825,699),
  line('x1',995,800,36),
  line('收起(共1件)',395,1180),
  line('共优惠¥0',30,1250),
  line(`先用后付 实付:¥${price}`,470,1380),
  line(`订单编号:PO-${id}`,30,1720)
 ]};
}
test('bounded batch navigates one order, verifies detail and returns to list',async()=>{
 let page=list('2026/09/02',5.66,1),tapCount=0,backCount=0,swipes=0;
 const device={size:async()=>({width:1080,height:2400}),tap:async(x,y)=>{
   tapCount++;assert.ok(x<540&&y>600&&y<1100);page=detail(5.66,'260902-TEST000001');
 },back:async()=>{backCount++;page=list('2026/09/02',5.66,1)},
 swipe:async()=>swipes++};
 const r=await collectOrderDetailsFromList({device,vision:{recognize:async()=>page},maxOrders:1,waitMs:0});
 assert.equal(r.orders.length,1);assert.equal(r.orders[0].complete,true);
 assert.equal(r.orders[0].amountMatched,true);
 assert.equal(r.orders[0].items[0].name,'农家四季豆');
 assert.equal(tapCount,1);assert.equal(backCount,1);assert.equal(swipes,0);
});
test('different amount cannot be declared complete',async()=>{
 let page=list('2026/09/02',5.66,1);
 const device={size:async()=>({width:1080,height:2400}),tap:async()=>{page=detail(4.56,'260902-TEST000002')},
 back:async()=>{page=list('2026/09/02',5.66,1)},swipe:async()=>{}};
 const r=await collectOrderDetailsFromList({device,vision:{recognize:async()=>page},maxOrders:1,waitMs:0});
 assert.equal(r.orders[0].complete,false);assert.equal(r.orders[0].amountMatched,false);
 assert.equal(r.orders[0].reason,'list_detail_amount_mismatch');
});
test('unknown page refuses to click',async()=>{
 let clicks=0,swipes=0;
 const r=await collectOrderDetailsFromList({device:{size:async()=>({width:1080,height:2400}),
 tap:async()=>clicks++,swipe:async()=>swipes++},vision:{recognize:async()=>({lines:[line('买菜特价',300,300)]})},waitMs:0});
 assert.equal(r.recognized,false);assert.equal(clicks,0);assert.equal(swipes,0);
});
test('wrong tap destination stops instead of blindly clicking Back or another order',async()=>{
 let taps=0,backs=0;
 const device={size:async()=>({width:1080,height:2400}),tap:async()=>taps++,back:async()=>backs++,swipe:async()=>{}};
 let count=0;
 const r=await collectOrderDetailsFromList({device,vision:{recognize:async()=>count++===0?list('2026/09/02',5.66,1):{lines:[line('我的订单',400,130)]}},waitMs:0});
 assert.equal(taps,1);assert.equal(backs,0);
 assert.equal(r.stopReason,'order_detail_did_not_open');
});

test('pending pickup order is not clicked during historical-order verification',async()=>{
 let taps=0,swipes=0;
 const pending={lines:[
  line('订单列表',440,138),line('全部',30,378),
  line('2026/10/08',30,600),line('待提货(10月9日可提货)',600,600,440),
  line('共12件 先用后付 实付:¥0',600,1000),
  line('付款¥42.88',850,1060,180)
 ]};
 const result=await collectOrderDetailsFromList({
  device:{size:async()=>({width:1080,height:2400}),tap:async()=>taps++,swipe:async()=>swipes++},
  vision:{recognize:async()=>pending},maxOrders:1,maxScrolls:1,waitMs:0
 });
 assert.equal(taps,0);assert.equal(swipes,0);
 assert.equal(result.orders.length,0);
});
test('when current pending and historical orders share a page, only historical order is opened',async()=>{
 let taps=0,backs=0;
 const orders={lines:[
  line('订单列表',440,138),line('全部',30,378),
  line('2026/10/08',30,530),line('待提货(10月9日可提货)',610,530,440),
  line('共12件 先用后付 实付:¥0',600,850),
  line('付款¥42.88',850,920,180),
  line('2026/09/02',30,1160),line('已提货',890,1160),
  line('共1件 先用后付 实付:¥5.66',600,1520)
 ]};
 let page=orders;
 const result=await collectOrderDetailsFromList({
  device:{size:async()=>({width:1080,height:2400}),
    tap:async(x,y)=>{taps++;assert.ok(y>1160 && y<1500);page=detail(5.66,'260902-TEST000003');},
    back:async()=>{backs++;page=orders;},swipe:async()=>{}},
  vision:{recognize:async()=>page},maxOrders:1,maxScrolls:1,waitMs:0
 });
 assert.equal(taps,1);assert.equal(backs,1);
 assert.equal(result.orders.length,1);assert.equal(result.orders[0].date,'2026-09-02');
});

test('obscured list quantity can be verified from detail without misreading coupon',async()=>{
 const obscured={lines:[
   line('订单列表',440,138),line('全部',30,378),
   line('2026/09/24',30,600),line('待评价',900,596),
   line('实付:¥5.66',790,1000)
 ]};
 let page=obscured,clicks=0,backs=0;
 const result=await collectOrderDetailsFromList({
  device:{size:async()=>({width:1080,height:2400}),
   tap:async(x,y)=>{clicks++;assert.ok(y>=690&&y<=800);page=detail(5.66,'260924-TEST000005')},
   back:async()=>{backs++;page=obscured},
   swipe:async()=>{}},
  vision:{recognize:async()=>page},maxOrders:1,maxScrolls:1,waitMs:0
 });
 assert.equal(clicks,1); assert.equal(backs,1);
 assert.equal(result.orders[0].summaryVerified,false);
 assert.equal(result.orders[0].detailComplete,true);
 assert.equal(result.orders[0].complete,true);
 assert.equal(result.orders[0].amountMatched,true);
});
test('same-date cards without verified identity cannot be opened by guessing',async()=>{
 const duplicated={lines:[
  line('订单列表',440,138),line('全部',30,378),
  line('2026/08/20',30,600),line('已提货',900,596),
  line('实付:¥27.95',790,900),
  line('2026/08/20',30,1270),line('已提货',900,1266),
  line('实付:¥124.37',790,1660)
 ]};
 let clicks=0;
 await collectOrderDetailsFromList({
  device:{size:async()=>({width:1080,height:2400}),
   tap:async()=>clicks++,swipe:async()=>{}},
  vision:{recognize:async()=>duplicated},maxOrders:1,maxScrolls:1,waitMs:0
 });
 assert.equal(clicks,0);
});

test('async detail navigation is polled without a second tap',async()=>{
 const original=list('2026/09/02',5.66,1);
 const destination=detail(5.66,'260902-TEST000009');
 let taps=0,backs=0,checks=0,isOpened=false;
 const r=await collectOrderDetailsFromList({
  device:{size:async()=>({width:1080,height:2400}),
   tap:async()=>{taps++;isOpened=true;},
   back:async()=>{backs++;isOpened=false;},
   swipe:async()=>{}},
  vision:{recognize:async()=>{checks++;return isOpened&&checks>3?destination:original}},
  maxOrders:1,maxScrolls:1,waitMs:0
 });
 assert.equal(taps,1);assert.equal(backs,1);
 assert.equal(r.orders[0].complete,true);
});
