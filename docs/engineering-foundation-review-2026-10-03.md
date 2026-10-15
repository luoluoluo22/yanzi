# 工程基础评估与通信回归记录

日期：2026-10-03。范围：Windows 主项目、Avalonia/Shared 项目结构、Android 宿主与 data-sdk、Cloudflare 同步后端、两套 JavaScript SDK、构建及测试入口。

## 判断

项目已经具备可用的对象同步、revision 冲突、tombstone、账号隔离、持久化消息队列和加密 LAN 基础。当前主要开发成本来自业务编排集中在窗口/Activity、通信策略重复实现、测试入口随协议变化失效。继续增加功能之前，应按业务职责建立边界，并把恢复与错误处理的回归作为日常开发入口。

本次完成了结构评估、通信边界拆分、实际缺陷修复和多层回归；并未把所有大文件改造成独立服务，也没有逐个手工验收全部界面。文件拆分与领域解耦是两个不同层次，partial 文件仍共享同一对象的状态。

开始时工作区已有 Android、聊天、LAN、扩展宿主及发布输入修改。本次在其上继续修改，保留原有内容，未提交 Git、推送或部署。

## 需要优先处理的结构

以下行数为本次修改后的源文件计数，包含注释和空行；用于定位责任集中，不能单独代表质量。

| 优先级 | 位置与现状 | 对后续开发的影响 | 建议边界及验收条件 |
| --- | --- | --- | --- |
| P1 | `SettingsWindow.xaml.cs`，约 14,288 行 | 设置页、账号、平台配置和事件处理集中；新增设置很容易触碰无关状态 | 先按设置页拆控制器，保存/校验放服务；每页只管理本页视图。保持 89 项设置同步归属检查通过 |
| P1 | Android `MainActivity.java`，约 13,500 行，内嵌 `YanziApiClient` | UI 生命周期、WebView、小程序、云端操作和配置存储交织，后台功能依赖静态 Context | 后续依次提取账号会话、对象仓库、燕幕控制器、聊天控制器、WebView runtime；后台服务使用 application context。每次只迁移一个完整职责并保留外观适配接口 |
| P1 | `MainWindow.CloudSync.cs`，约 5,959 行；本次另拆出 1,387 行设备消息文件 | 同步调度、包同步、对象合并和窗口更新仍共享状态 | 下一步把账号同步协调器移为服务，注入 UI 通知、对象仓库和传输接口；保留每账号互斥、离线 pending、下载水位和冲突体验 |
| P1 | Worker `index.js`，约 6,100 行 | 认证、设备、对象同步、商店和订阅等路由共用入口和辅助函数 | 按路由拆 handler，SQL 放领域 repository；优先设备消息及对象同步。共享 HTTP 错误契约，测试使用迁移后的真实本地 D1 |
| P1 | Android/Windows/SDK 的传输与恢复各有实现 | 超时、VPN 兜底、HTTP 拒绝和副作用重放策略容易漂移 | 用共同场景契约约束实现：稳定消息 ID、固定目标、短期执行期限、明确终态、先持久化再 ACK；不要把所有 POST/PUT 视为可安全重试 |
| P2 | `LocalAgentApiServer.cs`，约 3,435 行，已有若干 partial | HTTP 分发仍与宿主命令和状态交织 | 每个路由域独立注册与授权；保留 LAN AEAD、目标权限及文件路径限制 |
| P2 | `AppSettingsStore.cs`，约 2,806 行 | 配置 DTO、兼容迁移和持久化职责集中 | 分离 schema/migration/store，不拆散同一事务；新增字段必须明确本机/账号/秘密归属 |
| P2 | Avalonia、WPF 与 `Yanzi.Shared` | Shared 当前主要共享菜单/输入模型，完整同步实现仍集中在 Windows | 在提取同步服务时引入不依赖 WPF 的核心模型和接口；本次只在 Windows 构建 Avalonia，未验证 macOS 运行 |
| P2 | Workflow 主要面向发布，脚本测试入口分散 | 构建通过可能掩盖长期失效的测试和协议差异 | 普通变更运行通信基础回归；同步/Android 变更加完整模拟器闭环；再将无秘密的 Node/Java 验证接入 CI |

## 本次落地的变更

1. 桌面设备消息桥、在线心跳、执行及剪贴板/附件呈现移入 `MainWindow.DeviceMessaging.cs`，相关状态字段一并迁移。仍为 MainWindow partial，没有宣称已消除 UI 耦合。
2. `CloudSyncClient.Transport.cs` 集中请求构造、代理/直连兜底、退避、取消及请求复制；复制请求体改为可取消的异步读取，辅助方法及时释放请求对象。
3. Android 新增 `MobileApiTransport` 和 `HttpResponseBody`，前台 API 与后台消息共用有上限的 UTF-8 响应读取；空错误流不再引发空指针，保留换行，失败关闭流。通用 API 上限 32 MiB，后台消息仍为 2,000,000 字节。
4. 云端地址即使是私网地址，也按云端请求处理；只有明确选择 LAN 的调用才使用加密 LAN。此前模拟器访问 `10.0.2.2` 的云 Worker 会误进配对逻辑并报 `pair_required`，完整对象同步回归已复现并验证修复。
5. 手机 VPN 兜底沿用 `CloudRequestRetry` 的明确安全列表，停止对全部 PUT 和认证 POST 自动重放。响应过大或线程已中断不再触发路由重试。通用 HTTP 版本标识改为真实 BuildConfig 版本。
6. Worker 抽出 `device-payload.js`、`http-error.js`、`request-json.js`；主入口原有 `readJson` 调用拒绝数组/标量，格式错误返回 400。其他领域 handler 自己读取 JSON 的路径仍需逐域统一。
7. 设备 SDK 在共享同一个 store 的同一 JavaScript 运行环境中合并正在处理的重复收件任务；不再把活跃任务误报为崩溃 `unknown`。仍保留崩溃恢复的保守处理。独立进程或不同 store 对象的并发仍依赖持久 CAS，不能将本次内存协调宣称为分布式恰好一次。
8. SDK 拒绝非法期限；没有 receive 适配器时保留消息并报错，不再静默 ACK；claim 缺少有效期限时不执行；网关 HTML 响应仍保留 HTTP 状态，403 终止，503 保留队列重试；HTTP 200 缺失消息 ID 或返回 HTML 时也保留原始队列与 ID，避免误报 accepted。
9. 修复桌面网络验证的反射签名、PowerShell 5/7 HTTP 错误读取、过时的账号聊天广播夹具。账号聊天使用 `routing=account-chat`，命令显式指定目标，避免把错误广播语义写成测试要求。
10. 云对象测试每次建立独立临时 D1 状态与日志，完整清理 Worker 进程树，解决共享开发数据库导致的 `SQLITE_BUSY`。
11. 新增 `scripts/test-communication-foundation.ps1` 并接入 `dev-smoke.ps1`；`cloudflare` 的 npm test 同时包含推送适配器测试，避免漏测 `tests` 目录。

## 已完成的用户场景验证

| 层次 | 实际执行入口 | 结果与场景 |
| --- | --- | --- |
| 桌面与跨平台编译 | `dev-check.ps1`，最终另执行 `-SkipAndroidBuild` | 解决方案构建通过；全量重新编译时仍有既有 nullable/未使用字段警告，不能把增量构建零警告等同于全部消除 |
| 纯通信与 SDK | `test-communication-foundation.ps1` | 33 项 Node 测试全部通过，含本次新增 8 项；两组 Java 场景验证通过；桌面有限重试、冷却、网络恢复、POST 不重放、调用方取消通过 |
| 桌面同步模型 | `Yanzi.SyncVerification`，由 dev-check 调用 | 包稳定性、冲突保存、并发变更、水位、秘密隔离、路径安全、89 项设置归属、对象隔离及 tombstone 等验证通过 |
| 多设备协议与实际 SDK 调用 | `test-device-network-foundation.ps1` | 两轮各 85 项检查通过，包含账号隔离、定向消息、独立 ACK、幂等、设备删除/撤权、命令 claim；Node SDK → Worker → 实际文件能力 → 结果回读通过 |
| 聊天与附件后端 | `test-mobile-backend.ps1` | Range 字节、SHA256、删除、跨账号拒绝、消息 ID 重用拒绝；账号广播、逐设备 ACK、新设备离线补收、命令目标隔离通过 |
| 云对象两种模式 | `test-sync-local.ps1` | authoritative=false/true 均通过；创建、双设备 revision、409 冲突、恢复历史、燕幕字段保留、AI/仓库秘密清理、非法 Token 拒绝 |
| Android 完整闭环 | `dev-smoke.ps1 -SkipEnvironmentCheck` | emulator-5554 安装启动、UI smoke、燕幕 WebView 写入、小程序存储读写/409/tombstone、Windows→Android→Windows、定义迁移/更新/删除、统一目录本机执行全部通过；最后清空测试账号并重新 clean smoke |

本次测试使用临时本地 Worker/临时账号和模拟器。没有清空、安装或覆盖真实手机生产包，没有对真实云账号写入测试数据；没有进行生产云部署、真实手机断网/熄屏、厂商后台保活或 macOS 交互验证。推送测试使用模拟供应商接口。

主要产物：

- 设备基础：`%TEMP%/YanziDev/device-foundation/afc082b012a04e6ba30d7a167ef999c9`
- 后端附件/聊天：`%TEMP%/YanziDev/mobile-backend/2367eeeed7d047019bf96b60ac5253c6`
- 云对象非权威：`%TEMP%/YanziDev/cloud-object-sync/60921921c4c5499eb64022d3eeb238cc`
- 云对象权威：`%TEMP%/YanziDev/cloud-object-sync/e22b2364745b4a729e523b57f64dc85e`
- 模拟器最终 smoke：`%TEMP%/YanziDev/android-smoke.png`、`android-logcat.txt`

## 后续开发的约束

- 传输成功、服务端 accepted、设备执行完成、业务数据同步完成分别表示不同状态；UI 只展示有证据的状态。
- 每次副作用明确目标、稳定 operation/clientMessage ID 和过期时间；响应丢失不能成为重新生成 ID、再次执行的理由。
- 对象提交沿用 expectedRevision；409 应保留本地编辑并提供可理解的冲突处理，不能自动覆盖最新远端数据。
- pending 数据、游标和对应缓存必须以同一持久化边界前进；删除通过 tombstone 传播。
- 会话切换需要防止旧请求更新新账号状态；下一阶段为同步协调器引入会话代次及可取消生命周期。
- 工程级恢复测试使用独立目录、临时身份和固定目标设备；测试失败也应恢复环境。其他历史脚本仍使用共享 Wrangler 状态，需按此次独立状态模式逐步迁移。

建议后续顺序：账号/同步协调器 → Android 会话与对象仓库 → Worker 设备/对象 handler → 设置页控制器。验收以行为和故障恢复为准，避免只把一个大文件拆成数个同样耦合的文件。


## 后续整改执行

用户要求逐项整改、云部署和真机安装验收后，继续执行的代码边界与验证记录见 [整改执行记录](engineering-foundation-implementation-2026-10-03.md)。上文保留初次评估时的状态。
