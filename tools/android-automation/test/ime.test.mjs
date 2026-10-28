import test from 'node:test';
import assert from 'node:assert/strict';
import {AdbChineseIme} from '../src/ime.mjs';
const old='com.baidu.input_mi/.ImeService';
const newIme='com.android.adbkeyboard/.AdbIME';
function mocked({failure=false,verified=true,installed=true}={}){
 const calls=[];let active=old;
 const device={shell:async(...args)=>{
  calls.push(args);
  if(args[0]==='settings')return active;
  if(args[0]==='ime'&&args[1]==='list')return installed?old+'\n'+newIme:old;
  if(args[0]==='ime'&&args[1]==='set'){active=args[2];return 'Input method '+active+' selected';}
  if(args[0]==='am') {if(failure)throw new Error('transport');return 'Broadcast completed: result=0';}
 }};
 return {device,calls,get active(){return active}};
}
test('focus after IME switch, clear, type, verify, then restore',async()=>{
 const m=mocked();
 let focusInIme=false,verifyInIme=false;
 const result=await new AdbChineseIme(m.device).type('正新烤肠',{
  clearExisting:true,
  focus:async()=>{focusInIme=m.active===newIme;},
  verify:async()=>{verifyInIme=m.active===newIme;return true;}
 });
 assert.deepEqual(result,{sent:true,verified:true});
 assert.ok(focusInIme&&verifyInIme);
 assert.equal(m.active,old);
 assert.deepEqual(m.calls.filter(x=>x[0]==='am').map(x=>x[3]),['ADB_CLEAR_TEXT','ADB_INPUT_TEXT']);
});
test('restore if verification fails or broadcast fails',async()=>{
 for(const failure of [false,true]){
  const m=mocked({failure});
  await assert.rejects(new AdbChineseIme(m.device).type('西红柿',{focus:async()=>{},verify:async()=>false}),failure?/transport/:/not visible/);
  assert.equal(m.active,old);
 }
});
test('reject missing callbacks and missing keyboard without side effect',async()=>{
 const m=mocked({installed:false});
 const adapter=new AdbChineseIme(m.device);
 await assert.rejects(adapter.type('土豆'),/callbacks are required/);
 await assert.rejects(adapter.type('土豆',{focus:async()=>{},verify:async()=>true}),/unavailable/);
 assert.equal(m.active,old);
});
