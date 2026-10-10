// Read-only readiness of the dedicated Edge profile and bridge (53922).
// Never sends prompts, reads cookies, retrieves drafts, or touches normal Edge.
import {join} from 'node:path';
import {readFileSync} from 'node:fs';
const base='http://127.0.0.1:53922';
const tokenFile=join(process.env.LOCALAPPDATA||'',
  'OpenQuickHost','ExtensionStorage','chatgpt-agent-bridge','api-token.txt');
const result={connected:false,ready:false,loginRequired:null,challenge:null,
  draftPresent:null,diagnosticTabClosed:null,reason:null};
let headers;
async function api(path,method='GET',body){
  const res=await fetch(base+path,{method,headers,
    body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(8000)});
  if(!res.ok)throw new Error('bridge_http_'+res.status);
  return res.json();
}
async function waitJob(id,maxMs=30000){
  const until=Date.now()+maxMs;
  while(Date.now()<until){
    const j=await api('/api/jobs/'+id);
    if(!['queued','running'].includes(j.status))return j;
    await new Promise(done=>setTimeout(done,300));
  }
  throw new Error('inspection_timeout_do_not_resubmit');
}
const doJob=(payload)=>api('/api/jobs','POST',payload);
try{
  const token=readFileSync(tokenFile,'utf8').trim();
  headers={Authorization:'Bearer '+token,'X-Bridge-Request':'1',
    'Content-Type':'application/json'};
  const health=await api('/health');
  result.connected=health.connected===true;
  result.extensionVersion=health.extensionVersion;
  if(!result.connected)result.reason='dedicated_browser_not_connected';
  else{
    const creation=await doJob({action:'chatgpt_new_chat',
      tabPolicy:'new',temporary:false,timeoutSeconds:25});
    const created=await waitJob(creation.id,35000);
    result.newChatJobStatus=created.status;
    if(created.status!=='success')result.reason='chat_page_not_ready';
    const tabId=created.data?.tabId;
    if(Number.isInteger(tabId) && tabId>0){
      const status=await doJob({action:'chatgpt_status',timeoutSeconds:15});
      const listed=await waitJob(status.id,23000);
      const item=(listed.data?.tabs||[]).find(x=>x.tabId===tabId);
      if(item?.managed===true && item.active!==true && item.pinned!==true){
        try{
          const diag=await doJob({action:'chatgpt_diagnostics',
            tabId,timeoutSeconds:18});
          const checked=await waitJob(diag.id,25000);
          result.diagnosticStatus=checked.status;
          const dom=checked.data?.dom||{};
          if(checked.status==='success'){
            result.loginRequired=dom.loginRequired===true;
            result.challenge=dom.challenge===true;
            result.draftPresent=dom.draftPresent===true;
            result.ready=dom.composerExists===true &&
              !result.loginRequired&&!result.challenge&&!result.draftPresent&&!dom.generating;
            result.reason=result.ready?'safe_to_create_persistent_child':
              result.loginRequired?'normal_sign_in_required':
              result.challenge?'website_verification_required':
              result.draftPresent?'draft_guard_active':'composer_unavailable';
          }else result.reason='cannot_inspect_chat_page';
        }finally{
          try{
            const close=await doJob({action:'chatgpt_close',tabId,timeoutSeconds:15});
            const removed=await waitJob(close.id,23000);
            result.diagnosticTabClosed=removed.status==='success' &&
              (removed.data?.closedTabIds||[]).includes(tabId);
          }catch{result.diagnosticTabClosed=false;}
        }
      }else result.reason='test_tab_ownership_not_confirmed';
    }else result.reason='no_recoverable_test_tab';
  }
}catch(error){result.reason=error.message;}
console.log(JSON.stringify(result));
if(!result.ready)process.exitCode=2;
