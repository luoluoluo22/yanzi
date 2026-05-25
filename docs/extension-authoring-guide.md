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

### 固定到顶部后返回一组列表结果

如果想让某个扩展在“固定到顶部”后，像 `@文件` 一样继续输入关键词并返回多条列表结果，使用 `searchProvider`。

当前第一版支持：

- `searchProvider.type = "folder"`：在指定目录下搜索文件或文件夹
- `searchProvider.type = "script"`：运行扩展自己的脚本，并返回一组 JSON 结果项
- `searchProvider.path`：搜索根目录；如果省略且 `openTarget` 本身是目录，会自动回退到 `openTarget`
- `searchProvider.aliases`：固定到顶部后支持 `@别名 关键词`
- `searchProvider.includeSubdirectories` / `includeFiles` / `includeDirectories` / `maxResults`：控制搜索范围

```json
{
  "id": "download-folder-search",
  "name": "下载",
  "version": "0.1.0",
  "category": "目录搜索",
  "description": "固定到顶部后，在下载目录中搜索文件。",
  "keywords": ["下载", "文件夹", "folder", "search"],
  "icon": "mdi:folder-search-outline",
  "openTarget": "C:\\Users\\你的用户名\\Downloads",
  "searchProvider": {
    "type": "folder",
    "aliases": ["下载", "downloads"],
    "includeSubdirectories": true,
    "includeFiles": true,
    "includeDirectories": false,
    "maxResults": 120
  }
}
```

使用方式：

- 先把这个扩展固定到顶部
- 点顶部标签后直接输入关键词
- 或直接输入 `@下载 关键词`
- 或在 `扩展` 标签里输入 `下载 ` 显示全部结果，再输入 `下载 关键词` 显示过滤结果

当前注意点：

- 只有固定到顶部的扩展才支持 `@别名 关键词` 进入对应 scope
- 这是主界面列表搜索，不是运行脚本后自己弹出结果窗
- 目前内置了 `folder` 和 `script` provider，后续可以再扩展 `table`、`api` 等类型

### 通过脚本返回动态结果列表

如果目录搜索不够，或者结果来自 API、缓存、数据库、脚本计算，可以用 `searchProvider.type = "script"`。

约定：

- 扩展本身仍然要提供 `runtime` 和脚本入口
- 宿主会把查询词通过 `context.InputText` 传给脚本
- 脚本返回值必须是 JSON 数组，或 `{ success, errorMessage, items }` 这种包裹对象
- 每个结果项建议包含：`id`、`title`、`subtitle`、`kind`、`openTarget`、`keywords`、`accentHex`
- `kind` 目前支持：`file`、`folder`、`record`、`url`、`script`、`api`

```json
{
  "id": "demo-script-search",
  "name": "脚本结果",
  "version": "0.1.0",
  "category": "动态结果",
  "description": "通过脚本返回一组动态结果项。",
  "keywords": ["脚本", "搜索", "结果"],
  "icon": "mdi:code-json",
  "runtime": "csharp",
  "entryMode": "inline",
  "searchProvider": {
    "type": "script",
    "aliases": ["脚本结果", "script"],
    "maxResults": 50
  },
  "script": {
    "source": "using System.Text.Json;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var q = (context.InputText ?? string.Empty).Trim();\\n        var items = new object[]\\n        {\\n            new { id = \"doc-1\", title = \"接口文档\", subtitle = \"脚本生成的示例结果\", kind = \"record\", openTarget = \"https://example.com/docs\", keywords = new[] { \"文档\", q }, accentHex = \"#FF10B981\" },\\n            new { id = \"tool-1\", title = \"打开工具页\", subtitle = \"支持 URL / 文件 / 普通记录\", kind = \"url\", openTarget = \"https://example.com/tools?q=\" + System.Uri.EscapeDataString(q), keywords = new[] { \"工具\", q }, accentHex = \"#FF06B6D4\" }\\n        };\\n        return Task.FromResult(JsonSerializer.Serialize(items));\\n    }\\n}"
  }
}
```

### 扩展能力策略

按任务选择运行时，不要机械固定用 C#。复杂业务逻辑、JSON/HTTP/文件处理、原生 WPF 窗口、P/Invoke、需要强类型 .NET API 时，优先用 C#；Windows 自动化、注册表、服务、进程、计划任务、系统命令、已有 PowerShell cmdlet 能直接完成的任务，优先用 PowerShell；如果需求本质上是一串 cmd/bat 命令，优先用 PowerShell 包装执行或使用外部脚本入口。

宿主只做“管家”：负责搜索框入口、`context.InputText` 输入传递、扩展目录、状态、本地/云端存储，以及 `hostedViewXaml` 的少量受控动作。不要把 `YanziActionContext` 当成万能宿主 SDK。只使用文档明确列出的成员；不要发明 `context.SetTheme()`、`context.GetTheme()`、`context.OpenFilePicker()`、`context.ShowMessage()`、`context.GetStateAsync<T>()` 这类不存在的方法。需要文件选择、消息框、窗口、系统设置、剪贴板、进程、注册表或 Win32 调用时，优先用 C# / PowerShell / Windows 命令的原生能力。

编译器会自动导入 `YanziActionContext` 所在命名空间，C# 扩展源码不需要显式写宿主运行时 using。产品名和应用程序集名是 `Yanzi`；不要生成旧产品名相关程序集引用、pack URI、资源路径，或假设存在内置主题资源字典。

### 处理选中文本、剪贴板、文件路径或调用 API

C# 适合复杂逻辑和原生窗口。快捷面板触发时，宿主会把选中内容传给 `context.InputText`。

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
    "source": "public static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var text = string.IsNullOrWhiteSpace(context.InputText) ? \\\"没有收到选中内容。\\\" : context.InputText.Trim();\\n        return Task.FromResult($\\\"来源: {context.LaunchSource}\\\\n长度: {text.Length}\\\\n\\\\n{text}\\\");\\n    }\\n}"
  }
}
```

C# 入口必须提供：

```csharp
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
- `context.ExtensionDataDirectory`：当前扩展的数据目录
- `context.LaunchSource`：触发来源
- `context.Now`：执行时间
- `context.Permissions`：manifest 声明的权限
- `context.State`：宿主视图或扩展状态
- `await context.SetStateAsync(...)`：更新状态
- `context.Storage`：本地/云端存储 helper

### Windows 自动化

PowerShell 适合 Windows 自动化、注册表、服务、进程、计划任务、系统命令、剪贴板、文件自动化，以及已有 cmdlet 能直接完成的任务。不要为了简单 PowerShell/cmd 命令硬套 C#。

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

### 需要宿主内工作区或自定义界面

优先使用 `hostedViewXaml`。它适合便签、设置页、工作区、仪表盘、预览器、轻量编辑器这类“寄生在宿主里的界面”。

`hostedViewXaml` 的现实边界要先说清楚：

- 它会直接解析 WPF XAML，但更接近“受控宿主视图”，不是完整原生 WPF 应用
- 不支持 `x:Class`、代码隐藏、手写事件处理函数
- 按钮动作通过 `oqh:HostedViewBridge.Action` 声明
- 根元素可用 `oqh:HostedViewBridge.LoadedAction` 做首次加载
- 状态通过 `hostedViewXaml.state` 提供，XAML 中用 `{Binding [key]}` 访问
- 当前更适合扁平状态和轻量交互，不适合复杂列表模板、树结构、原生拖拽和多窗口工具

当前最常用的宿主动作：

- `setState`
- `runScript`
- `loadStorage`
- `saveStorage`
- `close`

#### 模板 4：双栏编辑器 / 便签工作区

适合左编辑右预览、左输入右结果的典型工作区。

```json
{
  "id": "sticky-note-workbench",
  "name": "简易便签",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "在宿主窗口中打开一个便签工作区。",
  "keywords": ["便签", "记事本", "note"],
  "icon": "mdi:note-text-outline",
  "hostedViewXaml": {
    "type": "xaml",
    "title": "简易便签",
    "window": {
      "width": 960,
      "height": 720,
      "minWidth": 760,
      "minHeight": 520
    },
    "state": {
      "note": "",
      "preview": "先在左侧输入内容，这里会显示便签结果。"
    },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\" oqh:HostedViewBridge.PreferredFocus=\"NoteBox\" oqh:HostedViewBridge.LoadedAction=\"loadStorage;path=note;key=note.txt;scope=both;defaultValue=\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\"0\"><TextBlock Text=\"便签内容\" Foreground=\"White\" FontSize=\"14\" FontWeight=\"SemiBold\" Margin=\"0,0,0,10\"/><TextBox x:Name=\"NoteBox\" Text=\"{Binding [note], UpdateSourceTrigger=PropertyChanged}\" AcceptsReturn=\"True\" VerticalScrollBarVisibility=\"Auto\" TextWrapping=\"Wrap\" MinHeight=\"320\" Padding=\"12\"/><Button Content=\"保存便签\" Margin=\"0,12,0,0\" oqh:HostedViewBridge.Action=\"saveStorage;path=note;key=note.txt;scope=both;successMessage=便签已保存。|setState;path=preview;valueFrom=note\"/></StackPanel><Border Grid.Column=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"10\" Padding=\"12\"><TextBlock Text=\"{Binding [preview]}\" TextWrapping=\"Wrap\" Foreground=\"White\"/></Border></Grid>"
  }
}
```

#### 模板 4.1：设置页 / 表单型工作区

适合配置保存、路径输入、账号信息、偏好设置。

设计重点：

- 纵向表单布局
- `loadStorage` 回填
- `saveStorage` 保存
- 底部状态文案

#### 模板 4.2：脚本工具台

适合输入内容后执行 C# / PowerShell，再把结果显示在右侧。

设计重点：

- 左侧输入区
- 右侧只读输出区
- 底部状态栏
- `runScript;inputFrom=...;outputTo=...`

#### 模板 4.3：仪表盘 / 状态面板

适合展示关键状态、日志摘要、快速动作。

设计重点：

- 多卡片布局
- 头部摘要
- 中部状态卡
- 底部操作按钮

```json
{
  "id": "ops-dashboard-demo",
  "name": "状态仪表盘",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "在宿主里展示关键状态卡片和快速操作。",
  "keywords": ["仪表盘", "状态", "dashboard"],
  "icon": "mdi:view-dashboard-outline",
  "hostedViewXaml": {
    "type": "xaml",
    "title": "状态仪表盘",
    "window": { "width": 1100, "height": 760, "minWidth": 820, "minHeight": 560 },
    "state": {
      "summary": "今日任务 5 项",
      "health": "运行正常",
      "recentLog": "暂无新日志",
      "status": "准备就绪"
    },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"16\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"16\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><StackPanel><TextBlock Text=\"状态仪表盘\" FontSize=\"24\" FontWeight=\"SemiBold\" Foreground=\"White\"/><TextBlock Text=\"用多卡片布局展示关键指标和最近状态\" Foreground=\"#FF9CA3AF\" Margin=\"0,6,0,0\"/></StackPanel><Grid Grid.Row=\"2\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><Border Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"今日摘要\" Foreground=\"#FF9CA3AF\"/><TextBlock Text=\"{Binding [summary]}\" Foreground=\"White\" FontSize=\"20\" FontWeight=\"SemiBold\" Margin=\"0,10,0,0\"/></StackPanel></Border><Border Grid.Column=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"运行状态\" Foreground=\"#FF9CA3AF\"/><TextBlock Text=\"{Binding [health]}\" Foreground=\"#FF34D399\" FontSize=\"20\" FontWeight=\"SemiBold\" Margin=\"0,10,0,0\"/></StackPanel></Border><Border Grid.Column=\"4\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"快速动作\" Foreground=\"#FF9CA3AF\"/><Button Content=\"刷新摘要\" Margin=\"0,12,0,0\" oqh:HostedViewBridge.Action=\"setState;path=status;value=已刷新摘要\"/></StackPanel></Border></Grid><Border Grid.Row=\"4\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"最近日志\" Foreground=\"White\" FontWeight=\"SemiBold\" Margin=\"0,0,0,10\"/><TextBox Text=\"{Binding [recentLog]}\" IsReadOnly=\"True\" AcceptsReturn=\"True\" Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\" MinHeight=\"220\"/></StackPanel></Border><DockPanel Grid.Row=\"5\" Margin=\"0,14,0,0\"><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" VerticalAlignment=\"Center\"/></DockPanel></Grid>"
  }
}
```

#### 模板 4.4：路径与文件工具

适合文件整理、批量处理、命令封装入口。

设计重点：

- 路径输入框
- 参数区
- 执行按钮
- 日志输出区

```json
{
  "id": "path-tool-demo",
  "name": "路径工具台",
  "version": "0.1.0",
  "category": "开发工具",
  "description": "输入目录和规则后执行本地处理脚本。",
  "keywords": ["路径", "文件", "folder"],
  "icon": "mdi:folder-cog-outline",
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": ["storage"],
  "hostedViewXaml": {
    "type": "xaml",
    "title": "路径工具台",
    "window": { "width": 980, "height": 720, "minWidth": 760, "minHeight": 520 },
    "state": { "path": "F:\\Desktop", "rule": "*.txt", "result": "等待执行", "status": "准备就绪" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><TextBlock Text=\"路径工具台\" FontSize=\"22\" FontWeight=\"SemiBold\" Foreground=\"White\"/><Border Grid.Row=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"18\"><StackPanel><TextBlock Text=\"目标目录\" Foreground=\"White\" Margin=\"0,0,0,8\"/><TextBox Text=\"{Binding [path], UpdateSourceTrigger=PropertyChanged}\" Padding=\"10\"/><TextBlock Text=\"匹配规则\" Foreground=\"White\" Margin=\"0,16,0,8\"/><TextBox Text=\"{Binding [rule], UpdateSourceTrigger=PropertyChanged}\" Padding=\"10\"/></StackPanel></Border><Border Grid.Row=\"4\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"12\"><TextBox Text=\"{Binding [result]}\" IsReadOnly=\"True\" AcceptsReturn=\"True\" Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\"/></Border><DockPanel Grid.Row=\"5\" Margin=\"0,14,0,0\"><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" VerticalAlignment=\"Center\"/><Button Content=\"执行检查\" DockPanel.Dock=\"Right\" oqh:HostedViewBridge.Action=\"runScript;inputFrom=path;outputTo=result;successMessage=检查完成\"/></DockPanel></Grid>"
  },
  "script": {
    "source": "using System.IO;\\nusing System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var path = context.InputText ?? string.Empty;\\n        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))\\n        {\\n            return Task.FromResult(\\\"目录不存在：\\\" + path);\\n        }\\n\\n        var files = Directory.GetFiles(path);\\n        return Task.FromResult(\\\"目录：\\\" + path + \\\"\\\\n文件数：\\\" + files.Length);\\n    }\\n}"
  }
}
```

#### 模板 4.5：搜索与预览工作区

适合左侧查询、右侧结果预览、底部状态反馈。

设计重点：

- 查询框
- 操作按钮
- 结果展示区
- 最近结果或说明区

```json
{
  "id": "search-preview-demo",
  "name": "搜索预览台",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "在宿主中输入查询并展示结果预览。",
  "keywords": ["搜索", "预览", "preview"],
  "icon": "mdi:file-search-outline",
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": ["network"],
  "hostedViewXaml": {
    "type": "xaml",
    "title": "搜索预览台",
    "window": { "width": 1040, "height": 730, "minWidth": 780, "minHeight": 540 },
    "state": { "query": "", "preview": "输入关键词后点击搜索", "status": "等待查询" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><DockPanel><TextBlock Text=\"搜索预览台\" FontSize=\"22\" FontWeight=\"SemiBold\" Foreground=\"White\"/><Button Content=\"搜索\" DockPanel.Dock=\"Right\" oqh:HostedViewBridge.Action=\"runScript;inputFrom=query;outputTo=preview;successMessage=搜索完成\"/></DockPanel><Grid Grid.Row=\"2\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"340\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\"0\"><TextBlock Text=\"关键词\" Foreground=\"White\" Margin=\"0,0,0,8\"/><TextBox Text=\"{Binding [query], UpdateSourceTrigger=PropertyChanged}\" Padding=\"10\"/><TextBlock Text=\"说明\" Foreground=\"White\" Margin=\"0,18,0,8\"/><Border Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"12\"><TextBlock Text=\"可用于搜索文件、接口说明、知识片段等。\" Foreground=\"#FFCBD5E1\" TextWrapping=\"Wrap\"/></Border></StackPanel><Border Grid.Column=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"12\"><TextBox Text=\"{Binding [preview]}\" IsReadOnly=\"True\" AcceptsReturn=\"True\" Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\"/></Border></Grid><TextBlock Grid.Row=\"3\" Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" Margin=\"0,14,0,0\"/></Grid>"
  },
  "script": {
    "source": "using System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var q = context.InputText ?? string.Empty;\\n        return Task.FromResult(string.IsNullOrWhiteSpace(q) ? \\\"请输入关键词。\\\" : \\\"查询：\\\" + q + \\\"\\\\n\\\\n这里是搜索结果预览占位内容。\\\");\\n    }\\n}"
  }
}
```

#### 模板 4.6：多分区编辑器

适合 Prompt 编辑、模板拼装、文案编辑工作台。

设计重点：

- 顶部标题与工具按钮
- 主编辑区
- 辅助说明区
- 底部状态栏

```json
{
  "id": "prompt-editor-demo",
  "name": "多分区编辑器",
  "version": "0.1.0",
  "category": "创作工具",
  "description": "在宿主里编辑主内容、补充说明和结果草稿。",
  "keywords": ["编辑器", "prompt", "writer"],
  "icon": "mdi:text-box-edit-outline",
  "hostedViewXaml": {
    "type": "xaml",
    "title": "多分区编辑器",
    "window": { "width": 1120, "height": 780, "minWidth": 860, "minHeight": 580 },
    "state": { "title": "新草稿", "main": "", "notes": "", "status": "准备就绪" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><DockPanel><TextBlock Text=\"多分区编辑器\" FontSize=\"22\" FontWeight=\"SemiBold\" Foreground=\"White\"/><Button Content=\"同步预览\" DockPanel.Dock=\"Right\" oqh:HostedViewBridge.Action=\"setState;path=status;value=已同步当前内容\"/></DockPanel><Grid Grid.Row=\"2\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"2*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\"0\"><TextBlock Text=\"标题\" Foreground=\"White\" Margin=\"0,0,0,8\"/><TextBox Text=\"{Binding [title], UpdateSourceTrigger=PropertyChanged}\" Padding=\"10\"/><TextBlock Text=\"正文\" Foreground=\"White\" Margin=\"0,16,0,8\"/><TextBox Text=\"{Binding [main], UpdateSourceTrigger=PropertyChanged}\" AcceptsReturn=\"True\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\" MinHeight=\"360\" Padding=\"12\"/></StackPanel><StackPanel Grid.Column=\"2\"><TextBlock Text=\"补充说明\" Foreground=\"White\" Margin=\"0,0,0,8\"/><TextBox Text=\"{Binding [notes], UpdateSourceTrigger=PropertyChanged}\" AcceptsReturn=\"True\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\" MinHeight=\"220\" Padding=\"12\"/><Border Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"12\" Margin=\"0,16,0,0\"><TextBlock Text=\"这里可以放预览、说明、模板提示等辅助内容。\" Foreground=\"#FFCBD5E1\" TextWrapping=\"Wrap\"/></Border></StackPanel></Grid><TextBlock Grid.Row=\"3\" Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" Margin=\"0,14,0,0\"/></Grid>"
  }
}
```

#### 模板 4.7：日志与输出查看器

适合脚本输出、任务记录、审计面板。

设计重点：

- 只读大文本区
- 刷新按钮
- 清空按钮
- 状态文案

```json
{
  "id": "log-viewer-demo",
  "name": "日志查看器",
  "version": "0.1.0",
  "category": "开发工具",
  "description": "在宿主里查看、清空和刷新日志内容。",
  "keywords": ["日志", "log", "viewer"],
  "icon": "mdi:text-box-search-outline",
  "hostedViewXaml": {
    "type": "xaml",
    "title": "日志查看器",
    "window": { "width": 980, "height": 700, "minWidth": 760, "minHeight": 520 },
    "state": { "logText": "暂无日志内容", "status": "准备就绪" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><DockPanel><TextBlock Text=\"日志查看器\" FontSize=\"22\" FontWeight=\"SemiBold\" Foreground=\"White\"/><StackPanel DockPanel.Dock=\"Right\" Orientation=\"Horizontal\"><Button Content=\"清空\" Margin=\"0,0,10,0\" oqh:HostedViewBridge.Action=\"setState;path=logText;value=日志已清空|setState;path=status;value=已清空\"/><Button Content=\"刷新\" oqh:HostedViewBridge.Action=\"setState;path=status;value=已刷新\"/></StackPanel></DockPanel><Border Grid.Row=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"12\"><TextBox Text=\"{Binding [logText]}\" IsReadOnly=\"True\" AcceptsReturn=\"True\" Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\"/></Border><TextBlock Grid.Row=\"3\" Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" Margin=\"0,14,0,0\"/></Grid>"
  }
}
```

#### 模板 4.8：欢迎页 / 向导型界面

适合首次启动引导、配置向导、模板选择。

设计重点：

- 说明区域
- 步骤区域
- 下一步 / 完成按钮
- 本地持久化

```json
{
  "id": "welcome-guide-demo",
  "name": "欢迎向导",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "首次启动时展示说明、步骤和完成状态。",
  "keywords": ["欢迎", "向导", "guide"],
  "icon": "mdi:compass-outline",
  "hostedViewXaml": {
    "type": "xaml",
    "title": "欢迎向导",
    "window": { "width": 920, "height": 680, "minWidth": 720, "minHeight": 500 },
    "state": {
      "step": "步骤 1 / 3",
      "status": "欢迎使用燕子工作区",
      "summary": "完成快捷键设置、创建第一个扩展、尝试鼠标面板。"
    },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"16\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><StackPanel><TextBlock Text=\"欢迎向导\" FontSize=\"26\" FontWeight=\"SemiBold\" Foreground=\"White\"/><TextBlock Text=\"{Binding [step]}\" Foreground=\"#FF60A5FA\" Margin=\"0,8,0,0\"/></StackPanel><Border Grid.Row=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"16\" Padding=\"20\"><StackPanel><TextBlock Text=\"开始之前\" Foreground=\"White\" FontSize=\"18\" FontWeight=\"SemiBold\"/><TextBlock Text=\"{Binding [summary]}\" Foreground=\"#FFCBD5E1\" TextWrapping=\"Wrap\" Margin=\"0,12,0,0\"/><Border Background=\"#FF111827\" CornerRadius=\"12\" Padding=\"14\" Margin=\"0,18,0,0\"><TextBlock Text=\"建议顺序：设置呼出方式 -> 导入模板 -> 测试第一个扩展。\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\"/></Border></StackPanel></Border><DockPanel Grid.Row=\"3\" Margin=\"0,14,0,0\"><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" VerticalAlignment=\"Center\"/><StackPanel DockPanel.Dock=\"Right\" Orientation=\"Horizontal\"><Button Content=\"下一步\" Margin=\"0,0,10,0\" oqh:HostedViewBridge.Action=\"setState;path=step;value=步骤 2 / 3|setState;path=status;value=继续查看下一步\"/><Button Content=\"完成\" oqh:HostedViewBridge.Action=\"setState;path=status;value=向导已完成|close\"/></StackPanel></DockPanel></Grid>"
  }
}
```

### 什么时候不要用 hostedViewXaml

以下场景直接改用 `uiMode = native-window`：

- 需要独立弹窗或多窗口
- 需要原生拖拽、复杂列表、树、表格
- 需要系统级悬浮窗
- 需要完整 WPF 事件模型
- 需要复杂本机交互且不想受宿主视图约束

## 图标写法

`icon` 支持：

- 内置图标：`mdi:search`、`mdi:translate`、`mdi:folder`、`mdi:clipboard`、`mdi:code`
- 应用别名：`app:wechat`、`app:qq`、`app:google`、`app:selection`
- 图片路径：扩展目录下相对路径，例如 `icons/logo.png`，或绝对路径、HTTPS 图片地址

## 按钮颜色

`accentHex` 控制扩展在启动器、鼠标面板和燕环中的按钮 / 卡片底色。支持 `#RRGGBB` 或 `#AARRGGBB`，例如 `#10B981`、`#FFF97316`。未填写时使用默认蓝色。

## 给 AI 的提示词

```text
请为燕子 Yanzi 生成一个单文件 manifest.json 扩展。

要求：
1. 只输出合法 JSON，不要 Markdown 代码块。
2. 按任务选择 runtime：复杂逻辑/原生窗口/强类型 .NET API 用 csharp；Windows 自动化/注册表/服务/系统命令优先用 powershell。
3. 如果使用 C#，源码必须包含 public static class YanziAction，并实现 public static Task<string> RunAsync(YanziActionContext context)。
4. 如果只是打开文件、目录、网页或系统协议，使用 openTarget，不要写脚本。
5. 如果是搜索类命令，使用 queryPrefixes 和 queryTargetTemplate，模板里用 {query}。
6. 如果需要宿主界面，使用 hostedView，actionType 优先使用 script。
7. 必须包含 id、name、version、category、description、keywords。
8. icon 优先使用内置值，例如 mdi:search、mdi:folder、mdi:clipboard、mdi:code、mdi:translate。
9. accentHex 用来设置按钮 / 卡片底色，支持 #RRGGBB 或 #AARRGGBB；请按扩展语义选择颜色，不要全部使用默认蓝色。
10. 不要写 null 字段，不要补充燕子未支持的字段。
11. 输出的 JSON 要能直接保存为 manifest.json。

我要的扩展功能是：
在这里描述你的需求。
```

## 调试建议

- 在扩展编辑器里优先点 `测试执行`
- 表单和 JSON 以最后编辑的一侧为准
- C# 编译产物会缓存在扩展目录的 `.yanzi-csharp-cache`
- 运行日志在 `logs/host.log`
- 开发机调试日志在 `logs/dev-debug.log`
