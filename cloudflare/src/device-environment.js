import { HttpError } from './http-error.js';
import { readJson } from './request-json.js';

export const ENVIRONMENT_SCHEMA = `CREATE TABLE IF NOT EXISTS device_environment (
 user_id TEXT NOT NULL, device_id TEXT NOT NULL, extension_id TEXT NOT NULL,
 sequence INTEGER NOT NULL, payload_json TEXT NOT NULL, received_at INTEGER NOT NULL,
 expires_at INTEGER NOT NULL, PRIMARY KEY(user_id,device_id,extension_id))`;
const schemas=new WeakMap();
async function ensureSchema(db) {
  if(!schemas.has(db)) schemas.set(db,db.prepare(ENVIRONMENT_SCHEMA).run().catch(error=>{schemas.delete(db);throw error;}));
  await schemas.get(db);
}
async function readEnvironmentJson(request) {
  const reader=request.body?.getReader();
  if(!reader)return readJson(request);
  const decoder=new TextDecoder();let text='',bytes=0;
  try {
    while(true) {
      const chunk=await reader.read();if(chunk.done)break;
      bytes+=chunk.value.byteLength;
      if(bytes>8192){await reader.cancel();throw new HttpError(413,'environment_too_large','Environment snapshot exceeds 8 KiB');}
      text+=decoder.decode(chunk.value,{stream:true});
    }
    text+=decoder.decode();
  }finally {reader.releaseLock();}
  return readJson(new Request(request.url,{method:'POST',body:text}));
}
export function normalizeEnvironment(body, now = Date.now()) {
  if (!Number.isSafeInteger(body.sequence) || body.sequence < 1 || typeof body.enabled !== 'boolean')
    throw new HttpError(400, 'invalid_environment', 'Invalid sequence or enabled flag');
  const value = {schemaVersion:1,sequence:body.sequence,enabled:body.enabled};
  if (!body.enabled) return value;
  const observed = Date.parse(body.observedAt);
  if (!Number.isFinite(observed) || observed > now + 300000 || observed < now - 3600000 ||
      !['home','away','unknown'].includes(body.place) || !['high','low','unknown'].includes(body.confidence))
    throw new HttpError(400,'invalid_environment','Invalid or stale sample');
  value.observedAt = new Date(observed).toISOString(); value.place=body.place; value.confidence=body.confidence;
  const network=body.network || {};
  value.network={wifiConnected:network.wifiConnected === true,internetValidated:network.internetValidated === true,
    type:['wifi','cellular','other','none'].includes(network.type)?network.type:'none'};
  value.availability=String(body.availability || 'unknown').slice(0,64);
  if (body.shareCoordinates === true && body.location) {
    const {latitude,longitude,accuracy}=body.location;
    if (![latitude,longitude,accuracy].every(Number.isFinite) || Math.abs(latitude)>90 || Math.abs(longitude)>180 || accuracy<0 || accuracy>100000)
      throw new HttpError(400,'invalid_environment','Invalid coordinates');
    value.location={latitude,longitude,accuracy};
  }
  return value;
}
export async function cleanupEnvironment(env, now=Date.now()) {
  await ensureSchema(env.DB);
  await env.DB.prepare('DELETE FROM device_environment WHERE expires_at <= ?').bind(now).run();
}
export async function handleEnvironment(request, env, api) {
  const match=new URL(request.url).pathname.match(/^\/v1\/me\/devices\/([^/]+)\/environment\/([a-zA-Z0-9_.-]{1,128})$/);
  if(!match) return null;
  const auth=await api.requireAuth(request,env);
  if(auth.grant) throw new HttpError(403,'account_owner_required','Environment requires account login');
  const device=api.normalizeDeviceId(decodeURIComponent(match[1])), extension=match[2];
  await api.ensureOwnedDevice(env,auth.userId,device);
  if(!['GET','PUT'].includes(request.method)) throw new HttpError(405,'method_not_allowed','Use GET or PUT');
  await ensureSchema(env.DB);
  const now=Date.now();
  if(request.method==='PUT') {
    const value=normalizeEnvironment(await readEnvironmentJson(request),now);
    await env.DB.prepare(`INSERT INTO device_environment VALUES(?,?,?,?,?,?,?)
      ON CONFLICT(user_id,device_id,extension_id) DO UPDATE SET sequence=excluded.sequence,
      payload_json=excluded.payload_json,received_at=excluded.received_at,expires_at=excluded.expires_at
      WHERE excluded.sequence > device_environment.sequence`).bind(auth.userId,device,extension,value.sequence,JSON.stringify(value),now,now+86400000).run();
    const row=await env.DB.prepare('SELECT sequence,payload_json FROM device_environment WHERE user_id=? AND device_id=? AND extension_id=?').bind(auth.userId,device,extension).first();
    if(row.sequence!==value.sequence || row.payload_json!==JSON.stringify(value)) throw new HttpError(409,'stale_environment','Newer or different state already exists');
    return api.json({ok:true,sequence:value.sequence,receivedAt:new Date(now).toISOString()});
  }
  const row=await env.DB.prepare('SELECT payload_json,received_at,expires_at FROM device_environment WHERE user_id=? AND device_id=? AND extension_id=? AND expires_at>?').bind(auth.userId,device,extension,now).first();
  const value=row?JSON.parse(row.payload_json):null;
  const response=api.json({ok:true,deviceId:device,extensionId:extension,exists:!!value?.enabled,
    value:value?.enabled?value:null,receivedAt:row?new Date(row.received_at).toISOString():null});
  response.headers.set('Cache-Control','no-store'); return response;
}
