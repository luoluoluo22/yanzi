# 燕子：开发质量闭环 → ChatGPT 来源对话回传（首版）

## 已实现的链路

开发修改 → `scripts/run-yanzi-quality-loop.ps1` 统一验收 → 版本/日志/截图/报告落盘 → 严格来源路由 → `POST /api/feedback` → 工作台 WebSocket → 燕子 Edge 扩展 → 原 ChatGPT 对话输入框提交 → 保留回复 Job 状态。

`scripts/watch-yanzi-quality-loop.ps1` 是可选的**有限轮次**源码变更监听。代码变更稳定后运行统一验收，按 `TaskId` / `Round` 回传结果。不会自动启动、不会开机常驻，也不会自动部署。

## 原始 ChatGPT 标签页如何绑定（必须先绑定一次）

当前 ChatGPT 工具调用环境**不会向燕子插件透传“发起当前 ChatGPT 消息的浏览器 tabId / conversationId”**。所以不能从“当前活跃标签页”猜测来源；用户可能在手机上发起任务，Edge 则同时打开多个聊天。

新版 Edge 浏览器助手增加上下文菜单：

1. 在 **ChatGPT 已保存的正式对话**（`https://chatgpt.com/c/...`）里右键页面空白处。
2. 选择 **「燕子：将此 ChatGPT 对话设为开发反馈回传目标」**。
3. 插件核查来源页面和临时对话模式，将 `conversationId`、规范 URL、tabId 交给本机 `chatgpt-bridge`；bridge 返回唯一绑定 `originId` 并持久化于 `state.json`。扩展图标显示 ↩ 表示绑定成功；! 表示失败。扩展弹窗日志状态存在 `chatgptFeedbackOriginStatus`。
4. 通过鉴权后的 `GET /api/origins` 可以列出绑定 ID。若恰好只有一条来源，可以在命令行使用 `-UseLatestOrigin`，无需手工复制 ID。

对话 URL 未创建（ChatGPT 新空白聊天）、临时对话、不在 ChatGPT 域名的网页不能作为来源。关闭标签页后重新打开同一 URL 仍可按对话 ID 找回；两个相同对话标签页同时存在时会拒绝发送，不能猜测。

## 运行一次并把反馈送回原对话

```powershell
# 只执行验收，暂不发送（即使无绑定也有 JSON 报告与 TXT 待投递）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-yanzi-quality-loop.ps1 -TaskId yanzi-ui -Round 1

# 已绑定唯一原对话后发送
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-yanzi-quality-loop.ps1 -TaskId yanzi-ui -Round 2 -UseLatestOrigin

# 多条来源时必须指定确切绑定（不允许猜）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-yanzi-quality-loop.ps1 -TaskId yanzi-ui -Round 3 -OriginId <YOUR-ORIGIN-UUID>
```

这会运行发布版本的截图构建、WPF 测试构建、ChatGPT bridge 回归、隐藏桌面共享 UI + OCR 悬浮窗验收、无屏 OCR 测试。生成：
- `<task>-r<round>-report.json`：Git HEAD、脏工作区文件数量、通过/失败/日志位置、未覆盖的物理 E2E 项；
- `<task>-r<round>-feedback.txt`：完整等待投递消息；
- 有来源绑定时 `<task>-r<round>-delivery.json`：回传 job ID、终态、原对话身份核对结果。

默认**不会自动发布或提交到 git**。对被认证用户仍需启用自身工具权限的上下文才能执行后续开发；发送提示词给 ChatGPT 网页不保证 ChatGPT 一定会自主完成代码操作。

## 自动监听变更、进入下一轮

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/watch-yanzi-quality-loop.ps1 -OriginId <YOUR-ORIGIN-UUID> -TaskId yanzi-ui -MaxRounds 5 -PollSeconds 20 -SettleSeconds 45 -RunImmediately
```

监听只在后台读磁盘（不触发鼠标和键盘输入），仅在代码发生变化并稳定一段时间后执行下一轮测试；最大轮次限制防止无限循环。无需监听时按 Ctrl+C 退出。也可由燕子现有的闲置任务或调度模块显式启动，不需要另建长期系统服务。

## 本地 HTTP API

`chatgpt-bridge` 继续使用 127.0.0.1:53921、Bearer 本地 token、变更请求头 `X-Bridge-Request: 1`。

- `GET /api/origins`：已绑定的真实来源。
- `POST /api/feedback`：`{ "originId":"UUID", "deliveryKey":"task-123:r2", "text":"报告内容" }`。同一 `deliveryKey` / 原文再次提交返回已有任务，不会重复发出。同键不同文返回错误。响应包含 jobId，在 `GET /api/jobs/<jobId>` 核对成功与否。
- 该流程使用内部 `chatgpt_feedback_send` 动作，具有 `expectedUrl` 和 `expectedConversationId`；扩展在定位标签页、进入输入框和点击发送前多次核对，若页面被关闭、跳转、存在草稿、正在生成、或多标签页目标歧义，**不发送**。
- 任务结果为超时或 `interrupted` 时不自动重发，避免网页已收到文字后出现重复提交。保留本机出站报告和 job 状态以便人工核对。

## 安装与部署（安全门禁）

源码安装路径：
- `tools/chatgpt-bridge/server.mjs` + `origin-routing.mjs`；`tools/chatgpt-bridge/install.ps1` 已包含新模块；
- `browser-extension/chatgpt-background.js` + `chatgpt-content.js` + `manifest.json` (v0.5.50)。

使用 `scripts/deploy-chatgpt-origin-routing.ps1` 安全部署。它会检查工作台连接/任务数，**只在没有运行中和排队任务时**备份旧版服务与 `state.json`、重启 bridge 并加载更新的 Edge 扩展。若有任务，输出 `DEPLOY_DEFERRED_ACTIVE_JOBS` 并原样退出，不打断工作。

此版本主要依赖实际打开并已保存的**网页正式 ChatGPT 对话**。从 ChatGPT 原生 Android 应用开始的对话无法直接获得网页标签页来源，需要先由用户在网页版打开相同对话并绑定，或未来有明确的客户端对话标识传给该工具。

## 验证与剩余缺口

- 单元/集成：ChatGPT 源路由、来源严格绑定、排歧义、回传入队、去重、模拟扩展 ACK/结果均已测试；桥接测试 46/46 通过（初次），后来新增内容身份负例需继续纳入全套。
- 质量控制器不带来源时已真机运行并生成报告，且不碰用户当前桌面；原本的隐藏 UI 和 OCR 独立测试仍适用。
- **最终生产活体验证**仍需等浏览器扩展与本机 bridge 都升级且来源成功绑定。务必区分“模拟 websocket 通过”和“真实 ChatGPT 页面完成回传”。
- **真正无人值守持续开发**还需 ChatGPT 会话保持相关插件/工作台的操作权限，以及更细的任务质量评价、Git worktree 隔离、CI 发布门禁、错误跟踪和回滚系统。本阶段没有声称其已完工。
