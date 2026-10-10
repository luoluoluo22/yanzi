import fs from 'node:fs/promises';
import path from 'node:path';
import {config} from '../src/config.js';

const directory=path.resolve(process.env.RACCOON_RUNTIME_DIR || '.raccoon-runtime');
const currentFile=path.join(directory,'connection-status.json');
async function health(url) {
  try {
    const response=await fetch(url,{signal:AbortSignal.timeout(5000)});
    if(!response.ok)return {ok:false,status:response.status};
    const data=await response.json();
    return {ok:data.ok===true && data.name==='raccoon-mcp',status:response.status};
  } catch {return {ok:false,status:'unreachable'};}
}
async function pid(name) {
  try {return JSON.parse((await fs.readFile(path.join(directory,name),'utf8')).replace(/^\uFEFF/, '')).pid;} catch {return null;}
}
const [local, remote, servicePid, tunnelPid]=await Promise.all([
  health(`http://127.0.0.1:${config.port}/health`),
  config.publicUrl ? health(new URL('/health',config.publicUrl)) : Promise.resolve({ok:null,status:'not-configured'}),
  pid('service.json'),pid('cloudflare-process.json')
]);
const snapshot={at:new Date().toISOString(),local,public:remote,servicePid,tunnelPid};
await fs.mkdir(directory,{recursive:true});
let previous;
try {previous=JSON.parse(await fs.readFile(currentFile,'utf8'));}catch {}
const signature=value=>JSON.stringify({...value,at:undefined});
if(!previous || signature(previous)!==signature(snapshot)) {
  await fs.appendFile(path.join(directory,'connection-history.jsonl'),JSON.stringify(snapshot)+'\n');
}
const temporary=currentFile+`.${process.pid}.tmp`;
await fs.writeFile(temporary,JSON.stringify(snapshot,null,2));
await fs.rename(temporary,currentFile);
console.log(JSON.stringify(snapshot));
