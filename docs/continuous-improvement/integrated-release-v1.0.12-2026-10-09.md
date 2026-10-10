# 燕子持续改进：集成与任务治理（2026-10-09）

## 本机正式发布

- 宿主：v1.0.12，Runtime/Shell 已激活，运行目录 %LOCALAPPDATA%\YanziRuntime\versions\20261009-164545-661。
- 闲置任务：v0.2.2，定向合并 idle.tasks.list，保留此前中断/超时自动重试。
- 本次仅部署 Windows 本机正式环境，没有 Git push、公开 GitHub Release 或公共安装包发布。

## 实际代码修改

1. src/OpenQuickHost/Sync/CloudObjectSyncStateStore.cs：从有效 .bak 恢复后保存时，不覆盖好备份；使用 File.Replace 将损坏的主文件归档至 .corrupt-*；新恢复日志不包含用户同步内容。来自隔离实验提交 4f0f547。
2. src/Yanzi.SyncVerification/Program.cs：新增在主文件二次损坏后依然可以从备份恢复 pending operations 的夹具断言。
3. src/OpenQuickHost/ScriptExtensionRunner.cs：保留主工作树原有其他改动，仅将 HandleTrigger 的返回任务由同步等待改成 OnlyOnFaulted 观察，防止 ChatGPT 长任务阻塞调度；对应稳定性 S-001。
4. src/OpenQuickHost/OpenQuickHost.csproj：版本从 v1.0.11 升为 v1.0.12。
5. tools/idle-chatgpt-tasks/IdleTaskApp.cs、manifest.json：添加只读能力 idle.tasks.list；增加 MinimumRepeatMinutes、NextEligibleAt 调度门槛，手动重新排队可清除门槛。此前 MaxInterruptedRetries 等机制仍存在。
6. scripts/test-idle-task-list-capability.ps1：真实本地 Agent API 临时 Provider 的重复验收脚本。

## 验收记录

- Release Runtime：构建成功，0 错误，14 条原有警告。
- 同步架构、交错变更、下载水位、备份、冲突保护及同步 UX 测试：全部通过。
- 共享 Runtime 隔离生命周期、RPC、闲置心跳：22/22。
- 闲置任务重试、避免重复与观察窗口：14/14。
- idle.tasks.list 临时 Provider 真实 Agent API E2E：8/8。
- 正式 idle.tasks.list：Provider 已注册，Agent 调用成功，返回原有 7 条任务。
- v1.0.12 正式 Runtime 发出 source=idle 后台触发，RPC 成功，调用往返约 103 毫秒，无额外 ChatGPT 任务派发。
- 已安装正式 Yanzi.dll 检出本次独有的 ScriptRunner background trigger failed 和 Cloud object state recovered from backup 文本。
- Runtime PID 24512；health initialized=true、idleTrigger=true，心跳持续更新，常驻实例正常。

## 任务状态治理

原有 7 条任务均保留，不删除历史。
- 长期性能守护：暂时 Enabled=false，保留实验提交 1525e8f，尚未合入正式环境。
- 长期能力网络完善：Enabled=false；保留三项独立实验，本次只移植 idle.tasks.list。
- 长期 AI 使用质量：Bridge 查明当前旧 Job 成功，结算为第 3 次成功，再 Enabled=false；等待建立真实基线。
- 长期稳定性守护：Enabled=true、MinimumRepeatMinutes=720，NextEligibleAt=2026-10-10T04:49:42+08:00。
- 长期同步可靠性：Enabled=true、MinimumRepeatMinutes=720，同一下一次允许检查时间。
- 原有两条一次性成功的测试任务不改。

观察截止时间只是下一次最早允许再次检查，不构成“通过验收”。同步修复的真实效果尚需有效的生产同步和故障样本；S-001 需关注真实调度延迟与错误/并发回归。

## 备份与回滚

- 集成前源码/正式小程序/任务数据备份在 F:\Desktop\kaifa\OpenQuickHost\.artifacts\integrate-ai-loops-20261009-164124。
- 旧 Runtime/Shell 保留在 %LOCALAPPDATA%\YanziRuntime\versions\20261009-141228-917 和对应 shells 目录；安装脚本保留 runtime.previous.json。
- 原用户 SyncState、照片、笔记、Job 历史、任务历史没有被实验覆盖或清理。
- 若发现调度丢事件、重复并发或背景异常观测失效，撤销 S-001；若备份/元数据受损，撤销同步恢复路径并保留诊断文件。小程序回滚源文件和 manifest 即可，不覆盖 tasks.json。
