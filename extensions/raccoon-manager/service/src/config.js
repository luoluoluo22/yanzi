import path from "node:path";
import fs from "node:fs";

// Existing environment variables take precedence over local configuration.
try { process.loadEnvFile(process.env.RACCOON_ENV_FILE || '.env'); } catch (error) { if (error.code !== "ENOENT") throw error; }

function integer(name, fallback, min, max) {
  const value = Number(process.env[name] ?? fallback);
  if (!Number.isSafeInteger(value) || value < min || value > max) {
    throw new Error(`${name} must be an integer between ${min} and ${max}.`);
  }
  return value;
}

const KiB = 1024;
const MiB = 1024 * KiB;
const GiB = 1024 * MiB;
const cliTransport = process.argv.includes("--http") ? "http" : undefined;
const root = fs.realpathSync(path.resolve(process.env.RACCOON_ROOT || process.cwd()));
const host = process.env.RACCOON_HOST || "127.0.0.1";
const port = integer("RACCOON_PORT", 3766, 1, 65535);

export const config = Object.freeze({
  root,
  filesystemUnrestricted: process.env.RACCOON_FILESYSTEM_UNRESTRICTED === "1",
  sourceBackupDir: path.resolve(process.env.RACCOON_SOURCE_BACKUP_DIR || '.raccoon-runtime/source-backups'),
  host,
  port,
  token: process.env.RACCOON_TOKEN || "",
  publicUrl: process.env.RACCOON_PUBLIC_URL || "",
  oauthPassword: process.env.RACCOON_OAUTH_PASSWORD || "",
  oauthStateFile: path.resolve(process.env.RACCOON_OAUTH_STATE_FILE || ".raccoon-runtime/oauth-state.json"),
  secretFile: path.resolve(process.env.RACCOON_SECRET_FILE || ".raccoon-runtime/secrets.json"),
  transport: (cliTransport || process.env.RACCOON_TRANSPORT || "stdio").toLowerCase(),
  shellEnabled: process.env.RACCOON_ENABLE_SHELL === "1",
  readOnly: process.env.RACCOON_READ_ONLY === "1",
  allowedHosts: (process.env.RACCOON_ALLOWED_HOSTS || "").split(",").map(s => s.trim()).filter(Boolean),
  allowedOrigins: (process.env.RACCOON_ALLOWED_ORIGINS || "").split(",").map(s => s.trim()).filter(Boolean),
  auditLog: process.env.RACCOON_AUDIT_LOG ? path.resolve(process.env.RACCOON_AUDIT_LOG) : null,
  pipelineDir: path.resolve(process.env.RACCOON_PIPELINE_DIR || ".raccoon-runtime/pipelines"),
  pipelineMaxRunningNodes: integer("RACCOON_PIPELINE_MAX_RUNNING_NODES", 8, 1, 64),

  // High hard ceilings keep Raccoon from being the bottleneck. Separate
  // defaults keep ordinary calls memory-bounded; callers can explicitly request
  // the higher ceilings when the host/client can handle them.
  defaultReadBytes: integer("RACCOON_DEFAULT_READ_BYTES", 64 * MiB, KiB, 512 * MiB),
  maxReadBytes: integer("RACCOON_MAX_READ_BYTES", 512 * MiB, KiB, 512 * MiB),
  maxMessageBytes: integer("RACCOON_MAX_MESSAGE_BYTES", 512 * MiB, KiB, 512 * MiB),
  defaultProcessLogBytes: integer("RACCOON_DEFAULT_PROCESS_LOG_BYTES", 256 * MiB, 64 * KiB, 2 * GiB),
  maxProcessLogBytes: integer("RACCOON_MAX_PROCESS_LOG_BYTES", 2 * GiB, 64 * KiB, 2 * GiB),
  maxShellOutputBytes: integer("RACCOON_MAX_SHELL_OUTPUT_BYTES", 512 * MiB, KiB, 512 * MiB),
  maxBuildOutputBytes: integer("RACCOON_MAX_BUILD_OUTPUT_BYTES", 512 * MiB, KiB, 512 * MiB),
  maxGitPatchBytes: integer("RACCOON_MAX_GIT_PATCH_BYTES", 512 * MiB, KiB, 512 * MiB),
  maxProcessReadBytes: integer("RACCOON_MAX_PROCESS_READ_BYTES", 512 * MiB, 64 * KiB, 512 * MiB),
  maxBatchFiles: integer("RACCOON_MAX_BATCH_FILES", 10000, 1, 10000),
  batchConcurrency: integer("RACCOON_BATCH_CONCURRENCY", 32, 1, 256),
  maxBatchConcurrency: integer("RACCOON_MAX_BATCH_CONCURRENCY", 256, 1, 256),
  maxDirectoryEntries: integer("RACCOON_MAX_DIRECTORY_ENTRIES", 1000000, 1, 1000000),
  maxTreeDepth: integer("RACCOON_MAX_TREE_DEPTH", 1000, 1, 1000),
  maxPatchFiles: integer("RACCOON_MAX_PATCH_FILES", 5000, 1, 5000),
  maxReplacementsPerFile: integer("RACCOON_MAX_REPLACEMENTS_PER_FILE", 10000, 1, 10000),
  maxWorkflowSteps: integer("RACCOON_MAX_WORKFLOW_STEPS", 10000, 1, 10000),
  maxSearchContextLines: integer("RACCOON_MAX_SEARCH_CONTEXT_LINES", 10000, 0, 10000),
  maxProcessSessions: integer("RACCOON_MAX_PROCESS_SESSIONS", 4096, 1, 4096),
  processEventChunkBytes: integer("RACCOON_PROCESS_EVENT_CHUNK_BYTES", 256 * KiB, 4 * KiB, 4 * MiB),
  maxSearchResults: integer("RACCOON_MAX_SEARCH_RESULTS", 1000000, 1, 1000000),
  maxRecentToolCalls: integer("RACCOON_MAX_RECENT_TOOL_CALLS", 10000, 100, 100000),
  httpRequestTimeoutMs: integer("RACCOON_HTTP_REQUEST_TIMEOUT_MS", 0, 0, 24 * 60 * 60 * 1000),
  httpHeadersTimeoutMs: integer("RACCOON_HTTP_HEADERS_TIMEOUT_MS", 60000, 1000, 10 * 60 * 1000),

  governorEnabled: process.env.RACCOON_GOVERNOR_ENABLED === "1",
  governorMaxConcurrentHeavyOps: integer("RACCOON_GOVERNOR_MAX_HEAVY_OPS", 8, 1, 1024),
  governorMaxSystemCpuPercent: integer("RACCOON_GOVERNOR_MAX_CPU_PERCENT", 95, 1, 100),
  governorMinFreeMemoryBytes: integer("RACCOON_GOVERNOR_MIN_FREE_MEMORY_BYTES", 512 * MiB, 0, 1024 * GiB),
  governorPollMs: integer("RACCOON_GOVERNOR_POLL_MS", 500, 100, 60000)
});

if (config.defaultReadBytes > config.maxReadBytes) {
  throw new Error("RACCOON_DEFAULT_READ_BYTES cannot exceed RACCOON_MAX_READ_BYTES.");
}
if (config.defaultProcessLogBytes > config.maxProcessLogBytes) {
  throw new Error("RACCOON_DEFAULT_PROCESS_LOG_BYTES cannot exceed RACCOON_MAX_PROCESS_LOG_BYTES.");
}
if (config.batchConcurrency > config.maxBatchConcurrency) {
  throw new Error("RACCOON_BATCH_CONCURRENCY cannot exceed RACCOON_MAX_BATCH_CONCURRENCY.");
}

export function insideRoot(input = ".") {
  const resolved = path.resolve(config.root, input);
  const relative = path.relative(config.root, resolved);
  const escapes = relative === ".." ||
    relative.startsWith(".." + path.sep) ||
    path.isAbsolute(relative);

  if (escapes && !config.filesystemUnrestricted) {
    throw new Error(`Path escapes RACCOON_ROOT: ${input}`);
  }
  if (!config.filesystemUnrestricted) assertNotServiceConfig(relative);
  // Resolve the nearest existing ancestor, including junctions on Windows.
  let ancestor = resolved;
  const suffix = [];
  while (true) {
    try {
      const real = fs.realpathSync(ancestor);
      const canonical = path.join(real, ...suffix);
      const rel = path.relative(config.root, canonical);
      if (!config.filesystemUnrestricted && (rel === ".." || rel.startsWith(".." + path.sep) || path.isAbsolute(rel))) {
        throw new Error(`Path escapes RACCOON_ROOT through a link: ${input}`);
      }
      if (!config.filesystemUnrestricted) assertNotServiceConfig(rel);
      return canonical;
    } catch (error) {
      if (error.code !== "ENOENT") throw error;
      // Reject dangling links instead of treating them as new file paths.
      try { if (fs.lstatSync(ancestor).isSymbolicLink()) throw new Error(`Dangling link: ${input}`); }
      catch (statError) { if (statError.code !== "ENOENT") throw statError; }
      const parent = path.dirname(ancestor);
      if (parent === ancestor) throw error;
      suffix.unshift(path.basename(ancestor));
      ancestor = parent;
    }
  }
}

function assertNotServiceConfig(relative) {
  const reserved = relative.split(path.sep).some(segment => {
    const name = segment.toLowerCase();
    return name === ".raccoon-runtime" || name === ".env" || (name.startsWith(".env.") && name !== ".env.example");
  });
  if (reserved) throw new Error("Path is reserved for local service configuration and credentials.");
}

export function assertHttpSafety() {
  const loopback = new Set(["127.0.0.1", "localhost", "::1"]);
  if (config.transport === "http" && !loopback.has(config.host) && !config.token) {
    throw new Error("RACCOON_TOKEN is required when HTTP binds to a non-loopback host.");
  }
}
