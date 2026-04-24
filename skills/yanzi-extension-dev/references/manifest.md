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
  "globalShortcut": "Ctrl+Alt+T",
  "hotkeyBehavior": "show-view"
}
```

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

Inline C# action example:

```json
{
  "id": "csharp-echo",
  "name": "C# 输入回显",
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": ["context.read"],
  "script": {
    "source": "using OpenQuickHost.CSharpRuntime;\\n\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText);\\n    }\\n}"
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
    "source": "using OpenQuickHost.CSharpRuntime;\\n\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText.ToUpperInvariant());\\n    }\\n}"
  }
}
```
