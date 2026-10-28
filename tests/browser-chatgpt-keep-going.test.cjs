/* Regression using a real headless Chromium DOM, no logged-in ChatGPT account. */
const fs=require('fs'), os=require('os'), path=require('path'), cp=require('child_process'), assert=require('assert');
const source=fs.readFileSync(path.join(__dirname,'..','browser-extension','chatgpt-keep-going.js'),'utf8');
const script=[
  "window.chrome={storage:{local:{get:async()=>({'yanzi.chatgpt.keepGoing.settings.v1':{message:'继续',delaySeconds:1,stopText:'ALL_DONE',stopMode:'contains',maxSends:5}}),set:async()=>{}}}};",
  "window.__sent=0;window.__errors=[];window.__snapshots=[];",
  "const input=document.getElementById('prompt-textarea');const send=document.getElementById('send');",
  "send.disabled=true;",
  "input.addEventListener('input',()=>{send.disabled=!(input.innerText||input.textContent).trim();});",
  "send.addEventListener('click',()=>{if(send.disabled)return;window.__sent++;",
  "input.textContent='';send.disabled=true;const stop=document.createElement('button');stop.dataset.testid='stop-button';stop.textContent='Stopping';document.getElementById('composer').appendChild(stop);",
  "setTimeout(()=>{stop.remove();const r=document.createElement('div');r.dataset.messageAuthorRole='assistant';const x=document.createElement('div');x.className='markdown';x.textContent=window.__sent>=2?'ALL_DONE':'继续处理第'+window.__sent+'轮';r.appendChild(x);document.getElementById('turns').appendChild(r);},170);",
  "});",
  "function check(x,reason){if(!x)window.__errors.push(reason);}",
  "setTimeout(()=>{const host=document.getElementById('yanzi-keep-going-host');check(!!host,'button not mounted');",
  "if(!host)return; const b=host.shadowRoot.querySelector('.toggle');check(b.textContent==='不要停','bad initial button');const gear=host.shadowRoot.querySelector('.settings');check(!!gear,'settings gear missing');check(gear?.getBoundingClientRect().left<b.getBoundingClientRect().left,'settings gear not on left');check(b.getBoundingClientRect().width>=110,'keep-going button not widened');gear?.click();check(!!document.getElementById('yanzi-keep-going-dialog-host'),'gear did not open settings');document.getElementById('yanzi-keep-going-dialog-host')?.shadowRoot.querySelector('.cancel')?.click();",
  "b.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true}));",
  "const dialog=document.getElementById('yanzi-keep-going-dialog-host');check(!!dialog,'context menu missing');",
  "if(dialog){check(dialog.shadowRoot.querySelector('textarea[name=message]').value==='继续','settings not loaded');dialog.shadowRoot.querySelector('.cancel').click();}",
  "b.click();check(b.getAttribute('aria-pressed')==='true','did not arm');},250);",
  "setTimeout(()=>{const host=document.getElementById('yanzi-keep-going-host');const b=host?.shadowRoot.querySelector('.toggle');",
  "check(!!b,'button removed');check(window.__sent>=1,'did not send first followup');window.__snapshots.push({at:5500,sent:window.__sent,mode:b?.dataset.mode});},5500);",
  "setTimeout(()=>{const b=document.getElementById('yanzi-keep-going-host')?.shadowRoot.querySelector('.toggle');",
  "check(window.__sent===2,'expected exactly 2 sends, got '+window.__sent);",
  "check(b?.getAttribute('aria-pressed')==='false','stop condition did not deactivate');",
  "check(b?.textContent==='不要停','button not reset');",
  "document.body.dataset.tests=JSON.stringify({ok:window.__errors.length===0,errors:window.__errors,sent:window.__sent,snapshots:window.__snapshots});},13000);"
].join('\n');
const html=[
  '<!doctype html><html><head><meta charset="utf-8"></head><body>',
  '<main><div id="turns"><div data-message-author-role="assistant"><div class="markdown">准备好了</div></div></div>',
  '<form id="composer" onsubmit="return false">',
  '<div id="prompt-textarea" contenteditable="true" role="textbox"></div>',
  '<button id="send" type="button" data-testid="send-button">Send</button></form></main>',
  '<script>',script,'</script><script>',source,'</script></body></html>'
].join('');
const temp=path.join(os.tmpdir(),'yanzi-keep-going-test-'+Date.now()+'.html');
fs.writeFileSync(temp,html,'utf8');
const locations=[
 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
 'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe'
];
const exe=locations.find(p=>fs.existsSync(p));
if(!exe)throw Error('Chromium not found');
const args=['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--disable-extensions','--disable-background-networking','--no-sandbox','--disable-features=RendererCodeIntegrity','--virtual-time-budget=15500','--dump-dom','file:///'+temp.replace(/\\/g,'/')];
let stdout='';
try { stdout=cp.execFileSync(exe,args,{encoding:'utf8',timeout:30000,maxBuffer:8*1024*1024,windowsHide:true}); }
finally { fs.unlinkSync(temp); }
const match=stdout.match(/data-tests="([^"]+)"/);
assert(match,'No final test result in Chromium DOM: '+stdout.slice(-1500));
const result=JSON.parse(match[1].replace(/&quot;/g,'"').replace(/&amp;/g,'&'));
console.log(JSON.stringify(result));
assert(result.ok,'Keep-going regression failed: '+JSON.stringify(result.errors));
