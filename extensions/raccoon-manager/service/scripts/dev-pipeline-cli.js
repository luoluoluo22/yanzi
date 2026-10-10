import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { config } from "../src/config.js";

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const action = process.argv[2] || "full";
const runId = process.argv[process.argv.indexOf("--run-id") + 1];
const client = new Client({ name:"raccoon-dev-cli", version:"1" });
async function call(name, args) {
  const response = await client.callTool({name,arguments:args});
  const text = response.content?.find(x=>x.type==="text")?.text;
  if (response.isError) throw new Error(text || "MCP request failed");
  return JSON.parse(text);
}
try {
  await client.connect(new StreamableHTTPClientTransport(new URL(`http://${config.host.includes(":") ? `[${config.host}]` : config.host}:${config.port}/mcp`), {requestInit:{headers:{Authorization:`Bearer ${config.token}`}}}));
  let report;
  if (["status","cancel"].includes(action)) {
    if (!process.argv.includes("--run-id") || !runId) throw new Error("--run-id is required");
    report = await call(action === "status" ? "dev_pipeline_status" : "dev_pipeline_cancel",{runId});
  } else {
    let pipeline = JSON.parse(await fs.readFile(path.join(project,"pipelines/raccoon-full.json"),"utf8"));
    if (action !== "full") {
      const id = {check:"syntax",test:"unit",http:"http"}[action];
      if (!id) throw new Error(`Unknown action: ${action}`);
      pipeline = {...pipeline,name:`Raccoon ${action}`,nodes:pipeline.nodes.filter(n=>n.id===id)};
    }
    pipeline.cwd = project;
    report = await call("dev_pipeline_start",{pipeline});
  }
  console.log(JSON.stringify(report,null,2));
  if (process.argv.includes("--wait")) {
    while (report.status === "running" || report.nodes.some(n=>n.status === "running")) {
      report = await call("dev_pipeline_wait",{runId:report.runId,afterRevision:report.revision,waitMs:30000});
    }
    console.log(JSON.stringify(report,null,2));
    if (report.status !== "succeeded") process.exitCode = 1;
  }
} catch (error) { console.error(error.message); process.exitCode = 1; }
finally { await client.close(); }
