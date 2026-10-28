# 燕子 Windows 单主程序恢复与防回退（2026-10-15）

## 问题与根因

此前 **Yanzi.exe（前台 Shell） + Yanzi.Runtime.exe（后台宿主）** 被当作两个独立 Windows 主进程。Shell 负责 UI，Runtime 负责手机消息、输入监听、闲时任务、小程序及 IPC。更新 Shell 而不更新 Runtime 时，还会导致 `chat.stream` 等新能力未注册。

现在正式版默认只运行一个 **Yanzi.exe**，在当前进程内同时创建 UI、后台服务、小程序运行环境和原有的 RuntimeRpc 服务。 `--dev` 明确隔离的开发进程与 `--runtime` 验证专用程序仍可保留旧测试拓扑，但不是正式版启动路径。

## 代码修改

- `HostRuntimeProfile`：增加 `IsUnified`；正式版 `OwnsBackgroundServices=true`，并且不属于薄 Shell 模式。仅显式开发 Shell 可使用旧的远程 Runtime 连接模式。
- `App.xaml.cs`：正式版只注册一次单实例、直接创建 `SharedRuntimeHost` 并在本进程启动 IPC，不再调用 `RuntimeConnection.EnsureAvailableAsync` 拉起独立进程。
- `SharedRuntimeHost`：允许真正的后台服务所有者启动 IPC，而不是要求进程名必须为 Runtime。
- `MainWindow.Runtime.cs`：健康检查展示后台 owner=`unified`。
- `App.xaml.cs` 重复启动路径：用有界后台 Task 发送主实例消息，避免 WPF dispatcher 同步等待命名管道造成死锁；副本必须及时退出。
- `scripts/publish-installer.ps1`：删除独立 Runtime 的打包/依赖去重步骤，只发布单个 `Yanzi.exe` 宿主。
- `scripts/verify-release-layout.ps1`：拒绝发布含 `Yanzi.Runtime.exe` 的目录；安装后明确要求一个 Yanzi 主进程且零独立 Runtime 进程。
- `Yanzi.RuntimeProfileVerification`：补充稳定版单宿主和显式 Dev 隔离身份断言。

## 现场验收

- 桌面 C# Release 编译：**0 错误**。
- RuntimeProfileVerification：Stable、Dev 两个模式通过。
- Win-x64 self-contained 发布验证：完整目录 573 个文件，总计约 210.79 MiB，**无 `Yanzi.Runtime.exe`**。
- 官方 Windows 安装目录更新为 `%LOCALAPPDATA%\Yanzi\current\Yanzi.exe`；健康检查由当前 `Yanzi.exe` 自身响应，owner=`unified`。
- 现场 IPC 健康与能力枚举：键盘监听、调度器、闲时任务、手机消息桥、手机轮询均运行；**209 个能力（含 `chat.stream` 和 `chat.send`）、571 项小程序目录、12 个正在运行的小程序**。
- 安装后发布布局验收：`INSTALLED_RELEASE_OK`，显示一个统一主 PID、零独立 Runtime。
- 现场重复启动 `Yanzi.exe --tray`：第二个临时进程立即退出，主程序未被重复启动。
- 当前在用的旧 `Yanzi.Runtime.exe` 已停止；它的历史快照暂留硬盘仅用于紧急回退。
- 开机启动项只指向 `Yanzi.exe --tray`，无 Runtime 启动项。

## 边界和回退

- 「单程序」指只有一个燕子 **主宿主** 进程。Chromium WebView2 的渲染进程、按需执行的 CLI（如 agy、Node）或外部服务本来就是独立子进程，不应该人为塞进 WPF 宿主。
- 回退备份：
  - 双进程旧安装：`%LOCALAPPDATA%\Yanzi\backup-before-unified-20261015-170543`
  - 第一版统一宿主：`%LOCALAPPDATA%\Yanzi\backup-unified-before-secondaryfix-20261015-171350`
- 回退前必须停掉当前 `Yanzi.exe`，恢复完整旧目录，禁止只覆盖一个 DLL。若退回双进程版本，需要额外恢复当时的 Runtime 快照；正常情况下不允许再启用旧 Runtime。
- 尚未通过这次单进程改造验收的其他功能（例如手机停止动作与公网 WebSocket 回退）继续在原相关任务中处理，不能被单进程健康检查代替。
