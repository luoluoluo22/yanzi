// Whitelisted network evidence. Never store URLs, headers, cookies or message bodies.
const allowedMethods=new Set(['GET','POST','PUT','PATCH','DELETE','OTHER']);
const allowedTypes=new Set(['xmlhttprequest','fetch','websocket','main_frame','sub_frame','script','stylesheet','image','other']);
const allowedClasses=new Set(['conversation_api','site_api','page_resource']);
const allowedBands=new Set(['2xx','3xx','4xx','5xx','other','unknown']);
const stages=new Set(['watch_started','page_dispatch_started','page_reply_received']);
function boundedInt(value,max){
  return Number.isSafeInteger(value)&&value>=0&&value<=max?value:0;
}
function filterCounts(object,allowed){
  const out={};
  if(!object||typeof object!=='object'||Array.isArray(object))return out;
  for(const key of allowed)
    if(Object.prototype.hasOwnProperty.call(object,key)){
      const value=boundedInt(object[key],200);
      if(value)out[key]=value;
    }
  return out;
}
export function sanitizeNetworkEvidence(input){
  if(!input||typeof input!=='object'||Array.isArray(input)||
     !Number.isSafeInteger(input.tabId)||input.tabId<1)
    throw new Error('network_evidence_invalid');
  const started=boundedInt(input.started,200);
  const completed=boundedInt(input.completed,200);
  const failed=boundedInt(input.failed,200);
  return {
    tabId:input.tabId,available:input.available===true,
    sampledMs:boundedInt(input.sampledMs,1_200_000),
    started,completed:Math.min(started,completed),
    failed:Math.min(started,failed),
    pending:boundedInt(input.pending,200),
    capped:input.capped===true,
    longestMs:boundedInt(input.longestMs,1_200_000),
    methods:filterCounts(input.methods,allowedMethods),
    types:filterCounts(input.types,allowedTypes),
    classes:filterCounts(input.classes,allowedClasses),
    statusBands:filterCounts(input.statusBands,allowedBands)
  };
}
export function classifyJobNetwork(job){
  const status=job?.status||'unknown';
  const o=job?.networkObservation;
  const net=o?.latest || (job?.data?.network?.available ? sanitizeNetworkEvidence(job.data.network):null);
  const stage=o?.stage || null;
  const started=net?.started||0;
  const conversation=net?.classes?.conversation_api||0;
  const message=String(job?.message||'');
  if(['queued','running'].includes(status))return {
    status:'in_progress',next:'wait',reason:'task_not_terminal'};
  if(status==='success')return {
    status:'reply_confirmed',next:'verify_reply',reason:'completed_web_reply'};
  if(message.includes('输入框已有其他草稿'))return {
    status:'draft_guard_blocked',next:'stop_preserve_draft',reason:'composer_guard_refused_submission'};
  if(conversation>0)return {
    status:'conversation_network_activity',next:'read_only_reconcile',
    reason:'conversation_class_traffic_does_not_prove_prompt_acceptance'};
  if(stage==='page_reply_received')return {
    status:'reply_callback_observed',next:'read_only_reconcile',
    reason:'browser_reply_observed_before_bridge_terminal'};
  if(stage==='page_dispatch_started')return {
    status:'dispatch_outcome_unknown',next:'read_only_reconcile',
    reason:'page_message_dispatch_started_without_final_reply'};
  if(message.includes('页面渲染进程无响应') ||
     message.includes('标签页加载超时'))return {
    status:'renderer_or_load_blocked',next:'stop_needs_review',
    reason:'no_proof_that_a_message_was_submitted'};
  if(started>0)return {
    status:'unattributed_network_activity',next:'stop_needs_review',
    reason:'resource_activity_not_message_delivery'};
  return {
    status:'no_network_evidence',next:'stop_needs_review',
    reason:'absence_of_observation_does_not_prove_no_submission'};
}
export function isAllowedProgressStage(value){return stages.has(value);}
