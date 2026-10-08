import test from 'node:test';
import assert from 'node:assert/strict';
import {AdbChineseIme} from '../src/ime.mjs';
test('temporarily set ADBKeyboard and restore previous IME',async()=>{
 const calls=[];
 const device={shell:async(...argv)=>{
   calls.push(argv);
   if(argv[0]==='settings')return 'com.baidu.input_mi/.ImeService';
   if(argv[0]==='ime'&&argv[1]==='list')return 'com.android.adbkeyboard/.AdbIME\ncom.baidu.input_mi/.ImeService';
   if(argv[0]==='ime'&&argv[1]==='set')return 'Input method selected';
   if(argv[0]==='am')return 'Broadcasting: Intent {}\nBroadcast completed: result=0';
 }};
 assert.deepEqual(await new AdbChineseIme(device).type('正新烤肠'),{sent:true,verified:false});
 assert.deepEqual(calls.at(-1),['ime','set','com.baidu.input_mi/.ImeService']);
 assert.deepEqual(calls.filter(x=>x[0]==='am')[0].slice(-2),['msg','正新烤肠']);
});
test('restores old keyboard even when input fails',async()=>{
 let restored=false;
 const device={shell:async(...argv)=>{
   if(argv[0]==='settings')return 'com.baidu.input_mi/.ImeService';
   if(argv[0]==='ime'&&argv[1]==='list')return 'com.android.adbkeyboard/.AdbIME';
   if(argv[0]==='ime'&&argv[1]==='set'){if(argv[2].startsWith('com.baidu'))restored=true;return 'Input method selected';}
   if(argv[0]==='am')throw new Error('transient');
 }};
 await assert.rejects(new AdbChineseIme(device).type('西红柿'),/transient/);
 assert.equal(restored,true);
});
test('does not switch if no supported keyboard',async()=>{
 let sideEffects=0;
 const d={shell:async(...args)=>{if(args[0]==='settings')return 'com.baidu/.Ime';if(args[0]==='ime'&&args[1]==='list')return 'com.baidu/.Ime';sideEffects++;}};
 await assert.rejects(new AdbChineseIme(d).type('买菜'),/not installed/);
 assert.equal(sideEffects,0);
});
