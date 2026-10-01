# 燕子 (Yanzi) 项目 AI Agent 开发规范约束

本文件定义了所有 AI Agent（包括 Antigravity、Gemini 等助手）在参与本项目开发、维护、编译及发布时，**必须严格遵守**的行为准则与技术规约。

---

## 1. 脚本编码规范与 PowerShell 兼容性

> [!IMPORTANT]
> **PowerShell 5.1 编码限制**
> 1. 项目在 Windows PowerShell 5.1 下执行。凡是包含中文字符的 `.ps1` 脚本，在创建或修改时，**必须强制以 UTF-8 with BOM 格式保存**。禁止使用无 BOM 的 UTF-8，以防止中文字符串在运行时被解析为 GBK 造成严重的乱码与逻辑失效。
> 2. 禁止在 Windows 下使用 `&&` 或 `||` 进行命令链拼接。应使用 `;`（分号）或在不同的命令行中分段编写。

---

## 2. 版本发布与 GitHub Release 规则

> [!CAUTION]
> **发布乱码防范**
> 1. 发布新版本（运行 `upload-release-installer.ps1`）时，为规避 GitHub CLI 命令行转码和控制台字符集的乱码，**禁止**将包含汉字的临时更新说明直接写入文件并使用 `--notes-file` 传递。
> 2. 建议在打包完成后，直接调用 GitHub REST API 发送标准的 JSON 格式 `PATCH /repos/{owner}/{repo}/releases/{release_id}` 请求，通过内存中的 UTF-8 JSON Payload 更新 Release Notes 的 `body` 和 `name` 字段，以确保线上显示中文 100% 正确。
> 3. **网络与代理兜底**：在通过 `gh` 客户端或 API 访问 GitHub 时，如果环境为国内且开启了代理，需注入 `GODEBUG="http2client=0"` 环境变量强制关闭 Go 语言的 HTTP/2 Client，以解决由代理 ALPN 握手协议引起的常见 `EOF` / `connection reset` 错误。同时必须以 `-KeepProxy` 传给上传脚本。

---

## 3. Cloudflare 后端同步服务（Worker）部署与运维约束

> [!CAUTION]
> **禁止手动部署规约**
> 1. 本项目已接入 GitHub -> Cloudflare Git 自动构建部署集成。
> 2. **在任何时间、任何情况下，严禁在本地命令行中手动执行 `npx wrangler deploy` 等手动发布命令**。
> 3. 所有关于代码和配置的发布，必须且唯一依赖将代码 `git push` 推送至 GitHub `main` 分支，交由云端 CI/CD 自动完成构建与部署，以维护线上版本与代码仓库的绝对一致性。

> [!IMPORTANT]
> **Durable Object 与 Migration 冲突防范**
> 1. **现象**：部署时报错 `Durable Object namespace name '...' already in use. Please use a different name and try again. [code: 10065]`。
> 2. **原因**：`wrangler.toml` 里声明了已在云端生效过的数据库/Durable Object 的 `[[migrations]]` 迁移片段。重复执行时，Cloudflare 会因尝试重新创建已存在的同名空间而冲突中断。
> 3. **规约**：在云端初始迁移成功执行后，**后续进行业务逻辑或 API 日常热更新时，必须将 `wrangler.toml` 中的 `[[migrations]]` 部分注释或移除**，避免干扰日常自动和手动发布。

> [!WARNING]
> **机密环境变量（Secrets）防丢失规约**
> 1. **现象**：登录或接口访问报错 `Imported HMAC key length (0) must be a non-zero value...`（表示 HMAC 密钥为空，Resend 邮件 Key 缺失等）。
> 2. **原因**：本地手动运行 `npx wrangler secret put` 上传的机密变量，在触发 GitHub -> Cloudflare 自动构建（Git 集成 Auto-Build）后，会因构建环境未配置 Secrets 导致在重新 deploy 时被清空覆写。
> 3. **规约**：不允许仅依赖本地 secret 命令行上传。**必须在 Cloudflare Dashboard 控制面板上该 Worker 服务的 `Settings` -> `Variables` -> `Environment Variables` 中，手动以“加密 (Secret)”类型绑定以下三个常量**。这样每次 Git 自动构建部署后它们都将长久保持：
>    - `AUTH_TOKEN_SECRET` (JWT 签名密钥)
>    - `RESEND_API_KEY` (邮件发送 API 密钥)
>    - `RESEND_FROM_EMAIL` (邮件发送方地址)

> [!NOTE]
> **自定义域名路由映射**
> 1. **现象**：`workers.dev` 二级域名访问正常，但自定义域名 `sync.luoluoluo.cc.cd` 访问报 404 且 OPTIONS 请求无 CORS 响应头。
> 2. **原因**：本地手动运行 `wrangler deploy` 时，如果没有在配置文件中明确绑定 routes 触发器，云端就不会将流量分发至最新版本的 Worker。
> 3. **规约**：在进行 Worker 维护和迁移时，必须确保 `wrangler.toml` 包含正确的 `[[routes]]` 配置：
>    ```toml
>    [[routes]]
>    pattern = "sync.luoluoluo.cc.cd"
>    custom_domain = true
>    ```

---

## 4. 本地修改与测试构建后的自动启动规约

> [!IMPORTANT]
> **构建成功后自动启动程序**
> 1. 在完成本地代码修改并执行 `dotnet build` 构建通过（0 错误）后，AI Agent **必须自动启动程序**以便用户直接体验与验收，无需等待用户额外提醒或手动启动。
> 2. **进程管理与启动命令规范**：
>    - 若系统中已有旧版 `Yanzi` 进程在运行，先安全终止旧进程：
>      ```powershell
>      Stop-Process -Name Yanzi -Force -ErrorAction SilentlyContinue
>      ```
>    - 为防止进程受控制台生命周期回收影响，必须使用独立进程创建方式拉起可执行文件（兼容 Windows PowerShell 5.1）：
>      ```powershell
>      Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "F:\Desktop\kaifa\OpenQuickHost\src\OpenQuickHost\bin\Debug\net9.0-windows\Yanzi.exe --tray"; CurrentDirectory = "F:\Desktop\kaifa\OpenQuickHost\src\OpenQuickHost\bin\Debug\net9.0-windows" }
>      ```
>      脱离终端作业独立运行，保证桌面窗口正常渲染且不阻塞命令行交互。

---

## 5. 燕子小程序（Extension）开发与测试规约

> [!IMPORTANT]
> **扩展体系核心规范参考**
> 在新增、修改或维护燕子本地小程序时，必须深入阅读并严格遵循以下三份标准规范文档：
> 1. [《燕子小程序（Extension）开发规范指南》](file:///F:/Desktop/kaifa/OpenQuickHost/docs/extension-development-specification.md)：规定了 manifest.json 配置字典、独立源码文件模式、输入捕获优先级模型与交互原则；
> 2. [《C# 扩展运行时（YanziActionContext）API 参考》](file:///F:/Desktop/kaifa/OpenQuickHost/docs/csharp-runtime-api-reference.md)：说明了 Roslyn 动态编译机制、`YanziActionContext` 暴露的通知/日志/存储方法以及代码最佳范式；
> 3. [《LocalAgentApi 接口清单》](file:///F:/Desktop/kaifa/OpenQuickHost/docs/local-agent-api-reference.md)：汇总了本地代理服务的 RESTful 接口，包含扩展执行、存储读写与桌面通知的端点说明。

> [!CAUTION]
> **小程序架构边界与交互防线**
> 1. **代码边界解耦**：小程序具备完整的 .NET 9 BCL 与 Win32 互操作能力，应当自身闭环解决问题。**严禁为了单个小程序的功能改动燕子主程序源代码**。
> 2. **UNIX 静默交互哲学**：高频触发的快捷类小程序，**成功执行一律静默（不弹通知、不弹确认窗）**；只有当执行异常、文件不存在、启动失败时，才调用桌面通知报警。
> 3. **API 自动化测试入参规范**：调用 `POST /v1/extensions/{id}/run` 进行本地测试时，JSON Body 传递的输入参数字段名必须为 **`input`**（小写），切勿写错为 `inputText`。
> 4. **精炼命名规范（轮盘与背包呈现）**：小程序名称必须简练利落，**最长建议不超过 6 个字，以 2 ~ 4 个字为最佳**（如“日历”、“截图”、“智能识别”）。由于小程序会在【轮盘背包（Backpack）】和【燕环（RadialMenu）】槽位中高频呈现，过长的名称会导致扇区槽位内文字拥挤排版、折行重叠或被省略号截断。





---

## 6. 自主开发与跨平台验证入口

> [!IMPORTANT]
> 后续 AI Agent 修改 Android 移动端、同步层或跨平台小程序能力前，**必须先阅读 `mobile/android/DEVELOPMENT_STATUS.md`**，以恢复当前真实实现、已验证结论、已知问题和下一步，不要仅依据旧 README 或历史聊天重新推断。
>
> 后续 AI Agent 修改桌面端、同步层或 Android 移动端时，优先使用仓库内统一脚本完成闭环，避免临时拼接命令导致 SDK、ADB 或测试设备不一致。

- 全环境检查：`scripts\dev-check.ps1`
  - 验证 Git/.NET/JDK/Node/Android SDK。
  - 构建桌面解决方案与 Android APK。
  - 运行 `Yanzi.SyncVerification`。
- Android 模拟器：`scripts\dev-emulator.ps1`
  - 默认使用专用 AVD `YanziApi30`。
  - 真机与模拟器同时在线时，开发安装优先使用模拟器。
  - `-Stop` 可关闭模拟器，`-WipeData` 可创建干净测试状态。
- Android 开发闭环：`scripts\dev-android-loop.ps1`
  - 构建、覆盖安装、启动、检查前台 Activity、UIAutomator 文本节点、logcat 和截图。
  - 测试产物写入 `%TEMP%\YanziDev`，不得污染 Git 工作区。
- 桌面开发闭环：`scripts\dev-desktop-loop.ps1`
  - 构建、终止旧 Yanzi 进程并使用独立 Win32 进程启动新版本。
- 完整回归入口：`scripts\dev-smoke.ps1`
  - 依次执行环境检查、桌面/Android 构建、同步验证、Worker 语法检查、Android UI smoke、燕幕对象同步、小程序对象同步、Windows↔Android 共享存储测试。
  - 集成测试结束后必须清空模拟器内的临时测试账号数据，再做一次 clean smoke，最后重新启动桌面燕子。
- Android 破坏性集成测试默认只允许在 `emulator-*` 上执行：
  - `scripts\test-android-object-sync.ps1`
  - `scripts\test-mobile-extension-storage.ps1`
  - `scripts\test-cross-platform-extension-storage.ps1`
  - `scripts\test-mobile-extension-definition-sync.ps1`
  - `scripts\test-unified-extension-catalog.ps1`
  - 测试脚本必须自行清空测试 App 数据并注入临时本地 Worker 账号，保证测试间互不污染。
  - 唯一真机例外是隔离包 `cc.luoluoluo.yanzi.mobile.dev`；必须通过 `scripts\test-real-phone-dev-object-sync.ps1` 或显式 `-AllowPhysicalDev` 进入，绝不能对生产包执行 `pm clear`。

### 真实手机 Dev 隔离开发

- 用户要求：日常真机回归默认不主动熄屏，避免亮屏后需要反复手动解锁。确需熄屏验证时，只在最终验收阶段执行一次；消息回归通过显式 `-IncludeFinalScreenOff` 开启。

- Android 新增独立 `dev` build type：
  - applicationId：`cc.luoluoluo.yanzi.mobile.dev`
  - application label：`燕子 Dev`
  - 与生产/历史包 `cc.luoluoluo.yanzi.mobile` 可同时安装。
- 构建：`scripts\build-android-mvp.ps1 -Configuration dev`
  - 输出：`mobile\android\app\build\manual-dev\yanzi-mobile-dev.apk`
- 真机安装/启动 smoke：`scripts\dev-real-phone.ps1`
  - 只安装/覆盖 `.dev` 包；
  - 安装前后必须核对生产包版本与 APK 路径不变；
  - 产物写入 `%TEMP%\YanziDev\real-phone`。
- 真机 Object Sync：`scripts\test-real-phone-dev-object-sync.ps1`
  - 使用 `adb reverse` 把真机 Dev 指向本机临时 Worker；
  - 使用临时测试账号，不需要真实账号 Token，不访问线上同步数据；
  - 结束时撤销 reverse、清空 Dev 测试数据并重新启动 Dev；
  - 必须再次确认生产包未变化。
- 广播 Action、Widget Action、taskAffinity 和 FileProvider authority 必须基于 `BuildConfig.APPLICATION_ID` / `${applicationId}`，禁止重新写死生产 applicationId，否则 Dev 与生产版会串扰。
- 后台 `mobile-js` 运行时必须保持 WebView 强引用直到 `done/fail`，结束后主动 destroy；headless 路径调用状态/UI 更新时必须允许 View 尚未创建。

### 统一小程序跨平台约定

- **同一个小程序使用同一个 `extensionId`**，不要为 Android 另造一套身份。
- Windows 包与手机 `mobile-js` 是同一小程序的不同 runtime：
  - 手机存在 runtime 时，统一目录默认本机执行；
  - 手机没有 runtime、但账号存在 Windows 小程序时，手机向电脑发送执行请求；
  - 同时存在两个 runtime 时，长按可显式选择手机或电脑。
- 手机小程序定义的账号权威数据使用 Object Sync：
  - 索引：`mobileExtensions.index.v1`
  - 定义：`mobileExtension.v1.<sha256(extensionId)>`
  - 修改只写单个定义对象；删除使用 tombstone，并更新索引。
  - Android `SharedPreferences.mobileExtensions` 与 Windows `MobileExtensionsJson` 只作为设备缓存/兼容数据，不作为账号权威来源。
- 小程序业务数据统一使用：
  - `extensionData.v1.<sha256(extensionId + "\0" + key)>`
  - Windows 与 Android 必须共享同一个对象 ID、revision、409 冲突和 tombstone 语义。
- 对象 payload 的跨语言字段名统一使用 **camelCase**；Android 读取历史数据时可兼容 PascalCase，但新写入不得继续制造 PascalCase 对象。
- 手机定义读取必须先 GET 索引对象，再按索引精确 GET 定义对象；不要为了读取手机小程序扫描整页账号同步对象。
- 本地 Worker 测试端口必须通过 `scripts\dev-test-worker.ps1` 清理完整 Wrangler 进程树，不能只终止 `workerd.exe`。

真实手机上的已安装燕子可能使用不同 debug 签名。未经确认不得卸载、清除数据或替换真实手机上的正式/历史版本来解决签名冲突；默认使用专用模拟器进行反复安装与破坏性测试。
