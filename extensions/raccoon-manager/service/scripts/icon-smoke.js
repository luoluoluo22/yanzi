import fs from "node:fs/promises";
import path from "node:path";
import { loadImage } from "@napi-rs/canvas";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const root = process.cwd();
const sizes = [16,24,32,48,64,128,256];

for (const size of sizes) {
  const file = path.join(root, "assets", `raccoon-${size}.png`);
  const image = await loadImage(await fs.readFile(file));
  if (image.width !== size || image.height !== size) throw new Error(`invalid icon size: ${file}`);
}

const ico = await fs.readFile(path.join(root, "assets", "raccoon.ico"));
if (ico.readUInt16LE(0) !== 0 || ico.readUInt16LE(2) !== 1 || ico.readUInt16LE(4) !== sizes.length) {
  throw new Error("invalid ICO header");
}

const transport = new StdioClientTransport({
  command: process.execPath,
  args: ["src/index.js"],
  env: { ...process.env, RACCOON_ROOT: root, RACCOON_TRANSPORT: "stdio" },
  cwd: root,
  stderr: "pipe"
});
const client = new Client({ name: "raccoon-icon-smoke", version: "0.7.0" });

try {
  await client.connect(transport);
  const info = client.getServerVersion();
  if (info?.name !== "raccoon-mcp") throw new Error("internal MCP name changed unexpectedly");
  if (info?.title !== "Raccoon") throw new Error("display title is not Raccoon");
  if (!info?.icons?.length) throw new Error("MCP icon metadata missing");
  const icon = info.icons[0];
  if (icon.mimeType !== "image/png" || !icon.src?.startsWith("data:image/png;base64,")) throw new Error("invalid MCP icon metadata");
  console.log(JSON.stringify({ ok:true, title:info.title, name:info.name, iconSizes:icon.sizes, pngSizes:sizes, icoEntries:sizes.length }, null, 2));
} finally {
  await client.close().catch(() => {});
}
