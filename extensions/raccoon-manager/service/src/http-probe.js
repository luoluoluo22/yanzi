import { z } from "zod";
import { config } from "./config.js";
import { getPath, sleep } from "./pipeline-runtime.js";
import { listSecretRefs, resolveSecretRef } from "./secret-store.js";

function resolveValue(value, secretValues) {
  if (typeof value !== "string" || !value.startsWith("secret://")) return value;
  const resolved = resolveSecretRef(value);
  secretValues.push(resolved);
  return resolved;
}

function redact(value, secretValues) {
  let output = String(value ?? "");
  for (const secret of secretValues) if (secret) output = output.split(secret).join("[REDACTED]");
  return output;
}

function evaluateAssertions(response, assertions) {
  const failures = [];
  for (const assertion of assertions) {
    if (assertion.type === "status") {
      if (response.status !== assertion.equals) failures.push(`status ${response.status} != ${assertion.equals}`);
    } else if (assertion.type === "bodyIncludes") {
      if (!response.body.includes(assertion.value)) failures.push("bodyIncludes did not match");
    } else if (assertion.type === "bodyRegex") {
      if (!new RegExp(assertion.value).test(response.body)) failures.push("bodyRegex did not match");
    } else if (assertion.type === "jsonPathExists") {
      if (response.json === undefined || getPath(response.json, assertion.path) === undefined) failures.push(`jsonPath missing: ${assertion.path}`);
    } else if (assertion.type === "jsonPathEquals") {
      const actual = response.json === undefined ? undefined : getPath(response.json, assertion.path);
      if (actual !== assertion.value) failures.push(`jsonPath value mismatch: ${assertion.path}`);
    }
  }
  return { ok: failures.length === 0, failures };
}

async function oneProbe(args) {
  const controller = new AbortController();
  let timer;
  const headers = {};
  const secretValues = [];
  try {
  timer = setTimeout(() => controller.abort(new Error("HTTP probe timed out")), args.requestTimeoutMs);
  for (const [key, value] of Object.entries(args.headers || {})) headers[key] = resolveValue(value, secretValues);
  if (args.authorization?.secretRef) {
    const secret = resolveSecretRef(args.authorization.secretRef);
    secretValues.push(secret);
    headers.Authorization = `${args.authorization.scheme || "Bearer"} ${secret}`;
  }

  let body;
  if (args.jsonBody !== undefined) {
    body = JSON.stringify(args.jsonBody);
    if (!Object.keys(headers).some(key => key.toLowerCase() === "content-type")) headers["Content-Type"] = "application/json";
  } else if (args.body !== undefined) {
    body = args.body;
  }

    const response = await fetch(args.url, {
      method: args.method,
      headers,
      body,
      redirect: args.redirect,
      signal: controller.signal
    });
    const chunks = [];
    let bytes = 0, clipped = false;
    if (response.body) {
      const reader = response.body.getReader();
      try {
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          const remaining = args.maxResponseBytes - bytes;
          chunks.push(Buffer.from(value.subarray(0, remaining)));
          bytes += Math.min(value.length, remaining);
          if (value.length > remaining) { clipped = true; await reader.cancel(); break; }
        }
      } finally { reader.releaseLock(); }
    }
    const rawText = Buffer.concat(chunks).toString("utf8");
    let rawJson;
    try { rawJson = JSON.parse(rawText); } catch {}
    const rawResult = {
      status: response.status,
      ok: response.ok,
      headers: Object.fromEntries([...response.headers.entries()].filter(([key]) => !["set-cookie"].includes(key.toLowerCase()))),
      body: rawText,
      bytes,
      clipped,
      json: rawJson
    };
    const assertions = evaluateAssertions(rawResult, args.assertions);
    const safeBody = redact(rawText, secretValues);
    let json;
    try { json = JSON.parse(safeBody); } catch {}
    const safeHeaders = Object.fromEntries(Object.entries(rawResult.headers).map(([key, value]) => [key, redact(value, secretValues)]));
    return { ...rawResult, headers: safeHeaders, body: safeBody, json, assertions };
  } catch (error) {
    const message = redact(error?.message || error, secretValues);
    return { status: null, ok: false, body: "", bytes: 0, clipped: false, json: undefined, assertions: { ok: false, failures: [message] }, error: message };
  } finally {
    clearTimeout(timer);
  }
}

export async function httpProbe(args) {
  const started = Date.now();
  let attempts = 0, last;
  while (attempts < args.maxAttempts) {
    const remainingBeforeAttempt = args.timeoutMs - (Date.now() - started);
    if (remainingBeforeAttempt <= 0) break;
    attempts++;
    last = await oneProbe({ ...args, requestTimeoutMs: Math.min(args.requestTimeoutMs, remainingBeforeAttempt) });
    if (last.assertions.ok) {
      return { ok: true, matched: true, attempts, durationMs: Date.now() - started, response: last };
    }
    const remaining = args.timeoutMs - (Date.now() - started);
    if (attempts >= args.maxAttempts || remaining <= 0) break;
    await sleep(Math.min(args.intervalMs, remaining));
  }
  return { ok: false, matched: false, attempts, durationMs: Date.now() - started, response: last, error: "HTTP assertions did not pass before timeout/attempt limit." };
}

const assertionSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("status"), equals: z.number().int().min(100).max(599) }),
  z.object({ type: z.literal("bodyIncludes"), value: z.string().max(16384) }),
  z.object({ type: z.literal("bodyRegex"), value: z.string().max(16384) }),
  z.object({ type: z.literal("jsonPathExists"), path: z.string().max(1024) }),
  z.object({ type: z.literal("jsonPathEquals"), path: z.string().max(1024), value: z.any() })
]);

export function registerHttpProbeTools(server) {
  server.registerTool("secret_list", {
    title: "List configured secret references",
    description: "List names from the protected Raccoon secret store without returning secret values."
  }, async () => ({ content: [{ type: "text", text: JSON.stringify({ refs: listSecretRefs() }, null, 2) }] }));

  server.registerTool("http_probe", {
    title: "HTTP/API probe",
    description: "Call an HTTP endpoint with optional secret references, structured assertions and bounded retry. Resolved secret values are never returned in tool output.",
    inputSchema: {
      url: z.string().url(),
      method: z.enum(["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"]).default("GET"),
      headers: z.record(z.string(), z.string()).default({}),
      authorization: z.object({
        scheme: z.string().min(1).max(50).default("Bearer"),
        secretRef: z.string().startsWith("secret://")
      }).optional(),
      body: z.string().optional(),
      jsonBody: z.any().optional(),
      assertions: z.array(assertionSchema).min(1).max(100).default([{ type: "status", equals: 200 }]),
      requestTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(30000),
      intervalMs: z.number().int().min(100).max(60000).default(5000),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(30000),
      maxAttempts: z.number().int().min(1).max(10000).default(1),
      maxResponseBytes: z.number().int().min(1024).max(config.maxReadBytes).default(Math.min(4 * 1024 * 1024, config.maxReadBytes)),
      redirect: z.enum(["follow", "error", "manual"]).default("follow")
    }
  }, async args => ({ content: [{ type: "text", text: JSON.stringify(await httpProbe(args), null, 2) }] }));
}
