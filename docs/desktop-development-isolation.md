# 稳定版、开发版与常驻 Runtime

截图、剪贴板、小程序后台实例、全局快捷键和输入监听、窗口绑定与排列、定时调度、手机通信与在线心跳、云同步/WebDAV/个人同步、本地 API、Everything 和自动备份由独立的 `Yanzi.Runtime.exe` 持有。稳定版和开发版界面通过当前 Windows 用户专用的 Named Pipe 连接同一个 Runtime。

退出界面只断开连接，不关闭 Runtime。Debug 构建默认是开发版，Release 默认是稳定版；Release 也接受 `--dev`。构建钩子和开发循环仅停止精确构建路径对应的界面进程。

| 项目 | Runtime | 稳定版界面 | 开发版界面 |
| --- | --- | --- | --- |
| 单实例标识 | Yanzi.OpenQuickHost | Yanzi.OpenQuickHost.Shell | Yanzi.OpenQuickHost.Dev.Shell |
| 配置目录 | %LocalAppData%\OpenQuickHost | %LocalAppData%\OpenQuickHost | %LocalAppData%\OpenQuickHost.Dev |
| 后台资源所有者 | 是 | 否 | 否 |
| 本地 API | 原配置端口（默认 53919） | 不监听 | 不监听 |
| 小程序执行、能力调用、运行实例 | 统一执行与管理 | IPC 请求 | IPC 请求 |
| 全局快捷键与网络后台任务 | 按原配置运行 | 不重复注册 | 不重复注册 |
| 系统注册 | Runtime 自启动 | 稳定界面自启动、yanzi:// | 不修改 |

原有小程序、剪贴板历史、账号和数据留在生产数据目录，迁移不搬动或清空它们。开发版界面的配置、日志与 WebView 缓存独立；小程序目录与后台运行实例以共享 Runtime 为准，开发界面的调用会作用于真实后台资源。

## 安装与开发

```powershell
# 首次准备健康检查工具
 dotnet build src/Yanzi.RuntimeVerification -c Release -p:SkipStopRunningApp=true
# 构建并激活共享后台；首次切换会短暂退出旧版完整宿主
 .\scripts\install-shared-runtime.ps1 -Activate
# 日常开发：只重建/重启开发界面
 .\scripts\dev-desktop-loop.ps1
```

Runtime 与稳定界面部署到 `%LocalAppData%\YanziRuntime` 下不同的不可变版本目录；`runtime.json` 保存当前启动位置。安装脚本会保留上一份位置文件，激活失败自动回退。后台进程不放在 Debug 输出目录，不随界面构建终止。安装包包含 Runtime，首次使用时复制到独立目录后启动，避免占用安装器替换的目录。要更新 Runtime 本身，显式重新运行安装脚本；普通界面重建不会替换正在运行的后台版本。

Runtime 重用现有 WPF 宿主引擎，并持有一个隐藏的 MainWindow，以兼容已有小程序、原生窗口和服务接口；这次完成进程与生命周期分离，尚未把所有后台代码改写为无 UI 依赖的类库。Runtime 自身退出或崩溃仍会中断服务。开发版不是第二套独立后台沙箱。

非安装版快照的 Velopack 自动更新不可用，仍遵循现有未打包构建规则；Runtime 快照升级目前走上述安装脚本。手机/云服务网络结果取决于原有账号、配置和网络状态。

## 验证

```powershell
 dotnet run --project src/Yanzi.RuntimeProfileVerification -c Debug -- --expect-dev
 dotnet run --project src/Yanzi.RuntimeProfileVerification -c Release
 dotnet run --project src/Yanzi.RuntimeProfileVerification -c Release --no-build -- --dev --expect-dev
 dotnet build src/Yanzi.Runtime -c Release -p:SkipStopRunningApp=true
 dotnet run --project src/Yanzi.RuntimeVerification -c Release -- src/Yanzi.Runtime/bin/Release/net9.0-windows/Yanzi.Runtime.exe
# 查询真实后台状态（不读取剪贴板内容）
 dotnet run --project src/Yanzi.RuntimeVerification -c Release --no-build -- --query status
```

进程验证使用独立临时目录、独立命名管道和动态 API 端口，不注册生产快捷键。覆盖两界面共享后台、退出和重启后进程/小程序实例保持不变、能力执行、API 存活、畸形消息、未知操作和并发请求。

日志：生产后台 `OpenQuickHost\logs\runtime.log`，稳定界面 `OpenQuickHost\logs\shell.log`，开发界面 `OpenQuickHost.Dev\logs\shell.log`。配置写入使用跨进程互斥锁。
