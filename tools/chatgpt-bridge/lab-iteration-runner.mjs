import {readFileSync,writeFileSync,mkdirSync,realpathSync,openSync,closeSync,unlinkSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {join,resolve,dirname,relative,isAbsolute,sep} from 'node:path';
import {fileURLToPath} from 'node:url';
import {applyProposal} from './lab-patch-executor.mjs';
import {classifyJobNetwork} from './network-evidence.mjs';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'..','..');
const lab=join(repo,'.tmp','yanzi-iteration-lab');
const taskPaths={title:['title.mjs','title.test.mjs'],duration:['duration.mjs','duration.test.mjs'],tags:['tags.mjs','tags.test.mjs']};
const tokenFile=join(process.env.LOCALAPPDATA||'', 'OpenQuickHost','ExtensionStorage','chatgpt-bridge','api-token.txt');
const defaultEndpoint='http://127.0.0.1:53921';
const digest=value=>createHash('sha256').update(value).digest('hex').toUpperCase();
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function options(args){
  const obj={};
  for(let i=0;i<args.length;i+=2){
    if(!args[i].startsWith('--') || !args[i+1])throw new Error('invalid CLI args');
    obj[args[i].slice(2)]=args[i+1];
  }
  if(!taskPaths[obj.task])throw new Error('task must be title, duration or tags');
  if(obj['parent-managed-id'] && !/^[0-9a-f-]{36}$/i.test(obj['parent-managed-id']))
    throw new Error('managed_parent_id_invalid');
  if(obj['parent-managed-id'] && obj['temporary-tab-id'])
    throw new Error('managed_parent_requires_persistent_child');
  if(!/^[A-Za-z0-9_.-]{5,70}$/.test(obj.run||''))throw new Error('supply stable --run ID (5..70 characters)');
  obj.maxRounds=Number(obj['max-rounds']||'2');
  if(!Number.isInteger(obj.maxRounds)||obj.maxRounds<1||obj.maxRounds>3)throw new Error('max rounds must be 1..3');
  if(obj['parent-task-id'] && !/^[a-zA-Z0-9_.:-]{5,100}$/.test(obj['parent-task-id']))
    throw new Error('invalid parent task ID');
  if(obj['preflight-tab-id'] && (!/^[0-9]+$/.test(obj['preflight-tab-id'])||
     Number(obj['preflight-tab-id'])<1)) throw new Error('invalid preflight tab ID');
  if(obj['temporary-tab-id'] && (!/^[0-9]+$/.test(obj['temporary-tab-id'])||
     Number(obj['temporary-tab-id'])<1)) throw new Error('invalid temporary tab ID');
  return obj;
}
async function main(){
  const opt=options(process.argv.slice(2));
  const working=resolve(opt.sandbox||lab);
  const rel=relative(realpathSync(lab),realpathSync(working));
  if(rel==='..'||rel.startsWith('..'+sep)||isAbsolute(rel))
    throw new Error('iteration_sandbox_outside_lab_boundary');
  const endpoint=opt['bridge-url']||defaultEndpoint;
  const target=new URL(endpoint);
  if(target.protocol!=='http:'||target.hostname!=='127.0.0.1'||!/^\d+$/.test(target.port))
    throw new Error('bridge_endpoint_must_be_local_ipv4');
  const isolatedToken=join(process.env.LOCALAPPDATA||'',
    'OpenQuickHost','ExtensionStorage','chatgpt-agent-bridge','api-token.txt');
  const sourceToken=opt['token-file']?resolve(opt['token-file']):
    target.port==='53922'?isolatedToken:tokenFile;
  if(opt['token-file']){
    const tokenRel=relative(realpathSync(lab),realpathSync(sourceToken));
    if(tokenRel==='..'||tokenRel.startsWith('..'+sep)||isAbsolute(tokenRel))
      throw new Error('mock_token_file_outside_lab');
  }
  const token=readFileSync(sourceToken,'utf8').trim();
  const headers={Authorization:'Bearer '+token,'X-Bridge-Request':'1','Content-Type':'application/json; charset=utf-8'};
  async function api(path,method='GET',body){
    const resp=await fetch(endpoint+path,{method,headers,body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(12000)});
    const data=await resp.json();
    if(!resp.ok)throw new Error('bridge HTTP '+resp.status+': '+(data.error||JSON.stringify(data)).slice(0,280));
    return data;
  }
  const health=await api('/health');
  if(!health.connected)throw new Error('bridge browser extension offline');
  // Optional explicit health gate: fail before starting a new child if the
  // reference managed tab has an unresponsive renderer or an occupied draft.
  async function waitJob(jobId,limitMs=110000){
    const until=Date.now()+limitMs;
    while(Date.now()<until){
      const job=await api('/api/jobs/'+jobId);
      if(!['queued','running'].includes(job.status))return job;
      await delay(1200);
    }
    throw new Error('bridge job remains active after bounded wait; do not resubmit');
  }
  async function requireReadyTab(tabId){
    const job=await api('/api/jobs','POST',{action:'chatgpt_diagnostics',tabId,timeoutSeconds:18});
    const inspected=await waitJob(job.id,25000);
    const dom=inspected.data?.dom;
    if(inspected.status!=='success'||inspected.data?.error||!dom?.composerExists||
       dom.challenge || dom.loginRequired || dom.generating || dom.draftPresent)
       throw new Error('browser preflight not ready: '+JSON.stringify(inspected.data||inspected.message).slice(0,400));
  }
  if(opt['preflight-tab-id']) await requireReadyTab(Number(opt['preflight-tab-id']));
  const managedId=opt['parent-managed-id']||null;
  let managedParent=null;
  const parentPath=managedId?'/api/managed-parents/'+managedId:null;
  const verifyManagedParent=async()=>{
    const p=await api(parentPath);
    if(p.id!==managedId||p.status!=='ready'||
       p.taskId!=='managed-parent:'+managedId||
       !/^[a-zA-Z0-9-]{8,120}$/.test(p.conversationId||'')||
       p.url!=='https://chatgpt.com/c/'+p.conversationId)
       throw new Error('managed_parent_identity_or_readiness_invalid');
    if(managedParent && (p.url!==managedParent.url||
        p.conversationId!==managedParent.conversationId))
       throw new Error('managed_parent_identity_changed');
    managedParent=p;
    return p;
  };
  if(managedId)await verifyManagedParent();
  const fileNames=taskPaths[opt.task];
  const original=readFileSync(join(working,fileNames[0]),'utf8');
  const tests=readFileSync(join(working,fileNames[1]),'utf8');
  const originalHash=digest(original);
  const record={runId:opt.run,task:opt.task,createdUtc:new Date().toISOString(),
    initialSha256:originalHash,extensionVersion:health.extensionVersion,
    maxRounds:opt.maxRounds,rounds:[],status:'running'};
  const path=join(working,'results','iteration-'+opt.run+'.json');
  mkdirSync(dirname(path),{recursive:true});
  let parentLock=null,runLock=null;
  if(managedId){
    runLock=join(dirname(path),'managed-'+opt.run+'.lock');
    parentLock=join(lab,'managed-parent-'+managedId+'.lock');
    const fd=openSync(runLock,'wx');closeSync(fd);
    try{const pfd=openSync(parentLock,'wx');closeSync(pfd);}
    catch(error){unlinkSync(runLock);throw error;}
    record.parentManagedId=managedId;
    record.parentUrl=managedParent.url;
  }
  const save=()=>writeFileSync(path,JSON.stringify(record,null,2),'utf8');
  const requirement='You do NOT have direct access to the local Windows filesystem. '
    +'Supply a JSON patch for a narrowly restricted Windows sandbox executor. '
    +'Output ONLY ONE VALID JSON OBJECT with keys task, expectedSha256 and source. '
    +'No markdown fences, no explanation. Do not edit tests, do not claim to have performed file edits. '
    +'The executor will write the source, run pinned tests with restricted Node permissions, '
    +'and send actual failures for one bounded revision.';
  const taskPrompt=(round=1,feedback='')=>[
    requirement,'Task: '+opt.task,
    'Correlation marker: YANZI_LAB_RUN_'+opt.run+'_ROUND_'+round,
    'Expected SHA-256: '+originalHash,
    'Source to replace (entire module):\n'+original,
    'Fixed tests, NOT MODIFIABLE:\n'+tests,
    feedback?'Previous proposal was tested and failed. Actual verification:\n'+feedback:'',
    'Your reply must be JSON such as {"task":"'+opt.task+'","expectedSha256":"'+originalHash+'","source":"export function ..."}'
  ].join('\n\n').slice(0,8500);
  let agentId=null;
  let feedback='';
  try{
    for(let round=1;round<=opt.maxRounds;round++){
      const key='lab:'+opt.run+':round-'+round;
      let jobId;
      if(opt['temporary-tab-id']){
        const tabId=Number(opt['temporary-tab-id']);
        await requireReadyTab(tabId);
        const queued=await api('/api/jobs','POST',{
          action:'chatgpt_send',tabId,newChat:false,temporary:true,
          closeAfter:false,timeoutSeconds:100,prompt:taskPrompt(round,feedback)
        });
        jobId=queued.id;
        record.tabId=tabId;
        record.mode='temporary';
      }else if(round===1){
        const created=await api('/api/subagents','POST',{
          requestKey:key,title:'受限实验 '+opt.task,
          prompt:taskPrompt(round),
          parentTaskId:managedId?'managed-parent:'+managedId:(opt['parent-task-id']||('lab:'+opt.run)),
          timeoutSeconds:90
        });
        agentId=created.id;
        jobId=created.initialJobId;
        record.agentId=agentId;record.childUrl=created.url;
      }else{
        const sent=await api('/api/subagents/'+agentId+'/messages','POST',{
          requestKey:key,prompt:taskPrompt(round,feedback)
        });
        jobId=sent.jobId;
      }
      const iteration={round,jobId,submissionStatus:'waiting'};
      record.rounds.push(iteration);save();
      const job=await waitJob(jobId,110000);
      iteration.submissionStatus=job.status;
      iteration.jobMessage=job.message||null;
      iteration.networkObservation=job.networkObservation||null;
      iteration.networkDiagnosis=job.networkDiagnosis||classifyJobNetwork(job);
      save();
      let text=job.data?.text;
      if(job.status!=='success' && opt['temporary-tab-id'] &&
         iteration.networkDiagnosis.next!=='stop_preserve_draft'){
        // Read-only recovery only: never resubmit uncertain prompts.
        try{
          const tabId=Number(opt['temporary-tab-id']);
          const diagnostic=await api('/api/jobs','POST',{action:'chatgpt_diagnostics',tabId,timeoutSeconds:18});
          const inspected=await waitJob(diagnostic.id,25000);
          if(inspected.status!=='success'||inspected.data?.error||
             inspected.data?.dom?.generating||inspected.data?.dom?.challenge)
            throw new Error('page_unstable_after_failed_send');
          const read=await api('/api/jobs','POST',{action:'chatgpt_messages',tabId,timeoutSeconds:18});
          const readResult=await waitJob(read.id,25000);
          const messages=readResult.data?.messages;
          const marker='YANZI_LAB_RUN_'+opt.run+'_ROUND_'+round;
          if(readResult.status!=='success'||!Array.isArray(messages)||messages.length<2||
             messages.at(-2).role!=='user'||messages.at(-1).role!=='assistant'||
             !String(messages.at(-2).text).includes(marker))
            throw new Error('no_matching_complete_conversation_turn');
          const candidate=String(messages.at(-1).text||'');
          const parsed=JSON.parse(candidate);
          if(parsed.task!==opt.task||parsed.expectedSha256!==originalHash)
            throw new Error('recovered_reply_source_identity_mismatch');
          text=candidate;
          iteration.reconciledAfterTimeout=true;
        }catch(error){iteration.reconciliationError=error.message;}
      }
      if(job.status!=='success' && !text){
        record.status=job.status==='error' && job.message==='输入框已有其他草稿，请先处理草稿' ? 'pre_submit_blocked' : 'browser_or_submission_failed';
        record.reason=record.status==='pre_submit_blocked' ? 'Draft preserved; no message sent' :
          'Uncertain web submission: '+iteration.networkDiagnosis.status+'; '+
          iteration.networkDiagnosis.reason+'; no automatic resubmission';
        break;
      }
      if(!opt['temporary-tab-id']){
        const child=await api('/api/subagents/'+agentId);
        record.childUrl=child.url;
      }
      let result;
      try{
        if(typeof text!=='string'||text.length>4000)throw new Error('model_did_not_return_bounded_json');
        const proposal=JSON.parse(text.trim());
        if(proposal.task!==opt.task)throw new Error('model_proposed_different_task');
        result=applyProposal(proposal,{sandbox:working});
        iteration.verification=result;
      }catch(error){
        result={status:'rejected',reason:error.message};
        iteration.verification=result;
      }
      save();
      if(managedId){
        await verifyManagedParent();
        const proof={
          runId:opt.run,task:opt.task,round,
          childJobId:jobId,childId:agentId,childUrl:record.childUrl,
          executorStatus:result.status,
          testPassed:result.testPassed===true,
          testsUnchanged:result.testsUnchanged===true,
          rollback:result.rollback===true,
          reason:result.reason||null,
          sourceSha256Before:result.sourceSha256Before||originalHash,
          sourceSha256After:result.sourceSha256After||null,
          testLog:String(result.testLog||'').slice(-900)
        };
        const message=[
          '[燕子本地独立测试器的可信结果]',
          '只以本地测试证据决策；子 Agent 的自述不能证明任务完成。',
          '严格回复 JSON：{"decision":"accept|retry|stop","reason":"..."}',
          'accept 只允许 executorStatus=passed 且 testPassed=true 且 testsUnchanged=true。',
          JSON.stringify(proof)
        ].join('\n');
        iteration.proofSha256=digest(JSON.stringify(result));
        iteration.parentDelivery='intent_recorded';save();
        const queued=await api(parentPath+'/verified-feedback','POST',{
          requestKey:'verified:'+opt.run+':'+round,
          childId:agentId,childJobId:jobId,
          task:opt.task,round,verification:result
        });
        iteration.parentJobId=queued.jobId;
        iteration.parentDelivery='waiting_response';save();
        const assessment=await waitJob(queued.jobId,170000);
        iteration.parentJobStatus=assessment.status;
        if(assessment.status!=='success'){
          record.status='needs_review';
          iteration.parentDelivery='uncertain_do_not_resend';
          record.reason=assessment.message||'parent_message_may_have_been_sent';
          break;
        }
        const reply=assessment.data;
        if(reply?.url!==managedParent.url||
            reply?.conversationId!==managedParent.conversationId||
            reply?.temporary!==false)
          throw new Error('parent_response_identity_mismatch');
        let decision;
        try{decision=JSON.parse(reply.text);}catch{throw new Error('parent_reply_not_json');}
        if(!decision||!['accept','retry','stop'].includes(decision.decision)||
           typeof decision.reason!=='string'||decision.reason.length>300)
          throw new Error('parent_reply_schema_invalid');
        iteration.parentDecision=decision;save();
        const passed=result.status==='passed' && result.testPassed===true &&
           result.testsUnchanged===true;
        if(decision.decision==='accept' && !passed)
          throw new Error('parent_attempted_accept_without_test_proof');
        if(decision.decision==='retry' && passed)
          throw new Error('parent_requested_retry_after_source_changed');
        if(decision.decision==='accept'){
          record.status='verified_pass';break;
        }
        if(decision.decision==='stop'){
          record.status='stopped_by_parent';break;
        }
      }
      if(result.status==='passed'){
        record.status='verified_pass';
        record.verifiedAt=new Date().toISOString();
        break;
      }
      feedback=JSON.stringify(result).slice(0,2500);
      record.status=round===opt.maxRounds?'failed_after_max_rounds':'revision_requested';
      save();
    }
  }catch(error){
    record.status='stopped_needs_review';
    record.reason=error.message;
  }
  record.finishedUtc=new Date().toISOString();save();
  // A completed and verified run frees its parent's local lease. The per-run
  // lock stays permanently as the idempotency record. Any uncertain outcome
  // deliberately keeps the parent lease for explicit manual review.
  if(parentLock && ['verified_pass','stopped_by_parent','failed_after_max_rounds','pre_submit_blocked'].includes(record.status))
    unlinkSync(parentLock);
  console.log(JSON.stringify({status:record.status,agentId,rounds:record.rounds.length,report:path,
    childUrl:record.childUrl||null,reason:record.reason||null}));
  if(record.status!=='verified_pass')process.exitCode=2;
}
main().catch(error=>{console.error('LAB_ITERATION_FAIL_CLOSED='+error.message);process.exitCode=2;});
