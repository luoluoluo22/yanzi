# 燕子 Ego Agent 团队（MVP）

统一入口：燕子小程序「Agent 团队」（WebView），本机控制台 http://127.0.0.1:43830 。

架构：

- 燕子 `ego-agent-team-service` 保存任务、主管、子 Agent、消息 ID、投递状态及逐步日志；数据保存在 `%LOCALAPPDATA%/OpenQuickHost/ExtensionStorage/ego-agent-team/runs.json`。
- 页面独立 `ego-agent-team-workbench`；只通过本机 HTTP 接口管理，后台拥有 Ego 的本机令牌，不将令牌返回给网页。
- 主管使用 Ego Browser API 打开真实 ChatGPT 临时对话并返回 `YANZI_SPAWN_CHILD:` 指令，服务依据实际回复创建 1～3 个独立子 Agent。
- 子 Agent 输出先写入本地消息邮箱，然后由燕子发送到主管原会话；只有主管实际回复才确认消息 delivered。主管完整总结需要等待生成停止、内容稳定采样；可在工作台点击「核验主管完整回复」，该操作只读取原对话，不重新发消息。
- 网页发送必须验证输入框被清空；对可能已经发送的提示词，系统绝不自动重发。只有“打开全新空白会话”因导航超时可以有限重试。
- 进程重启中断的运行标记为 `needs_review`，不会猜测上次执行结果，也不会盲目继续发送。

## 验证

`node --check server.mjs`
`node --test tests/contract.test.mjs`

需要家里电脑启动「Ego 浏览器 API 服务」以及本项目的「燕子 Agent 团队服务」。用户从「Agent 团队」小程序发起新任务，网页每五秒刷新任务状态。页面仅为本机使用，未做远程公网暴露。

## 目前限制

当前首轮验证采用 ChatGPT 临时对话，关闭会话后无法作为长期可恢复的主管会话。本机消息日志可持久保存；后续可以扩展到保存的 ChatGPT 会话与任务恢复确认机制。当前服务单队列执行，避免多个任务在 Ego 共用执行队列中争抢。暂不自动重试失败的子任务发送。
