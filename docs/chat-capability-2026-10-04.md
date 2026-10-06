# chat.send 正式能力接入与 K70 验证（2026-10-04）

## 已完成
- 内置 Provider 在 YanziBuiltinCapabilityRegistration 注册 chat.send 和 chat.status；无需动态编译小程序。
- 正式 /v1/agent/catalog 中均 available=true；capability.list 可发现完整 Schema、版本和权限。
- 本机账号下 Android 手机支持设备 ID、名称、唯一名称片段、本地别名。省略 target 时按在线优先、最近活跃排序；不会模糊匹配多个手机后悄悄选择一个。
- 本机配置 %LOCALAPPDATA%/OpenQuickHost/chat-target-aliases.json 的 K70 别名对应已知 K70 设备。别名必须匹配当前账号下的手机，不能绕过账号设备校验。此配置未硬编码进 Provider。
- 文件路径必须为绝对路径，附件 1 字节至 30 MB；原图走正常云端附件，支持自动判断 photo/file。文字不能为空，文字类型不能携带文件。
- 调用权限 chat.send=device.message.send、chat.status=device.message.read；Registry 校验权限，正式 Agent API 校验本机认证。
- ACK 等待超时或查询网络异常返回 pending、原 messageId 和 waitTimedOut/error，不把 pending 当成功，也不要求重复发送。
- 显式设备消息只认该设备的全局成功 ACK；账号聊天只认指定设备 receipt。失败 ACK 的 acked=false。
- chat.status 读取单条消息，不读取手机待消费队列，不修改 deliveredAt。
- 修复能力客户端与后台 replay 使用不同 CloudSyncClient 实例并发处理一个发件箱任务的问题：共享发送闸门，上传前重读持久任务，避免重复上传导致 409。
- Schema 支持 minimum/maximum；聊天 IPC 预算 180 秒，其余请求仍 45 秒。ACK 等待参数 1–60 秒，默认 30 秒，从消息提交后开始计时。
- Runtime 安装脚本在启动期间容忍健康探针暂时找不到管道，在既有期限内重试；仍保留激活失败回滚。

## 标准调用
通过已认证 POST /v1/capabilities/invoke：
```json
{"name":"capability.list","payload":{}}
```
```json
{"name":"chat.send","payload":{"target":"K70","filePath":"F:\\Desktop\\94e412f3-59c3-42aa-bd21-e4fc357641f2.png","waitForAck":true,"timeoutSeconds":60}}
```
等待超时后继续查询原消息：
```json
{"name":"chat.status","payload":{"messageId":"msg_b014f19db8b8bda8cb136c00","waitForAck":true,"timeoutSeconds":60}}
```
success=true 表示能力调用成功；只有 data.status=completed 且 data.acked=true 才表示指定手机成功接收。
waitForAck=false 只提交，返回 pending 和 messageId。
空 ackedAt/error 使用空字符串，契约与当前 Schema 校验器一致。

## 真机云端验收
- 原图：F:/Desktop/94e412f3-59c3-42aa-bd21-e4fc357641f2.png
- 大小：923,587 字节；SHA256：88352cf4156b38c718aefc9f5f779f0aa941d490d56a93b3c41a5d4d05215ee7。
- 真实路径：capability.list → capability.invoke(chat.send) → K70 ACK；全程未调用内部 chat.send。
- 调用开始：22:53:02.527；返回：22:53:27.924，约 25.4 秒。
- messageId：msg_b014f19db8b8bda8cb136c00。
- 指定设备：android-f85961cb-a306-46aa-8125-33f9d8614a7e（Xiaomi 23113RKC6C）。
- 返回 completed、delivered=true、acked=true、waitTimedOut=false。
- 服务器 ACK：2026-10-04T14:53:25.336Z（北京时间 22:53:25.336）。
- 第三次正式调用 chat.status 再次确认相同 ACK。
- 首次验收遇到并发 409，任务记录保存了真实消息 msg_136f7655562717b1cbb6d31d；后续只读查询确认它也已 ACK。第二次验收用于确认修复后的发送调用直接返回 completed。

## 验证
- Release CapabilityVerification 构建：0 错误（宿主既有 22 警告）。
- --chat-capability：34 项通过，包含发现、权限、Schema、路径、选择/歧义/别名、账号边界、两种 ACK 路由、失败、超时、HTTP 查询错误、IPC 预算。
- --outbox：7 项通过，包含断网、重启、丢失接受响应、附件复用、账号隔离、两个客户端并发同一任务只上传和 POST 一次。
- Shared Runtime 隔离验证：17 项通过，Shell 生命周期、常驻能力、API 与脚本取消。
- Runtime Release 构建成功并使用仓库安装脚本激活；启动桌面 Shell。

## 范围与限制
本轮完成正式能力发送闭环，未修改 Android APK、手机数据或云端 Worker，未提交或推送 Git。没有再次进行息屏测试；此前 K70 息屏接收延迟问题仍是独立的已知问题。本轮无 USB 连接，未用 ADB 人工核对手机聊天画面；成功依据为指定设备在既有落库后 ACK 链中返回的真实成功回执。

最终激活 Runtime：20261004-225632-024（PID 19612）；Shell PID 24544。最终目录与 chat.status 再次核验通过。
