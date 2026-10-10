import fs from "node:fs";
import { spawnSync } from "node:child_process";
for (const dir of ["src", "scripts", "test"]) {
  for (const file of fs.readdirSync(dir).filter(f => f.endsWith(".js"))) {
    const result = spawnSync(process.execPath, ["--check", `${dir}/${file}`], { stdio: "inherit" });
    if (result.status !== 0) process.exit(result.status || 1);
  }
}
