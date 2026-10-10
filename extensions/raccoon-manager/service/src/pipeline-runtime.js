function pathSegments(input) {
  const source = String(input || "").replace(/^\$\.?/, "");
  if (!source) return [];
  const segments = [];
  for (const part of source.split(".")) {
    const match = /^([^\[]+)?((?:\[(?:\d+|\*)\])*)$/.exec(part);
    if (!match) throw new Error(`Unsupported path syntax: ${input}`);
    if (match[1]) segments.push(match[1]);
    for (const token of match[2].matchAll(/\[(\d+|\*)\]/g)) {
      segments.push(token[1] === "*" ? "*" : Number(token[1]));
    }
  }
  return segments;
}

export function getPath(value, input) {
  let current = value;
  for (const segment of pathSegments(input)) {
    if (segment === "*") {
      if (!Array.isArray(current)) return undefined;
      current = current.flatMap(item => Array.isArray(item) ? item : [item]);
      continue;
    }
    if (Array.isArray(current) && typeof segment === "string") {
      current = current.map(item => item?.[segment]);
    } else {
      current = current?.[segment];
    }
    if (current === undefined) return undefined;
  }
  return current;
}

export function buildPipelineContext(run, currentNodeId) {
  const context = {};
  for (const node of run.nodes || []) {
    const outputs = node.outputs || {};
    context[node.id] = {
      status: node.status,
      ...outputs,
      outputs,
      result: node.result || null,
      attempts: node.attempts || 0
    };
  }
  if (currentNodeId && context[currentNodeId]) context.self = context[currentNodeId];
  return context;
}

export function interpolateTemplate(input, context, { allowMissing = false } = {}) {
  if (input == null) return input;
  return String(input).replace(/\$\{([a-zA-Z0-9_.-]+)\}/g, (_all, key) => {
    const value = getPath(context, key);
    if (value === undefined) {
      if (allowMissing) return "";
      throw new Error(`Unknown pipeline variable: ${key}`);
    }
    if (value == null) return "";
    return typeof value === "object" ? JSON.stringify(value) : String(value);
  });
}

function splitLogical(expression, operator) {
  const parts = [];
  let quote = null;
  let escaped = false;
  let start = 0;
  for (let i = 0; i < expression.length - 1; i++) {
    const char = expression[i];
    if (escaped) { escaped = false; continue; }
    if (char === "\\") { escaped = true; continue; }
    if (quote) {
      if (char === quote) quote = null;
      continue;
    }
    if (char === "\"" || char === "'") { quote = char; continue; }
    if (expression.slice(i, i + operator.length) === operator) {
      parts.push(expression.slice(start, i).trim());
      start = i + operator.length;
      i += operator.length - 1;
    }
  }
  parts.push(expression.slice(start).trim());
  return parts;
}

function parseOperand(token, context) {
  const value = token.trim();
  const variable = /^\$\{([a-zA-Z0-9_.-]+)\}$/.exec(value);
  if (variable) return getPath(context, variable[1]);
  if ((value.startsWith("\"") && value.endsWith("\"")) || (value.startsWith("'") && value.endsWith("'"))) {
    if (value.startsWith("\"")) return JSON.parse(value);
    return value.slice(1, -1).replace(/\\'/g, "'").replace(/\\\\/g, "\\");
  }
  if (/^-?\d+(?:\.\d+)?$/.test(value)) return Number(value);
  if (value === "true") return true;
  if (value === "false") return false;
  if (value === "null") return null;
  if (value === "undefined") return undefined;
  return interpolateTemplate(value, context);
}

function evaluateAtomic(expression, context) {
  const trimmed = expression.trim();
  const exists = /^exists\s+(.+)$/i.exec(trimmed);
  if (exists) {
    const value = parseOperand(exists[1], context);
    return value !== undefined && value !== null && value !== "";
  }

  const match = /^(.+?)\s+(==|!=|>=|<=|>|<|contains|matches)\s+(.+)$/i.exec(trimmed);
  if (!match) throw new Error(`Unsupported condition expression: ${trimmed}`);
  const left = parseOperand(match[1], context);
  const right = parseOperand(match[3], context);
  switch (match[2].toLowerCase()) {
    case "==": return left === right || (left != null && right != null && String(left) === String(right));
    case "!=": return !(left === right || (left != null && right != null && String(left) === String(right)));
    case ">": return Number(left) > Number(right);
    case ">=": return Number(left) >= Number(right);
    case "<": return Number(left) < Number(right);
    case "<=": return Number(left) <= Number(right);
    case "contains":
      return Array.isArray(left) ? left.some(item => String(item) === String(right)) : String(left ?? "").includes(String(right ?? ""));
    case "matches":
      return new RegExp(String(right)).test(String(left ?? ""));
    default:
      return false;
  }
}

export function evaluateExpression(expression, context) {
  if (!expression) return true;
  return splitLogical(expression, "||").some(orPart =>
    splitLogical(orPart, "&&").every(andPart => evaluateAtomic(andPart, context))
  );
}

function sourceText(result, source) {
  if (source === "stderr") return result.stderr || "";
  return result.stdout || "";
}

export function captureOutputs(capture, result) {
  const outputs = {};
  for (const [name, selector] of Object.entries(capture || {})) {
    if (selector.regex) {
      const match = new RegExp(selector.regex, selector.flags || "").exec(sourceText(result, selector.source || "stdout"));
      if (!match) throw new Error(`Capture ${name} did not match regex`);
      const group = selector.group ?? 1;
      if (match[group] === undefined) throw new Error(`Capture ${name} regex group ${group} is missing`);
      outputs[name] = match[group];
      continue;
    }
    if (selector.jsonPath) {
      let parsed;
      try { parsed = JSON.parse(sourceText(result, selector.source || "stdout").trim()); }
      catch { throw new Error(`Capture ${name} expected JSON in ${selector.source || "stdout"}`); }
      const value = getPath(parsed, selector.jsonPath);
      if (value === undefined) throw new Error(`Capture ${name} path not found: ${selector.jsonPath}`);
      outputs[name] = value;
      continue;
    }
    if (selector.path) {
      const value = getPath(result, selector.path);
      if (value === undefined) throw new Error(`Capture ${name} result path not found: ${selector.path}`);
      outputs[name] = value;
      continue;
    }
    throw new Error(`Capture ${name} requires regex, jsonPath or path`);
  }
  return outputs;
}

export function matchesProbe(result, condition = {}) {
  if (condition.exitCode != null && result.exitCode !== condition.exitCode) return false;
  if (condition.stdoutIncludes != null && !String(result.stdout || "").includes(condition.stdoutIncludes)) return false;
  if (condition.stderrIncludes != null && !String(result.stderr || "").includes(condition.stderrIncludes)) return false;
  if (condition.stdoutRegex != null && !new RegExp(condition.stdoutRegex).test(String(result.stdout || ""))) return false;
  if (condition.stderrRegex != null && !new RegExp(condition.stderrRegex).test(String(result.stderr || ""))) return false;

  if (condition.jsonPathExists != null || condition.jsonPathEquals != null) {
    let parsed;
    try { parsed = JSON.parse(String(result.stdout || "").trim()); }
    catch { return false; }
    if (condition.jsonPathExists != null && getPath(parsed, condition.jsonPathExists) === undefined) return false;
    if (condition.jsonPathEquals != null) {
      const actual = getPath(parsed, condition.jsonPathEquals.path);
      if (actual !== condition.jsonPathEquals.value) return false;
    }
  }

  const hasExplicitCheck = Object.keys(condition).length > 0;
  return hasExplicitCheck ? true : Boolean(result.ok);
}

export function runnerExecutable(runner = "default") {
  if (runner === "default") return undefined;
  if (runner === "powershell5") {
    if (process.platform !== "win32") throw new Error("powershell5 runner is only available on Windows");
    return "powershell.exe";
  }
  if (runner === "powershell7") return process.platform === "win32" ? "pwsh.exe" : "pwsh";
  if (runner === "cmd") {
    if (process.platform !== "win32") throw new Error("cmd runner is only available on Windows");
    return "cmd.exe";
  }
  if (runner === "bash") return process.platform === "win32" ? "bash.exe" : "/bin/bash";
  throw new Error(`Unsupported runner: ${runner}`);
}

export function sleep(ms, signal) {
  if (signal?.aborted) return Promise.reject(signal.reason || new Error("Cancelled"));
  if (ms <= 0) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(done, ms);
    function done() {
      signal?.removeEventListener("abort", abort);
      resolve();
    }
    function abort() {
      clearTimeout(timer);
      reject(signal.reason || new Error("Cancelled"));
    }
    signal?.addEventListener("abort", abort, { once: true });
  });
}
