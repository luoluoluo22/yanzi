const $=id=>document.getElementById(id);
let activeId=null,requestKey=null;const escaped=s=>String(s??'').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const state=s=>s==='completed'?'✓ 已通过':s==='needs_review'?'✕ 需检查':({starting_supervisor:'主管启动中',running_children:'子 Agent 执行中',delivering:'消息回流中',queued:'排队中'})[s]||s;
function render(r){
  const parent=r.sessions.supervisor||{};
  const children=(r.children||[]).map(c=>'<div class="child"><b>'+escaped(c.title)+'</b><div class="pill '+(c.status==='needs_review'?'bad':'')+'">'+escaped(c.status)+'</div><p>'+escaped(c.prompt)+'</p><pre>'+escaped(c.result||c.error||'等待中')+'</pre>'+(c.url?'<a target="_blank" rel="noopener" href="'+escaped(c.url)+'">打开子对话</a>':'')+'</div>').join('');
  const mailbox=(r.mailbox||[]).map(m=>'<div class="row">'+escaped(m.title)+' · '+escaped(m.id.slice(0,8))+' · '+escaped(m.status)+'</div>').join('');
  const events=(r.events||[]).slice(-8).map(e=>'<div class="muted">'+escaped(e.at.slice(11,19))+' · '+escaped(e.text)+'</div>').join('');
  return '<article class="run"><div class="run-top"><b>'+escaped(r.goal)+'</b><span class="pill '+(r.status==='needs_review'?'bad':'')+'">'+state(r.status)+'</span></div><p class="muted">'+escaped(r.id)+'</p><div class="row"><b>主管 Agent</b> · '+escaped(r.sessions.supervisor?'已创建':'未创建')+(parent.url?' · <a href="'+escaped(parent.url)+'" target="_blank" rel="noopener">打开主管对话</a>':'')+'</div><pre>'+escaped(r.plan||'等待主管生成子任务调用…')+'</pre><h3>子 Agent</h3><div class="children">'+children+'</div><h3>燕子消息中心</h3>'+mailbox+'<h3>主管最终汇总</h3><pre>'+escaped(r.parentSummary||r.error||'等待结果回流及主管汇总…')+'</pre><h3>事件日志</h3>'+events+'</article>';
}
async function api(path,init){const response=await fetch(path,{cache:'no-store',...init});const data=await response.json();if(!response.ok)throw Error(data.error||'HTTP '+response.status);return data;}
async function refresh(){
 try{
  const [health,runs]=await Promise.all([api('/health'),api('/api/runs')]);
  $('health').textContent=health.egoConnected?'● Ego Browser 已连接':'● Ego Browser 未连接';
  $('runs').innerHTML=runs.length?runs.map(render).join(''):'<p>尚无运行记录</p>';
 }catch(error){$('health').textContent='连接失败';$('message').textContent=error.message;}
}
$('launch').onsubmit=async event=>{
 event.preventDefault();const goal=$('goal').value.trim(),children=$('children').value.split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
 if(!goal||children.length<1||children.length>3){$('message').textContent='子任务必须为 1～3 项';return;}
 $('start').disabled=true;
 requestKey=requestKey||'agent-ui:'+crypto.randomUUID();
 try{
  const r=await api('/api/runs',{method:'POST',headers:{'Content-Type':'application/json','X-Yanzi-Request':'1'},body:JSON.stringify({requestKey,goal,children})});
  activeId=r.id;requestKey=null;$('message').textContent='已入队：'+r.id;await refresh();
 }catch(error){$('message').textContent=error.message+'；可刷新检查记录，避免重复提交';}
 finally{$('start').disabled=false;}
};
$('refresh').onclick=refresh;refresh();setInterval(refresh,5000);
