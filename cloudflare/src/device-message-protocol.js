// Transport-independent routing rules. Never infer a device identity from its platform.
export const DEVICE_MESSAGE_PROTOCOL = Object.freeze({
  name: 'yanzi.device-messaging', version: 1, supportedVersions: [1],
  transports: ['https', 'websocket', 'sse', 'lan-aead-v1'],
  routing: ['device', 'account-chat', 'platform'],
  delivery: 'at-least-once', idempotency: 'clientMessageId',
  receiptScope: 'device', commandRouting: 'single-device',
  features: ['execution-deadline', 'cancel-before-execution', 'execution-claim', 'sequence-pagination', 'trace-context'],
  executionDeadlineSeconds: 120,
  maximumExecutionDeadlineSeconds: 300,
  limits: { attachmentBytes: 30 * 1024 * 1024, textCharacters: 4000 },
});

export function acceptsAccountChat(device) {
  return ['android', 'desktop'].includes(device.platform) || device.capabilities?.receiveAccountChat === true;
}

export function isAccountChat(message) {
  return !message.targetDeviceId && message.payload?.accountChat === true;
}

export function messageMatchesDevice(message, device) {
  if (message.targetDeviceId) return message.targetDeviceId === device.deviceId;
  if (isAccountChat(message)) return message.sourceDeviceId !== device.deviceId && acceptsAccountChat(device);
  return message.targetPlatform === device.platform;
}

export function isExecutionMessage(kind) {
  return kind.startsWith('run-') || kind.startsWith('fs-') || kind === 'capability.invoke';
}

export function canonicalMessageJson(value) {
  if (Array.isArray(value)) return '[' + value.map(canonicalMessageJson).join(',') + ']';
  if (value && typeof value === 'object') return '{' + Object.keys(value).sort()
    .map(key => JSON.stringify(key) + ':' + canonicalMessageJson(value[key])).join(',') + '}';
  return JSON.stringify(value);
}

export function executionDeadline(kind, supplied, now = Date.now()) {
  if (!isExecutionMessage(kind)) return supplied;
  const maximum = now + DEVICE_MESSAGE_PROTOCOL.maximumExecutionDeadlineSeconds * 1000;
  return new Date(supplied ? Math.min(Date.parse(supplied), maximum) :
    now + DEVICE_MESSAGE_PROTOCOL.executionDeadlineSeconds * 1000).toISOString();
}

export function traceContext(envelope, clientId) {
  const result = {};
  for (const name of ['operationId', 'correlationId', 'causationId', 'traceId']) {
    const value = envelope[name] ?? envelope.payload?.[name];
    if (value != null && (typeof value !== 'string' || !/^[a-zA-Z0-9_.:-]{1,100}$/.test(value)))
      throw new TypeError('Invalid ' + name);
    if (value != null) result[name] = value;
  }
  result.clientMessageId = clientId || null;
  result.operationId ||= clientId || crypto.randomUUID();
  result.traceId ||= result.operationId;
  return result;
}
