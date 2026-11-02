# 第一阶段 PR #127 — singleflight 物理 GET 可观测性与办公室隔离验收（2026-11-02）

## 变更及边界
- 分支：`fix/phase1-gitee-http-lifecycle-20261102`；以既有 PR #127 为基础，未修改其他 Agent 的 PR #128。
- Gitee HTTP 一分钟窗口快照新增三个**进程级累计/瞬时聚合字段**：`singleflightLeadersTotal`、`singleflightSharedWaitersTotal`、`singleflightActiveFlights`。快照在窗口锁内读取，避免异步慢日志造成计数与窗口输出时刻错位。保留既有 `sentTotal/completedTotal` 作为真实底层 GET 计数；singleflight leaders **不是** HTTP 请求数。
- 不记录 token、URL、路径或响应正文；不启用真实账户同步、不更改 ReleaseQueue 或生产账号状态。

## 独立自动回归
- 办公室 CHINAMI-3IF988E；隔离 SDK `D:\Projects\_qa_tools\dotnet9\dotnet.exe` 版本 9.0.320。
- `dotnet run --project tests/GiteeHttpReadMetricsVerification/GiteeHttpReadMetricsVerification.csproj -c Release`：全部断言通过；`GiteeReadFlightVerification` 同样通过。
- 新增集成用例：**64 个逻辑并发读取 → 1 次模拟 HttpMessageHandler 物理 GET**；首窗口 `sent=1,completed=1,leadersTotal=1,sharedWaitersTotal=63,active=0`；随后的顺序读取不复用旧值，实际第二次 handler GET，第二窗口 `sentTotal=2,completedTotal=2,leadersTotal=2,sharedWaitersTotal=63`。
- 以上“物理 GET”指测试替身的 HttpMessageHandler 调用，**并非 Gitee 实网请求**，不能宣称生产请求降幅。新增集成测试再连续执行 **5/5 通过**。

## Release 编译及立即独立 Canary QA
- 隔离 `win-x64 --self-contained true` Release publish 输出：`D:\Projects\_qa_tools\canary-phase1-20261102-1340`，未覆盖正式版。
- `Yanzi.dll` SHA-256：`9A4B69AFFD965E131A36E4BFC7B26C2079118DDA85DBC932E7D1A1C80C4728B8`。
- `--dev --show` Canary 第一轮 PID 32716，13 秒启动后枚举 8 个顶层 HWND，WM_NULL **386/400**，P95 **19.0918ms**，私有内存 **67.82 MiB**，句柄 **580**，线程 **20**。14 次失败如实保留，不能视为全通过。
- 第二轮 PID 35184，等待 20 秒完成启动后，8 个 HWND 各 20 次 WM_NULL：**160/160 通过**。两轮 Canary 均退出，无残留；正式版 PID **29104→29104** 未变。
- 该心跳只验证独立开发实例窗口消息，**不等同真实仓库可见交互或 Gitee 账号同步 QA**。

## PR/CI 与剩余验收
- 变更前 PR #127 head `2ba219ad` 对应 CI `41387206654` completed/success；本轮新代码需在推送后重新等待 CI，不能沿用旧结论。
- PR #128 的旧 head `e9bdd7b` 对应 CI `41386040430` completed/success，仍为 Draft，未覆盖其 Retry-After 代码。
- 家里 PC-202409131508 浣熊连接本轮返回 pending，未取得同提交家里 Canary 证据。
- 必须继续验证：两端同提交隔离安装、真实 Gitee 相同负载 `sentTotal` 差值、429/503 重试与超时、跨端新增/修改/冲突/删除墓碑/恢复一致性。**阶段一未完成，PR #127 保持 Draft。**
