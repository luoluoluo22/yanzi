import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {copyFileSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {once} from 'node:events';
const repo=resolve(import.meta.dirname,'..','..','..');
const lab=join(repo,'.tmp','yanzi-iteration-lab');
const runner=join(repo,'tools','chatgpt-bridge','lab-iteration-runner.mjs');
const sha=s=>createHash('sha256').update(s).digest('hex').toUpperCase();

test('isolated two-round child iteration: fail, rollback, repair, pass; real fixture stays untouched',async t=>{
  mkdirSync(lab,{recursive:true});
  const sandbox=mkdtempSync(join(lab,'runner-selftest-'));
  t.after(()=>rmSync(sandbox,{recursive:true,force:true}));
  copyFileSync(join(lab,'title.mjs'),join(sandbox,'title.mjs'));
  writeFileSync(join(sandbox,'title.mjs'),'export function normalizeTitle(value) { return value; }\n');
  copyFileSync(join(lab,'title.test.mjs'),join(sandbox,'title.test.mjs'));
  writeFileSync(join(sandbox,'test-token.txt'),'mock-token');
  const initial=sha(readFileSync(join(sandbox,'title.mjs')));
  const originalMain=sha(readFileSync(join(lab,'title.mjs')));
  const testHash=sha(readFileSync(join(sandbox,'title.test.mjs')));
  let step=0;
  const proposal=(round)=>JSON.stringify({
    task:'title',expectedSha256:initial,
    source:round===1
      ? 'export function normalizeTitle(value) { return "BAD"; }'
      : 'export function normalizeTitle(value) { return typeof value === "string" ? value.trim().replace(/\\s+/g, " ") : ""; }'
  });
  const requests=[];
  const server=createServer(async(req,res)=>{
    let body='';
    for await(const chunk of req)body+=chunk.toString();
    let data={};try{data=body?JSON.parse(body):{}}catch{}
    requests.push({url:req.url,method:req.method,body:data});
    let result,status=200;
    if(req.url==='/health')result={connected:true,extensionVersion:'mock'};
    else if(req.url==='/api/subagents'&&req.method==='POST'){
      status=202;result={id:'lab-child-mock',initialJobId:'round-1',status:'starting'};
    }else if(req.url==='/api/subagents/lab-child-mock/messages'&&req.method==='POST'){
      step++;status=202;result={jobId:'round-2'};
    }else if(req.url==='/api/jobs/round-1')result={status:'success',data:{text:proposal(1)}};
    else if(req.url==='/api/jobs/round-2')result={status:'success',data:{text:proposal(2)}};
    else if(req.url==='/api/subagents/lab-child-mock')result={url:'https://chatgpt.com/c/mock'};
    else {status=404;result={error:'unrecognized_mock_route'};}
    res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(result));
  });
  server.listen(0,'127.0.0.1');
  await once(server,'listening');
  t.after(()=>server.close());
  const port=server.address().port;
  const child=spawn(process.execPath,[runner,'--task','title','--run','mock-two-rounds-001',
    '--max-rounds','2','--sandbox',sandbox,'--bridge-url','http://127.0.0.1:'+port,
    '--token-file',join(sandbox,'test-token.txt')],{cwd:repo,windowsHide:true});
  let out='',err='';
  child.stdout.on('data',chunk=>out+=chunk);
  child.stderr.on('data',chunk=>err+=chunk);
  const [code]=await once(child,'close');
  assert.equal(code,0,err+'\n'+out);
  const final=JSON.parse(out.trim());
  assert.equal(final.status,'verified_pass');
  assert.equal(final.rounds,2);
  const report=JSON.parse(readFileSync(join(sandbox,'results','iteration-mock-two-rounds-001.json')));
  assert.equal(report.rounds[0].verification.status,'failed_rolled_back');
  assert.equal(report.rounds[1].verification.status,'passed');
  assert.equal(report.rounds[1].verification.testPassed,true);
  assert.equal(sha(readFileSync(join(sandbox,'title.test.mjs'))),testHash);
  assert.notEqual(sha(readFileSync(join(sandbox,'title.mjs'))),initial);
  assert.equal(sha(readFileSync(join(lab,'title.mjs'))),originalMain);
  assert.equal(requests.filter(x=>x.method==='POST'&&x.url==='/api/subagents').length,1);
  assert.equal(requests.filter(x=>x.url==='/api/subagents/lab-child-mock/messages').length,1);
  assert.match(requests.find(x=>x.url==='/api/subagents/lab-child-mock/messages').body.prompt,/failed_rolled_back/);
});
