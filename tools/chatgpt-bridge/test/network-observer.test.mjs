import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('../../../browser-extension/chatgpt-network-observer.js',import.meta.url),'utf8');
function fixture(){
  const listeners={};
  const chrome={webRequest:{}};
  for(const name of ['onBeforeRequest','onCompleted','onErrorOccurred']){
    chrome.webRequest[name]={addListener(fn,filter,extra){
      listeners[name]={fn,filter,extra};
    }};
  }
  const context=vm.createContext({chrome,URL,Date,Number,Map,Set});
  vm.runInContext(source,context);
  return {listener:(name,payload)=>listeners[name].fn(payload),
    captured:listeners,observer:context.yanziChatGptNetworkObserver};
}

test('network metadata observer is scoped to a specific tab and never retains URL, query, tokens or bodies',()=>{
  const f=fixture();
  assert.equal(f.observer.supported,true);
  for(const key of Object.keys(f.captured)){
    assert.deepEqual(Array.from(f.captured[key].filter.urls),['https://chatgpt.com/*']);
    assert.equal(f.captured[key].extra,undefined);
  }
  const watch=f.observer.start(77);
  assert.ok(watch);
  f.listener('onBeforeRequest',{tabId:55,requestId:'ignored',method:'POST',
    url:'https://chatgpt.com/backend-api/conversation?authorization=SECRET',type:'xmlhttprequest'});
  f.listener('onBeforeRequest',{tabId:77,requestId:'req1',method:'POST',
    url:'https://chatgpt.com/backend-api/conversation?authorization=SECRET',type:'xmlhttprequest',
    requestBody:{raw:[{bytes:'PRIVATE_PROMPT'}]}});
  f.listener('onCompleted',{tabId:77,requestId:'req1',statusCode:200,
    url:'https://chatgpt.com/backend-api/conversation?authorization=SECRET'});
  f.listener('onBeforeRequest',{tabId:77,requestId:'req2',method:'GET',
    url:'https://chatgpt.com/robots.txt?api_key=ABCSECRET',type:'xmlhttprequest'});
  f.listener('onErrorOccurred',{tabId:77,requestId:'req2',error:'net::ERR_FAILED'});
  const summary=watch.stop();
  assert.equal(summary.started,2);
  assert.equal(summary.completed,1);
  assert.equal(summary.failed,1);
  assert.equal(summary.pending,0);
  assert.equal(summary.classes.conversation_api,1);
  assert.equal(summary.classes.page_resource,1);
  assert.equal(summary.statusBands['2xx'],1);
  assert.equal(summary.methods.POST,1);
  assert.equal(summary.methods.GET,1);
  assert.equal(f.observer.start(77)?.stop().started,0);
  const serialized=JSON.stringify(summary);
  for(const forbidden of ['SECRET','PRIVATE_PROMPT','api_key','conversation?','req1','net::ERR_FAILED'])
    assert.equal(serialized.includes(forbidden),false);
});

test('observer rejects overlapping observers for the same tab and ignores late events',()=>{
  const f=fixture();
  const watch=f.observer.start(42);
  assert.equal(f.observer.start(42),null);
  f.listener('onBeforeRequest',{tabId:42,requestId:'a',method:'GET',
    url:'https://chatgpt.com/robots.txt',type:'other'});
  assert.equal(watch.snapshot().pending,1);
  assert.equal(watch.stop().pending,1);
  f.listener('onCompleted',{tabId:42,requestId:'a',statusCode:200});
  assert.equal(watch.snapshot().completed,0);
  assert.equal(f.observer.start(42)?.stop().started,0);
});

test('permission absent is explicit and fails closed',()=>{
  const context=vm.createContext({chrome:{},URL,Date});
  vm.runInContext(source,context);
  assert.equal(context.yanziChatGptNetworkObserver.supported,false);
  assert.equal(context.yanziChatGptNetworkObserver.start(4),null);
});
