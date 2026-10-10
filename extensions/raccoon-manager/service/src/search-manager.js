import fs from "node:fs/promises";
import { createReadStream } from "node:fs";
import readline from "node:readline";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { minimatch } from "minimatch";
import { insideRoot, config } from "./config.js";

const searches = new Map();
const RETAIN_MS = 5 * 60 * 1000;

function cleanup() {
  const now = Date.now();
  for (const [id, s] of searches) {
    if (s.finishedAt && now - s.finishedAt > RETAIN_MS) searches.delete(id);
  }
}

function isHidden(relativePath) {
  return relativePath.split(/[\\/]/).some(part => part.startsWith(".") && part !== "." && part !== "..");
}

function filePatternMatches(name, filePattern, ignoreCase) {
  if (!filePattern) return true;
  const patterns = String(filePattern).split("|").map(s => s.trim()).filter(Boolean);
  return patterns.some(pattern => minimatch(name, pattern, { nocase: ignoreCase, dot: true }));
}

function compileMatcher(pattern, { literalSearch, ignoreCase, searchType }) {
  if (literalSearch) {
    const needle = ignoreCase ? pattern.toLowerCase() : pattern;
    return value => (ignoreCase ? value.toLowerCase() : value).includes(needle);
  }

  if (searchType === "files" && /[*?\[\]]/.test(pattern)) {
    return value => minimatch(value, pattern, { nocase: ignoreCase, dot: true });
  }

  let regex;
  try {
    regex = new RegExp(pattern, ignoreCase ? "i" : "");
  } catch {
    const needle = ignoreCase ? pattern.toLowerCase() : pattern;
    return value => (ignoreCase ? value.toLowerCase() : value).includes(needle);
  }
  return value => regex.test(value);
}

async function walk(session, dir, relativeBase = "") {
  if (session.stopped) return;
  if (session.deadline && Date.now() > session.deadline) {
    session.status = "timed_out";
    session.stopped = true;
    return;
  }

  let entries;
  try {
    entries = await fs.readdir(dir, { withFileTypes: true });
  } catch (error) {
    session.errors.push({ path: dir, error: error.message });
    return;
  }

  for (const entry of entries) {
    if (session.stopped) return;
    const rel = relativeBase ? path.join(relativeBase, entry.name) : entry.name;
    if (!session.includeHidden && isHidden(rel)) continue;
    const full = path.join(dir, entry.name);

    if (entry.isDirectory()) {
      await walk(session, full, rel);
      continue;
    }
    if (!entry.isFile()) continue;

    if (!filePatternMatches(entry.name, session.filePattern, session.ignoreCase)) continue;

    if (session.searchType === "files") {
      if (session.matcher(entry.name) || session.matcher(rel)) {
        session.results.push({
          type: "file",
          path: full,
          relativePath: rel,
          name: entry.name
        });
        if (session.earlyTermination && entry.name.toLowerCase() === session.pattern.toLowerCase()) {
          session.status = "completed";
          session.stopped = true;
          return;
        }
        if (session.results.length >= session.maxResults) {
          session.status = "max_results";
          session.stopped = true;
          return;
        }
      }
      continue;
    }

    try {
      const handle = await fs.open(full, "r");
      try {
        const probe = Buffer.alloc(8192);
        const { bytesRead } = await handle.read(probe, 0, probe.length, 0);
        if (probe.subarray(0, bytesRead).includes(0)) continue;
      } finally {
        await handle.close();
      }

      const input = createReadStream(full, {
        encoding: "utf8",
        highWaterMark: 256 * 1024
      });
      const rl = readline.createInterface({ input, crlfDelay: Infinity });
      const previous = [];
      const pending = [];
      let lineNumber = 0;

      try {
        for await (const line of rl) {
          if (session.stopped) break;
          lineNumber += 1;

          for (let i = pending.length - 1; i >= 0; i--) {
            const item = pending[i];
            item.result.context.push({ line: lineNumber, text: line });
            item.remaining -= 1;
            if (item.remaining <= 0) pending.splice(i, 1);
          }

          if (session.matcher(line)) {
            let column = -1;
            if (session.literalSearch) {
              const hay = session.ignoreCase ? line.toLowerCase() : line;
              const needle = session.ignoreCase ? session.pattern.toLowerCase() : session.pattern;
              column = hay.indexOf(needle);
            }

            const result = {
              type: "content",
              path: full,
              relativePath: rel,
              line: lineNumber,
              column: column >= 0 ? column + 1 : null,
              text: line,
              context: [
                ...previous,
                { line: lineNumber, text: line }
              ]
            };
            session.results.push(result);
            if (session.contextLines > 0) {
              pending.push({ result, remaining: session.contextLines });
            }

            if (session.results.length >= session.maxResults) {
              session.status = "max_results";
              session.stopped = true;
              break;
            }
          }

          previous.push({ line: lineNumber, text: line });
          if (previous.length > session.contextLines) previous.shift();

          if (session.deadline && Date.now() > session.deadline) {
            session.status = "timed_out";
            session.stopped = true;
            break;
          }
        }
      } finally {
        rl.close();
        input.destroy();
      }
    } catch (error) {
      session.errors.push({ path: full, error: error.message });
    }
  }
}

async function runSearch(session) {
  try {
    await walk(session, session.root);
    if (session.status === "running") session.status = "completed";
  } catch (error) {
    session.status = "failed";
    session.errors.push({ error: String(error?.stack || error) });
  } finally {
    session.finishedAt = Date.now();
  }
}

export function startSearch(options) {
  cleanup();
  const id = randomUUID();
  const root = insideRoot(options.path || ".");
  const searchType = options.searchType || "files";
  const session = {
    id,
    root,
    path: options.path || ".",
    pattern: options.pattern,
    searchType,
    literalSearch: Boolean(options.literalSearch),
    ignoreCase: options.ignoreCase !== false,
    includeHidden: Boolean(options.includeHidden),
    filePattern: options.filePattern || "",
    contextLines: Number.isSafeInteger(options.contextLines) ? options.contextLines : 5,
    earlyTermination: options.earlyTermination ?? (searchType === "files"),
    maxResults: Math.min(config.maxSearchResults, Math.max(1, options.maxResults || 1000)),
    deadline: options.timeout_ms ? Date.now() + options.timeout_ms : null,
    matcher: compileMatcher(options.pattern, {
      literalSearch: Boolean(options.literalSearch),
      ignoreCase: options.ignoreCase !== false,
      searchType
    }),
    results: [],
    errors: [],
    status: "running",
    stopped: false,
    createdAt: Date.now(),
    finishedAt: null
  };
  searches.set(id, session);
  void runSearch(session);
  return summarize(session);
}

function summarize(session) {
  return {
    sessionId: session.id,
    searchType: session.searchType,
    pattern: session.pattern,
    path: session.path,
    status: session.status,
    resultCount: session.results.length,
    errorCount: session.errors.length,
    runtimeMs: (session.finishedAt || Date.now()) - session.createdAt
  };
}

export function getSearchResults({ sessionId, offset = 0, length = 100 }) {
  cleanup();
  const session = searches.get(sessionId);
  if (!session) throw new Error(`Unknown search session: ${sessionId}`);

  let start;
  let count;
  if (offset < 0) {
    count = Math.min(-offset, session.results.length);
    start = Math.max(0, session.results.length - count);
  } else {
    start = Math.min(offset, session.results.length);
    count = Math.min(config.maxSearchResults, Math.max(0, length));
  }

  return {
    ...summarize(session),
    offset: start,
    length: count,
    results: session.results.slice(start, start + count),
    errors: session.errors.slice(0, 20)
  };
}

export function stopSearch(sessionId) {
  const session = searches.get(sessionId);
  if (!session) throw new Error(`Unknown search session: ${sessionId}`);
  session.stopped = true;
  if (session.status === "running") session.status = "stopped";
  if (!session.finishedAt) session.finishedAt = Date.now();
  return summarize(session);
}

export function listSearches() {
  cleanup();
  return [...searches.values()].map(summarize).sort((a, b) => b.runtimeMs - a.runtimeMs);
}
