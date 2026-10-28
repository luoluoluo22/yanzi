// Ego is the primary browser for extension development. This reloads the
// EXISTING unpacked Yanzi extension in-place and never installs a duplicate.
await taskSpace('Yanzi extension development');
const before=await cdp('Extensions.getExtensions',{},null);
const list=before.extensions||before.result?.extensions||[];
const yanzi=list.find(x=>/燕子浏览器助手|Yanzi Browser Helper/i.test(x.name));
if(!yanzi||!yanzi.enabled)throw Error('Yanzi unpacked extension not installed/enabled in Ego.');
const result=await cdp('Extensions.loadUnpacked',{path:yanzi.path},null);
const extensionId=result.id||result.result?.id;
if(extensionId!==yanzi.id)throw Error('Extension id changed unexpectedly; check for duplicate extension.');
const after=await cdp('Extensions.getExtensions',{},null);
const updated=(after.extensions||after.result?.extensions||[]).find(x=>x.id===yanzi.id);
if(!updated?.enabled)throw Error('Yanzi extension did not stay enabled');
console.log('EGO_YANZI_RELOAD_OK '+JSON.stringify({id:updated.id,version:updated.version,path:updated.path}));