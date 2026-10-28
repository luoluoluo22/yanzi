# 燕子 AI OS 能力网络开发计划

## 当前状态

### Phase 1 能力注册 ✅
- Capability Registry
- Provider SDK

### Phase 2 能力发现 ✅
- Manifest Discovery 基础
- Capability Query

### Phase 3 生命周期管理 ✅
- Runtime Registry
- Runtime Binding
- Provider 注销

### Phase 4 跨小程序调用 ✅
已完成：
- Invocation Service
- JSON 调用协议
- Call Log
- Clipboard Provider 接入

本轮完成：
- `capability.list` / `capability.describe`：返回版本、提供者、输入输出 Schema 和所需权限。
- 参数 Schema：调用前验证 MVP 子集，不支持的约束在注册时拒绝。
- 权限模型：WebView 使用宿主传入的小程序权限；匿名客户端默认无权限；已鉴权的本地 Agent 具有管理权限。
- 启动注册内置能力，WebView 与 REST 接入统一调用和有界审计日志。
- 修复 SDK Provider 注销和提供者替换后的清理，保留 manifest Schema。
- 50 项基础自动验证通过（`src/Yanzi.CapabilityVerification`）。
- C# 运行时已注入 `context.Capabilities`；Provider 从 manifest `provides` 读取契约，在实际运行时注册 Handler。
- 运行时结束注销能力，重启/热重载重新绑定；manifest 保存和重命名保留能力声明。
- 真实剪贴板小程序提供 `clipboard.search`、`clipboard.delete`、`clipboard.remind`。
- 真实日历小程序提供 `calendar.create`、`calendar.list`、`calendar.delete`、`calendar.fromClipboard`。
- 35 项真实运行时验证通过（`scripts/test-capability-runtime.ps1`），包括双向跨小程序调用、真实剪贴板捕获、日历落盘、权限拒绝、重启和热重载。

可继续扩展：
- AI 聊天工具路由和通用任务编排。
- EventBus 自动触发与权限授权界面。
- PowerShell / 其他外部进程的 Provider 注册协议。

本地 C# 能力网络 MVP 已完成真实业务闭环。真实小程序实现位于用户 Extensions 目录，未复制到仓库。接入方法、能力契约和回归入口见 `yanzi-capability-runtime-integration-2026-10-02.md`。

## 能力网络 MVP 验收目标（已完成本地调用闭环）

目标：

1. AI 可以发现能力
2. AI 可以理解能力输入输出
3. AI 可以组合多个能力完成任务

示例：

用户：找出昨天复制的网址并创建提醒

流程：

clipboard.search
        ↓
calendar.create
        ↓
完成任务


## 多设备基础协议补齐验收（2026-10-02）

按 [多设备审查报告](yanzi-device-network-audit-2026-10-02.md) 完成加密配对与撤销、账号隔离 Peer Registry、持久 Outbox/Inbox、LAN 即时送达后云端补投、执行期限/取消/claim、逐设备回执、分块续传与配额、trace/分页、受限设备凭据、浏览器/Node 通用 SDK。真实小程序互调已重新回归 35 项通过，桌面能力/API 50 项、网络专项 14/20/6 项、Worker/SDK Node 21 项及隔离 Worker 68 项通过；物理 Windows ↔ Android Dev 双向文件和终端、丢块确认恢复及完整云端重连回归通过。

协议及接入见 [协议 v1](yanzi-device-message-protocol-v1.md)、[SDK](../protocol/sdk/README.md)。新操作系统的后台/推送/原生能力仍需适配。云端新代码尚未发布；桌面已构建并自动启动，手机只升级隔离 Dev 包，生产包未改动。


2026-10-02 用户体验修订：设备连接改为同账号登录后自动建立，后台保持加密；取消用户复制设备 ID 和密钥的步骤。手机和电脑提供同账号连接状态页。受限网页能力授权仍独立，不因账号设备自动连接而扩大网页权限。73 项隔离协议检查及 22 项 Node 检查通过，真机已验证无人工导入的自动发现与加密握手。
