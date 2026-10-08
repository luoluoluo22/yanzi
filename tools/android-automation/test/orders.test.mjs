import test from 'node:test';
import assert from 'node:assert/strict';
import {extractOrderCards,collectVisibleOrderPages} from '../src/orders.mjs';
const line=(text,y,x=50)=>({text,box:{x,y,width:360,height:38}});
function frame(...rows){return {lines:[line('我的订单',100),line('全部',210),...rows],text:'我的订单 全部'};}
test('extracts dated order counts prices and status',()=>{
 const r=extractOrderCards(frame(
 line('2026/09/24 10:21',370),line('待评价',440),line('商品19件',600),line('实付 ¥147.05',780),
 line('2026/09/02 15:46',1000),line('已提货',1090),line('商品22件',1310),line('实付 ¥145.28',1500)).lines);
 assert.equal(r.recognized,true);
 assert.equal(r.orders.length,2);
 assert.deepEqual([r.orders[0].date,r.orders[0].quantity,r.orders[0].amount],['2026-09-24',19,147.05]);
 assert.equal(r.orders[1].verified,true); assert.equal(r.complete,false);
});
test('home or incomplete details must not create verified orders',()=>{
 assert.equal(extractOrderCards([line('订单',100),line('商品19件',400)]).recognized,false);
 const r=extractOrderCards(frame(line('2026/09/24',400),line('已提货',480)).lines);
 assert.equal(r.orders[0].verified,false);assert.equal(r.orders[0].amount,null);
});
test('identical visible card dedupes; same amount different goods retained',()=>{
 const r=extractOrderCards(frame(
  line('2026/08/20',400),line('已提货',480),line('商品3件',560),line('白菜',600),line('实付¥27.95',650),
  line('2026/08/20',900),line('已提货',980),line('商品3件',1060),line('土豆',1120),line('实付¥27.95',1170)).lines);
 assert.equal(r.orders.length,2);
 assert.notEqual(r.orders[0].identity,r.orders[1].identity);
});
test('one-screen duplicate OCR card filtered when identical text',()=>{
 const r=extractOrderCards(frame(
 line('2026/08/20',400),line('已提货',480),line('商品3件',560),line('实付¥27.95',650),
 line('2026/08/20',900),line('已提货',980),line('商品3件',1060),line('实付¥27.95',1170)).lines);
 assert.equal(r.orders.length,1);
});
test('collector never scrolls unless order list verified',async()=>{
 let swipes=0;
 const output=await collectVisibleOrderPages({device:{size:async()=>({width:1080,height:2400}),swipe:async()=>swipes++},
 vision:{recognize:async()=>({lines:[line('商品价格',340)],text:'价格'})}});
 assert.equal(output.recognized,false);assert.equal(swipes,0);assert.equal(output.orders.length,0);
});
test('bounded pagination dedupes overlap and conservatively reports partial',async()=>{
 let i=0;let swipes=0;
 const pages=[
 frame(line('2026/09/24',400),line('已提货',470),line('订单号 12345678901234',520),line('商品2件',570),line('实付¥27.95',630)),
 frame(line('2026/09/24',400),line('已提货',470),line('订单号 12345678901234',520),line('商品2件',570),line('实付¥27.95',630),
 line('2026/09/01',950),line('待评价',1020),line('订单号 12345678909999',1070),line('商品4件',1120),line('实付¥33.45',1180)),
 frame(line('2026/09/01',400),line('待评价',470),line('订单号 12345678909999',520),line('商品4件',570),line('实付¥33.45',630),line('没有更多订单',1950))
 ];
 const result=await collectVisibleOrderPages({device:{size:async()=>({width:1080,height:2400}),swipe:async()=>{swipes++}},
 vision:{recognize:async()=>pages[Math.min(i++,2)]},maxPages:5,waitMs:0});
 assert.equal(result.pagesScanned,3);assert.equal(result.orders.length,2);
 assert.equal(result.duplicatesIgnored,2);
 assert.equal(result.complete,true);
 assert.equal(swipes,2);
});
test('no-change and hard page cap bound traversal',async()=>{
 let swipes=0;const page=frame(line('2026/09/24',400),line('已提货',470),line('商品2件',570),line('实付¥27.95',630));
 const r=await collectVisibleOrderPages({device:{size:async()=>({width:1080,height:2400}),swipe:async()=>swipes++},
 vision:{recognize:async()=>page},maxPages:8,waitMs:0});
 assert.equal(r.stopReason,'page_unchanged');assert.equal(swipes,1);assert.equal(r.complete,false);
});
