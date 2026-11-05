# PR #180 / #181 — 家里 Windows 正式版覆盖安装验收（2026-11-05）

## 安装对象与来源

- 设备：`PC-202409131508`（家里 Windows，Raccoon_Connection 直连验收）。
- 验收时间：2026-11-05 北京时间 22:06–22:18（UTC+08:00）。
- Git 源码：已合并的 `origin/main`，完整 commit `6dc5f3efaa5f3242f8b5a29ae1dfde21f1a07d87`，包含 PR #180 ScheduleConfigWindow 与 PR #181 Windows QA gate。
- 独立干净 Git worktree：`F:\Desktop\kaifa\yanzi-formal-main-install-qa-20261105`。未 checkout、reset、clean、覆盖家里脏工作树 `F:\Desktop\kaifa\OpenQuickHost`，亦未操作远端旧服务目录。
- 程序版本：此前正式安装 `1.1.17.0`；本次将同一份 main 源码以 **本地 Release 打包参数** `-Version 1.1.18` 覆盖安装，最终 EXE/DLL `1.1.18.0`。没有修改仓库 csproj 版本号，**没有发布 GitHub Release v1.1.18**；这是从已合并主线构建的本地正式路径安装包，不等于线上公开发布。
- 现有 1.1.17 源自另一未合并功能分支，和本次 main 不同，因此采用较高安装版本以免同版本覆盖失效。那条功能分支与主线的功能差异未在本轮测试，不能暗示全部新功能被包含。

## 执行与结果

1. `git fetch origin main`；`git worktree add --detach F:\Desktop\kaifa\yanzi-formal-main-install-qa-20261105 6dc5f3ef...`。工作树干净，源码 SHA 与远端 main 一致。
2. 备份原 `%LOCALAPPDATA%\Yanzi\packages\Yanzi-1.1.17-full.nupkg` 到 `.artifacts\formal-install-qa\rollback-Yanzi-1.1.17-full.nupkg`，记录旧 DLL SHA-256（`6BA94A418821A8B69F10DAA7663F6C4BAAD4A4BD12DDE93095105F113167737E`）、原正式目录及扩展数量。
3. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-installer.ps1 -Version 1.1.18 -SkipBaselineDownload -KeepHistoricalPackages -ArtifactRoot F:\Desktop\kaifa\yanzi-formal-main-install-qa-20261105\.artifacts`：退出码 **0**；`verify-release-layout` 报 `RELEASE_PAYLOAD_OK version=1.1.18.0 files=368 MiB=189.63`；Velopack 1.2.0 生成 `Yanzi-win-Setup-1.1.18.exe`，大小90,684,350字节。Setup SHA-256 `E2CADAC5C0134632BEDC51C7988E39EBD540C4B37F054027CD10840E3D1D684C`。本地未代码签名，且该包没有对外发布。
4. `Start-Process -FilePath .artifacts\installer\Yanzi-win-Setup-1.1.18.exe -ArgumentList @('--silent') -PassThru -Wait`：退出码 **0**。没有删除 `%LOCALAPPDATA%\OpenQuickHost` 用户数据目录。
5. 正式安装目录为 `%LOCALAPPDATA%\Yanzi\current`。构建产物与实际已安装 `Yanzi.dll` SHA-256 都是 `F91F68D59758081DB418507CAD70A88629864FDAE7BCD50D3D5A7EDFB3E5A572`，逐字节指纹相同；`Yanzi.exe` 和 `Yanzi.dll` 文件版本均为 **1.1.18.0**。
6. 通过 **已安装路径** 的 `Yanzi.exe` 启动（不是工作树直接运行）。真实正式进程 `PID 14628`，22:12:57 启动，22:17:55 复核仍仅1个进程，`Responding=True`；`scripts/verify-production-install.ps1 -ExpectedVersion 1.1.18 -ExpectedDllSha256 F91F68...` 输出 `PRODUCTION_INSTALL_VERIFIED`，退出码 **0**。
7. 在真实 Windows 桌面执行双击 Ctrl，正式版 `燕子 · AI 对话` 窗口实际出现并可被独立前台激活；左侧历史会话、输入框、模型选择和底部工具区域均正常渲染。已在本机保存整窗截图至 `.artifacts\formal-install-qa\official-ai-window-live.png`（包含用户自己的历史会话，**不得提交公开仓库**）；本仓库仅包含从真实窗口截图裁切、无私人内容的标题栏：`docs/ui-unification/formal-install-ai-titlebar-home-20261105.png`。
8. `%LOCALAPPDATA%\OpenQuickHost\Extensions` 扩展目录数量安装前后均为 **94**；正式账户数据目录存在；回滚用 1.1.17 安装包保留。安装后日志出现部分既有扩展源码云端冲突和外部图标请求错误，不属于覆盖安装成功证明，亦未在本轮消除。

## 验收边界与待验证

- **本次已完成**：真实稳定通道静默覆盖安装、已安装二进制指纹和版本核对、唯一正式进程重启、真实 AI 对话 UI 加载、基本扩展目录不丢失。
- **未能独占桌面进一步点击设置**：前台租约提示由另一项“正式燕子1.1.18真实模型调用exec_command并验证临时文件”任务占用；未绕过它抢键盘/鼠标。因此未把正式版设置页和 ScheduleConfigWindow 弹窗的实际交互标为本轮通过。它们此前在独立 Windows STA WPF 与定时调度 E2E 中已有证据。
- **仍未覆盖**：Windows Narrator 真实朗读语音、正式账户跨设备云同步与异常回滚、替换前那条未合并功能分支的特性兼容性；不将安装通过等同于所有业务完整通过。

本地备份及完整 GUI 截图仅留在 `.artifacts\formal-install-qa`，不推送含用户数据的文件。
