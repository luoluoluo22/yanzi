/* Real Chromium tests: exact reply matching, deadline cutoff and settings persistence. */
const fs=require('fs'),cp=require('child_process'),os=require('os'),path=require('path'),assert=require('assert');
const extension=fs.readFileSync(path.join(__dirname,'..','browser-extension','chatgpt-keep-going.js'),'utf8');
const edge='C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
if(!fs.existsSync(edge))throw Error('Edge browser not installed');
const cases=[
 {name:'exact_final_reply',delay:1,max:5,stopMode:'equals',stopText:'可以停了',finalReply:'可以停了',expected:1,budget:14000},
 {name:'exact_reply_zero_width',delay:1,max:5,stopMode:'equals',stopText:'可以停了',finalReply:'可以\u200b停了',expected:1,budget:14000},
 {name:'deadline_no_send',delay:30,max:0,stopMode:'contains',stopText:'',expireInMs:2400,expected:0,budget:5600},
 {name:'deadline_during_stream',delay:1,max:0,stopMode:'contains',stopText:'',expireInMs:2400,streaming:true,expected:0,budget:5600},
 {name:'save_datetime_setting',delay:1,max:2,stopMode:'contains',stopText:'',editSettings:true,expected:0,budget:4100},
 {name:'settings_dialog_pauses_sending',delay:1,max:2,stopMode:'contains',stopText:'',popupHold:true,expected:0,budget:8300}
];
const tests=[];
for(const cfg of cases){
 const initialReply=cfg.editSettings?'': '<div data-message-author-role="assistant"><div class="markdown">准备好了</div></div>';
 const payload=JSON.stringify(cfg);
 const script=[
  'const cfg='+payload+';',
  "window.__events=[];window.__sent=0;window.__saved={};",
  "const key='yanzi.chatgpt.keepGoing.settings.v1';",
  "const saved={message:'继续',delaySeconds:cfg.delay,stopText:cfg.stopText,stopMode:cfg.stopMode,maxSends:cfg.max,stopAt:cfg.expireInMs?Date.now()+cfg.expireInMs:0};",
  'window.chrome={storage:{local:{get:async()=>({[key]:saved}),set:async obj=>{Object.assign(window.__saved,obj);}}}};',
  "const composer=document.getElementById('composer'),input=document.getElementById('prompt-textarea'),send=document.getElementById('send');",
  "send.disabled=true; input.addEventListener('input',()=>{send.disabled=!(input.innerText||input.textContent).trim();});",
  "send.addEventListener('click',()=>{if(send.disabled)return;window.__sent++;input.textContent='';send.disabled=true;",
  "const generating=document.createElement('button');generating.dataset.testid='stop-button';composer.appendChild(generating);",
  "setTimeout(()=>{generating.remove();const turn=document.createElement('div');turn.dataset.messageAuthorRole='assistant';const content=document.createElement('div');content.className='markdown';content.textContent=cfg.finalReply||'准备好';turn.appendChild(content);document.getElementById('turns').appendChild(turn);},250);",
  "});",
  "if(cfg.streaming){const generating=document.createElement('button');generating.dataset.testid='stop-button';composer.appendChild(generating);}",
  "const check=(condition,msg)=>{if(!condition)window.__events.push(msg);};",
  "setTimeout(()=>{const host=document.getElementById('yanzi-keep-going-host');const btn=host?.shadowRoot?.querySelector('button');",
  "check(!!btn,'missing keep going button');if(!btn)return;",
  "if(cfg.popupHold){btn.click();btn.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true}));check(!!document.getElementById('yanzi-keep-going-dialog-host'),'popup missing');}",
  "else if(cfg.editSettings){btn.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true}));",
  "const dlg=document.getElementById('yanzi-keep-going-dialog-host');check(!!dlg,'missing settings popup');if(!dlg)return;",
  "const input=dlg.shadowRoot.querySelector('input[name=stopAtLocal]');check(!!input,'missing datetime input');",
  "const date=new Date(Date.now()+86400000); const two=x=>String(x).padStart(2,'0');",
  "input.value=[date.getFullYear(),two(date.getMonth()+1),two(date.getDate())].join('-')+'T'+two(date.getHours())+':'+two(date.getMinutes());",
  "dlg.shadowRoot.querySelector('button.primary').click();",
  "setTimeout(()=>{check(Boolean(window.__saved[key].stopAt> Date.now()+86000000),'absolute stop time not saved');check(!document.getElementById('yanzi-keep-going-dialog-host'),'dialog not closed');},300);",
  "}else {btn.click();check(btn.getAttribute('aria-pressed')==='true','activation failed');}",
  "},350);",
  "setTimeout(()=>{const btn=document.getElementById('yanzi-keep-going-host')?.shadowRoot.querySelector('button');",
  "if(!cfg.editSettings){check(window.__sent===cfg.expected,'send count '+window.__sent+' expected '+cfg.expected);",
  "if(cfg.popupHold){check(btn?.getAttribute('aria-pressed')==='true','popup should pause, not disable');check(btn?.textContent?.includes('设置中'),'missing pause state');}",
  "else{check(btn?.getAttribute('aria-pressed')==='false','stop failed');check(btn?.textContent==='不要停','idle button not restored');}}", 
  "document.body.dataset.report=JSON.stringify({name:cfg.name,ok:window.__events.length===0,errors:window.__events,sendCount:window.__sent,buttonMode:btn?.dataset.mode,buttonText:btn?.textContent});",
  "},cfg.editSettings?2500:cfg.budget-1200);"
 ].join('\n');
 const html=['<!doctype html><html><head><meta charset="utf-8"></head><body>',
 '<main><div id="turns">',initialReply,'</div><form id="composer" onsubmit="return false">',
 '<div id="prompt-textarea" contenteditable="true" role="textbox"></div>',
 '<button id="send" type="button" data-testid="send-button">Send</button></form></main>',
 '<script>',script,'</script><script>',extension,'</script></body></html>'].join('');
 const filename=path.join(os.tmpdir(),'yanzi-stop-test-'+cfg.name+'-'+process.pid+'.html');
 fs.writeFileSync(filename,html,'utf8');
 let htmlOut='';
 try{
  htmlOut=cp.execFileSync(edge,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--disable-extensions','--disable-background-networking','--no-sandbox','--disable-features=RendererCodeIntegrity','--virtual-time-budget='+cfg.budget,'--dump-dom','file:///'+filename.replace(/\\/g,'/')],{encoding:'utf8',timeout:30000,maxBuffer:8*1024*1024,windowsHide:true,stdio:['ignore','pipe','ignore']});
 }finally{fs.unlinkSync(filename);}
 const match=htmlOut.match(/data-report="([^"]+)"/);
 assert(match,cfg.name+': browser did not run test to completion '+htmlOut.slice(-900));
 const result=JSON.parse(match[1].replace(/&quot;/g,'"').replace(/&amp;/g,'&'));
 tests.push(result);
 console.log(JSON.stringify(result));
 assert(result.ok, cfg.name+': '+JSON.stringify(result.errors));
}
console.log('PASS '+tests.length+' keep-going stop cases');
