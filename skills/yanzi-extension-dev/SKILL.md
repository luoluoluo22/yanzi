---
name: yanzi-extension-dev
description: Build, inspect, edit, and install Yanzi extensions. Use when an agent needs to create or modify `manifest.json`, author C# or PowerShell extensions, call Yanzi's local extension HTTP API, or troubleshoot extension loading and execution.
---

# Yanzi Extension Development

Use this skill when working on local extensions for Yanzi.

## Quick Start

Yanzi stores runtime extensions under the app's `Extensions/` directory. On Windows development/runtime environment, the actual path is `%LOCALAPPDATA%\OpenQuickHost\Extensions\` (and configuration/storage files under `%LOCALAPPDATA%\OpenQuickHost\`). The Local Agent API authentication Token is located in `%LOCALAPPDATA%\OpenQuickHost\appsettings.local.json` under the `"agentApiToken"` field. Each extension lives in its own folder.

Two extension shapes are supported:

1. JSON extension
2. C# or PowerShell action extension
3. Hosted view extension

For lightweight action extensions inside a single `manifest.json`, choose the runtime by task:

- use `runtime = csharp` for complex logic, JSON/HTTP/file processing, native WPF windows, P/Invoke, and strongly typed .NET APIs
- use `runtime = powershell` for Windows automation, registry/service/process/scheduled-task/system-command work, clipboard/file automation, and tasks with existing PowerShell cmdlets
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

Web app fields:

- `app.type`
- `app.entry`
- `app.singleInstance`
- `app.window.width`
- `app.window.height`
- `app.window.minWidth`
- `app.window.minHeight`

Read [references/manifest.md](references/manifest.md) when editing manifests.

## Script Execution

Current script runtimes:

- `csharp`
- `powershell`

Choose the runtime by task. C# is best for complex logic, native windows, P/Invoke, and strongly typed .NET APIs. PowerShell is often simpler and more reliable for Windows automation, registry/service/process/scheduled-task/system-command work, and tasks with existing cmdlets. If the task is essentially a cmd/bat command sequence, wrap it with PowerShell or use an external script entry instead of forcing C#.

Capability strategy:

- Prefer native language/system capabilities for feature implementation: C#/.NET/WPF/Windows APIs for complex app logic; PowerShell/cmdlets for system automation.
- Treat `YanziActionContext` as a small host concierge API for input, launch metadata, extension directories, state, and local/cloud storage.
- Do not invent host wrapper methods such as `context.SetTheme()`, `context.GetTheme()`, `context.OpenFilePicker()`, `context.ShowMessage()`, or `context.GetStateAsync<T>()`.
- For windows, dialogs, file pickers, clipboard, processes, registry, HTTP, system settings, and Win32 calls, write native C# directly unless the manifest/reference docs explicitly name a host API.
- The compiler injects the runtime namespace for `YanziActionContext`; extension source should not add host runtime usings. The app assembly is `Yanzi`; do not generate legacy product-name pack URIs, assembly references, resource paths, or assumed theme dictionaries.

Inline C# example:

```json
{
  "id": "csharp-echo",
  "name": "C# 输入回显",
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": [],
  "script": {
    "source": "public static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText);\\n    }\\n}"
  }
}
```

C# entry contract:

```csharp
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
}
```

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
- Prefer C# inline actions for new extensions unless the task is specifically Windows shell automation. For complex or large extensions, prefer multi-file format (e.g. `entryMode = entry` pointing to `main.cs`) over inline source to maintain code clarity.
- For PowerShell extensions, keep files ASCII unless non-ASCII output is required; when Chinese text is required, ensure the file is written with BOM-compatible UTF-8 handling.
- When debugging inline scripts, prefer using the editor test flow first, then inspect `logs/host.log` and `dev-debug.log` on the development machine.
- C# extensions loaded via in-process AssemblyLoadContext persist in memory. If your extension registers global event hooks (e.g. keyboard/mouse hooks via Win32 `SetWindowsHookEx`), use a `static` manager to keep the hook alive and running in the background even after `RunAsync` completes or the setting window closes.
