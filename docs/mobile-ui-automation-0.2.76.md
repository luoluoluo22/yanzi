# 燕子 Android 0.2.76 · 安全跨应用任务、只读重试与任务历史

日期：2026-10-17。基于 0.2.75 的真实云端任务引擎扩展，不需要 Root，不要求 USB ADB 才能运行。仍需用户在手机开启 Android 无障碍、燕子「远程屏幕控制」及「远程界面自动化」，手机必须解锁。Android 0.2.76 安装后沿用这些授权。

## 功能和接口

沿用 `mobile.ui.task.start/status/cancel`，新增 `mobile.ui.task.history`（可选 `limit:1..20`）。任务状态增加 `allowedPackageCount`、`currentStep`、`currentAction`、`attempt` 和 `retryCount` 等机器可读字段；任务历史最多保存 20 条终态记录，不包含选择器、输入文字或界面内容。请勿将云端请求本身当作机密传输渠道，用户输入内容仍经现有命令链路传输。

### 显式允许的跨应用任务

任务请求在原有 `steps` 基础上增加 `allowedPackages`，总数不超过 4 个；列表必须包含启动时的前台应用，且各包名均应是可启动应用。若没有 `allowedPackages`，任务只能在开始时的前台应用里工作。新步骤：

```json
{
  "action": "openApp",
  "packageName": "com.android.settings"
}
```

下一步可以是查找、等待或验证，默认目标包名跟随上一步明确打开的应用。每一步都有当前前台包名校验，拒绝跨越没有在启动前声明的应用。系统桌面是用户可见的中转界面，不被当成对任意应用的授权。

**小米/Android 后台启动限制**：优先尝试 Android 正常启动方式；如果前台仍停留在原应用，调用经授权的无障碍 HOME 全局操作，再在可见桌面中按真实应用名称**精确匹配**图标并点击。如果该图标不在当前桌面页面，则返回 `app_shortcut_not_visible_on_home` / 控件不存在的明确错误，不会猜测位置、乱点其他图标、越过屏幕锁定或操作未批准的应用。

### 只读失败重试

`find/wait/verify` 可使用 `maxAttempts:1..3` 和 `retryDelayMs:150..1500`。只重试 `ui_target_not_found`、`ui_wait_timeout`、`ui_verification_failed`、`active_window_unavailable` 等可恢复的读取异常；**不重试** `openApp`、`click`、`type`，不重试前台应用错配或权限错误。总任务沿用最多 12 步、85 秒、同一时刻一个任务、协作式取消，进程重启不重复有副作用的任务。

## K70 真机验收证据

测试设备：Redmi K70，ADB serial `e3e68545`；使用燕子云端账号持有者会话发送所有真正的 `mobile.ui.task.*` 指令，ADB 仅安装 APK、选择开始前台和独立核查屏幕。实际测试没有修改用户文件，没有发送消息或操作付款。

| 验收 | 具体证据 | 结果 |
| --- | --- | --- |
| 安装 0.2.76 | Android 实际返回 `versionCode=76, versionName=0.2.76` | 通过 |
| 跨应用显式白名单 | `ecb234e1-5ed8-4654-830a-996f3edd696c`：文件管理器 → 系统设置 → 验证；`completedSteps=2,totalSteps=2,state=completed` | 通过 |
| 跨应用目标不可见安全失败 | 4 步任务 `635626f6-b33a-4acf-ad09-f6b14762ca5d`：前两步成功切换到设置并验证，返回文件管理器时因当前桌面页找不到图标而在第三步正确失败，未盲点 | 通过限制行为 |
| 缺少显式跨应用允许列表 | 请求仅给出 `openApp`，没有 `allowedPackages`，返回 `explicit_app_allowlist_required` | 通过 |
| 只读重试 | `fee709fe-57ca-4e7a-83ca-7e916512878d`：不存在的控件，`state=failed,attempt=3,retryCount=2,completedSteps=0,reason=ui_target_not_found` | 通过 |
| 任务历史 | `mobile.ui.task.history` 返回上述任务记录，可按 taskId 检索，最多 20 条 | 通过 |
| 四步原有功能回归 | `20a8d5d5-847b-48f2-a5a9-9d6ed974edc9`：`completedSteps=4,state=completed` | 通过 |
| 协作式取消回归 | `418a5de2-9808-4230-805f-fa8f71115cc5`：`state=cancelled,completedSteps=0,reason=ui_task_cancelled` | 通过 |

### 复现工具

桌面端脚本 `scripts/yanzi-phone-ui.ps1` 支持 `-Action task-start -StepsFile <JSON文件> -AllowedPackages '包名1','包名2'` 和 `-Action task-history -HistoryLimit 10`。所有执行请求都使用已登录的燕子主账号会话；不在代码中保存凭证。

示例 JSON 文件保留在 `scripts/fixtures/mobile-ui-076/` 下，需先手动保证目标前台应用与例子相符。不要把 `ANDROID_SETTINGS` 等占位符当包名：实际例子使用 `com.android.settings`。

## 当前范围与下一阶段

- **已实现**：用户主动触发的有限跨应用任务、只读阶段有界重试、隐私最小化的最近任务记录。
- **尚未实现**：自动翻桌面页/应用抽屉搜索图标、在锁屏时执行、跨设备共享任务状态、通知/文件变化等后台事件触发并自动唤醒、自动操作支付/聊天发送等危险流程。
- 建议下一阶段新增可审计的本地事件订阅接口与任务白名单，并为真正需要长期后台执行的任务设计明确的 Android 服务生命周期；事件触发后仍要经过原有单次任务权限、安全前台校验和失败限制，不能让后台事件绕过用户授权。
