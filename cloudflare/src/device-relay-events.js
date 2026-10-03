// These events contain no authorization secrets; recipients fetch account-scoped durable state.
export function isAccountWakeEvent(event) {
  return event.type === 'sync-ready' || event.type === 'external-access-ready';
}
