'use strict';
(function () {
  const configuration=window.YanziPrivateMusicService || null;
  let connected=false;
  let activePoll=null;
  async function apiCall(route,options={}) {
    if(!configuration) return null;
    const controller=new AbortController();
    const timer=setTimeout(()=>controller.abort(),9000);
    try {
      const res=await fetch(configuration.url+route,{
        method:options.method||'GET',
        cache:'no-store',
        headers:{'X-Yanzi-Private-Token':configuration.token},
        signal:controller.signal
      });
      const data=await res.json();
      return res.ok ? data : null;
    } catch (_){return null}
    finally{clearTimeout(timer)}
  }
  async function resolve(songId) {
    if(!Number.isSafeInteger(Number(songId)) || Number(songId)<=0)return null;
    const info=await apiCall('/song/url?id='+encodeURIComponent(songId)+'&quality=exhigh');
    if(info?.ok && /^https:\/\//.test(info.url||'')) return info;
    return null;
  }
  function initUI() {
    const anchor=document.getElementById('btnManualSync') || document.getElementById('tabNavFavorites');
    if(!anchor || document.getElementById('yanziNcmLoginBtn'))return;
    const btn=document.createElement('button');
    btn.id='yanziNcmLoginBtn';
    btn.type='button';
    btn.textContent=configuration?'连接网易云账号':'网易云账号服务未启动';
    btn.title='通过官方网易云扫码登录，本机保存独立会话，不读取桌面客户端凭证';
    btn.style.cssText='margin-left:9px;padding:7px 11px;background:#343238;color:#fff;border:1px solid #555;border-radius:8px;cursor:pointer;';
    btn.disabled=!configuration;
    anchor.parentElement?.appendChild(btn);

    const panel=document.createElement('div');
    panel.id='yanziNcmLoginPanel';
    panel.style.cssText='position:fixed;inset:0;background:#000b;display:none;align-items:center;justify-content:center;z-index:200000;';
    const box=document.createElement('div');
    box.style.cssText='background:#202024;color:white;border:1px solid #555;padding:28px;border-radius:15px;width:320px;text-align:center;box-shadow:0 12px 48px #0009;';
    const title=document.createElement('h3');title.textContent='网易云音乐账号授权';
    const desc=document.createElement('p');desc.textContent='请使用网易云音乐手机 App 扫码。你的登录凭据仅加密保存在这台电脑。';desc.style.fontSize='13px';
    const pic=document.createElement('img');pic.alt='扫码登录';pic.style.cssText='display:block;width:220px;height:220px;object-fit:contain;background:#fff;margin:10px auto;';
    const status=document.createElement('p');status.textContent='正在准备二维码';
    const exit=document.createElement('button');exit.textContent='关闭';exit.style.cssText='border:0;border-radius:8px;padding:9px 20px;cursor:pointer;';
    exit.onclick=()=>{panel.style.display='none';if(activePoll){clearInterval(activePoll);activePoll=null}};
    const logout=document.createElement('button');
    logout.textContent='解除此设备授权';
    logout.style.cssText='margin:0 8px 8px 0;border:1px solid #775;color:#fff;background:#442b2b;border-radius:8px;padding:9px 12px;cursor:pointer;';
    logout.style.display='none';
    logout.onclick=async()=>{
      const result=await apiCall('/logout',{method:'POST'});
      if(result?.ok){
        connected=false;
        btn.textContent='连接网易云账号';
        logout.style.display='none';
        status.textContent='当前设备的登录凭据已清除';
      } else status.textContent='解除授权失败，请检查本地服务';
    };
    box.append(title,desc,pic,status,logout,exit);
    panel.append(box);
    document.body.append(panel);

    btn.onclick=async()=>{
      panel.style.display='flex';
      if(connected){
        pic.style.display='none';
        status.textContent='此设备已连接网易云账号，播放时优先尝试使用当前账号权限';
        logout.style.display='inline-block';
        return;
      }
      logout.style.display='none';
      pic.style.display='none';
      status.textContent='正在向网易云请求二维码…';
      if(activePoll){clearInterval(activePoll);activePoll=null}
      const data=await apiCall('/login/qr/start',{method:'POST'});
      if(!data?.qrImage){status.textContent='暂时无法创建二维码。请确认网络和本地服务。';return}
      pic.src=data.qrImage;pic.style.display='block';
      status.textContent='等待扫码确认';
      let inFlight=false;
      activePoll=setInterval(async()=>{
        if(inFlight)return;
        inFlight=true;
        try{
          const resp=await apiCall('/login/qr/status');
          if(!resp)return;
          if(resp.status==='authorized' && resp.connected){
            connected=true;
            btn.textContent='网易云账号已连接';
            status.textContent='授权完成！随后播放将优先尝试你的账号可用音源。';
            clearInterval(activePoll);activePoll=null;
            setTimeout(()=>{panel.style.display='none'},1600);
          } else if(resp.status==='expired'){
            status.textContent='二维码已过期，请关闭窗口后重新打开。';
            clearInterval(activePoll);activePoll=null;
          } else if(resp.status==='awaiting_confirmation'){
            status.textContent='请在手机上确认授权';
          }
        }finally{inFlight=false}
      },2400);
    };
    apiCall('/account').then(r=>{
      if(r?.connected){connected=true;btn.textContent='网易云账号已连接'}
    });
  }
  window.YanziAuthorizedMusic={
    resolve,connected:()=>connected,initUI,
    status:()=>apiCall('/account')
  };
  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',initUI);
  else initUI();
})();
