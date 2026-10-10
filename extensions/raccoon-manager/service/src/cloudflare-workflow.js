import { z } from "zod";
import { config } from "./config.js";
import { waitUntil } from "./dev-pipeline.js";
import { captureOutputs, interpolateTemplate } from "./pipeline-runtime.js";
import { httpProbe } from "./http-probe.js";

const runnerSchema = z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default");
const probeConditionSchema = z.object({
  exitCode: z.number().int().optional(),
  stdoutIncludes: z.string().max(16384).optional(),
  stderrIncludes: z.string().max(16384).optional(),
  stdoutRegex: z.string().max(16384).optional(),
  stderrRegex: z.string().max(16384).optional(),
  jsonPathExists: z.string().max(1024).optional(),
  jsonPathEquals: z.object({ path: z.string().max(1024), value: z.any() }).optional()
}).default({ exitCode: 0 });
const captureSelectorSchema = z.object({
  source: z.enum(["stdout", "stderr"]).default("stdout"),
  regex: z.string().max(16384).optional(),
  flags: z.string().max(16).default(""),
  group: z.number().int().min(0).max(100).default(1),
  jsonPath: z.string().max(1024).optional(),
  path: z.string().max(1024).optional()
});
const httpAssertionSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("status"), equals: z.number().int().min(100).max(599) }),
  z.object({ type: z.literal("bodyIncludes"), value: z.string().max(16384) }),
  z.object({ type: z.literal("bodyRegex"), value: z.string().max(16384) }),
  z.object({ type: z.literal("jsonPathExists"), path: z.string().max(1024) }),
  z.object({ type: z.literal("jsonPathEquals"), path: z.string().max(1024), value: z.any() })
]);

async function oneCommand(command, cwd, runner, timeoutMs, maxOutputBytes) {
  return waitUntil({
    command, cwd, runner,
    condition: { exitCode: 0 },
    intervalMs: 100,
    timeoutMs,
    commandTimeoutMs: timeoutMs,
    maxAttempts: 1,
    maxOutputBytes,
    idempotent: false
  });
}

function normalizedHttpProbe(input) {
  return {
    url: input.url,
    method: input.method,
    headers: input.headers,
    authorization: input.authorization,
    body: input.body,
    jsonBody: input.jsonBody,
    assertions: input.assertions,
    requestTimeoutMs: input.requestTimeoutMs,
    intervalMs: input.intervalMs,
    timeoutMs: input.timeoutMs,
    maxAttempts: input.maxAttempts,
    maxResponseBytes: input.maxResponseBytes,
    redirect: input.redirect
  };
}

export async function cloudflareDeployVerify(args) {
  const stages = {};
  stages.deploy = await oneCommand(args.deployCommand, args.cwd, args.runner, args.deployTimeoutMs, args.maxOutputBytes);
  if (!stages.deploy.ok) return { ok: false, stage: "deploy", stages };

  let outputs = {};
  if (args.versionProbe) {
    stages.version = await waitUntil({
      command: args.versionProbe.command,
      cwd: args.versionProbe.cwd || args.cwd,
      runner: args.versionProbe.runner,
      condition: args.versionProbe.condition,
      intervalMs: args.versionProbe.intervalMs,
      timeoutMs: args.versionProbe.timeoutMs,
      commandTimeoutMs: args.versionProbe.commandTimeoutMs,
      maxAttempts: args.versionProbe.maxAttempts,
      maxOutputBytes: args.maxOutputBytes,
      idempotent: true
    });
    if (!stages.version.ok) return { ok: false, stage: "version_wait", stages };
    try {
      outputs = captureOutputs(args.versionProbe.capture || {}, stages.version.last || {});
    } catch (error) {
      return { ok: false, stage: "version_capture", stages, error: error.message };
    }
  }
  stages.outputs = outputs;

  if (args.activateCommand) {
    let command;
    try { command = interpolateTemplate(args.activateCommand, { deploy: outputs }); }
    catch (error) { return { ok: false, stage: "activate_template", stages, error: error.message }; }
    stages.activate = await oneCommand(command, args.cwd, args.activateRunner, args.activateTimeoutMs, args.maxOutputBytes);
    if (!stages.activate.ok) return { ok: false, stage: "activate", stages };
  }

  stages.probes = [];
  for (const probe of args.probes) {
    const result = await httpProbe(normalizedHttpProbe(probe));
    stages.probes.push({ name: probe.name, result });
    if (!result.ok) return { ok: false, stage: "http_verify", failedProbe: probe.name, stages };
  }

  return { ok: true, stage: "complete", outputs, stages };
}

export function registerCloudflareWorkflowTool(server) {
  server.registerTool("cloudflare_deploy_verify", {
    title: "Cloudflare deploy and verify",
    description: "Run one deployment command, wait for a new deployment/version condition, capture values, optionally activate traffic using captured values, then run secret-aware HTTP/API assertions.",
    inputSchema: {
      cwd: z.string().default("."),
      deployCommand: z.string().min(1).max(65536),
      runner: runnerSchema,
      deployTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(10 * 60 * 1000),
      maxOutputBytes: z.number().int().min(1024).max(config.maxShellOutputBytes).default(Math.min(4 * 1024 * 1024, config.maxShellOutputBytes)),
      versionProbe: z.object({
        command: z.string().min(1).max(65536),
        cwd: z.string().optional(),
        runner: runnerSchema,
        condition: probeConditionSchema,
        capture: z.record(z.string(), captureSelectorSchema).default({}),
        intervalMs: z.number().int().min(100).max(60000).default(10000),
        timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(10 * 60 * 1000),
        commandTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(120000),
        maxAttempts: z.number().int().min(1).max(10000).default(60)
      }).optional(),
      activateCommand: z.string().min(1).max(65536).optional(),
      activateRunner: runnerSchema,
      activateTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(5 * 60 * 1000),
      probes: z.array(z.object({
        name: z.string().min(1).max(200),
        url: z.string().url(),
        method: z.enum(["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"]).default("GET"),
        headers: z.record(z.string(), z.string()).default({}),
        authorization: z.object({ scheme: z.string().min(1).max(50).default("Bearer"), secretRef: z.string().startsWith("secret://") }).optional(),
        body: z.string().optional(),
        jsonBody: z.any().optional(),
        assertions: z.array(httpAssertionSchema).min(1).max(100).default([{ type: "status", equals: 200 }]),
        requestTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(30000),
        intervalMs: z.number().int().min(100).max(60000).default(5000),
        timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(60000),
        maxAttempts: z.number().int().min(1).max(10000).default(12),
        maxResponseBytes: z.number().int().min(1024).max(config.maxReadBytes).default(Math.min(4 * 1024 * 1024, config.maxReadBytes)),
        redirect: z.enum(["follow", "error", "manual"]).default("follow")
      })).max(50).default([])
    }
  }, async args => ({ content: [{ type: "text", text: JSON.stringify(await cloudflareDeployVerify(args), null, 2) }] }));
}
