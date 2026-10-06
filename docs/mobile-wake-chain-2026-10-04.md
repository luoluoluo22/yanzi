# 息屏接收链快速修复与 Push 资格限制（2026-10-04）

当前候选构建 0.2.55/code 55；不表示 K70 息屏实时性通过。

已修改：
- WAKE_LOCK 权限与 MessageWakeLock：ready 调度、HTTP 消息拉取、消息处理至保存/通知/ACK 使用 PARTIAL_WAKE_LOCK，单次硬上限 30 秒；正常结束/异常/队列拒绝释放，系统超时兜底。下载或排队超过 30 秒仍可能暂停，未使用长期锁。
- Push 到达后用 messagesReady Intent 立即触发消费，跨服务启动的短锁移交接收调度；Push 本身不提前制造“已收到消息”的泛化通知。
- ready 拉取优先于心跳/维护；接收消费与维护继续隔离。
- WebSocket 使用应用层 ping/pong：30 秒探测、120 秒无数据/pong视为失效，清除逻辑在线并触发 HTTP 拉取和重连；旧连接回调有 generation 防护。普通线程在深度 Doze 中仍可能被延迟，这不是系统唤醒来源。
- Worker 不再因为 relay.isConnected=true 跳过 Push；保持原设备路由，只发送 messages-ready 与 messageId 元数据。
- 现有 FCM 是可选构建；本机 YANZI_FCM_CONFIG 未设置，候选 APK 未启用 FCM。小米 Push 尚未接入。

验证：
- Dev、DevAndroidTest、Release、lintVitalRelease 构建通过。
- 模拟器 Dev 原生 wake-chain：12 项通过，包含真实部分锁获取/释放/系统超时、重复释放和 WebSocket 活性边界。
- 既有 message-delivery 原生回归通过：SHA-256、通知关闭仍保存/ACK、失败 ACK 重试/去重、内容续传与丢失响应等。首次运行因通知未关闭而被测试前置条件拒绝；模拟器设置为 ignore 后通过，通知 appop 已恢复 default。
- Worker 协议与 Push mock 测试共 7 项通过，包括 relay 仍 connected 时也发送指定设备 Push。
- Dev APK 仅覆盖模拟器并启动；没有覆盖 K70 正式包，USB 未连接/无线 ADB offline。没有息屏真机验收，也没有真实系统 Push 验收。
- Worker 改动未部署。仓库禁止手动 wrangler deploy，只能通过 main Git CI；本轮没有提交或推送，不声称线上已改变。

个人开发者限制：
- 用户确认无企业资质。小米当前注册标准写明暂不接受个人开发者账号申请：
  https://dev.mi.com/distribute/doc/details?pId=1731
- 对公打款与法人身份认证是企业认证的可选路径，但营业执照仍需提供：
  https://dev.mi.com/xiaomihyperos/documentation/detail?pId=1145
- 第三方聚合推送的小米厂商通道仍要求应用自己的小米 AppID/AppKey/AppSecret，并不能免除通道资格：
  https://docs.jiguang.cn/jpush/client/Android/android_3rd_param
- 个人可以考虑 Firebase/FCM，但必须验证 K70 的 Google Play 服务及到推送服务的持续连接。不能用亮屏 FCM 成功替代息屏/网络切换验收：
  https://firebase.google.com/docs/cloud-messaging/android/get-started
- Doze 限制普通线程/网络并可忽略普通唤醒锁；短锁保护已触发处理，不能自行叫醒被系统冻结的连接：
  https://developer.android.com/training/monitoring-device-state/doze-standby

后续优先检查 K70 的 GMS/FCM 可用性。若走 Firebase，客户端应用配置与 Worker 服务账号须通过本机配置/云端加密 Secrets 提供，不能在聊天粘贴私钥。高优先级 FCM 的处理窗口有限，生产方案还需补充受系统管理的持久任务、通知展示及重试策略。若 K70 无可用 GMS，只能先改善自用设备后台配置并测量；不能承诺获得厂商 Push 的深度息屏秒到能力。
