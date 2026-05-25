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
  "hotkeyBehavior": "show-view"
}
```

- `icon`: supports full `mdi:name`, `app:name`, relative image paths, absolute paths, or HTTPS image URLs.
- `accentHex`: optional button/card color in launcher, quick panel, and radial menu. Use `#RRGGBB` or `#AARRGGBB`.

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
