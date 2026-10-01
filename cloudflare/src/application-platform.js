const encoder = new TextEncoder();
const MAX_CONTENT = 256 * 1024;
const MAX_BODY = MAX_CONTENT * 6 + 8192;
export async function extensionObjectId(extensionId, key) {
  const bytes = await crypto.subtle.digest('SHA-256', encoder.encode(extensionId + '\0' + key));
  return 'extensionData.v1.' + hex(bytes);
}
function hex(bytes) { return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join(''); }
function identifier(value, ErrorType) {
  if (typeof value !== 'string' || !/^[a-z0-9][a-z0-9-]{0,99}$/.test(value))
    throw new ErrorType(400, 'invalid_application_id', 'Invalid application ID');
  return value;
}
export async function applicationBody(request, ErrorType) {
  if (Number(request.headers.get('content-length')) > MAX_BODY)
    throw new ErrorType(413, 'payload_too_large', 'Payload too large');
  const reader = request.body?.getReader();
  let size = 0; const chunks = [];
  if (!reader) throw new ErrorType(400, 'body_required', 'JSON body required');
  while (true) {
    const {done, value} = await reader.read(); if (done) break;
    size += value.length;
    if (size > MAX_BODY) { await reader.cancel(); throw new ErrorType(413, 'payload_too_large', 'Payload too large'); }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  let result;
  try { result = JSON.parse(new TextDecoder().decode(bytes)); }
  catch { throw new ErrorType(400, 'invalid_json', 'Invalid JSON'); }
  if (!result || typeof result !== 'object' || Array.isArray(result)) throw new ErrorType(400, 'invalid_json', 'JSON object required');
  return result;
}
const body = applicationBody;
export function normalizeDataKey(rawKey,E) {
  if (!rawKey || rawKey.length > 200 || /[\u0000-\u001f]/.test(rawKey)) throw new E(400,'invalid_key','Invalid data key');
  const segments=rawKey.replaceAll('\\','/').split('/').map(s=>s.trim()).filter(Boolean);
  if (!segments.length || segments.some(s=>s==='.'||s==='..')) throw new E(400,'invalid_key','Invalid data key');
  return segments.join('/');
}
export async function authorizeData(request,env,api,extensionId,key,write=false) {
  const header=request.headers.get('authorization')||'';
  if (header.startsWith('Bearer ')) {
    const claims=await api.verifyToken(env,header.slice(7).trim());
    if (claims.type==='extension-access') {
      const grant=await api.read(env,claims.sub,'applicationGrant.v1.'+claims.grantId);
      if (claims.extensionId!==extensionId || !grant || grant.deleted || grant.payload?.extensionId!==extensionId ||
          grant.payload.access!==claims.access || grant.payload.expiresAt<=Date.now()/1000 ||
          (grant.payload.key && (grant.payload.key!==key || claims.key!==key)))
        throw new api.HttpError(403,'scope_denied','Access denied or revoked');
      if (write && claims.access!=='read-write') throw new api.HttpError(403,'read_only','Read-only grant');
      return {userId:claims.sub};
    }
  }
  return api.requireAuth(request,env);
}
export async function handleApplicationPlatform(request, env, api) {
  const url = new URL(request.url), path = url.pathname, E = api.HttpError;
  if (!path.startsWith('/v1/applications') && !path.startsWith('/v1/extension-data/')) return null;
  if (path === '/v1/applications/catalog' && request.method === 'GET') {
    const object = await env.PACKAGES.get('downloads/applications/catalog.json');
    if (!object) return api.json({ok:true, schemaVersion:1, applications:[]});
    if (object.size > 128 * 1024) throw new E(503, 'catalog_invalid', 'Catalog exceeds limit');
    const catalog = await object.json();
    if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.applications)) throw new E(503, 'catalog_invalid', 'Invalid catalog');
    const response = api.json({ok:true, ...catalog}); response.headers.set('cache-control','no-store'); return response;
  }
  const grantMatch = path.match(/^\/v1\/applications\/([a-z0-9-]+)\/grants(?:\/([a-f0-9-]+))?$/);
  if (grantMatch) {
    const auth = await api.requireAuth(request, env), extensionId = identifier(grantMatch[1], E);
    if (request.method === 'POST' && !grantMatch[2]) {
      const input = await body(request, E);
      if (input.userConsent !== true || !['read','read-write'].includes(input.access))
        throw new E(400, 'consent_required', 'Explicit user consent and read/read-write access required');
      const clientName = String(input.clientName || '').trim().slice(0,100);
      if (!clientName) throw new E(400, 'client_name_required', 'Client name required');
      const grantId = crypto.randomUUID(), expiresAt = Math.floor(Date.now()/1000) + 3600;
      await api.write(env, auth.userId, 'applicationGrant.v1.' + grantId,
        {schemaVersion:1,expectedRevision:0,deleted:false,payload:{extensionId,access:input.access,clientName,expiresAt}});
      const token = await api.signToken(env, {sub:auth.userId,type:'extension-access',extensionId,
        access:input.access,grantId,exp:expiresAt});
      const response = api.json({ok:true,grantId,extensionId,access:input.access,expiresAt,accessToken:token});
      response.headers.set('cache-control','no-store'); return response;
    }
    if (request.method === 'DELETE' && grantMatch[2]) {
      const objectId = 'applicationGrant.v1.' + grantMatch[2], grant = await api.read(env,auth.userId,objectId);
      if (!grant || grant.payload?.extensionId !== extensionId) throw new E(404,'grant_not_found','Grant not found');
      if (!grant.deleted) await api.write(env,auth.userId,objectId,{schemaVersion:1,expectedRevision:grant.revision,deleted:true,payload:{}});
      return api.json({ok:true});
    }
    throw new E(405,'method_not_allowed','Unsupported grant method');
  }
  const libraryMatch = path.match(/^\/v1\/applications\/library(?:\/([a-z0-9-]+))?$/);
  if (libraryMatch) {
    const auth = await api.requireAuth(request, env);
    if (!libraryMatch[1] && request.method === 'GET') {
      const index = await api.read(env,auth.userId,'applications.index.v1');
      const applications = [];
      for (const id of (index?.payload?.ids || []).slice(0,200)) {
        const object = await api.read(env,auth.userId,'applicationSelection.v1.' + identifier(id,E));
        if (object && !object.deleted) applications.push({applicationId:id,revision:object.revision,...object.payload});
      }
      return api.json({ok:true,applications});
    }
    if (libraryMatch[1] && request.method === 'PUT') {
      const id = identifier(libraryMatch[1],E), input = await body(request,E);
      if (!Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 0 || typeof input.enabled !== 'boolean')
        throw new E(400,'invalid_selection','enabled and expectedRevision required');
      // Index first: an interrupted selection write can be retried without losing discoverability.
      for (let attempt=0; attempt<4; attempt++) {
        const index = await api.read(env,auth.userId,'applications.index.v1'), ids = index?.payload?.ids || [];
        if (ids.includes(id)) break;
        if (ids.length >= 200) throw new E(400,'library_full','Application library limit reached');
        try {
          await api.write(env,auth.userId,'applications.index.v1',{schemaVersion:1,expectedRevision:index?.revision||0,
            deleted:false,payload:{ids:[...ids,id]}}); break;
        } catch(error) { if (error.status !== 409 || attempt === 3) throw error; }
      }
      const object = await api.write(env,auth.userId,'applicationSelection.v1.'+id,{schemaVersion:1,
        expectedRevision:input.expectedRevision,deleted:false,payload:{enabled:input.enabled}});
      return api.json({ok:true,revision:object.revision});
    }
    throw new E(405,'method_not_allowed','Unsupported library method');
  }
  const dataMatch = path.match(/^\/v1\/extension-data\/([a-z0-9-]+)$/);
  if (dataMatch) {
    const extensionId = identifier(dataMatch[1],E);
    const rawKey = url.searchParams.get('key');
    const key = normalizeDataKey(rawKey,E);
    const auth = await authorizeData(request,env,api,extensionId,key,request.method!=='GET');
    const id = await extensionObjectId(extensionId,key);
    if (request.method === 'GET') {
      const object = await api.read(env,auth.userId,id), payload = object?.payload;
      const content = payload?.content ?? payload?.Content ?? '';
      if (object && !object.deleted && ((payload?.extensionId ?? payload?.ExtensionId) !== extensionId || (payload?.key ?? payload?.Key) !== key))
        throw new E(409,'invalid_envelope','Stored data scope mismatch');
      if (object && !object.deleted && hex(await crypto.subtle.digest('SHA-256',encoder.encode(content))) !== (payload.contentHash ?? payload.ContentHash)?.toLowerCase())
        throw new E(409,'invalid_content_hash','Stored content checksum mismatch');
      const response = api.json({ok:true,accountId:auth.userId,exists:!!object&&!object.deleted,
        revision:object?.revision||0,content:object&&!object.deleted?content:''});
      response.headers.set('cache-control','no-store'); return response;
    }
    if (request.method === 'PUT') {
      const input = await body(request,E);
      if (input.accountId !== auth.userId) throw new E(409,'account_changed','Expected account differs');
      if (!Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 0 || typeof input.content !== 'string')
        throw new E(400,'invalid_write','content and expectedRevision required');
      const contentBytes = encoder.encode(input.content);
      if (contentBytes.length > MAX_CONTENT) throw new E(413,'content_too_large','Content exceeds 256 KiB');
      const contentHash = hex(await crypto.subtle.digest('SHA-256',contentBytes));
      const object = await api.write(env,auth.userId,id,{schemaVersion:1,expectedRevision:input.expectedRevision,
        deleted:false,payload:{extensionId,key,content:input.content,contentHash,contentType:'text/plain; charset=utf-8'}});
      return api.json({ok:true,accountId:auth.userId,revision:object.revision});
    }
    throw new E(405,'method_not_allowed','Unsupported data method');
  }
  throw new E(404,'not_found','Application route not found');
}
