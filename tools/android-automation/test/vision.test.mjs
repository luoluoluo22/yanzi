import test from 'node:test';
import assert from 'node:assert/strict';
import {selectUnique,ScreenVision,UnsafeTargetError} from '../src/vision.mjs';
test('OCR coordinate resolves center; duplicate target fails closed',()=>{
 const nodes=[{text:'加入购物车',box:{x:80,y:120,width:140,height:44},recognitionScore:.98}];
 assert.deepEqual(selectUnique(nodes,'加入购物车'),{x:150,y:142,matched:nodes[0]});
 assert.throws(()=>selectUnique([...nodes,...nodes],'加入购物车'),UnsafeTargetError);
 assert.throws(()=>selectUnique(nodes,'购买'),UnsafeTargetError);
});
test('UI tree preferred; OCR only when missing',async()=>{
 let invoked=false;
 const device={uiTree:async()=>[{text:'购物车',enabled:true,bounds:{x:30,y:70,width:100,height:50}}],size:async()=>({width:1080,height:2400})};
 const vision=new ScreenVision(device,{ocr:()=>{invoked=true;return {}; }});
 const result=await vision.locate('购物车');
 assert.equal(result.source,'uiautomator');assert.equal(result.x,80);assert.equal(invoked,false);
});
test('WeChat sparse tree triggers OCR fallback',async()=>{
 const device={uiTree:async()=>[{text:'',description:'',bounds:{x:0,y:0,width:1080,height:2400}}],withScreenshotFile:async(cb)=>cb('temp.png')};
 const vision=new ScreenVision(device,{ocr:async()=>({success:true,data:{lines:[{text:'历史订单',box:{x:30,y:90,width:120,height:30}}],text:'历史订单',engine:'ppocr'}})});
 assert.equal((await vision.locate('历史订单')).source,'ocr');
});
test('wrong OCR and no stable match never clicks',async()=>{
 let tapped=false;
 const device={uiTree:async()=>[],withScreenshotFile:async cb=>cb('x'),size:async()=>({width:1080,height:2400}),tap:async()=>{tapped=true;}};
 const vision=new ScreenVision(device,{ocr:async()=>({data:{text:'商品',lines:[{text:'其他',box:{x:30,y:30,width:40,height:20}}]}})});
 await assert.rejects(vision.tapText('购物车'),UnsafeTargetError); assert.equal(tapped,false);
});
