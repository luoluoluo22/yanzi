# 燕子四阶段性能治理 — 执行与验收台账

更新时间：2026-11-01 17:44（北京时间）。此台账用于持续自动化推进；**未验证通过的阶段不可标记为完成**。

## 第一阶段：同步引擎降频与去重（进行中）

基线（家里电脑既有日志样本，非总 HTTP 计数）：主日志最近 16,000 行内有 4,920 条 `extension-file-change` 同步调度记录；约五小时诊断样本内有 5,996 条 Gitee 读取请求；2,271 条账户扩展同版本代码冲突提示。

### 子任务 1A：扩展 FileSystemWatcher 入口合并

- 分支：`fix/phase1-watcher-batch-20261101`，以 `origin/main` 的 `f367c54` 为基础；代码在 `MainWindow.CloudSync.cs` 与 `MainWindow.xaml.cs`。
- 改动：FileSystemWatcher 线程先在锁保护下按扩展 ID 累加 dirty 状态、事件总数及目录清单变更标记；一轮只投递一次 Dispatcher 回调，并由 400ms 计时器统一调用现有个人云同步/账户同步调度；关闭窗口停止计时器。保留现有上传、冲突判断和延迟同步语义；未更改正式 ReleaseQueue。
- 编译：2026-11-01，Release `dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --no-restore -p:RunAnalyzers=false -p:SkipStopRunningApp=true -m:1 --nologo -v:q`，退出码 0。现有非改动文件的可空性/未使用字段警告仍在。
- 本机真实事件压力测试：独立复制到 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-watcher-20261101\Yanzi.exe`，与源程序集 SHA-256 一致，使用 `--dev --show --watcher-qa` 隔离运行（PID 25048）；正式版仍为 PID 23788，未覆盖。
- 第一轮：2 个测试扩展目录共计 440 次写文件，实际观测到 330 个有效 FileSystemWatcher 通知 → 1 次批处理，日志 `17:42:37.013 Extension watcher batch: events=330, extensions=2, catalogChanged=False`。
- 第二轮：两次相隔约 2 秒的各 80 次写文件 → 分别触发 76 和 63 条 watcher 通知，各合并成 1 次批处理（17:43:05.796 与 17:43:07.841）；包含 `.cs` 变更时 `catalogChanged=True` 且 17:43:06.454 的 `Local manifest changed` 日志确认没有丢失目录刷新。
- 独立窗口消息心跳：40/40 成功，无超时，最大约 7ms；开发版非可见顶层窗口，不能替代有人交互的 UI 可见窗口 QA。
- 保护：本次所有专用测试扩展目录已经删除，开发版测试进程已停止；正式版未重启。
- 验收状态：**局部真实事件压力验证通过，跨端云同步一致性仍待验收，因此第一阶段整体未完成**。
- 注意：开发版禁用账户/个人同步，所以以上实验仅验证 watcher→dispatcher 合并和清单刷新，不构成实际 Gitee 上传、冲突合并或跨端一致性通过。

### 子任务 1B：Gitee/个人同步读取去重（进行中：并发 singleflight 已实现、实网仍待验收）

- 本轮修复（2026-11-01，北京时间）：`Sync/GiteeReadFlightCoordinator.cs` 在进程内对“同 Gitee 账号 token、owner、仓库、分支和完整路径”的在途 GET 请求执行 singleflight。只共享正在等待的请求；请求一结束立即移除，不缓存成功、404 或错误结果。各等待者使用独立取消语义，响应字节数组分别复制。写入/删除前后打断旧请求的合并资格，防止后续读取错误地加入旧 GET。密钥哈希后作隔离键，禁止明文存储 token。
- 回归：`dotnet run --project tests/GiteeReadFlightVerification/GiteeReadFlightVerification.csproj -c Release` 退出码 0：64 个并发读请求只执行 1 个实际底层读、等待者取消独立、失败后能再取、写入失效前后的两代在途请求不互相误清理、跨账号/路径隔离、任务结束后 ActiveCount=0；独立测试计数 networkFlights=7、sharedWaiters=65（仅虚拟请求，不是生产网络数据）。
- 主程序 Release 编译退出码 0。本机验证：独立开发版安装到 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-gitee-read-20261101`，Yanzi.dll SHA-256 `719E4CCFDD22F3479135E1116CFB8AEFB7732B2B7E1DC12554D742213C797E57`，进程 PID 3296，窗口消息 50/50 成功、最慢 8ms；测试进程已退出，正式版 PID 23788 未更改。
- 未完成：该轮不使用生产 Gitee 凭证，也未在开发版启用真实账户同步。还需采样并发比例，验证实际 HTTP 请求下降、失败重试与家↔公司变更/冲突/删除一致性；顺序重复 GET 不会被本 singleflight 改善，后续仍需增量索引和版本检测。
- 2026-11-01 后续子修复：`Sync/PersonalSyncService.cs` 的启动器配置同步将“检查配置对象是否存在”与“读取配置对象”合并成单次遍历，每个定义只读取一次。旧流程在找到首个存在对象时先做一次存在性 GET，再重新完整遍历，额外读取 1 至 N 条路径；新流程保留“非空但损坏对象也算存在”、旧配置回退和逐项最新读取语义，不采用长期缓存。该优化只消除一次同步内的重复读取，不能代替 Gitee 大量周期性顺序请求的后续治理。
- 验收：`scripts/verify-launcher-config-singlepass.ps1` 检查两处调用方仍使用相同的存在性语义、每个配置对象在单次扫描只读取一次，且旧燕幕兼容入口保留；14 个定义时仅作静态请求模型测试，首个存在对象位于第 1/3/14 个时模拟请求数从 15/17/28 降为 14，**不是线上 HTTP 实测减少数量**。Release 编译成功退出码 0；本机复制到 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-launcher-singlepass-20261101`，SHA-256 `D2B303CDEFD00904DCC34467534CB2BE8E1211D0390547972DD99B2D723CFF11`，独立 `--dev --show` 实例 PID 18612 启动成功，窗口消息 50/50 成功、最慢 9ms；测试实例已停止，正式版 PID 23788 始终运行。开发版未启用真实 Gitee 写入、未完成双端一致性验收。
- 实施需保留 ETag/哈希失效、跨设备更新检测和删除墓碑语义；不得直接将长 TTL 缓存当作权威值。后续考虑基于经验证的版本/哈希增量索引。
- 验收：对相同业务负载采集真实发送到 Gitee 的 HTTP 请求数、延迟、失败率，并在家里↔公司双端变更、冲突、删除恢复测试中保持一致。

### 子任务 1C：账户同版本扩展冲突重复扫描（未开始修复）

- 证据：既有样本 2,271 条冲突日志；需要区分逻辑上有效的冲突与每次重试的重复打印。
- 验收：无变化时冲突不再重复消耗扫描、上传和日志资源；版本真正变化时仍能正确提示并处置。

## 第二阶段：UI 主线程减负（未开始）

后台化目录签名/全量清单扫描，治理同步 Dispatcher.Invoke 与高频计时器。验收：仓库和小程序在同步压力下 UI 心跳/P95/P99 与故障复现对比。

## 第三阶段：存储与生命周期（未开始）

审计 DeploymentAgent 历史安装包（家里约 2.70GB/办公室约 2.87GB）及 WebView2 本地数据（家里约 1.49GB）；先落实必要回滚和用户数据保留策略，不得直接删除 IndexedDB/账号内容。

## 第四阶段：常驻资源与基线（未开始）

在冷启动、空闲 30 分钟、仓库搜索、批量云同步和多小程序并发下比较 CPU、私有内存、线程/句柄、UI 延迟分位数和对象释放情况。

## 自动化工作要求

每轮先确认北京时间 08:00–22:00；非工作窗口不访问或唤醒家里电脑。检查已合并与未合并修复，优先补缺失的本地安装和跨端测试；每轮推进可核验的修复或验收。每项按源码→编译→立即隔离本机安装 QA→回归→PR/CI→合并 main→回读验证。延迟的仅为正式 GitHub Release 发布，不是开发版安装验证。所有阶段验证通过后停用定时任务。
