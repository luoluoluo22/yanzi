import http from 'node:http';
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const root=dirname(fileURLToPath(import.meta.url));
const storage=join(process.env.LOCALAPPDATA||'', 'OpenQuickHost','ExtensionStorage','ego-agent-team');
mkdirSync(storage,{recursive:true});
const stateFile=join(storage,'runs.json');
const egoToken=join(process.env.LOCALAPPDATA||'','OpenQuickHost','ExtensionStorage','ego-browser-api','api-token.txt');
const host='127.0.0.1',port=43830,origin='http://'+host+':'+port;
const egoBase='http://127.0.0.1:43820';
let runs=[];
try {runs=JSON.parse(readFileSync(stateFile,'utf8'));if(!Array.isArray(runs))runs=[];}catch{}
for(const r of runs)if(!['completed','needs_review'].includes(r.status)){r.status='needs_review';r.error='进程重启：状态未知，禁止自动重发网页消息';}
const save=()=>{writeFileSync(stateFile,JSON.stringify(runs.slice(-50),null,2),'utf8');};
save();
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const log=(r,text)=>{r.events.push({at:new Date().toISOString(),text});if(r.events.length>100)r.events.shift();save();};
async function fetchEgo(path,options={}){
  if(!existsSync(egoToken))throw Error('未找到 Ego Browser API 本机令牌');
  const token=readFileSync(egoToken,'utf8').trim();
  const response=await fetch(egoBase+path,{...options,headers:{Authorization:'Bearer '+token,...options.headers},signal:AbortSignal.timeout(20000)});
  let body;try{body=await response.json();}catch{throw Error('Ego API 返回非 JSON');}
  if(!response.ok||body.ok===false)throw Error('Ego API '+response.status+': '+(body.error||JSON.stringify(body)));
  return body;
}
async function action(r,type,sessionId=null,text=null){
  const payload={action:type};
  if(sessionId)payload.sessionId=sessionId;
  if(text!==null)payload.text=text;
  if(type==='send_prompt')payload.confirm=true;
  const result=await fetchEgo('/api/sites/chatgpt/actions',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
  const started=Date.now();let job;
  for(;;){
    await sleep(850);
    job=(await fetchEgo('/api/jobs/'+result.job.id)).job;
    if(!['queued','running'].includes(job.status))break;
    if(Date.now()-started>90000)throw Error('Ego 操作超时；任务ID '+result.job.id+'，禁止盲目重试');
  }
  if(job.status!=='success')throw Error('Ego '+type+' 未成功：'+job.status+' '+(job.message||''));
  return {sessionId:result.session?.id||sessionId,data:job.data,jobId:job.id};
}
function extractReply(text){
  if(typeof text!=='string'||!text.includes('ChatGPT 说：'))return null;
  let reply=text.slice(text.lastIndexOf('ChatGPT 说：')+'ChatGPT 说：'.length);
  reply=reply.split('ChatGPT 可能会出错')[0].split('最新一条回复')[0].trim();
  if(!reply||reply.includes('正在回应')||reply.includes('思考中'))return null;
  return reply;
}
async function waitReply(r,sessionId,before='',timeoutMs=220000){
  const until=Date.now()+timeoutMs;
  while(Date.now()<until){
    const reading=await action(r,'read_page',sessionId);
    const found=extractReply(reading.data?.text);
    if(found && found!==before)return {text:found,url:reading.data.url};
    await sleep(4500);
  }
  throw Error('等待 ChatGPT 新回复超时；未自动重发，保留会话供核验');
}
async function open(r,who){
  const opened=await action(r,'open');
  r.sessions[who]={id:opened.sessionId,url:opened.data?.url||null};
  log(r,who+' 已创建独立 Ego 浏览器会话');
  return opened.sessionId;
}
async function sendWait(r,sessionId,text,before=''){
  const sent=await action(r,'send_prompt',sessionId,text);
  log(r,'Ego 已提交消息，任务 '+sent.jobId+'；等待 ChatGPT 实际回复');
  return waitReply(r,sessionId,before);
}
function makePrompt(r){
  return ['你是燕子 Agent 团队的主管，所有子任务由燕子小程序实际创建，不要虚构子任务结果。',
    '团队总目标：'+r.goal,'请通过以下协议申请两个子 Agent（数量为 '+r.seeds.length+'，不要多也不要少）：',
    ...r.seeds.map((v,i)=>'第 '+(i+1)+' 个子任务：'+v),
    '本轮请严格只输出 '+r.seeds.length+' 行指令，每行格式：YANZI_SPAWN_CHILD:具体子任务内容。',
    '不要计算、不要执行子任务，也不要提前总结。',
    '燕子会把每个子 Agent 真实结果整理、去重、持久化，再以消息回传给你。'].join('\n');
}
function directives(text){return String(text).split(/\r?\n/).map(x=>x.match(/^\s*(?:[-*]\s*)?YANZI_SPAWN_CHILD\s*[:：]\s*(.+)$/i)?.[1]?.trim()).filter(Boolean);}
async function runTeam(r){
  try{
    r.status='starting_supervisor';save();
    const sid=await open(r,'supervisor');
    const plan=await sendWait(r,sid,makePrompt(r));
    r.plan=plan.text;r.sessions.supervisor.url=plan.url;
    const tasks=directives(plan.text);
    if(tasks.length!==r.seeds.length || tasks.some(v=>v.length<4||v.length>2000))
      throw Error('主管未按协议发出 '+r.seeds.length+' 条 YANZI_SPAWN_CHILD 指令；拒绝伪造子 Agent');
    log(r,'主管真实回复 '+tasks.length+' 条创建指令，燕子开始派生子 Agent');
    r.status='running_children';save();
    for(let i=0;i<tasks.length;i++){
      const c={id:randomUUID(),title:'子 Agent '+(i+1),prompt:tasks[i],status:'running',result:null,sessionId:null};
      r.children.push(c);save();
      try{
        const id=await open(r,'child_'+i);c.sessionId=id;
        const result=await sendWait(r,id,'请完成子任务。'+tasks[i]+' 请只输出答案，不要解释。');
        c.status='success';c.result=result.text;c.url=result.url;
        log(r,c.title+' 实际完成，结果已写入燕子消息存储');
      }catch(error){c.status='needs_review';c.error=error.message;log(r,c.title+' 执行失败：'+error.message);}
      r.mailbox.push({id:randomUUID(),childId:c.id,title:c.title,status:'pending',result:c.result,error:c.error||null});
      save();
    }
    r.status='delivering';save();
    const message=r.mailbox.map(m=>'[燕子子 Agent 结果回传]\n消息ID：'+m.id+'\n任务：'+m.title+'\n状态：'+(m.error?'失败':'完成')+'\n结果：'+(m.result||'无')+'\n错误：'+(m.error||'无')).join('\n\n');
    const final=await sendWait(r,sid,'来自燕子消息中心的实际回传（请勿虚构）：\n'+message+
      '\n\n请逐项核对结果后汇总。只有成功的子任务才能标记成功；失败项必须列出来。',plan.text);
    r.parentSummary=final.text;r.sessions.supervisor.url=final.url;
    r.mailbox.forEach(m=>{m.status='delivered';m.deliveredAt=new Date().toISOString();});
    r.status=r.children.every(c=>c.status==='success')?'completed':'needs_review';
    log(r,'主管已真实接收燕子消息并回复汇总；团队状态 '+r.status);
  }catch(error){r.status='needs_review';r.error=error.message;log(r,'需要检查：'+error.message);}
  save();
}
let busy=false;
const pending=[];
async function pump(){
 if(busy)return;busy=true;
 while(pending.length){const r=pending.shift();await runTeam(r);}
 busy=false;
}
function response(res,status,data){res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(data));}
async function readBody(req){let text='';for await(const part of req){text+=part;if(text.length>24000)throw Error('请求过大');}return JSON.parse(text);}
const files={'/':'index.html','/app.js':'app.js','/style.css':'style.css'};
http.createServer(async(req,res)=>{
 const path=new URL(req.url,origin).pathname;
 if(req.headers.host!==host+':'+port||(req.headers.origin&&req.headers.origin!==origin))return response(res,403,{error:'origin_not_allowed'});
 res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'");
 if(req.method==='GET'&&files[path]){
   const name=files[path],bytes=readFileSync(join(root,'public',name));
   res.writeHead(200,{'Content-Type':name.endsWith('.js')?'text/javascript; charset=utf-8':name.endsWith('.css')?'text/css; charset=utf-8':'text/html; charset=utf-8','Cache-Control':'no-store'});return res.end(bytes);
 }
 try{
 if(req.method==='GET'&&path==='/health'){let connected=false;try{connected=(await fetchEgo('/health')).ok===true;}catch{}return response(res,200,{ok:true,egoConnected:connected,queued:pending.length,active:busy});}
 if(req.method==='GET'&&path==='/api/runs')return response(res,200,runs.slice(-20).reverse());
 const match=path.match(/^\/api\/runs\/([0-9a-f-]{36})$/i);
 if(req.method==='GET'&&match){const found=runs.find(r=>r.id===match[1]);return response(res,found?200:404,found||{error:'not_found'});}
 if(req.method==='POST'&&path==='/api/runs'){
   if(req.headers['x-yanzi-request']!=='1')return response(res,403,{error:'missing_request_header'});
   const b=await readBody(req);
   if(!/^[A-Za-z0-9_.:-]{8,100}$/.test(b.requestKey||''))throw Error('请求标识不合法');
   if(typeof b.goal!=='string'||!b.goal.trim()||b.goal.length>3000)throw Error('总任务无效');
   if(!Array.isArray(b.children)||b.children.length<1||b.children.length>3||b.children.some(s=>typeof s!=='string'||!s.trim()||s.length>1500))throw Error('必须提供 1～3 个子任务');
   const previous=runs.find(r=>r.requestKey===b.requestKey);
   if(previous)return response(res,200,previous);
   if(pending.length>=3)throw Error('任务队列已满');
   const r={id:randomUUID(),requestKey:b.requestKey,goal:b.goal.trim(),seeds:b.children.map(s=>s.trim()),status:'queued',createdAt:new Date().toISOString(),sessions:{},children:[],mailbox:[],events:[],plan:null,parentSummary:null,error:null};
   runs.push(r);save();pending.push(r);void pump();return response(res,202,r);
 }
 return response(res,404,{error:'not_found'});
 }catch(error){return response(res,400,{error:error.message});}
}).listen(port,host,()=>console.log('yanzi ego-agent-team on '+origin));
