import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const sub='browser-extension';
function edit(file,transform) {
 const p=path.join(root,sub,file);const original=fs.readFileSync(p,'utf8');
 const updated=transform(original);if(updated===original)throw Error('No changes: '+file);
 fs.writeFileSync(p,updated,'utf8');
 console.log('updated',file,original.length,'->',updated.length);
}
function cutBetween(text,start,end) {
 const a=text.indexOf(start);const b=text.indexOf(end,a+start.length);
 if(a<0||b<0||b<=a)throw Error('Cannot find exact boundaries for '+start);
 return text.slice(0,a)+text.slice(b);
}
edit('chatgpt-background.js',text=>{
 text=cutBetween(text,'// Dedicated bridge leaves the existing Yanzi connection independent.','const CHATGPT_MAX_CONCURRENT_TASKS = 4;');
 text=cutBetween(text,'function cacheChatGptImageAsset(dataUrl) {','// Only inspect the original reserved tab after Chrome loses the async response');
 const endMarker='// Feedback origin must be captured from the actual ChatGPT page';
 const at=text.indexOf(endMarker);if(at<0)throw Error('origin-bind / websocket section not found');
 return '// ChatGPT helper functions used by local content features.\n// Legacy extension-driven Bridge transport has been removed: Agent execution uses Ego Browser API.\n'+text.slice(0,at).trimEnd()+'\n';
});
edit('popup/popup.html',text=>{
 const nav='          <button type="button" data-panel="agents"';
 const start=text.indexOf(nav);const stop=text.indexOf('          <button type="button" data-panel="market"',start);
 if(start<0||stop<0)throw Error('agents navigation missing');
 text=text.slice(0,start)+text.slice(stop);
 const apiNav=text.indexOf('          <button type="button" data-panel="api"');
 const logsNav=text.indexOf('          <button type="button" data-panel="logs"',apiNav);
 if(apiNav<0||logsNav<0)throw Error('api navigation missing');
 text=text.slice(0,apiNav)+text.slice(logsNav);
 const agents=text.indexOf('    <div class="panel-content" id="panel-agents">');
 const overview=text.indexOf('    <div class="panel-content" id="panel-overview"',agents);
 if(agents<0||overview<0)throw Error('agent panel missing');
 text=text.slice(0,agents)+text.slice(overview);
 text=text.replace(/      <p class="server-info">ChatGPT 小程序：[\s\S]*?<\/p>\r?\n/,'');
 const oldCard=text.indexOf('    <section class="glass-card service-card">');
 const footer=text.indexOf('    <footer class="app-footer">',oldCard);
 if(oldCard<0||footer<0)throw Error('legacy guide card missing');
 const replacement='    <section class="glass-card service-card">\n      <h2>Agent 团队 · Ego API</h2>\n      <p>主管和子 Agent 统一由燕子小程序管理，浏览器自动化只使用 Ego Browser API，不再由扩展执行。</p>\n      <div class="guide-actions"><button type="button" id="open-workbench">打开 Agent 团队</button></div>\n    </section>\n';
 text=text.slice(0,oldCard)+replacement+text.slice(footer);
 const oldApi=text.indexOf('    <div class="panel-content" id="panel-api"');
 const logs=text.indexOf('    <div class="panel-content" id="panel-logs"',oldApi);
 if(oldApi<0||logs<0)throw Error('legacy api panel missing');
 text=text.slice(0,oldApi)+text.slice(logs);
 text=text.replace('<script src="api-guide.js"></script>','').replace('<script src="agent-console-popup.js"></script>','');
 if(text.includes('53921')||text.includes('agent-launch-form')||text.includes('api-guide.js'))throw Error('old UI markup remains');
 return text;
});
edit('popup/popup.js',text=>{
 const initial="    for (const panel of ['agents','overview','market','api','logs'])";
 if(!text.includes(initial))throw Error('panel list not found');
 text=text.replace(initial,"    for (const panel of ['overview','market','logs'])");
 text=text.replace("  document.getElementById('show-api').onclick = () => showPanel('api');\n",'');
 text=text.replace("chrome.tabs.create({ url: 'http://127.0.0.1:53921/' })",
   "chrome.tabs.create({ url: 'http://127.0.0.1:43830/' })");
 const bridgeStart=text.indexOf('  chrome.storage.local.get({ localWsUrl: "ws://127.0.0.1:53919/v1/browser/ws", chatgptBridgeStatus:');
 const bridgeEnd=text.indexOf('  document.getElementById("save-port")',bridgeStart);
 if(bridgeStart<0||bridgeEnd<0)throw Error('popup bridge status code missing');
 text=text.slice(0,bridgeStart)+'  chrome.storage.local.get({ localWsUrl: "ws://127.0.0.1:53919/v1/browser/ws" }, config => {\n    portInput.value = new URL(config.localWsUrl).port;\n  });\n'+text.slice(bridgeEnd);
 const oldListener=text.indexOf('  chrome.storage.onChanged.addListener(changes => {');
 const nextStatus=text.indexOf('  const statusDot =',oldListener);
 if(oldListener<0||nextStatus<0)throw Error('old bridge status listener missing');
 text=text.slice(0,oldListener)+text.slice(nextStatus);
 text=text.replace("  document.getElementById('show-api').onclick = () => showPanel('api');",'');
 if(text.includes('53921')||text.includes('chatgptBridgeStatus')||text.includes("showPanel('api')"))throw Error('Old Bridge popup script survives');
 return text;
});
edit('manifest.json',text=>{
 const m=JSON.parse(text);
 if(m.version!=='0.5.82')throw Error('Unexpected prior version '+m.version);
 m.version='0.5.83';
 return JSON.stringify(m,null,2)+'\n';
});
for(const rel of ['popup/agent-console-popup.js','popup/api-guide.js','popup/api.html']) {
 const full=path.join(root,sub,rel);
 if(!fs.existsSync(full))throw Error('Old resource not found '+rel);
 fs.unlinkSync(full);
 console.log('deleted',rel);
}
console.log('Removed 53921 websocket reconnect, old console message handler, origin binder, asset upload and API docs; preserved main Yanzi WebSocket and webapp/keep-going features.');
