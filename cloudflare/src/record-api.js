import {applicationBody,authorizeData,normalizeDataKey,extensionObjectId} from './application-platform.js';
const encoder=new TextEncoder();
async function digest(content){return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',encoder.encode(content))),b=>b.toString(16).padStart(2,'0')).join('');}
function fail(E,code,message,status=400){throw new E(status,code,message);}
function view(id,record,profile){const result={id,version:record.version};for(const [key,field] of Object.entries(profile.fields)){const value=record.item[field.name]??null;result[key]=field.type==='date'&&typeof value==='string'?value.slice(0,10):value;}return result;}
function itemFrom(input,old,profile,E){
  const item={...profile.defaults,...old};
  for(const [key,value] of Object.entries(input)){
    if(['id','expectedVersion'].includes(key))continue;
    const field=profile.fields[key];if(!field)fail(E,'unknown_field','Unknown field: '+key);
    if(value===null&&field.nullable){item[field.name]=null;continue;}
    if(field.type==='string'&&(typeof value!=='string'||!value.trim()||value.length>(field.maxLength||4096)))fail(E,'invalid_field','Invalid '+key);
    if(field.type==='boolean'&&typeof value!=='boolean')fail(E,'invalid_field','Invalid '+key);
    if(field.type==='date'){
      if(typeof value!=='string'||!/^\d{4}-\d{2}-\d{2}$/.test(value)||!Number.isFinite(Date.parse(value+'T00:00:00Z'))||new Date(value+'T00:00:00Z').toISOString().slice(0,10)!==value)fail(E,'invalid_field','Expected YYYY-MM-DD for '+key);
      item[field.name]=field.format==='local-midnight'?value+'T00:00:00':value;
    }else if(field.type==='datetime'){
      if(typeof value!=='string'||!/^\d{4}-\d{2}-\d{2}T/.test(value)||!Number.isFinite(Date.parse(value)))fail(E,'invalid_field','Invalid date/time for '+key);
      item[field.name]=value;
    }else item[field.name]=value;
  }
  for(const [key,field] of Object.entries(profile.fields))if(field.required&&(item[field.name]===undefined||item[field.name]===null))fail(E,'required_field','Required field: '+key);
  for(const [target,triggers] of Object.entries(profile.resetOnChange||{}))if(old&&triggers.some(name=>item[name]!==old[name]))item[target]=false;
  return item;
}
export async function handleRecordApi(request,env,api){
  const url=new URL(request.url),match=url.pathname.match(/^\/v1\/extension-data\/([a-z0-9-]+)\/records(?:\/([a-zA-Z0-9_-]{1,100}))?$/);
  if(!match)return null;
  const E=api.HttpError,extensionId=match[1],key=normalizeDataKey(url.searchParams.get('key'),E),method=request.method;
  if(!['GET','POST','PATCH','DELETE'].includes(method))fail(E,'method_not_allowed','Unsupported method',405);
  if((method==='POST'&&match[2])||(['PATCH','DELETE'].includes(method)&&!match[2]))fail(E,'method_not_allowed','Use collection POST or record PATCH/DELETE',405);
  const auth=await authorizeData(request,env,api,extensionId,key,method!=='GET');
  const catalogObject=await env.PACKAGES.get('downloads/applications/catalog.json');
  if(!catalogObject||catalogObject.size>131072)fail(E,'schema_unavailable','Application schema unavailable',503);
  const catalog=JSON.parse(await catalogObject.text());
  const profile=catalog.applications?.find(app=>app.applicationId===extensionId)?.apiSchema;
  if(profile?.format!=='records-v1'||profile.key!==key||!profile.fields)fail(E,'unsupported_schema','This resource has no record API',404);
  const objectId=await extensionObjectId(extensionId,key),input=method==='GET'?null:await applicationBody(request,E);
  const id=match[2]||input?.id||crypto.randomUUID();
  if(!/^[a-zA-Z0-9_-]{1,100}$/.test(id))fail(E,'invalid_id','Invalid record ID');
  for(let attempt=0;attempt<4;attempt++){
    const object=await api.read(env,auth.userId,objectId),payload=object?.payload;
    let doc={schemaVersion:1,records:{}};
    if(object&&!object.deleted){
      const content=payload.content??payload.Content;
      if((payload.extensionId??payload.ExtensionId)!==extensionId||(payload.key??payload.Key)!==key||typeof content!=='string'||await digest(content)!==(payload.contentHash??payload.ContentHash)?.toLowerCase())fail(E,'invalid_envelope','Stored document failed integrity checks',409);
      try{doc=JSON.parse(content);}catch{fail(E,'invalid_document','Stored document is not JSON',409);}
      if(doc.schemaVersion!==1||!doc.records||typeof doc.records!=='object'||Array.isArray(doc.records))fail(E,'invalid_document','Expected records-v1 document',409);
    }
    const old=Object.hasOwn(doc.records,id)?doc.records[id]:null;
    if(method==='GET'){
      if(match[2]&&(!old||old.deleted))fail(E,'record_not_found','Record not found',404);
      const response=api.json({ok:true,revision:object?.revision||0,...(match[2]?{record:view(id,old,profile)}:{fields:profile.fields,records:Object.entries(doc.records).filter(([,v])=>!v.deleted).map(([k,v])=>view(k,v,profile))})});
      response.headers.set('cache-control','no-store');return response;
    }
    if(method==='POST'&&old)fail(E,'record_exists','Record ID already exists',409);
    if(method!=='POST'){
      if(!old||old.deleted)fail(E,'record_not_found','Record not found',404);
      if(typeof input.expectedVersion!=='string'||input.expectedVersion!==old.version)fail(E,'record_conflict','Read the latest record and retry with its expectedVersion',409);
    }
    const item=method==='DELETE'?old.item:itemFrom(input,old?.item,profile,E);
    if(method==='POST'){if(profile.itemIdField)item[profile.itemIdField]=id;if(profile.createdTimeField)item[profile.createdTimeField]=new Date().toISOString();}
    const record={version:crypto.randomUUID(),deleted:method==='DELETE',item};Object.defineProperty(doc.records,id,{value:record,enumerable:true,configurable:true,writable:true});
    const content=JSON.stringify(doc);if(encoder.encode(content).length>262144)fail(E,'content_too_large','Document exceeds 256 KiB',413);
    try{
      const saved=await api.write(env,auth.userId,objectId,{schemaVersion:1,expectedRevision:object?.revision||0,deleted:false,payload:{extensionId,key,content,contentHash:await digest(content),contentType:'application/json; charset=utf-8'}});
      return api.json({ok:true,revision:saved.revision,record:method==='DELETE'?{id,version:record.version,deleted:true}:view(id,record,profile)},method==='POST'?201:200);
    }catch(error){if(error.status!==409||attempt===3)throw error;}
  }
}
