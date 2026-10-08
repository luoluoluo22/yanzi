import test from 'node:test';
import assert from 'node:assert/strict';
import {AndroidDevice,parseUiDump} from '../src/adb.mjs';
test('parse Android xml bounds and decoded text',()=>{
 const xml='<hierarchy><node text="多多&amp;买菜" resource-id="app:id/link" content-desc="" clickable="true" enabled="true" bounds="[10,15][210,85]"/></hierarchy>';
 assert.deepEqual(parseUiDump(xml)[0],{text:'多多&买菜',description:'',resourceId:'app:id/link',packageName:'',clickable:true,enabled:true,bounds:{x:10,y:15,width:200,height:70}});
});
test('explicit serial protects all operations',async()=>{
 const calls=[];
 const device=new AndroidDevice({serial:'abc',execute:async(_exe,args)=>{
  calls.push(args);return {stdout:args.includes('devices')?'List of devices attached\nabc device model:Pixel\n':args.includes('size')?'Physical size: 1080x2400\n':'',stderr:''};
 }});
 assert.equal((await device.ensureConnected()).serial,'abc');
 await device.tap(20,40);
 assert.deepEqual(calls.at(-1),['-s','abc','shell','input','tap','20','40']);
 assert.deepEqual(await device.size(),{width:1080,height:2400});
 await assert.rejects(device.typeAscii('西红柿'),/Chinese requires/);
});
test('override display resolution is used for OCR/tap alignment',async()=>{
 const d=new AndroidDevice({execute:async(_exe,args)=>({stdout:args.includes('devices')?'List of devices attached\na device\n':'Physical size: 1440x3200\nOverride size: 1080x2400\n'})});
 assert.deepEqual(await d.size(),{width:1080,height:2400});
});
test('multiple phones without serial rejected',async()=>{
 const device=new AndroidDevice({execute:async()=>({stdout:'List of devices attached\na device\nb device\n'})});
 await assert.rejects(device.ensureConnected(),/Multiple phones/);
});
test('no Chinese or out of bounds tap as implicit text',async()=>{
 const device=new AndroidDevice({serial:'a',execute:async()=>({stdout:''})});
 await assert.rejects(device.typeAscii('包菜'),/Chinese requires/);
 await assert.rejects(device.tap(-1,55),/Invalid/);
});
