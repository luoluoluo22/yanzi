import test from 'node:test';
import assert from 'node:assert/strict';
import os from 'node:os';
import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
const appDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const portA = 39901, portB = 39902;
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function ready(port) {
  for (let i=0; i<60; i++) {
    try { if ((await (await fetch(`http://127.0.0.1:${port}/health`)).json()).ok) return; }
    catch {}
    await pause(100);
  }
  throw Error(`port ${port} did not open`);
}
function start(port, temp, token) {
  return spawn(process.execPath,[path.join(appDir,'src/index.js'),'--http'],{
    cwd: appDir, windowsHide:true, stdio:'ignore',
    env:{...process.env,RACCOON_ENV_FILE:path.join(temp,'does-not-exist.env'),
      RACCOON_ROOT:temp,RACCOON_TRANSPORT:'http',RACCOON_HOST:'127.0.0.1',RACCOON_PORT:String(port),
      RACCOON_PUBLIC_URL:'',RACCOON_OAUTH_PASSWORD:'',RACCOON_TOKEN:token,
      RACCOON_SETTINGS_FILE:path.join(temp,'settings.json'),RACCOON_READ_ONLY:'0',RACCOON_ENABLE_SHELL:'0'}
  });
}
test('MCP tool deviceName forwards by configured peer name', async () => {
  const tmp = await fs.mkdtemp(path.join(os.tmpdir(),'mcp-device-name-'));
  const a = path.join(tmp,'main'),b = path.join(tmp,'peer');
  await fs.mkdir(a); await fs.mkdir(b);
  await fs.writeFile(path.join(a,'settings.json'), JSON.stringify({
    remoteDevices:[{id:'notebook-test',name:'笔记本',url:`http://127.0.0.1:${portB}/mcp`,token:'peer-secret-test'}]
  }));
  const peer=start(portB,b,'peer-secret-test'), main=start(portA,a,'main-secret-test');
  let client;
  try {
    await Promise.all([ready(portA),ready(portB)]);
    client = new Client({name:'device-name-test',version:'1.0'});
    await client.connect(new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${portA}/mcp`),{
      requestInit:{headers:{Authorization:'Bearer main-secret-test'}}}));
    const catalog=await client.listTools();
    for (const tool of ['ping','fs_list']) {
      assert.ok(catalog.tools.find(t=>t.name===tool)?.inputSchema?.properties?.deviceName, `${tool} must expose deviceName`);
    }
    const result=await client.callTool({name:'ping',arguments:{deviceName:'笔记本'}});
    assert.equal(result.isError,undefined,JSON.stringify(result.content));
    const data=JSON.parse(result.content.find(c=>c.type==='text').text);
    assert.equal(path.resolve(data.root), path.resolve(b));
  } finally {
    await client?.close().catch(()=>{});
    for(const proc of [main,peer]){proc.kill();}
    await fs.rm(tmp,{recursive:true,force:true,retryDelay:250,maxRetries:6});
  }
});
