# 燕子跨设备交付生命周期（Windows）

目标：家里完成开发/验证 -> 合并 main -> 运行 scripts/deploy-to-devices.ps1 -> GitHub Actions 构建、校验、正式发布 -> 办公室由独立于燕子的 Windows 定时任务自动下载并安装 -> 记录状态和健康结果。

这是“发布即通知、设备自主拉取”机制，不要求办公室提供公网端口或持续在线。真实跨网络传输走 GitHub Release，失败时尝试镜像，断点续传，校验 SHA-256；典型网络访问速度须按实际网络测量，不能假设恒定。

## 首次在办公室安装

可信源：本仓库 scripts/device-deploy-agent.ps1 和 scripts/install-device-deploy-agent.ps1。放到同一目录并用当前账户运行：

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-device-deploy-agent.ps1 -IntervalMinutes 3

代理复制到 %LOCALAPPDATA%\OpenQuickHost\DeploymentAgent\device-deploy-agent.ps1；任务名 Yanzi-CrossDevice-Deployment。代理独立运行，燕子退出时仍可完成安装。

人工立即触发办公室检查：schtasks /Run /TN Yanzi-CrossDevice-Deployment

停止自动更新：schtasks /Change /TN Yanzi-CrossDevice-Deployment /Disable

## 家里发布

先提高项目版本号、完成测试、合并并推送干净 main：

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-to-devices.ps1

该脚本调用通用 publish-yanzi-device-release.yml，并且阻止源码版本号与远程 main 不一致。版本号不可重复发布覆盖。构建成功且 Release 正式发布后，办公室代理在下一轮计划任务下载并安装。

## 状态

办公室机器读取：

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\OpenQuickHost\DeploymentAgent\device-deploy-agent.ps1" -Mode status

日志：%LOCALAPPDATA%\OpenQuickHost\DeploymentAgent\deploy.log
状态：%LOCALAPPDATA%\OpenQuickHost\DeploymentAgent\status.json

状态有 checking / current / available / downloading / verified / installing / verified-running / failed。verified-running 表示安装的版本正确且燕子进程已启动，不等价于所有 UI 功能通过验收。

目前结果只保存于设备本机，可通过已授权的远程浣熊查看；跨设备实时状态云面板尚未开发，不能视为已有回传能力。

## 安全边界

限定 luoluoluo22/yanzi 发布源；拒绝草稿、预发布及没有 SHA-256 digest 的资源；仅新版本自动安装，防止自动降级；同一台电脑同一时间只有一个部署进程；安装前才终止燕子；失败留下明确状态且不误报成功。特殊回退可由授权操作者显式执行 -Mode rollback -TargetVersion x.y.z。

接收端运行在当前用户权限，不使用 SYSTEM 权限，不允许发布清单远程执行任意命令。
