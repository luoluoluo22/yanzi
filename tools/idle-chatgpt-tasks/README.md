# 闲置任务

后台常驻的任务看板。燕子判断电脑进入闲置后，按队列执行任务。

## 当前能力

- 完整任务看板：新建、查看、编辑、删除、搜索。
- 状态分类：未执行、执行中、已完成、异常。
- 类型分类：ChatGPT、小程序。
- 自定义标签。
- 每个任务都有“重复执行”开关。
  - 关闭：成功后进入“已完成”。
  - 开启：成功后自动重新进入“未执行”，排到队尾，等待下一次闲置。
  - 失败不会自动循环，避免错误任务持续消耗资源。
- ChatGPT 任务通过现有网页工作台 Bridge 执行。
- 小程序任务通过燕子本地 Agent API `POST /v1/extensions/{id}/run` 执行，可附带 input。
- 顶部显示真实闲置倒计时；任务卡片显示最早预计执行时间。
- 开机后随 Runtime 后台常驻，但不主动显示窗口。
- 常驻实例注册进 `RunningExtensionRegistry`，会出现在系统托盘的“正在运行的小程序”中。
- 手动启动“闲置任务”只呼出已有看板，不创建第二个实例。

## 闲置判定

默认配置：

```json
{
  "startup": {
    "mode": "on_app_launch",
    "idle": {
      "enabled": true,
      "afterMinutes": 5,
      "repeatMinutes": 10,
      "pauseWhenFullscreen": true
    }
  }
}
```

闲置 = 鼠标和键盘持续无输入，并且当前没有全屏前台窗口。

## 数据

任务数据：

`%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\idle-chatgpt-tasks\tasks.json`

闲置实时状态：

`%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\idle-chatgpt-tasks\idle-trigger-status.json`

## ChatGPT 中断/超时自动恢复（v0.2.1）

- 每 10 分钟核对一次正在执行的 ChatGPT Job；正在正常执行时保留同一个 Job ID，不以已运行 15 分钟作为唯一失败证据。
- 当 Bridge 明确返回 `interrupted` 或 `timeout` 时，旧 Job ID 保留并延迟 2 / 5 / 15 分钟重新派发，单次执行连续失败最多自动补发 3 次。
- 每次补发前先查询上一 Job：成功则直接结算；仍在执行则继续等待；无法连接则延后 5 分钟，避免重复派发。
- 重试请求包含原任务提示词及“先检查已有成果、从未完成步骤继续”的恢复指令，避免覆盖旧修改。
- `tasks.json` 新增 `RetryCount`、`NextRetryAt`、`PreviousBridgeJobId`，成功后归零；达到上限保留失败状态供人工排查。
- Bridge POST 反馈丢失时使用与实际发送完全相同的恢复提示词核对历史任务，而不是盲目重发。
- 验证工程：`.artifacts/idle-retry-test/IdleRetryVerification.csproj`，用于隔离状态机回归测试。


## v0.2.2：业务只读能力与观察窗口

- 新增 idle.tasks.list：可由现有燕子能力网络发现并调用，返回任务状态、运行次数、简短反馈、minimumRepeatMinutes 和 nextEligibleAt；不返回完整提示词，也不主动写入任务数据。
- 保持 v0.2.1 的 ChatGPT Job 状态核对、中断/超时自动续跑、单次最多 3 次补发和失败回退策略。
- MinimumRepeatMinutes 控制重复任务成功后下一次最早允许执行的时间间隔；NextEligibleAt 控制已有待执行任务最早可选中时间。未到时调度器直接跳过，不发起无价值的 ChatGPT 会话。
- 主动点击“重新排队”会清除 NextEligibleAt，允许用户手动干预；任务禁用时不再自动派发。
- 临时 Provider 的真实 Agent API 回归测试脚本：scripts/test-idle-task-list-capability.ps1；隔离状态机测试：.artifacts/idle-retry-test。
- 本机正式发布与回滚信息：docs/continuous-improvement/integrated-release-v1.0.12-2026-10-09.md。
