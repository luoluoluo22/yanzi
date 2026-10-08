import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

const manifest=JSON.parse(readFileSync(new URL('../extension/manifest.json',import.meta.url),'utf8'));
const names=['android.device.status','pdd.product.search','pdd.cart.inspect','pdd.orders.preview','pdd.orders.collect'];
const allowedKeywords=new Set(['type','properties','items','required','enum','additionalProperties','minimum','maximum','minLength','title','description','default','examples','$schema']);
function inspectSchema(schema){
 assert.equal(typeof schema,'object');
 for(const [key,value] of Object.entries(schema)){
  assert.ok(allowedKeywords.has(key),'Unsupported host Schema keyword: '+key);
  if(key==='properties')for(const child of Object.values(value))inspectSchema(child);
  if(key==='items')inspectSchema(value);
 }
}
test('Yanzi extension manifest declares only scoped read/search capabilities',()=>{
 assert.equal(manifest.entry,'provider.cs');
 assert.deepEqual(manifest.provides.map(x=>x.name),names);
 assert.ok(!manifest.provides.some(x=>/purchase|checkout|pay|delete|replace/i.test(x.name)));
 for(const item of manifest.provides){
  inspectSchema(item.inputSchema);inspectSchema(item.outputSchema);
  assert.ok(item.permissions.length===1);
 }
});
test('client denies unknown operations before even checking ADB',()=>{
 const result=spawnSync(process.execPath,[fileURLToPath(new URL('../src/capability-cli.mjs',import.meta.url)),'android.shell','e30='],{encoding:'utf8',timeout:5000});
 assert.notEqual(result.status,0);
 const data=JSON.parse(result.stdout);
 assert.equal(data.ok,false);
 assert.match(data.error,/not allowed/);
});

test('CLI emits one ASCII-safe JSON response for Unicode errors',()=>{
 const result=spawnSync(process.execPath,
  [fileURLToPath(new URL('../src/capability-cli.mjs',import.meta.url)),'错误能力','e30='],
  {encoding:'utf8',timeout:5000});
 assert.notEqual(result.status,0);
 assert.match(result.stdout,/^[\x00-\x7f]*$/);
 const data=JSON.parse(result.stdout);
 assert.equal(data.ok,false);
 assert.match(data.error,/错误能力/);
});
