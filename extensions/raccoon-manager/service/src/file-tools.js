import fs from "node:fs/promises";
import fsSync, { createReadStream } from "node:fs";
import readline from "node:readline";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { randomUUID } from "node:crypto";
import XLSX from "xlsx";
import AdmZip from "adm-zip";
import { PDFDocument } from "pdf-lib";
import { marked } from "marked";
import { Document, Packer, Paragraph, HeadingLevel, TextRun } from "docx";
import { createCanvas, DOMMatrix, ImageData, Path2D } from "@napi-rs/canvas";
import { insideRoot, config } from "./config.js";
import { assertOrdinaryWriteAllowed, isProtectedSource, decodeSource } from './source-integrity.js';
import { getRuntimeSetting } from "./runtime-settings.js";

const execFileAsync = promisify(execFile);
const imageMime = new Map([
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".gif", "image/gif"],
  [".webp", "image/webp"]
]);
const excelExt = new Set([".xlsx", ".xls", ".xlsm"]);
const textLikeExt = new Set([
  ".txt", ".md", ".json", ".jsonl", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
  ".css", ".scss", ".html", ".htm", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg",
  ".cs", ".java", ".kt", ".kts", ".py", ".ps1", ".bat", ".cmd", ".sh", ".sql", ".csv",
  ".log", ".svg", ".gradle", ".properties", ".gitignore", ".gitattributes"
]);

if (!globalThis.DOMMatrix) globalThis.DOMMatrix = DOMMatrix;
if (!globalThis.ImageData) globalThis.ImageData = ImageData;
if (!globalThis.Path2D) globalThis.Path2D = Path2D;

function textResult(value) {
  return {
    content: [{
      type: "text",
      text: typeof value === "string" ? value : JSON.stringify(value, null, 2)
    }]
  };
}

function decodeXml(value) {
  return value
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'")
    .replaceAll("&amp;", "&");
}

function xmlText(xml) {
  return [...xml.matchAll(/<w:t(?:\s[^>]*)?>([\s\S]*?)<\/w:t>/g)]
    .map(m => decodeXml(m[1]))
    .join("");
}

function prettyXml(xml) {
  const normalized = xml.replace(/>\s*</g, "><").replace(/></g, ">\n<");
  const lines = normalized.split("\n");
  let depth = 0;
  return lines.map(line => {
    const trimmed = line.trim();
    if (/^<\//.test(trimmed)) depth = Math.max(0, depth - 1);
    const out = "  ".repeat(depth) + trimmed;
    if (/^<[^!?/][^>]*[^/]>$/.test(trimmed) && !/<\/[^>]+>$/.test(trimmed)) depth += 1;
    return out;
  }).join("\n");
}

function pageSlice(total, offset, length) {
  if (offset < 0) {
    const count = Math.min(-offset, total);
    return { start: Math.max(0, total - count), count };
  }
  const start = Math.min(Math.max(0, offset), total);
  return { start, count: Math.min(Math.max(0, length), total - start) };
}

async function readTextFile(target, offset, length) {
  const input = createReadStream(target, { encoding: "utf8", highWaterMark: 256 * 1024 });
  const rl = readline.createInterface({ input, crlfDelay: Infinity });
  let totalLines = 0;
  let startLine = Math.max(0, offset);
  const selected = [];
  const tail = offset < 0 ? Math.max(1, -offset) : 0;

  try {
    for await (const line of rl) {
      if (tail) {
        selected.push(line);
        if (selected.length > tail) selected.shift();
      } else if (totalLines >= startLine && selected.length < length) {
        selected.push(line);
      }
      totalLines += 1;
    }
  } finally {
    rl.close();
    input.destroy();
  }

  if (tail) startLine = Math.max(0, totalLines - selected.length);
  return textResult({
    path: target,
    type: "text",
    startLine,
    linesReturned: selected.length,
    totalLines,
    remainingLines: Math.max(0, totalLines - (startLine + selected.length)),
    text: selected.join("\n")
  });
}

async function readExcel(target, { sheet, range, offset, length }) {
  const workbook = XLSX.readFile(target, { cellDates: true });
  const sheetNames = workbook.SheetNames;
  let selectedName;
  if (sheet == null || sheet === "") selectedName = sheetNames[0];
  else if (/^\d+$/.test(String(sheet))) selectedName = sheetNames[Number(sheet)];
  else selectedName = String(sheet);
  if (!selectedName || !workbook.Sheets[selectedName]) throw new Error(`Unknown Excel sheet: ${sheet}`);
  const ws = workbook.Sheets[selectedName];
  let rows = XLSX.utils.sheet_to_json(ws, { header: 1, defval: null, raw: false, ...(range ? { range } : {}) });
  const totalRows = rows.length;
  const sliced = pageSlice(totalRows, offset, length);
  rows = rows.slice(sliced.start, sliced.start + sliced.count);
  return textResult({
    path: target,
    type: "excel",
    sheet: selectedName,
    range: range || null,
    offset: sliced.start,
    rowsReturned: rows.length,
    totalRows,
    data: rows
  });
}

async function readImage(target, mime) {
  const buffer = await fs.readFile(target);
  return {
    content: [
      { type: "text", text: `Image: ${target} (${buffer.length} bytes)` },
      { type: "image", data: buffer.toString("base64"), mimeType: mime }
    ]
  };
}

async function loadPdfDocument(buffer) {
  const pdfjs = await import("pdfjs-dist/legacy/build/pdf.mjs");
  return pdfjs.getDocument({
    data: new Uint8Array(buffer),
    useWorkerFetch: false,
    isEvalSupported: false,
    useSystemFonts: true
  }).promise;
}

async function readPdfBuffer(buffer, label, { offset, length, options }) {
  const doc = await loadPdfDocument(buffer);
  const totalPages = doc.numPages;
  const sliced = pageSlice(totalPages, offset, length);
  const pages = [];
  const images = [];
  const includeImages = options?.includeImages !== false;
  const renderLimit = Math.min(sliced.count, Number(options?.maxRenderedPages || 8));

  for (let index = 0; index < sliced.count; index++) {
    const pageIndex = sliced.start + index;
    const page = await doc.getPage(pageIndex + 1);
    const tc = await page.getTextContent();
    const text = tc.items.map(item => item.str || "").join(" ").replace(/\s+/g, " ").trim();
    pages.push(`## Page ${pageIndex + 1}\n\n${text}`);

    if (includeImages && index < renderLimit) {
      try {
        const baseViewport = page.getViewport({ scale: 1 });
        const maxWidth = Number(options?.imageMaxWidth || 1400);
        const scale = Math.min(2, Math.max(0.75, maxWidth / Math.max(1, baseViewport.width)));
        const viewport = page.getViewport({ scale });
        const canvas = createCanvas(Math.ceil(viewport.width), Math.ceil(viewport.height));
        const ctx = canvas.getContext("2d");
        await page.render({ canvasContext: ctx, viewport }).promise;
        images.push({
          type: "image",
          data: canvas.toBuffer("image/png").toString("base64"),
          mimeType: "image/png"
        });
      } catch (error) {
        pages.push(`\n> Page render warning: ${error.message}`);
      }
    }
  }
  if (typeof doc.destroy === "function") await doc.destroy().catch(() => {});

  const content = [{
    type: "text",
    text: [
      `PDF: ${label}`,
      `Pages: ${totalPages}; reading ${sliced.start + 1}-${sliced.start + sliced.count}`,
      "",
      pages.join("\n\n")
    ].join("\n")
  }];
  content.push(...images);
  if (includeImages && sliced.count > renderLimit) {
    content.push({
      type: "text",
      text: `Rendered the first ${renderLimit} requested pages as images; text was extracted for all requested pages.`
    });
  }
  return { content };
}

function docxOutline(zip) {
  const entry = zip.getEntry("word/document.xml");
  if (!entry) throw new Error("DOCX is missing word/document.xml");
  const xml = entry.getData().toString("utf8");
  const body = xml.match(/<w:body[^>]*>([\s\S]*?)<\/w:body>/)?.[1] || xml;
  const elements = body.match(/<w:p\b[\s\S]*?<\/w:p>|<w:tbl\b[\s\S]*?<\/w:tbl>/g) || [];
  const outline = [];
  elements.forEach((element, index) => {
    if (element.startsWith("<w:tbl")) {
      const rows = [...element.matchAll(/<w:tr\b[\s\S]*?<\/w:tr>/g)].map(rowMatch =>
        [...rowMatch[0].matchAll(/<w:tc\b[\s\S]*?<\/w:tc>/g)].map(cellMatch => xmlText(cellMatch[0]))
      );
      outline.push({ index, type: "table", rows });
    } else {
      const style = element.match(/<w:pStyle[^>]*w:val="([^"]+)"/)?.[1] || null;
      const images = [...element.matchAll(/<a:blip[^>]*r:embed="([^"]+)"/g)].map(m => m[1]);
      const text = xmlText(element);
      if (text || images.length) outline.push({ index, type: "paragraph", style, text, images });
    }
  });
  const media = zip.getEntries().filter(e => e.entryName.startsWith("word/media/")).map(e => ({
    path: e.entryName,
    bytes: e.header.size
  }));
  return { outline, media, xml };
}

async function readDocx(target, { offset, length }) {
  const zip = new AdmZip(target);
  const { outline, media, xml } = docxOutline(zip);
  if (offset === 0) {
    return textResult({
      path: target,
      type: "docx-outline",
      elements: outline,
      media
    });
  }

  const pretty = prettyXml(xml);
  const lines = pretty.split("\n");
  const sliced = pageSlice(lines.length, offset, length);
  return textResult({
    path: target,
    type: "docx-xml",
    startLine: sliced.start,
    linesReturned: sliced.count,
    totalLines: lines.length,
    xml: lines.slice(sliced.start, sliced.start + sliced.count).join("\n")
  });
}

async function readUrl(url, args) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
  const contentType = (response.headers.get("content-type") || "").split(";")[0].trim().toLowerCase();
  const buffer = Buffer.from(await response.arrayBuffer());
  if (contentType.startsWith("image/")) {
    return {
      content: [
        { type: "text", text: `Image URL: ${url}` },
        { type: "image", data: buffer.toString("base64"), mimeType: contentType }
      ]
    };
  }
  if (contentType === "application/pdf" || new URL(url).pathname.toLowerCase().endsWith(".pdf")) {
    return readPdfBuffer(buffer, url, args);
  }
  return textResult({
    url,
    contentType,
    bytes: buffer.length,
    text: buffer.toString("utf8")
  });
}

export async function readRichFile(args) {
  const offset = Number.isSafeInteger(args.offset) ? args.offset : 0;
  const length = Number.isSafeInteger(args.length) ? args.length : getRuntimeSetting("fileReadLineLimit");
  if (args.isUrl) return readUrl(args.path, { ...args, offset, length });

  const target = insideRoot(args.path);
  if (isProtectedSource(target)) decodeSource(await fs.readFile(target));
  const ext = path.extname(target).toLowerCase();
  if (imageMime.has(ext)) return readImage(target, imageMime.get(ext));
  if (excelExt.has(ext)) return readExcel(target, { ...args, offset, length });
  if (ext === ".pdf") return readPdfBuffer(await fs.readFile(target), target, { ...args, offset, length });
  if (ext === ".docx") return readDocx(target, { offset, length });
  return readTextFile(target, offset, length);
}

export async function readManyRich(paths, concurrency = config.batchConcurrency) {
  const results = new Array(paths.length);
  let next = 0;
  const workers = Array.from({ length: Math.min(concurrency, paths.length) }, async () => {
    while (true) {
      const index = next++;
      if (index >= paths.length) return;
      const item = paths[index];
      try {
        const result = await readRichFile({ path: item, offset: 0, length: getRuntimeSetting("fileReadLineLimit") });
        results[index] = [
          { type: "text", text: `===== ${item} =====` },
          ...result.content
        ];
      } catch (error) {
        results[index] = [{ type: "text", text: `===== ${item} =====\nERROR: ${error.message}` }];
      }
    }
  });
  await Promise.all(workers);
  return { content: results.flat() };
}

function parseMarkdownParagraphs(content) {
  const paragraphs = [];
  for (const rawLine of String(content).split(/\r?\n/)) {
    const line = rawLine.trimEnd();
    if (!line.trim()) {
      paragraphs.push(new Paragraph(""));
      continue;
    }
    const heading = line.match(/^(#{1,3})\s+(.*)$/);
    if (heading) {
      const level = heading[1].length === 1 ? HeadingLevel.HEADING_1 :
        heading[1].length === 2 ? HeadingLevel.HEADING_2 : HeadingLevel.HEADING_3;
      paragraphs.push(new Paragraph({ text: heading[2], heading: level }));
      continue;
    }
    const bullet = line.match(/^[-*]\s+(.*)$/);
    if (bullet) {
      paragraphs.push(new Paragraph({ text: bullet[1], bullet: { level: 0 } }));
      continue;
    }
    paragraphs.push(new Paragraph({ children: [new TextRun(line)] }));
  }
  return paragraphs;
}

async function writeDocx(target, content) {
  const document = new Document({ sections: [{ children: parseMarkdownParagraphs(content) }] });
  const buffer = await Packer.toBuffer(document);
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.writeFile(target, buffer);
  return { ok: true, path: target, bytes: buffer.length, format: "docx" };
}

async function writeExcel(target, content, mode) {
  let parsed;
  try { parsed = typeof content === "string" ? JSON.parse(content) : content; }
  catch (error) { throw new Error(`Excel content must be valid JSON: ${error.message}`); }

  let workbook = mode === "append" && fsSync.existsSync(target) ? XLSX.readFile(target) : XLSX.utils.book_new();

  if (Array.isArray(parsed)) {
    const name = workbook.SheetNames[0] || "Sheet1";
    let rows = parsed;
    if (mode === "append" && workbook.Sheets[name]) {
      const existing = XLSX.utils.sheet_to_json(workbook.Sheets[name], { header: 1, defval: null });
      rows = [...existing, ...parsed];
      workbook.Sheets[name] = XLSX.utils.aoa_to_sheet(rows);
    } else {
      const ws = XLSX.utils.aoa_to_sheet(rows);
      if (workbook.Sheets[name]) workbook.Sheets[name] = ws;
      else XLSX.utils.book_append_sheet(workbook, ws, name);
    }
  } else if (parsed && typeof parsed === "object") {
    for (const [name, rows] of Object.entries(parsed)) {
      const ws = XLSX.utils.aoa_to_sheet(rows);
      if (workbook.Sheets[name]) workbook.Sheets[name] = ws;
      else XLSX.utils.book_append_sheet(workbook, ws, name);
    }
  } else {
    throw new Error("Excel content must be a 2D array or an object of sheet arrays.");
  }

  await fs.mkdir(path.dirname(target), { recursive: true });
  XLSX.writeFile(workbook, target);
  const stat = await fs.stat(target);
  return { ok: true, path: target, bytes: stat.size, format: "excel" };
}

export async function writeRichFile({ path: input, content, mode = "rewrite" }) {
  const target = insideRoot(input);
  assertOrdinaryWriteAllowed(target);
  const ext = path.extname(target).toLowerCase();
  if (excelExt.has(ext)) return writeExcel(target, content, mode);
  if (ext === ".docx") {
    if (mode === "append" && fsSync.existsSync(target)) {
      throw new Error("Appending to DOCX is not supported; use edit_block for an existing DOCX.");
    }
    return writeDocx(target, content);
  }
  await fs.mkdir(path.dirname(target), { recursive: true });
  if (mode === "append") await fs.appendFile(target, content, "utf8");
  else await fs.writeFile(target, content, "utf8");
  const stat = await fs.stat(target);
  return {
    ok: true,
    path: target,
    bytes: stat.size,
    mode,
    warning: String(content).split(/\r?\n/).length > getRuntimeSetting("fileWriteLineLimit")
      ? `Content exceeds configured fileWriteLineLimit=${getRuntimeSetting("fileWriteLineLimit")}.`
      : null
  };
}

function browserCandidates() {
  if (process.platform === "win32") {
    const pf = process.env.ProgramFiles || "C:\\Program Files";
    const pfx86 = process.env["ProgramFiles(x86)"] || "C:\\Program Files (x86)";
    const local = process.env.LOCALAPPDATA || "";
    return [
      path.join(pfx86, "Microsoft", "Edge", "Application", "msedge.exe"),
      path.join(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
      path.join(local, "Microsoft", "Edge", "Application", "msedge.exe"),
      path.join(pf, "Google", "Chrome", "Application", "chrome.exe"),
      path.join(pfx86, "Google", "Chrome", "Application", "chrome.exe")
    ];
  }
  return ["/usr/bin/microsoft-edge", "/usr/bin/google-chrome", "/usr/bin/chromium", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"];
}

function findBrowser() {
  const browser = browserCandidates().find(candidate => fsSync.existsSync(candidate));
  if (!browser) throw new Error("No supported Chromium/Edge executable found for PDF rendering.");
  return browser;
}

async function renderMarkdownPdf(markdown, outputPath, options = {}) {
  const browser = findBrowser();
  const dir = path.dirname(outputPath);
  await fs.mkdir(dir, { recursive: true });
  const tempHtml = path.join(os.tmpdir(), `raccoon-${randomUUID()}.html`);
  const browserProfile = await fs.mkdtemp(path.join(os.tmpdir(), "raccoon-pdf-profile-"));
  const isFullHtml = /<html[\s>]/i.test(markdown);
  const body = isFullHtml ? markdown : await marked.parse(markdown);
  const baseHref = pathToFileURL(dir + path.sep).href;
  const html = isFullHtml ? body : `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<base href="${baseHref}">
<style>
@page { size: ${options.pageSize || "A4"}; margin: ${options.margin || "18mm"}; }
body { font-family: "Microsoft YaHei", "Segoe UI", sans-serif; line-height: 1.55; color: #111; }
img, svg { max-width: 100%; }
table { border-collapse: collapse; width: 100%; }
th, td { border: 1px solid #999; padding: 6px 8px; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
</head><body>${body}</body></html>`;
  await fs.writeFile(tempHtml, html, "utf8");
  try {
    const args = [
      "--headless",
      "--disable-gpu",
      "--no-first-run",
      "--no-default-browser-check",
      `--user-data-dir=${browserProfile}`,
      "--print-to-pdf-no-header",
      `--print-to-pdf=${outputPath}`,
      pathToFileURL(tempHtml).href
    ];
    await execFileAsync(browser, args, { windowsHide: true, timeout: 120000, maxBuffer: 4 * 1024 * 1024 });
    const stat = await fs.stat(outputPath);
    if (stat.size === 0) throw new Error("Browser generated an empty PDF.");
    return outputPath;
  } finally {
    await fs.unlink(tempHtml).catch(() => {});
    await fs.rm(browserProfile, { recursive: true, force: true }).catch(() => {});
  }
}

export async function writePdf({ path: input, content, outputPath, options = {} }) {
  const source = insideRoot(input);
  if (typeof content === "string") {
    const out = insideRoot(outputPath || input);
    await renderMarkdownPdf(content, out, options);
    const stat = await fs.stat(out);
    return { ok: true, mode: "create", outputPath: out, bytes: stat.size };
  }

  if (!Array.isArray(content)) throw new Error("PDF content must be markdown text or an array of operations.");
  if (!outputPath) throw new Error("outputPath is required when modifying an existing PDF.");
  const out = insideRoot(outputPath);
  const srcBytes = await fs.readFile(source);
  const dest = await PDFDocument.load(srcBytes);

  for (const op of content) {
    if (op.type === "delete") {
      const indexes = [...new Set(op.pageIndexes || [])].sort((a, b) => b - a);
      for (const index of indexes) {
        if (index < 0 || index >= dest.getPageCount()) throw new Error(`Invalid delete page index: ${index}`);
        dest.removePage(index);
      }
      continue;
    }

    if (op.type === "insert") {
      const index = Math.max(0, Math.min(op.pageIndex, dest.getPageCount()));
      let sourcePdf;
      let tempPdf;
      if (op.sourcePdfPath) {
        sourcePdf = await PDFDocument.load(await fs.readFile(insideRoot(op.sourcePdfPath)));
      } else if (op.markdown != null) {
        tempPdf = path.join(os.tmpdir(), `raccoon-insert-${randomUUID()}.pdf`);
        await renderMarkdownPdf(op.markdown, tempPdf, op.pdfOptions || {});
        sourcePdf = await PDFDocument.load(await fs.readFile(tempPdf));
      } else {
        throw new Error("Insert operation requires markdown or sourcePdfPath.");
      }

      const copied = await dest.copyPages(sourcePdf, sourcePdf.getPageIndices());
      copied.forEach((page, i) => dest.insertPage(index + i, page));
      if (tempPdf) await fs.unlink(tempPdf).catch(() => {});
      continue;
    }

    throw new Error(`Unknown PDF operation: ${op.type}`);
  }

  await fs.mkdir(path.dirname(out), { recursive: true });
  const bytes = await dest.save();
  await fs.writeFile(out, bytes);
  return { ok: true, mode: "modify", sourcePath: source, outputPath: out, bytes: bytes.length };
}

export async function getFileInfo(input) {
  const target = insideRoot(input);
  const stat = await fs.stat(target);
  const info = {
    path: target,
    type: stat.isDirectory() ? "directory" : stat.isFile() ? "file" : "other",
    size: stat.size,
    createdAt: stat.birthtime.toISOString(),
    modifiedAt: stat.mtime.toISOString(),
    accessedAt: stat.atime.toISOString(),
    mode: stat.mode,
    permissions: (stat.mode & 0o777).toString(8)
  };
  if (!stat.isFile()) return info;

  const ext = path.extname(target).toLowerCase();
  if (excelExt.has(ext)) {
    const wb = XLSX.readFile(target);
    info.sheets = wb.SheetNames.map(name => {
      const ws = wb.Sheets[name];
      const ref = ws["!ref"] ? XLSX.utils.decode_range(ws["!ref"]) : null;
      return {
        name,
        rowCount: ref ? ref.e.r - ref.s.r + 1 : 0,
        colCount: ref ? ref.e.c - ref.s.c + 1 : 0
      };
    });
  } else if (textLikeExt.has(ext) || !ext) {
    const input = createReadStream(target, { encoding: "utf8", highWaterMark: 256 * 1024 });
    const rl = readline.createInterface({ input, crlfDelay: Infinity });
    let lineCount = 0;
    try {
      for await (const _line of rl) lineCount += 1;
    } finally {
      rl.close();
      input.destroy();
    }
    info.lineCount = lineCount;
    info.lastLine = Math.max(0, lineCount - 1);
    info.appendPosition = lineCount;
  }
  return info;
}

function replaceExpected(source, oldString, newString, expected) {
  const count = source.split(oldString).length - 1;
  if (count !== expected) {
    throw new Error(`Expected ${expected} replacement(s), found ${count}.`);
  }
  return source.split(oldString).join(newString);
}

async function editDocx(target, { old_string, new_string, expected_replacements = 1 }) {
  const zip = new AdmZip(target);
  const candidates = zip.getEntries()
    .filter(entry => /^word\/(document|header\d+|footer\d+)\.xml$/.test(entry.entryName));
  let remaining = expected_replacements;
  let changed = 0;

  for (const entry of candidates) {
    const pretty = prettyXml(entry.getData().toString("utf8"));
    const count = pretty.split(old_string).length - 1;
    if (!count) continue;
    if (count > remaining) throw new Error(`Replacement is ambiguous in ${entry.entryName}: ${count} matches.`);
    const updated = pretty.split(old_string).join(new_string);
    zip.updateFile(entry.entryName, Buffer.from(updated, "utf8"));
    changed += count;
    remaining -= count;
    if (remaining === 0) break;
  }

  if (changed !== expected_replacements) {
    throw new Error(`Expected ${expected_replacements} replacement(s), found ${changed} across DOCX body/headers/footers.`);
  }
  zip.writeZip(target);
  return { ok: true, path: target, replacements: changed, format: "docx" };
}

async function editExcel(target, { range, content }) {
  if (!range || !range.includes("!")) throw new Error("Excel edit range must include a sheet, e.g. Sheet1!A1:C3.");
  const [sheetName, cellRange] = range.split("!");
  const values = typeof content === "string" ? JSON.parse(content) : content;
  if (!Array.isArray(values)) throw new Error("Excel edit content must be a 2D array.");
  const wb = XLSX.readFile(target);
  const ws = wb.Sheets[sheetName];
  if (!ws) throw new Error(`Unknown sheet: ${sheetName}`);
  const decoded = XLSX.utils.decode_range(cellRange);

  for (let r = 0; r < values.length; r++) {
    for (let c = 0; c < (values[r]?.length || 0); c++) {
      const address = XLSX.utils.encode_cell({ r: decoded.s.r + r, c: decoded.s.c + c });
      const value = values[r][c];
      ws[address] = { v: value, t: typeof value === "number" ? "n" : typeof value === "boolean" ? "b" : "s" };
    }
  }

  const existing = ws["!ref"] ? XLSX.utils.decode_range(ws["!ref"]) : decoded;
  existing.s.r = Math.min(existing.s.r, decoded.s.r);
  existing.s.c = Math.min(existing.s.c, decoded.s.c);
  existing.e.r = Math.max(existing.e.r, decoded.s.r + values.length - 1);
  existing.e.c = Math.max(existing.e.c, decoded.s.c + Math.max(0, ...values.map(row => row.length)) - 1);
  ws["!ref"] = XLSX.utils.encode_range(existing);
  XLSX.writeFile(wb, target);
  return { ok: true, path: target, range, rows: values.length, format: "excel" };
}

export async function editRichBlock(args) {
  const target = insideRoot(args.file_path || args.path);
  assertOrdinaryWriteAllowed(target);
  const ext = path.extname(target).toLowerCase();
  if (excelExt.has(ext)) return editExcel(target, args);
  if (ext === ".docx") return editDocx(target, args);

  const original = await fs.readFile(target, "utf8");
  const expected = Number.isSafeInteger(args.expected_replacements) ? args.expected_replacements : 1;
  const updated = replaceExpected(original, args.old_string, args.new_string, expected);
  await fs.writeFile(target, updated, "utf8");
  return {
    ok: true,
    path: target,
    replacements: expected,
    bytesBefore: Buffer.byteLength(original),
    bytesAfter: Buffer.byteLength(updated)
  };
}
