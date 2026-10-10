import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { createCanvas, loadImage } from "@napi-rs/canvas";
import { insideRoot } from "./config.js";

const execFileAsync = promisify(execFile);

function psQuote(value) {
  return "'" + String(value).replaceAll("'", "''") + "'";
}

export async function captureDesktop({ monitorIndex, savePath }) {
  if (process.platform !== "win32") {
    throw new Error("Desktop screenshot capture is currently implemented for Windows.");
  }

  const temp = path.join(os.tmpdir(), `raccoon-screen-${randomUUID()}.png`);
  const indexExpr = monitorIndex == null ? "$null" : String(monitorIndex);
  const script = [
    "Add-Type -AssemblyName System.Windows.Forms",
    "Add-Type -AssemblyName System.Drawing",
    `$screens = [System.Windows.Forms.Screen]::AllScreens`,
    `$idx = ${indexExpr}`,
    "$bounds = if($null -eq $idx){ [System.Windows.Forms.SystemInformation]::VirtualScreen } else {",
    "  if($idx -lt 0 -or $idx -ge $screens.Length){ throw 'Invalid monitorIndex' }",
    "  $screens[$idx].Bounds",
    "}",
    "$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height",
    "$g = [System.Drawing.Graphics]::FromImage($bmp)",
    "try {",
    "  $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)",
    `  $bmp.Save(${psQuote(temp)}, [System.Drawing.Imaging.ImageFormat]::Png)`,
    "  Write-Output ($bounds.X.ToString() + ',' + $bounds.Y.ToString() + ',' + $bounds.Width.ToString() + ',' + $bounds.Height.ToString())",
    "} finally {",
    "  $g.Dispose()",
    "  $bmp.Dispose()",
    "}"
  ].join("; ");

  const { stdout } = await execFileAsync("powershell.exe", [
    "-NoLogo", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script
  ], {
    windowsHide: true,
    timeout: 60000,
    maxBuffer: 1024 * 1024,
    encoding: "utf8"
  });

  const buffer = await fs.readFile(temp);
  await fs.unlink(temp).catch(() => {});
  const [x, y, width, height] = stdout.trim().split(",").map(Number);

  let savedPath = null;
  if (savePath) {
    const target = insideRoot(savePath);
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, buffer);
    savedPath = target;
  }

  return {
    buffer,
    bytes: buffer.length,
    bounds: { x, y, width, height },
    monitorIndex: monitorIndex ?? null,
    savedPath
  };
}

export async function compareImages({ beforePath, afterPath, threshold = 24, diffSavePath }) {
  const beforeTarget = insideRoot(beforePath);
  const afterTarget = insideRoot(afterPath);
  const [beforeImage, afterImage] = await Promise.all([
    loadImage(beforeTarget),
    loadImage(afterTarget)
  ]);

  const width = Math.min(beforeImage.width, afterImage.width);
  const height = Math.min(beforeImage.height, afterImage.height);
  if (!width || !height) throw new Error("Images have invalid dimensions.");

  const canvasA = createCanvas(width, height);
  const canvasB = createCanvas(width, height);
  const ctxA = canvasA.getContext("2d");
  const ctxB = canvasB.getContext("2d");
  ctxA.drawImage(beforeImage, 0, 0, width, height);
  ctxB.drawImage(afterImage, 0, 0, width, height);
  const a = ctxA.getImageData(0, 0, width, height).data;
  const b = ctxB.getImageData(0, 0, width, height).data;

  let changedPixels = 0;
  let totalDelta = 0;
  let maxDelta = 0;
  let minX = width, minY = height, maxX = -1, maxY = -1;

  let diffCanvas = null;
  let diffCtx = null;
  let diff = null;
  if (diffSavePath) {
    diffCanvas = createCanvas(width, height);
    diffCtx = diffCanvas.getContext("2d");
    diff = diffCtx.createImageData(width, height);
  }

  for (let i = 0, pixel = 0; i < a.length; i += 4, pixel++) {
    const dr = Math.abs(a[i] - b[i]);
    const dg = Math.abs(a[i + 1] - b[i + 1]);
    const db = Math.abs(a[i + 2] - b[i + 2]);
    const delta = Math.max(dr, dg, db);
    totalDelta += (dr + dg + db) / 3;
    maxDelta = Math.max(maxDelta, delta);

    if (delta >= threshold) {
      changedPixels++;
      const x = pixel % width;
      const y = Math.floor(pixel / width);
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }

    if (diff) {
      diff.data[i] = delta;
      diff.data[i + 1] = 0;
      diff.data[i + 2] = 0;
      diff.data[i + 3] = 255;
    }
  }

  let savedDiffPath = null;
  if (diff && diffSavePath) {
    diffCtx.putImageData(diff, 0, 0);
    const target = insideRoot(diffSavePath);
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, diffCanvas.toBuffer("image/png"));
    savedDiffPath = target;
  }

  const totalPixels = width * height;
  return {
    before: { path: beforePath, width: beforeImage.width, height: beforeImage.height },
    after: { path: afterPath, width: afterImage.width, height: afterImage.height },
    compared: { width, height, pixels: totalPixels },
    threshold,
    changedPixels,
    changedRatio: changedPixels / totalPixels,
    meanChannelDelta: totalDelta / totalPixels,
    maxChannelDelta: maxDelta,
    changedBounds: changedPixels
      ? { x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1 }
      : null,
    diffSavePath: savedDiffPath
  };
}
