import fs from "node:fs/promises";
import path from "node:path";
import { createCanvas, loadImage } from "@napi-rs/canvas";

const root = process.cwd();
const assets = path.join(root, "assets");
const sourcePath = path.join(assets, "raccoon.png");
const sizes = [16, 24, 32, 48, 64, 128, 256];

const source = await fs.readFile(sourcePath);
const image = await loadImage(source);
if (image.width !== image.height) throw new Error("Raccoon icon source must be square.");

const pngs = [];
for (const size of sizes) {
  const canvas = createCanvas(size, size);
  const ctx = canvas.getContext("2d");
  ctx.imageSmoothingEnabled = true;
  ctx.imageSmoothingQuality = "high";
  ctx.drawImage(image, 0, 0, size, size);
  const png = canvas.toBuffer("image/png");
  const file = path.join(assets, `raccoon-${size}.png`);
  await fs.writeFile(file, png);
  pngs.push({ size, png });
}

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(pngs.length, 4);

const entries = Buffer.alloc(16 * pngs.length);
let offset = 6 + entries.length;
pngs.forEach(({ size, png }, index) => {
  const base = index * 16;
  entries[base] = size === 256 ? 0 : size;
  entries[base + 1] = size === 256 ? 0 : size;
  entries[base + 2] = 0;
  entries[base + 3] = 0;
  entries.writeUInt16LE(1, base + 4);
  entries.writeUInt16LE(32, base + 6);
  entries.writeUInt32LE(png.length, base + 8);
  entries.writeUInt32LE(offset, base + 12);
  offset += png.length;
});

await fs.writeFile(
  path.join(assets, "raccoon.ico"),
  Buffer.concat([header, entries, ...pngs.map(item => item.png)])
);

console.log(JSON.stringify({
  source: path.relative(root, sourcePath),
  dimensions: [image.width, image.height],
  generated: sizes.map(size => `assets/raccoon-${size}.png`),
  ico: "assets/raccoon.ico"
}, null, 2));
