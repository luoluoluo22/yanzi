/* Shared agent console UI. The browser extension sends requests over the
   authenticated local Bridge WebSocket; no API credentials enter this page. */
(() => {
  const $=id=>document.getElementById(id);
  const form=$('agent-launch-form');
  if(!form)return;
  const goal=$('agent-goal');
  const children=$('agent-children');
  const feedback=$('agent-feedback');
  const connection=$('agent-connection');
  const launch=$('agent-launch');
  const results=$('agent-results');
  const state=$('agent-run-state');
  let activeId=null;
  let busy=false;

  async function invoke(action,payload={}) {
    const response=await chrome.runtime.sendMessage({type:'yanzi_agent_console',action,payload});
    if(!response?.ok)throw new Error(response?.error||'未收到燕子 Bridge 响应');
    return response.result;
  }
  function badge(node,status,text) {
    node.className='agent-console-led'+(status==='ready'?' ready':status==='needs_review'?' error':'');
    node.textContent=text||status;
  }
  function button(label,url) {
    const node=document.createElement('button');
    node.type='button';node.textContent=label;
    const valid=typeof url==='string' && /^https:\/\/chatgpt\.com\/c\/[A-Za-z0-9_-]{8,120}\/?$/.test(url);
    node.disabled=!valid;
    node.onclick=()=>{if(valid)chrome.tabs.create({url});};
    return node;
  }
  function entry(title,status,description,url) {
    const section=document.createElement('section');section.className='agent-run-entry';
    const head=document.createElement('header');
    const titleNode=document.createElement('strong');titleNode.textContent=title;
    const label=document.createElement('span');label.className='agent-run-status '+status;
    const translations={ready:'已回复',starting:'启动中',working:'执行中',processing:'回传处理中',
      success:'已回传',needs_review:'需人工检查',queued:'排队中',verified_late:'已核验送达'};
    label.textContent=translations[status]||status||'等待中';
    head.append(titleNode,label);section.append(head);
    if(description){const p=document.createElement('p');p.textContent=String(description).slice(0,500);section.append(p);}
    if(url)section.append(button('打开真实对话 ↗',url));
    return section;
  }
  function render(run) {
    activeId=run.id;
    const mapping={running:'执行中',completed:'已完成',needs_review:'需人工检查'};
    badge(state,run.status,mapping[run.status]||run.status);
    results.replaceChildren();
    const intro=document.createElement('div');
    intro.textContent=run.goal||'未命名协作任务';
    intro.style.color='#dae7f7';
    results.append(intro);
    const parent=run.parent;
    if(parent)results.append(entry('主管 Agent',parent.status,
      parent.lastResponse?.text||'等待主管建立正式对话',parent.url));
    for(const child of run.children||[]) {
      const delivery=parent?.deliveries?.find(d=>d.subagentId===child.id);
      const status=['success','verified_late'].includes(delivery?.status)?'success':child.status;
      results.append(entry(child.title||'子 Agent',status,
        child.lastResult?.text||delivery?.error||'等待结果',child.url));
    }
  }
  function setBusy(value) {
    busy=value;launch.disabled=value;launch.textContent=value?'正在创建任务…':'启动多 Agent';
  }
  async function refresh() {
    try {
      const data=activeId?await invoke('detail',{id:activeId}):await invoke('list');
      badge(connection,'ready','Bridge 已连接');
      const run=Array.isArray(data)?data[0]:data;
      if(run)render(run);
      else {badge(state,'idle','暂无任务');results.textContent='输入任务与子任务，一键创建独立对话并自动回流。';}
    } catch(error) {
      badge(connection,'needs_review','Bridge 未连接');
      feedback.textContent=error.message;
    }
  }
  form.addEventListener('submit',async event=>{
    event.preventDefault();
    if(busy)return;
    const task=goal.value.trim();
    const subtasks=children.value.split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
    if(!task||subtasks.length<1||subtasks.length>3) {
      feedback.textContent='请填写总任务，并提供 1～3 行非空子任务。';
      return;
    }
    setBusy(true);
    try {
      const run=await invoke('launch',{requestKey:'agent-ui:'+crypto.randomUUID(),
        goal:task,children:subtasks});
      activeId=run.id;
      await chrome.storage.local.set({yanziAgentLastRunId:activeId});
      feedback.textContent='已提交，关闭弹窗也会继续执行。结果将自动回传主管对话。';
      badge(connection,'ready','Bridge 已连接');
      render(run);
    }catch(error) {
      feedback.textContent=error.message+'；如果不确定是否提交，请先刷新任务记录。';
    }finally{setBusy(false);}
  });
  $('agent-demo').onclick=()=>{
    goal.value='测试燕子多 Agent 自动回流：主管接收两个独立子对话的结果并汇总。';
    children.value='计算 5+7，只回复 CHILD_SUM_12\n把“蓝色”翻译成英文，只回复 CHILD_COLOR_BLUE';
    goal.focus();
  };
  $('agent-refresh').onclick=()=>refresh();
  chrome.storage.local.get({yanziAgentLastRunId:null}).then(data=>{
    activeId=data.yanziAgentLastRunId;
    return refresh();
  });
  setInterval(refresh,4000);
})();
