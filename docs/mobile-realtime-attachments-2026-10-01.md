# 跨端消息实时连接、附件与推送接入

## 已实现机制

消息先写入 D1，再经账号隔离的 Durable Object WebSocket 推送。Windows 与 Android 收到后执行原有收件流程并提交 ACK；断线重新连接时从 D1 补收。连接存在时每 30 秒兜底查漏，Android 无连接时 5 秒轮询；Windows 保留 SSE 和 5 秒轮询兼容旧后端。设备心跳仍为 30 秒，在线判定窗口为 120 秒。长连接与在线心跳分别负责消息与状态，不能把“在线”理解为保证立即送达。

PC 聊天窗口文字、图片、文件均支持直连失败后转云端。云附件上传至现有 R2 桶的 private-mobile 前缀，消息只携带引用。读取需要同账号鉴权；不提供公开下载链接。大小上限 30 MiB，保存 7 天，由每 30 分钟的定时任务分批清理。手机与电脑均校验 SHA256/大小，下载支持 Range 和临时文件续传。手机使用私有文件目录和 FileProvider 打开附件，图片在聊天中预览。

发送提供 clientMessageId 去重，同一个 ID 不同内容返回 409。Android 保存收据后 ACK；Windows 对命令及文件操作写入执行开始/完成收据，重启后不自动再次执行。执行中途断电时结果可能未知，会返回中断说明，用户确认后重新发起；不能承诺分布式“恰好一次”。接收回执表示客户端已处理，并非用户已读。

## 系统推送配置

用户确认尚未开通推送服务。本轮提供实际 FCM SDK 可选构建、Token 注册、HTTP v1 发送适配器与 HTTPS webhook 适配器。无配置时正常使用长连接和前台服务，但不能承诺系统结束 App 后仍可唤醒。FCM 需要手机能使用相应 Google 服务；无 Google 服务的手机需另外接入厂商 SDK，webhook 仅提供服务端桥接协议，并不是已完成全部厂商接入。

FCM 开通后：

1. 在 Firebase 中分别登记生产 package 与 Dev package，配置文件保存在仓库外。
2. 构建时设置 YANZI_FCM_CONFIG 为对应 google-services.json 的绝对路径；文件存在才引入 Firebase Messaging 及推送服务。默认无配置构建不依赖 Firebase。
3. 将服务账号 JSON 配置为 Worker 的加密 FIREBASE_SERVICE_ACCOUNT，须在 Cloudflare Dashboard 配置并按仓库规定确保 Git 自动构建保留。
4. 真机打开 App 获取 Token；Token 通过设备注册上传，日志不输出 Token。
5. 断开长连接验证离线推送、后台恢复及权限禁用。强制停止 App 的系统限制、国产 ROM 的电池策略和 Android 13/14+ 权限还需对应真机验证。

自建推送桥接服务使用 Worker 加密 PUSH_WEBHOOK_URL（HTTPS）和 PUSH_WEBHOOK_TOKEN，接收 Bearer 鉴权 JSON：token、messageId、type=messages-ready。厂商客户端 SDK 取得 Token 后调用 MobilePushSupport.registerToken(context, "webhook", token)，推送到达调用 receive；具体厂商 SDK 尚待账号开通。

## 发布前置条件

2026-10-01 已通过 Git main 自动构建部署新后端。代码初次推送为 3bb3e0f，发布链路修正为 61acb66，两次 Cloudflare 构建均成功。线上 D1 0018 迁移已完成，DEVICE_RELAY 命名空间及每 30 分钟清理任务已确认生效，原有鉴权/邮件 Secret 绑定保留。

必须先应用 D1 migrations/0018_mobile_attachments.sql，并部署 DEVICE_RELAY Durable Object 绑定和首次 device-relay-v1 迁移。后端发布遵守 .agents/AGENTS.md：仅 Git main 推送触发 Cloudflare Git 自动构建，禁止本地 wrangler deploy。上线后依据仓库要求处理已生效的 migration 配置。生产部署前需核对 Git 构建配置中的数据库迁移步骤；当前仓库没有对应自动迁移 workflow。

已在当前真实网络上验证 Windows 与 Android Dev 的公网通信；生产手机包仍保持原版本，未升级或清空。具体国产厂商推送、不同运营商/网络覆盖、Android 13/14+ 真机仍待额外配置与对应设备验收。

发现旧 pre-push 配置会强制部署历史版本，已移除该行为，仅保留原有 Secret 同步。PC 在旧后端回退至 SSE 时，现在每两分钟重新尝试 WebSocket，避免后台发布后一直停留在旧通信模式。

最终公网回归产物：%TEMP%\YanziDev\public-chat\59948230f72141d68bc291e5fa7c21d4。实际 WPF 聊天窗口无 IP、失效 IP 两种文字发送均成功；PC→手机及手机→PC 文件（约 600 KB）/图片均完成下载并验证 SHA256，两端 WebSocket 状态正常，手机进程重启后补收 pending 消息成功。全程未主动熄屏，临时云附件已清理。

最终 10 条公网通知从服务端提交到手机 ACK 的中位耗时为 2934 ms，p95/样本最慢为 4371 ms。之前两组中位耗时为 2562/3253 ms，最慢一次为 6563 ms，并观察到手机 HTTP 超时后自动恢复。ACK 包含通知处理和回传网络开销，并非通知界面显示延迟。不能承诺微信级时延，也不能把网络波动直接归因于 Cloudflare 免费套餐。长连接消除了定期轮询等待，但当前网络路径仍有秒级抖动。

## 验证入口

- scripts/test-real-phone-message-bridge.ps1：临时本地 Worker、真实 Windows 聊天窗口、实体 Android Dev；恢复原偏好并核对生产包不变。
  默认不主动熄屏；只有最终验收确需时，显式传入 -IncludeFinalScreenOff，在全部功能测试结束后执行一次。
- scripts/test-mobile-attachments.ps1：校验错误、Range、删除、重复消息与 ID 冲突。
- scripts/test-real-phone-public-chat.ps1：已有账号的公网双向附件、两端长连接、通知 ACK 延迟、进程重启补收；-TextOnly 可只跑两个文字入口。
- node --experimental-default-type=module cloudflare/tests/mobile-push.test.mjs：真实 RSA JWT 签名验证、模拟服务商传输、无配置与失败分支。
- FCM 可选构建已用临时无效配置编译成功，未请求真实推送服务；最终测试 APK 使用无 FCM 配置构建。

最终完整真机回归通过，产物为 %TEMP%\YanziDev\message-bridge\b86dfc8f65d24fadae5731e740239e11；10 条消息提交到 ACK 中位耗时 68 ms，样本最慢/p95 87 ms。该结果只代表本地 Worker + adb reverse，不代表公网网络质量。补充后端范围/鉴权/校验回归也通过，详见 DEVELOPMENT_STATUS.md 第 16 节。
