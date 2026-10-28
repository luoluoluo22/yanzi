import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {copyFileSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {once} from 'node:events';
const root=resolve(import.meta.dirname,'..','..','..');
const lab=join(root,'.tmp','yanzi-iteration-lab');
const script=join(root,'tools','chatgpt-bridge','lab-iteration-runner.mjs');
const sha=buf=>createHash('sha256').update(buf).digest('hex').toUpperCase();

test('temporary chat: real sandbox patch rolls back, timeout reconciles, model fixes second round',async t=>{
  mkdirSync(lab,{recursive:true});
  const dir=mkdtempSync(join(lab,'temp-iteration-unit-'));
  t.after(()=>rmSync(dir,{recursive:true,force:true}));
  writeFileSync(join(dir,'title.mjs'),'export function normalizeTitle(value) { return value; }\n');
  copyFileSync(join(lab,'title.test.mjs'),join(dir,'title.test.mjs'));
  writeFileSync(join(dir,'test-token.txt'),'mock-local-token');
  const beforeHash=sha(readFileSync(join(dir,'title.mjs')));
  const originalMain=sha(readFileSync(join(lab,'title.mjs')));
  const proofTest=sha(readFileSync(join(dir,'title.test.mjs')));
  const wrong=JSON.stringify({task:'title',expectedSha256:beforeHash,
    source:'export function normalizeTitle(value) { return "WRONG"; }'});
  const good=JSON.stringify({task:'title',expectedSha256:beforeHash,
    source:'export function normalizeTitle(value) { return typeof value === "string" ? value.trim().replace(/\\s+/g, " ") : ""; }'});
  const sendPrompts=[];
  const server=createServer(async(req,res)=>{
    let raw='';for await(const part of req)raw+=part.toString();
    const body=raw?JSON.parse(raw):{};
    let status=200,answer={error:'unknown_mock_endpoint'};
    if(req.url==='/health')answer={connected:true,extensionVersion:'fake'};
    else if(req.url==='/api/jobs'&&req.method==='POST'){
      if(body.action==='chatgpt_diagnostics')answer={id:'diagnostics'};
      else if(body.action==='chatgpt_messages')answer={id:'messages'};
      else if(body.action==='chatgpt_send'){
        sendPrompts.push(body.prompt);
        answer={id:'submission-'+sendPrompts.length};
      }
    }else if(req.url==='/api/jobs/diagnostics')
      answer={status:'success',data:{dom:{composerExists:true,draftPresent:false,
        generating:false,challenge:false,loginRequired:false}}};
    else if(req.url==='/api/jobs/submission-1')
      answer={status:'error',message:'reply timed out but may have arrived',data:null};
    else if(req.url==='/api/jobs/submission-2')
      answer={status:'success',data:{text:good}};
    else if(req.url==='/api/jobs/messages')
      answer={status:'success',data:{messages:[
        {role:'user',text:sendPrompts[0]},
        {role:'assistant',text:wrong}
      ]}};
    else status=404;
    res.writeHead(status,{'Content-Type':'application/json'});
    res.end(JSON.stringify(answer));
  });
  server.listen(0,'127.0.0.1');
  await once(server,'listening');
  t.after(()=>server.close());
  const child=spawn(process.execPath,[script,'--task','title','--run','temp-recovery-test',
    '--temporary-tab-id','1001','--max-rounds','2','--sandbox',dir,
    '--bridge-url','http://127.0.0.1:'+server.address().port,
    '--token-file',join(dir,'test-token.txt')],{cwd:root,windowsHide:true});
  let output='',error='';
  child.stdout.on('data',part=>output+=part);
  child.stderr.on('data',part=>error+=part);
  const [exit]=await once(child,'close');
  assert.equal(exit,0,error+'\n'+output);
  const state=JSON.parse(output);
  assert.equal(state.status,'verified_pass');
  const report=JSON.parse(readFileSync(join(dir,'results','iteration-temp-recovery-test.json')));
  assert.equal(report.rounds.length,2);
  assert.equal(report.rounds[0].reconciledAfterTimeout,true);
  assert.equal(report.rounds[0].verification.status,'failed_rolled_back');
  assert.equal(report.rounds[1].verification.status,'passed');
  assert.equal(sendPrompts.length,2);
  assert.match(sendPrompts[1],/failed_rolled_back/);
  assert.equal(sha(readFileSync(join(lab,'title.mjs'))),originalMain);
  assert.equal(sha(readFileSync(join(dir,'title.test.mjs'))),proofTest);
  assert.notEqual(sha(readFileSync(join(dir,'title.mjs'))),beforeHash);
});
