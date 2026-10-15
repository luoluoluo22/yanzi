# 燕子闲置任务中断/超时自动续跑 v0.2.1（2026-10-09）

## 需求和根因
ChatGPT 浏览器工作台的任务可能因会话时间限制、浏览器扩展断开或后台服务重启进入 `interrupted`/`timeout`。旧版将这两种状态视为永久终态，即使启用了 RepeatEnabled 也不会重新执行，造成长期改进任务停止。

## 行为
1. 每 10 分钟复查占用队列的非终态 Job，不凭单纯 15 分钟时长认定失败，避免重复提交尚在运行的任务。
2. 确认为 `interrupted` / `timeout` 时，分别在 2、5、15 分钟冷却后安排下一次重派；单轮成功前最多 3 次自动重试。
3. 每次重派先查询原 Bridge Job ID：已成功就结算；仍在运行就继续监控；桥接不可用则顺延 5 分钟，不盲目提交。
4. 恢复提示词包括原任务目标及对现有代码、日志、记录的核对要求，防止重复执行或覆盖已完成改动。
5. 通过 `RetryCount`、`NextRetryAt`、`PreviousBridgeJobId` 持久化恢复状态，成功后清零。达到上限时保留终态以供排查。
6. POST 返回丢失时按照相同 `ComposePrompt` 查找 Job，而不是用旧提示词匹配，防止丢失后重复提交。
7. 整体仍受真实闲置条件限制：键鼠无输入至少 5 分钟、没有全屏前台窗口；之后每 10 分钟触发任务看板。

## 修改和验证
- 工作区：`tools/idle-chatgpt-tasks/IdleTaskApp.cs`、`manifest.json`、`README.md`。
- 正式安装目录：`%LOCALAPPDATA%\OpenQuickHost\Extensions\idle-chatgpt-tasks`。
- 更新版本：0.2.0 → 0.2.1，未修改公共 Runtime。
- 隔离 C# 编译与状态机测试：12/12 通过。
- 正式 Runtime 的动态 C# 编译：2026-10-09 15:53:54，success=True。
- 正式插件状态：0.2.1、isRunning=True、1 个实例；tasks.json 7 条保持原样。
- 2026-10-09 15:56:56 闲置心跳正常、idleSeconds=68、reason=waiting-idle，因此未绕过闲置门槛强制派发。旧失败任务会在正在运行的任务完成核对后按顺序处理。
- Bridge 实时 Job 核对：旧稳定性守护 Job=success；其余历史能力网络、同步可靠性、AI 质量分别为 interrupted、timeout、interrupted，符合重试队列的目标。

## 备份与回滚
- 正式源码、manifest 和用户 tasks.json 备份：`.artifacts/idle-retry-prod-backup-20261009-155251`。
- 回滚：先停止闲置任务小程序，将备份源码及 manifest 还原，再重新启动；除出现数据损坏外不覆盖 tasks.json。
- 生产环境保留已提交的 Job ID，不自动清理、覆写其他项目工作树，不推送仓库或公开发布。
