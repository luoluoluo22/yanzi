# 燕子四阶段性能治理 — 执行与验收台账

更新时间：2026-11-01 20:47（北京时间）。此台账用于持续自动化推进；**未验证通过的阶段不可标记为完成**。

## 第一阶段：同步引擎降频与去重（进行中）

基线（家里电脑既有日志样本，非总 HTTP 计数）：主日志最近 16,000 行内有 4,920 条 `extension-file-change` 同步调度记录；约五小时诊断样本内有 5,996 条 Gitee 读取请求；2,271 条账户扩展同版本代码冲突提示。

### 子任务 1A：扩展 FileSystemWatcher 入口合并

- 分支：`fix/phase1-watcher-batch-20261101`，已通过 PR #115 合并 `main`（`5435f64`，CI 通过）；代码在 `MainWindow.CloudSync.cs` 与 `MainWindow.xaml.cs`。
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

- 已合并：PR #117（`689211b`，CI 通过），`Sync/GiteeReadFlightCoordinator.cs` 在进程内对“同 Gitee 账号 token、owner、仓库、分支和完整路径”的在途 GET 请求执行 singleflight。只共享正在等待的请求；请求一结束立即移除，不缓存成功、404 或错误结果。各等待者使用独立取消语义，响应字节数组分别复制。写入/删除前后打断旧请求的合并资格，防止后续读取错误地加入旧 GET。密钥哈希后作隔离键，禁止明文存储 token。
- 回归：`dotnet run --project tests/GiteeReadFlightVerification/GiteeReadFlightVerification.csproj -c Release` 退出码 0：64 个并发读请求只执行 1 个实际底层读、等待者取消独立、失败后能再取、写入失效前后的两代在途请求不互相误清理、跨账号/路径隔离、任务结束后 ActiveCount=0；独立测试计数 networkFlights=7、sharedWaiters=65（仅虚拟请求，不是生产网络数据）。
- 主程序 Release 编译退出码 0。本机验证：独立开发版安装到 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-gitee-read-20261101`，Yanzi.dll SHA-256 `719E4CCFDD22F3479135E1116CFB8AEFB7732B2B7E1DC12554D742213C797E57`，进程 PID 3296，窗口消息 50/50 成功、最慢 8ms；测试进程已退出，正式版 PID 23788 未更改。
- 未完成：该轮不使用生产 Gitee 凭证，也未在开发版启用真实账户同步。还需采样并发比例，验证实际 HTTP 请求下降、失败重试与家↔公司变更/冲突/删除一致性；顺序重复 GET 不会被本 singleflight 改善，后续仍需增量索引和版本检测。
- 已合并：PR #118（`fa5fad2`，CI 四项通过），`Sync/PersonalSyncService.cs` 的启动器配置同步将“检查配置对象是否存在”与“读取配置对象”合并成单次遍历，每个定义只读取一次。旧流程在找到首个存在对象时先做一次存在性 GET，再重新完整遍历，额外读取 1 至 N 条路径；新流程保留“非空但损坏对象也算存在”、旧配置回退和逐项最新读取语义，不采用长期缓存。该优化只消除一次同步内的重复读取，不能代替 Gitee 大量周期性顺序请求的后续治理。
- 验收：`scripts/verify-launcher-config-singlepass.ps1` 检查两处调用方仍使用相同的存在性语义、每个配置对象在单次扫描只读取一次，且旧燕幕兼容入口保留；测试脚本假设 14 个对象作静态请求模型（当前源码实际定义 11 个对象），首个存在对象位于第 1/3/14 个时模拟请求数从 15/17/28 降为 14，**不是线上 HTTP 实测减少数量**。Release 编译成功退出码 0；本机复制到 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-launcher-singlepass-20261101`，SHA-256 `D2B303CDEFD00904DCC34467534CB2BE8E1211D0390547972DD99B2D723CFF11`，独立 `--dev --show` 实例 PID 18612 启动成功，窗口消息 50/50 成功、最慢 9ms；测试实例已停止，正式版 PID 23788 始终运行。开发版未启用真实 Gitee 写入、未完成双端一致性验收。
- 实施需保留 ETag/哈希失效、跨设备更新检测和删除墓碑语义；不得直接将长 TTL 缓存当作权威值。后续考虑基于经验证的版本/哈希增量索引。
- 验收：对相同业务负载采集真实发送到 Gitee 的 HTTP 请求数、延迟、失败率，并在家里↔公司双端变更、冲突、删除恢复测试中保持一致。

### 子任务 1C：账户同版本扩展冲突重复扫描（代码已合并，实网验收未完成）

- 证据：既有样本 2,271 条冲突日志。2026-11-01 北京时间 18:27–18:36 的家里正式版日志新样本：804 行内 72 条冲突日志，仅 18 个唯一的“扩展 ID / 本地版本 / 云端版本 / 云端修订”指纹，各重复 4 次；证明反复提示并非 72 次不同的代码变更。办公室 CHINAMI-3IF988E 北京时间 18:49 的只读样本：最近 16,000 行正式版日志中 1,044 条冲突提示，仅 23 个唯一指纹，最常见的三个指纹各重复 48 次；两个样本窗口不同，不得直接比较两台机器的性能。办公室正式版 PID 29104 未修改。
- 修复分支：`fix/phase1-conflict-memo-20261101`，已通过 PR #119 合并 `main`（`6bf964d`，CI 四项通过）。同版本且远端包有 SHA-256 时，将“保留本地”判定进程内记忆 5 分钟，文件监听事件即时失效，远端版本/修订/哈希、安装路径变化即时重新计算；计算期间若文件发生变化，generation 防止写入旧缓存。相同冲突指纹 15 分钟内不重复记录，内容/修订变化立即重新提示。缺少远端哈希时不再无意义打包本地 ZIP。
- 安全边界：**同版本永不触发云端覆盖安装**；新版本升级仍执行原有两次内容哈希、下载后复核和升级备份。5 分钟只是冲突判定的最长重算间隔，绝非云端文件内容缓存；文件监听漏报时最多延迟冲突提示，不改变安装安全判断。
- 回归入口：`scripts/verify-account-conflict-memo.ps1`，测试 TTL、远端修订/哈希、文件监听失效、竞态 generation、冲突日志提醒及新版本安装分支，退出码 0。四轮相隔三分钟、远端状态不变的**模型**中，本地 ZIP 哈希从 4 次降至 2 次、重复日志从 4 条降至 1 条；这是纯内存模型，不是实际云端负载。
- Release WPF 编译退出码 0；独立 Canary 安装 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-conflict-memo-20261101`（155 文件），程序集 SHA-256 `52CB988BF9E0DB3BE018058B72DB14879CDA1161F597AFB120B392A7FD7C4F71` 与构建一致；`--dev --show --watcher-qa` PID 2236 实际窗口 WM_NULL 50/50，最慢 149ms；Canary 已退出，正式版 PID 23788 未更改。dev 模式禁用账户同步，不能据此宣称真实 Gitee 减量或跨端冲突通过。
- PR #119：CI `classify`、`portable-contracts`、`desktop-and-worker`、`Workers Builds: yanzi-sync` 全部通过；已在 GitHub 合并并回读验证主线源码与测试脚本一致。**双端业务一致性与实际 HTTP 减量仍未验收。** 办公室云同步诊断样本有 386 条 Gitee `Read requested`、378 条 `Read completed`，它们是逻辑读取日志而非实际发出的 HTTP GET 计数。
- 下一步：在 Gitee 实际网络请求发出点补齐计数（不能以逻辑 `Read requested` 代替 HTTP GET）；待正常发布队列触发后以同时间窗口/同业务负载采集家↔公司请求量、耗时、失败率和冲突/删除/恢复一致性，检查同版本缓存失效后确实重新计算；未通过前第一阶段保持“进行中”。

### 子任务 1D：扩展二进制恢复去重复下载（2026-11-01，局部修复）

- 源码：`ExtensionStorageService.Recovery.cs`。批量恢复时原先先逐个检查/发布本地二进制资源（`PublishExtensionBinaryAssetIfAbsentAsync` 已访问远端并比对内容），后在扫描远端索引时，再次下载**已存在本地文件**，即使内容不同也不覆盖本地文件。这些第二次下载及本地二次读盘没有改变同步结果。
- 修复：第二轮遇到本地已存在的二进制文件直接跳过远端 GET，保留第一轮的云端查重/不覆盖冲突规则；缺失文件仍下载并原子恢复，下载期间本地新建文件也不会被覆盖。增加每轮计数日志 `existingBinaryReadsAvoided`，用于真实运行后核实避免的请求数。
- 回归：`scripts/verify-extension-binary-recovery-skip.ps1` 检查先判断文件存在、再发远端请求、恢复前再次检查竞态、文本同步路径未被误改。模型测算：已有 30 个/缺失 2 个时，第二轮远端读取由 32 减至 2；**模型不是生产 HTTP 实测**。本机 QA：Release 编译退出码 0，复制独立安装目录 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-binary-recovery-20261101`，程序集 SHA-256 `5B979B52310F13D0E9B5CC3067F2FC159E2E0030168C862FBB826EE315CF8525` 一致；以 `--dev --show` 启动 PID 18616，窗口消息 50/50 全成功、最长 9ms。测试后仅关闭开发实例，正式版 PID 23788 未中断。开发通道未连接真实同步，仍需对已存在/缺失云端资源、删除恢复及家里↔办公室双端一致性进行真实网络验收，不能仅凭模型宣布第一阶段完成。
- 与“同版本冲突重复处理”并行开发分支 `fix/phase1-conflict-memo-20261101` 相互独立，本修复不修改其代码或状态。

### 子任务 1E：Gitee 真实 HTTP GET 计数与失败分布（2026-11-01 19:05，北京时间；代码/本机 QA 通过，待 PR 与双端实网）

- 分支：`fix/phase1-gitee-httpmetrics-20261101`（基于 `origin/main` 的 `37c308e`），独立工作树，不触碰其他 Agent 的开发分支。
- 变更：`Sync/GiteeHttpReadMetricsHandler.cs` 在 Gitee 专属 `HttpMessageHandler.SendAsync` 真实发送入口计数，覆盖内容读取、SHA/owner/probe GET 与自动重试；不把 `Read requested` 逻辑日志当作网络请求。只按一分钟窗口输出一条聚合日志，包含 `sent/completed/sentTotal/completedTotal`、2xx/404/429或5xx/其他状态、传输异常、取消、耗时总计与最大值；请求 URL、路径、token、响应内容均不进入新指标。空闲不主动唤醒，窗口到期后在下一次 GET 输出；窗口边界可能存在跨窗在途请求，以累计 sentTotal/completedTotal 为准。
- 自动验证：`dotnet run --project tests/GiteeHttpReadMetricsVerification/GiteeHttpReadMetricsVerification.csproj -c Release` 退出码 0；模拟 6 次 GET + 1 次 POST，首窗口真实传输计数 GET=6，状态分类 2xx/404/503/401/异常/取消各 1，POST 不计；61 秒后只输出 1 条窗口日志，累计计数跨窗保持单调；日志异常不影响同步。以上均为本地模拟 HTTP，不是生产 Gitee 请求。
- 既有 singleflight 回归：`tests/GiteeReadFlightVerification` 退出码 0，64 个并发逻辑读取只触发 1 次底层请求；测试记录 networkFlights=7、sharedWaiters=65。
- Release 主程序编译成功（退出码 0，既有其他文件的可空性警告）；独立 `--dev --show` Canary 目录 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-httpmetrics-20261101-190431`，Yanzi.dll SHA-256 `3FDD6855FD083C6468BD0C88E3650A93F0D7216A9B199A5CB3525387662E6CC2`，测试 PID 19236，WM_NULL 50/50、最大 8ms；Canary 已关闭，正式版 PID 23788 前后保持不变。开发版不连接正式账户同步，因此不能宣称真实云端减量或双端一致性通过。
- 后续：提交/PR/CI/合并主线后，需以相同业务负载的真实日志窗口对比 Gitee `sentTotal` 增量、失败率、冲突重复与跨端更新/删除恢复；不能从模拟数值推断生产降幅。第一阶段仍为进行中。

### 2026-11-01 19:41 双端独立 Canary 安装证据（阶段一仍未验收完成）
主线 `a372d14` 的相同 `Yanzi.dll`（SHA-256 `BBEFB21FC46A0B8C223914C6D5870A4412F03EEDD2571CC735F14C796CD23CF6`）已在家里/办公室隔离安装；窗口消息心跳分别 100/100、100/100，P95 0.0952/0.2721 ms；正式版 PID 均未变化。办公室无全局 .NET Runtime，使用正式安装目录已有的 .NET 9.0.21 运行时修复独立启动（仅改 Canary）。Gitee HTTP 计数、singleflight、扩展索引批处理自动回归通过。`--dev` 禁用真实账户同步，因此真实 HTTP 降幅、家↔公司更新/冲突/删除恢复仍待实网验收，不能据此宣称阶段一完成。完整证据见 [qa-phase1-dual-canary-20261101.md](qa-phase1-dual-canary-20261101.md)。

### 2026-11-01 20:47 可复用的阶段一日志窗口诊断（不代表真实 HTTP 降幅）
- 新增 scripts/measure-phase1-log-window.ps1：对指定北京时间左闭右开窗口，流式只读统计正式版 Gitee 逻辑读取、FileSystemWatcher 原始事件/批处理、冲突关键词、个人同步日志和真实 HTTP 指标行数；只输出聚合 JSON，不输出原始路径、token 或正文。指标中的 sentTotal 只展示首末快照，不跨处理器重启直接相减。
- 新增 scripts/verify-phase1-log-window.ps1：临时目录合成日志，覆盖窗口边界、日志分类、HTTP 指标快照、敏感值不泄漏及非法窗口拒绝。PowerShell 5.1 本机测试通过；同一脚本在办公室隔离临时目录运行通过。
- 正式版同窗 20:25–20:35 样本：家里逻辑读取 744、extension-file-change 389、冲突关键词 126、Personal sync 406；办公室对应均 0（该时段无同步日志）。两端正式版 Yanzi.dll 哈希均与相同主线 Canary 不同，故缺失 HTTP sentTotal 指标，不能将 744:0 当成优化效果。详细原始计数见 qa-phase1-dual-canary-20261101.md。
- 本地 Release 编译成功，独立 --dev Canary 运行后 WM_NULL 30/30，最长 17.16ms；正式版 PID 23788 未变化，测试实例已关闭。CI 新增 Windows 运行的 fixture 回归，等待 PR 检查。
- 第一阶段仍需等安全发布/测试通道取得真实 Gitee 发送计数和双端新增、修改、冲突、删除恢复证据，未达阶段验收标准。
## 第二阶段：UI 主线程减负（未开始）

后台化目录签名/全量清单扫描，治理同步 Dispatcher.Invoke 与高频计时器。验收：仓库和小程序在同步压力下 UI 心跳/P95/P99 与故障复现对比。

## 第三阶段：存储与生命周期（未开始）

审计 DeploymentAgent 历史安装包（家里约 2.70GB/办公室约 2.87GB）及 WebView2 本地数据（家里约 1.49GB）；先落实必要回滚和用户数据保留策略，不得直接删除 IndexedDB/账号内容。

## 第四阶段：常驻资源与基线（未开始）

在冷启动、空闲 30 分钟、仓库搜索、批量云同步和多小程序并发下比较 CPU、私有内存、线程/句柄、UI 延迟分位数和对象释放情况。

## 自动化工作要求

每轮先确认北京时间 08:00–22:00；非工作窗口不访问或唤醒家里电脑。检查已合并与未合并修复，优先补缺失的本地安装和跨端测试；每轮推进可核验的修复或验收。每项按源码→编译→立即隔离本机安装 QA→回归→PR/CI→合并 main→回读验证。延迟的仅为正式 GitHub Release 发布，不是开发版安装验证。所有阶段验证通过后停用定时任务。

### 子任务 1F：Gitee HTTP 计数器跨同步周期存活及空闲结算（2026-11-02，北京时间夜间，待 QA）

- 发现：后台每轮可能新建 `PersonalSyncService` 和 Gitee backend，原 `GiteeHttpReadMetricsHandler` 只有经过 60 秒且出现**下一次 GET** 才输出窗口，短周期的真实 GET 会因实例重建而未被统计。
- 隔离分支：`fix/phase1-gitee-http-lifecycle-20261102`，草稿 PR #127（最新 CI run #370 / 41343134698，创建时尚未结束）。Gitee backend 改用进程内共享的专用 `HttpClient`，其 Handler/计数器跨同步周期持续存在，连接池复用；认证仍只随单次请求的 URL 或 JSON 正文发送，不放入共享默认请求头。计数器每分钟由唯一低频 Timer 主动结算；没有 GET/完成事件时不输出空窗口；跨窗口完成的请求不会被遗漏。
- 新增 `GiteeHttpReadMetricsVerification` 真实计时自动回归：一次短 GET 后不再发送新请求，100ms 计时窗口必须自动报告 sent=completed=1，后续空闲窗口不重复报告。原有状态分类、异常、取消、重试和跨窗口累计测试保留。
- 2026-11-02 夜间只允许远端代码/CI，不触碰家里或办公室电脑。**尚未取得本分支 CI 结果、Release 编译、双端独立 Canary 安装及实网请求数**；PR 不得在这些门禁完成前合并。
- 共享连接池安全边界：禁用 CookieContainer（`UseCookies=false`），设置 5 分钟连接寿命和 2 分钟空闲回收以允许 DNS 刷新；新 Windows CI 步骤执行 `tests/GiteeHttpReadMetricsVerification`，覆盖空闲计时器结算及跨窗口请求完成。
- 风险/后续：共享 HttpClient 的长期连接池与账号隔离、计时器退出释放、跨窗口完成数、两端实际同步一致性仍需真实验收；真实 HTTP 降幅必须与相同业务负载的对照组比较。PR #126 的日志解析改动独立进行，需待各自 QA 后按合并顺序回读主线，避免覆盖。
