import { randomUUID } from 'node:crypto';

export function validateAgentRun(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('agent_run_body_required');
  if (typeof input.requestKey !== 'string' || !/^[A-Za-z0-9_.:-]{5,128}$/.test(input.requestKey))
    throw new Error('agent_run_request_key_invalid');
  if (typeof input.goal !== 'string' || !input.goal.trim() || input.goal.length > 8000)
    throw new Error('agent_run_goal_invalid');
  if (!Array.isArray(input.children) || input.children.length < 1 || input.children.length > 3)
    throw new Error('agent_run_requires_1_to_3_children');
  const children=input.children.map((value, index) => {
    if (typeof value !== 'string' || !value.trim() || value.length > 8000)
      throw new Error('agent_run_child_invalid_'+index);
    return value.trim();
  });
  return {requestKey:input.requestKey,goal:input.goal.trim(),children};
}

export function parentPrompt(goal, children) {
  return [
    '你是燕子多 Agent 协作主管。总任务：',goal,
    '燕子调度器已经负责创建 '+children.length+' 个子 Agent；你无需自行调用工具派生。',
    '你会收到以 [燕子子 Agent 结果回传] 开头的独立回传消息。',
    '每收到一条请核对任务与执行状态，不要把失败冒充成功。',
    '全部 '+children.length+' 个子任务结束后请汇总结果，说明失败任务（如果有）。',
    '本轮初始化请只回复：主管已就绪，等待 '+children.length+' 个子任务结果。'
  ].join('\n');
}

export function newRunRecord(input, parentId, childIds, now=Date.now()) {
  return {id:randomUUID(),requestKey:input.requestKey,goal:input.goal,
    children:input.children,parentId,childIds,createdAt:now};
}

export function agentRunSnapshot(run, state) {
  const parent=state.managedParents.find(p=>p.id===run.parentId);
  const children=run.childIds.map(id=>state.subagents.find(s=>s.id===id)).filter(Boolean);
  const deliveries=parent?.deliveries.filter(d=>children.some(c=>c.id===d.subagentId)) || [];
  let status='running';
  if(!parent || children.length!==run.childIds.length ||
     parent.status==='needs_review' || children.some(c=>c.status==='needs_review') ||
     deliveries.some(d=>['needs_review','error','interrupted'].includes(d.status))) status='needs_review';
  else if(children.length===run.childIds.length && children.every(c=>c.status==='ready') &&
    children.every(c=>deliveries.some(d=>d.subagentId===c.id && ['success','verified_late'].includes(d.status))) &&
    parent?.status==='ready') status='completed';
  return {id:run.id,requestKey:run.requestKey,goal:run.goal,createdAt:run.createdAt,status,
    parent:parent?{id:parent.id,title:parent.title,status:parent.status,url:parent.url,
      lastResponse:parent.lastResponse,deliveries:deliveries.map(d=>({
        subagentId:d.subagentId,status:d.status,returnJobId:d.returnJobId,error:d.error||null
      }))}:null,
    children:children.map(c=>({id:c.id,title:c.title,status:c.status,url:c.url,
      lastResult:c.lastResult,rounds:c.rounds.length}))};
}
