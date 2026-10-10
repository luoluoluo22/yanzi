# 燕子 Runtime v1.0.11 · 闲置调度恢复（2026-10-09）

## 根因与证据

- 2026-10-09 10:00 正式 Runtime 运行中，健康响应缺少 idleTrigger 字段，日志没有 ExtensionIdleTrigger: started；状态心跳停在 09:59:47。
- 工作区已具备 ExtensionIdleTriggerService，但当时安装的旧二进制没有实际包含该启动逻辑。
- 旧版任务看板虽然 isRunning=true，但不代表调度器在运行。

## 修复

- MainWindow.Extensions.cs：开机先启动闲置小程序，并通过 finally 确保闲置调度器启动。
- ExtensionIdleTriggerService.cs：记录 LastObservedAt。
- MainWindow.Runtime.cs：对外报告 idleTrigger 和 idleTriggerObservedAt。
- install-shared-runtime.ps1：激活时必须通过闲置调度器启动及新鲜心跳双重验收，失败自动回滚旧 Runtime。
- Yanzi.RuntimeVerification/Program.cs：增加隔离环境的闲置调度健康与心跳回归验收。
- OpenQuickHost.csproj：版本升级到 1.0.11。

## 验证

- Release 构建：0 errors（原有 14 个编译警告）。
- 隔离 Runtime 验收：22/22 通过（包含闲置调度器在线、心跳写入）。
- 正式环境激活：20261009-141228-917；Yanzi.exe 1.0.11.0。
- 生产实时健康：backgroundServices.initialized=true，idleTrigger=true，idleTriggerObservedAt=2026-10-09T14:13:08+08:00。
- 生产状态文件心跳已从 09:59:47 恢复，至 14:15:25 仍持续更新。
- 真实 idle tick：lastTriggeredAt=2026-10-09T14:12:53+08:00；稳定性守护旧 Job 已被读取确认为 success，从 running 回到 pending，RunCount=6。
- 其余历史终态：能力网络 interrupted，同步可靠性 timeout，AI 使用质量 interrupted。它们在 Bridge state.json 中仍是相同终态；按照避免重复发送的原则不自动重派。
- git diff --check 通过。
- 正式运行 Runtime SHA256：703D8211C467ACA34BFF9D80C5AED678C772C75EB9A26379ABBF4B0B46B9DD99。

## 发布与回滚

- 已通过 install-shared-runtime.ps1 -SkipBuild -Activate 激活本机正式 Runtime；并自动留下 runtime.previous.json 与旧版本快照。
- 源文件备份：.artifacts/idle-runtime-fix-backup-20261009-140711。
- 任务数据/运行指针备份：.artifacts/idle-release-data-20261009-141228。
- 本次未对 Git 工作区批量提交，也未执行 GitHub Releases 上传；公开发行渠道仍需单独构建与来源审计。
