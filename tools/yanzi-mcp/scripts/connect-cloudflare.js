import fs from 'node:fs';
const source='F:/Desktop/kaifa/raccoon-mcp/.raccoon-runtime/';
const runtime=new URL('../.runtime/',import.meta.url);
const state=JSON.parse(fs.readFileSync(source+'cloudflare.json','utf8'));
const secret=fs.readFileSync(source+'cloudflare-api-token.txt','utf8').trim();
const hostname='yanzi-mcp.luoluoluo.cc.cd';
async function api(route,method='GET',body){
 const r=await fetch('https://api.cloudflare.com/client/v4/'+route,{method,headers:{Authorization:'Bearer '+secret,'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(30000)});
 const data=await r.json();if(!r.ok||!data.success)throw Error('Cloudflare '+method+' failed: '+r.status);return data.result;
}
const target=state.tunnelId+'.cfargotunnel.com';
const records=await api(`zones/${state.zoneId}/dns_records?name=${hostname}`);
if(records.some(r=>r.type!=='CNAME'||r.content!==target))throw Error('Hostname is occupied');
const route=`accounts/${state.accountId}/cfd_tunnel/${state.tunnelId}/configurations`;
const current=await api(route);
const own=current.config.ingress.find(x=>x.hostname===hostname);
if(own && own.service!=='http://127.0.0.1:3767')throw Error('Existing hostname uses another service');
if(!own){
 fs.writeFileSync(new URL('cloudflare-before-yanzi.json',runtime),JSON.stringify(current,null,2));
 const ingress=[...current.config.ingress];
 const at=ingress.findIndex(x=>!x.hostname);
 if(at<0)throw Error('Missing catch-all route');
 ingress.splice(at,0,{hostname,path:'^(/mcp|/authorize|/token|/register|/revoke|/oauth/consent|/health|/\\.well-known/oauth-(authorization-server|protected-resource(/mcp)?))$',service:'http://127.0.0.1:3767',originRequest:{httpHostHeader:hostname}});
 await api(route,'PUT',{config:{...current.config,ingress}});
}
if(!records.length)await api(`zones/${state.zoneId}/dns_records`,'POST',{type:'CNAME',name:hostname,content:target,proxied:true,ttl:1,comment:'Yanzi MCP'});
console.log(JSON.stringify({url:`https://${hostname}/mcp`,raccoonRoutePreserved:true,connectorRestarted:false}));
