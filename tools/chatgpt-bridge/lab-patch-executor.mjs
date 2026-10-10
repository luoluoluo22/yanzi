import { createHash } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, readFileSync, realpathSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'..','..');
const boundary=resolve(repo,'.tmp','yanzi-iteration-lab');
const TASKS=Object.freeze({
  title:['title.mjs','title.test.mjs','normalizeTitle'],
  duration:['duration.mjs','duration.test.mjs','formatDuration'],
  tags:['tags.mjs','tags.test.mjs','uniqueTags']
});
const sha=s=>createHash('sha256').update(s).digest('hex').toUpperCase();
const under=(base,path)=>{
  const rel=relative(base,path);
  return rel!== '..' && !rel.startsWith('..'+sep) && !isAbsolute(rel);
};
function sandboxDirectory(option) {
  const dir=resolve(option||boundary);
  if(!under(boundary,dir) || !existsSync(dir) ||
     !lstatSync(dir).isDirectory() || !under(realpathSync(boundary),realpathSync(dir)))
     throw new Error('sandbox_outside_isolated_lab');
  return dir;
}
function safeFile(dir,name){
  const file=join(dir,name);
  if(!existsSync(file) || !lstatSync(file).isFile() ||
     !under(realpathSync(dir),realpathSync(file)))throw new Error('invalid_fixture_file:'+name);
  return file;
}
function validateSource(source,fn){
  if(typeof source!=='string' || source.length<30 || source.length>2500)
    throw new Error('source_length_invalid');
  if(!new RegExp('^\\s*export\\s+function\\s+'+fn+'\\s*\\(\\s*\\w+\\s*\\)\\s*\\{[\\s\\S]*\\}\\s*$').test(source))
    throw new Error('source_expected_single_export');
  if(/\b(?:import|require|eval|process|globalThis|global|window|document|fetch|WebSocket|XMLHttpRequest|Worker|Buffer|setTimeout|setInterval|Atomics|Proxy|Reflect|constructor|prototype|__proto__|this|new|try|catch|while|do|throw|await|yield)\b/i.test(source))
    throw new Error('source_contains_disallowed_capability');
  if(/\bFunction\b/.test(source) || /\[\s*['"]|\u0060/.test(source))throw new Error('source_dynamic_property_access');
  const methods=new Set(['replace','replaceAll','trim','toLowerCase','toUpperCase','map','filter','includes','some','every','isArray','isInteger','floor','push','add','has','length','slice','match','test']);
  for(const match of source.matchAll(/\.\s*([A-Za-z_$][\w$]*)/g))
    if(!methods.has(match[1]))throw new Error('source_disallowed_property:'+match[1]);
  try{new Function(source.replace(/^\s*export\s+/,''));}
  catch{throw new Error('source_syntax_invalid');}
}
function testFixture(dir,filename){
  const env=Object.fromEntries(['PATH','Path','SystemRoot','windir','TEMP','TMP']
    .filter(key=>process.env[key]).map(key=>[key,process.env[key]]));
  const run=spawnSync(process.execPath,[
    '--permission','--allow-fs-read='+dir,
    '--max-old-space-size=64',safeFile(dir,filename)
  ],{cwd:dir,env,timeout:12000,maxBuffer:128*1024,windowsHide:true,encoding:'utf8',shell:false});
  return {pass:run.status===0 && !run.error,
    timeout:run.error?.code==='ETIMEDOUT',
    log:((run.stdout||'')+'\n'+(run.stderr||'')).slice(-4000)};
}
export function applyProposal(proposal,options={}){
  const dir=sandboxDirectory(options.sandbox);
  if(!proposal||typeof proposal!=='object'||Array.isArray(proposal))throw new Error('proposal_object_required');
  const config=TASKS[proposal.task];
  if(!config)throw new Error('task_not_allowlisted');
  if(typeof proposal.expectedSha256!=='string'||!/^[a-f0-9]{64}$/i.test(proposal.expectedSha256))
    throw new Error('expected_sha256_required');
  validateSource(proposal.source,config[2]);
  const file=safeFile(dir,config[0]),test=safeFile(dir,config[1]);
  const previous=readFileSync(file), before=sha(previous), testHash=sha(readFileSync(test));
  if(before!==proposal.expectedSha256.toUpperCase())throw new Error('source_version_conflict');
  const baseline=testFixture(dir,config[1]);
  if(baseline.pass)throw new Error('fixture_already_passing');
  const tmp=file+'.yanzi-staging',bak=file+'.yanzi-rollback';
  if(existsSync(tmp)||existsSync(bak))throw new Error('unfinished_patch_transaction');
  let committed=false,result;
  try{
    writeFileSync(bak,previous,{flag:'wx'});
    writeFileSync(tmp,proposal.source,{flag:'wx'});
    renameSync(tmp,file);
    const validation=testFixture(dir,config[1]);
    const testsUnchanged=sha(readFileSync(test))===testHash;
    committed=validation.pass && testsUnchanged;
    result={
      task:proposal.task,status:committed?'passed':'failed_rolled_back',
      baselinePassed:baseline.pass,testPassed:validation.pass,
      testsUnchanged, sourceSha256Before:before,
      sourceSha256After:committed?sha(readFileSync(file)):before,
      rollback:!committed,testLog:validation.log
    };
  }finally{
    if(!committed && existsSync(bak))renameSync(bak,file);
    if(existsSync(tmp))rmSync(tmp,{force:true});
    if(committed && existsSync(bak))rmSync(bak,{force:true});
  }
  if(options.log!==false){
    const folder=join(dir,'results');mkdirSync(folder,{recursive:true});
    writeFileSync(join(folder,proposal.task+'-latest.json'),JSON.stringify(result,null,2),'utf8');
  }
  return result;
}
if(process.argv[1] && realpathSync(process.argv[1])===fileURLToPath(import.meta.url)){
  const [a,input,b,dir]=process.argv.slice(2);
  if(a!=='--input'||!input||(b && b!=='--sandbox'))throw new Error('usage --input file [--sandbox directory]');
  const content=readFileSync(resolve(input),'utf8');
  if(content.length>4000)throw new Error('proposal_too_large');
  try {
    console.log(JSON.stringify(applyProposal(JSON.parse(content),{sandbox:dir}),null,2));
  } catch(error) {
    console.log(JSON.stringify({status:'rejected',reason:error.message,rollback:false},null,2));
    process.exitCode=2;
  }
}
