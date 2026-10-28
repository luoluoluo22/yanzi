# 燕子 Antigravity CLI 真流式与常驻会话验收 — 2026-10-15

## 已完成的架构
- `extensions/antigravity-chat/worker.cjs` 使用 Antigravity CLI 1.3.5 的 NDJSON `--output-format stream-json`，逐行消费 `agent_response.text_delta`。约 220ms 合并一次全量快照，不为每个 token 写数据库。
- 手机消息的临时事件使用 `chat.stream`：同 LAN 优先通过已认证设备直连；Cloudflare `POST /v1/me/mobile/messages/stream` → Durable Object WebSocket 作为回退。临时事件不进入 D1 消息表、离线推送和持久队列；完整答案仍经 `chat.send` 可靠投递。
- 流式协议为 `start → update* → complete`；可发送 `cancelled` 和 `error`。手机复用同一临时气泡，完成事件可立即恢复「发送」按钮，最终消息到达后按历史刷新。
- 第二阶段的 Persistent Session 已接入：每个燕子反重力会话拥有一个常驻 `agy.exe`，通过 `--input-format stream-json --output-format stream-json` 与 stdin 交互；闲置 30 分钟后由 daemon 清理。会话互不共用子进程，停止一轮时终止当前会话进程，按保留的 conversationId 在后续消息中恢复。
- 修复真实 CLI 首轮握手：`init` 在输入第一条 user 消息后才发出；不能等待 `init` 才写入 stdin。

## 实际检查结果
- 真实 Antigravity CLI 两轮 stdin 测试：收到 2 个 result 及增量事件；基于 `PersistentCliSession` 的真实双轮测试 2/2 SUCCESS 且 PID 未改变。
- 从一加 Dev 真机同会话连续两轮请求：两条桌面任务的 CLI PID 相同（6040）；手机呈现实时文字增量。
- 实际 LAN 流式任务：发送 6 次、成功 6 次、失败 0 次；手机日志出现 `start`、多个 `update` 和 `render`。
- 部署 `complete` 后，手机记录 `start seq=0 / update seq=1 / complete seq=2`，点击前显示停止，完成后恢复发送；完整回复成功保存并显示。
- Worker 单元测试 15/15 通过；持久会话模拟测试覆盖同 PID、会话隔离及停止互不干扰；测试结束残留临时进程为 0。
- Cloudflare npm test 42/42 通过，含流式完成态与「不写入 D1」约束；线上版本 `1f8ced64-e25b-4d29-8e59-b6a8ab8e7a6c`。
- 桌面 Yanzi.Runtime 已发布并通过后台服务健康检查，当前备份指针 `%LOCALAPPDATA%\YanziRuntime\runtime.pointer-pre-complete-150526.json`。一加 Dev APK 已安装。
- 独立真实 CLI 中断恢复测试：停止当前会话进程后，重新启动并返回 `RESUMED`，未留下本次集成测试的 CLI 孤儿进程。

## 后续验收边界
- **手机端「停止」尚未完整通过**。实测捕获 `YanziAgentStop: stop failed IllegalStateException`，根因是云端预取消异常阻断了实际 `/取消`。已修复为「预取消尽力而为，失败仍发送 CLI 取消」，重新构建安装 Dev 包；补丁后的真机取消状态仍需再次有效验证，不应宣称手机端停止完全验收。
- Cloudflare 公网 WebSocket 回退仍缺稳定的真机连接验收；已补充连接失败类型和 HTTP 状态的匿名日志。LAN 流式不受影响。
- 当前 PC 仍运行 `Yanzi.exe` + `Yanzi.Runtime.exe` 双进程，这是另一个「恢复单程序」项目的问题，本次仅保证实际后台 Runtime 获得新能力。
- 可靠聊天消息可能按长度分段持久化，不应声称任意长度回复始终只写一次。离线期间临时 delta 不补发，最终回答依然可靠送达。
- 合并正式主分支前应完成云端回退、手机停止、重连和复用会话的最终联调。
