# K70 蜂窝网络收图与 ACK 修复（0.2.53）
日期：2026-10-04，北京时间。

## 当前结论
用户指定的同一张原图 F:/Desktop/3c8012f6-108e-4ed6-8730-f5f76c110926.png（1,107,893 字节）已在 K70 当前蜂窝网络＋VPN、电脑 LAN 不可用的环境中完成云端下载、聊天保存和 ACK。
原图 SHA-256：b8a65037f1807edc66b4910abc299c9c03bd3245388acfe7d7f0ff5f61285a7f。

此前 0.2.52 的 Wi-Fi 场景成功不能代表本次蜂窝网络场景已修好。用户复测证实了剩余问题，本次按原消息日志定位并修复。

## 原消息与新消息证据
- 原消息 msg_4dd0da6651246a9d2c86af61：创建 21:28:04，deliveredAt 21:28:11，升级前持续 pending。0.2.53 先在 21:35:17 下载/校验并保存聊天，但 ACK 直连仍失败；补齐 ACK 网络路线后原消息于 21:37:35.828 返回 acked，手机日志 21:37:38.277 为 ack_success。没有重建或替换这条原消息。
- 最终完整复测 msg_50d4834d0bb3ad7ace8ee7a9：使用完全相同原图，正常 attachment 模式，创建 21:38:16.043，ackedAt 21:38:20.452，约 4.4 秒。手机日志包含 download_started、content_request route=system offset=0、download_done、chat_saved、receipt_saved、ack_started、ack_success。
- 最终手机截图显示 21:28 与 21:38 两次原图记录，电脑连接图标为云端；状态栏显示 5G 和 VPN。没有通过 LAN 或 inlinePhoto 代替本次云端验证。
- 服务端与手机日志时间存在少量差异，因此时间耗时统一使用服务端 createdAt/ackedAt 计算。

## 定位与改动
最初堆栈明确落在 MobileAttachmentClient.download 的 /content getResponseCode/TLS 握手阶段，反复 SocketException: Connection reset。元数据 GET 已支持切换到系统路线，但内容 GET 只有绕过 VPN 的物理直连，没有失败兜底。
内容路线修复后，原消息成功保存图片，ACK POST 又暴露相同 Connection reset：其请求也只有直连，且不属于原有 safe retry 列表。

修复：
1. MobileMessageClient 增加只读 readSystemFirst，附件元数据优先系统网络，传输失败时最多切换一次物理网络。
2. MobileAttachmentClient 内容 GET 也采用系统网络优先/物理网络兜底。每次尝试重新读取 .part 长度，通过 Range 继续；206 必须有匹配的 Content-Range，仍检查完整大小和 SHA-256。
3. 同一 messageId/device 的 ACK POST 采用系统网络优先及一次传输失败重试。服务端同一回执幂等，重复 ACK 不重新保存聊天或下载附件；其他写入未因此增加自动重试。
4. versionCode/name 升至 53/0.2.53，并保留数据覆盖安装 K70。账号、聊天记录和通知设置保留，最终通知 allow，未发现崩溃。
5. MessageDeliveryVerification 增加下载中断 16 字节后续传、Range=16，以及 ACK 响应断开后的安全重试验证。

## 验证和范围
Dev、DevAndroidTest、Release 构建以及 Release lintVital 成功。模拟器 message-delivery 原生回归通过：正常下载/SHA-256、通知关闭、ACK 500、聊天去重、明确缺失附件失败、内容中断和续传、ACK 响应丢失后重试。
相关文件 diff --check 通过。模拟器临时测试数据已清理，Dev 重新启动。
K70 最终安装 0.2.53/code 53，签名与之前版本相同，未卸载或清空正式包。
本次没有发布 GitHub Release、推送代码或部署 Cloudflare。未实现只读 peek 队列；未做息屏长期验收或 WebSocket 半断线故障注入。

源码位于远端 F:/Desktop/kaifa/OpenQuickHost。
