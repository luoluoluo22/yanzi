async function run(context) {
  const account = context.mobile.getAccountId();
  const cacheKey = 'quick-notes.v1.' + (account || 'local');
  let state;
  try { state = JSON.parse(localStorage.getItem(cacheKey) || '{}'); } catch (_) { state = {}; }
  state.notes = state.notes || [];
  state.revision = state.revision || 0;
  state.generation = state.generation || 0;
  let selected = '', busy = false;
  document.head.insertAdjacentHTML('beforeend', `<meta name="viewport" content="width=device-width,initial-scale=1"><style>
    *{box-sizing:border-box}body{margin:0;padding:22px;background:#f7f9fc;color:#192638;font:16px system-ui}
    h1{font-size:27px;margin:0 0 8px}button{background:#236de8;color:white;border:0;border-radius:10px;padding:12px;margin:6px 6px 6px 0;font-size:16px}
    input,textarea{width:100%;font:16px system-ui;border:1px solid #d4deec;border-radius:10px;padding:12px;margin:6px 0;background:white}
    textarea{min-height:220px;resize:vertical}.note{display:block;width:100%;text-align:left;background:white;color:#192638;border:1px solid #d4deec}
    #status{font-size:14px;color:#53677e;min-height:40px}#editor{display:none}#list{margin-top:16px}p{white-space:pre-wrap}</style>`);
  document.body.insertAdjacentHTML('afterbegin', `<h1>便签</h1><div id="status">本地便签</div>
    <button id="add">新建</button><button id="sync">同步</button><input id="search" placeholder="搜索便签">
    <div id="editor"><input id="title" placeholder="标题"><textarea id="body" placeholder="记下想法…"></textarea>
    <button id="save">保存</button><button id="cancel">返回列表</button><button id="delete">删除</button></div><div id="list"></div>`);
  const el = id => document.getElementById(id), message = text => el('status').textContent = text;
  const persist = () => { localStorage.setItem(cacheKey, JSON.stringify(state)); };
  const draftKey = cacheKey + '.draft';
  const draft = () => localStorage.setItem(draftKey, JSON.stringify({id:selected,title:el('title').value,body:el('body').value}));
  function render() {
    el('list').replaceChildren();
    const query = el('search').value.toLowerCase();
    for (const note of state.notes.filter(n => (n.title+' '+n.body).toLowerCase().includes(query)).sort((a,b) => b.updatedAt.localeCompare(a.updatedAt))) {
      const button = document.createElement('button'); button.className = 'note';
      button.textContent = (note.title || '无标题') + '\n' + note.body.slice(0,75);
      button.onclick = () => open(note); el('list').append(button);
    }
    if (!el('list').children.length) el('list').textContent = '还没有便签，点击「新建」开始。';
  }
  function open(note) {
    selected = note.id; el('title').value = note.title; el('body').value = note.body;
    el('editor').style.display = 'block'; el('list').style.display = 'none'; draft();
  }
  function close() { el('editor').style.display = 'none'; el('list').style.display = 'block'; render(); }
  async function sync() {
    if (busy) return;
    if (!account) { message('未登录：便签保存在此手机，登录后可使用账号便签'); return; }
    if (context.mobile.getAccountId() !== account) { message('账号已切换，请关闭并重新打开便签'); return; }
    busy = true; message('同步中…');
    try {
      const read = await context.storage.readText('notes.v1.json');
      if (!read.ok) throw new Error('账号暂不可用');
      if (state.dirty) {
        if (read.revision !== state.revision) {
          if (read.exists && JSON.stringify(JSON.parse(read.content).notes) === JSON.stringify(state.notes)) {
            state.revision=read.revision; state.dirty=false; persist(); message('已同步'); return;
          }
          message('其他设备已有新版本，本地便签已保留。可导出后再使用云端版本。');
          el('resolve').style.display = 'block'; return;
        }
        const sentGeneration=state.generation;
        const write = await context.storage.writeText('notes.v1.json', JSON.stringify({schemaVersion:1,notes:state.notes}), state.revision);
        if (!write.ok) throw new Error(write.conflict ? '同步冲突，本地便签已保留' : '同步失败，本地便签已保留');
        state.revision = write.revision; state.dirty = state.generation !== sentGeneration;
      } else if (read.exists) {
        const remote = JSON.parse(read.content);
        if (remote.schemaVersion !== 1 || !Array.isArray(remote.notes)) throw new Error('数据格式不支持，未覆盖本地便签');
        state.notes = remote.notes; state.revision = read.revision;
      } else { state.notes = []; state.revision = read.revision; }
      persist(); render(); message(state.dirty ? '已保存新修改，等待继续同步' : '已同步 · '+new Date().toLocaleTimeString());
      if (state.dirty) setTimeout(sync, 500);
    } catch (error) { message(error.message+'；可继续本地编辑'); }
    finally { busy = false; }
  }
  const resolve = document.createElement('div'); resolve.id='resolve'; resolve.style.display='none';
  const exportButton = document.createElement('button'); exportButton.textContent='导出本地便签到剪贴板';
  exportButton.onclick = async () => { await context.mobile.setClipboardText(JSON.stringify(state.notes,null,2)); message('已复制本地便签'); };
  const cloudButton = document.createElement('button'); cloudButton.textContent='备份本地并使用云端';
  cloudButton.onclick = () => { localStorage.setItem(cacheKey+'.conflict-backup.'+Date.now(),JSON.stringify(state)); state.dirty=false; persist(); resolve.style.display='none'; sync(); };
  resolve.append(exportButton,cloudButton); el('status').after(resolve);
  el('add').onclick = () => open({id:'note-'+Date.now()+'-'+Math.random().toString(16).slice(2),title:'',body:''});
  el('search').oninput=render; el('sync').onclick=sync;
  el('title').oninput=draft; el('body').oninput=draft;
  el('save').onclick = () => {
    if (!el('title').value.trim() && !el('body').value.trim()) { message('请填写标题或内容'); return; }
    const note={id:selected,title:el('title').value.trim(),body:el('body').value,updatedAt:new Date().toISOString()};
    state.notes=state.notes.filter(n=>n.id!==selected); state.notes.unshift(note); state.dirty=true; state.generation++;
    try { persist(); localStorage.removeItem(draftKey); close(); message('已保存到手机，等待同步'); sync(); }
    catch (_) { message('本地存储不足，请先复制内容'); }
  };
  el('cancel').onclick=close;
  el('delete').onclick=()=>{if (!confirm('删除这条便签？'))return; state.notes=state.notes.filter(n=>n.id!==selected);state.dirty=true;state.generation++;persist();localStorage.removeItem(draftKey);close();sync();};
  render();
  try { const recovered=JSON.parse(localStorage.getItem(draftKey)||'null'); if(recovered) { open(recovered);message('已恢复未保存的草稿'); } } catch (_) {}
  await sync();
  // Keep a visible mobile-view alive until the user closes its native container.
  await new Promise(()=>{});
}
