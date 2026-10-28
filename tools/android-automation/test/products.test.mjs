import test from 'node:test';
import assert from 'node:assert/strict';
import {extractProductCards,chooseProduct,yuanPer500g} from '../src/products.mjs';
const at=(text,x,y,w=240)=>({text,box:{x,y,width:w,height:35}});
test('parses product-specific groups, price and associated buttons',()=>{
 const lines=[
  at('综合',111,255),at('销量',376,258),
  at('冷冻秘500g/包|正新原',538,382),at('味脆皮爆汁烤肠烤肉肠线下',538,438),
  at('券后¥5.99',533,762),at('立即抢购',849,762,160),
  at('冷冻100g/包正新一口爆',538,897),at('汁肠烤肠',535,950),
  at('限量¥3.98',533,1275),at('加入购物车',803,1272,203),
  at('冷冻精选700g/袋|三全经',538,1925),at('典原味香爆烤肠80%含肉量',538,1980),
  at('秒杀¥8.99',533,2305),at('立即抢购',846,2305,168)
 ];
 const cards=extractProductCards(lines);
 assert.equal(cards.length,3);
 assert.equal(cards[0].brand,'正新');
 assert.equal(cards[0].weightG,500);
 assert.equal(cards[0].price,5.99);
 assert.equal(cards[0].unitPrice500g,5.99);
 assert.equal(cards[0].action.label,'立即抢购');
 assert.equal(cards[1].unitPrice500g,19.9);
 assert.equal(cards[2].unitPrice500g,6.42);
 assert.equal(chooseProduct(cards,{brand:'正新',weightG:500,maxUnitPrice500g:7}).price,5.99);
});
test('no price or multiple add buttons produces unverified card',()=>{
 const rows=[at('500g/包|正新烤肠',538,410),at('加入购物车',805,660),at('加入购物车',820,730)];
 const c=extractProductCards(rows)[0];
 assert.equal(c.verified,false);
 assert.ok(c.issues.includes('ambiguous_or_missing_action'));
 assert.ok(c.issues.includes('ambiguous_or_missing_price'));
 assert.equal(chooseProduct([c]),null);
});
test('handles coupon and unit conversion',()=>{
 assert.equal(yuanPer500g(16.99,700),12.14);
 const a=extractProductCards([at('500g/包正新原味',550,350),at('¥9.00',545,560),at('券后¥5.99',550,600),at('加入购物车',805,620)]);
 assert.equal(a[0].price,5.99);
});
