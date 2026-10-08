import test from 'node:test';
import assert from 'node:assert/strict';
import {extractCartRows,verifyCartChange} from '../src/cart.mjs';
import {UnsafeTargetError} from '../src/vision.mjs';
const l=(text,x,y,w=200)=>({text,box:{x,y,width:w,height:33}});
const mock=(n=1,m=1)=>({visible:true,verified:true,rows:[
  {name:'正新原味烤肠',weightG:500,quantity:n,price:5.99,verified:true},
  {name:'豆腐',weightG:400,quantity:m,price:2.49,verified:true}
]});
test('extract cart items with price and quantity tied to correct row',()=>{
 const data=[l('购物车',36,404),
 l('500g/包|正新原味烤肠',350,580),l('¥5.99',370,695),l('1',945,699,30),
 l('400g/盒|豆腐',350,850),l('¥2.49',370,964),l('1',945,962,30),
 l('立即支付',675,1800)];
 const cart=extractCartRows(data);
 assert.equal(cart.rows.length,2);
 assert.equal(cart.rows[0].name,'正新原味烤肠');
 assert.equal(cart.rows[0].quantity,1);
 assert.equal(cart.rows[1].weightG,400);
 assert.equal(cart.verified,true);
});
test('cannot verify cart lacking quantity',()=>{
 const cart=extractCartRows([l('购物车',35,400),l('500g/包|正新',350,550),l('¥5.99',370,600),l('立即支付',640,1200)]);
 assert.equal(cart.verified,false);
 assert.ok(cart.rows[0].issues.includes('ambiguous_quantity'));
});
test('only expected item may change and missing response is not success',()=>{
 const target={name:'正新原味烤肠',weightG:500};
 assert.equal(verifyCartChange(mock(1,1),mock(2,1),{type:'add',target}).to,2);
 assert.throws(()=>verifyCartChange(mock(1,1),mock(1,1),{type:'add',target}),UnsafeTargetError);
 assert.throws(()=>verifyCartChange(mock(1,1),mock(2,2),{type:'add',target}),/Unrelated/);
 assert.throws(()=>verifyCartChange(mock(1,1),{...mock(2,1),verified:false},{type:'add',target}),UnsafeTargetError);
});
