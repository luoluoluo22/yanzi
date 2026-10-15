# 燕子 Antigravity CLI → 手机增量流式响应验收（2026-10-15）

## 链路
- 本地小程序 `extensions/antigravity-chat/main.cs` 按目标手机与会话分配单独任务目录，进程当前仍为每轮一次。
- `worker.cjs` 使用 `agy -p ... --output-format stream-json`；逐行读取 NDJSON 的 `init / step_update.agent_response.text_delta / result`。
- `chat.stream` 只传临时快照：优先通过手机已认证 LAN，断开时经 Cloudflare 认证的 `POST /v1/me/mobile/messages/stream` → Durable Object WebSocket 推送；不进入消息表、持久队列或离线推送。
- 手机 `AgentStreamProtocol` 验证 `\u001eYANZI_STREAM:` 控制消息、seq 和会话 ID；`MainActivity` 更新同一临时气泡，最终收到原 `chat.send` 的持久消息后按正式历史刷新。
- `/取消` 仍终止当前 agy 进程树（每轮一次进程）；`/恢复` 负责残留锁恢复。

## 验证
- Antigravity 1.3.5 真机命令输出已看到 `init`、`agent_response.text_delta`、`result.status=SUCCESS`。
- Node worker: 12/12 测试通过，包括流式快照在最终消息之前到达。
- Cloudflare: npm test 41/41 通过；新增测试断言 stream 路由不访问 D1。
- C# 桌面编译成功（0 错误）；Windows win-x64 self-contained 发布。
- Android Dev APK 构建成功，安装到一加设备并启动。
- Cloudflare Worker 已发布：`c3e9c53e-3dad-459f-8e4d-57f5a4cb9098`；未授权接口回 403。
- Windows 当前版通过原子目录切换安装；备份位置：`%LOCALAPPDATA%\Yanzi\backup-pre-stream-20261015-141755`；启动成功。
- 已在一加 Dev 真机新建反重力会话、发送测试输入；手机显示「停止」，桌面 worker 启动、执行完毕，手机收到最终五句回复并恢复「发送」。
- **尚未完成**：实际生成期间未观察到增量临时气泡；Android 日志出现 WebSocket reconnect，仍需实测实时连接及 LAN 路由；停止按钮的真实中途终止还未单独验收。

## 未完成/边界
- 持久 `--input-format stream-json` 会话常驻进程属于第二阶段，尚未实现。
- 持久消息端点单条文字长度有限（当前约 4000 字符），长结果仍被 worker 分段持久化，不能笼统声称所有结果只写一次。
- 临时快照在手机离线或 WebSocket 不通时不会恢复；最终回答仍通过可靠 `chat.send` 交付。
- 备份目录是回退依据；不要复制单个 DLL 混装，自包含发布应全目录更新。
