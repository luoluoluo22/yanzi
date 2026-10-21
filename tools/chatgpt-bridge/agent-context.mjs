import { parseConversationUrl } from './origin-routing.mjs';

export const BOOTSTRAP_VERSION = 1;

export function agentContext(kind, record, parent = null) {
  if (!['supervisor', 'child'].includes(kind) || !record || !record.id)
    throw new Error('agent_identity_invalid');
  const identity = parseConversationUrl(record.url);
  if (!identity || identity.conversationId !== record.conversationId)
    throw new Error('agent_conversation_unbound');
  return {
    schemaVersion: BOOTSTRAP_VERSION,
    agentId: record.id,
    role: kind,
    taskId: record.taskId || record.parentTaskId || ('subagent:' + record.id),
    conversationId: identity.conversationId,
    conversationUrl: identity.url,
    parentAgentId: kind === 'child' ? (record.parentManagedId || null) : null,
    replyTo: kind === 'child' ? (record.parentManagedId || record.parentOriginId || null) : record.id,
    capabilities: kind === 'supervisor'
      ? ['agent.self', 'agent.spawn', 'agent.status', 'agent.notify']
      : ['agent.self', 'agent.status', 'agent.notify'],
    bootstrap: {
      enabled: Boolean(record.bootstrap?.enabled),
      status: record.bootstrap?.status || 'disabled',
      version: BOOTSTRAP_VERSION
    }
  };
}

export function bootstrapTask(context) {
  if (!context?.agentId || !context?.conversationUrl) throw new Error('bootstrap_context_invalid');
  const parsed = parseConversationUrl(context.conversationUrl);
  if (!parsed || parsed.conversationId !== context.conversationId) throw new Error('bootstrap_url_invalid');
  const payload = { ...context, bootstrap: { ...context.bootstrap, status: 'registered' } };
  // Single-line text prevents some ChatGPT rich-text editors from losing the
  // send-button state when many paragraph nodes are inserted programmatically.
  // Full authoritative context stays queryable via the local Agent Registry.
  const compact = {
    agentId:context.agentId,role:context.role,taskId:context.taskId,
    conversationId:context.conversationId,conversationUrl:context.conversationUrl,
    parentAgentId:context.parentAgentId,replyTo:context.replyTo,
    capabilities:context.capabilities
  };
  const message =
    '[YANZI_AGENT_BOOTSTRAP_V1] 此消息只注册你的身份，不派生任务。元信息=' +
    JSON.stringify(compact) + '。查询完整身份：GET http://127.0.0.1:53921/api/agents/' +
    context.agentId + '/context，必须通过已授权的本地燕子/浣熊工具，禁止泄露密钥。' +
    '子任务结果由燕子按 parentAgentId 自动返回。只回复 YANZI_BOOTSTRAP_ACK:' + context.agentId;
  return {
    action: 'chatgpt_subagent_continue',
    prompt: message,
    expectedUrl: parsed.url,
    expectedConversationId: parsed.conversationId,
    temporary: false, newChat: false, closeAfter: false,
    timeoutSeconds: 130
  };
}

export function verifyBootstrapAck(text, agentId) {
  return typeof text === 'string' &&
    text.includes('YANZI_BOOTSTRAP_ACK:' + agentId);
}
