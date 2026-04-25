# 燕子 (Yanzi / Swallow)

> Windows 桌面启动器 — Quicker 的开源替代

燕子是一款面向效率用户的 Windows 桌面启动器，设计目标是成为 [Quicker](https://getquicker.net/) 的完整开源替代。通过全局热键呼出命令面板，让你在不离开当前工作流的情况下，快速执行脚本、打开文件、进行搜索，以及管理跨设备同步的个人扩展库。

---

## 与 Quicker 相比的优势

| 特性 | 燕子 | Quicker |
|:--|:--|:--|
| 完全开源 | 是 | 否 |
| 无订阅 / 免费使用 | 是 | 付费订阅部分功能 |
| 扩展格式 | 标准 JSON，人类可读 | 私有格式 |
| AI 辅助编写扩展 | 内置提示词工具，一键生成 | 不支持 |
| 云同步方案 | 用户自持 Cloudflare Worker | 官方服务器，无法自控 |
| 本地脚本运行时 | C# / PowerShell，可扩展 | 内置沙盒 |
| 二次开发 | 完整 .NET WPF 源码 | 不可修改 |
| 数据主权 | 全部存储在本地 / 你的云账户 | 存储在第三方服务器 |

---

## 核心功能

- **全局热键呼出** — 默认 `Ctrl+Shift+Space`，在任何界面弹出命令面板
- **命令搜索** — 中文、拼音缩写、英文关键词均可命中
- **参数化命令** — 输入 `谷歌 今天的新闻` 即可执行带参数的搜索模板
- **脚本扩展** — 内联 C# 动作、PowerShell 脚本或目录脚本入口，支持测试执行与日志查看
- **宿主视图 (Hosted View)** — 扩展可在启动器内开启双栏工作区，不需要弹出新窗口
- **扩展快捷键** — 每个扩展可注册独立全局快捷键，直接触发动作
- **云同步** — 基于 Cloudflare Worker，扩展元数据与包文件跨设备同步
- **扩展市场** — 上传 / 下载其他用户的扩展包
- **快速面板 (Quick Panel)** — 鼠标右键式浮动面板，展示收藏扩展
- **Agent Skill 导出** — 将启动器内置 Skill 导出到 AI 编码工具（如 Antigravity、Cursor 等）
- **开机自启 / 托盘驻留** — 最小化到系统托盘，随时唤起

---

## 用户使用指南

### 1. 下载与安装

从 [Releases](https://github.com/luoluoluo22/yanzi/releases) 页面下载最新版 ZIP 压缩包，解压后直接运行 `Yanzi.exe`，无需安装。

**系统要求：**
- Windows 10 / 11（64 位）
- .NET 9 运行时（首次运行时 Windows 会提示下载）

### 构建输出目录约定

为了避免本地调试、发布和临时验证目录混淆，当前项目只认这两个标准输出目录：

- 调试版：`src\OpenQuickHost\bin\Debug\net9.0-windows\`
- 发布版：`src\OpenQuickHost\bin\Release\net9.0-windows\`

约定说明：

- `src\OpenQuickHost\bin\Debug\net9.0-windows\` 是默认本地运行目录
- `src\OpenQuickHost\bin\Release\net9.0-windows\` 是默认发布构建目录
- 像 `net9.0`、`net9.0-windows-verify` 这类目录，属于历史残留或临时验证目录，不作为正式输出目录使用
- 临时验证输出如果需要保留，统一明确标成 `verify`，验证完成后清理

### 2. 基本操作

| 操作 | 快捷键 / 动作 |
|:--|:--|
| 呼出启动器 | `Ctrl+Shift+Space` |
| 搜索命令 | 直接输入关键字 |
| 切换条目 | `Up / Down` |
| 执行命令 | `Enter` 或双击 |
| 打开动作菜单 | `Ctrl+K` |
| 返回 / 收起 | `Esc` |
| 右键管理 | 右键点击条目 |

### 3. 添加扩展

**方法一：启动器内 `+` 按钮**

1. 呼出启动器，点击底部状态栏右侧 `+`
2. 粘贴扩展 JSON（可让 AI 帮你生成）
3. 点击保存即可立即使用

**方法二：直接写 JSON 文件**

在应用数据目录的 `Extensions/` 下新建一个子目录，放入 `manifest.json`：

```json
{
  "id": "my-extension",
  "name": "打开项目目录",
  "version": "0.1.0",
  "category": "目录",
  "keywords": ["project", "项目", "代码"],
  "openTarget": "C:\\Users\\你的用户名\\Desktop\\my-project"
}
```

重启启动器或手动刷新后即可命中。

**方法三：内联 C# 动作**

```json
{
  "id": "csharp-echo",
  "name": "C# 输入回显",
  "version": "0.1.0",
  "category": "C#",
  "keywords": ["csharp", "dotnet"],
  "runtime": "csharp",
  "entryMode": "inline",
  "script": {
    "source": "using OpenQuickHost.CSharpRuntime;\\n\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(string.IsNullOrWhiteSpace(context.InputText) ? \\\"没有收到输入\\\" : context.InputText.Trim());\\n    }\\n}"
  }
}
```

**方法四：内联 PowerShell 脚本**

```json
{
  "id": "clipboard-read",
  "name": "读取剪贴板",
  "version": "0.1.0",
  "category": "脚本",
  "keywords": ["clipboard", "剪贴板"],
  "runtime": "powershell",
  "entryMode": "inline",
  "script": {
    "source": "param([string]$InputText = '')\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\nGet-Clipboard -Raw"
  }
}
```

### 4. 参数化命令

在 `manifest.json` 中声明前缀和 URL 模板：

```json
{
  "id": "google-search",
  "name": "谷歌搜索",
  "keywords": ["谷歌", "gg", "guge", "google"],
  "queryPrefixes": ["谷歌", "gg", "guge", "google"],
  "queryTargetTemplate": "https://www.google.com/search?q={query}"
}
```

启动器输入 `gg openai`，即可用默认浏览器打开对应搜索页。

### 5. 云同步

1. 在设置窗口登录账号（需配置你自己的 Cloudflare Worker）
2. 新增或修改扩展后，客户端自动同步扩展元数据到云端
3. 其他设备登录同一账号后可一键拉取全部扩展记录

---

## 开发者部署指南

### 本地构建与运行

**前置依赖：**
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Windows 系统（WPF 依赖 Windows）

```powershell
# 克隆仓库
git clone https://github.com/luoluoluo22/yanzi.git
cd yanzi

# 编译
dotnet build

# 运行（Debug）
.\src\OpenQuickHost\bin\Debug\net9.0-windows\Yanzi.exe
```

### 发布单文件可执行程序

```powershell
dotnet publish .\src\OpenQuickHost\OpenQuickHost.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

输出文件在 `./publish/Yanzi.exe`，可直接分发给用户，无需安装 .NET 运行时。

也可以使用仓库脚本生成发布产物：

```powershell
.\scripts\publish-installer.ps1 -Version 0.1.0
```

脚本会先生成自包含单文件：`.artifacts\publish\win-x64\Yanzi.exe`。
如果本机安装了 Inno Setup 6，还会继续生成一键安装包：`.artifacts\installer\YanziSetup-0.1.0.exe`。

### 配置云同步后端（Cloudflare Worker）

云同步基于 Cloudflare Worker + KV 存储，**你需要部署自己的 Worker 实例**。

```powershell
# 进入 Worker 目录
cd cloudflare

# 安装依赖
npm install

# 部署到你的 Cloudflare 账户
npx wrangler deploy
```

部署完成后，将 Worker 的 URL 填入项目根目录的 `syncsettings.json`：

```json
{
  "baseUrl": "https://your-worker.your-account.workers.dev"
}
```

参考示例文件：`syncsettings.example.json`。

### 网站部署（Cloudflare Pages）

官网静态站位于 `website/` 目录：

```powershell
cd website
npm install
npm run build

# 部署到 Cloudflare Pages
npx wrangler pages deploy ./dist
```

### 目录说明

```text
OpenQuickHost/
├── OpenQuickHost.sln          根目录解决方案
├── src/
│   └── OpenQuickHost/         WPF 桌面应用源码
│       ├── MainWindow.xaml / .cs
│       ├── SettingsWindow.xaml / .cs
│       ├── QuickPanelWindow.xaml / .cs
│       ├── AddJsonExtensionWindow.*
│       ├── ScriptExtensionRunner.cs
│       ├── LocalAgentApiServer.cs
│       └── Sync/
├── cloudflare/                Cloudflare Worker 后端源码
├── website/                   官网静态站源码
├── docs/                      产品说明与扩展规范文档
├── installer/                 Inno Setup 一键安装包脚本
├── scripts/                   发布与验证脚本
├── skills/                    内置 Agent Skill 包
└── syncsettings.example.json  云同步配置示例
```

---

## 扩展规范文档

| 文档 | 说明 |
|:--|:--|
| [产品说明](docs/product-overview.md) | 设计原则与产品定位 |
| [扩展编写指南](docs/extension-authoring-guide.md) | 面向用户和 AI 的扩展写作示例 |
| [扩展规范](docs/extension-spec.md) | manifest.json 完整字段说明 |
| [Agent Skill 规范](docs/agent-skill-spec.md) | 为 AI 工具导出 Skill 的格式规范 |
| [使用说明](docs/getting-started.md) | 快速上手指南 |

---

## 开源协议

本项目以 MIT 协议开源，欢迎 Fork、提 PR 或基于此构建你自己的启动器工具。
