# 第一阶段双电脑 Canary 安装与独立 QA（2026-11-01，北京时间）

## 范围与主线
- 本轮 19:18–19:41；家里 PC-202409131508、办公室 CHINAMI-3IF988E。
- 源码：`origin/main` 提交 `a372d14`（PR #123 已合并，包含 #122 的 Gitee 物理 HTTP GET 计数和此前同步修复）。
- 仅使用隔离 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-dual-a372d145`，启动参数 `--dev --show`。正式版及用户账户目录不修改，不触碰正式 ReleaseQueue。
- 家里编译命令：`dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --no-restore -p:RunAnalyzers=false -p:SkipStopRunningApp=true -m:1 --nologo -v:q`，退出码 0（有既存可空性警告）。
- 测试程序集：`Yanzi.dll` SHA-256 `BBEFB21FC46A0B8C223914C6D5870A4412F03EEDD2571CC735F14C796CD23CF6`；独立目录 155 个文件、约 49.8 MB；跨端传输 ZIP 20,098,434 字节，SHA-256 `A22716B32BC435D3783B6FF47367C8D49B9EA29A1D253AEE207B93BB14945D04`，办公室落地校验一致。

## 双端实际启动与消息心跳
| 设备 | 正式版 PID（全程保持） | Canary PID | 相同业务程序集哈希 | WM_NULL 成功 | P95 | P99 |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| 家里 | 23788 | 10040 | 一致 | 100/100 | 0.0952 ms | 0.1418 ms |
| 办公室 | 29104 | 29544 | 一致 | 100/100 | 0.2721 ms | 7.0424 ms |

说明：窗口消息心跳仅证明隔离 Canary 消息泵可响应，不等同真实仓库 UI 操作压力测试；办公室进程在启动完成后私有内存约 218.6 MB、40 线程，家里测试时约 250.1 MB、27 线程。不同机器、不同时间的样本不可直接推断性能改善幅度。

## 办公室启动故障与隔离修复
1. 首次复制的是 framework-dependent Release 文件，办公室没有全局 .NET Desktop Runtime：`.NET Runtime` 事件 1023 提示 `You must install .NET`；最初 2 线程/2.5 MB 的进程不是成功启动，早期窗口心跳结果不得计入通过。
2. 仅在 Canary 目录从**办公室已有正式版安装目录**复制自带 .NET 9.0.21 runtime 文件，并备份 Canary 原有 apphost、runtimeconfig、deps；使用正式版自包含 apphost 和 runtimeconfig/deps，保留新构建的 `Yanzi.dll`、`Yanzi.UI.Wpf.dll`。
3. 修正 11 个 framework-dependent 引用程序集（例如 `Microsoft.Win32.SystemEvents.dll`）与自包含运行时实现的冲突；曾捕获 `SystemEvents is not supported on this platform` 启动致命错误，修正后正常启动。备份在 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-dual-a372d145-original-apphost`。
4. 该办公室运行包是**相同业务程序集 + 本机既有运行时的混合 Canary**，并非与家里字节完全一致的发布包。正式版 PID 29104 未被重启、覆盖或停止；下一次建议直接使用 `dotnet publish -r win-x64 --self-contained true` 制作独立可携带测试包。

## 自动回归（均为本机隔离/模拟，不是生产网络请求）
- `tests/GiteeHttpReadMetricsVerification`：退出码 0；6 GET + 1 POST 分类正确；第二窗口累计 `sentTotal=8`、首窗 6、汇总 2；重试/错误/取消分类、无 URL/token 泄漏检查通过。
- `tests/GiteeReadFlightVerification`：退出码 0；64 个并发逻辑读取只触发 1 次底层 fetch；独立取消、失效隔离、无在途条目泄漏；测试汇总 `networkFlights=7, sharedWaiters=65`。
- `scripts/verify-extension-index-batch.ps1`：退出码 0；40 个二进制写入仅一次索引写入/验证；第二端可发现全部 40 个键；重复同步不重写索引，分歧云端字节不被覆盖。
- 最初测试命令使用 `--no-restore` 遇到缺少 `project.assets.json`，去掉该参数后成功；另一次脚本路径拼写错误，已改用仓库实际存在的 `verify-extension-index-batch.ps1` 重测通过。

## 尚未满足的阶段一门禁
- `--dev` 按 `HostRuntimeProfile.ApplySettings` 明确禁用真实账户/个人同步，**不能**用 Canary 空请求日志宣称真实 Gitee GET 降幅。
- 尚无相同业务负载下旧/新**真实** Gitee 物理 HTTP GET `sentTotal`、失败率、耗时对照；旧基线 5 小时 5996 次仅为逻辑日志样本，不能直接作网络基线。
- 家↔办公室真实账户同步的新增、修改、冲突、删除墓碑、恢复一致性尚未验收。需要通过受控真实同步通道验证，严禁直接修改正式账户或同步仓库测试数据。
- 尚无真实仓库操作期间的 UI P95/P99；阶段二、三、四均未验收。

**结论：阶段一“相同主线业务程序集双端隔离安装、启动、窗口消息心跳和同步关键自动回归”通过；“真实云同步降频/多端一致性”未通过最终验收。不得发布阶段一完成结论，也不得绕过正式 ReleaseQueue。**


## 2026-11-01 20:25–20:35 正式版只读日志对照

本轮只读解析两台电脑正式版的 `cloud-sync-diagnostics.log` 和 `shell.log`，按每行前缀时间筛选同一北京时间窗口（2026-11-01 20:25:00 至 20:35:00，左闭右开）。**计数为日志条数，不是物理 HTTP 请求次数**；没有对正式版注入或启用新同步，也没有触碰账号数据。

| 设备 | Gitee `Read requested` | `extension-file-change` | 冲突关键词日志 | `Personal sync` | 实际 HTTP `sentTotal` 指标 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 家里 PC-202409131508 | 744 | 389 | 126 | 406 | 0 行 |
| 办公室 CHINAMI-3IF988E | 0 | 0 | 0 | 0 | 0 行 |

正式版进程采样：家里 PID 23788，私有内存 578.0 MiB、句柄 1630、线程 80、累计 CPU 859.5 秒；办公室 PID 29104，私有内存 513.4 MiB、句柄 1420、线程 65、累计 CPU 2666.3 秒。累计 CPU 不能当作窗口内 CPU 消耗。两端工作负载明显不相同，办公室该窗口无对应日志，**不得用 744:0 推导优化降幅**。正式版未安装最新主线，故 `sentTotal` 指标为 0 行不等于 0 次网络请求。此轮只建立可复核的同窗口只读样本，真实 HTTP 降幅及同步一致性仍未验证。