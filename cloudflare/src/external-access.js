import {applicationBody,normalizeDataKey} from './application-platform.js';
import {scopesOf,selectScopes,listAccessResources} from './access-resources.js';
const now=()=>Math.floor(Date.now()/1000);
const random=()=>Array.from(crypto.getRandomValues(new Uint8Array(32)),b=>b.toString(16).padStart(2,'0')).join('');
async function hash(value){return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value))),b=>b.toString(16).padStart(2,'0')).join('');}
function response(api,value,status=200){const r=api.json(value,status);r.headers.set('cache-control','no-store');return r;}
export async function cleanupExternalAccess(env){
  const cutoff=now()-7*86400;
  await env.DB.batch([env.DB.prepare('DELETE FROM external_access_requests WHERE expires_at < ? AND coalesce(token_expires_at,0) < ?').bind(cutoff,cutoff),
    env.DB.prepare('DELETE FROM external_access_invites WHERE expires_at < ?').bind(cutoff)]);
}
export async function handleExternalAccess(request,env,api){
  const url=new URL(request.url),path=url.pathname,E=api.HttpError;
  if(path==='/v1/applications/access-resources'&&request.method==='GET'){
    const auth=await api.requireAuth(request,env);return response(api,{ok:true,resources:await listAccessResources(env,auth.userId,E)});
  }
  if(path==='/v1/applications/access-grants'&&request.method==='GET'){
    const auth=await api.requireAuth(request,env);
    const rows=await env.DB.prepare("SELECT * FROM external_access_requests WHERE user_id=? AND status='approved' AND token_expires_at>? ORDER BY decided_at DESC LIMIT 200").bind(auth.userId,now()).all();
    const grants=[];for(const row of rows.results){const grant=await api.read(env,auth.userId,'applicationGrant.v1.'+row.request_id);if(grant&&!grant.deleted)grants.push({requestId:row.request_id,clientName:row.client_name,expiresAt:row.token_expires_at,scopes:grant.payload.scopes||scopesOf(row)});}
    return response(api,{ok:true,grants});
  }
  const revokeGrant=path.match(/^\/v1\/applications\/access-grants\/([a-f0-9-]{36})$/);
  if(revokeGrant&&request.method==='DELETE'){
    const auth=await api.requireAuth(request,env),id='applicationGrant.v1.'+revokeGrant[1],grant=await api.read(env,auth.userId,id);
    if(!grant)throw new E(404,'grant_not_found','Grant not found');
    if(!grant.deleted)await api.write(env,auth.userId,id,{schemaVersion:1,expectedRevision:grant.revision,deleted:true,payload:{}});
    return response(api,{ok:true});
  }
  if(path==='/ai'&&request.method==='GET') return new Response('# Yanzi external data access\n\nAsk the user for a resource-specific /connect/... address generated in Yanzi. GET that address, follow its nextRequest JSON to request consent, show the returned userCode to the user, then poll the supplied URL with the private requestSecret. Never read or modify data before approval. After approval use the returned Bearer accessToken and data endpoints. No Yanzi account password is required.\n',{headers:{'content-type':'text/plain; charset=utf-8','access-control-allow-origin':'*'}});
  if(path==='/v1/applications/access-invites'&&request.method==='POST'){
    const auth=await api.requireAuth(request,env),input=await applicationBody(request,E);
    const multi=input.resources!==undefined;
    if(!multi&&!/^[a-z0-9][a-z0-9-]{0,99}$/.test(input.extensionId||''))throw new E(400,'invalid_extension_id','Invalid extension ID');
    const key=multi?'multiple':normalizeDataKey(input.key,E),access=input.access||'read';
    if(!['read','read-write'].includes(access))throw new E(400,'invalid_access','Invalid access');
    const scopes=multi?selectScopes(input.resources,await listAccessResources(env,auth.userId,E),E).map(s=>({...s,access:access==='read'?'read':s.access})):null;
    const extensionId=multi?'multiple':input.extensionId;
    const invite=random(),created=now(),expires=created+7*86400;
    const r=await env.DB.prepare(`INSERT INTO external_access_invites(invite_hash,user_id,extension_id,data_key,access,created_at,expires_at,scopes_json)
      SELECT ?,?,?,?,?,?,?,? WHERE (SELECT count(*) FROM external_access_invites WHERE user_id=? AND revoked=0 AND expires_at>?)<10`)
      .bind(await hash(invite),auth.userId,extensionId,key,access,created,expires,scopes?JSON.stringify(scopes):null,auth.userId,created).run();
    if(!r.meta.changes)throw new E(429,'invite_limit','At most 10 active invitation addresses');
    return response(api,{ok:true,address:url.origin+'/connect/'+invite,expiresAt:expires,extensionId,key,access,resources:scopes||[{extensionId,key,access}]});
  }
  const revoke=path.match(/^\/v1\/applications\/access-invites\/([a-f0-9]{64})$/);
  if(revoke&&request.method==='DELETE'){
    const auth=await api.requireAuth(request,env),inviteHash=await hash(revoke[1]);
    await env.DB.batch([env.DB.prepare('UPDATE external_access_invites SET revoked=1 WHERE invite_hash=? AND user_id=?').bind(inviteHash,auth.userId),
      env.DB.prepare("UPDATE external_access_requests SET status='denied',decided_at=? WHERE invite_hash=? AND user_id=? AND status='pending'").bind(now(),inviteHash,auth.userId)]);
    return response(api,{ok:true});
  }
  if(path==='/v1/applications/access-requests'&&request.method==='GET'){
    const auth=await api.requireAuth(request,env);
    const rows=await env.DB.prepare("SELECT * FROM external_access_requests WHERE user_id=? AND status='pending' AND expires_at>? ORDER BY created_at LIMIT 10").bind(auth.userId,now()).all();
    return response(api,{ok:true,accountId:auth.userId,requests:rows.results.map(r=>({requestId:r.request_id,extensionId:r.extension_id,dataKey:r.data_key,access:r.access,clientName:r.client_name,userCode:r.user_code,expiresAt:r.expires_at,scopes:scopesOf(r),requiresScopeConfirmation:!!r.scopes_json}))});
  }
  const decision=path.match(/^\/v1\/applications\/access-requests\/([a-f0-9-]{36})\/decision$/);
  if(decision&&request.method==='POST'){
    const auth=await api.requireAuth(request,env),input=await applicationBody(request,E),id=decision[1],time=now();
    if(typeof input.approve!=='boolean')throw new E(400,'decision_required','approve must be a boolean');
    const row=await env.DB.prepare('SELECT * FROM external_access_requests WHERE request_id=? AND user_id=?').bind(id,auth.userId).first();
    if(!row)throw new E(404,'request_not_found','Request not found');
    if(row.status!=='pending'||row.expires_at<=time)throw new E(409,'request_already_decided','Request handled or expired');
    const approvedScopes=input.approve&&row.scopes_json?selectScopes(input.scopes,scopesOf(row),E):null;
    let expires=0;
    if(input.approve){
      const grantId='applicationGrant.v1.'+id;
      let grant=await api.read(env,auth.userId,grantId);
      if(!grant){
        try{grant=await api.write(env,auth.userId,grantId,{schemaVersion:1,expectedRevision:0,deleted:false,
          payload:{extensionId:row.extension_id,key:row.data_key,access:row.access,clientName:row.client_name,expiresAt:time+3600,...(approvedScopes?{scopes:approvedScopes}:{})}});}
        catch(error){if(error.status!==409)throw error;grant=await api.read(env,auth.userId,grantId);}
      }
      if(!grant||grant.deleted||grant.payload.extensionId!==row.extension_id||grant.payload.key!==row.data_key)throw new E(409,'grant_invalid','Grant differs');
      if(approvedScopes&&JSON.stringify(grant.payload.scopes)!==JSON.stringify(approvedScopes))throw new E(409,'grant_invalid','Another device selected different scopes; refresh the request');
      expires=grant.payload.expiresAt;
    }
    const updated=await env.DB.prepare("UPDATE external_access_requests SET status=?,decided_at=?,token_expires_at=? WHERE request_id=? AND user_id=? AND status='pending' AND expires_at>?")
      .bind(input.approve?'approved':'denied',time,expires,id,auth.userId,time).run();
    if(!updated.meta.changes)throw new E(409,'request_already_decided','Another device handled the request');
    return response(api,{ok:true,status:input.approve?'approved':'denied'});
  }
  const connect=path.match(/^\/connect\/([a-f0-9]{64})(?:\/(requests|resources))?$/);
  if(connect){
    const inviteHash=await hash(connect[1]),invite=await env.DB.prepare('SELECT * FROM external_access_invites WHERE invite_hash=? AND revoked=0 AND expires_at>?').bind(inviteHash,now()).first();
    if(!invite)throw new E(404,'invitation_unavailable','Invitation unavailable or expired');
    const address=url.origin+'/connect/'+connect[1];
    const available=scopesOf(invite);
    if(request.method==='GET'&&connect[2]==='resources')return response(api,{ok:true,resources:available,containsData:false});
    if(request.method==='GET'&&!connect[2]){
      return response(api,{protocol:'yanzi-device-consent-v1',authorizationRequired:true,extensionId:invite.extension_id,key:invite.data_key,maximumAccess:invite.access,
        instructions:'POST nextRequest to request access. Tell the user the returned userCode and wait for a Yanzi approval popup. Poll no faster than every 3 seconds with requestSecret. Approval returns a one-hour Bearer token. No account password or manual token copying is required.',
        resources:available,resourceList:address+'/resources',selectionInstructions:'List resources first; request a scopes array of {extensionId,key,access}, or scopes:"all" for the entire listed snapshot. Each scope still requires device consent. New resources are never automatically included.',
        nextRequest:{method:'POST',url:address+'/requests',body:{clientName:'Your AI or application name',access:invite.access,...(invite.scopes_json?{scopes:available.map(s=>({extensionId:s.extensionId,key:s.key,access:s.access}))}:{})}},
        expiresAt:invite.expires_at});
    }
    if(request.method==='POST'&&connect[2]==='requests'){
      const input=await applicationBody(request,E),clientName=typeof input.clientName==='string'?input.clientName.trim():'';
      if(!clientName||clientName.length>100||/[\u0000-\u001f]/.test(clientName))throw new E(400,'invalid_client_name','clientName must be 1-100 characters');
      const access=input.access||invite.access;
      if(!['read','read-write'].includes(access)||(access==='read-write'&&invite.access!=='read-write'))throw new E(403,'scope_denied','Requested access exceeds invitation');
      const scopes=invite.scopes_json?selectScopes(input.scopes,available,E).map(s=>({...s,access:access==='read'?'read':s.access})):null;
      const id=crypto.randomUUID(),secret=random(),userCode=random().slice(0,8).toUpperCase(),time=now();
      const result=await env.DB.prepare(`INSERT INTO external_access_requests(request_id,invite_hash,user_id,extension_id,data_key,access,client_name,user_code,secret_hash,created_at,expires_at,scopes_json)
        SELECT ?,?,?,?,?,?,?,?,?,?,?,? WHERE (SELECT count(*) FROM external_access_requests WHERE user_id=? AND status='pending' AND expires_at>?)<3
        AND (SELECT count(*) FROM external_access_requests WHERE invite_hash=? AND created_at>?)<3`)
        .bind(id,inviteHash,invite.user_id,invite.extension_id,invite.data_key,access,clientName,userCode,await hash(secret),time,time+300,scopes?JSON.stringify(scopes):null,invite.user_id,time,inviteHash,time-60).run();
      if(!result.meta.changes)throw new E(429,'request_limit','Too many authorization requests; retry later');
      return response(api,{ok:true,status:'pending',requestId:id,requestSecret:secret,userCode,expiresAt:time+300,pollIntervalSeconds:3,
        poll:{method:'GET',url:url.origin+'/v1/external-access/requests/'+id,headers:{Authorization:'Bearer '+secret}}},202);
    }
    throw new E(405,'method_not_allowed','Use GET discovery or POST /requests');
  }
  const poll=path.match(/^\/v1\/external-access\/requests\/([a-f0-9-]{36})$/);
  if(poll&&request.method==='GET'){
    const secret=request.headers.get('authorization')?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
    if(!secret)throw new E(401,'request_secret_required','Private requestSecret required');
    const row=await env.DB.prepare('SELECT * FROM external_access_requests WHERE request_id=? AND secret_hash=?').bind(poll[1],await hash(secret)).first();
    if(!row)throw new E(404,'request_not_found','Request not found');
    if(row.status==='denied')throw new E(403,'access_denied','User denied the request');
    if(row.status==='pending'&&row.expires_at<=now())throw new E(410,'request_expired','Request expired');
    if(row.last_polled_at>now()-3){const r=response(api,{error:'slow_down',message:'Poll every 3 seconds or slower'},429);r.headers.set('retry-after','3');return r;}
    const claimed=await env.DB.prepare('UPDATE external_access_requests SET last_polled_at=? WHERE request_id=? AND last_polled_at<=?').bind(now(),row.request_id,now()-3).run();
    if(!claimed.meta.changes){const r=response(api,{error:'slow_down'},429);r.headers.set('retry-after','3');return r;}
    if(row.status==='pending')return response(api,{ok:true,status:'pending',userCode:row.user_code},202);
    const grant=await api.read(env,row.user_id,'applicationGrant.v1.'+row.request_id);
    if(!grant||grant.deleted)throw new E(403,'grant_revoked','Grant revoked');
    if(row.token_expires_at<=now())throw new E(410,'grant_expired','Grant expired; request new consent');
    const token=await api.signToken(env,{sub:row.user_id,type:'extension-access',extensionId:row.extension_id,key:row.data_key,access:row.access,grantId:row.request_id,exp:row.token_expires_at});
    const root=url.origin+'/v1/extension-data/'+encodeURIComponent(row.extension_id),query='?key='+encodeURIComponent(row.data_key);
    return response(api,{ok:true,status:'approved',accessToken:token,tokenType:'Bearer',expiresAt:row.token_expires_at,accountId:row.user_id,
      extensionId:row.extension_id,key:row.data_key,access:row.access,...(!row.scopes_json?{data:{document:root+query,records:root+'/records'+query}}:{}),
      resources:(grant.payload.scopes||scopesOf(row)).map(s=>({...s,data:{document:url.origin+'/v1/extension-data/'+encodeURIComponent(s.extensionId)+'?key='+encodeURIComponent(s.key),records:url.origin+'/v1/extension-data/'+encodeURIComponent(s.extensionId)+'/records?key='+encodeURIComponent(s.key)}})),
      usage:'Use Authorization: Bearer accessToken. GET records to obtain IDs and versions. POST records creates; PATCH/DELETE records/{id}?key=... require expectedVersion. GET document returns versioned content; PUT requires accountId and expectedRevision.'});
  }
  return null;
}
