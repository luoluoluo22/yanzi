# PR #180 · ScheduleConfigWindow — Windows 真机验收

- 验收设备：家里 Windows `PC-202409131508`，Raccoon_Connection 实连，2026-11-05（北京时间 UTC+08:00）。
- 修复分支：`feat/ui-schedule-qa-20261105`。基准提交 `f8fc5b0f2cf75902b9062bedd85b69e01230060c`；新增主题/键盘/无障碍修复 `a29835dd41a87b2864001e8d2f1171e8ee494eb9`；QA 证据提交 `3aa27530e679537c42e03aed40765aa9313189d4`。
- 独立工作树：`F:\Desktop\kaifa\yanzi-ui-schedule-visual-qa-20261105`，未修改正式安装的燕子用户配置。

## 深浅主题与 Windows UI

- Release 原生编译通过；`dotnet run --project tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj -c Release --no-build --no-restore`：原17项 + 新增3项，`RESULT pass=20 fail=0`，退出码0。
- 使用 PR #181 的真实 Windows QA 脚本，对 SHA `a29835dd41a87b2864001e8d2f1171e8ee494eb9` 及完整证据 SHA `3aa27530e679537c42e03aed40765aa9313189d4` 各执行一次；`host-build` / `test-restore` / `wpf-smoke` 均退出码0，`PASS=20 FAIL=0`。
- 实际 WPF 双主题截图：`schedule-pr180-light-verified-20261105.png`、`schedule-pr180-dark-verified-20261105.png`。发现并修复浅色主体被深色覆盖、Cron 输入框黑底问题；包括底部按钮、频率选择、预览样式在内的主体配色随主题切换。
- 向真实 Windows WPF 窗口发送 `Tab`、`Space`、`Escape`，现场日志记录 `PHYSICAL_KEY=Tab`、`PHYSICAL_KEY=Space focus=ModeCronBtn`、`PHYSICAL_KEY=Escape`。Space 切换到 Cron 面板，Esc 关闭窗口。
- UI Automation 自动化断言：61个可交互选择项具有 Focusable、Button 类型、非空 Name 和 InvokePattern；确定/取消默认 Enter/Esc 按钮。新增每个模式/频率/日期控件的 `AutomationProperties.ItemStatus`（已选中/未选中），并新增 3 组状态断言；21:51 再次全量 Release 编译通过，20/20 通过，退出码0。
- 系统 UIA 焦点观察：`schedule-pr180-narrator-uia-focus-20261105.txt` 包含`启用定时运行` → CheckBox、`简单设置`及`Cron 表达式` → Button、`Cron 表达式`输入框 → Edit 的真实操作系统控制树。Windows Narrator 于21:42:56成功启动过，但缺少可信语音转录，因此**并未验证实际朗读内容**。

## 真实业务调度 E2E：通过

- 使用仓库已有 `HostAssets.UseIsolatedDataRootForVerification`，以 `--dev --schedule-isolated-e2e` 运行原生 MainWindow 和真实 WPF ScheduleConfigWindow。该模式关闭生产全局监听，仅为测试明确启用独立 ExtensionSchedulerService DispatcherTimer；没有启动正式用户后台服务。
- 写入一次性 PowerShell 小程序清单，经原生弹窗确认设定 `41 21 * * *`；真实 `MainWindow.UpdateExtensionStartupFromSettingsAsync` 保存，并从磁盘 `manifest.json` 回读一致。再次打开并取消设置后，原配置仍在磁盘上。
- `ExtensionSchedulerService` 计算出本地 `2026-11-05 21:41:00`，真实 DispatcherTimer 在21:41:00.325调用，宿主日志确认 `launchSource=scheduler`、PowerShell 进程启动及成功结束。
- 合成 PowerShell 小程序于北京时间 **21:41:02.2577004** 实际写入标记文件，实测只运行1次；脚本返回 `ISOLATED_SCHEDULE_PROBE_EXECUTED`，退出码0。测试随后调用真实设置 API 关闭定时，确认从 scheduler NextRunsSnapshot 与 manifest 同时消失，E2E进程退出码0。
- 证据：`schedule-pr180-e2e-result-20261105.json`、`schedule-pr180-e2e-host-log-20261105.txt`、`tests/WpfUiMigrationSmoke/ScheduleSchedulerE2E.cs`（可重复的独立真机测试）。与直接执行脚本不同，本次确实等待整分钟后由调度服务发起。
- 隔离环境一次性 ID `qa-schedule-isolated-98a74c6a042f4e29b4df4acb9bf57a0b`，根目录在系统 TEMP 下。不涉及正式账户的现有定时任务，也不与其他真实设备共享凭证。

## 仍然没有完成、不能冒充通过

1. 外部云端/跨客户端同步和网络异常后的回滚：本次已通过本机真实调度和持久化，但没有对正式账号云服务、双设备同步进行破坏性配置测试。
2. NVDA/Narrator **实际朗读文本**：可访问控件类型、名字与焦点导航在真实 Windows UIA 下有证据；未有语音记录或人工读屏确认。选项被选中状态如何被朗读仍需独立确认。

说明：上述已通过项目的证据均来自 Windows 原生进程和实际文件写入；不以 Linux XML 检查、抽象单元测试或设计稿代替真机。
