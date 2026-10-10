import fs from "node:fs";
import path from "node:path";
import net from "node:net";
import { spawn, execFile } from "node:child_process";
import { promisify } from "node:util";
import { createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { config } from "../src/config.js";
import { ServiceGateway } from "../src/service-gateway.js";

const root=process.cwd(),runtime=path.join(root,".raccoon-runtime"),file=path.join(runtime,"gateway.json");
const initial=JSON.parse(fs.readFileSync(file,"utf8"));
const headers={Authorization:`Bearer ${config.token}`};
const alive=pid=>{try{process.kill(pid,0);return true;}catch{return false;}};
const persist=current=>{
  const temp=file+".tmp";fs.writeFileSync(temp,JSON.stringify({pid:process.pid,port:initial.port,current},null,2));fs.renameSync(temp,file);
  fs.writeFileSync(path.join(runtime,"service.json"),JSON.stringify({...current,startedAt:new Date().toISOString()},null,2));
};
async function control(worker,endpoint,method="GET") {
  const response=await fetch(`http://127.0.0.1:${worker.port}/__raccoon/${endpoint}`,{method,headers,signal:AbortSignal.timeout(5000)});
  if(!response.ok) throw Object.assign(new Error(`Worker control ${endpoint}: ${response.status}`),{status:response.status});
  return response.json();
}
async function waitHealth(worker) {
  for(let i=0;i<100;i++) {
    if(!alive(worker.pid)) throw new Error("Candidate worker exited");
    try {const r=await fetch(`http://127.0.0.1:${worker.port}/health`,{signal:AbortSignal.timeout(1000)});if(r.ok)return r.json();} catch{}
    await new Promise(resolve=>setTimeout(resolve,100));
  }
  throw new Error("Candidate health check timed out");
}
async function unusedPort() {
  const listener=net.createServer();await new Promise(resolve=>listener.listen(0,"127.0.0.1",resolve));
  const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));return port;
}
async function launch(entry,releaseId) {
  const port=await unusedPort();
  const out=fs.openSync(path.join(runtime,`worker-${releaseId || "legacy"}.stdout.log`),"a");
  const err=fs.openSync(path.join(runtime,`worker-${releaseId || "legacy"}.stderr.log`),"a");
  let child;
  try {child=spawn(process.execPath,[entry,"--http"],{cwd:root,windowsHide:true,detached:true,stdio:["ignore",out,err],env:{...process.env,RACCOON_PORT:String(port)}});} finally {fs.closeSync(out);fs.closeSync(err);}
  child.unref();
  const worker={pid:child.pid,port,entry,releaseId};
  try {worker.version=(await waitHealth(worker)).version;return worker;}
  catch(error){if(child.pid)child.kill();throw error;}
}
async function retire(worker) {
  if(alive(worker.pid)) process.kill(worker.pid);
}
async function legacyProbe(worker) {
  const client=new Client({name:"deployment-guard",version:"1"});
  const state={legacy:true};
  try {
    await client.connect(new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${worker.port}/mcp`),{requestInit:{headers}}));
    for(const [tool,key] of [["process_list","retainedProcessSessions"],["list_searches","retainedSearchSessions"],["db_query_sessions","databaseSessions"],["process_resource_watch_list","processWatches"]]) {
      const r=await client.callTool({name:tool,arguments:{}});if(r.isError)throw new Error(`Cannot verify ${tool}; refusing deployment`);
      const data=JSON.parse(r.content.find(x=>x.type==="text").text);
      const list=Array.isArray(data)?data:(data.sessions || data.processes || data.searches || data.watches);
      if(!Array.isArray(list))throw new Error(`Unknown state format: ${tool}`);state[key]=list.length;
    }
  }finally{await client.close();}
  if(fs.existsSync(path.join(config.pipelineDir,"state.sqlite"))) {
    const db=new DatabaseSync(path.join(config.pipelineDir,"state.sqlite"),{readOnly:true});
    try {state.activePipelines=db.prepare("SELECT data FROM runs").all().map(r=>JSON.parse(r.data)).filter(r=>["running","interrupted"].includes(r.status)).length;}finally{db.close();}
  }
  // Bootstrap only: old releases cannot report active HTTP handlers. Refuse while
  // any connection remains after our own inspection client has disconnected.
  if(process.platform === "win32") {
    const command=`@(Get-NetTCPConnection -LocalPort ${Number(worker.port)} -State Established -ErrorAction SilentlyContinue).Count`;
    const {stdout}=await promisify(execFile)("powershell.exe",["-NoProfile","-WindowStyle","Hidden","-Command",command],{windowsHide:true,timeout:10000});
    state.legacyConnections=Number(stdout.trim());
  } else throw new Error("Legacy bootstrap requires explicit active-request inspection on this platform");
  state.busy=Object.entries(state).some(([key,value])=>key!=="legacy" && Number(value)>0);
  return state;
}
let current=initial.current;
if(!alive(current.pid)) current=await launch(current.entry,current.releaseId);
await waitHealth(current);
const gateway=new ServiceGateway({current,token:config.token,persist,
  prepare:async({releaseId})=>{
    if(!/^[0-9a-f-]{36}$/.test(releaseId || ""))throw new Error("Invalid release id");
    const directory=path.join(runtime,"releases",releaseId);
    const release=JSON.parse(fs.readFileSync(path.join(directory,"release.json"),"utf8"));
    for(const [name,hash] of Object.entries(release.hashes)) if(createHash("sha256").update(fs.readFileSync(path.join(directory,name))).digest("hex")!==hash)throw new Error("Release changed after preparation");
    return launch(path.join(directory,"src/index.js"),releaseId);
  },
  probe:async worker=>{try{return await control(worker,"state");}catch(error){if(error.status===404)return legacyProbe(worker);throw error;}},
  activate:worker=>control(worker,"activate","POST"),
  release:async worker=>{try{await control(worker,"release","POST");}catch(error){if(error.status!==404)throw error;throw new Error("Legacy worker cannot hand off safely; keep it running until an agreed migration window");}},
  retire
});
gateway.server.listen(initial.port,"127.0.0.1",()=>{persist(gateway.current);console.error(`Raccoon gateway listening on 127.0.0.1:${initial.port}`);});
gateway.server.on("error",error=>{console.error(error.message);process.exit(1);});
setInterval(async()=>{
  if(gateway.updating || alive(gateway.current.pid))return;
  gateway.updating=true;gateway.switching=true;
  try{gateway.current=await launch(gateway.current.entry,gateway.current.releaseId);persist(gateway.current);}
  catch(error){console.error("Worker recovery failed:",error.message);}
  finally{gateway.updating=false;gateway.unlock();}
},2000).unref();
