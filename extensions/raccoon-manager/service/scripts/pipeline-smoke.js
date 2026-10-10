import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";

const root = await fs.mkdtemp(path.join(os.tmpdir(),"raccoon-pipeline-smoke-"));
const base = "http://127.0.0.1:39677", token = "pipeline-smoke-only";
let stderr = "";
const child = spawn(process.execPath,["src/index.js","--http"],{windowsHide:true,stdio:["ignore","ignore","pipe"],env:{...process.env,
  RACCOON_ROOT:root,RACCOON_HOST:"127.0.0.1",RACCOON_PORT:"39677",RACCOON_TOKEN:token,RACCOON_PUBLIC_URL:"",RACCOON_OAUTH_PASSWORD:"",
  RACCOON_PIPELINE_DIR:path.join(root,".raccoon-runtime/pipelines"),RACCOON_ENABLE_SHELL:"1",RACCOON_READ_ONLY:"0",RACCOON_AUDIT_LOG:""}});
child.stderr.on("data",data=>{stderr+=data.toString();});
const exited = new Promise(resolve=>child.once("exit",resolve));
const clients = [];
async function connect() {
  const client = new Client({name:"pipeline-smoke",version:"1"});
  await client.connect(new StreamableHTTPClientTransport(new URL(base+"/mcp"),{requestInit:{headers:{Authorization:`Bearer ${token}`}}}));
  clients.push(client); return client;
}
async function call(client,name,args) {
  const response = await client.callTool({name,arguments:args});
  if(response.isError) throw new Error(response.content[0].text);
  return JSON.parse(response.content.find(x=>x.type==="text").text);
}
try {
  let healthy = false;
  for(let i=0;i<50;i++) {
    try { healthy = (await fetch(base+"/health")).ok; } catch {}
    if(healthy) break;
    await new Promise(resolve=>setTimeout(resolve,100));
  }
  if(!healthy) throw new Error("Smoke server failed: "+stderr);
  let client = await connect();
  const waited = await call(client,"wait_until",{
    command:"node -e \"console.log(JSON.stringify({ready:true}))\"",
    condition:{jsonPathEquals:{path:"$.ready",value:true}},
    intervalMs:100,timeoutMs:5000,commandTimeoutMs:5000,maxAttempts:2,idempotent:true
  });
  assert.equal(waited.matched,true);
  const pipeline = {name:"connection-independent smoke",nodes:[
    {id:"a",command:"node -e \"console.log(JSON.stringify({revision:7}))\"",capture:{revision:{jsonPath:"$.revision"}}},
    {id:"b",command:"node -e \"console.log('revision=${a.revision}')\"",dependsOn:["a"],if:"${a.revision} == 7",expect:{stdoutIncludes:"revision=7"}},
    {id:"poll",command:"node -e \"console.log('ready')\"",dependsOn:["a"],idempotent:true,retryUntil:{condition:{stdoutIncludes:"ready"},intervalMs:100,timeoutMs:5000,maxAttempts:2}},
    {id:"skip",command:"node -e \"console.log('must-not-run')\"",dependsOn:["a"],if:"${a.revision} == 999"}
  ]};
  await call(client,"dev_pipeline_validate",{pipeline});
  let report = await call(client,"dev_pipeline_start",{pipeline,idempotencyKey:"smoke-1"});
  const runId=report.runId;
  await client.close(); client = await connect();
  for(let i=0;i<100;i++) {
    report = await call(client,"dev_pipeline_wait",{runId,afterRevision:report.revision,waitMs:1000});
    if(report.status !== "running") break;
  }
  assert.equal(report.status,"succeeded");
  assert.equal(report.nodes.find(n=>n.id==="a").outputs.revision,7);
  assert.equal(report.nodes.find(n=>n.id==="b").status,"succeeded");
  assert.equal(report.nodes.find(n=>n.id==="poll").status,"succeeded");
  assert.equal(report.nodes.find(n=>n.id==="skip").status,"skipped");
  assert.equal((await call(client,"dev_pipeline_start",{pipeline,idempotencyKey:"smoke-1"})).runId,runId);
  const logs=await call(client,"dev_pipeline_logs",{runId,nodeId:"b"});
  assert.match(logs.text,/revision=7/);
  assert.ok((await call(client,"dev_pipeline_list",{})).some(r=>r.runId===runId));
  console.log(JSON.stringify({ok:true,runId,reconnectContinued:true,persistedLogs:true,deduplicated:true,waitUntil:true,variablePassing:true,conditions:true,retryUntil:true}));
} finally {
  for(const client of clients) await client.close().catch(()=>{});
  child.kill(); await exited;
  await fs.rm(root,{recursive:true,force:true});
}
