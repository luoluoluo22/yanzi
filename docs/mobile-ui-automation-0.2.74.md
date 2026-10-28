# 燕子 Android 0.2.74：远程 UI 自动化执行引擎

## 目标与权限

0.2.73 已在 K70 真机完成公共文件列目录、96 字节文件新建与 SHA-256 读回、原有文件覆盖拒绝及 JPG 预览验收。

0.2.74 扩充 **定位控件 → 执行点击/输入 → 等待 → 验证 → 任务状态/取消**。保留 0.2.73 的全部文件、照片和 0.2.72 屏幕能力。依赖 Android AccessibilityService，不需要 Root 或 Shizuku，不开放任意 Shell。

默认关闭「燕子 → 我的 → 远程界面自动化」。用户需要先启用「远程屏幕控制」和 Android 系统无障碍，然后单独确认「远程界面自动化」。关掉开关后，执行中的任务在下一次安全检查时停止；手机锁屏时拒绝操作。任务设计只作用于任务开始时的前台应用，任何途中跳转到其他应用都会拒绝继续，防止误操作。

## 接口

所有命令通过原有 `capability.invoke`，以 `payload.name` 指明能力。需账号持有人授权，委托设备必须逐项配置 `capability.invoke:mobile.ui.*` 对应 scope。

| 命令 | 请求 arguments | 响应 |
| --- | --- | --- |
| `mobile.ui.status` | `{}` | `screenControlEnabled, accessibilityConnected, automationEnabledByUser, ready, deviceLocked` |
| `mobile.ui.find` | `{selector,packageName?}` | `found, matches, packageName, resourceId, text, description, bounds, ...` |
| `mobile.ui.click` | `{selector,packageName?,allowFirst?}` | `clicked,packageName` |
| `mobile.ui.type` | `{selector,value,packageName?,allowFirst?}` | `typed,characters,packageName`（不会回传所输入的文字） |
| `mobile.ui.wait` | `{selector,expectFound?,timeoutMs?,packageName?}` | `matched,found,packageName` |
| `mobile.ui.verify` | `{selector,expectFound?,packageName?}` | `verified,found,matches,packageName`；条件不符合则失败 |
| `mobile.ui.task.start` | `{steps,packageName?}` | `taskId,state=running,totalSteps,completedSteps`；**立即返回**，后台逐步执行 |
| `mobile.ui.task.status` | `{taskId?}` | `running/completed/failed/cancelled/interrupted,completedSteps,reason?` |
| `mobile.ui.task.cancel` | `{taskId?}` | `cancellationRequested:true`；中断后续步骤 |

`selector` 至少提供一个非空字段：`resourceId`、`text`、`description`、`className`。可选 `contains`（默认精确匹配）、`editableOnly`、`clickableOnly`。非唯一目标的点击/输入默认拒绝，可明确设置 `allowFirst:true`。

限制：单次文本最多 500 字符，节点搜索最多 500 个、20 层，单次等待最长 12 秒，单个任务最多 12 步、85 秒，同时只运行 1 个任务。输入依赖控件提供 `ACTION_SET_TEXT`，**不向系统标记为密码框的控件写入文字**；不支持通过剪贴板绕过输入限制，也不支持盲点坐标自动化。对不支持无障碍控件操作的第三方应用将返回明确错误。

## 推荐验收：文件管理器搜索

0.2.74 安装且获得新授权后：

1. 调用 `mobile.ui.status` 验证 `ready:true`；缺少系统无障碍、远控开关或界面自动化开关时提示用户分别开启。
2. 使用已具备的 `mobile.apps.open` 打开「文件管理」（`com.android.fileexplorer`）。
3. 读取已有 `mobile.screen.tree` 确认搜索按钮的实际 `resourceId` 或 `description`，**不要猜控件标识**。
4. 点击搜索 → 等待编辑框出现 → 设置测试关键词（建议在 Download 新建的 `yanzi-remote-readwrite-test-20261017-1744.txt`）→ 等待匹配结果 → 验证。
5. 用 `mobile.ui.task.status` 检查异步完成数；另做等待阶段的 `mobile.ui.task.cancel` 测试，确认已取消的任务不再执行后续点击或输入。
6. 从屏幕再次验证结果；操作失败不声称通过，记录包名、步骤编号与安全错误代码。严禁自动发送消息、付款或修改已有文件。

测试脚本：`scripts/yanzi-phone-ui.ps1`。请传入明确的 `-SourceDeviceId`、`-TargetDeviceId`；`-Action status` 会输出权限缺失指引。多步骤任务使用 `-Action task-start -StepsFile <UTF-8 JSON 数组路径>`，再用 `-Action task-status` 查看结果。

一个**示意**任务（实际控件选择器需根据 K70 文件管理器的真机控件树调整）：

```json
[
  {"action":"click","selector":{"description":"搜索"}},
  {"action":"wait","selector":{"className":"android.widget.EditText"},"timeoutMs":5000},
  {"action":"type","selector":{"className":"android.widget.EditText"},"value":"yanzi-remote-readwrite-test-20261017-1744.txt"},
  {"action":"wait","selector":{"text":"yanzi-remote-readwrite-test-20261017-1744.txt","contains":true},"timeoutMs":9000},
  {"action":"verify","selector":{"text":"yanzi-remote-readwrite-test-20261017-1744.txt","contains":true}}
]
```

## 设计与现有限制

- 任务状态仅保存任务 ID、当前状态、步骤数量、包名和机器错误码，不持久化输入文本或屏幕内容。敏感 UI 能力的 ACK 结果在手机端回执确认后清除。
- 云端执行消息仍含任务定义及输入文字，并受原有消息保留策略约束；**不要用此通道处理密码、一次性验证码、支付信息或其他秘密**。后续应增加隐私级任务附件/短 TTL。
- 新任务只在当前进程中执行；进程异常退出后状态标为 `interrupted`，不会盲目重播有副作用的动作。
- 取消采用协作式取消；已发出的单次 Android 控件动作无法撤销，后续步骤在取消标记检查后停止。
- 此版先实现可靠的单应用自动化，不包含多 App 跳转的自主流程、任意后台执行、实时屏幕视频或远程 Root/ADB。
