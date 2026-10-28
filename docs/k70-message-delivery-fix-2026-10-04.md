# K70 图片接收修复与验证
日期：2026-10-04（北京时间）

## 已完成
K70（USB e3e68545，型号 23113RKC6C）从 0.2.51/code 51 保留数据覆盖升级到本地修复版 0.2.52/code 52。用户明确同意覆盖升级。升级前后签名证书 SHA-256 相同：8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3。未卸载或清除正式包，账号和聊天记录保留，最终 App 前台运行且未发现崩溃。通知 POST_NOTIFICATION 已恢复为 allow。

## 修复内容
- DeviceHeartbeatService：companion、外部访问、状态快照、账号同步调度、heartbeat、LAN refresh、receipt cleanup、outbox replay、realtime connect 分别隔离异常，辅助步骤失败继续执行消息轮询。心跳成功立即记录成功时间；维护步骤仍按 60 秒周期执行。
- BackgroundCadence：实时连接状态补偿轮询从 5 分钟缩短为 30 秒，断线保持 5 秒。
- 图片/聊天接收：移除通知权限前置退出，先下载校验、保存 SQLite 聊天记录、持久化 receipt，再 best-effort 通知和 ACK；持久化失败不能误报接收成功。
- ChatHistoryStore：增加按账号和 messageId 检查持久记录的查询，区分真正写库失败和 ACK 重试时已存在的记录。
- ACK、下载、聊天保存、receipt、通知增加按 messageId 的日志与异常堆栈。
- MobileMessageClient：只保存并输出经过格式过滤的服务端错误码，不输出凭据或原始错误正文。
- 服务端明确返回 HTTP 404 attachment_not_found 的消息提交 success=false 回执；其他 HTTP/传输失败继续重试。缺失附件不会记为图片成功接收。
- 新增 MessageDeliveryVerification，由 LanRecoveryTest 的 message-delivery suite 调用。

## 实机证据
| 场景 | messageId | 最终结果 |
|---|---|---|
| 用户原始约 1.1 MB 图片 | msg_49d3aac9b4687fe139f1e183 | acked，21:01:07；发生在本次安装修复前 |
| 0.2.51 重新发送正常附件 1,176,303 字节 | msg_579ff8b6253f9fc80c6801f0 | acked，21:15:56；创建后约 6.5 秒 |
| 0.2.52 临时关闭通知后，正常附件原图 | msg_77883bcb8f664853ab944ede | acked，21:18:23；创建后约 7.8 秒 |
| 最终修复版云端 mobile.status.get | msg_2745a6483807cbf7e4099cb7 | completed，21:24:27；创建后约 5 秒 |
| 历史广播附件在 K70 上缺失 | msg_8740199cb0a63bb13fe4ed81 | K70 receipt=failed，21:21:47；其他设备既有 acked 保留 |

正常图片使用 chat.send 的 attachment 模式，没有使用 inlinePhoto。实机日志出现 dispatched、download_started、download_done、chat_saved、receipt_saved、notification_attempted、ack_started、ack_success。最终截图看到图片聊天记录；原有图片在升级后继续显示。云端附件下载不依赖电脑 LAN 接收路径。

原报告把 deliveredAt 当作手机接收的证据并不可靠，本次最终确认依据 ackedAt、K70 消费日志与手机图片显示。检查没有调用会污染 deliveredAt 的 chat.queue。

新日志定位到五条历史广播图片/文件反复失败于 MobileAttachmentClient.download 的元数据 GET，返回 attachment_not_found。它们的 K70 失败回执已写入，避免反复重试。全局 message.status 可能是其他设备先前的 acked，广播必须查看指定设备 receipts。

## 验证
Dev、DevAndroidTest、Release 构建成功，Release lintVital 通过。
模拟器原生 message-delivery 回归通过：辅助阶段异常隔离、30 秒补偿规则、正常附件下载与 SHA-256、关闭通知、ACK 500 后重试、单聊天记录/单下载去重、文本、明确缺失附件失败回执。模拟器临时测试数据已清理，并重新启动 Dev 做干净启动。
本次改动文件 diff --check 通过；仓库存在大量其他预先修改，未提交、推送或发布这些更改。

## 限制与后续
- K70 未制造 WebSocket 半断线、LAN 故障或息屏长时间场景。30 秒补偿是代码及规则验证，不能称为这些实机场景已全部验收。
- 清理异常测试验证隔离 helper，没有在 K70 上制造 SharedPreferences 磁盘提交失败；不能据此断言它是原始故障的唯一根因。
- 服务端真正无副作用的 peek 队列接口未在本次实现；chat.queue 临时实现保持原状，本次未调用。
- chat.send/chat.status 已由现有 Runtime 支持并用于验证，本次未修改桌面 Runtime。
- 仅构建并安装本地 Android 修复包，没有发布 GitHub Release 或部署 Cloudflare。
- 源码位于远端 F:\Desktop\kaifa\OpenQuickHost。Android app/build.gradle.kts 已预先为 0.2.52；未在本次改版本号。
