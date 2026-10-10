import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { refreshAccountDevices, cachedAccountDevices, invokeAccountDevice,
         resetAccountDeviceCacheForTest } from "../src/yanzi-federation.js";
import { callPeerToolByDeviceId, resolveDeviceSelector } from "../src/peer-manager.js";

const remoteId = "desktop-" + "e".repeat(32);
const ownId = "desktop-" + "f".repeat(32);
function row(id,name,online=true,platform="desktop") {
  return { deviceId:id, displayName:name, online, platform };
}
test("account-scoped discovery skips local host and non-Windows devices", async () => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), "yanzi-peer-test-"));
  const previous = process.env.YANZI_AGENT_SETTINGS_FILE;
  const originalFetch = global.fetch;
  try {
    const settings = path.join(temp, "settings.json");
    await fs.writeFile(settings, JSON.stringify({agentApiPort:12345,agentApiToken:"test-only"}));
    process.env.YANZI_AGENT_SETTINGS_FILE = settings;
    global.fetch = async (url,options) => {
      assert.equal(new URL(url).hostname,"127.0.0.1");
      assert.equal(options.headers["X-Yanzi-Token"],"test-only");
      return new Response(JSON.stringify({items:[
        row(ownId,"Windows · "+os.hostname()),row(remoteId,"Windows · NEW-LAPTOP"),
        row("android-" + "b".repeat(32),"Android",true,"android")
      ]}),{status:200,headers:{"Content-Type":"application/json"}});
    };
    resetAccountDeviceCacheForTest();
    const discovered = await refreshAccountDevices(true);
    assert.equal(discovered.length,1);
    assert.equal(discovered[0].id,remoteId);
    assert.equal(discovered[0].source,"yanzi");
    assert.equal(discovered[0].status,"online");
    assert.deepEqual(cachedAccountDevices(), discovered);

    global.fetch = async () => new Response("unauthorized",{status:401});
    assert.deepEqual(await refreshAccountDevices(true),[]);
  } finally {
    if(previous===undefined) delete process.env.YANZI_AGENT_SETTINGS_FILE;
    else process.env.YANZI_AGENT_SETTINGS_FILE = previous;
    global.fetch = originalFetch;
    resetAccountDeviceCacheForTest();
    await fs.rm(temp,{recursive:true,force:true});
  }
});

test("account relay transparently unwraps verified MCP tool results", async () => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), "yanzi-peer-call-"));
  const previous = process.env.YANZI_AGENT_SETTINGS_FILE;
  const originalFetch = global.fetch;
  let count=0;
  try {
    process.env.YANZI_AGENT_SETTINGS_FILE=path.join(temp,"settings.json");
    await fs.writeFile(process.env.YANZI_AGENT_SETTINGS_FILE,JSON.stringify({agentApiPort:23456,agentApiToken:"fake-token"}));
    global.fetch=async (url,options) => {
      count++;
      if(url.endsWith("/v1/me/devices")) return Response.json({items:[row(remoteId,"Windows · OFFICE")]});
      assert.equal(url.endsWith("/v1/capabilities/invoke"),true);
      const body=JSON.parse(options.body);
      assert.equal(body.name,"device.raccoon.invoke");
      assert.equal(body.payload.target,remoteId);
      assert.equal(body.payload.name,"ping");
      const mcp={content:[{type:"text",text:"pong"}],isError:false};
      return Response.json({success:true,data:{success:true,status:"completed",
        result:{output:JSON.stringify({success:true,data:mcp})}}});
    };
    resetAccountDeviceCacheForTest();
    const result=await invokeAccountDevice(remoteId,"ping",{});
    assert.equal(count,2);
    assert.deepEqual(result,{content:[{type:"text",text:"pong"}],isError:false});
    assert.deepEqual(await callPeerToolByDeviceId("Windows · OFFICE", "ping", {}),result);
    assert.equal(resolveDeviceSelector(remoteId), remoteId);
    global.fetch=async (url) => url.endsWith("/v1/me/devices")
      ? Response.json({items:[row(remoteId,"Windows · OFFICE",false)]})
      : Response.json({success:true});
    await assert.rejects(invokeAccountDevice(remoteId,"ping",{}),/offline/);
  } finally {
    if(previous===undefined) delete process.env.YANZI_AGENT_SETTINGS_FILE;
    else process.env.YANZI_AGENT_SETTINGS_FILE=previous;
    global.fetch=originalFetch;
    resetAccountDeviceCacheForTest();
    await fs.rm(temp,{recursive:true,force:true});
  }
});
