# 聊天通知与 K70 验证（本地 0.2.54）

范围：聊天独立 yanzi_chat 通道，默认 HIGH、系统通知铃声、200/100/200ms 两段振动、消息类别、私密锁屏；云端和 LAN 聊天使用此通道，一般同步和低优先级前台连接保持原通道。messageId 更新只提醒一次；通知失败不影响聊天保存与 ACK。

入口：我的 → 消息通知。显示 Android 通道设置、响铃模式、音量与勿扰；提供聊天通道设置、全部通知设置、声音设置和本机测试提醒。系统设置返回后刷新。HyperOS 自有开关可能进一步阻止声音/横幅，页面文案不保证系统必定展示。

验证：Dev / DevAndroidTest / Release / lintVitalRelease 已通过首次及干净构建；一次重复进程输出陈旧导致并发构建，出现 class 缺失错误，结束后串行 clean 重建通过；模拟器实际通知发送、聊天/同步通道隔离、重复更新、Dev 设置包名、重复注册保留重要级别通过；原下载 SHA-256、通知关闭仍落库、ACK 故障/丢失、去重、中断续传回归通过。

K70 同签名保留数据升级 0.2.54。系统原为振动模式，通知音量0。HyperOS 通道页悬浮和振动原关闭；已开启。锁屏改为显示通知、隐藏内容。两次本机测试：横幅截图可见；vibrator_manager 记录 NOTIFICATION 两段振动 finished。用户切回响铃并在聊天类别选定系统铃声后确认提示音可听；最终通道 sound 为具体 content://media/.../25 URI，userLockedFields=36。新通道初始化将解析系统当前默认铃声为具体 URI；保留已创建通道的用户选择，不通过重建通道覆盖。K70 原始间接默认 URI 的兼容行为仍需新安装设备验证。

云端文字 msg_d28455045140de085fdba3bf 22:02:25 dispatched/chat_saved/posted，22:02:27 ACK成功。
首次升级后的图片 msg_461b745fbd490a4277bfdf3a 网络重连后 22:01:38保存/通知，22:01:40 ACK；不是可靠的息屏实时证据。
最终明确息屏（Dozing，退到桌面）照片 msg_8d3b778f378c2c58bf73695a 云端 createdAt 14:06:56.014Z，超过一分钟仍 pending/deliveredAt=null。进程与前台服务在、isFrozen=false、deviceidle ACTIVE，但服务心跳暂停。亮屏后22:08:51拉取，22:09:51保存/通知，22:09:53 ACK成功。照片未丢失，但息屏实时验收失败，不能把最终ACK说成秒到。

下一步：定位息屏 CPU/网络调度与厂商后台限制，评估小米厂商推送唤醒；未接入小米推送/焦点通知，未保证进程终止后实时收到。避免无限持有 WakeLock 掩盖调度和耗电问题。未发布/推送/部署云端。

最终：铃声 URI 调整后的 Dev/DevAndroidTest/Release 与 lintVitalRelease 再次通过；fresh channel 选择系统具体铃声断言、原两套仪表回归通过。最终0.2.54保留数据覆盖，K70选定的Fresh URI及userLockedFields=36未改变，无AndroidRuntime崩溃。模拟器测试数据已清理并启动。LAN通知已接入保存后best-effort，但本轮未做实体LAN网络验收。
