import {normalizeDataKey} from './application-platform.js';
export function scopesOf(row){return row.scopes_json?JSON.parse(row.scopes_json):[{extensionId:row.extension_id,key:row.data_key,access:row.access}];}
export function selectScopes(requested,available,E){
  const values=requested==='all'?available:requested;
  if(!Array.isArray(values)||!values.length||values.length>200)throw new E(400,'invalid_scopes','Select 1-200 resources, or explicitly select all');
  const result=[],seen=new Set();
  for(const value of values){
    const allowed=available.find(s=>s.extensionId===value.extensionId&&s.key===value.key);
    const access=value.access||allowed?.access;
    if(!allowed||!['read','read-write'].includes(access)||(access==='read-write'&&allowed.access!=='read-write'))throw new E(403,'scope_denied','Requested resource or permission exceeds invitation');
    const identity=allowed.extensionId+'\0'+allowed.key;if(seen.has(identity))throw new E(400,'duplicate_scope','Duplicate resource');seen.add(identity);
    result.push({...allowed,access});
  }
  return result;
}
export async function listAccessResources(env,userId,E){
  const object=await env.PACKAGES.get('downloads/applications/catalog.json');
  const catalog=object&&object.size<=131072?JSON.parse(await object.text()):{applications:[]};
  const result=new Map();
  function add(extensionId,key){
    if(!/^[a-z0-9][a-z0-9-]{0,99}$/.test(extensionId||''))return;
    try{key=normalizeDataKey(key,E);}catch{return;}
    const app=catalog.applications?.find(a=>a.applicationId===extensionId);
    result.set(extensionId+'\0'+key,{extensionId,key,name:app?.name||extensionId,access:'read-write',format:app?.apiSchema?.key===key?app.apiSchema.format:'document'});
  }
  for(const app of catalog.applications||[])for(const key of [...(app.dataKeys||[]),...(app.apiSchema?.key?[app.apiSchema.key]:[])])add(app.applicationId,key);
  const rows=await env.DB.prepare(`SELECT coalesce(json_extract(payload_json,'$.extensionId'),json_extract(payload_json,'$.ExtensionId')) AS extensionId,
    coalesce(json_extract(payload_json,'$.key'),json_extract(payload_json,'$.Key')) AS dataKey FROM user_sync_objects
    WHERE user_id=? AND deleted=0 AND object_id LIKE 'extensionData.v1.%' LIMIT 201`).bind(userId).all();
  if(rows.results.length>200)throw new E(413,'resource_limit','More than 200 resources; use a smaller resource catalog');
  for(const row of rows.results)add(row.extensionId,row.dataKey);
  if(result.size>200)throw new E(413,'resource_limit','More than 200 resources');
  return [...result.values()].sort((a,b)=>(a.extensionId+'/'+a.key).localeCompare(b.extensionId+'/'+b.key));
}
