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
  const message = [
    '[YANZI_AGENT_BOOTSTRAP_V1]',
    '你现在是燕子托管的 Agent。以下元信息来自本地 Agent Registry，是你自己的稳定身份。',
    '本消息不是用户的新任务；不要再次派生子任务，也不要自行重复发回此初始化消息。',
    JSON.stringify(payload, null, 2),
    '在你的后续脚本中，必须使用上方 agentId 来指明自身身份；不能用当前活跃标签页猜测。',
    '查询自身元信息：GET http://127.0.0.1:53921/api/agents/' + context.agentId + '/context',
    '本机调用要通过已获授权的燕子/浣熊执行器，并使用 Bridge 的本地鉴权。不要在网页输出或转发密钥。',
    context.role === 'supervisor'
      ? '派发子 Agent：POST /api/subagents 并填入 parentManagedId=' + context.agentId + '，bootstrap=true；子任务完成后燕子自动汇总并返回当前对话。'
      : '你的父级 agentId=' + (context.parentAgentId || '未绑定') + '；由燕子根据任务关系将完成事件回传父级，不需要你猜测父聊天 URL。',
    '确认已读取身份后只回复 YANZI_BOOTSTRAP_ACK:' + context.agentId
  ].join('\n');
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
