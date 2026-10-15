# 燕子：Visual Studio 专有能力（2026-10-05）

## 已实现能力

- `visualStudio.status`
- `visualStudio.open`
- `visualStudio.build`
- `visualStudio.rebuild`
- `visualStudio.clean`

## 本机安装

Visual Studio Community 2022：

- Product display version：17.13.5
- Installation version：17.13.35919.96
- IDE：`C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe`
- MSBuild：`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`

本机同时安装了 Visual Studio Build Tools 2022，但 Provider 优先使用完整 Community 实例。

## 技术路线

### visualStudio.open

使用 `devenv.exe <solution/project>` 打开：

- .sln
- .slnx
- .csproj
- .vcxproj
- .fsproj

输入必须是真实存在的 solution/project 文件。

### Build / Rebuild / Clean

使用 Visual Studio 安装目录中的 MSBuild，而不是依赖 PATH。

Build/Rebuild 默认加入：

`/restore`

原因：真实验收发现全新的 SDK 工程如果没有 project.assets.json，单纯 MSBuild Build 会报 NETSDK1004；而 Visual Studio IDE 的常规用户体验是自动 restore。因此 Provider 将 Build/Rebuild 语义提升为“可直接构建”。

Clean 不执行 restore。

参数通过 ProcessStartInfo.ArgumentList 传递，不拼 shell 字符串。

支持：

- configuration
- platform
- properties
- timeoutSeconds

MSBuild 属性名和属性值均做输入校验。

## 错误反馈

每次构建都会生成独立 MSBuild file log：

`%LOCALAPPDATA%\OpenQuickHost\VisualStudioBuildLogs\`

Provider 从日志和 stderr 中提取：

- file
- line
- column
- code
- message
- project
- raw

并按错误身份去重，避免 MSBuild 正文与最终汇总重复报告同一个错误。

## 真实验收

建立两个独立测试工程：

- `artifacts/visualstudio-capability-valid`
- `artifacts/visualstudio-capability-broken`

正式 Capability API 验收结果：

### 正常工程

`visualStudio.build`

- success=true
- exitCode=0
- errorCount=0
- 自动 restore 成功

`visualStudio.rebuild`

- success=true
- exitCode=0
- errorCount=0

`visualStudio.clean`

- success=true
- exitCode=0
- errorCount=0

### 故意损坏工程

Program.cs 人工加入语法错误。

`visualStudio.build` 返回真实编译错误：

- CS1002
- CS1001
- 文件：Program.cs
- 行列信息可解析
- exitCode=1

因此燕子可以形成：

`修改代码 -> Visual Studio/MSBuild 构建 -> 结构化错误 -> AI 修复 -> 再构建`

的闭环。

### IDE

`visualStudio.open` 已真实打开：

`F:\Desktop\kaifa\OpenQuickHost\OpenQuickHost.sln`

devenv.exe 进程成功启动。

## 额外发现

Shared Runtime 启动后 Local Agent API 不是瞬时可用：本次日志中 Runtime 进程约 5 秒后才记录：

`Local Agent API started at http://*:42980/`

因此外部健康检查不应仅判断进程存在，应等待 API ready 信号/端口监听。

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
