# 燕子 Android 0.2.75：远程 UI 自动化窗口稳定性修复及 QA

## 为什么需要 0.2.75

2026-10-17，用户把 Redmi K70（ADB 序列号 e3e68545）通过 USB 连接至台式电脑。0.2.74 已经用原签名 APK 通过 ADB 安装成功。手机通过「我的 → 远程界面自动化」弹窗确认授权后，`mobile.ui.status` 云端返回全部权限开启、`ready=true`。

实际测试记录：
- 0.2.74 真机云端 `mobile.ui.find` 按文件管理器控件 ID 找到了“最近”标题，按中文无障碍描述找到“搜索”按钮；`mobile.ui.click` 返回 `clicked=true`。
- 远程 `mobile.ui.type` 通过 `ACTION_SET_TEXT` 成功输入 27 字符和 11 字符；对本机已经存在的 `ADBKeyboard` 查询，系统文件管理器显示 **20 项相关结果**。云端 `mobile.ui.verify` 返回 `verified=true`。
- 通过 0.2.73 新建的测试文件没有被 MIUI 文件管理器搜索索引命中，改用已被索引的文件名后通过；这是搜索内容/索引差异，不能说输入失败。
- 桌面 PowerShell 5 工具原先把中文 JSON 发送为旧编码，修正为 UTF-8 字节后中文选择器通过。另有顶层 JSON 数组被 PowerShell 5 包装为 `{"value":[...],"Count":4}` 的序列化错误，已改为验证后嵌入原始 JSON 数组。修复后手机已经读到了合法 `steps`，进入实际前台窗口约束检查，而不再因参数格式拒绝。
- 用户操作期间，前台先后变为货拉拉位置选择、后变为微信视频通话。任务返回 `foreground_package_mismatch`，阻止在错误应用中执行；另复现 `active_window_unavailable`。在微信通话期间**不应强行切换应用**。
- 0.2.75 增加 `typeWindowsChanged`、`typeWindowContentChanged`、`typeViewFocused` 事件及 `flagRetrieveInteractiveWindows`，当 `getRootInActiveWindow()` 返回空时，只从 **获得焦点或活动状态的应用窗口** 回退获取节点，不读取任意后台窗口。
- 0.2.75 已进行 Java Release 编译、打包与 ADB 安装，并核对手机显示 versionCode=75，Android 无障碍服务仍处于启用状态。**由于用户正在微信视频通话，0.2.75 修复后的多步骤成功执行及取消仍待空闲时真机验收，不应声称已完成。**

## 未通过或未完成项目

| 验收项目 | 状态 |
| --- | --- |
| USB ADB 授权、APK 0.2.74/0.2.75 覆盖安装 | 通过 |
| 新「远程界面自动化」授权与云端 ready | 0.2.74 通过 |
| 文件管理器搜索按钮定位、点击 | 0.2.74 通过 |
| 搜索输入框文字设置 | 0.2.74 通过 |
| 现有文件搜索结果及 UI.verify | 0.2.74 通过（20 项） |
| 前台应用变化时拒绝继续操作 | 0.2.74 通过 |
| 云端 JSON 中文编码、steps 数组 | 客户端调用脚本修复，参数错误不再出现 |
| 0.2.75 编译与真机升级 | 通过 |
| 0.2.75 活动窗口回退稳定性 | 尚需真实空闲窗口验收 |
| `mobile.ui.task.start` → `task.status` 完成四步 | 尚未通过 |
| `mobile.ui.task.cancel` 阻止后续步骤 | 尚未执行 |

## 剩余真机操作

待用户结束正在进行的微信通话、允许前台短暂切到文件管理器时：

1. 启动文件管理器并进入「搜索」页面，确保当前 `com.android.fileexplorer`。
2. 使用 `scripts/yanzi-phone-ui.ps1` 和 `.artifacts/ui-remote/search-task.json` 发起四步只读/输入任务（控件查找 → 输入 `ADBKeyboard` → 等待出现含关键词的标题 → 验证）。
3. 取 `taskId` 后查询 `mobile.ui.task.status`，要求 `state=completed,completedSteps=4`；同时用 ADB UI 树复核文件管理器显示 20 项。
4. 另发一个等待不存在控件、10 秒超时的无副作用任务，取得任务 ID 立即 `task.cancel`，等待 `state=cancelled` 且后续步骤数不增加。
5. 测试前台变化引起安全失败，确认不会点击其他 App。完成后尝试恢复原应用；不要在用户的视频通话界面运行任何自动化或替用户结束通话。

所有真机测试需避免复制、发送用户聊天信息；严禁在用户正在进行电话、视频通话、支付时进行前台切换。
