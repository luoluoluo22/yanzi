// Public, free self-service access for pre-1.0 users who cannot install a VIP-only updater.
// All accounts are verified by /v1/auth/login; passwords and bearer tokens stay only in page memory.
export const upgradePageHtml = `<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>燕子 · VIP 云服务与旧数据导出</title>
<style>body{margin:0;padding:45px 20px;background:#121820;color:#edf3f9;font:15px system-ui,-apple-system,'Microsoft YaHei',sans-serif}main{max-width:680px;margin:auto}h1{font-size:26px}p{line-height:1.75;color:#b5c4d6}section{background:#1b2633;border:1px solid #34475b;border-radius:16px;padding:22px;margin:20px 0}label{display:block;margin:12px 0 5px;color:#dbe5ef}input{font:inherit;box-sizing:border-box;width:100%;background:#111a26;color:#eef4ff;border:1px solid #485b70;padding:12px;border-radius:10px}button,.buy{display:inline-block;border:0;cursor:pointer;text-align:center;font:inherit;color:#fff;background:#376ff0;padding:11px 18px;margin:12px 8px 0 0;border-radius:10px;text-decoration:none}button:disabled{opacity:.5;cursor:wait}.secondary{background:#425169}small{color:#96aabf}#notice{white-space:pre-wrap;color:#dce9f9;line-height:1.65}</style>
</head><body><main><h1>燕子 VIP 云服务</h1>
<p>Windows 本地基础功能和已拥有的版本继续可用。云同步、跨设备消息、云端存储等服务，需要有效 VIP 会员。旧版客户端无需升级也能通过本页面购买、激活会员并导出既有云同步对象。</p>
<a class="buy" href="https://wzyp.cn/shop/4AOUCE2B" target="_blank" rel="noopener noreferrer">查看 VIP 升级选项</a>
<section><h2>登录燕子账户</h2><label for="email">账户邮箱</label><input id="email" type="email" maxlength="200" autocomplete="username" required>
<label for="password">账户密码</label><input id="password" type="password" maxlength="128" autocomplete="current-password" required>
<button id="signin" type="button">登录并检查 VIP</button><button id="logout" class="secondary" type="button" disabled>退出本页登录</button>
<p><small>仅连接燕子官方云端接口。不会在浏览器存储密码、令牌或同步文件；刷新页面即退出。</small></p></section>
<section><h2>激活或续费</h2><label for="license">购买后获得的 VIP 卡密</label><input id="license" type="text" autocomplete="off" maxlength="128">
<button id="redeem" type="button" disabled>激活 VIP</button></section>
<section><h2>取回历史云数据</h2><p>所有已登录用户（包括会员到期用户）均可免费导出历史云同步对象 JSON。当前不包括附件二进制、扩展安装包、第三方网盘或其他外部存储，请保留现有本地备份。</p>
<button id="export" class="secondary" type="button" disabled>导出历史同步对象</button></section>
<section role="status" aria-live="polite" id="notice">请先登录燕子账户。</section>
</main><script src="/upgrade.js" defer></script></body></html>`;

export const upgradePageScript = `(() => {
 'use strict';
 let token = null;
 const el = id => document.getElementById(id);
 const status = message => { el('notice').textContent = message; };
 const setLoggedIn = yes => {
   el('redeem').disabled = !yes;
   el('export').disabled = !yes;
   el('logout').disabled = !yes;
 };
 const headers = () => ({'accept':'application/json',
   'x-yanzi-client':'web','x-yanzi-client-version':'0.1.0',
   ...(token ? {authorization:'Bearer '+token} : {})});
 const api = async (path, method='GET', body=null) => {
   const r = await fetch(path,{method,credentials:'omit',cache:'no-store',
     headers:{...headers(), ...(body ? {'content-type':'application/json'} : {})},
     ...(body ? {body:JSON.stringify(body)} : {})});
   let result;
   try { result = await r.json(); }
   catch { throw new Error('服务器没有返回合法的 JSON 数据 (HTTP '+r.status+')'); }
   if (!r.ok) {
     const e = new Error(result.message || result.error || ('HTTP '+r.status));
     e.status = r.status; e.retry = Math.min(60,Number(r.headers.get('retry-after')) || 60);
     throw e;
   }
   return result;
 };
 const fail = error => status('操作未完成：'+(error instanceof Error ? error.message : String(error)));
 const busy = async (button, task) => {
   if(button.disabled)return;
   button.disabled=true;
   try { await task(); }
   catch(e){ fail(e); }
   finally {button.disabled=false;}
 };
 el('signin').addEventListener('click', () => busy(el('signin'),async()=>{
   const email = el('email').value.trim();
   const password = el('password').value;
   if(!email||!password)throw new Error('请填写邮箱和密码');
   const auth = await api('/v1/auth/login','POST',{email,password});
   token = auth.accessToken;
   el('password').value='';
   if(!token)throw new Error('登录响应没有访问令牌');
   setLoggedIn(true);
   const membership = await api('/v1/user/cloud-access');
   status(membership.isVip ? 'VIP 已激活，云服务可用。' : '当前不是有效 VIP。购买卡密后可在本页激活；历史同步对象随时可以导出。');
 }));
 el('logout').addEventListener('click',()=>{token=null;setLoggedIn(false);status('已退出本页登录。');});
 el('redeem').addEventListener('click',()=>busy(el('redeem'),async()=>{
   if(!token)throw new Error('请先登录');
   const code=el('license').value.trim();
   if(!code)throw new Error('请输入 VIP 卡密');
   const answer=await api('/v1/licenses/redeem','POST',{code});
   el('license').value='';
   status(answer.isVip ? 'VIP 激活成功。现在可以使用会员云服务。' : (answer.message||'激活请求已处理，请检查会员状态。'));
 }));
 el('export').addEventListener('click',()=>busy(el('export'),async()=>{
   if(!token)throw new Error('请先登录');
   let cursor=0; const objects=[];
   for(let page=0;page<1000;page++){
     status('正在读取历史云同步数据：第 '+(page+1)+' 页，已取得 '+objects.length+' 条记录…');
     let result;
     for(let retries=0;;retries++){
       try { result=await api('/v1/user/cloud-export?cursor='+cursor);break; }
       catch(error){if(error.status!==429||retries>=2)throw error;
         status('服务器限流，稍后继续同一页导出…');
         await new Promise(resolve=>setTimeout(resolve,error.retry*1000));}
     }
     if(!Array.isArray(result.objects))throw new Error('导出数据格式不正确');
     objects.push(...result.objects);
     if(!result.hasMore){
       const blob = new Blob([JSON.stringify({format:'yanzi-cloud-sync-objects-v1',
         exportedAtUtc:new Date().toISOString(),
         note:'仅包含云同步对象，不含附件和第三方网盘文件。',objects},null,2)],{type:'application/json'});
       const url=URL.createObjectURL(blob);
       const link=document.createElement('a');link.href=url;link.download='yanzi-cloud-sync-history.json';
       link.click();setTimeout(()=>URL.revokeObjectURL(url),10000);
       status('导出完成：'+objects.length+' 条历史同步对象。请妥善保存 JSON，附件等数据需要另外备份。');
       return;
     }
     if(!Number.isSafeInteger(result.cursorRevision)||result.cursorRevision<=cursor)
       throw new Error('分页游标无效，已停止导出，避免重复或丢失数据');
     cursor=result.cursorRevision;
   }
   throw new Error('数据过多，超过单次导出限制，请联系维护者');
 }));
})();`;
