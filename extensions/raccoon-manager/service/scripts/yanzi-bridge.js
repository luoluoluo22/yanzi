// Authorized loopback adapter for Yanzi's account-scoped device relay.
// Input: a JSON object via stdin; output: one JSON object on stdout.
import fs from 'node:fs';
import path from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';

const runtime = process.env.RACCOON_RUNTIME_DIR
  || path.join(process.env.LOCALAPPDATA || '', 'OpenQuickHost', 'McpRuntime', 'raccoon');
const envFile = path.join(runtime, '.env');
const entries = fs.readFileSync(envFile, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/);
const options = Object.fromEntries(entries
  .filter(line => /^[A-Z][A-Z0-9_]*=/.test(line))
  .map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]));
const port = Number(options.RACCOON_PORT || '3766');
if (!Number.isInteger(port) || port <= 0 || port > 65535 || !options.RACCOON_TOKEN) {
  throw new Error('Raccoon local runtime is not provisioned.');
}
const chunks = [];
for await (const chunk of process.stdin) chunks.push(chunk);
const request = JSON.parse(Buffer.concat(chunks).toString('utf8'));
if (!['list', 'call'].includes(request.action)) throw new Error('Unsupported bridge action.');
if (request.action === 'call' && (!/^[a-zA-Z][a-zA-Z0-9_.:-]{0,127}$/.test(request.name) ||
    !request.arguments || typeof request.arguments !== 'object' || Array.isArray(request.arguments))) {
  throw new Error('Invalid MCP tool name or arguments.');
}
const client = new Client({ name: 'yanzi-raccoon-loopback', version: '1.0.0' });
const transport = new StreamableHTTPClientTransport(new URL('http://127.0.0.1:' + port + '/mcp'), {
  requestInit: { headers: { Authorization: 'Bearer ' + options.RACCOON_TOKEN } }
});
try {
  await client.connect(transport);
  const result = request.action === 'list' ? await client.listTools() : await client.callTool({
    name: request.name, arguments: request.arguments
  });
  console.log(JSON.stringify(result));
} catch (error) {
  console.error('Raccoon bridge call failed: ' + (error?.message || 'unknown error').replaceAll(options.RACCOON_TOKEN, '[redacted]'));
  process.exitCode = 1;
} finally {
  await client.close().catch(() => {});
}
