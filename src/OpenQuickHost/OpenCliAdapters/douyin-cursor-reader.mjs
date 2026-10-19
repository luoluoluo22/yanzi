// Read-only Douyin cursor adapter. OpenCLI 1.8.6's --page uses page_num,
// but the work_list backend actually advances with max_cursor.
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { join } from "node:path";
import { existsSync } from "node:fs";

const exec = promisify(execFile);
const page = Number(process.argv[2]);
if (!Number.isInteger(page) || page < 1 || page > 20) {
  console.error("invalid_page");
  process.exit(2);
}
const opencli = join(process.env.APPDATA || "", "npm", "node_modules",
  "@jackwener", "opencli", "dist", "src", "main.js");
if (!existsSync(opencli)) {
  console.error("OpenCLI executable is not installed");
  process.exit(2);
}
const session = "yanzi-douyin-reader-" + process.pid;
async function run(args, timeout = 12000) {
  const { stdout } = await exec(process.execPath, [opencli, "browser", session, ...args], {
    windowsHide: true, shell: false, timeout, maxBuffer: 1024 * 1024, encoding: "utf8"
  });
  return stdout.trim();
}
function makeReadScript(pageNumber) {
  return [
    "(async () => {",
    "const limit=20, target=" + pageNumber + "*limit, seen=new Set(), collected=[];",
    "let cursor=null, hasMore=true, total=null, duplicateCount=0, scannedRequests=0;",
    "const visitedCursors=new Set();",
    "while(hasMore && collected.length<target && scannedRequests<85) {",
    "  const url=new URL('https://creator.douyin.com/janus/douyin/creator/pc/work_list');",
    "  url.searchParams.set('page_size',String(limit)); url.searchParams.set('status','0');",
    "  if(cursor!==null)url.searchParams.set('max_cursor',String(cursor));",
    "  const response=await fetch(url.href,{credentials:'include'});",
    "  if(!response.ok)throw new Error('HTTP '+response.status);",
    "  const payload=await response.json();",
    "  if(payload.status_code!==0)throw new Error('Creator API code '+payload.status_code);",
    "  const list=payload.aweme_list || payload.data?.work_list || [];",
    "  if(!Array.isArray(list))throw new Error('Unexpected Douyin response shape');",
    "  scannedRequests++; if(Number.isInteger(payload.total))total=payload.total;",
    "  for(const v of list) {",
    "    const id=String(v.aweme_id||'');",
    "    if(!id||seen.has(id)){duplicateCount++;continue;}",
    "    seen.add(id); const s=v.status;",
    "    const status=typeof s==='number'?s:s?.is_delete?'deleted':",
    "      s?.is_prohibited?'prohibited':s?.in_reviewing?'reviewing':",
    "      s?.is_private?'private':v.public_time&&v.public_time>Date.now()/1000?'scheduled':'published';",
    "    const timestamp=(v.create_time??v.public_time??0)*1000;",
    "    collected.push({aweme_id:id,title:v.desc??'',status,",
    "      play_count:v.statistics?.play_count??0,digg_count:v.statistics?.digg_count??0,",
    "      create_time:new Date(timestamp).toLocaleString('zh-CN',{timeZone:'Asia/Shanghai'})});",
    "  }",
    "  hasMore=payload.has_more===true;",
    "  if(!hasMore||list.length===0)break;",
    "  const next=payload.max_cursor;",
    "  if(next==null||String(next)===String(cursor)||visitedCursors.has(String(next)))",
    "    throw new Error('Cursor did not advance; refusing duplicate pages');",
    "  visitedCursors.add(String(next));cursor=next;",
    "}",
    "if(collected.length<target && hasMore)throw new Error('Cursor pagination incomplete');",
    "const start=(" + pageNumber + "-1)*limit;",
    "return {items:collected.slice(start,start+limit),page:" + pageNumber + ",page_size:limit,total,",
    "  has_more:collected.length>start+limit||hasMore,scanned_requests:scannedRequests,",
    "  duplicate_ids_dropped:duplicateCount};",
    "})()"
  ].join("\n");
}
let opened=false;
try {
  await run(["open", "https://creator.douyin.com"], 25000);
  opened=true;
  const raw=await run(["eval",makeReadScript(page)],95000);
  const data=JSON.parse(raw);
  if(!Array.isArray(data.items)||data.page!==page)throw new Error("Unexpected pagination output");
  process.stdout.write(JSON.stringify(data));
} catch(error) {
  console.error("Douyin cursor read failed: "+String(error?.message||"unknown").slice(0,230));
  process.exitCode=1;
} finally {
  if(opened) {
    try { await run(["tab","close"],8000); } catch {}
  }
}
