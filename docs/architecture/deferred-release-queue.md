# 燕子延迟发布队列（2026-10-31）

**日常提交入队不等正式发布。** 本地 PowerShell 脚本 `scripts/release-queue.ps1` 在独立 Windows 计划任务中运行，队列持久存于 `%LOCALAPPDATA%\OpenQuickHost\ReleaseQueue\queue.json`。无需燕子 GUI 常驻、无需聊天会话持续连接。

## 使用

安装定时检查（仅家用构建电脑）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release-queue.ps1 -Mode install -RepoPath F:\Desktop\kaifa\OpenQuickHost
```

新开发分支已合并推送远端 `main`，并通过基本验证后入队：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release-queue.ps1 -Mode enqueue
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release-queue.ps1 -Mode status
```

入队仅保存源提交 SHA、时间和请求版本，返回后可马上处理下一项工作。同一提交幂等；新提交取代尚未启动的较旧候选；已启动的任务保留独立状态并串行处理。状态文件以原子替换写入。支持按 ID `-Mode cancel -EntryId <id>` 或 `retry` 重入队失败项。

## 后台触发与阶段

每 5 分钟通过用户会话任务检查。电脑本地时区 08:00–23:00 执行；家里电脑关闭期间不唤醒。**同一候选满足任意一个条件即触发：累计入队 72 小时，或鼠标键盘连续无输入 600 秒。**

后台严格检查源提交仍是 `origin/main`，为其创建隔离工作树；自动计算不冲突的补丁版本（如果现有版本已发布），执行 dotnet restore/build、能力目录测试、部署接收器回归，必要时提交版本号并推送主分支。仅验证通过后发出 GitHub Actions 正式发布请求，立刻返回，不等待构建和上传。下一轮 tick 查询 CI 和 Release 资产，只有源 SHA、目标版本、完整安装包全部核验成功才记录 published。现有设备自动更新代理和12/21巡检仍负责双端安装补偿。每个阶段失败阻断后续发布，并保留错误和日志。

正式 GitHub Actions `expected_sha` 是不可变提交校验；即使主分支在调度与执行间变化，也不能误发布新提交。不存在后台直接编译后上传未审查文件的旁路。

## 约束与后续工作

此队列现阶段实现后台源码验证、异步正式发布和正式设备安装补偿。此前讨论的 **办公室优先试装 Canary** 尚需要独立的本地试装包接收器与自动验收回执，不能仅凭正式发布后的双端安装把它标记为试装已通过。上线前应将办公 Canary 的 QA 收据纳入放行门槛。

另需检验用户更换账户、长时进程异常退出、两台设备同时提交队列等情形；只有一台家庭构建机作为生产队列所有者，办公室不运行同名发布器。
