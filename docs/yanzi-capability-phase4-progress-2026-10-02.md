# 燕子能力网络 Phase 4 开发记录

日期：2026-10-02

## 本轮完成

跨小程序调用继续推进。

新增：

- YanziExtensionCapabilityBridge
- YanziCapabilityCallLog

增强：

- YanziCapabilityInvocationService 接入调用记录
- 成功和失败调用均记录提供者信息

能力网络现在具备：

1. 能力声明
2. 能力发现
3. 运行实例管理
4. 统一调用入口
5. 桥接层基础
6. 调用记录基础

下一步：

- 对接实际 Extension Runtime
- 注册 clipboard-history 能力
- 完成 calendar.create 实际调用验证

## 后续开发：能力契约与统一入口

已实现并验证：

- `capability.list` / `capability.describe` 可通过能力协议、WebView 和本地 Agent REST 查询。
- 描述包含 `name`、`description`、`providerExtensionId`、`version`、`inputSchema`、`outputSchema`、`permissions`，不暴露 Handler。
- 内置能力在宿主启动时注册；本地 API 独立启动时也能注册。
- WebView 的 `invoke` 经过统一调用服务；保留成功时直接返回 Data、失败时 Promise reject 的行为。
- 入参始终转换为 `JsonElement`，缺省对象参数按 `{}` 校验。`clipboard.set` 使用 `{ "text": "内容" }`。
- 调用前校验权限和入参；成功与失败记录提供者、调用者和错误码，内存日志最多保留 1000 条，不记录业务 payload。
- SDK 注册同步建立运行表；注销旧提供者不会清除已经被新提供者替换的能力。
- manifest discovery 保留 Schema / version / permissions；无 invoker 的声明不注册为可调用能力。

Schema MVP 支持：单一 `type`（object/array/string/number/integer/boolean/null）、`properties`、`required`、布尔 `additionalProperties`、`items`、`enum`、`minLength`，以及 `title` / `description` / `default` / `examples` / `$schema` 元信息。其他关键字（例如 `$ref`、`format`、`pattern`、`oneOf`）在注册时明确拒绝。C# Extension Runtime 接入后，其 Handler 返回值也经过 `outputSchema` 校验；直接使用宿主 Provider SDK 的 Handler 仍由提供者负责输出契约。

权限采用现有 manifest 声明权限作为调用入口的访问控制依据，不是进程沙箱。WebView 由宿主构造调用者；JSON 中传入的 `permissions` / `isTrusted` 不产生授权。已持有本地 Agent Token 的调用者拥有现有本地 API 管理权限。C# 小程序已有完整 .NET/系统访问能力，不因此变成受限沙箱。

验证命令：

```powershell
dotnet run --project src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj -p:SkipStopRunningApp=true
```

结果：29 项通过，覆盖契约发现、权限拒绝、参数校验无副作用、JSON 协议、权限伪造拒绝、manifest 绑定、注销/替换、两个测试 Provider 的组合调用及独立 REST 服务的 401/400/404/成功响应。未执行真实剪贴板写入、历史搜索或日历创建。

以上是上一轮基础层的验证记录；下文为真实运行时接入后的最终结果。

## 真实小程序调用闭环完成

- `YanziExtensionCapabilitySession` 将 manifest 声明与实际 C# Handler 绑定，使用 JSON 字符串跨 AssemblyLoadContext，避免宿主类型耦合。
- `ScriptExtensionRunner` 注入 C# 注册/调用代理。每次加载拥有独立租约，退出时清理能力；旧租约清理不会删除新租约的能力。
- `context.Capabilities.Register/InvokeAsync/ListAsync/DescribeAsync` 可供 C# 小程序使用，调用者身份与权限由宿主提供。
- 输入和真实运行时输出均做契约校验；调用链检测循环及 16 层嵌套上限。
- 实际剪贴板窗口使用自身历史集合搜索，日历使用实际 Reminder Manager 写入原业务存储。真实 Provider 源码只维护在 `%LOCALAPPDATA%\OpenQuickHost\Extensions`。
- `clipboard.remind` 内部调用 `calendar.create`，`calendar.fromClipboard` 内部调用 `clipboard.search`，审计日志确认两种实际跨小程序身份。
- 独立第三个 C# 小程序通过 SDK 查询真实历史；没有 `calendar.write` 时创建提醒被拒绝。
- 停止、重启及 `/reload` 均通过验证，旧运行时注销，新运行时重新绑定，业务数据保留。

最终验证：基础 30 项、真实运行时 29 项全部通过。真实测试产生未来日期的无闹钟提醒，在验证内存/磁盘和重载后精确删除；原剪贴板的全部可读取格式恢复，测试历史记录和临时客户端目录清理。测试不清空任何用户业务库。

真实验证入口：

```powershell
powershell -STA -NoProfile -ExecutionPolicy Bypass -File scripts/test-capability-runtime.ps1
```

详细契约、接入代码及限制见 `yanzi-capability-runtime-integration-2026-10-02.md`。

## 预编译旧版上下文兼容性修复

真实截图小程序使用预编译 `Yanzi.Capture.dll`，其上下文只有原来的对象注册接口，没有新增 `RegisterCapabilityJson/InvokeCapabilityJson`。首次接入时强制反射注入新增属性导致 `CreateInProcessRuntimeContext` 空引用，发生在业务入口执行前。

修复：新增属性仅在存在、可写且委托类型匹配时注入，旧 DLL 保留原运行时接口，无需重新编译或安装。新增使用截图工程实际旧上下文的回归用例，基础验证现为 31 项通过；真实调用链快速回归（`-SkipLifecycle`）19 项通过。实际已安装截图 DLL 验证屏幕捕获和截图界面显示正常（2560×1440），最后恢复后台常驻。

## AI 目录与本地网页接入

已增加认证后的统一目录 `/v1/agent/catalog`、OpenAPI 3.1 `/v1/agent/openapi.json` 和单小程序目录 `/v1/extensions/{id}/capabilities`；原小程序列表也关联能力。manifest 声明可发现，实际绑定的能力才可调用，提供者停止后声明仍保留且 `available=false`。

本地 `/docs` 网页登录后自动读取目录，支持筛选提供者、查看输入输出 Schema 与权限、生成必填参数模板、编辑 JSON 并调用、启动提供者、复制无 Token 目录及读取调用日志。没有能力的小程序显示 0 项，能力名称相同时用提供者 ID 区分选择项。

本次验证：基础接口 39 项通过；真实运行时 35 项通过，包含两个提供者的目录关联和停止后的不可用状态；控制台 Cookie 认证、目录、OpenAPI 与时间调用通过。隔离浏览器使用独立测试凭据验证目录加载、筛选、契约、未注册能力禁用、参数 JSON 错误和实际时间调用。不会将用户 Token 写入网页或测试日志。


## 2026-10-03：系统依赖 Provider 扩展为 Git / Python / Node.js / FFmpeg

`manifest.json` 的 `requires` 继续保持“小程序只声明需要什么、宿主负责准备”的边界。本轮将原先 Git 特例重构为统一系统依赖 Provider：

- `git`：WinGet `Git.Git`
- `python` / `python3`：默认 WinGet `Python.Python.3.14`，支持最低版本判断
- `node` / `nodejs`：默认 WinGet `OpenJS.NodeJS.LTS`；最低版本超过 LTS major 时切换 Current
- `ffmpeg`：WinGet `Gyan.FFmpeg`

统一流程为：发现可执行文件 → 读取实际版本 → 比较 `>=` 最低版本 → 缺失或版本过低时 WinGet 安装 → 刷新当前进程 PATH → 再次发现和版本校验。多个小程序同时请求同一依赖时仍由 Resolver 按 canonical name 加锁，避免重复安装。

AI 系统提示词、小程序生成提示词、manifest 参考与开发规范均已更新。小程序可以直接声明：

```json
"requires": ["git", "python>=3.12", "node>=22", "ffmpeg>=8"]
```

本机真实 Provider 检测结果：Git 2.53.0、Python 3.12.9、Node 22.17.1、FFmpeg 7.1.1。能力回归为 62 checks passed。


## 2026-10-03：能力实验室小程序

新增 `prototypes/capability-lab` 并部署到本机 Extensions 的 `capability-lab`。该小程序用于观察和验证宿主基础依赖 Provider，因此故意不在自身 manifest 中预声明 Git/Python/Node/FFmpeg 的 requires，而是使用 `system.install` 权限主动调用 `dependency.status / dependency.progress / dependency.ensure`，从而在界面中展示检测、安装、PATH 刷新、版本验证与完成/失败阶段。

宿主新增只读能力 `dependency.progress`；当前进度阶段为 checking / installing / verifying / completed / failed，并提供百分比阶段进度。普通业务小程序仍应优先使用 manifest.requires 自动补齐依赖。

能力实验室包含四组真实实验：Python 纯标准库 Mandelbrot、Node.js 并行 SHA-256 链、FFmpeg 合成测试视频与截帧、Git 临时时间胶囊提交。原型通过燕子动态 C# 编译器编译；四组脚本冒烟测试全部通过；正式 Extensions 目录经 LocalExtensionCatalog 解析成功并再次编译成功。
