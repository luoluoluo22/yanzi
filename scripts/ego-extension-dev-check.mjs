// Run from Ego's Node.js runtime:
// ego-browser nodejs -e "await import('file:///F:/Desktop/kaifa/OpenQuickHost/scripts/ego-extension-dev-check.mjs')"
const {fileURLToPath}=await import('node:url');
const path=await import('node:path');
const fs=await import('node:fs');
const root=path.dirname(path.dirname(fileURLToPath(import.meta.url)));
await taskSpace('Yanzi extension development');
const current=await cdp('Extensions.getExtensions',{},null);
const extensions=current.extensions||current.result?.extensions||[];
const normalize=p=>path.resolve(p).toLowerCase().replaceAll('/','\\');
const yanzi=extensions.find(e=>/Yanzi Browser Helper|燕子浏览器助手/i.test(e.name));
// Follow the already installed source; never load a second unpacked build
// just because this script is executed from another Git worktree.
const source=yanzi?.path||path.join(root,'browser-extension');
const manifest=JSON.parse(fs.readFileSync(path.join(source,'manifest.json'),'utf8'));
const opencli=extensions.find(e=>e.name==='OpenCLI');
if(!yanzi){console.log('YANZI_MISSING_EXTENSION');}
else if(normalize(yanzi.path)!==normalize(source)){console.log('YANZI_SOURCE_MISMATCH '+JSON.stringify({expected:source,actual:yanzi.path}));}
else if(!yanzi.enabled){console.log('YANZI_EXTENSION_DISABLED '+yanzi.id);}
else if(process.env.YANZI_EGO_RELOAD==='1'){
 const result=await cdp('Extensions.loadUnpacked',{path:source},null);
 console.log('YANZI_RELOADED '+JSON.stringify({id:result.id||result.result?.id,version:manifest.version}));
}else console.log('YANZI_READY '+JSON.stringify({id:yanzi.id,version:yanzi.version,source:yanzi.path}));
console.log('OPENCLI_'+(opencli?.enabled?'READY':'NOT_READY')+' '+JSON.stringify(opencli?{id:opencli.id,version:opencli.version}:{}));
