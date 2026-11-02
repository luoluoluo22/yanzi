# 阶段一 Gitee 429/5xx Retry-After 退避 QA（2026-11-02）

- 设备：办公室 CHINAMI-3IF988E；北京时间约 11:38–11:58。家里 PC-202409131508 的浣熊调用仍 pending，Desktop Commander 也报告离线，未尝试唤醒。
- 隔离工作树：`D:\Projects\qa-phase1-retry-after-20261102`；基线 `d067ec8`，分支 `fix/phase1-gitee-retry-after-20261102`。未修改 PR #126/#127 的工作树或正式安装目录。
- 发现：`ReadGiteeWithRetryAsync` 对 Gitee 429/5xx 固定 400/800/1600ms 重试，忽略服务端 `Retry-After`；多同步组件可能同时重试，加剧限流。
- 修复：独立 `GiteeReadRetryPolicy` 按服务端 Retry-After delta/HTTP-date 决定退避；缺省指数退避附加 0–200ms 抖动。若服务器要求等待超过两分钟，直接向调用方返回原始 HTTP 响应，不提前重试；错误分类/最大四次尝试/调用方取消语义保留。新诊断仅记录状态码、尝试次数与等待毫秒数，不记录 URL/token/正文。
- 自动测试：`tests/GiteeReadRetryPolicyVerification` 7/7 通过（指数、抖动上限、429 delta、503 HTTP-date、长 Retry-After 不提前重试、无头、过期头）；隔离 .NET SDK 9.0.320，退出码 0。
- Release 编译：`dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release -p:SkipStopRunningApp=true -p:RunAnalyzers=false -m:1` 退出码 0、0 错误。主线脚本只搜索系统级 SDK，办公室使用隔离 SDK，故本地 QA 暂时采用 PR #127 已验证的 `DOTNET_ROOT` 脚本版本；**此脚本变更不随本 PR 提交**，正式 CI 使用标准 SDK。
- 独立 Canary：自包含 win-x64 输出 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-retry-after-20261102-1145`，递归 586 文件；`Yanzi.dll` SHA-256 `0E1767139C2F44BCAC1C5DC6A6997E31938E292532FB39345FDED27CD0793AD9`。隔离 `--dev --show` 第三轮进程 PID 1432，选取稳定顶层 HWND 的 WM_NULL 心跳 30/30，最大 4.946ms；正式 PID 29104 前后存活，测试 Canary 已退出。
- QA 说明：前两轮对所有动态/短生命周期 HWND 混测，分别 15/30、27/30，属于测试目标不稳定；第三轮对稳定 HWND 复测 30/30。消息泵烟测不等于实际仓库 UI 或 Gitee 实网同步 QA。
- CI：新增 Windows CI 步骤；提交后需回读结果。真实 429/503 响应与 Retry-After 实网、同负载物理 GET 降幅、家↔公司一致性仍未验收。此独立 PR 不合并，第一阶段保持进行中。
- 安全：未读取/更改账号密钥、WebView2 IndexedDB、同步数据、回滚包、ReleaseQueue；未停止正式实例。
