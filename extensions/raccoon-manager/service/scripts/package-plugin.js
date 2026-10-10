import fs from "node:fs";
import path from "node:path";
import AdmZip from "adm-zip";
import { loadImage } from "@napi-rs/canvas";

const root=process.cwd();
const portable=JSON.parse(fs.readFileSync(path.join(root,"plugin.json"),"utf8"));
const presentation=portable.extensions["com.openai"];
const compatibility={name:portable.name,version:portable.version,description:portable.description,author:portable.author,license:portable.license,...presentation};
const zip=new AdmZip();
zip.addFile("plugin.json",Buffer.from(JSON.stringify(portable,null,2)));
zip.addFile(".codex-plugin/plugin.json",Buffer.from(JSON.stringify(compatibility,null,2)));
zip.addLocalFile(path.join(root,".app.json"));
for(const field of ["composerIcon","logo"]) {
  const relative=presentation.interface[field].replace(/^\.\//,"");
  if(!relative.startsWith("assets/") || relative.includes(".."))throw new Error("Invalid asset path");
  const file=path.join(root,relative),image=await loadImage(file);
  if(image.width!==image.height)throw new Error("Plugin icons must be square");
  if(!zip.getEntry(relative))zip.addFile(relative,fs.readFileSync(file));
}
zip.addFile("README.md",Buffer.from("# Raccoon\n\nAI 开发工作台。插件包携带名称和图标，引用已注册的 MCP 应用，不包含服务器密钥或项目文件。\n\n现有远程插件需通过支持的插件编辑/包更新流程应用这些元数据；仅刷新 MCP 工具不会保证图标更新。\n"));
const runtime=path.join(root,".raccoon-runtime");fs.mkdirSync(runtime,{recursive:true});
const output=path.join(runtime,"raccoon-plugin.zip");zip.writeZip(output);
const entries=zip.getEntries().map(e=>e.entryName);
if(entries.some(name=>name.includes(".env") || name.includes("token")))throw new Error("Unexpected private file in plugin package");
console.log(JSON.stringify({ok:true,output,displayName:presentation.interface.displayName,entries},null,2));
