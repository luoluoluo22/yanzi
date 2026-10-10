const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const listeners = {};
const chrome = { webRequest: {
  onBeforeRequest: { addListener: (f) => listeners.before = f },
  onCompleted: { addListener: (f) => listeners.complete = f },
  onErrorOccurred: { addListener: (f) => listeners.error = f }
}};
const context = { chrome, URL, Date, Map, Set };
context.globalThis = context;
vm.runInNewContext(fs.readFileSync(require("node:path").join(__dirname, "../browser-extension/lanzou-network-observer.js"), "utf8"), context);
(async () => {
  let r = await context.handleLanzouNetworkControl({operation:"status"});
  assert.equal(r.enabled, false);
  await context.handleLanzouNetworkControl({operation:"start"});
  listeners.before({requestId:"1",url:"https://pc.woozooo.com/doupload.php?token=forbidden",method:"POST",type:"xmlhttprequest",timeStamp:1,requestBody:{formData:{task:["5"],folder_id:["-1"],password:["sensitive"],sign:["secret"],user:["private"]},raw:[{bytes:"PRIVATE"}]}});
  listeners.complete({requestId:"1",statusCode:200});
  listeners.before({requestId:"2",url:"https://evil.example.com/collect",method:"POST",type:"xmlhttprequest",timeStamp:1,requestBody:{formData:{data:["secret"]}}});
  listeners.complete({requestId:"2",statusCode:201});
  r = await context.handleLanzouNetworkControl({operation:"snapshot"});
  assert.equal(r.events.length, 1);
  assert.equal(r.events[0].host, "pc.woozooo.com");
  assert.deepEqual(Array.from(r.events[0].bodyFields), ["task","folder_id"]);
  assert.equal(r.events[0].taskCode, "5");
  listeners.before({requestId:"3",url:"https://pc.woozooo.com/doupload.php",method:"POST",type:"xmlhttprequest",timeStamp:1,requestBody:{formData:{task:["secret-12345"],vei:["private-token"]}}});
  listeners.complete({requestId:"3",statusCode:200});
  r = await context.handleLanzouNetworkControl({operation:"snapshot"});
  assert.equal(r.events[1].taskCode, null);
  assert.equal(JSON.stringify(r).includes("forbidden"), false);
  assert.equal(JSON.stringify(r).includes("sensitive"), false);
  assert.equal(JSON.stringify(r).includes("PRIVATE"), false);
  await context.handleLanzouNetworkControl({operation:"stop"});
  assert.equal((await context.handleLanzouNetworkControl({operation:"status"})).enabled, false);
  console.log("LANZOU_OBSERVER_TESTS=PASSED");
})().catch(err=>{console.error(err);process.exitCode=1});
