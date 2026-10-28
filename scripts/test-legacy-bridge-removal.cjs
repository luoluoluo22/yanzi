const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('F:\\Desktop\\kaifa\\ego-evaluation\\node_modules\\playwright-core');
const root=path.resolve(__dirname,'../browser-extension');
(async()=>{
 const manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'),'utf8'));
 assert.equal(manifest.version,'0.5.83');
 const allJs=(fs.readdirSync(root).filter(x=>x.endsWith('.js')).map(x=>fs.readFileSync(path.join(root,x),'utf8')).join('\n')+
  fs.readdirSync(path.join(root,'popup')).filter(x=>x.endsWith('.js')).map(x=>fs.readFileSync(path.join(root,'popup',x),'utf8')).join('\n'));
 assert.doesNotMatch(allJs,/127\.0\.0\.1:53921|chatgptBridgeReconnect|connectChatGptBridge|yanzi_agent_console|agent_console_request/);
 assert.equal(fs.existsSync(path.join(root,'popup/agent-console-popup.js')),false);
 assert.equal(fs.existsSync(path.join(root,'popup/api-guide.js')),false);
 console.log('PASS old Bridge source and popup resources removed');
 const profile=fs.mkdtempSync(path.join(require('node:os').tmpdir(),'yanzi-bridge-test-'));
 const browser=await chromium.launchPersistentContext(profile,{
  executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  headless:true,
  args:['--no-sandbox','--disable-extensions-except='+root,'--load-extension='+root]
 });
 try{
  let id;
  const worker=browser.serviceWorkers()[0]||await browser.waitForEvent('serviceworker',{timeout:9000});
  id=new URL(worker.url()).hostname;
  const page=await browser.newPage();
  const errors=[];
  page.on('pageerror',e=>errors.push(String(e.message)));
  await page.goto('chrome-extension://'+id+'/popup/popup.html',{waitUntil:'domcontentloaded'});
  await page.locator('#market-list').waitFor({timeout:9000});
  assert.equal(await page.locator('#agent-launch-form').count(),0);
  assert.equal(await page.locator('#chatgpt-status').count(),0);
  assert.equal(await page.locator('[data-panel=agents]').count(),0);
  const href=await page.locator('#open-workbench').count();
  assert.equal(href,1);
  await page.locator('[data-panel=overview]').click();
  assert.equal(await page.locator('#panel-overview').isVisible(),true);
  assert.deepEqual(errors,[]);
  console.log('PASS extension MV3 worker and popup load; overview and Ego workbench CTA remain');
  console.log('EXTENSION_ID='+id);
 }finally{await browser.close();fs.rmSync(profile,{recursive:true,force:true});}
})().catch(err=>{console.error(err);process.exitCode=1});