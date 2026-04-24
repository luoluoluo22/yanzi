# 扩展编写指南

这份指南用于帮助用户和 AI 助手编写燕子扩展。扩展本质上是一个 `manifest.json`，根据任务复杂度选择不同字段。

## 如何选择扩展类型

### 打开文件、目录、网页或系统设置

使用 `openTarget`，这是最简单、最稳定的扩展。

```json
{
  "id": "open-downloads",
  "name": "打开下载目录",
  "version": "0.1.0",
  "category": "目录",
  "description": "打开当前用户的下载目录。",
  "keywords": ["downloads", "下载", "xiazai"],
  "icon": "mdi:folder",
  "openTarget": "C:\\Users\\你的用户名\\Downloads"
}
```

### 搜索或带参数打开网页

使用 `queryPrefixes` 和 `queryTargetTemplate`。用户输入前缀后，剩余文本会替换 `{query}`。

```json
{
  "id": "google-search",
  "name": "谷歌搜索",
  "version": "0.1.0",
  "category": "搜索",
  "description": "用默认浏览器打开 Google 搜索。",
  "keywords": ["google", "谷歌", "gg", "guge"],
  "icon": "mdi:search",
  "queryPrefixes": ["谷歌", "google", "gg", "guge"],
  "queryTargetTemplate": "https://www.google.com/search?q={query}"
}
```

### 处理选中文本、剪贴板、文件路径或调用 API

优先使用 C# 内联动作。快捷面板触发时，宿主会把选中内容传给 `context.InputText`。

```json
{
  "id": "csharp-selection-summary",
  "name": "选中内容摘要",
  "version": "0.1.0",
  "category": "C#",
  "description": "读取快捷面板传入的选中文本。",
  "keywords": ["csharp", "selection", "选中", "摘要"],
  "icon": "mdi:code",
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": ["context.read"],
  "script": {
    "source": "using OpenQuickHost.CSharpRuntime;\\n\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var text = string.IsNullOrWhiteSpace(context.InputText) ? \\\"没有收到选中内容。\\\" : context.InputText.Trim();\\n        return Task.FromResult($\\\"来源: {context.LaunchSource}\\\\n长度: {text.Length}\\\\n\\\\n{text}\\\");\\n    }\\n}"
  }
}
```

C# 入口必须提供：

```csharp
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        return Task.FromResult(context.InputText);
    }
}
```

常用上下文字段：

- `context.InputText`：启动器或快捷面板传入的文本
- `context.ExtensionDirectory`：当前扩展目录
- `context.LaunchSource`：触发来源
- `context.Now`：执行时间
- `context.Permissions`：manifest 声明的权限

### Windows 自动化

PowerShell 仍适合调用系统命令、剪贴板、进程和文件自动化。

```json
{
  "id": "clipboard-read",
  "name": "读取剪贴板",
  "version": "0.1.0",
  "category": "脚本",
  "description": "读取当前剪贴板文本。",
  "keywords": ["clipboard", "剪贴板"],
  "icon": "mdi:clipboard",
  "runtime": "powershell",
  "entryMode": "inline",
  "permissions": ["clipboard.read"],
  "script": {
    "source": "param([string]$InputText = \\\"\\\", [string]$ContextPath = \\\"\\\")\\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\\n$text = Get-Clipboard -Raw\\nif ([string]::IsNullOrWhiteSpace($text)) { Write-Output \\\"当前剪贴板为空。\\\" } else { Write-Output $text.Trim() }"
  }
}
```

### 需要界面输入和输出

使用 `hostedView`。如果 `actionType` 是 `script`，按钮会执行当前扩展的 C# 或 PowerShell 入口，并把标准输出显示在右侧。

```json
{
  "id": "text-workbench",
  "name": "文本处理台",
  "version": "0.1.0",
  "category": "工具",
  "description": "在宿主窗口中输入文本并执行 C# 动作。",
  "keywords": ["text", "文本", "workbench"],
  "icon": "mdi:terminal",
  "runtime": "csharp",
  "entryMode": "inline",
  "hostedView": {
    "type": "split-workbench",
    "title": "文本处理台",
    "description": "左侧输入文本，右侧显示执行结果。",
    "inputLabel": "输入",
    "inputPlaceholder": "输入要处理的文本...",
    "outputLabel": "结果",
    "actionButtonText": "执行",
    "actionType": "script",
    "emptyState": "结果会显示在这里。"
  },
  "script": {
    "source": "using OpenQuickHost.CSharpRuntime;\\n\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText.ToUpperInvariant());\\n    }\\n}"
  }
}
```

## 图标写法

`icon` 支持：

- 内置图标：`mdi:search`、`mdi:translate`、`mdi:folder`、`mdi:clipboard`、`mdi:code`
- 应用别名：`app:wechat`、`app:qq`、`app:google`、`app:selection`
- 图片路径：扩展目录下相对路径，例如 `icons/logo.png`，或绝对路径、HTTPS 图片地址

## 给 AI 的提示词

```text
请为燕子 Yanzi 生成一个单文件 manifest.json 扩展。

要求：
1. 只输出合法 JSON，不要 Markdown 代码块。
2. 优先使用 runtime = "csharp"、entryMode = "inline"、script.source。
3. C# 源码必须包含 public static class YanziAction，并实现 public static Task<string> RunAsync(YanziActionContext context)。
4. 如果只是打开文件、目录、网页或系统协议，使用 openTarget，不要写脚本。
5. 如果是搜索类命令，使用 queryPrefixes 和 queryTargetTemplate，模板里用 {query}。
6. 如果需要宿主界面，使用 hostedView，actionType 优先使用 script。
7. 必须包含 id、name、version、category、description、keywords。
8. icon 优先使用内置值，例如 mdi:search、mdi:folder、mdi:clipboard、mdi:code、mdi:translate。
9. 不要写 null 字段，不要补充燕子未支持的字段。
10. 输出的 JSON 要能直接保存为 manifest.json。

我要的扩展功能是：
在这里描述你的需求。
```

## 调试建议

- 在扩展编辑器里优先点 `测试执行`
- 表单和 JSON 以最后编辑的一侧为准
- C# 编译产物会缓存在扩展目录的 `.yanzi-csharp-cache`
- 运行日志在 `logs/host.log`
- 开发机调试日志在 `logs/dev-debug.log`
