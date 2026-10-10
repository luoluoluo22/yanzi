import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {createHttpApp} from '../src/http.js';
import {createYanziServer} from '../src/server.js';
import {YanziBridge} from '../src/bridge.js';
import {Client} from '@modelcontextprotocol/sdk/client/index.js';
import {StreamableHTTPClientTransport} from '@modelcontextprotocol/sdk/client/streamableHttp.js';

test('HTTP requires OAuth, publishes discovery, and carries MCP calls',async()=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'yanzi-http-test-'));
 const settingsFile=path.join(dir,'settings.json');
 await fs.writeFile(settingsFile,JSON.stringify({agentApiPort:12345,agentApiToken:'test-only-token'}));
 let installed=false;
 const extensionCap={name:'example.extension.read',description:'Read extension result',audience:'user',permissions:['file.read'],inputSchema:{type:'object',additionalProperties:false}};
 const bridge=new YanziBridge({settingsFile,fetchImpl:async url=>Response.json(url.endsWith('/health')?{ok:true}:{hostCapabilities:[],extensions:installed?[{capabilities:[extensionCap]}]:[]})});
 const app=createHttpApp({password:'x'.repeat(40),stateFile:path.join(dir,'state.json'),serverFactory:()=>createYanziServer(bridge)});
 const listener=app.listen(0,'127.0.0.1');
 await new Promise(r=>listener.once('listening',r));
 const base=`http://127.0.0.1:${listener.address().port}`;
 const client=new Client({name:'yanzi-http-check',version:'1'});
 try{
  const unauth=await fetch(base+'/mcp',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});
  assert.equal(unauth.status,401);
  assert.match(unauth.headers.get('www-authenticate'),/oauth-protected-resource/);
  assert.equal((await (await fetch(base+'/.well-known/oauth-authorization-server')).json()).issuer,'https://yanzi-mcp.luoluoluo.cc.cd/');
  const token=app.locals.oauthProvider.issue('self-test').access_token;
  await client.connect(new StreamableHTTPClientTransport(new URL(base+'/mcp'),{requestInit:{headers:{Authorization:'Bearer '+token}}}));
  assert.ok((await client.listTools()).tools.some(x=>x.name==='yanzi_ping'));
  installed=true;
  assert.ok((await client.listTools()).tools.some(x=>x.name==='yanzi_example_extension_read'));
  const result=await client.callTool({name:'yanzi_ping',arguments:{}});
  assert.equal(result.isError,false);
 }finally{await client.close();await new Promise(r=>listener.close(r));await fs.rm(dir,{recursive:true,force:true});}
});
