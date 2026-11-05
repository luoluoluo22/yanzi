# PR #180 · ScheduleConfigWindow — Windows 真机专项验收

- 验收设备：PC-202409131508（家里 Windows，经 Raccoon_Connection 实连）
- 验收日期：2026-11-05，北京时间约 21:15–21:24（UTC+08:00）
- 基准提交：`f8fc5b0f2cf75902b9062bedd85b69e01230060c`（PR #180 旧 HEAD）
- 修复工作树：`F:\Desktop\kaifa\yanzi-ui-schedule-visual-qa-20261105`，隔离 feature 分支 `feat/ui-schedule-visual-accessibility-20261105`，未改脏主工作树；最终修复 SHA 以本分支 Git 历史为准
- 编译命令：`dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --no-restore -nologo -v quiet -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false`
- WPF 测试编译：`dotnet build tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj -c Release --no-restore -nologo -v quiet`
- STA 真窗口：`dotnet run --project tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj -c Release --no-build --no-restore`
- 验收记录：两项 Release 编译退出码均 0，WPF `RESULT pass=20 fail=0`，测试进程退出码 0。复用了原同 SHA 隔离 QA 工作树的已还原 NuGet/obj 缓存；第一次全新工作树并行编译阻塞后停止，使用 `-m:1` 重新完成全编译；编译存在非本轮新增的 nullable 警告。
- 自动化 UI 断言：原 17 项 + 新增 3 项：61 个鼠标选择控件具有键盘焦点与 UIA Button/Name/Invoke，默认 Enter/Esc 按钮，空间键切换模式/15 分钟生成 `*/15 * * * *`、深浅语义主题刷。
- 实际桌面键盘输入：通过 Raccoon 桌面动作向真正显示的 WPF 窗口发送 Tab、Space、Escape；窗口进程日志记录 `PHYSICAL_KEY=Tab`、`PHYSICAL_KEY=Space focus=ModeCronBtn`、`PHYSICAL_KEY=Escape`，Space 让 UI 切换至 Cron 选项页，Esc 后窗口关闭（进程退出码 0）。
- 真实渲染图（RenderTargetBitmap，来自窗口本身，不含其他桌面或隐私内容）：`schedule-pr180-light-verified-20261105.png`、`schedule-pr180-dark-verified-20261105.png`。视觉核对：窗口底色/文案/控件、选择项/按钮均随主题切换；首次实测发现浅色窗口整体仍为深色，修复后又发现 Cron 输入框黑底，二次修复为公共 `Yanzi.Input`。
- 行为边界：实际 `ScheduleConfigWindow` 的确认/取消、Cron 产生与弹窗返回值通过 STA 合成数据检验；源码中 SettingsWindow 保存后调用 `UpdateExtensionStartupFromSettingsAsync`，AddJsonExtensionWindow 保存结果回写 `StartupScheduleBox`。**尚未通过实际扩展运行、后台调度器计时触发、跨客户端同步/异常回滚的端到端测试**，这些必须保留待验收，不能将其误写为完整业务联动通过。
- 本轮未覆盖：真实屏幕阅读器朗读体验、整套外部定时运行服务的触发；UIA 自动化控件树/Invoke 已通过，但不能替代 NVDA/Narrator 读屏人工交互。

说明：全部验收使用 `App.IsVerificationHarness=true` 及合成业务数据，没有修改正式燕子用户定时任务；全部截图来自真实 Windows WPF，不是设计稿或 Linux XML 解析。
