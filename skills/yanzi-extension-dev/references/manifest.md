# Manifest Reference

Common fields:

```json
{
  "id": "my-extension",
  "name": "My Extension",
  "version": "0.1.0",
  "category": "扩展",
  "description": "What this extension does",
  "keywords": ["keyword-1", "keyword-2"],
  "icon": "mdi:puzzle-outline",
  "accentHex": "#FF10B981",
  "globalShortcut": "Ctrl+Alt+T",
  "hotkeyBehavior": "show-view",
  "requires": ["git"]
}
```

- `icon`: supports full `mdi:name`, `app:name`, relative image paths, absolute paths, or HTTPS image URLs.
- `accentHex`: optional button/card color in launcher, quick panel, and radial menu. Use `#RRGGBB` or `#AARRGGBB`.
- `requires`: declares capabilities/runtime dependencies the mini-app needs. The host resolves these before launch. Automatic system providers currently support `git`, `python`, `node`, and `ffmpeg`, including minimum versions such as `python>=3.12`, `node>=22`, and `ffmpeg>=8`.
- `provides`: declares capabilities exported by this mini-app to Yanzi's capability network. Do not use `provides` for dependencies.

JSON extension example:

```json
{
  "id": "open-docs",
  "name": "打开文档",
  "openTarget": "F:\\Desktop\\docs\\README.txt"
}
```

Query command example:

```json
{
  "id": "google-search",
  "name": "谷歌",
  "queryPrefixes": ["谷歌", "google", "gg"],
  "queryTargetTemplate": "https://www.google.com/search?q={query}"
}
```

PowerShell file script example:

```json
{
  "id": "script-clipboard",
  "name": "读取剪贴板",
  "runtime": "powershell",
  "entry": "main.ps1",
  "permissions": ["clipboard.read"]
}
```

Capability strategy:

- Choose the runtime by task: C#/.NET/WPF/Windows APIs for complex app logic, native windows, P/Invoke, and strongly typed APIs; PowerShell/cmdlets for Windows automation, registry, services, processes, scheduled tasks, and simple command sequences.
- Use `YanziActionContext` only for documented host concierge capabilities: input, launch metadata, extension directories, state, and storage.
- Do not invent undocumented host methods such as `context.SetTheme()`, `context.GetTheme()`, `context.OpenFilePicker()`, `context.ShowMessage()`, or `context.GetStateAsync<T>()`.
- The compiler injects the runtime namespace for `YanziActionContext`; extension source should not add host runtime usings. The app assembly is `Yanzi`; do not generate legacy product-name pack URIs, assembly references, resource paths, or assumed theme dictionaries.

Inline C# action example:

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

Inline single-file script example:

```json
{
  "id": "inline-clipboard",
  "name": "读取剪贴板（内联）",
  "runtime": "powershell",
  "entryMode": "inline",
  "permissions": ["clipboard.read"],
  "script": {
    "source": "param([string]$InputText = \"\", [string]$ContextPath = \"\")\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n$text = Get-Clipboard -Raw\nif ([string]::IsNullOrWhiteSpace($text)) { Write-Output \"当前剪贴板为空。\" } else { Write-Output $text.Trim() }"
  }
}
```

Hosted C# action example:

```json
{
  "id": "sample-text-workbench",
  "name": "文本处理台",
  "runtime": "csharp",
  "entryMode": "inline",
  "hostedView": {
    "type": "split-workbench",
    "title": "文本处理台",
    "actionType": "script",
    "inputLabel": "输入",
    "outputLabel": "结果",
    "actionButtonText": "执行"
  },
  "script": {
    "source": "public static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText.ToUpperInvariant());\\n    }\\n}"
  }
}
```

C# multi-file extension example (with startup):

```json
{
  "id": "keyboard-mapper",
  "name": "按键修改器",
  "runtime": "csharp",
  "entryMode": "entry",
  "entry": "main.cs",
  "startup": {
    "mode": "on_app_launch"
  }
}
```

Idle trigger example:

```json
{
  "startup": {
    "idle": {
      "enabled": true,
      "afterMinutes": 5,
      "repeatMinutes": 10,
      "pauseWhenFullscreen": true
    }
  }
}
```

- `startup.idle` is a generic mini-app trigger. The host treats the PC as idle only after keyboard and mouse have had no input for `afterMinutes` **and** there is no fullscreen foreground window.
- `repeatMinutes = 0` means once per continuous idle period. A positive value allows another run after that many minutes while the same idle period continues.
- Idle launches use `context.LaunchSource == "idle"`; headless/background work should branch on this value and avoid opening its normal UI.

Web app extension example:

```json
{
  "id": "notes-app",
  "name": "笔记应用",
  "icon": "app/favicon.ico",
  "runtime": "web-app",
  "app": {
    "type": "webview",
    "entry": "app/index.html",
    "singleInstance": true,
    "window": {
      "width": 1180,
      "height": 760
    }
  }
}
```
