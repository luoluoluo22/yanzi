import test from 'node:test';
import assert from 'node:assert/strict';
import {extractOrderCards} from '../src/orders.mjs';
const line=(text,y)=>({text,box:{x:50,y,width:360,height:38}});
test('parses date, quantity, payable and state on an order list',()=>{
 const r=extractOrderCards([line('我的订单',100),line('全部',210),
 line('2026/09/24 10:21',370),line('待评价',440),line('商品19件',600),line('实付 ¥147.05',780),
 line('2026/09/02 15:46',1000),line('已提货',1090),line('商品22件',1310),line('实付 ¥145.28',1500)]);
 assert.equal(r.recognized,true);
 assert.equal(r.orders.length,2);
 assert.deepEqual([r.orders[0].date,r.orders[0].quantity,r.orders[0].amount],['2026-09-24',19,147.05]);
 assert.equal(r.orders[1].verified,true);
 assert.equal(r.complete,false);
});
test('home screen or incomplete order must not invent data',()=>{
 assert.equal(extractOrderCards([line('订单',100),line('商品19件',400)]).recognized,false);
 const r=extractOrderCards([line('订单列表',100),line('全部',250),line('2026/09/24',400),line('已提货',480)]);
 assert.equal(r.recognized,true);assert.equal(r.orders.length,1);assert.equal(r.orders[0].verified,false);
 assert.equal(r.orders[0].amount,null);
});
test('duplicate visible order cards collapse without treating list as complete',()=>{
 const r=extractOrderCards([line('全部订单',100),line('全部',230),line('2026/08/20',400),line('已提货',480),line('商品3件',560),line('实付¥27.95',650),line('2026/08/20',900),line('已提货',980),line('商品3件',1060),line('实付¥27.95',1170)]);
 assert.equal(r.orders.length,1);
 assert.equal(r.complete,false);
});
