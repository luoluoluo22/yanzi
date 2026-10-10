import fs from "node:fs";
import path from "node:path";
import { randomUUID, createHash } from "node:crypto";

const root=process.cwd(),id=randomUUID();
const directory=path.join(root,".raccoon-runtime/releases",id);
fs.mkdirSync(directory,{recursive:true});
for(const name of ["src","assets","package.json","package-lock.json","node_modules"]) fs.cpSync(path.join(root,name),path.join(directory,name),{recursive:true,dereference:true});
const hashes={};
for(const file of fs.readdirSync(path.join(directory,"src"))) if(file.endsWith(".js")) {
  const name=`src/${file}`;hashes[name]=createHash("sha256").update(fs.readFileSync(path.join(directory,name))).digest("hex");
}
hashes["package.json"]=createHash("sha256").update(fs.readFileSync(path.join(directory,"package.json"))).digest("hex");
fs.writeFileSync(path.join(directory,"release.json"),JSON.stringify({id,version:JSON.parse(fs.readFileSync(path.join(directory,"package.json"))).version,hashes}));
console.log(JSON.stringify({releaseId:id,directory}));
