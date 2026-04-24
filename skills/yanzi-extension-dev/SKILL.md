---
name: yanzi-extension-dev
description: Build, inspect, edit, and install Yanzi extensions. Use when an agent needs to create or modify `manifest.json`, author PowerShell script extensions, call Yanzi's local extension HTTP API, or troubleshoot extension loading and execution.
---

# Yanzi Extension Development

Use this skill when working on local extensions for Yanzi.

## Quick Start

Yanzi stores runtime extensions under the app's `Extensions/` directory. Each extension lives in its own folder.

Two extension shapes are supported:

1. JSON extension
2. Script extension

For lightweight script extensions inside a single `manifest.json`, prefer:

- `runtime = powershell`
- `entryMode = inline`
- `script.source`

Minimum layout:

```text
Extensions/
  my-extension/
    manifest.json
```

Script extension layout:

```text
Extensions/
  my-script/
    manifest.json
    main.ps1
```

## Manifest Model

Common fields:

- `id`
- `name`
- `version`
- `category`
- `description`
- `keywords`
- `globalShortcut`
- `hotkeyBehavior`

JSON action fields:

- `openTarget`
- `queryPrefixes`
- `queryTargetTemplate`

Script fields:

- `runtime`
- `entryMode`
- `entry`
- `script.source`
- `permissions`

Hosted view fields:

- `hostedView.type`
- `hostedView.title`
- `hostedView.description`
- `hostedView.inputLabel`
- `hostedView.inputPlaceholder`
- `hostedView.outputLabel`
- `hostedView.actionButtonText`
- `hostedView.actionType`
- `hostedView.emptyState`

Read [references/manifest.md](references/manifest.md) when editing manifests.

## Script Execution

Current script runtime:

- `powershell`

Entry file example:

```json
{
  "id": "script-clipboard",
  "name": "读取剪贴板",
  "runtime": "powershell",
  "entry": "main.ps1",
  "permissions": ["clipboard.read"]
}
```

PowerShell conventions:

- `param(...)` must be the first statement
- then set output encoding if needed
- write user-facing result to stdout
- write failure detail to stderr or throw

The host passes:

- `-InputText`
- `-ContextPath`
- env `YANZI_INPUT`
- env `YANZI_CONTEXT_PATH`
- env `YANZI_EXTENSION_ID`
- env `YANZI_EXTENSION_DIR`
- env `YANZI_LAUNCH_SOURCE`

Read [references/script-extensions.md](references/script-extensions.md) when authoring scripts.

## Local Agent API

Yanzi exposes a localhost HTTP API for extension CRUD. It is intended for same-machine agents.

Default:

- base URL: `http://127.0.0.1:53919`
- header: `X-Yanzi-Token`

Useful endpoints:

- `GET /health`
- `GET /v1/extensions`
- `GET /v1/extensions/template`
- `GET /v1/extensions/{id}`
- `POST /v1/extensions`
- `PUT /v1/extensions/{id}`
- `PATCH /v1/extensions/{id}/rename`
- `PATCH /v1/extensions/{id}/shortcut`
- `DELETE /v1/extensions/{id}`

Read [references/local-agent-api.md](references/local-agent-api.md) for request and response shapes.

## Working Rules

- Prefer editing extension folders through the local API when you are acting as an external agent.
- When working inside the Yanzi codebase, update both the runtime behavior and the bundled skill docs if behavior changes.
- For script extensions, keep PowerShell files ASCII unless non-ASCII output is required; when Chinese text is required, ensure the file is written with BOM-compatible UTF-8 handling.
- When debugging inline scripts, prefer using the editor test flow first, then inspect `logs/host.log` and `logs/dev-debug.log` on the development machine.
