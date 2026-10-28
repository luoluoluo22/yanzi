import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import {JSDOM} from 'jsdom';
const script=readFileSync(new URL('../../../browser-extension/chatgpt-background.js',import.meta.url),'utf8');
const id='01234567-1234-4567-89ab-abcdefabcdef';
const url='https://chatgpt.com/c/01234567-1234-5678-90ab-cdef01234567';
function makeWorker({transcript='',originalTab=true}={}) {
 const dom=new JSDOM('<main>'+transcript+'</main>');
 const tabs=originalTab?[{id:12,url,status:'complete',active:false,pinned:false}]:[];
 const sent=[],opened=[],removed=[];
 const chrome={
  runtime:{getManifest:()=>({version:'0.5.76'}),onInstalled:{addListener(){}},onMessage:{addListener(){}},lastError:null},
  action:{setBadgeText:async()=>{},setBadgeBackgroundColor:async()=>{}},
  contextMenus:{remove:(_,callback)=>callback(),create:()=>{},onClicked:{addListener(){}}},
  alarms:{create:()=>{},onAlarm:{addListener(){}}},
  storage:{local:{set:async()=>{}},session:{get:async()=>({chatgptManagedTabs:{}}),set:async()=>{}}},
  tabs:{
   query:async()=>tabs,
   get:async id=>{const tab=tabs.find(t=>t.id===id);if(!tab)throw new Error('no tab');return tab;},
   create:async input=>{const tab={id:25,url:input.url,status:'complete',active:false,pinned:false};opened.push(tab);tabs.push(tab);return tab;},
   remove:async id=>{removed.push(id);tabs.splice(tabs.findIndex(t=>t.id===id),1);},
   sendMessage:async(...args)=>{sent.push(args);throw new Error('verify should never send!')}
  },
  scripting:{executeScript:async({func,args})=>[{result:func(...args)}]}
 };
 const context=vm.createContext({chrome,document:dom.window.document,URL,console,
   setTimeout,clearTimeout,setInterval:()=>0,clearInterval:()=>{},crypto:globalThis.crypto,
   WebSocket:class {static OPEN=1;readyState=0;}});
 vm.runInContext(script,context);
 return {run:async (marker,{freshRead=false}={})=>context.runChatGptTask({
  taskId:'verification-1',action:'chatgpt_verify_delivery',expectedUrl:url,
  expectedConversationId:url.split('/').at(-1),marker,
  newChat:false,closeAfter:false,temporary:false,freshRead
 }),removed,opened,sent,close:()=>dom.window.close()};
}
test('read-only verifier locates exact Job message and settled parent response',async t=>{
 const a=makeWorker({transcript:'<div data-message-author-role="user">[燕子子 Agent 结果回传] 任务 Job：'+id+'</div><div data-message-author-role="assistant">LONGRUN R3 B GREEN 已收到</div>'});
 t.after(a.close);
 const reply=await a.run(id);
 assert.equal(reply.status,'success');
 assert.equal(reply.data.verified,true);
 assert.equal(reply.data.observed,'completed');
 assert.match(reply.data.replyText,/GREEN/);
 assert.deepEqual(a.sent,[]);
 assert.deepEqual(a.removed,[],'existing tabs are never closed');
});
test('read-only verifier does not trust reply from unrelated Job, and closes temporary inspection tab',async t=>{
 const a=makeWorker({originalTab:false,transcript:'<div data-message-author-role="user">任务 Job：other-not-our-job</div><div data-message-author-role="assistant">done</div>'});
 t.after(a.close);
 const reply=await a.run(id);
 assert.equal(reply.status,'success');
 assert.equal(reply.data.verified,false);
 assert.equal(reply.data.observed,'not_found');
 assert.equal(a.opened.length,1);
 assert.deepEqual(a.removed,[25]);
 assert.deepEqual(a.sent,[]);
});

test('fresh read-only verification opens a separate unique hashed tab and does not disturb original parent',async t=>{
 const parent=makeWorker({transcript:'<div data-message-author-role="user">任务 Job：'+id+'</div><div data-message-author-role="assistant">已确认</div>'});
 t.after(parent.close);
 const a=await parent.run(id,{freshRead:true});
 assert.equal(a.status,'success');
 assert.equal(a.data.verified,true);
 assert.equal(parent.opened.length,1);
 assert.equal(parent.opened[0].url,url+'#yanzi-readonly-verification-1');
 assert.deepEqual(parent.removed,[25]);
 assert.deepEqual(parent.sent,[]);
});
