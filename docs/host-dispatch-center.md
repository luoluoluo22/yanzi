# 燕子宿主事件与调度中心（第一阶段）

## 目标及边界

统一管理小程序的闲时、Cron、系统事件与退出前保存，避免多个小程序自行轮询系统。
此次新增的是宿主能力、协议和诊断页面，不新建业务小程序；现有 `startup.idle`、
`startup.schedule` 与 `on_app_launch` 保持兼容。

## 已支持事件

| 事件 | 触发条件 | 小程序侧 |
| --- | --- | --- |
| `host.idle` | 已有闲时阈值 / 周期检查 | 保留旧 `startup.idle` 配置与 `HandleTrigger("idle")` |
| `host.schedule` | 已有 Cron 到期 | 保留旧 `startup.schedule` 与 `HandleTrigger("scheduler")` |
| `host.resume` | Windows 从休眠恢复 | 新 `events.subscribe` 明确订阅 |
| `host.unlock` | Windows 用户解锁 | 新 `events.subscribe` 明确订阅 |
| `network.online` | Windows 网络变为可用 | 新 `events.subscribe` 明确订阅 |
| `host.beforeExit` | 用户真正退出宿主且小程序正在运行 | 新 `events.beforeExit`，必须确认保存完成 |
| `host.recover` | 重启识别到中断任务且小程序明确允许恢复 | 新 `events.recoverInterrupted` |

注意：关闭窗口到托盘**不是**退出；Windows 强制关机/断电时不能保证交付
`host.beforeExit`。小程序必须平时定期保存持久检查点。宿主正常升级、卸载前清理、
宿主异常崩溃是不同生命周期阶段。

### manifest.json 可选声明

```json
{
  "events": {
    "subscribe": ["host.resume", "network.online"],
    "beforeExit": { "enabled": true, "timeoutMs": 1200 },
    "recoverInterrupted": false
  }
}
```

未声明时保留既有功能。普通事件会遵守小程序的后台运行声明：WebView 如果未设置 `app.runInBackground=true` 且当前没有运行实例，宿主不会为系统事件弹出前台窗口；这类等待原因可在调度中心查看。

只有启用且运行中的小程序在真正退出时收到保存通知，
不会为通知而临时启动已停止的小程序。宿主单个小程序最长允许 2 秒，
所有小程序合计最长 5 秒，任何小程序不得无限阻止退出。退出保存走独立优先通道，即使该小程序已有闲时/Cron 任务正在派发，仍能收到保存通知。

### 小程序退出应答

**C# 常驻小程序**：注册 `HostObjectRegistry` 的 `<extensionId>-window` 对象，
公开 `Task HandleTrigger(string source)`；当 source 为 `host.beforeExit`
时，await 持久保存并在保存完成后才让 Task 结束。保存失败应抛出异常。

**WebView 小程序**：显式设置
`window.yanzi.beforeExit = async () => { await saveCheckpoint(); };`，
由 WebView2 `Runtime.evaluate` 等待 Promise 完成；仅监听 DOM 事件但未返回
Promise 不算保存确认。没有处理函数、超时、异常均记为失败，不假报成功。
仅在小程序明确声明 `events.beforeExit.enabled` 时调用。

## 持久任务状态

默认保存至 `%LOCALAPPDATA%\OpenQuickHost\DispatchCenter\history.json`。
新调度入口限制 **全局最多 2 个同时派发**、同一小程序最多 1 个派发，
重复请求不创建第二份任务。内存状态和最后 500 条持久记录支持
`queued`、`running`、`dispatched`、`failed`、`cancelled`、
`interrupted`、`recovered`、`waiting`。

`dispatched` 表示**已完成宿主向小程序的事件交付**，而不是小程序异步
上传/备份的最终业务完成。业务结果须由小程序自己报告。

每次启动将上次遗留的 `queued` / `running` 标成 `interrupted`，
不无条件重新执行。仅 `events.recoverInterrupted=true` 且本机仍启用、
近期 24 小时内的安全白名单事件才会重新派发，避免重复发送和不可逆操作。
未声明恢复的任务保留供人工检查。

## UI 和验证

托盘菜单 **调度中心** 提供按小程序/事件/状态/原因搜索，显示最近派发结果、
闲时未触发的原因和 Cron 下一次运行时间。可右键复制诊断上下文，
页面只读，不会启动小程序。

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-host-dispatch-center.ps1`
使用独立临时数据目录，验证去重、持久化、失败回执、中断识别与显式恢复。
此测试不模拟真实 Windows 注销、休眠或 WebView JS，因此现场完整退出
保存仍属于正式安装/真机验收门禁。

## 下一阶段

扩展 CPU/网络/电源条件、任务优先级、执行配额、用户主动恢复与安全重试策略，
所有功能继续复用相同事件/调度架构，不再引入第二套小程序后台循环。
