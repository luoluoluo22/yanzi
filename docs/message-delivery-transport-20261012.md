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

## 运行中限制与安全验收

- 2026-10-12 台式机实际 Runtime DLL 与已构建宿主 SHA256 一致，健康状态 mobileBridge=true，mobilePolling=false，Cloudflare WebSocket 已连接。
- 尝试由服务器身份向桌面发送验收消息返回 device_scope_denied：服务器授权仅包含现有目标，未扩大授权。该测试没有生成被接收消息，不得记为端到端通过。
- 初次检查时手机无 ADB 连接，Dev APK 尚未安装。其后真实设备已连接并完成下述回归；正式 Android 包仍未更新。


## 2026-10-12 OnePlus 实机补充验收

- ADB 设备 OnePlus GM1900 (Android 11) 已连接。正式版与开发版均为 0.2.55；已先导出现有 Dev APK 备份，再以 Android Gradle 临时版本覆盖参数构建 0.2.55-dev 开发测试包，并通过同一调试签名原地升级。正式包和用户数据未被删除。
- 首次发送：手机→桌面局域网处理耗时约 308 ms，LAN 确认 383 ms，但云端 POST 回退到 Android 系统网络前等待物理网络超时，cloud_accepted=12,734 ms。
- 修复：MobileMessageClient 仅对 POST /v1/me/mobile/messages 优先使用系统网络（VPN 可用时即走 VPN），对安全重试且有稳定 clientMessageId 的提交才允许失败后物理网络兜底。ACK 原本已经 system-first，其他非消息端点保持原有路由。
- 修复后真机复测：手机→桌面处理 271 ms，LAN 确认 173 ms，云端 POST 入库确认 402 ms。Cloudflare 单条消息查询给出 desktop 回执 status=completed / acked=true；Windows 仅处理一次，没有重复弹窗。
- Windows→OnePlus Dev 定向消息两次均 completed / acked=true，手机 logcat 记录 chat_saved、receipt_saved、notification_attempted、ack_success。
- 屏幕 OFF 且 Android 报 Dozing 状态时再次发送：OnePlus Dev 已保存并 ACK；验证结束后恢复屏幕 ON。该结论仅针对短时熄屏，不代表长时间 Deep Doze 或厂商强制限制已被验证。
- 局限：同一 Wi-Fi 条件与当前设备 VPN 状态得到的数据，不可外推至蜂窝网络或异地网络；本次未发布 Android 正式版。版本 0.2.55-dev 为测试覆盖参数构建，不代表代码库默认版本已经同步升级。
- 原有开发包已备份于 .artifacts/mobile-message-qa-20261012/before-android-0.2.55-dev.apk；用于本次验收的测试包保存于该目录。所有账户凭证及其他未提交开发改动均保持不变。
