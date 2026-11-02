# 阶段一 PR #127 办公室隔离构建与 Canary QA（2026-11-02 北京时间）

## 范围与安全边界
- 时间：2026-11-02 09:38–09:55 CST；设备：办公室 CHINAMI-3IF988E。
- 基线分支：`fix/phase1-gitee-http-lifecycle-20261102`，本地克隆起点 `ea113a51cd7f17c32e79675ac171322cbbbaecee`，与远端当时一致。未覆盖其他 Agent 工作树。
- 只在 `D:\Projects\qa-phase1-pr127-20261102` 克隆源码；缺失的 .NET SDK 通过微软官方 dotnet-install.ps1 安装至隔离 `D:\Projects\_qa_tools\dotnet9`，SDK 9.0.320；未改系统级 PATH 或正式版运行时。
- 正式 Yanzi.exe 始终为 PID 29104；开发实例使用 `--dev --show`，禁用真实账户/个人同步，未触碰生产账号、密钥、IndexedDB、历史回滚包、ReleaseQueue 或正式安装目录。

## 新发现并修复的并发问题
- PR #127 起点的 `tests/GiteeReadFlightVerification` 在办公室 SDK 上独立连续执行 5 次，**2 次通过、3 次失败**（`completed entries are removed` 两次、`no in-flight entries leak after QA` 一次）。这是实际复现的并发时序问题，不是 Gitee 实网请求计数。
- 根因：`GiteeReadFlightCoordinator` 仅依赖 `ContinueWith(ExecuteSynchronously)` 移除已完成的 singleflight；等待者可能先恢复并返回，导致字典仍保留完成任务，后续顺序读取可能短暂重用旧结果。
- 修复：等待者 `try/finally` 中若共享任务已完成则执行精确匹配移除；取消等待者不驱逐仍在运行的共享请求，保留原有 continuation 负责全体取消后的清理。增加 20 轮“完成→立即顺序再读”压力回归，并把 singleflight 测试纳入 Windows CI。
- 修复后本机独立连续 **10/10 轮通过**，每轮 `GITEE_SINGLEFLIGHT_PASS networkFlights=47 sharedWaiters=65`，所有活动条目最终清零。计数为模拟测试网络工厂调用，不是实际 Gitee HTTP 计数。
- 同时 `GiteeHttpReadMetricsVerification` 通过：`sentTotal=8 firstWindow=6 summaries=2`，覆盖空闲计时器、跨窗口完成、日志慢回调、并发有序输出及敏感信息脱敏。

## 隔离编译、安装与消息泵
- 首次 `dotnet publish -c Release -r win-x64 --self-contained true` 失败：`scripts/copy-native-window-refs.ps1` 硬编码从 `%ProgramFiles%\dotnet` 查找 WindowsDesktop 参考程序集，无法识别隔离 SDK。修复脚本优先读取有效 `DOTNET_ROOT`，未更改系统路径。
- 修复后单独执行引用复制测试，**5/5** 参考 DLL 正确复制；重新执行自包含 win-x64 Release publish，**退出码 0**，已有其他源文件的可空性警告不影响编译。
- Canary 目录：`%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-pr127-office-20261102`；586 个文件；`Yanzi.dll` SHA-256 `C68CDA707145D6F175EFE2E3C57B959D47E5B63E42666C0F585C8BE67B7AE4C6`。
- 独立 `--dev --show` 两次启动（PID 9020、4556），均保持运行；`MainWindowHandle=0`，因此第二轮枚举同 PID 的全部 8 个顶层 HWND，并对可响应句柄执行 `WM_NULL` 100 次：**100/100 成功**，P95 **0.222 ms**、P99 **1.098 ms**、最大 **5.139 ms**；进程私有内存约 **65.9 MiB**、句柄 **561**、线程 **21**。此为消息泵心跳，不等同仓库实际可见 UI 交互 QA。
- 两个 Canary PID 均在 QA 后主动停止；正式版 PID **29104** 前后不变，未安装或重启正式版。

## 门禁与下一步
- 本轮可宣布：办公室隔离 SDK/Release 自包含构建、两组自动回归及独立 Canary 启动/窗口消息心跳通过。
- **不可宣布：** 家里双端相同提交 Canary、真实 Gitee GET 降幅、重试/鉴权、跨设备新增/修改/冲突/删除墓碑/恢复一致性通过。家里 PC-202409131508 的浣熊请求持续 pending，未取得有效连接；`--dev` 禁止生产同步，不能用 0 次 HTTP 窗口替代实网 QA。
- 代码提交/PR CI 后仍保持草稿，等双端业务验收通过再合并 main；正式 GitHub Release 继续受原延迟队列约束。