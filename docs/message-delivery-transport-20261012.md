# 消息收发低延迟与恢复机制（2026-10-12）

## 设计决策

- Android 普通文本消息：先行云端可靠提交，局域网在独立有界线程池并行进行 best-effort 投递。LAN 超时绝不阻塞云端。LAN 与云端共享 clientMessageId、clientOperationId，由 Windows 回执去重。
- Android 执行命令：仍可 LAN 优先，不允许不受控并行执行相同命令，以免重复副作用。
- Windows WebSocket：每 25 秒发送应用级 ping，Cloudflare DeviceRelay 返回 pong；超过 90 秒没有任何帧则主动 Abort 并重连。命令处理期间暂停活性超时，避免中断正在运行的任务。
- Cloudflare SSE 当前仅有保活信号，不能作为实时消息源；客户端断线后用 WebSocket 退避重连，辅以 60 秒的 HTTP cursor 补偿。禁止恢复高频 D1 全表查询。
- Windows inbox 的 receivedAtUtc 记录实际接收时间，不复用服务端 createdAt。Android 日志标签 YanziMessageTiming，Windows 日志标签 MobileMessageTiming；clientSubmittedAt 的端到端指标可能受设备时钟漂移影响。

## 验收

- Windows Release 构建 OpenQuickHost、Yanzi.Runtime、Yanzi.RuntimeVerification；用 scripts/install-shared-runtime.ps1 -SkipBuild -Activate 安装，运行时 health 的 mobileBridge 必须为 true。
- Android Gradle 执行 :app:assembleDev --offline --no-daemon，生成独立的 .dev APK，不得用于替换正式包。
- Cloudflare 测试：node --test cloudflare/src/device-relay-events.test.mjs cloudflare/src/device-message-protocol.test.mjs cloudflare/tests/mobile-push.test.mjs。
- 真机测试：用 messageId 关联 Android 发送、Cloudflare 入库、Windows 收到与 ACK、UI 通知，分别测试同 LAN / 不同网 / 离线重连 / 熄屏唤醒。手机不在线时不得声称真机测试已通过。

## 数据和回退

- 源码快照保存在 .artifacts/message-delivery-repair-20261012/。
- Windows 安装前的快照指针为 %LOCALAPPDATA%/YanziRuntime/runtime.previous.json；安装脚本自带故障回滚。
- 正式 Android 需要正确签名凭证与真实设备安装测试，不能将调试证书 APK 当作生产更新。
- 本次不改动 Cloudflare 数据库结构、身份密钥、消息路由协议和云端配额策略。
