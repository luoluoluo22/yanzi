import {createHash} from 'node:crypto';
import {execFile} from 'node:child_process';
import {
  createReadStream,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  realpathSync,
  statfsSync,
  statSync
} from 'node:fs';
import os from 'node:os';
import {
  basename,
  dirname,
  isAbsolute,
  join,
  relative,
  resolve,
  sep
} from 'node:path';
import {promisify} from 'node:util';

const execFileAsync = promisify(execFile);
const HASH_LIMIT_BYTES = 1024 * 1024 * 1024;
const LIST_LIMIT = 200;

export const SERVER_CAPABILITY_PROTOCOL = 1;

export const SERVER_CAPABILITIES = Object.freeze([
  {
    name: 'server.status.get',
    version: 1,
    available: true,
    readOnly: true,
    scope: 'capability.invoke:server.status.get',
    description: 'Read Linux host load, memory, uptime and workspace disk usage.',
    parameters: []
  },
  {
    name: 'server.files.list',
    version: 1,
    available: true,
    readOnly: true,
    scope: 'capability.invoke:server.files.list',
    description: 'List files inside the dedicated Yanzi server workspace.',
    parameters: ['path']
  },
  {
    name: 'server.files.hash',
    version: 1,
    available: true,
    readOnly: true,
    scope: 'capability.invoke:server.files.hash',
    description: 'Calculate SHA-256 for one workspace file, up to 1 GiB.',
    parameters: ['path']
  },
  {
    name: 'server.archive.create',
    version: 1,
    available: true,
    readOnly: false,
    scope: 'capability.invoke:server.archive.create',
    description: 'Create a tar.gz archive from a workspace file or directory without exposing a shell.',
    parameters: ['source', 'output']
  }
]);

function capabilityError(code, message = code) {
  const error = new Error(message);
  error.code = code;
  return error;
}

function assertRelativePath(value, field) {
  const text = String(value ?? '').trim();
  if (!text) return '.';
  if (isAbsolute(text) || text.includes('\0')) {
    throw capabilityError('file_scope_denied', field + ' must be a relative workspace path');
  }
  return text;
}

export class ServerCapabilityRuntime {
  constructor({workspaceRoot = '/srv/yanzi-workspace'} = {}) {
    mkdirSync(workspaceRoot, {recursive: true, mode: 0o750});
    this.workspaceRoot = realpathSync(resolve(workspaceRoot));
  }

  catalog() {
    return SERVER_CAPABILITIES.map(item => ({...item, parameters: [...item.parameters]}));
  }

  #insideWorkspace(candidate) {
    const rel = relative(this.workspaceRoot, candidate);
    return rel === '' || (!rel.startsWith('..' + sep) && rel !== '..' && !isAbsolute(rel));
  }

  #existingPath(value, field = 'path') {
    const requested = assertRelativePath(value, field);
    const candidate = resolve(this.workspaceRoot, requested);
    if (!this.#insideWorkspace(candidate) || !existsSync(candidate)) {
      throw capabilityError('file_not_found', field + ' was not found in the server workspace');
    }
    const canonical = realpathSync(candidate);
    if (!this.#insideWorkspace(canonical)) {
      throw capabilityError('file_scope_denied', field + ' resolves outside the server workspace');
    }
    return canonical;
  }

  #outputPath(value) {
    const requested = assertRelativePath(value, 'output');
    if (requested === '.') throw capabilityError('invalid_output_path', 'output must name a file');
    const candidate = resolve(this.workspaceRoot, requested);
    if (!this.#insideWorkspace(candidate)) throw capabilityError('file_scope_denied');

    mkdirSync(dirname(candidate), {recursive: true, mode: 0o750});
    const canonicalParent = realpathSync(dirname(candidate));
    if (!this.#insideWorkspace(canonicalParent)) throw capabilityError('file_scope_denied');

    return join(canonicalParent, basename(candidate));
  }

  async invoke(name, argumentsValue = {}) {
    const args = argumentsValue && typeof argumentsValue === 'object' && !Array.isArray(argumentsValue)
      ? argumentsValue
      : {};

    switch (name) {
      case 'server.status.get':
        return this.#status();
      case 'server.files.list':
        return this.#list(args);
      case 'server.files.hash':
        return this.#hash(args);
      case 'server.archive.create':
        return this.#archive(args);
      default:
        throw capabilityError('unsupported_server_capability');
    }
  }

  #status() {
    const disk = statfsSync(this.workspaceRoot);
    return {
      schemaVersion: 1,
      collectedAt: new Date().toISOString(),
      host: {
        hostname: os.hostname(),
        platform: os.platform(),
        release: os.release(),
        architecture: os.arch(),
        nodeVersion: process.version
      },
      uptimeSeconds: Math.floor(os.uptime()),
      loadAverage: os.loadavg(),
      cpuCount: os.cpus().length,
      memory: {
        totalBytes: os.totalmem(),
        freeBytes: os.freemem()
      },
      workspace: {
        path: this.workspaceRoot,
        totalBytes: disk.bsize * disk.blocks,
        freeBytes: disk.bsize * disk.bavail
      }
    };
  }

  #list(args) {
    const directory = this.#existingPath(args.path ?? '.', 'path');
    if (!statSync(directory).isDirectory()) throw capabilityError('not_a_directory');

    const items = readdirSync(directory, {withFileTypes: true})
      .sort((a, b) => a.name.localeCompare(b.name))
      .slice(0, LIST_LIMIT)
      .map(entry => {
        const full = join(directory, entry.name);
        const stat = lstatSync(full);
        return {
          name: entry.name,
          type: entry.isDirectory() ? 'directory' : entry.isFile() ? 'file' : entry.isSymbolicLink() ? 'symlink' : 'other',
          size: stat.size,
          modifiedAt: stat.mtime.toISOString()
        };
      });

    return {
      scope: 'server-workspace',
      path: relative(this.workspaceRoot, directory) || '.',
      limit: LIST_LIMIT,
      items
    };
  }

  async #hash(args) {
    const file = this.#existingPath(args.path, 'path');
    const stat = statSync(file);
    if (!stat.isFile()) throw capabilityError('not_a_file');
    if (stat.size > HASH_LIMIT_BYTES) throw capabilityError('file_too_large');

    const hash = createHash('sha256');
    await new Promise((resolvePromise, reject) => {
      const input = createReadStream(file);
      input.on('data', chunk => hash.update(chunk));
      input.on('error', reject);
      input.on('end', resolvePromise);
    });

    return {
      path: relative(this.workspaceRoot, file),
      algorithm: 'sha256',
      sha256: hash.digest('hex'),
      size: stat.size
    };
  }

  async #archive(args) {
    const source = this.#existingPath(args.source, 'source');
    if (source === this.workspaceRoot) {
      throw capabilityError('archive_source_too_broad', 'archive source must be below the workspace root');
    }

    const sourceName = basename(source);
    const defaultOutput = join(
      'archives',
      sourceName.replace(/[^a-zA-Z0-9._-]+/g, '-') + '-' + Date.now() + '.tar.gz'
    );
    const output = this.#outputPath(args.output || defaultOutput);
    if (existsSync(output)) throw capabilityError('output_already_exists');

    try {
      await execFileAsync('/usr/bin/tar', ['-czf', output, '-C', dirname(source), sourceName], {
        timeout: 240000,
        maxBuffer: 1024 * 1024
      });
    } catch (error) {
      throw capabilityError('archive_failed', error.stderr || error.message);
    }

    const stat = statSync(output);
    return {
      source: relative(this.workspaceRoot, source),
      output: relative(this.workspaceRoot, output),
      format: 'tar.gz',
      size: stat.size
    };
  }
}

export function assertCapabilityAuthorization(message, name) {
  const authorization = message?.payload?.authorization;
  if (authorization?.type === 'account-owner') return;
  if (authorization?.type === 'device-grant'
      && Array.isArray(authorization.scopes)
      && authorization.scopes.includes('capability.invoke:' + name)) return;
  throw capabilityError('capability_scope_denied');
}

export async function executeCapabilityMessage(runtime, message) {
  if (message?.kind !== 'capability.invoke') throw capabilityError('unsupported_message_kind');
  const name = String(message?.payload?.name || '');
  assertCapabilityAuthorization(message, name);
  const args = message.payload.arguments ?? message.payload.payload ?? {};
  const result = await runtime.invoke(name, args);
  return {success: true, output: JSON.stringify(result)};
}
