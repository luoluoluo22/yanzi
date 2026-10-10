import os from "node:os";
import { getRuntimeSetting } from "./runtime-settings.js";
import { config } from "./config.js";

const firstUsed = Date.now();
let lastUsed = firstUsed;
let successfulCalls = 0;
let failedCalls = 0;
let totalCalls = 0;
const toolCounts = new Map();
const durations = new Map();
const recent = [];

function safeClone(value, maxChars = 65536) {
  try {
    const text = JSON.stringify(value);
    if (text.length <= maxChars) return JSON.parse(text);
    return { clipped: true, preview: text.slice(0, maxChars) };
  } catch {
    return String(value);
  }
}

export function recordToolCall({ tool, args, result, ok, durationMs }) {
  if (!getRuntimeSetting("telemetryEnabled")) return;
  lastUsed = Date.now();
  totalCalls += 1;
  if (ok) successfulCalls += 1;
  else failedCalls += 1;
  toolCounts.set(tool, (toolCounts.get(tool) || 0) + 1);
  const d = durations.get(tool) || { count: 0, totalMs: 0, maxMs: 0 };
  d.count += 1;
  d.totalMs += durationMs;
  d.maxMs = Math.max(d.maxMs, durationMs);
  durations.set(tool, d);

  if (!["get_recent_tool_calls", "get_usage_stats"].includes(tool)) {
    recent.push({
      at: new Date().toISOString(),
      tool,
      ok,
      durationMs,
      args: safeClone(args),
      result: safeClone(result)
    });
    if (recent.length > config.maxRecentToolCalls) recent.splice(0, recent.length - config.maxRecentToolCalls);
  }
}

export function getUsageStats() {
  const performance = {};
  for (const [tool, value] of durations) {
    performance[tool] = {
      calls: value.count,
      averageMs: Math.round(value.totalMs / Math.max(1, value.count)),
      maxMs: value.maxMs
    };
  }
  return {
    firstUsed,
    lastUsed,
    totalToolCalls: totalCalls,
    successfulCalls,
    failedCalls,
    successRate: totalCalls ? successfulCalls / totalCalls : 1,
    toolCounts: Object.fromEntries([...toolCounts.entries()].sort()),
    performance,
    host: {
      hostname: os.hostname(),
      platform: process.platform,
      arch: process.arch,
      node: process.version,
      uptimeSeconds: os.uptime(),
      totalMemoryBytes: os.totalmem(),
      freeMemoryBytes: os.freemem()
    }
  };
}

export function getRecentToolCalls({ maxResults = 50, toolName, since }) {
  let rows = recent;
  if (toolName) rows = rows.filter(item => item.tool === toolName);
  if (since) {
    const sinceMs = Date.parse(since);
    if (!Number.isNaN(sinceMs)) rows = rows.filter(item => Date.parse(item.at) >= sinceMs);
  }
  return rows.slice(-maxResults);
}
