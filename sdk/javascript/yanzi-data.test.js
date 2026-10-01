import test from 'node:test';
import assert from 'node:assert/strict';
import {YanziDocument} from './yanzi-data.js';
test('offline draft survives restart; conflict requires explicit resolution',async()=>{
  const cache=new Map(),storage={getItem:k=>cache.get(k),setItem:(k,v)=>cache.set(k,v)};
  let online=false,remote={accountId:'a',revision:0,content:''};
  const client={extensionId:'notes',read:async()=>{if(!online)throw Error('offline');return {...remote};},
    write:async(key,content,rev,account)=>{assert.equal(rev,remote.revision);assert.equal(account,'a');remote={accountId:'a',content,revision:rev+1};return remote;}};
  let doc=new YanziDocument({client,key:'data',accountId:'a',storage}); doc.save('draft');
  await assert.rejects(doc.sync(),/offline/);
  doc=new YanziDocument({client,key:'data',accountId:'a',storage});assert.equal(doc.state.content,'draft');
  online=true;assert.equal((await doc.sync()).status,'synced');
  doc.save('local');remote={accountId:'a',revision:2,content:'cloud'};
  assert.equal((await doc.sync()).status,'conflict');assert.equal(doc.state.content,'local');
  doc.resolve('local');await doc.sync();assert.equal(remote.content,'local');
  const other=new YanziDocument({client,key:'data',accountId:'b',storage});assert.equal(other.state.content,'');
  await assert.rejects(other.sync(),/Account changed/);
});
test('an edit during upload is not discarded',async()=>{
  const cache=new Map(),storage={getItem:k=>cache.get(k),setItem:(k,v)=>cache.set(k,v)};
  let finish; const client={extensionId:'notes',read:async()=>({accountId:'a',revision:0,content:''}),
    write:()=>new Promise(resolve=>{finish=resolve;})};
  const doc=new YanziDocument({client,key:'data',accountId:'a',storage});doc.save('first');
  const sync=doc.sync();await new Promise(resolve=>setTimeout(resolve,0));doc.save('second');
  finish({accountId:'a',revision:1});assert.equal((await sync).status,'pending');
  assert.equal(doc.state.content,'second');assert.equal(doc.state.dirty,true);assert.equal(doc.state.revision,1);
});
