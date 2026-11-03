const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const script = fs.readFileSync(path.join(__dirname, "../browser-extension/opencli-store-onboarding.js"),"utf8");

function setup(status, options = {}) {
  let listener;
  const created = [], updated = [], focused = [], saves = [];
  let saved = options.lastOpenedAt || 0;
  const store = "https://chromewebstore.google.com/detail/opencli/ildkmabpimmkaediidaifkhjpohdnifk";
  const chrome = {
    runtime: {id:"yanzi-helper",onMessage:{addListener:fn=>listener=fn}},
    tabs: {
      query:async()=>options.tabs || [],
      create:async opts => {created.push(opts);return {id:42}},
      update:async (id,opts)=> {updated.push({id,opts});return {id}}
    },
    windows: { update:async (id,opts)=> {focused.push({id,opts});return {id}} },
    storage: {local: {
      get:async()=>({opencliInstallGuideOpenedAt:saved}),
      set:async val=>{saves.push(val);saved=val.opencliInstallGuideOpenedAt}
    }}
  };
  const context = {console,chrome,Date,
    yanziBrowserHost:{isConnected:()=>true},
    yanziOpenCliCheckExtension:async()=>({ok:true,data:{Status:status}})};
  vm.createContext(context);
  vm.runInContext(script,context,{filename:"opencli-store-onboarding.js"});
  return {context,listener:()=>listener,created,updated,focused,saves,store};
}
test("missing extension returns the exact verified official store listing",async()=>{
  const {context,store}=setup("missing");
  const x=await context.yanziOpenCliStoreOnboarding.preflight();
  assert.equal(x.ok,false);assert.equal(x.needsInstall,true);
  assert.equal(x.code,"opencli_extension_missing");assert.equal(x.storeUrl,store);
});
test("registered and disabled extension never triggers reinstall",async()=>{
  for(const status of ["installed","disabled","unknown"]){
    const {context,created}=setup(status);
    const result=await context.yanziOpenCliStoreOnboarding.preflight();
    if(status==="disabled"){assert.equal(result.needsEnable,true);assert.equal(result.needsInstall,false)}
    else assert.equal(result.ok,true);
    assert.equal(created.length,0);
  }
});
test("missing extension is not inferred from disconnected Yanzi host",async()=>{
  const {context}=setup("missing");
  context.yanziBrowserHost.isConnected=()=>false;
  const r=await context.yanziOpenCliStoreOnboarding.preflight();
  assert.equal(r.ok,true);
  assert.equal(r.status,"unknown");
});
test("explicit user action opens official store in existing browser once",async()=>{
  const {context,created,store,saves,listener}=setup("missing");
  const r=await new Promise(resolve=>{const ret=listener()({action:"yanzi_opencli_open_store"},
    {id:"yanzi-helper"}, resolve);assert.equal(ret,true)});
  assert.equal(r.ok,true);assert.equal(r.opened,true);
  assert.equal(created.length,1);
  assert.equal(created[0].url,store);
  assert.equal(created[0].active,true);
  assert.equal(saves.length,1);
  const again=await context.yanziOpenCliStoreOnboarding.openStore();
  assert.equal(again.reason,"cooldown");
  assert.equal(created.length,1);
});
test("reuses existing store tab and rejects external-extension requests",async()=>{
  const {context,created,updated,focused,listener}=setup("missing",{tabs:[{id:7,windowId:8}]});
  const opened=await context.yanziOpenCliStoreOnboarding.openStore();
  assert.equal(opened.reason,"existing-tab");
  assert.equal(created.length,0);assert.equal(updated[0].id,7);assert.equal(focused[0].id,8);
  let reply;listener()({action:"yanzi_opencli_open_store"},{id:"another"},r=>reply=r);
  assert.equal(reply.ok,false);
  assert.equal(created.length,0);
});
