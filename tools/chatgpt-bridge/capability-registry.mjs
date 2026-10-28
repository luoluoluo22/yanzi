/**
 * Versioned, explicit browser capability registry.
 * This layer describes safe, callable site commands; it never executes arbitrary
 * browser code and never grants a new site without an installed executor.
 *
 * Inspired by OpenCLI's site/command adapters. Calls are still run by Yanzi's
 * existing authenticated, queued ChatGPT bridge.
 */
const COMMON = Object.freeze({
  tabId: { type: 'integer', minimum: 1, description: 'Specific ChatGPT tab (never infer an occupied user tab)' },
  timeoutSeconds: { type: 'integer', minimum: 0, maximum: 1200 },
  temporary: { type: 'boolean', description: 'Defaults to true for ordinary chats' },
  closeAfter: { type: 'boolean' },
  tabPolicy: { type: 'string', enum: ['reuse', 'new'] },
  newChat: { type: 'boolean' },
  prompt: { type: 'string', minLength: 1, maxLength: 100000 },
  files: { type: 'array', maxItems: 4 },
  includePageSnapshot: { type: 'boolean' }
});

function define(site, command, title, action, access, required = [], allowed = []) {
  return Object.freeze({
    id: site + '.' + command,
    site, command, title, action, access,
    inputSchema: Object.freeze({
      type: 'object',
      additionalProperties: false,
      required: [...required],
      properties: Object.fromEntries(allowed.map(key => [key, COMMON[key]]))
    })
  });
}

const DEFINITIONS = Object.freeze([
  define('chatgpt', 'ask', 'Ask ChatGPT and await response', 'chatgpt_send', 'write',
    ['prompt'], ['prompt', 'files', 'tabId', 'newChat', 'temporary', 'tabPolicy', 'closeAfter', 'timeoutSeconds']),
  define('chatgpt', 'read', 'Read the selected conversation', 'chatgpt_messages', 'read',
    [], ['tabId', 'includePageSnapshot', 'timeoutSeconds']),
  define('chatgpt', 'new', 'Open a new ChatGPT conversation', 'chatgpt_new_chat', 'write',
    [], ['tabId', 'temporary', 'tabPolicy', 'timeoutSeconds']),
  define('chatgpt', 'status', 'Inspect browser connection and conversations', 'chatgpt_status', 'read',
    [], ['timeoutSeconds']),
  define('chatgpt', 'image', 'Generate an image in ChatGPT', 'chatgpt_image', 'write',
    ['prompt'], ['prompt', 'files', 'tabId', 'newChat', 'temporary', 'tabPolicy', 'closeAfter', 'timeoutSeconds']),
  define('chatgpt', 'images', 'Collect images from a conversation', 'chatgpt_capture_images', 'read',
    ['tabId'], ['tabId', 'timeoutSeconds']),
  define('chatgpt', 'cleanup', 'Close only idle Yanzi-owned tabs', 'chatgpt_cleanup', 'write',
    [], ['timeoutSeconds'])
]);

const BY_ID = new Map(DEFINITIONS.map(def => [def.id, def]));
const FORBIDDEN_KEYS = new Set(['__proto__', 'prototype', 'constructor']);

export function listBrowserCapabilities() {
  // Create detached JSON-safe data so callers cannot mutate the registry.
  return { version: 1, adapters: [{ site: 'chatgpt', transport: 'yanzi-browser-extension', authenticated: true }],
    capabilities: DEFINITIONS.map(({ id, site, command, title, access, inputSchema }) => ({
      id, site, command, title, access, inputSchema: structuredClone(inputSchema)
    })) };
}

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value) &&
    (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
}

export function resolveBrowserCapability(input) {
  if (!isRecord(input)) throw new Error('capability_request_invalid');
  const id = input.capability ?? ((typeof input.site === 'string' && typeof input.command === 'string')
    ? input.site + '.' + input.command : null);
  if (typeof id !== 'string' || !BY_ID.has(id)) throw new Error('capability_not_supported');
  for (const key of Object.keys(input)) {
    if (!['capability', 'site', 'command', 'arguments'].includes(key) || FORBIDDEN_KEYS.has(key))
      throw new Error('capability_request_field_not_supported:' + key);
  }
  if (input.site != null && input.site !== BY_ID.get(id).site) throw new Error('capability_site_mismatch');
  if (input.command != null && input.command !== BY_ID.get(id).command) throw new Error('capability_command_mismatch');
  const args = input.arguments ?? {};
  if (!isRecord(args)) throw new Error('capability_arguments_invalid');
  const definition = BY_ID.get(id);
  const schema = definition.inputSchema;
  for (const key of schema.required) {
    if (!Object.hasOwn(args, key)) throw new Error('capability_required:' + key);
  }
  for (const [key, value] of Object.entries(args)) {
    if (FORBIDDEN_KEYS.has(key) || !Object.hasOwn(schema.properties, key))
      throw new Error('capability_argument_not_supported:' + key);
    const field = schema.properties[key];
    if (field.type === 'string' && (typeof value !== 'string' || (field.minLength && !value.trim()) ||
        (field.maxLength && value.length > field.maxLength) ||
        (field.enum && !field.enum.includes(value)))) throw new Error('capability_argument_invalid:' + key);
    if (field.type === 'integer' && (!Number.isInteger(value) ||
        (field.minimum != null && value < field.minimum) ||
        (field.maximum != null && value > field.maximum))) throw new Error('capability_argument_invalid:' + key);
    if (field.type === 'boolean' && typeof value !== 'boolean') throw new Error('capability_argument_invalid:' + key);
    if (field.type === 'array' && (!Array.isArray(value) || value.length > field.maxItems))
      throw new Error('capability_argument_invalid:' + key);
  }
  // Controlled projection, no arbitrary task actions, JS snippets, or HTTP destinations.
  return { capability: id, access: definition.access, task: { action: definition.action, ...args } };
}
