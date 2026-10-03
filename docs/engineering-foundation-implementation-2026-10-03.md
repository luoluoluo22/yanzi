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

实际部署与真机结果在完成后追加。既有 release-inputs APK 与发布元数据保持原有工作区状态，不纳入本轮代码提交。

## 验证边界

视图 partial 仍共享窗口状态；UI 创建、绑定和 JavaScript 视图桥留在对应宿主，不能把文件变短称作全部领域解耦。提取的保存、网络、对象仓库和生命周期职责已独立。现有大视图后续可按功能继续改造，但本轮不更换整个 UI 框架。

macOS 运行未实测；Avalonia 只在 Windows 构建。既有编译器可空性/未使用字段警告仍存在，本轮新增会话代码引入的警告已消除。
