'use strict';
// Optional per-user backend for the PRIVATE YanZi miniapp.
// No browser cookies or music licenses are extracted from the desktop client.
const http = require('node:http');
const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');
const api = require('NeteaseCloudMusicApi');
const account = require('./auth-store.cjs');

const PORT = Number(process.env.YANZI_MUSIC_PORT || 38651);
const APP = path.resolve(__dirname, '..', 'app');
const TOKEN = crypto.randomBytes(32).toString('hex');
let cookie = account.load();
let qrKey = null;
let lastQrPoll = 0;
let lastActivity = Date.now();
const levels = ['lossless','exhigh','higher','standard'];
const coverHost = host => host === 'music.163.com' || host.endsWith('.music.126.net')
    || host.endsWith('.music.163.com');

function reply(res, code, data, origin) {
  res.writeHead(code, {
    'Content-Type':'application/json; charset=utf-8',
    'Cache-Control':'no-store',
    'X-Content-Type-Options':'nosniff',
    'Access-Control-Allow-Origin': origin === 'null' ? 'null' : '',
    'Access-Control-Allow-Headers':'X-Yanzi-Private-Token, Content-Type',
    'Access-Control-Allow-Methods':'GET, POST, OPTIONS',
    'Vary':'Origin',
  });
  res.end(JSON.stringify(data));
}
function tokenMatches(candidate) {
  const a=Buffer.from(String(candidate||''),'utf8'), b=Buffer.from(TOKEN,'utf8');
  return a.length === b.length && crypto.timingSafeEqual(a,b);
}
async function handle(req,res) {
  lastActivity=Date.now();
  const origin=req.headers.origin;
  if (origin && origin!=='null') return reply(res,403,{ok:false,error:'origin_denied'},origin);
  if(req.method==='OPTIONS') return reply(res,204,{},origin);
  if(!tokenMatches(req.headers['x-yanzi-private-token'])) return reply(res,401,{ok:false,error:'invalid_local_capability'},origin);
  const target=new URL(req.url,'http://127.0.0.1:'+PORT);
  try {
    if (target.pathname==='/health') return reply(res,200,{ok:true,service:'yanzi-private-music'},origin);
    if (target.pathname==='/account' && req.method==='GET') {
      if (!cookie) return reply(res,200,{ok:true,connected:false},origin);
      try {
        const status=await api.login_status({cookie});
        const profile=status.body?.data?.profile || status.body?.profile;
        return reply(res,200,{ok:true,connected:!!profile},origin);
      } catch (_) { return reply(res,200,{ok:true,connected:false},origin); }
    }
    if (target.pathname==='/login/qr/start' && req.method==='POST') {
      const keyResult=await api.login_qr_key({timestamp:Date.now()});
      const key=keyResult.body?.data?.unikey;
      if(!key) throw Error('官方二维码申请失败');
      qrKey=key;
      const imgResult=await api.login_qr_create({key,qrimg:true,timestamp:Date.now()});
      const image=imgResult.body?.data?.qrimg;
      if(typeof image!=='string' || !image.startsWith('data:image/')) throw Error('无法生成登录二维码');
      return reply(res,200,{ok:true,qrImage:image},origin);
    }
    if (target.pathname==='/login/qr/status' && req.method==='GET') {
      if(!qrKey) return reply(res,409,{ok:false,status:'not_started'},origin);
      if(Date.now()-lastQrPoll<1800) return reply(res,429,{ok:false,status:'rate_limited'},origin);
      lastQrPoll=Date.now();
      const r=await api.login_qr_check({key:qrKey,timestamp:Date.now(),noCookie:true});
      const code=Number(r.body?.code);
      if(code===803) {
        const raw=r.body?.cookie || r.cookie?.join('; ') || '';
        account.save(raw);
        cookie=account.load();
        qrKey=null;
        return reply(res,200,{ok:true,status:'authorized',connected:!!cookie},origin);
      }
      if(code===800) qrKey=null;
      return reply(res,200,{ok:true,status:code===800?'expired':code===802?'awaiting_confirmation':'waiting'},origin);
    }
    if(target.pathname==='/song/url' && req.method==='GET') {
      const songId=target.searchParams.get('id');
      if(!/^[1-9]\d{0,17}$/.test(songId || '')) return reply(res,400,{ok:false,error:'invalid_song_id'},origin);
      const preferred=target.searchParams.get('quality')||'exhigh';
      const selected=levels.includes(preferred)?preferred:'exhigh';
      const order=levels.slice(levels.indexOf(selected));
      let preview=null;
      for (const level of order) {
        const r=await api.song_url_v1({id:songId,level, ...(cookie?{cookie}:{})});
        const track=r.body?.data?.find(x=>String(x.id)===songId);
        if (!track?.url) continue;
        let parsed;
        try {parsed=new URL(track.url)}catch(_){continue}
        if(!coverHost(parsed.hostname) || !['http:','https:'].includes(parsed.protocol)) continue;
        if(parsed.protocol==='http:') parsed.protocol='https:';
        const item = {ok:true,songId,level,trial:!!track.freeTrialInfo,
          authorized:!!cookie,validityMs:Math.max(0,Number(track.expi||0))*1000,
          url:parsed.toString(),size:Number(track.size||0),code:Number(track.code||0)};
        if(!item.trial) return reply(res,200,item,origin);
        if(!preview) preview=item;
      }
      if(preview) return reply(res,200,preview,origin);
      return reply(res,404,{ok:false,error:'no_authorized_music_source'},origin);
    }
    if(target.pathname==='/logout' && req.method==='POST') {
      cookie='';qrKey=null;account.clear();
      return reply(res,200,{ok:true,connected:false},origin);
    }
    return reply(res,404,{ok:false,error:'unknown_route'},origin);
  } catch (e) {
    // Never echo headers, auth cookies, signed media URLs, or upstream response.
    return reply(res,502,{ok:false,error:'upstream_error',message:String(e?.message||'Unknown error').slice(0,90)},origin);
  }
}
const server=http.createServer((req,res)=>{void handle(req,res)});
server.requestTimeout=12000;
server.listen(PORT,'127.0.0.1',()=>{
  // The token only grants access to the local helper and is never a NetEase credential.
  fs.writeFileSync(path.join(APP,'private-service-config.js'),
    'window.YanziPrivateMusicService='+JSON.stringify({url:'http://127.0.0.1:'+PORT,token:TOKEN})+';\n',
    {encoding:'utf8',mode:0o600});
  console.log('PRIVATE_MUSIC_LISTENING 127.0.0.1:'+PORT);
});
setInterval(()=>{if(Date.now()-lastActivity>45*60*1000)server.close(()=>process.exit(0));},60*1000).unref();
