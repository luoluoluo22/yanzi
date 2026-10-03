# 工程基础整改执行与验收

对应 [评估报告](engineering-foundation-review-2026-10-03.md)。本记录区分职责提取、视图文件整理和实际运行验收。

## 逐项落实

| 报告项 | 已落实的工程边界 | 验证 |
| --- | --- | --- |
| 设置窗口 | 账号、AI、搜索、触发器视图分文件，视图模型独立；SettingsPersistenceController 管理编辑基线和原子保存，SettingsAiController 使用不可变配置快照执行 HTTP，SettingsTriggerController 处理触发模式及快捷键校验 | 设置 89 项归属回归；嵌套并发修改、显式 null、删除、列表替换与复制隔离测试 |
| Android Activity | MobileApiClient 外观保留兼容；MobileSessionStore、MobileObjectRepository、MobileYanmController、MobileChatController、MobileWebViewRuntime 独立；Application 初始化后台 Context | Debug/Dev 构建、五项模拟器集成、干净启动；退出账号的旧请求不重试；对象超过 1000 项继续分页 |
| 桌面同步 | Yanzi.Core.AccountSyncCoordinator 负责账号代次、互斥与原子提交；对象仓库接口及 CloudAccountObjectRepository、AccountObjectSyncService 分离；云传输和设备消息独立文件 | 100 个并发操作串行执行，等待/运行中切换账号、取消后恢复；下载水位、冲突、pending 与对象回归 |
| Worker | device-api/sync-api 路由模块，device-repository/sync-object-repository 管理数据操作；依赖由入口注入；HTTP 错误和 JSON 解析集中 | 迁移后的真实 D1 两轮消息协议及 SDK，权威/兼容两种对象模式；SQL 仓库 1003 对象分页、历史和账号隔离 |
| 多端通信 | HTTP 状态与传输失败分开；只重试明确安全的请求；稳定 ID、固定目标、执行期限、持久化后 ACK 和恢复场景共用测试契约 | 35 项 Node 场景、纯 Java 传输测试、桌面重试及失效会话回归；附件、聊天和协议端到端 |
| 本地 API | LocalApiRouter 保留有序注册和中央认证；账号、文件、扩展、设置域独立文件；保留加密 LAN 与既有权限处理器 | 14 项真实 HTTP 检查：无凭据/错误凭据拒绝、无副作用、公开文档无 Token、OPTIONS、各域成功与 404 |
| 配置模型/迁移/存储 | AppSettingsModels、AppSettingsMigration、AppSettingsStore 分离；Update 在同一 IO 锁中加载、合并、归一化并原子保存 | 89 项本机/账号/秘密分类；AI 密钥边界、账号配置 round-trip、并发保存 |
| 跨平台核心 | 新增 net9.0 Yanzi.Core，提供账号协调器、JSON 编辑合并及对象仓库接口；WPF/Avalonia 引用，不引入 WPF 依赖 | CoreVerification 可独立执行；Windows 完整解决方案构建；CI Linux 单独构建/测试核心 |
| 自动回归 | 新增 engineering-checks CI（Node/Java/Core 与 Windows/D1）；五项模拟器测试各自持久化目录；失败路径也清理模拟器测试账号并启动桌面 | 完整 dev-smoke；真实 D1/消息与附件脚本；Workflow YAML 解析 |

## 修复的用户场景

- 设置页停留期间后台同步修改其他字段，保存本页时不覆盖那些更新；深层字段同样按改动合并。
- 退出或切换账号期间收到旧 HTTP 响应，拒绝旧结果，停止回退重试；排队的旧账号任务不会进入新会话。
- 对象数量超过首个快照页时继续读取 changes，不将缺失页当作完整账号状态。
- 旧聊天缓存已迁移到 SQLite；真机聊天验收直接检查持久化数据库，避免读取旧缓存造成假失败。
- 多台手机同时连接时可显式选择序列号；默认 Dev 包保留正式包和数据。

## 本地验证记录

- `dev-check`：完整 Windows 解决方案、Android Debug/Dev 构建及同步验证通过。
- `dev-smoke -SkipEnvironmentCheck`：通信回归、安装/启动、燕幕、业务存储、Windows↔Android、定义迁移/更新/tombstone、统一目录本机执行通过；已清理模拟器账号并再次完成干净启动。
- `test-cloud-object-sync`：authoritative=true 和 false 均通过。
- `test-device-network-foundation`：两轮各 85 项以及通用设备 SDK 持久化/去重端到端通过。
- `--local-api-boundaries`：14 项通过。
- `Yanzi.CoreVerification`：18 项场景通过，另包含 100 个并发操作互斥验证。
- `test-network-retry`：有限回退、冷却、恢复、POST 不重放、取消与失效会话检查通过。

## 云部署与真机验收

在本地检查全部通过后，仅使用 Git 推送 main 触发云端构建。生产健康端点通过 `foundationRevision=2026-10-03-domains-v1` 标识本轮实现。真机目标为一加 `81f7e66d`，安装隔离包 `cc.luoluoluo.yanzi.mobile.dev`。

2026-10-03 已完成以下实际验收：

- 代码提交 `b493395` 与 CI 清理修复 `999c34e` 已推送 main。Cloudflare Builds 自动部署 `999c34efa01cb00e0bd65611d1cfd302a46912d6` 成功，Build ID `14f5d8c8-ca30-4f78-ba13-9992a4bff0ab`；生产 `https://sync.luoluoluo.cc.cd/health` 返回本轮 foundationRevision。
- [GitHub 工程回归 37101938359](https://github.com/luoluoluo22/yanzi/actions/runs/37101938359) 的 Linux 和 Windows 任务全部成功。
- 一加已安装刚构建的 Dev `0.2.42-dev / versionCode 42`。APK SHA256：`DE02BDE14A6646199872EC966D06C77734E9BF9733518894E8619AABE669CBFF`。正式版 `0.2.42 / versionCode 42` 的安装路径及版本前后相同，未清数据。
- `test-real-phone-message-bridge -SkipBuild -SkipInstall -VerifyAccountLan` 完整通过：前后台通知 ACK、后台心跳、账号认证 LAN 发现、能力云回退、增量对象/tombstone、不重复改写、权限恢复、断线恢复、双向文本/图片/文件字节、实时连接、执行结果、120 秒实际离线过期与重新上线、进程重启补收。已恢复原 Dev 公网地址与偏好并重启桌面。
- `test-real-phone-public-foundation` 使用真机现有公网账号凭据完成鉴权、能力探测、中文/换行对象读写、旧 revision 409、当前数据保留、两条不可变历史及测试对象 tombstone 清理。

- `test-real-phone-lan-receive`：3,145,729 字节文件和图片的 SHA256 相同；丢失首块 ACK 后仅请求一次首块，重发恢复；相同 ID 再发，SQLite 只保留一条收件记录。测试桌面配对仓库独立，暂时暂停 Dev 的公网配对刷新，完成后恢复原配置，避免已有配对与测试授权相互覆盖。

- `test-real-phone-public-chat` 最终全量重跑通过：无 LAN/过时 LAN 的聊天窗口发送、10 次通知、双向图片与 600,000 字节文件的 SHA256、双方 WebSocket 在线、停止应用时保持 pending、重新启动后 ACK。通知服务端 ACK 中位数 2,430 ms，P95 2,543 ms；这衡量持久化确认，不等同于 UI 展示耗时。测试附件清理成功；此前一条遗留测试附件已精确核对哈希及创建时间并删除。
- `dev-real-phone -SkipBuild -SkipInstall` 最终通过：24 个已渲染的应用文本节点、无 Fatal/ANR、正式包保留。界面截图保存在 `%TEMP%/YanziDev/real-phone/android-smoke.png`。修复 PowerShell 5.1 将 ADB pull 成功进度误作异常的问题。手机仍连接原公网账号，保留正式版和 Dev 用户数据；测试附件已删除，局域网测试收件历史保留作为持久化证据。

公网验收产物：`%TEMP%/YanziDev/public-chat/50f99c4debdc40619d5ba720ac51342c`。局域网验收产物：`%TEMP%/YanziDev/lan-receive/9aa296eb50848edaf1ef9c82c504356`。

真机桥产物：`%TEMP%/YanziDev/message-bridge/1458b7a6fb3147a7ba668614032088ad`。

验收中修复了检查问题：聊天持久化已迁至 SQLite，脚本改查实际数据库并验证重复次数；启动检查增加可见内容断言；公网附件检查改用逐文件 SHA256，避免 Android shell 通配符检查的歧义。公网收件等待增加新消息 ID 断言，附件清理对 ID 去重并接受已删除的 404，避免旧收件记录与重复删除造成误判。ADB 清理连接卡住时只重启本机调试服务，随后确认手机偏好及正式包恢复/保留。

既有 release-inputs APK 与发布元数据保持原有工作区状态，不纳入本轮代码提交。

## 验证边界

视图 partial 仍共享窗口状态；UI 创建、绑定和 JavaScript 视图桥留在对应宿主，不能把文件变短称作全部领域解耦。提取的保存、网络、对象仓库和生命周期职责已独立。现有大视图后续可按功能继续改造，但本轮不更换整个 UI 框架。

macOS 运行未实测；Avalonia 只在 Windows 构建。既有编译器可空性/未使用字段警告仍存在，本轮新增会话代码引入的警告已消除。

### CI 新环境修复

首次 GitHub Windows 运行中的构建、同步、本地 API、桌面重试和 D1/SDK 场景全部通过，但清理阶段因写死 OpenQuickHost 目录名失败。现已改为精确匹配当前仓库配置路径，增加 6 项含 CI 路径、正反斜杠、不同项目及相似路径的进程归属检查；本地真实 Worker 启停回归再次通过。
