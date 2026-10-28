const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const replies = [];
const outgoing = [];
let requestCount = 0;

class SafeForm {
  constructor() { this.items = new Map(); }
  append(key, value) { this.items.set(key, value); }
}
const chrome = {runtime:{
  onMessage:{addListener(){}},
  sendMessage(item){replies.push(item);}
}};
const ctx = {
  chrome, location:{hostname:"pc.woozooo.com",protocol:"https:"},
  Uint8Array, Blob, FormData:SafeForm,
  AbortController, setTimeout, clearTimeout,
  atob(text){return Buffer.from(text,"base64").toString("binary");},
  async fetch(url, options) {
    requestCount++;
    outgoing.push({url, options});
    return {status:200,text:async()=>JSON.stringify({
      zt:1,info:"success",text:[{id:323,name_all:"yanzi-lanzou-proof.zip",pwd:"must-not-leak"}]
    })};
  }
};
ctx.globalThis = ctx;
vm.runInNewContext(fs.readFileSync(path.join(__dirname,"../browser-extension/content.js"),"utf8"),ctx);
(async () => {
  const base64=Buffer.from("small disposable ZIP fixture").toString("base64");
  await ctx.performLanzouUploadFile({taskId:"denied",filename:"yanzi-lanzou-proof.zip",folderId:"123",base64,confirmAction:false});
  assert.equal(replies.at(-1).status,"error");
  assert.equal(requestCount,0);
  await ctx.performLanzouUploadFile({taskId:"traversal",filename:"../../passwords.zip",folderId:"123",base64,confirmAction:true});
  assert.equal(replies.at(-1).status,"error");
  assert.equal(requestCount,0);
  ctx.location.hostname="untrusted.example";
  await ctx.performLanzouUploadFile({taskId:"domain",filename:"yanzi-lanzou-proof.zip",folderId:"123",base64,confirmAction:true});
  assert.equal(replies.at(-1).status,"error");
  assert.equal(requestCount,0);
  ctx.location.hostname="pc.woozooo.com";
  await ctx.performLanzouUploadFile({taskId:"success",filename:"yanzi-lanzou-proof.zip",folderId:"123",base64,confirmAction:true});
  assert.equal(replies.at(-1).status,"success");
  assert.equal(replies.at(-1).data.resultCode,1);
  assert.equal(replies.at(-1).data.items[0].id,"323");
  assert.equal(JSON.stringify(replies.at(-1)).includes("must-not-leak"),false);
  assert.equal(requestCount,1);
  assert.equal(outgoing[0].url,"/html5up.php");
  assert.equal(outgoing[0].options.credentials,"same-origin");
  assert.equal(outgoing[0].options.body.items.get("folder_id"),"123");
  assert.equal(outgoing[0].options.body.items.has("upload_file"),true);
  console.log("LANZOU_UPLOAD_SECURITY_TESTS=PASSED");
})().catch(err=>{console.error(err);process.exitCode=1});
