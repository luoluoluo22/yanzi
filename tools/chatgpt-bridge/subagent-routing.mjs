import { randomUUID } from 'node:crypto';
import { parseConversationUrl } from './origin-routing.mjs';

export function validateSubagentCreate(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input))
    throw new Error('subagent_request_required');
  if (typeof input.prompt !== 'string' || !input.prompt.trim() || input.prompt.length > 50000)
    throw new Error('subagent_prompt_invalid');
  if (typeof input.requestKey !== 'string' ||
      !/^[a-zA-Z0-9_.:-]{5,128}$/.test(input.requestKey))
    throw new Error('subagent_request_key_required');
  if (input.parentTaskId != null && (typeof input.parentTaskId !== 'string' ||
      !/^[a-zA-Z0-9_.:-]{5,100}$/.test(input.parentTaskId)))
    throw new Error('subagent_parent_task_invalid');
  if (input.parentManagedId != null && (typeof input.parentManagedId !== 'string' ||
      !/^[0-9a-f-]{36}$/i.test(input.parentManagedId)))
    throw new Error('subagent_managed_parent_invalid');
  if (input.parentOriginId != null &&
      (typeof input.parentOriginId !== 'string' || !/^[0-9a-f-]{36}$/i.test(input.parentOriginId)))
    throw new Error('subagent_parent_origin_invalid');
  if (input.title != null && (typeof input.title !== 'string' || input.title.length > 140))
    throw new Error('subagent_title_invalid');
  if (input.timeoutSeconds != null &&
      (!Number.isInteger(input.timeoutSeconds) || (input.timeoutSeconds !== 0 &&
        (input.timeoutSeconds < 10 || input.timeoutSeconds > 1200))))
    throw new Error('subagent_timeout_invalid');
  return {
    prompt: input.prompt.trim(), requestKey:input.requestKey,
    title: (input.title || '子 Agent').trim() || '子 Agent',
    parentOriginId: input.parentOriginId || null,
    parentTaskId: input.parentTaskId || null,
    parentManagedId: input.parentManagedId || null,
    timeoutSeconds: input.timeoutSeconds ?? 180
  };
}

export function validateSubagentMessage(input) {
  if (!input || typeof input.prompt !== 'string' ||
      !input.prompt.trim() || input.prompt.length > 50000)
    throw new Error('subagent_message_invalid');
  if (typeof input.requestKey !== 'string' ||
      !/^[a-zA-Z0-9_.:-]{5,128}$/.test(input.requestKey))
    throw new Error('subagent_request_key_invalid');
  return { prompt: input.prompt.trim(), requestKey: input.requestKey };
}

export function childIdentityFromReply(response) {
  const identity = parseConversationUrl(response?.url);
  if (!identity || response?.conversationId !== identity.conversationId ||
      !Number.isSafeInteger(response?.tabId) || response.tabId < 1 ||
      response.temporary !== false)
    return null;
  return { tabId: response.tabId, conversationId: identity.conversationId, url: identity.url };
}

export function createSubagentRecord(input, jobId, now = Date.now()) {
  return {
    id: randomUUID(), title: input.title, parentOriginId: input.parentOriginId,
    parentTaskId: input.parentTaskId, parentManagedId: input.parentManagedId,
    createdAt: now, status: 'starting', initialJobId: jobId, lastJobId: jobId,
    tabId: null, url: null, conversationId: null, rounds: [{jobId, round: 1, startedAt:now}]
  };
}

export function makeContinuationTask(subagent, prompt) {
  const identity = parseConversationUrl(subagent?.url);
  if (!identity || identity.conversationId !== subagent?.conversationId ||
      subagent.status !== 'ready') throw new Error('subagent_not_ready_or_unbound');
  return {
    action:'chatgpt_subagent_continue', prompt,
    expectedUrl:identity.url, expectedConversationId:identity.conversationId,
    newChat:false, temporary:false, closeAfter:false,
    timeoutSeconds:180
  };
}

export function makeParentSummary(subagent, childJob) {
  const text = typeof childJob?.data?.text === 'string' ? childJob.data.text : '';
  const label = childJob.status === 'success' ? '完成' : '执行异常，可能需要查看子对话';
  const lines = [
    '[燕子子 Agent 结果回传]',
    '子任务：' + subagent.title,
    '子 Agent ID：' + subagent.id,
    '执行状态：' + label,
    '轮次：' + subagent.rounds.length,
    '子对话：' + (subagent.url || '尚未形成正式对话 URL'),
    '任务 Job：' + childJob.id,
    '结果：' + (text.trim().slice(0,4000) || childJob.message || '没有可用的文本回复')
  ];
  return lines.join('\n').slice(0,6500);
}

/**
 * A managed parent is its own saved ChatGPT conversation. Its stable ID
 * doubles as a routing key for durable child event notifications.
 */
export function validateManagedParentCreate(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input))
    throw new Error('managed_parent_request_required');
  if (typeof input.requestKey !== 'string' ||
      !/^[A-Za-z0-9_.:-]{5,128}$/.test(input.requestKey))
    throw new Error('managed_parent_request_key_invalid');
  if (typeof input.prompt !== 'string' || !input.prompt.trim() ||
      input.prompt.length>30000)
    throw new Error('managed_parent_prompt_invalid');
  if (input.title != null && (typeof input.title !== 'string' || input.title.length > 140))
    throw new Error('managed_parent_title_invalid');
  return {requestKey:input.requestKey,prompt:input.prompt.trim(),
    title:(input.title||'燕子父 Agent').trim()||'燕子父 Agent'};
}

export function createManagedParentRecord(input, jobId, timestamp=Date.now()) {
  const id=randomUUID();
  return {id,taskId:'managed-parent:'+id,title:input.title,
    createdAt:timestamp,status:'starting',initialJobId:jobId,lastJobId:jobId,
    conversationId:null,url:null,tabId:null,
    deliveries:[],lastResponse:null};
}

export function makeManagedParentReturnTask(parent, text) {
  if (!parent || parent.status!=='ready') throw new Error('managed_parent_not_ready');
  const identity=parseConversationUrl(parent.url);
  if (!identity || identity.conversationId!==parent.conversationId)
    throw new Error('managed_parent_identity_invalid');
  return {action:'chatgpt_subagent_continue',prompt:text,
    expectedUrl:identity.url,expectedConversationId:identity.conversationId,
    newChat:false,temporary:false,closeAfter:false,timeoutSeconds:180};
}
