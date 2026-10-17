# Android 0.2.75 红米 K70 自动化任务最终真机 QA

日期：2026-10-17。设备：Redmi K70 / Android product vermeer，USB ADB 序列号 e3e68545，应用版本 0.2.75（versionCode=75）。使用已授权燕子云端 `capability.invoke` 完成任务执行与状态检查；ADB 仅用于前台确认、UI 树独立核验。

## 最终结论

**本次历史遗留的两项 P0 验收均通过**：

1. 四步异步任务 `mobile.ui.task.start` → `mobile.ui.task.status`：任务 ID `9fcf0936-46f1-41fb-9262-2f1fae34ee8f`，前台包 `com.android.fileexplorer`，最终 `state=completed`、`completedSteps=4`、`totalSteps=4`。步骤为找到 `android:id/input`、输入 `ADBKeyboard`、等待搜索结果标题、验证标题。
2. 异步任务协作式取消：第一轮因工具交互延迟，等待步骤先达到 `ui_wait_timeout`，不能算取消成功。调整为同一测试进程连续发起 start/cancel/status 后，任务 ID `1961b9cd-34bd-4f2e-9cf8-d9ed251ce741`，取消命令回应 `cancellationRequested=true`，最终 `state=cancelled`、`completedSteps=0`、`totalSteps=3`、`reason=ui_task_cancelled`。
3. 独立副作用检查：ADB `uiautomator dump` 确认搜索框仍为 `ADBKeyboard`、结果项为 `ADBKeyboard.apk`，没有被取消任务中后续步骤预设的 `YANZI_CANCEL_MUST_NOT_TYPE` 替换。这证明取消不仅更新状态，还阻止了后续输入。
4. 前台保护：在此前用户使用货拉拉、微信视频通话时，云端任务返回 `foreground_package_mismatch` 或不可用窗口错误，没有跨应用执行。用户明确表示通话结束后，先独立验证手机在系统桌面，再继续文件管理器验收。

## 本次验证范围与限制

- 本次证明：Android 0.2.75 在 K70 上真实执行跨多步的元素查找、文字输入、等待、验证、状态查询及协作式取消。所有操作都限定文件管理器内，无删除、支付、外发消息、修改用户现有文件。
- 它不证明：所有第三方 App 的 Accessibility 行为一致；设备离线/锁屏仍可靠执行；任务因进程重启后自动恢复；取消已经开始的单次 Android 系统操作可被撤销。取消仅能阻止后续步骤。
- `ADBKeyboard` 搜索结果在验收时为一项；之前记录的 20 项是另一个时间点的索引结果，不应硬编码数量。
- 版本 0.2.75 此前已发布 GitHub release `android-v0.2.75` 及云端升级 APK，安装文件公开下载校验 SHA-256 为 `67c1cd87b00fe57203547f7e01706ee1505ee10a9d8a0c91fe11c8165261d8c7`。本轮 QA 不修改手机 APK。
- 测试用任务定义保存在工作区 `.artifacts/ui-remote/search-task.json` 和 `.artifacts/ui-remote/cancel-task.json`，它们是局部 QA 数据，不包含用户文件内容或密码。

## 后续建议

将“启动后数秒内取消任务”加入自动化回归测试。执行时应从一次连续工具调用完成 start/cancel/status，避免消息往返间隔使等待步骤先超时。另在交付流程中分别列示「APK 构建成功」「真机单步成功」「真机四步完成」「真机取消成功」，杜绝把其中一项的通过当作整体通过。
