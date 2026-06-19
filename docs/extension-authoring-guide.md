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
请帮我生成一个 Yanzi 扩展的完整 JSON 配置。

一、背景说明
这个产品的设计理念是“万物皆扩展”。用户会在桌面启动器、快捷面板、鼠标呼出面板里运行扩展。
扩展可以是：
1. 直接打开网页、程序、文件、文件夹
2. 做网页搜索
3. 运行脚本处理输入内容
4. 用 C#/.NET/WPF/Windows 原生能力完成系统操作或独立工具
5. 在宿主界面里展示一个简单工作区
宿主的角色是管家：负责搜索框入口、输入传递、扩展状态、本地/云端存储和少量受控宿主视图动作；除这些已声明 API 外，功能实现应优先写原生 C#，不要臆造宿主封装方法。

我的需求是：
[在这里详细描述你的具体需求]

二、输出要求
1. 只返回一个 ```json 代码块，不要解释，不要额外文字
2. JSON 必须能直接被 System.Text.Json 解析
3. 如果最简单的配置就能实现，不要过度设计
4. 优先选择最贴近需求的方案：
   - 打开类：优先用 openTarget
   - 搜索类：优先用 queryPrefixes + queryTargetTemplate
   - 脚本类：按任务选择 runtime，不要机械固定用 C#
   - 复杂业务逻辑、JSON/HTTP/文件处理、原生 WPF 窗口、P/Invoke、需要强类型 .NET API 时，优先用 runtime = csharp
   - Windows 自动化、注册表/服务/进程/计划任务/系统命令、已有 PowerShell cmdlet 能直接完成的任务，优先用 runtime = powershell
   - 如果需求本质上是一串 cmd/bat 命令，优先用 powershell 包装执行或输出外部 .bat 入口，不要为了简单命令硬套 C#
   - 内联脚本：使用 entryMode = inline 和 script.source
   - 需要窗口、复杂交互、文件/进程/剪贴板/注册表/Win32 调用时，优先使用语言/系统原生能力，而不是要求宿主提供新的专用 API
   - 如果 C# 里必须启动 PowerShell，请优先用 ProcessStartInfo.ArgumentList 或 -EncodedCommand，避免手拼带嵌套双引号的 Arguments 字符串
   - 如果进程需要管理员权限，不要同时设置 Verb = "runas" 和 UseShellExecute = false；要么 UseShellExecute = true 触发 UAC 且不重定向输出，要么明确提示用户以管理员身份运行
   - 蓝牙、Wi-Fi、网卡、USB 等系统开关不要默认使用 Disable-PnpDevice / Enable-PnpDevice；这会禁用硬件设备、让设置页开关消失，且需要管理员权限。除非需求明确是禁用硬件设备，否则优先打开系统设置或给出可恢复的用户操作
   - 修改 Windows 个性化、壁纸、系统颜色等设置时，不能只写注册表后直接返回成功；必须调用实际生效 API、检查返回值，必要时生成壁纸文件并调用 SystemParametersInfo(SPI_SETDESKWALLPAPER) 或明确提示需要用户手动刷新/注销
   - 脚本测试只能判断代码是否成功执行，不能自动证明桌面背景、系统颜色、网络状态等外部副作用真的生效；这类脚本应自行读取/验证结果后再返回成功
   - 宿主会自动引用随应用发布的常用托管 DLL，可直接使用 System.Drawing.Common、System.Management、System.IO.Ports、System.ServiceProcess、System.Diagnostics.EventLog、System.DirectoryServices、System.Security.Cryptography.ProtectedData、System.Text.Encoding.CodePages 等基础库
4.1 如果需要用户在主界面输入“前缀 + 内容”后触发扩展，必须提供 queryPrefixes；脚本或工作区扩展会通过 context.InputText 收到去掉前缀后的内容
5. 如果是 C# 内联脚本，必须严格遵守宿主约定：
   - 必须包含 "runtime": "csharp"
   - 必须包含 "entryMode": "inline"
   - 不要默认包含 "context.read"；只有脚本必须读取快捷面板触发前的选中文本/文件时才声明它
   - 如果通过 queryPrefixes 或 hostedViewXaml 输入框传入内容，context.InputText 不需要 "context.read"
   - script.source 不需要写任何宿主运行时 using；编译器已自动导入 YanziActionContext 所在命名空间
   - script.source 里声明 public static class YanziAction
   - script.source 里实现 public static Task<string> RunAsync(YanziActionContext context)
   - 输入内容从 context.InputText 读取
   - YanziActionContext 只提供宿主管家能力：InputText、LaunchSource、ExtensionDirectory、ExtensionDataDirectory、Now、Permissions、State、SetStateAsync、Storage、ViewState、UpdateView
   - 不要发明 context.SetTheme、context.GetTheme、context.OpenFilePicker、context.ShowMessage、context.GetStateAsync<T>() 等不存在的宿主 API；这些需求应优先用原生 C#/.NET/WPF/Windows API 自己实现
   - 不要根据旧命名空间推断 pack URI、程序集名或资源路径；当前应用程序集名是 Yanzi，且没有内置主题资源字典
5.1 只有脚本真正创建 WPF 原生窗口时才输出 "uiMode": "native-window"，典型特征是 new Window、ShowDialog、WindowStartupLocation 或 WindowStyle。仅使用 System.Windows.Clipboard 不属于原生窗口扩展。
5.2 只要是 native-window 扩展，就不要再同时输出 hostedViewXaml 或 hostedViewV2
5.3 如果需求是独立弹窗小工具、原生窗口小应用、独立编辑器，而不是寄生在宿主里的工作区，优先输出 native-window，而不是 hostedViewXaml

三、字段说明
- id：扩展唯一标识，只能英文小写、数字、短横线，例如 "open-project-folder"
- name：扩展显示名称
- version：版本号，默认 "0.1.0"
- category：分类，例如 "扩展"、"网页搜索"、"效率工具"
- description：一句话描述扩展用途
- keywords：搜索关键词数组
- icon：图标，可用 mdi:图标名 或图片地址
- accentHex：可选，扩展按钮 / 卡片底色，支持 #RRGGBB 或 #AARRGGBB，例如 #10B981、#FFF97316；不要所有扩展都用默认蓝色
- openTarget：点击后直接打开的目标
- queryPrefixes：前缀数组，例如 ["百度", "baidu"]；搜索扩展会把后面的内容替换进 {query}，脚本 / 工作区扩展会把后面的内容传给 context.InputText
- queryTargetTemplate：搜索模板，必须包含 {query}
- searchProvider：可选；如果希望某个扩展被固定到顶部后，在主界面继续输入关键词就返回一组列表结果，可输出 searchProvider
- searchProvider.type：当前支持 "folder"，表示在指定目录下搜索文件/文件夹
- searchProvider.type 也支持 "script"；这时扩展自己的脚本需要返回 JSON 结果数组
- searchProvider.path：搜索根目录；如果省略且 openTarget 本身是目录，会自动拿 openTarget 当根目录
- searchProvider.aliases：可选；固定到顶部后支持 @别名 关键词，例如 @项目 需求文档
- searchProvider.includeSubdirectories / includeFiles / includeDirectories / maxResults：可选，控制搜索范围
- script provider 的脚本返回格式建议是 JSON 数组，每项包含 title、subtitle、kind、openTarget、keywords、accentHex；kind 可用 file、folder、record、url、script、api
- runtime：脚本运行时，例如 "csharp" 或 "powershell"
- uiMode：可选；如果希望 C# 扩展自己弹原生窗口而不是寄生在宿主界面中，可写 "native-window"
- entryMode：如果是内联脚本请写 "inline"
- entry：如果是外部脚本文件，写入口文件名
- permissions：权限数组，例如 ["clipboard", "network"]
- 宿主 API 边界：context 不是万能能力对象，只能使用本文明确列出的成员；其它能力请在 script.source 中直接使用 C# 原生库、WPF、P/Invoke、Process、File、HttpClient 等实现
- 命名边界：产品名和应用名是 Yanzi；不要在 C# 脚本里写旧产品名相关命名空间、程序集引用、pack URI、资源路径或品牌文案。hostedViewXaml 的 oqh:HostedViewBridge 命名空间使用模板给出的 Yanzi 命名空间
- 扩展脚本现在支持 context.Storage 本地/云端存储 helper：ReadTextAsync、WriteTextAsync、ReadJsonAsync<T>、WriteJsonAsync<T>
- context.Storage 默认支持 scope = local、cloud、both；local 写入本地扩展数据目录，cloud / both 会通过宿主 API 写入坚果云 / WebDAV
- context.Storage.ReadTextAsync 的可用写法是：await context.Storage.ReadTextAsync("note.txt", scope: "both")；不要传 defaultValue 参数
- context.Storage.WriteTextAsync 的可用写法是：await context.Storage.WriteTextAsync("note.txt", content, scope: "both")
- 如果需要默认值，请自己写：var text = await context.Storage.ReadTextAsync("note.txt", scope: "both") ?? string.Empty; 或用 try/catch，不要发明 defaultValue 参数
- script.source：内联脚本源码
- hostedViewXaml：如果要让宿主直接加载自定义 XAML 界面，请输出 hostedViewXaml
- hostedViewXaml.xaml：填写可直接解析的 WPF XAML 字符串，根元素建议用 Grid、UserControl 或 Window
- hostedViewXaml.xaml 必须放在合法 JSON 字符串里，内部所有双引号都必须正确转义为 \"
- hostedViewXaml 是标准 WPF XAML，不是 WinUI / MAUI / UWP / Web 风格标记
- hostedViewXaml 中 Grid 没有 Padding 属性；如果要留内边距，请用 Margin、在 Grid 外包一层 Border 并把 Padding 写在 Border 上，或在 StackPanel / Border 上设置间距
- hostedViewXaml 中不要使用宿主没有声明的 StaticResource；除非我明确给出资源名，否则不要写 Converter={StaticResource ...}、Style={StaticResource ...} 这类引用
- hostedViewXaml 中不要假设存在 InverseBoolConverter、BooleanToVisibilityConverter 或任何自定义 Converter，除非我明确给出
- hostedViewXaml.state：初始化状态对象，值可用字符串、数字、布尔；XAML 中可通过 {Binding [key]} 绑定
- hostedViewXaml.window.width / height / minWidth / minHeight：可选，控制窗口尺寸
- hostedViewXaml 中按钮可用 xmlns:oqh="clr-namespace:Yanzi"，再用 oqh:HostedViewBridge.Action 声明动作
- 所有 URL、xmlns、图片地址都必须是纯文本，不要写成 [text](url) 这种 Markdown 链接
- oqh:HostedViewBridge.Action 当前支持 close、setState、runScript、loadStorage、saveStorage；多个动作可用 | 分隔，参数用 ;key=value
- 根元素还支持 oqh:HostedViewBridge.LoadedAction，可在窗口打开时自动执行 loadStorage
- 视图脚本如果要读写界面状态，优先使用 context.State 和 await context.SetStateAsync(...)；兼容写法 context.ViewState / await context.UpdateView() 也支持
- 不要使用 context.GetStateAsync<T>()；当前宿主没有这个 API。读取状态请用 context.State["key"]，写状态请用 await context.SetStateAsync(...)
- hostedViewXaml 当前更适合工作区、设置页、仪表盘、面板、轻量编辑器；如果需求是独立多窗口工具、复杂原生拖拽、系统级悬浮窗，请改用 native-window
- hostedViewXaml 当前没有代码隐藏，不要输出 Click=、TextChanged= 这类事件处理函数名；宿主只会识别 oqh:HostedViewBridge.Action / LoadedAction
- hostedViewXaml 当前状态模型偏扁平，优先使用 note、preview、status、path、result、query 这类简单键名，不要假设存在复杂对象树绑定
- 如果需求里需要列表、表格、树、拖拽排序、复杂选择器，请先收敛成静态布局 + 按钮动作；当前宿主还没有成熟的列表模板和通用事件桥
- 如果需求里需要打开文件、选择目录、消息确认、颜色选择、进度条、取消任务，不要发明宿主 action；请改用 native-window 或 C# 原生 WPF 对话框/控件自己实现
- hostedViewV2：如果要在宿主里显示内置界面，也可以输出 hostedViewV2，不要返回 @view: 之类的协议字符串
- hostedViewV2.type：当前支持 "single-pane"、"split-horizontal"
- hostedViewV2.window.width / height / minWidth / minHeight：可选，控制窗口尺寸
- hostedViewV2.state：初始化状态对象，例如 { "note": "", "preview": "先输入内容", "count": 0 }
- hostedViewV2.components：当前支持 text、textarea、button、markdown
- 组件的 bind 字段用于绑定到 state 路径
- button.actions：当前支持 setState、runScript、loadStorage、saveStorage
- 如果只是旧版简单双栏工作区，也可以输出 hostedView，但新方案优先用 hostedViewXaml 或 hostedViewV2
- 如果不想寄生在宿主界面中，而是希望扩展自己弹原生 WPF 窗口，可使用 C# 扩展并设置 uiMode = native-window；这类扩展仍然需要用 YanziActionContext 读取输入、状态和存储
- native-window 扩展中的 WPF 窗口代码必须在 STA 线程中创建和显示；如果手动 new Window / TextBox / Button，必须显式创建 STA 线程再 ShowDialog，不要直接在 RunAsync 当前线程里 new Window
- 如果需求是笔记、便签、编辑器、独立小应用，并且不寄生在宿主界面中，请优先参考模板 5.1 的原生笔记窗口，不要自己改写窗口启动结构
- 如果需求是修改宿主自身界面资源，可使用 System.Windows.Application.Current.Dispatcher 和 Application.Current.Resources 等 WPF 原生对象尝试实现，但不要写 context.SetTheme 这类未声明方法
- 不要输出 x:Class，也不要假设宿主会自动解析你自定义的事件处理函数

四、请优先参考这些模板思路

模板 1：打开类扩展
{
  "id": "open-project-folder",
  "name": "打开项目文件夹",
  "version": "0.1.0",
  "category": "扩展",
  "description": "打开指定项目目录。",
  "keywords": ["项目", "folder", "vscode"],
  "openTarget": "C:\\Projects\\Demo",
  "icon": "mdi:folder"
}

模板 2：网页搜索扩展
{
  "id": "search-baidu",
  "name": "百度搜索",
  "version": "0.1.0",
  "category": "网页搜索",
  "description": "用百度搜索关键词。",
  "keywords": ["百度", "搜索", "网页"],
  "queryPrefixes": ["百度", "baidu"],
  "queryTargetTemplate": "https://www.baidu.com/s?wd={query}",
  "icon": "https://www.baidu.com/favicon.ico"
}

模板 2.1：目录搜索扩展（支持 @别名 和 扩展名+空格 激活）
{
  "id": "download-folder-search",
  "name": "下载",
  "version": "0.1.0",
  "category": "目录搜索",
  "description": "固定到顶部后，在下载目录里继续搜索文件。",
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
使用方式：固定到顶部后，可以直接输入 @下载 关键词；也可以在“扩展”标签里输入“下载 空格”显示全部结果，输入“下载 空格 关键词”显示过滤结果。

模板 2.2：脚本结果列表扩展（script provider）
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
    "source": "using System.Text.Json;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var q = (context.InputText ?? string.Empty).Trim();
        var items = new object[]
        {
            new { id = \"doc-1\", title = \"接口文档\", subtitle = \"脚本生成的示例结果\", kind = \"record\", openTarget = \"https://example.com/docs\", keywords = new[] { \"文档\", q }, accentHex = \"#FF10B981\" },
            new { id = \"tool-1\", title = \"打开工具页\", subtitle = \"支持 URL / 文件 / 普通记录\", kind = \"url\", openTarget = \"https://example.com/tools?q=\" + System.Uri.EscapeDataString(q), keywords = new[] { \"工具\", q }, accentHex = \"#FF06B6D4\" }
        };
        return Task.FromResult(JsonSerializer.Serialize(items));
    }
}"
  }
}

模板 3：内联脚本扩展
{
  "id": "inline-text-demo",
  "name": "处理输入文本",
  "version": "0.1.0",
  "category": "脚本",
  "description": "读取输入内容并返回结果。",
  "keywords": ["脚本", "文本", "inline"],
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": [],
  "icon": "mdi:code-tags",
  "script": {
    "source": "using System.Threading.Tasks;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = context.InputText ?? string.Empty;
        return Task.FromResult(\"收到输入：\" + input);
    }
}"
  }
}

模板 3.1：带前缀输入的内联脚本扩展
{
  "id": "text-length-counter",
  "name": "文本长度统计",
  "version": "0.1.0",
  "category": "脚本",
  "description": "在主界面输入前缀后，把后面的文本传给脚本并返回长度。",
  "keywords": ["文本", "长度", "统计", "脚本"],
  "queryPrefixes": ["统计", "count"],
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": [],
  "icon": "mdi:counter",
  "script": {
    "source": "using System.Threading.Tasks;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = context.InputText ?? string.Empty;
        return Task.FromResult(\"原文：\" + input + \"\
长度：\" + input.Length);
    }
}"
  }
}

模板 4：宿主自定义 XAML 视图扩展（hostedViewXaml）
适用：双栏编辑器、便签工作区、预览器、轻量设置页。
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
    "description": "使用自定义 XAML 渲染便签窗口，并在本地 / 坚果云持久化。",
    "window": {
      "width": 960,
      "height": 720,
      "minWidth": 760,
      "minHeight": 520
    },
    "state": {
      "note": "",
      "preview": "先在左侧输入内容，这里会显示便签结果。",
      "saved": true
    },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\" oqh:HostedViewBridge.PreferredFocus=\"NoteBox\" oqh:HostedViewBridge.LoadedAction=\"loadStorage;path=note;key=note.txt;scope=both;defaultValue=\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\"0\"><TextBlock Text=\"便签内容\" Foreground=\"White\" FontSize=\"14\" FontWeight=\"SemiBold\" Margin=\"0,0,0,10\"/><TextBox x:Name=\"NoteBox\" Text=\"{Binding [note], UpdateSourceTrigger=PropertyChanged}\" AcceptsReturn=\"True\" VerticalScrollBarVisibility=\"Auto\" TextWrapping=\"Wrap\" MinHeight=\"320\" Padding=\"12\"/><Button Content=\"保存便签\" Margin=\"0,12,0,0\" oqh:HostedViewBridge.Action=\"saveStorage;path=note;key=note.txt;scope=both;successMessage=便签已保存。|setState;path=preview;valueFrom=note\"/></StackPanel><Border Grid.Column=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"10\" Padding=\"12\"><TextBlock Text=\"{Binding [preview]}\" TextWrapping=\"Wrap\" Foreground=\"White\"/></Border></Grid>"
  },
  "startup": {
    "mode": "on_app_launch",
    "schedule": "0 9 * * *"
  }
}

模板 4.1：设置页 / 表单型 hostedViewXaml
适用：配置保存、账号信息、路径输入、开关集合。重点是表单布局和 loadStorage / saveStorage。
{
  "id": "workspace-settings-demo",
  "name": "工作区设置",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "在宿主里编辑并保存设置项。",
  "keywords": ["设置", "配置", "workspace"],
  "icon": "mdi:cog-outline",
  "hostedViewXaml": {
    "type": "xaml",
    "title": "工作区设置",
    "window": { "width": 900, "height": 680, "minWidth": 720, "minHeight": 520 },
    "state": {
      "workspaceName": "默认工作区",
      "defaultFolder": "F:\\Desktop",
      "status": "修改后点击保存"
    },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\" oqh:HostedViewBridge.LoadedAction=\"loadStorage;path=workspaceName;key=settings/workspace-name.txt;scope=local|loadStorage;path=defaultFolder;key=settings/default-folder.txt;scope=local\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><TextBlock Text=\"工作区设置\" FontSize=\"22\" FontWeight=\"SemiBold\" Foreground=\"White\"/><Border Grid.Row=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"18\"><StackPanel><TextBlock Text=\"工作区名称\" Foreground=\"White\" Margin=\"0,0,0,8\"/><TextBox Text=\"{Binding [workspaceName], UpdateSourceTrigger=PropertyChanged}\" Padding=\"10\"/><TextBlock Text=\"默认目录\" Foreground=\"White\" Margin=\"0,18,0,8\"/><TextBox Text=\"{Binding [defaultFolder], UpdateSourceTrigger=PropertyChanged}\" Padding=\"10\"/><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" Margin=\"0,18,0,0\"/></StackPanel></Border><StackPanel Grid.Row=\"3\" Orientation=\"Horizontal\" HorizontalAlignment=\"Right\" Margin=\"0,14,0,0\"><Button Content=\"保存\" oqh:HostedViewBridge.Action=\"saveStorage;path=workspaceName;key=settings/workspace-name.txt;scope=local|saveStorage;path=defaultFolder;key=settings/default-folder.txt;scope=local|setState;path=status;value=设置已保存\"/></StackPanel></Grid>"
  }
}

模板 4.2：脚本工具台 hostedViewXaml
适用：本机脚本执行、文本处理、网络请求入口、结果回显。重点是 textarea + runScript + output。
{
  "id": "script-console-demo",
  "name": "脚本工具台",
  "version": "0.1.0",
  "category": "开发工具",
  "description": "在宿主窗口中输入内容并执行 C# 脚本。",
  "keywords": ["脚本", "控制台", "console"],
  "icon": "mdi:console",
  "runtime": "csharp",
  "entryMode": "inline",
  "permissions": ["network"],
  "hostedViewXaml": {
    "type": "xaml",
    "title": "脚本工具台",
    "window": { "width": 1020, "height": 720, "minWidth": 760, "minHeight": 520 },
    "state": { "input": "", "output": "执行结果会显示在这里。", "status": "准备就绪" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"12\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><TextBlock Text=\"脚本工具台\" FontSize=\"22\" FontWeight=\"SemiBold\" Foreground=\"White\"/><Grid Grid.Row=\"2\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\"0\"><TextBlock Text=\"输入\" Foreground=\"White\" Margin=\"0,0,0,8\"/><TextBox Text=\"{Binding [input], UpdateSourceTrigger=PropertyChanged}\" AcceptsReturn=\"True\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\" MinHeight=\"360\" Padding=\"12\"/></StackPanel><Border Grid.Column=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"12\" Padding=\"12\"><TextBox Text=\"{Binding [output]}\" IsReadOnly=\"True\" AcceptsReturn=\"True\" Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\"/></Border></Grid><DockPanel Grid.Row=\"3\" Margin=\"0,14,0,0\"><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" VerticalAlignment=\"Center\"/><Button Content=\"执行脚本\" DockPanel.Dock=\"Right\" oqh:HostedViewBridge.Action=\"runScript;inputFrom=input;outputTo=output;successMessage=脚本执行完成\"/></DockPanel></Grid>"
  },
  "script": {
    "source": "using System.Threading.Tasks;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = context.InputText ?? string.Empty;
        return Task.FromResult(\"输入长度：\" + input.Length + \"\
\
\" + input.ToUpperInvariant());
    }
}"
  }
}

模板 4.3：仪表盘 / 状态面板 hostedViewXaml
适用：展示关键数据、状态摘要、日志片段、快速动作。重点是多卡片布局，而不是双栏。
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
    "state": { "summary": "今日任务 5 项", "health": "运行正常", "recentLog": "暂无新日志", "status": "准备就绪" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"16\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"16\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><StackPanel><TextBlock Text=\"状态仪表盘\" FontSize=\"24\" FontWeight=\"SemiBold\" Foreground=\"White\"/><TextBlock Text=\"用多卡片布局展示关键指标和最近状态\" Foreground=\"#FF9CA3AF\" Margin=\"0,6,0,0\"/></StackPanel><Grid Grid.Row=\"2\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions><Border Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"今日摘要\" Foreground=\"#FF9CA3AF\"/><TextBlock Text=\"{Binding [summary]}\" Foreground=\"White\" FontSize=\"20\" FontWeight=\"SemiBold\" Margin=\"0,10,0,0\"/></StackPanel></Border><Border Grid.Column=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"运行状态\" Foreground=\"#FF9CA3AF\"/><TextBlock Text=\"{Binding [health]}\" Foreground=\"#FF34D399\" FontSize=\"20\" FontWeight=\"SemiBold\" Margin=\"0,10,0,0\"/></StackPanel></Border><Border Grid.Column=\"4\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"快速动作\" Foreground=\"#FF9CA3AF\"/><Button Content=\"刷新摘要\" Margin=\"0,12,0,0\" oqh:HostedViewBridge.Action=\"setState;path=status;value=已刷新摘要\"/></StackPanel></Border></Grid><Border Grid.Row=\"4\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"14\" Padding=\"16\"><StackPanel><TextBlock Text=\"最近日志\" Foreground=\"White\" FontWeight=\"SemiBold\" Margin=\"0,0,0,10\"/><TextBox Text=\"{Binding [recentLog]}\" IsReadOnly=\"True\" AcceptsReturn=\"True\" Background=\"Transparent\" BorderThickness=\"0\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\" VerticalScrollBarVisibility=\"Auto\" MinHeight=\"220\"/></StackPanel></Border><DockPanel Grid.Row=\"5\" Margin=\"0,14,0,0\"><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" VerticalAlignment=\"Center\"/></DockPanel></Grid>"
  }
}

模板 4.4：路径与文件工具 hostedViewXaml
适用：文件整理、批量重命名入口、命令封装。重点是路径输入、脚本执行、结果日志。
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
    "source": "using System.IO;
using System.Threading.Tasks;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var path = context.InputText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return Task.FromResult(\"目录不存在：\" + path);
        }

        var files = Directory.GetFiles(path);
        return Task.FromResult(\"目录：\" + path + \"\
文件数：\" + files.Length);
    }
}"
  }
}

模板 4.5：搜索与预览工作区 hostedViewXaml
适用：左侧查询、右侧结果预览、历史记录。重点是输入区、结果区、状态区。
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
    "source": "using System.Threading.Tasks;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var q = context.InputText ?? string.Empty;
        return Task.FromResult(string.IsNullOrWhiteSpace(q) ? \"请输入关键词。\" : \"查询：\" + q + \"\
\
这里是搜索结果预览占位内容。\");
    }
}"
  }
}

模板 4.6：多分区编辑器 hostedViewXaml
适用：文案编辑、Prompt 编辑、模板拼装。重点是头部工具栏 + 主编辑区 + 底部状态栏。
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

模板 4.7：日志与输出查看器 hostedViewXaml
适用：脚本日志、任务记录、审计面板。重点是只读输出区和清空/刷新动作。
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

模板 4.8：欢迎页 / 向导型 hostedViewXaml
适用：首次启动引导、模板选择、配置引导。重点是说明区、步骤按钮和持久化。
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
    "state": { "step": "步骤 1 / 3", "status": "欢迎使用燕子工作区", "summary": "完成快捷键设置、创建第一个扩展、尝试鼠标面板。" },
    "xaml": "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\"><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"16\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions><StackPanel><TextBlock Text=\"欢迎向导\" FontSize=\"26\" FontWeight=\"SemiBold\" Foreground=\"White\"/><TextBlock Text=\"{Binding [step]}\" Foreground=\"#FF60A5FA\" Margin=\"0,8,0,0\"/></StackPanel><Border Grid.Row=\"2\" Background=\"#FF171717\" BorderBrush=\"#FF2E2E2E\" BorderThickness=\"1\" CornerRadius=\"16\" Padding=\"20\"><StackPanel><TextBlock Text=\"开始之前\" Foreground=\"White\" FontSize=\"18\" FontWeight=\"SemiBold\"/><TextBlock Text=\"{Binding [summary]}\" Foreground=\"#FFCBD5E1\" TextWrapping=\"Wrap\" Margin=\"0,12,0,0\"/><Border Background=\"#FF111827\" CornerRadius=\"12\" Padding=\"14\" Margin=\"0,18,0,0\"><TextBlock Text=\"建议顺序：设置呼出方式 -> 导入模板 -> 测试第一个扩展。\" Foreground=\"#FFE5E7EB\" TextWrapping=\"Wrap\"/></Border></StackPanel></Border><DockPanel Grid.Row=\"3\" Margin=\"0,14,0,0\"><TextBlock Text=\"{Binding [status]}\" Foreground=\"#FF9CA3AF\" VerticalAlignment=\"Center\"/><StackPanel DockPanel.Dock=\"Right\" Orientation=\"Horizontal\"><Button Content=\"下一步\" Margin=\"0,0,10,0\" oqh:HostedViewBridge.Action=\"setState;path=step;value=步骤 2 / 3|setState;path=status;value=继续查看下一步\"/><Button Content=\"完成\" oqh:HostedViewBridge.Action=\"setState;path=status;value=向导已完成|close\"/></StackPanel></DockPanel></Grid>"
  }
}


模板 5：原生窗口扩展（uiMode = native-window）
{
  "id": "native-window-demo",
  "name": "原生窗口示例",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "在独立 WPF 窗口中显示输入内容。",
  "keywords": ["native", "window", "wpf"],
  "icon": "mdi:application-outline",
  "runtime": "csharp",
  "uiMode": "native-window",
  "entryMode": "inline",
  "permissions": [],
  "script": {
    "source": "using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = context.InputText ?? string.Empty;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var textBlock = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(input) ? \"这是一个独立原生窗口示例。\" : \"输入内容：\" + input,
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 16
                };

                var closeButton = new Button
                {
                    Content = \"关闭\",
                    Width = 88,
                    Height = 32,
                    Margin = new Thickness(24, 0, 24, 24),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var panel = new DockPanel();
                DockPanel.SetDock(closeButton, Dock.Bottom);
                panel.Children.Add(closeButton);
                panel.Children.Add(textBlock);

                var window = new Window
                {
                    Title = \"原生窗口示例\",
                    Width = 520,
                    Height = 320,
                    MinWidth = 420,
                    MinHeight = 240,
                    Content = panel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                closeButton.Click += (_, _) => window.Close();
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw error;
        }

        return Task.FromResult(\"窗口已关闭\");
    }
}"
  }
}

模板 5.1：原生笔记窗口（带存储）
注意：这类扩展必须直接沿用下面的 STA 线程结构和 storage 调用方式；不要改成在 RunAsync 当前线程里直接 new Window，也不要给 ReadTextAsync 传 defaultValue。
{
  "id": "note-native-app",
  "name": "独立笔记",
  "version": "0.1.0",
  "category": "效率工具",
  "description": "在独立窗口中创建和保存笔记。",
  "keywords": ["笔记", "便签", "native"],
  "icon": "mdi:notebook-edit-outline",
  "runtime": "csharp",
  "uiMode": "native-window",
  "entryMode": "inline",
  "permissions": ["storage"],
  "script": {
    "source": "using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var storage = context.Storage;
        string noteContent;
        try
        {
            noteContent = await storage.ReadTextAsync(\"note.txt\", scope: \"local\") ?? string.Empty;
        }
        catch
        {
            noteContent = string.Empty;
        }

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    Title = \"独立笔记\",
                    Width = 600,
                    Height = 500,
                    MinWidth = 400,
                    MinHeight = 300,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var textBox = new TextBox
                {
                    Text = noteContent,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    Padding = new Thickness(12),
                    Margin = new Thickness(10),
                    MinHeight = 360
                };

                var saveButton = new Button
                {
                    Content = \"保存笔记\",
                    Margin = new Thickness(10, 0, 10, 10),
                    Height = 32,
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };

                var statusText = new TextBlock
                {
                    Text = \"就绪\",
                    Margin = new Thickness(10),
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 12
                };

                saveButton.Click += async (_, _) =>
                {
                    await storage.WriteTextAsync(\"note.txt\", textBox.Text, scope: \"both\");
                    statusText.Text = \"已保存到本地，云端同步在后台进行\";
                };

                var panel = new StackPanel();
                panel.Children.Add(textBox);
                panel.Children.Add(saveButton);
                panel.Children.Add(statusText);
                window.Content = panel;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw error;
        }

        return \"笔记窗口已关闭\";
    }
}"
  }
}


五、最终要求
请结合我的需求，只返回一个包含最终 JSON 的 ```json 代码块，不要返回多个方案，不要附加说明。
如果需求里提到便签、面板、编辑器、工作区、内置界面，请优先使用 hostedViewXaml；如果只是简单表单，再考虑 hostedViewV2。
如果需求里明确是独立弹窗小工具或原生小应用，并且脚本需要直接 new Window / TextBox / Button，就必须输出 native-window，不要改成 hostedViewXaml。
```

## 调试建议

- 在扩展编辑器里优先点 `测试执行`
- 表单和 JSON 以最后编辑的一侧为准
- C# 编译产物会缓存在扩展目录的 `.yanzi-csharp-cache`
- 运行日志在 `logs/host.log`
- 开发机调试日志在 `logs/dev-debug.log`
