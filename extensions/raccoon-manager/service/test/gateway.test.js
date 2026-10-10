import test from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { ServiceGateway } from "../src/service-gateway.js";

async function fixture() {
  let completeLong;
  let ready;
  const longReady=new Promise(resolve=>{ready=resolve;});
  const old=http.createServer((req,res)=>{
    if(req.url === "/long") {completeLong=()=>res.end("old-complete");ready();}
    else res.end("old");
  });
  const candidate=http.createServer((_req,res)=>res.end("new"));
  const listen=server=>new Promise(resolve=>server.listen(0,"127.0.0.1",resolve));
  await listen(old);await listen(candidate);
  const retired=[];
  const gateway=new ServiceGateway({current:{id:"old",port:old.address().port},token:"gateway-test",
    prepare:async()=>({id:"new",port:candidate.address().port}),probe:async()=>({busy:false}),release:async()=>{},activate:async()=>{},retire:async worker=>{retired.push(worker.id);}});
  await listen(gateway.server);
  const base=`http://127.0.0.1:${gateway.server.address().port}`;
  return {gateway,base,retired,longReady,complete:()=>completeLong?.(),close:async()=>{
    completeLong?.();
    for(const server of [gateway.server,old,candidate]) {server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
  }};
}

test("deployment defers without interrupting an active request",async()=>{
  const f=await fixture();
  try {
    const request=fetch(f.base+"/long").then(r=>r.text());
    await f.longReady;
    await assert.rejects(f.gateway.deploy({}),/deferred/);
    assert.equal(f.gateway.current.id,"old");
    assert.deepEqual(f.retired,["new"]);
    f.complete();assert.equal(await request,"old-complete");
  }finally{await f.close();}
});
test("busy background state preserves current worker; failed warmup preserves it too",async()=>{
  const f=await fixture();
  try {
    f.gateway.probe=async()=>({busy:true,activePipelines:1});
    await assert.rejects(f.gateway.deploy({}),/deferred/);
    assert.equal(await (await fetch(f.base)).text(),"old");
    f.gateway.prepare=async()=>{throw new Error("candidate validation failed");};
    await assert.rejects(f.gateway.deploy({}),/validation failed/);
    assert.equal(f.gateway.current.id,"old");
  }finally{await f.close();}
});
test("switch buffers new requests briefly and forwards them to validated worker",async()=>{
  const f=await fixture();
  try {
    let unlock;
    f.gateway.release=()=>new Promise(resolve=>{unlock=resolve;});
    const deployment=f.gateway.deploy({});
    for(let i=0;i<100 && !unlock;i++) await new Promise(resolve=>setTimeout(resolve,5));
    const request=fetch(f.base).then(r=>r.text());
    await new Promise(resolve=>setTimeout(resolve,20));
    unlock();await deployment;
    assert.equal(await request,"new");
    assert.equal(f.gateway.current.id,"new");
    assert.deepEqual(f.retired,["old"]);
  }finally{await f.close();}
});
test("gateway control requires local Host and bearer token",async()=>{
  const f=await fixture();
  try {
    assert.equal((await fetch(f.base+"/__raccoon/status")).status,404);
    const publicStatus=await new Promise((resolve,reject)=>{
      const request=http.get(f.base+"/__raccoon/status",{headers:{Authorization:"Bearer gateway-test",Host:"public.example"}},response=>{response.resume();resolve(response.statusCode);});
      request.on("error",reject);
    });
    assert.equal(publicStatus,404);
    assert.equal((await fetch(f.base+"/__raccoon/status",{headers:{Authorization:"Bearer gateway-test"}})).status,200);
  }finally{await f.close();}
});
