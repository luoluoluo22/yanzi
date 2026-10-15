# 燕子自动公开发布：授权、真实任务、第二轮修复验收（2026-10-09）

## 用户进一步授权

在已经授权的“当前 Windows 本机受控部署”基础上，用户允许长期 Agent 完成测试与真实生产验收后，自行向 luoluoluo22/yanzi GitHub 发布与该项目相关的经过审阅的 Windows 版本、安装包和更新说明，无需每次另行申请。仍不得发布未审阅的其他任务修改、隐私资料、API Token、远程 Cloudflare 服务或 Android 产物。

- docs/continuous-improvement/PRODUCTION_DEPLOYMENT_POLICY.md：新增公共发布边界和发布门禁。
- scripts/ai-loop-publish-reviewed-release.ps1：独立、干净的 ai-loop Git worktree + reviewed commit + base + ApprovedFiles +版本匹配的安装包（来自 worktree 本身 .artifacts/installer）+ 可重复验收 + release notes。默认仅 dry-run；Apply 才推送审核后的单独 release 分支和 Github Release；绝不 git add . 或直接推送脏 main，不覆盖已有 tag。
- 本机 GitHub origin/private remote 中曾有直接嵌入的 token，已改为无密钥 URL，并通过 gh auth setup-git 的凭据助手验证能从 origin 读取 HEAD。不可复制/显示原 token。
- 门禁测试：Parser=0 errors；脏主工作树被正确拒绝；隔离的 clean Git worktree 版本 9.9.9 + 精确审核文件和虚拟资产能够生成 PLAN_ONLY 预览；临时 fixture branch/worktree 和伪安装包均已清理，没有任何公开发布。后续又加了更严格的资产必须属于隔离工作树检查，应按真实安装包做再次验收。

## 五项长期任务

已将公开发布授权及专用脚本写进正式 tasks.json 的五个长期任务 Prompt，先备份于 .artifacts/ai-loop-public-release-authorization-20261009-171536；性能、稳定性、同步、能力网络按原调度启用，AI质量仍因缺真实基线而暂停。

## 真实派发与复盘

1. 2026-10-09 17:16：正式燕子 Runtime idle 触发成功，新 Job 5c39a580-512f-4fb8-9eb1-9b0981ad3521，Bridge 请求确认已包含 GitHub 发布权限。Bridge 17:22 确认 success，但 Agent 实际只观察并写性能报告，明确回传 deploymentStatus=blocked、releaseStatus=blocked；并未安装或公开发布。
2. 识别到旧提示词的逻辑死锁：候选没有上线却仍要求“等待部署后的真实反馈”，致使循环只报告未部署。已替换 performance Prompt 里的旧反馈前提，要求先在独立工作树做当前正式 v1.0.12 的最小兼容性集成、隔离测试及受控上线，之后才允许等待生产样本；旧 Job success 状态已依据 Bridge 结果结算并留备份 .artifacts/ai-loop-integrate-priority-20261009-172354。
3. 2026-10-09 17:24：第二轮 Job aa3c8040-ddfc-4c3e-94b8-b879b209e0a6 真正由 Runtime 自动派发；直接从 Bridge 保存的请求检查到了最新集成优先指令、公共发布授权和受控发布脚本路径。最后一次可核实状态 17:31 北京时间仍为 running；不得声称该 Job 已完成。

## 下一轮修复：真实完成与业务验收分开

- 闲置任务源码升级到 v0.2.3，增加 LastDeploymentStatus、LastReleaseStatus、LastBusinessStatus。桥接请求 success 与业务部署/发布成功不再混同，按 Agent 回传中结构化 deploymentStatus / releaseStatus 归档业务结果；未明确上报则为 unverified。
- 最初 18/18 隔离回归通过；进一步增加对实际 CompleteSuccess 状态落库/重复任务冷却的 4 项测试，现累计 22/22 通过。
- 已通过 scripts/ai-loop-promote-extension.ps1 在本机正式升级闲置任务 v0.2.3；自动备份位于 %LOCALAPPDATA%/OpenQuickHost/AiLoopReleaseBackups/idle-chatgpt-tasks-20261009-172851-903。
- 正式 Yanzi extension.status 返回 v0.2.3、running，Agent API idle.tasks.list 真实调用成功，看到 businessStatus、deploymentStatus、releaseStatus 等新字段，原 Job aa3c8040-ddfc-4c3e-94b8-b879b209e0a6 未被重发或更换 ID。

## GitHub 最终发布

截至最后一次检查，GitHub 最新 Windows 公开 Release 仍是 v1.0.10；用户当前 Windows 正式 Runtime 是 v1.0.12，但主仓库 HEAD 对应项目版本仍 v1.0.10，且主工作树存在大量并行未提交改动。 .artifacts/installer 中仅找到 v1.0.10 安装包，缺少由干净审阅后源码生成的 v1.0.12+ 安装包。

因此**本轮禁止误报“最终公开版本已发布”**。必须完成第二轮实际性能修复、生产样本门禁、clean reviewed release commit、隔离构建新安装包并最终核实 GitHub Release URL + assets。旧一键 release.ps1 含 git add .，不能自动使用。

已创建一次性发布续验提醒约 2026-10-09 17:56 北京时间，续查第二轮 Job；未满足门禁时只写 releaseStatus=blocked，不发布伪版本。


## 2026-10-09 17:53（发布续验，只读生产核查）

- `taskId=performance-guard`，`changeId=perf-icon-1525e8f-on-1.0.12-20261009`，`baseRevision=ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`；本次 `changedFiles=[]`（仅更新本验收记录），未重跑构建、未部署、未回滚、未提交或推送。
- 已重新连接 Windows 浣熊。Bridge Job `aa3c8040-ddfc-4c3e-94b8-b879b209e0a6` 实际 `jobStatus=error`，`diagnosis=conversation_network_activity`，`nextAction=read_only_reconcile`；流量不证明提示词被接受，**未重新提交 Job**。
- 正式 Runtime PID=22416，Shell PID=4856，二者可执行路径均为 `20261009-174418-888`；`runtime.json` 激活时间 `2026-10-09T17:44:21.0431702+08:00`；正式 Yanzi.dll SHA-256 `3EA8256B327964F7660CE5CEF22ED77E1AE3468015E3796A1C936ED9B345B358` 与候选一致。`deploymentAttempted=false`（本次）、`deploymentStatus=installed`（沿用已验收激活）、`rollbackTriggered=false`；旧版快照 `20261009-164545-661` 和指针备份仍为预设回滚依据。
- 本次未新增有效生产启动样本：Runtime 自激活后主实例成功启动记录仅 1 次；前轮已验证 `smokePassed=true`、11 个扩展恢复、idle 心跳及只读 API；正式反馈仍为 `realWorldSamples=1`，不能从单样本推断 P50/P95。首次小程序打开独立耗时仍无数据。单次 Shell 8701ms 高于部署前最近 6162ms，须继续监测。Runtime 日志出现 2 次 VelopackLocator 初始化失败，但同日志部署前已有至少 6 次同类失败，且当前进程仍在线；不据此宣称新增崩溃或启动失败。
- 当前性能独立工作树 `ai-loop/performance-v1_0_12` HEAD `ff89bb0` 含多项未提交兼容快照，**不 clean**；主工作树也脏。`.artifacts/installer` 仅有 Windows `1.0.10` 安装包，没有从已审阅 1.0.12+ 源码生成的匹配安装包。`gh release list --repo luoluoluo22/yanzi` 仍显示 Windows 最新 `v1.0.10`，不存在经本轮核验的新 Release URL/资产。
- **`releaseStatus=blocked`**；`releaseBlocker=生产反馈尚未达到激活后6小时或10个有效启动样本，Shell启动单次负向观测待确认，缺clean reviewed release commit、同源新版Windows installer和已审阅release notes`。依门禁没有运行 `ai-loop-publish-reviewed-release.ps1 -Apply`，也没有运行旧 `release.ps1`。`nextEligibleAt=2026-10-09T23:44:21+08:00`（或提前达到10个有效样本）；届时先核对 P50/P95、Shell 首开、CPU/RAM/I/O 与业务回归，异常则按原备份回滚，通过后再准备独立干净审阅提交与同源安装包、执行发布预演/正式核验。
