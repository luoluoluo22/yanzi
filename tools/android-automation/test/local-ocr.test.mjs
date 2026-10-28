import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {LocalOcrClient} from '../src/local-ocr.mjs';
test('local OCR only targets loopback and sends scoped token',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'yanzi-ocr-test-'));
 const file=path.join(dir,'settings.json');
 try{
  await writeFile(file,JSON.stringify({agentApiPort:37555,agentApiToken:'test-key'}));
  let called;
  const c=new LocalOcrClient({settingsFile:file,fetchImpl:async(uri,options)=>{called={uri,options};return {ok:true,json:async()=>({success:true,data:{text:'识别',lines:[]}})};}});
  const data=await c.request('/v1/capabilities/invoke',{name:'ocr.recognize',payload:{imagePath:'test.png'}});
  assert.equal(called.uri,'http://127.0.0.1:37555/v1/capabilities/invoke');
  assert.equal(called.options.headers['X-Yanzi-Token'],'test-key');
  assert.equal(data.data.text,'识别');
 }finally{await rm(dir,{recursive:true,force:true});}
});
