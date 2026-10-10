<p align="center"><img src="assets/raccoon-128.png" width="72" height="72" alt="Raccoon icon"></p>

# Raccoon

Raccoon is a local-first MCP server for AI-assisted development and computer operations.

The project is intentionally independent from Yanzi/OpenQuickHost. Its job is to give AI clients a fast, persistent, auditable interface to a computer workspace.

## Current release: 0.8.0

0.8.0 adds persistent background development pipelines: dependency-aware parallel execution, shared resource locks, bounded disk logs, explicit verification, idempotent submission/retries, cancellation and failed-branch resume. The 0.9 execution-engine branch extends this with variable capture/interpolation, safe conditional DAG expressions, `retryUntil`, `wait_until`, explicit shell runners, transactional multi-file edits, scoped Git commits, isolated Git worktrees, secret references and structured HTTP probes. The branch currently exposes 98 MCP tools. See [Raccoon 0.9 开发执行引擎](EXECUTION-ENGINE.zh-CN.md) and [开发流水线说明](PIPELINES.zh-CN.md).

See [Windows deployment and ChatGPT connection guide (中文)](DEPLOYMENT.zh-CN.md). Node.js 22.16 or later is required. The server loads `.env` from its working directory; existing environment variables take precedence.

### MCP tools

Raccoon keeps its compact development-first API and also exposes a Desktop Commander-compatible surface.

**Raccoon development API**

- System/files: `ping`, `system_info`, `fs_list`, `fs_read_many`, `fs_read_chunk`, `fs_write_text`, `fs_write_chunk`, `fs_write_many`, `fs_apply_patch_many`, `fs_transaction`
- Development: `shell_run`, `project_build_test`, `git_summary`, `git_commit_scope`, `worktree_task`, `wait_until`, `http_probe`, `secret_list`, `android_dev_cycle`, `cloudflare_deploy_verify`
- Pipelines: `dev_pipeline_validate`, `dev_pipeline_start`, `dev_pipeline_status`, `dev_pipeline_list`, `dev_pipeline_wait`, `dev_pipeline_logs`, `dev_pipeline_resume`, `dev_pipeline_cancel`; nodes support output capture, `${node.output}` interpolation, safe `if` expressions and `retryUntil`
- Persistent processes: `process_start`, `process_read`, `process_write`, `process_kill`, `process_list`

**Native development feedback API**

- Android/ADB: `adb_devices`, `adb_shell`, `adb_push`, `adb_pull`, `adb_install`, `adb_uninstall`, `adb_screenshot`, `adb_input`, `adb_app`, `adb_packages`, `adb_logcat_start`, `adb_ui_dump`, `adb_find_and_tap`, `android_app_snapshot`
- Visual feedback: `screenshot_capture`, `visual_compare`
- Foreground control: `foreground_control_acquire`, `foreground_control_heartbeat`, `foreground_control_pause`, `foreground_control_release`, `foreground_control_status`
- Windows desktop automation: `desktop_actions`, `desktop_windows` — mouse/keyboard/window actions automatically claim foreground control and keep one overlay for the whole batch
- SQLite streaming: `db_info`, `db_query_start`, `db_query_next`, `db_query_close`, `db_query_sessions`, `db_execute`
- Build cache: `build_cache_run`, `build_cache_stats`, `build_cache_clear`
- Resource control: `resource_snapshot`, `process_resource_snapshot`, `resource_governor_configure`, `resource_governor_status`, `process_resource_watch_start`, `process_resource_watch_read`, `process_resource_watch_stop`, `process_resource_watch_list`

**Desktop Commander compatibility API**

- Device/config: `list_devices`, `who_am_i`, `shutdown`, `get_config`, `set_config_value`; configured Raccoon peers are federated and compatibility tools route by optional `deviceId`
- Rich files: `read_file`, `read_multiple_files`, `write_file`, `write_pdf`, `create_directory`, `list_directory`, `move_file`, `get_file_info`, `edit_block`
- Search: `start_search`, `get_more_search_results`, `stop_search`, `list_searches`
- Terminal/processes: `start_process`, `read_process_output`, `interact_with_process`, `force_terminate`, `list_sessions`, `list_processes`, `kill_process`
- Observability/onboarding: `get_usage_stats`, `get_recent_tool_calls`, `give_feedback_to_desktop_commander`, `give_feedback_to_raccoon`, `get_prompts`
- Extra file operations: `copy_file`, `delete_path`

`read_file` supports line pagination/tail reads, URLs, PNG/JPEG/GIF/WebP, Excel sheets/ranges, PDF text plus rendered page images, and DOCX outline/raw XML modes. `write_pdf` supports markdown/HTML/CSS/SVG creation through the installed Chromium/Edge browser and page insert/delete modifications.

### Multi-device federation

Raccoon can treat other Raccoon HTTP endpoints as devices. Configure peers through `set_config_value` using the `remoteDevices` key:

```json
[
  {
    "id": "dev-pc",
    "name": "Development PC",
    "url": "http://192.168.1.20:3766/mcp",
    "token": "peer-token"
  }
]
```

`list_devices` returns the local node plus configured peers with online/offline state. Desktop Commander-compatible tools accept optional `deviceId`; when it refers to a peer, the MCP call is transparently forwarded to that Raccoon node.

## Core design

The main performance target is MCP round-trip count.

A normal edit loop can be reduced to:

```text
fs_read_many
    ↓
fs_apply_patch_many
    ↓
project_build_test
    ↓
git_summary
```

Long-running commands no longer need to be restarted or have their entire output returned repeatedly:

```text
process_start
     ↓
sessionId
     ↓
process_read(cursor)
     ↓
process_write(...)
     ↓
process_read(nextCursor)
```

`process_read` uses event cursors rather than a fixed line count. Only new buffered output needs to cross MCP after the first read.

## Capacity and concurrency

Large files are not limited by the MCP single-message size: use `fs_read_chunk` / `fs_write_chunk` with byte offsets to traverse or modify files of arbitrary size while keeping memory bounded. The capacity smoke suite verifies random-access I/O against a 1 GiB sparse file.

Current request defaults and configured ceilings:

| Capability | Normal/default request | Configured ceiling |
| --- | ---: | ---: |
| Bulk read per file | 64 MiB | 512 MiB |
| Chunk read/write | 8 MiB read default | 512 MiB |
| MCP HTTP/stdio message | — | 512 MiB |
| Retained process log/session | 256 MiB | 2 GiB |
| Shell output/call | 4 MiB | 512 MiB |
| Build-step output | 2 MiB | 512 MiB |
| Git patch output | 2 MiB | 512 MiB |
| Process read/call | 1 MiB | 512 MiB |
| Files per batch | caller-selected | 10,000 |
| Batch concurrency | 32 | 256 |
| Patch files/call | caller-selected | 5,000 |
| Replacements/file | caller-selected | 10,000 |
| Workflow steps/call | caller-selected | 10,000 |
| Retained process sessions | — | 4,096 |
| Search results/session | 1,000 default | 1,000,000 |

Reverse proxies and MCP clients can impose lower per-request limits than Raccoon itself. Chunk/range APIs are therefore the preferred path for very large data rather than relying on a single giant JSON-RPC request.

These are request/session budgets, not whole-file limits. A 10 GiB file can still be processed with repeated chunk calls without increasing the per-request budget.

File writes and patches use in-process per-file locks. Multi-file read/write/patch operations can run concurrently; writes to the same canonical file remain serialized to prevent lost updates. `project_build_test` accepts optional `parallelism` for independent workflow steps.

If two AI chats attempt to patch the same old text concurrently:

1. one caller acquires the file lock and writes;
2. the second waits;
3. the second re-reads the new file after acquiring the lock;
4. if its expected old text is stale, the patch is rejected instead of silently overwriting the first caller.

This does not replace Git branches/worktrees for fully independent parallel development, but it prevents a common lost-update race inside one Raccoon service.

## Persistent process state

Process sessions live at the Raccoon service level, not inside one MCP request.

This matters for stateless Streamable HTTP: a later MCP HTTP request can use the `sessionId` from an earlier `process_start` request and continue reading or writing the same local process.

Output is buffered as ordered stdout/stderr events. The default retained buffer is 256 MiB per process and can be changed with `RACCOON_MAX_PROCESS_LOG_BYTES`; cursor reads prevent the whole retained log from crossing MCP on each call.

Finished sessions older than one hour are cleaned up opportunistically. If the retained session limit is reached, old finished sessions are evicted before new processes are rejected.

## Install

```powershell
cd F:\Desktop\kaifa\raccoon-mcp
npm install
```

## stdio

```powershell
$env:RACCOON_ROOT="F:\Desktop\kaifa"
$env:RACCOON_ENABLE_SHELL="1"
npm start
```

## Streamable HTTP

```powershell
$env:RACCOON_ROOT="F:\Desktop\kaifa"
$env:RACCOON_TOKEN="replace-with-a-random-secret"
$env:RACCOON_ENABLE_SHELL="1"
npm run start:http
```

Default MCP endpoint:

```text
http://127.0.0.1:3766/mcp
```

Health endpoint:

```text
http://127.0.0.1:3766/health
```

HTTP binds to loopback by default. Raccoon refuses to bind HTTP to a non-loopback host unless `RACCOON_TOKEN` is configured.

## Public ChatGPT connection with Cloudflare

For an existing Cloudflare domain, `scripts/cloudflare-setup.js` configures a dedicated Tunnel and enables the single-owner OAuth flow built on the MCP SDK. Cloudflare credentials and OAuth state remain in the ignored `.raccoon-runtime` directory. Public requests require OAuth; the local static bearer token is not accepted on the public hostname. See [the Chinese deployment guide](DEPLOYMENT.zh-CN.md#cloudflare-免费隧道方案) for permissions, configuration, and validation steps.

## Workspace boundary

All filesystem, Git, workflow, and process working-directory paths are resolved under `RACCOON_ROOT`, including existing symbolic links and Windows junctions. This is a file-tool boundary, not an OS sandbox: enabled shell commands retain the operating-system user's permissions.

Set `RACCOON_FILESYSTEM_UNRESTRICTED=1` to disable this file-path boundary and reserved `.env`/`.raccoon-runtime` path checks. Absolute paths on any drive and links outside the root are then accepted; relative paths still start at `RACCOON_ROOT`. Windows account permissions and read-only mode still apply. Restart the service after changing this setting.

`main.cs` edits must use `fs_transaction` with `validate.command` set to the project's compile command. Ordinary text writes, byte chunks and patch tools reject this filename. Transactions back up original bytes under `.raccoon-runtime/source-backups`, strictly decode UTF-8, write with BOM through a sibling temporary file, read back with `Get-Content -Encoding utf8` on Windows, and report byte size, Chinese character/quote counts and SHA-256. Failed writes or compilation restore the exact original bytes and BOM. A Unicode scan is performed before writing; C# syntax and semantics are checked by the supplied compiler command. Shell commands and external editors run with OS permissions and must follow this workflow themselves.

For example:

```powershell
$env:RACCOON_ROOT="F:\Desktop\kaifa"
```

allows Raccoon to work across development projects in `kaifa` while rejecting filesystem traversal outside that root.

## Tests

```powershell
npm run verify
```

The stdio smoke suite verifies:

- concurrent patch locking and stale-write rejection
- project workflow execution
- persistent process output
- process stdin
- process termination
- Git summary

The HTTP smoke suite verifies that process state survives across separate stateless MCP HTTP requests.

The compatibility smoke suite verifies all 30 Desktop Commander-equivalent tool names are present and exercises device/config calls, real two-node federation and deviceId routing, text range reads, image and URL reads, recursive directory operations, progressive search, Excel read/edit/metadata, DOCX create/read/edit, PDF create/read/render/modify, multi-file reads, interactive processes, shell switching, managed and system process termination, session/process listing, usage/history, prompts, feedback routing, and graceful shutdown.

The capacity smoke suite verifies 1 GiB sparse random-access I/O, 256-file write/read/patch operations at 256 requested concurrency, streaming search/read/metadata on a >8 MiB text file, an 8 MiB persistent process log, workflow execution at 256 requested parallelism, a 20 MiB single HTTP tool-call write (larger than the old 16 MiB request ceiling), 64 simultaneous HTTP MCP calls, and 64 concurrent appends to the same locked file without lost writes.

The feature smoke suite verifies a real Windows desktop screenshot returned as an MCP image, pixel comparison, live ADB device discovery and Android screenshot, a safe ADB push/pull round trip plus logcat session, 2,000-row SQLite cursor streaming, build-cache miss/hit/output restoration and input invalidation, system/process resource sampling, governor-enforced heavy-work concurrency, and automatic process priority throttling.

## Roadmap

Next priorities:

1. bind `worktree_task` automatically to AI workspace sessions, then add merge/rebase handoff
2. Android one-call development cycle and UI-tree automation
3. Cloudflare deployment/traffic/API verification workflows built on `wait_until` + `http_probe`
4. LSP/repository symbol index and AST-aware edits
5. Git snapshot / rollback tools
6. additional database adapters beyond SQLite (PostgreSQL/MySQL/DuckDB)
7. richer profiling, tracing and long-running task orchestration

## Status

Early development. APIs may change.
