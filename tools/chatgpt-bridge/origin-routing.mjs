import { randomUUID } from 'node:crypto';

export function parseConversationUrl(value) {
  if (typeof value !== 'string' || value.length > 2048) return null;
  let parsed;
  try { parsed = new URL(value); } catch { return null; }
  if (parsed.protocol !== 'https:' || parsed.hostname !== 'chatgpt.com' ||
      parsed.username || parsed.password || parsed.port) return null;
  const match = /^\/c\/([a-zA-Z0-9_-]{8,120})\/?$/.exec(parsed.pathname);
  if (!match) return null;
  return { conversationId: match[1], url: 'https://chatgpt.com/c/' + match[1] };
}

export function createOriginBinding(input, now = Date.now()) {
  const parsed = parseConversationUrl(input?.url);
  if (!parsed || !Number.isSafeInteger(input?.tabId) || input.tabId <= 0 ||
      (input?.conversationId != null && input.conversationId !== parsed.conversationId)) {
    throw new Error('origin_identity_invalid');
  }
  if (input?.temporary === true) throw new Error('temporary_conversation_cannot_be_bound');
  return { id: randomUUID(), conversationId: parsed.conversationId, url: parsed.url,
    tabId: input.tabId, boundAt: now, status: 'bound' };
}

export function validateFeedbackRequest(input) {
  if (!input || typeof input !== 'object') throw new Error('invalid_feedback');
  if (typeof input.originId !== 'string' || !/^[a-f0-9-]{36}$/i.test(input.originId))
    throw new Error('origin_id_required');
  if (typeof input.deliveryKey !== 'string' ||
      !/^[a-zA-Z0-9_:.-]{5,128}$/.test(input.deliveryKey))
    throw new Error('delivery_key_required');
  if (typeof input.text !== 'string' || !input.text.trim() || input.text.length > 7000)
    throw new Error('feedback_text_invalid');
  return { originId: input.originId, deliveryKey: input.deliveryKey, text: input.text.trim() };
}

export function makeFeedbackTask(origin, text) {
  const parsed = parseConversationUrl(origin?.url);
  if (!parsed || origin.conversationId !== parsed.conversationId ||
      origin.status !== 'bound') throw new Error('origin_no_longer_valid');
  return {
    action: 'chatgpt_feedback_send',
    prompt: text,
    expectedUrl: parsed.url,
    expectedConversationId: parsed.conversationId,
    temporary: false, newChat: false, closeAfter: false,
    timeoutSeconds: 150
  };
}
