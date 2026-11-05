# Windows 真机验收门禁（强制）

UI 迁移不得以 Linux XAML/XML/JavaScript 静态检查或 GitHub Actions 成功代替实际设备验证。每轮优先连接家里电脑 PC-202409131508，离线时转到已连接的备用 Windows 设备。始终在独立、干净的 Git worktree 中操作，禁止覆盖家里的主开发目录或 Ubuntu 旧服务工作树。

Windows PowerShell 5.1+ 运行：

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\qa-ui-windows.ps1 -RepoPath <绝对路径到独立worktree> -ExpectedSha <本轮Git提交40位SHA> -MinimumPassingTests 15

该脚本核对 Git SHA 和工作区状态，使用真实 Windows .NET WPF 宿主 Release 编译、NuGet 恢复并运行 tests/WpfUiMigrationSmoke 的 STA/ShowDialog 测试；任何非零退出码或未满足测试数都失败。结果写入系统 TEMP 目录的 windows-ui-qa.json 和各步骤日志。运行前必须保证实际受影响窗口已覆盖在冒烟测试项目中；未覆盖的窗口不得借其他窗口通过来标记验收。新窗口应新增功能测试，提升本次 MinimumPassingTests 或增加对应断言。

**该脚本仅代表 WPF 运行冒烟；不代表 UI 全面验收。** 仍需在目标设备核查深浅主题截图、键盘焦点/快捷键、屏幕阅读器与无障碍播报、真实服务与业务数据。浏览器扩展需在 Edge/Chrome 安装运行验证，Android 界面须在手机实测。若这些步骤缺失，清单标记为待功能/视觉验收，不能改成已完成。

每次 PR 合并前，在对应的 inventory.json 项目 evidence 中记录设备名、SHA、具体测试命令、通过/失败数量、截图位置及未覆盖的操作。Windows 设备不可达时记录离线原因，并尝试备用设备；不要省略真机测试后只重复静态扫描和报告。GitHub 推送认证已可在 Ubuntu 使用 HOME=/root 加上已配置的 Git credential helper，不要把 Token 写入代码仓库。
