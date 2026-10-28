import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {copyFileSync,mkdirSync,mkdtempSync,readFileSync,writeFileSync,existsSync,rmSync} from 'node:fs';
import {once} from 'node:events';
import {join,resolve} from 'node:path';
const root=resolve(import.meta.dirname,'..','..','..');
const lab=join(root,'.tmp','yanzi-iteration-lab');
const runner=join(root,'tools','chatgpt-bridge','lab-iteration-runner.mjs');
const parentId='feab3200-1111-4444-8888-123456789abc';
const conv='ab123456-bb22-cc33-dd44-eeeeeeeeeeee';
const parentUrl='https://chatgpt.com/c/'+conv;
const childId='cb123456-bb22-cc33-dd44-eeeeeeeeeeee';
const childConv='cb234567-bb22-cc33-dd44-eeeeeeeeeeee';
const childUrl='https://chatgpt.com/c/'+childConv;
const hash=s=>createHash('sha256').update(s).digest('hex').toUpperCase();

async function scenario(t,decisions,{failBeforeSend=false}={}){
  mkdirSync(lab,{recursive:true});
  const sandbox=mkdtempSync(join(lab,'parent-integrated-'));
  t.after(()=>rmSync(sandbox,{recursive:true,force:true}));
  t.after(()=>rmSync(join(lab,'managed-parent-'+parentId+'.lock'),{force:true}));
  writeFileSync(join(sandbox,'title.mjs'),'export function normalizeTitle(value) { return value; }\n');
  copyFileSync(join(lab,'title.test.mjs'),join(sandbox,'title.test.mjs'));
  writeFileSync(join(sandbox,'token.txt'),'fake-token');
  const initial=hash(readFileSync(join(sandbox,'title.mjs')));
  const originalTest=hash(readFileSync(join(sandbox,'title.test.mjs')));
  const bad=JSON.stringify({task:'title',expectedSha256:initial,
    source:'export function normalizeTitle(value) { return "WRONG"; }'});
  const good=JSON.stringify({task:'title',expectedSha256:initial,
    source:'export function normalizeTitle(value) { return typeof value === "string" ? value.trim().replace(/\\s+/g," ") : ""; }'});
  const parentFeedback=[],childPrompts=[],calls=[];
  const server=createServer(async(req,res)=>{
    let body='';for await(const part of req)body+=part.toString();
    const data=body?JSON.parse(body):{};
    calls.push([req.method,req.url]);
    let status=200,answer=null;
    if(req.url==='/health')answer={connected:true,extensionVersion:'mock'};
    else if(req.url==='/api/managed-parents/'+parentId)
      answer={id:parentId,taskId:'managed-parent:'+parentId,status:'ready',url:parentUrl,conversationId:conv};
    else if(req.url==='/api/subagents'&&req.method==='POST'){
      childPrompts.push(data.prompt);
      assert.equal(data.parentManagedId,undefined);
      assert.equal(data.parentTaskId,'managed-parent:'+parentId);
      status=202;answer={id:childId,initialJobId:'child-1'};
    }else if(req.url==='/api/subagents/'+childId+'/messages'){
      childPrompts.push(data.prompt);status=202;answer={jobId:'child-2'};
    }else if(req.url==='/api/subagents/'+childId)
      answer={id:childId,status:'ready',url:childUrl,conversationId:childConv};
    else if(req.url==='/api/jobs/child-1')
      answer=failBeforeSend?{status:'error',message:'输入框已有其他草稿，请先处理草稿',data:null}:
        {status:'success',data:{text:bad}};
    else if(req.url==='/api/jobs/child-2')
      answer={status:'success',data:{text:good}};
    else if(req.url==='/api/managed-parents/'+parentId+'/verified-feedback'&&req.method==='POST'){
      assert.equal(data.childId,childId);
      assert.equal(data.childJobId,'child-'+data.round);
      assert.equal(data.task,'title');
      assert.equal(data.verification.sourceSha256Before,initial);
      parentFeedback.push({testPassed:data.verification.testPassed===true,
        testsUnchanged:data.verification.testsUnchanged===true});
      status=202;answer={jobId:'parent-'+parentFeedback.length};
    }else if(req.url?.startsWith('/api/jobs/parent-')){
      const n=Number(req.url.split('-').at(-1));
      answer={status:'success',data:{url:parentUrl,conversationId:conv,temporary:false,
        text:JSON.stringify({decision:decisions[n-1],reason:'parent-reviewed-'+n})}};
    }else {status=404;answer={error:'unsupported_mock_route'};}
    res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(answer));
  });
  server.listen(0,'127.0.0.1');await once(server,'listening');
  t.after(()=>server.close());
  const child=spawn(process.execPath,[runner,'--task','title','--run','parent-integrated-smoke',
    '--parent-managed-id',parentId,'--max-rounds','2',
    '--sandbox',sandbox,'--bridge-url','http://127.0.0.1:'+server.address().port,
    '--token-file',join(sandbox,'token.txt')],{cwd:root,windowsHide:true});
  let output='',error='';
  child.stdout.on('data',c=>output+=c);
  child.stderr.on('data',c=>error+=c);
  const [exit]=await once(child,'close');
  return {exit,output,error,sandbox,initial,originalTest,parentFeedback,childPrompts,calls,
    report:existsSync(join(sandbox,'results','iteration-parent-integrated-smoke.json')) ? JSON.parse(readFileSync(join(sandbox,'results','iteration-parent-integrated-smoke.json'),'utf8')):null};
}

test('real executor failure -> verified parent retry -> fixed patch -> verified parent accept',async t=>{
  const a=await scenario(t,['retry','accept']);
  assert.equal(a.exit,0,a.error+'\n'+a.output+'\n'+JSON.stringify(a.calls));
  assert.equal(a.report.status,'verified_pass');
  assert.equal(a.report.rounds.length,2);
  assert.equal(a.report.rounds[0].verification.status,'failed_rolled_back');
  assert.equal(a.report.rounds[1].verification.status,'passed');
  assert.equal(a.report.rounds[0].parentDecision.decision,'retry');
  assert.equal(a.report.rounds[1].parentDecision.decision,'accept');
  assert.equal(a.parentFeedback.length,2);
  assert.equal(a.parentFeedback[0].testPassed,false);
  assert.equal(a.parentFeedback[1].testPassed,true);
  assert.equal(a.parentFeedback[1].testsUnchanged,true);
  assert.equal(hash(readFileSync(join(a.sandbox,'title.test.mjs'))),a.originalTest);
  assert.notEqual(hash(readFileSync(join(a.sandbox,'title.mjs'))),a.initial);
  assert.ok(existsSync(join(a.sandbox,'results','managed-parent-integrated-smoke.lock')));
  assert.equal(existsSync(join(lab,'managed-parent-'+parentId+'.lock')),false);
});
test('parent cannot accept failed local tests; uncertain lease retained',async t=>{
  const a=await scenario(t,['accept']);
  assert.equal(a.exit,2);
  assert.equal(a.report.status,'stopped_needs_review');
  assert.match(a.report.reason,/accept_without_test_proof/);
  assert.equal(a.parentFeedback[0].testPassed,false);
  assert.equal(hash(readFileSync(join(a.sandbox,'title.mjs'))),a.initial);
  assert.ok(existsSync(join(lab,'managed-parent-'+parentId+'.lock')));
  rmSync(join(lab,'managed-parent-'+parentId+'.lock'),{force:true});
});

test('draft conflict stops before sending, frees parent lease, and preserves user draft',async t=>{
  const a=await scenario(t,[],{failBeforeSend:true});
  assert.equal(a.exit,2);
  assert.equal(a.report.status,'pre_submit_blocked');
  assert.equal(a.parentFeedback.length,0);
  assert.equal(a.report.rounds.length,1);
  assert.equal(a.report.rounds[0].submissionStatus,'error');
  assert.equal(a.report.rounds[0].networkDiagnosis.status,'draft_guard_blocked');
  assert.equal(a.report.rounds[0].networkDiagnosis.next,'stop_preserve_draft');
  assert.equal(hash(readFileSync(join(a.sandbox,'title.mjs'))),a.initial);
  assert.equal(existsSync(join(lab,'managed-parent-'+parentId+'.lock')),false);
  assert.equal(existsSync(join(a.sandbox,'results','managed-parent-integrated-smoke.lock')),true);
});
