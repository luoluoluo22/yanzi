import test from 'node:test';
import assert from 'node:assert/strict';
import {parseOrderDetailFrame,collectOrderDetail} from '../src/order-detail.mjs';
const L=(text,x,y,width=310)=>({text,box:{x,y,width,height:36}});
test('parse detail page product specification, price, count, without exposing PII',()=>{
 const lines=[
  L('订单详情',445,138),L('罗某某 13800138000 某提货点',123,580),
  L('450g~550g/份|高原露天现采',239,992),L('实付：¥4.72',822,990),
  L('黄瓜',236,1040),L('¥4.99',935,1038),L('X1',1003,1090,38),
  L('300g±30g/袋|云南新鲜现采',239,1282),L('实付：¥4.16',837,1278),
  L('山地青尖椒',239,1332),L('￥4.99',935,1328),L('x1',1003,1378,38),
  L('展开(共10件)',393,1570),L('先用后付 实付：¥52.39',460,1680),
  L('订单编号：PO-260221-440370522493648',34,2005)];
 const parsed=parseOrderDetailFrame(lines);
 assert.equal(parsed.recognized,true);
 assert.equal(parsed.declaredCount,10);
 assert.equal(parsed.items.length,2);
 assert.deepEqual([parsed.items[0].name,parsed.items[0].paidUnitPrice,parsed.items[0].quantity],['高原露天现采黄瓜',4.72,1]);
 assert.deepEqual([parsed.items[1].name,parsed.items[1].paidUnitPrice],['云南新鲜现采山地青尖椒',4.16]);
 assert.equal(parsed.orderIdHash.length,23);
 assert.ok(!JSON.stringify(parsed).includes('13800138000'));
 assert.ok(!JSON.stringify(parsed).includes('440370522493648'));
});
test('requires detail header and refuses unverified quantity',()=>{
 const noPage=parseOrderDetailFrame([L('订单列表',445,138),L('450g/份|黄瓜',239,990)]);
 assert.equal(noPage.recognized,false);
 const r=parseOrderDetailFrame([L('订单详情',445,138),L('1000g/袋|猪肉香菇饺',239,930),L('实付：¥9.44',837,930)]);
 assert.equal(r.items[0].verified,false);
 assert.ok(r.items[0].issues.includes('missing_or_ambiguous_quantity'));
});
test('collector expands one time then stops after exactly declared unique items',async()=>{
 const header=L('订单详情',445,138);
 const initial={lines:[header,L('展开(共2件)',393,1600)]};
 const expanded={lines:[header,L('450g/份|黄瓜',239,930),L('实付：¥4.72',830,932),L('x1',1000,1030,40),
 L('500g/袋|土豆',239,1230),L('实付：¥1.99',830,1230),L('x1',1000,1330,40),
 L('共优惠¥0',35,1490),L('先用后付 实付：¥6.71',460,1600),
 L('订单编号：PO-260221-123456789001',35,1800)]};
 let reads=0,taps=0,swipes=0;
 const r=await collectOrderDetail({device:{size:async()=>({height:2400,width:1080}),tap:async()=>taps++,swipe:async()=>swipes++},
 vision:{recognize:async()=>reads++===0?initial:expanded},maxPages:4,waitMs:0});
 assert.equal(r.complete,true);
 assert.equal(taps,1);assert.equal(swipes,0);
 assert.equal(r.declaredCount,2);
 assert.equal(r.items.length,2);
 assert.equal(r.totalMatched,true);
 assert.equal(r.paidTotal,6.71);
});
test('no inferred complete when expanded item count is missing',async()=>{
 const frame={lines:[L('订单详情',445,138),L('450g/份|黄瓜',239,930),L('实付：¥4.72',830,932),L('x1',1000,1030,40)]};
 const r=await collectOrderDetail({device:{size:async()=>({height:2400,width:1080}),swipe:async()=>{}},
 vision:{recognize:async()=>frame},maxPages:2,waitMs:0});
 assert.equal(r.complete,false);
 assert.equal(r.stopReason,'page_unchanged');
});

test('parses instant noodle multi-pack 138g×5 and does not miss it',()=>{
 const data=parseOrderDetailFrame([
   L('订单详情',445,138),
   L('138g*5袋/包 | 今麦郎1.5倍老坛',239,560),
   L('酸菜牛肉面【新老包装随机发货】',239,612),
   L('实付：¥8.5',831,557),
   L('x1',1000,661,40),
   L('收起(共10件)',388,1990)
 ]);
 assert.equal(data.recognized,true);
 assert.equal(data.declaredCount,10);
 assert.equal(data.items.length,1);
 assert.equal(data.items[0].specification,'138g*5袋/包');
 assert.equal(data.items[0].quantity,1);
 assert.equal(data.items[0].paidUnitPrice,8.5);
 assert.ok(data.items[0].name.includes('今麦郎'));
});

test('order detail cannot be marked complete if sum differs from order total',async()=>{
 const frame={lines:[L('订单详情',445,138),
  L('450g/份|黄瓜',239,800),L('实付：¥4.72',830,800),L('x1',1000,930,40),
  L('收起(共1件)',400,1200),L('共优惠¥0',35,1300),
  L('先用后付 实付：¥7.31',460,1400),
  L('订单编号：PO-260221-123456789007',35,1750)]};
 const r=await collectOrderDetail({device:{size:async()=>({height:2400,width:1080}),swipe:async()=>{}},
 vision:{recognize:async()=>frame},maxPages:2,waitMs:0});
 assert.equal(r.declaredCount,1);
 assert.equal(r.items.length,1);
 assert.equal(r.paidTotal,7.31);
 assert.equal(r.computedTotal,4.72);
 assert.equal(r.totalMatched,false);
 assert.equal(r.complete,false);
});

test('real completed-order OCR with category tags, count specs and inline price',()=>{
 const result=parseOrderDetailFrame([
  L('订单详情',445,138),
  L('冷鲜500g/盒【天天鲜肉】鲜猪五花',239,317),L('¥12.97',921,315),
  L('肉-喜满春',236,368),L('X1',1003,368,35),
  L('30枚/份|1.4kg大观谷农家杂粮蛋旭 ¥19.98',236,565),
  L('飞农业',241,618),L('X1',1003,620,35),
  L('冷藏400g±40g/袋【颗粒饱满】新鲜黄¥1.99',236,815),
  L('甜玉米',236,868),L('x1',1003,870,35),
  L('4斤~5斤/份【脆嫩爽口】精品包菜新鲜¥4.99',238,1065),
  L('爽口',231,1118),L('X1',1003,1120,35),
  L('收起(共19件)',395,1322)
 ]);
 assert.equal(result.declaredCount,19);
 assert.equal(result.items.length,4);
 assert.deepEqual(result.items.map(x=>x.paidUnitPrice),[12.97,19.98,1.99,4.99]);
 assert.ok(result.items.every(x=>x.verified));
 assert.equal(result.items[1].specification,'30枚/份');
 assert.equal(result.items[2].specification,'400g±40g/袋');
});

test('overlapping scrolls reconcile partially OCR-read price without guessing',async()=>{
 const header=L('订单详情',445,138);
 const partial={lines:[
  header,
  L('500g/袋【香脆可口】圈嘴打手',239,510),
  L('牙签瓜子袋装香瓜子',239,564),
  L('x1',1000,615,35),
  L('收起(共1件)',395,1050),
  L('先用后付 实付：¥12.94',470,1330),
  L('订单编号：PO-260924-123456789111',30,1650)
 ]};
 const full={lines:[
  header,
  L('500g/袋【香脆可口】圈嘴打手',239,510),
  L('实付：¥12.94',830,508),
  L('牙签瓜子袋装香瓜子',239,564),
  L('¥14.99',925,560),
  L('x1',1000,615,35),
  L('收起(共1件)',395,1050),
  L('先用后付 实付：¥12.94',470,1330),
  L('订单编号：PO-260924-123456789111',30,1650)
 ]};
 let read=0;const deltas=[];
 const res=await collectOrderDetail({
  device:{size:async()=>({height:2400,width:1080}),
    swipe:async(x,y,x2,y2)=>deltas.push(y-y2)},
  vision:{recognize:async()=>read++===0?partial:full},
  maxPages:3,waitMs:0
 });
 assert.equal(res.complete,true);
 assert.equal(res.declaredCount,1);
 assert.equal(res.computedTotal,12.94);
 assert.ok(deltas.length>=1&&deltas.every(x=>x<=750));
});
