import test from 'node:test';
import assert from 'node:assert/strict';
import {extractCartRows,verifyCartChange} from '../src/cart.mjs';
import {UnsafeTargetError} from '../src/vision.mjs';
const l=(text,x,y,w=200)=>({text,box:{x,y,width:w,height:33}});
const cart=(qty,unavailable=2)=>{
 const rows=qty?[{name:'宸欢白砂糖(一级带嘴)',weightG:468,quantity:qty,price:5.99,verified:true}]:[];
 return {visible:true,verified:true,complete:true,outOfStockCount:unavailable,rows};
};
test('realistic cart with unavailable products: name, quantity, price, location',()=>{
 const data=[l('购物车',36,875),l('专场满40减6',50,1000),
  l('468g/袋|宸欢白砂糖(一级带嘴)',375,1115,330),
  l('已卖9.9万袋',375,1174),l('细腻易溶',525,1188),l('￥5.99',370,1304),
  l('1',889,1301,30),l('下架商品(共2件)',34,1450),
  l('查看全部下架商品',350,1580),l('已选1',90,2253),l('立即支付',685,2248)];
 const s=extractCartRows(data);
 assert.equal(s.rows.length,1);
 assert.equal(s.rows[0].name,'宸欢白砂糖(一级带嘴)');
 assert.equal(s.rows[0].quantity,1);
 assert.equal(s.rows[0].price,5.99);
 assert.equal(s.rows[0].quantityBox.x,889);
 assert.equal(s.outOfStockCount,2);
 assert.equal(s.verified,true);
});
test('empty active shopping cart with 2 unavailable items is verifiable',()=>{
 const s=extractCartRows([l('下架商品(共2件)',30,876),l('查看全部下架商品',350,1000),
  l('全选',90,2250),l('立即支付',685,2248)]);
 assert.equal(s.visible,true); assert.equal(s.verified,true);
 assert.equal(s.rows.length,0);assert.equal(s.outOfStockCount,2);
});
test('cannot verify cart without footer, end barrier, or quantity',()=>{
 const noFooter=extractCartRows([l('购物车',30,500),l('500g/包|正新',350,800),l('¥5.99',370,1100)]);
 assert.equal(noFooter.verified,false);
 const incomplete=extractCartRows([l('购物车',30,500),l('500g/包|正新',350,800),l('¥5.99',370,1100),l('已选1',90,2250),l('立即支付',680,2248)]);
 assert.equal(incomplete.verified,false);
 const noQty=extractCartRows([l('购物车',30,500),l('500g/包|正新',350,800),l('¥5.99',370,1100),l('下架商品(共2件)',30,1450),l('已选1',90,2250),l('立即支付',680,2248)]);
 assert.equal(noQty.verified,false);
 assert.ok(noQty.rows[0].issues.includes('ambiguous_quantity'));
});
test('only target can change; unavailable 2 must stay 2',()=>{
 const target={name:'宸欢白砂糖(一级带嘴)',weightG:468};
 assert.equal(verifyCartChange(cart(0),cart(1),{type:'add',target}).to,1);
 assert.equal(verifyCartChange(cart(1),cart(0),{type:'remove',target}).to,0);
 assert.throws(()=>verifyCartChange(cart(1),cart(0,1),{type:'remove',target}),/Unavailable/);
 assert.throws(()=>verifyCartChange(cart(1),cart(1),{type:'remove',target}),UnsafeTargetError);
});
