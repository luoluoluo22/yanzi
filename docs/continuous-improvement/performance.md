# 燕子性能守护

状态：awaiting-production-feedback（1525e8f 已于 17:44:21 本机正式激活；初始正式启动有效观测 1 组，尚不足 P50/P95 结论）
本轮更新时间：2026-10-09 17:46:10 +08:00  
下一次最早检查时间：2026-10-09 23:44:21 +08:00（或候选安装后累计 10 个有效正式启动样本时提前检查）

目标：在不提高崩溃率和明显恶化首次功能打开体验的前提下，持续降低启动耗时与后台资源占用。

## 当前基线

### 启动
来源：2026-10-06 正式 Runtime 日志，18 个有效启动样本。

- Runtime 启动 P50：6284.5 ms
- Runtime 启动 P95：9042.8 ms
- 从启动到“Installed applications loaded” P50：1869.0 ms
- 从启动到“Installed applications loaded” P95：2616.4 ms
- “Installed applications loaded”之后到 SettingsWindow InitializeComponent 完成 P50：4359.5 ms
- 同阶段 P95：6336.1 ms

### 稳态资源快照
采样对象：当前正式版 Yanzi.Runtime / Yanzi。

- 5 秒 CPU 快照：两进程均约 0%
- Yanzi.Runtime：Working Set 约 864 MiB；Private 约 1749 MiB
- Yanzi Shell：Working Set 约 387 MiB；Private 约 297 MiB
- 10 秒磁盘 I/O 快照：
  - Yanzi Shell：读 705,931 B，写 1,481 B，20 次读 / 20 次写
  - Yanzi.Runtime：读 90,119 B，写 816,963 B，86 次读 / 42 次写
- 首次打开耗时：当前没有专门埋点，暂不建立伪精确基线；继续作为回归护栏。

以上资源数据只是单次稳态快照，不用于宣称趋势。

## 本轮观察

启动日志显示，应用目录扫描结束后仍长期存在约 4 秒空档。独立工作树加入临时分段计时后，在隔离 Runtime 中得到：

- 隔离 Runtime 启动锁 -> Shared Runtime ready：5057 ms
- MainWindow 构造：4679.8 ms
- InstalledApplicationCatalog 扫描：约 1092 ms
- 扫描后 383 个应用 CommandItem 构造增量：约 3272.6 ms

进一步确认：主要成本来自应用 CommandItem 构造时同步解析/提取图标。Runtime 本身不展示应用列表，却重复承担前台 Shell 才需要的应用图标解析。

### 被否决的假设

曾怀疑每个 CommandItem 重复执行 SearchUsageMemory.Load() 是主因。直接对正式 search-memory.json（7,484 B）做 383 次“读文件 + JSON 解析”基准，7 轮中位总成本只有 54.9 ms，不到当前启动 P50 的 1%。该实验已回滚，没有保留代码。

## 当前实验

实验目标：只消除后台 Runtime 不需要的已安装应用图标解析，不改变 Shell 的图标加载逻辑。

- 独立工作树：`F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance`
- 分支：`ai-loop/performance`
- 本地提交：`1525e8f0a648d97fad1b9851d390015574365f25`
- 提交说明：`perf(runtime): skip installed app icon resolution`
- 正式 main：未合并
- 远端：未 push
- 正式版本：未发布

### 最小改动

`CommandItem` 新增可选的 `resolveIconSource` 参数，默认仍为 true。只有 `CreateInstalledApplicationCommands()` 在 `HostRuntimeProfile.IsRuntime` 时传 false。

因此：
- Runtime：保留应用命令元数据，但不解析 ImageSource / VectorIcon。
- Shell：默认值仍为 true，行为不变，仍正常加载应用图标。
- 没有改变用户数据格式、同步格式、应用扫描逻辑或正式 UI。

## 受控验证

### 性能
同一台机器、隔离 Runtime 数据目录、独立 IPC 管道：

基线：
- Shared Runtime ready：5057 ms
- 383 个应用对象扫描后构造增量：约 3272.6 ms

候选实验：
- Shared Runtime ready：1821 ms
- 383 个应用对象扫描后构造增量：约 109.7 ms
- 受控单次总启动改善约 64%
- 构造段改善约 96.6%

移除诊断埋点后的最终候选版 3 次隔离启动：
- 1809 ms
- 1789 ms
- 1797 ms
- 中位数：1797 ms

候选版隔离 Runtime 就绪时内存：
- Working Set：170.9–176.6 MiB
- Private：101.6–109.5 MiB

注意：没有同条件的“基线内存”样本，所以本轮不宣称内存改善，只记录候选值。

### 构建 / 测试

- OpenQuickHost Release 构建：通过，0 error；14 个既有 warning，与本改动无关
- Runtime Profile Verification：通过
- Yanzi.Runtime Release 构建：通过，0 warning / 0 error
- Shared Runtime Verification：17 项检查全部通过
- Shell `--dev --tray` 冒烟：通过；启动到 SettingsWindow ready 5322 ms，与本轮前约 5.4 秒同量级

## 预期收益

正式环境预期优先改善 Runtime 启动关键路径，并使 Shell 更早获得可用 Runtime。保守目标：

- Runtime 启动 P50 至少改善 20%
- Runtime 启动 P95 不恶化
- Shell 首次打开耗时不恶化超过 10%
- 后台 CPU、内存、磁盘 I/O 任一关键指标不恶化超过 10%

受控实验显示的约 64% 改善不直接当作正式环境收益，必须等待真实样本验证。

## 回滚条件

满足反馈门槛后，出现任一条件则回滚 `1525e8f`：

1. 正式 Runtime 启动 P50 改善不足 10%；
2. Runtime 启动 P95 比基线恶化超过 10%；
3. Shell 首次打开、后台 CPU、内存或磁盘 I/O 任一关键指标恶化超过 10%；
4. 任何 Runtime 能力、应用命令、Shell/Runtime 协作出现与缺失图标源相关的功能回归；
5. 出现新的崩溃或启动失败。

## 反馈窗口

截至 2026-10-08 15:14 +08:00，当前实验仍仅存在于独立工作树；再次核验，提交 `1525e8f0a648d97fad1b9851d390015574365f25` 不是 `main` 的祖先，`ai-loop/performance` 工作树干净，未发现实验被合并、push 或发布。因此正式环境的“实验后真实样本”仍为 0。

- 默认反馈门槛：实验进入可运行版本后，至少等待 6 小时，或累计 10 个有效实验后真实启动样本。
- 当前尚未部署，不能把自然经过的 6 小时当作反馈窗口完成。
- 本轮按规则只收集数据，不继续改代码。
- 下一次最早检查时间：2026-10-08 21:20:00 +08:00；若届时仍未部署，则仍只收集数据并顺延。若之后人工部署，反馈计时应从实际部署时刻重新开始；累计到 10 个有效实验后样本可提前复查。

## 本轮数据（2026-10-06 21:19）

本轮处于“等待真实反馈”，没有代码改动、没有构建、没有合并、没有发布。

### 部署/分支核验

- `main` 当前 HEAD：`7510000ced6228612fd58ca8cd367a5cf331b822`
- `git merge-base --is-ancestor 1525e8f... main` 返回 1：候选实验未进入 `main`
- 独立工作树：`F:\\Desktop\\kaifa\\OpenQuickHost-ai-loops\\performance`
- 工作树分支：`ai-loop/performance`
- 工作树 HEAD：`1525e8f0a648d97fad1b9851d390015574365f25`
- 工作树状态：干净

### 新的正式环境观察样本

20:16:56 的正式 Shell 启动日志新增 1 个未应用实验的对照样本：

- `TryAcquirePrimaryInstance`：20:16:56.443
- `Installed applications loaded`：20:16:58.343，耗时约 1900 ms
- `SettingsWindow InitializeComponent completed`：20:17:02.927
- 该次 Shell 关键启动段约 6484 ms
- 应用扫描完成后到 SettingsWindow 初始化完成约 4584 ms

该样本与既有正式基线 P50 6284.5 ms / 扫描后阶段 P50 4359.5 ms 同量级，进一步说明当前正式运行版本没有体现候选实验的约 1.8 秒受控结果；它不是实验后样本，不能用于判断候选收益。

### 稳态资源快照

21:18 左右对当前正式进程做短时采样：

- Yanzi.Runtime（PID 11468）：1 秒 CPU 约 0%；Working Set 约 659 MiB；Private 约 1331 MiB
- Yanzi Shell（PID 1892）：1 秒 CPU 约 0%；Working Set 约 394 MiB；Private 约 302 MiB
- 5 秒 I/O 增量：
  - Runtime：读约 2.4 KiB，写约 684 KiB，CPU 累计约 46.9 ms
  - Shell：读约 683.5 KiB，写约 0.5 KiB，CPU 累计约 0 ms

这些仍只是短时快照；由于没有同条件的实验版本真实样本，本轮不据此宣称趋势，也不触发新的优化假设。

### 本轮结论

反馈门槛没有满足。原因不是“时间未满”，而是候选实验从未进入可运行正式版本，因此有效实验后样本仍为 0。按约束，本轮停止在“等待真实反馈”，不处理第二个性能瓶颈。

## 实验历史

### 2026-10-06 第二轮（21:19）
观察 → 核验实验未进入 `main`；正式环境新增 1 个约 6.48 s 的启动对照样本，并采集 CPU/内存/I/O 短时快照。  
假设 → 不提出新假设；现有候选仍需真实部署后验证。  
最小改动 → 无代码改动，仅更新状态文件。  
验证 → `main` 不包含 `1525e8f`，性能工作树干净；有效实验后样本仍为 0。  
下一步 → 最早 2026-10-07 03:19:20 +08:00 再检查；若仍未部署则继续只收集数据。

### 2026-10-06 首轮
观察 → 锁定 Runtime 启动关键路径；排除 SearchUsageMemory 重复读取假设；定位到 Runtime 同步解析 383 个前台应用图标。  
假设 → Runtime 不展示应用列表，跳过其应用图标解析可减少约 3 秒关键路径，且不影响 Shell 首次打开。  
最小改动 → 仅 Runtime 已安装应用命令关闭图标解析。  
验证 → 受控 Runtime 约 5.06 s 降至约 1.80 s；构建与 17 项 Runtime 验证通过；Shell 冒烟无明显回归。  
下一步 → 等待真实反馈，不继续处理第二个性能瓶颈。


### 2026-10-07 第三轮（08:46）

观察 → 状态仍为“等待真实反馈”。再次核验候选提交 `1525e8f0a648d97fad1b9851d390015574365f25` 不在当前 `main`（`7510000ced6228612fd58ca8cd367a5cf331b822`）祖先链上；独立工作树 `F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance` 仍位于 `ai-loop/performance`，HEAD 仍为该候选提交且工作树干净。当前正式进程来自 `C:\Users\Administrator\AppData\Local\YanziRuntime\versions\20261006-201631-255\Yanzi.Runtime.exe` 与对应 Shell 目录，因此本轮仍没有实验后真实样本。

假设 → 不提出新性能假设，不处理第二个瓶颈。现有唯一候选仍是“Runtime 跳过不需要的已安装应用图标解析”，必须先进入可运行版本后再判断真实收益。

最小改动 → 无代码改动；未构建、未测试、未合并、未 push、未发布。仅采集正式环境数据并更新本状态文件。

验证 / 本轮数据：

- 2026-10-07 08:00 冷启动/系统开机时段新增正式对照样本（未应用候选实验）：
  - Runtime：`TryAcquirePrimaryInstance=True` 08:00:37.780 → `Installed applications loaded` 08:00:40.389，约 2609 ms；→ `SettingsWindow InitializeComponent completed` 08:00:46.855，关键段约 9075 ms；扫描后阶段约 6466 ms。
  - Shell：`TryAcquirePrimaryInstance=True` 08:00:34.413 → `Installed applications loaded` 08:00:48.119，约 13706 ms；→ `SettingsWindow InitializeComponent completed` 08:00:52.282，关键段约 17869 ms；扫描后阶段约 4163 ms。
  - 该样本发生在系统开机时段，启动竞争明显，不纳入既有常态 P50/P95，也不用于判断候选实验收益。
- 08:46 左右正式进程 1 秒 CPU：
  - Yanzi.Runtime（PID 17180）：约 0%；Working Set 395,923,456 B（约 377.6 MiB）；Private 1,789,964,288 B（约 1707.0 MiB）。
  - Yanzi Shell（PID 5996）：约 0%；Working Set 418,058,240 B（约 398.7 MiB）；Private 321,683,456 B（约 306.8 MiB）。
- 同时段 5 秒 I/O 增量：
  - Runtime：读 65,845 B，写 704,488 B，63 次读 / 15 次写。
  - Shell：读 703,773 B，写 835 B，12 次读 / 12 次写。
- 这些资源数据仍只是短时正式版快照，没有同条件候选版本真实样本，因此不触发收益/回滚判断。

结论 → 反馈门槛仍未满足：候选实验尚未部署，有效实验后样本仍为 0。按长期改进规则，本轮到此停止，不改代码。当前受控实验基线、预期收益与回滚条件保持不变。

下一次最早检查时间 → 2026-10-07 14:46:57 +08:00。若届时候选仍未进入可运行版本，继续只收集数据并顺延；若在此之前人工部署，则反馈窗口从实际部署时刻重新开始，达到 10 个有效实验后启动样本可提前复查，否则至少等待部署后 6 小时。

### 2026-10-08 第四轮（15:14，仅读取与采样）

观察 → 上轮状态为“等待真实反馈”。当前 `main` HEAD 为 `8a4b6327412e36f6f5cd000d2482f14fde8584eb`；候选提交 `1525e8f0a648d97fad1b9851d390015574365f25` 仍不是 `main` 的祖先。当前 `main` 提交版本的 `MainWindow.xaml.cs` 没有候选的 `resolveIconSource` 参数及 Runtime 跳过图标解析的条件逻辑，而候选提交保留这些逻辑。独立工作树 `F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance` 位于 `ai-loop/performance`，HEAD 仍为该候选提交，状态干净。主工作树另有大量其他任务的未提交及未跟踪文件，均未触碰。

部署核验 → 当前运行目录已更新至 `C:\Users\Administrator\AppData\Local\YanziRuntime\versions\20261008-113304-007\Yanzi.Runtime.exe`，对应 Shell 路径为 `...\shells\20261008-113304-007\Yanzi.exe`。版本更新不等于候选优化已部署；本轮未取得已运行二进制包含该优化的证据。因此有效“候选实验后”启动样本按 **0** 计，不能启动六小时真实反馈倒计时，也不计算候选收益。

本轮正式版启动观察（来自 `runtime.log` 与 `shell.log`；时间为本地 +08:00，所示为“主实例锁成功”到 SettingsWindow InitializeComponent 完成的关键段，不等同完整用户可交互启动）：

- 10 月 8 日 11:33：Runtime 11:33:06.818 → 11:33:15.545，**8727 ms**；已安装应用完成时 11:33:09.612，锁到扫描完成 **2794 ms**，扫描后阶段 **5933 ms**。Shell 11:33:14.917 → 11:33:21.113，**6196 ms**。
- 10 月 8 日 15:06：Runtime 15:06:45.186 → 15:06:54.108，**8922 ms**；已安装应用完成时 15:06:47.731，扫描完成阶段 **2545 ms**，扫描后阶段 **6377 ms**。Shell 15:06:41.906 → 15:06:59.316，**17410 ms**；其应用扫描阶段达 **13293 ms**，存在明显竞争/干扰可能，不能简单归因于同一瓶颈。
- 09:22 的 Runtime **13420 ms** / Shell **23398 ms** 为当天另一组观测，但存在并发启动迹象，不能与稳态样本直接比较。15:06 的 Runtime 日志还出现一次 `TryAcquirePrimaryInstance=False`，不额外计为成功主实例启动。
- 样本量及条件不足，本轮不更新已有 Runtime 启动 P50/P95 基线（**6284.5 / 9042.8 ms，18 样本**），也不报告带伪精度的新分位数。首次功能打开耗时仍缺专门埋点。

15:13 采集的正式进程 5 秒资源快照（CPU 为占单个逻辑核心的近似百分比，并非全机 CPU 占比）：

- `Yanzi.Runtime` PID 16920：CPU **1.25%**；Working Set **883.9 MiB**；Private **1751.4 MiB**；5 秒读 **2380 B** / 写 **36055 B**；读 **18 次** / 写 **15 次**。
- `Yanzi` Shell PID 16156：CPU **0.31%**；Working Set **389.2 MiB**；Private **297.7 MiB**；5 秒读 **3114 B** / 写 **325 B**；读 **4 次** / 写 **4 次**。
- 另有 `Yanzi.Runtime` PID 1180：CPU **0%**，Working Set **50.8 MiB**，Private **13.7 MiB**，5 秒磁盘 I/O 为 0。仅登记为待观察现象，不凭一次快照判断异常、处理第二个瓶颈或终止进程。
- 以上数据是短时快照，不支持推断资源趋势或与候选版比较收益。

假设 → 不提出新的性能假设；继续沿用上一轮唯一候选“Runtime 不必同步解析应用图标”。受控实验的基线、预期收益、构建/测试记录及超过 10% 的回归回滚条件全部保留，未推断正式环境改善已发生。

最小改动 → **无性能代码改动**。未创建或重置工作树，未构建、部署、提交、合并、push、发布、删除任何用户数据；仅更新本 `performance.md` 观测状态。

验证 → 通过 Git 祖先关系、两个工作树状态与源代码引用核验候选隔离状态；从正式 Runtime/Shell 日志取得新启动记录；采集进程 CPU、内存、I/O。由于未改代码，本轮构建/测试不适用；不以旧版构建通过替代真实反馈验收。

结论 → **继续等待真实反馈，候选实验后有效样本为 0；本轮不处理新瓶颈**。当前运行版本虽更新，仍未确认候选改动真正进入该运行版本。

下一次最早检查时间 → **2026-10-08 21:20:00 +08:00**。届时先核验候选是否被实际部署；如仍未部署，只追加观测并顺延。若经人工确认已部署，从实际部署时间起计至少 6 小时，或累计 10 个有效候选后启动样本，再比较 Runtime 启动 P50/P95、Shell 首次打开、后台 CPU/内存/磁盘 I/O；收益不足或关键副作用超过 10% 时按既定回滚条件处置。不得仅因 6 小时自然流逝就宣称候选通过。


### 2026-10-09 第五轮（07:58，等待真实反馈）

观察 → 状态文件显示仍“等待真实反馈”；上一轮唯一候选尚未确认部署，实验后有效样本为 0。本轮严格停止在数据采集和记录，不启动新的性能实验。主工作树当前存在其他任务的未提交/未跟踪文件，均不触碰。

部署 / 隔离核验 → 当前 main HEAD 为 c759896d84bda0bb5f8a298091d89bf7216f74b1；git merge-base --is-ancestor 1525e8f0a648d97fad1b9851d390015574365f25 main 返回 1，候选仍不是 main 祖先。独立工作树 F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance 分支 ai-loop/performance，HEAD 仍为 1525e8f0a648d97fad1b9851d390015574365f25，状态干净。main 源码没有候选 resolveIconSource 跳过逻辑，候选分支有。当前正式进程路径仍指向 YanziRuntime\versions\20261008-113304-007\Yanzi.Runtime.exe 与相应 shells\20261008-113304-007\Yanzi.exe。未取得该已运行二进制确实包含实验的证据，因此“已确认实验后有效样本”仍为 **0 个**；不能因为从上轮经过了六小时就视为反馈达标。

新增正式启动观测（本地 +08:00，2026-10-09 07:36–07:37，关键段仅为主实例锁成功至 SettingsWindow InitializeComponent 完成，不等于完整可交互启动）：

- Runtime：07:36:53.024 → 07:37:02.148，关键段 **9124 ms**；07:36:55.607 完成 Installed applications loaded，扫描前段 **2583 ms**、扫描后 **6541 ms**。07:37:02.056 另有 TryAcquirePrimaryInstance=False，不另计成功启动。
- Shell：07:36:49.769 → 07:37:07.297，关键段 **17528 ms**；07:37:03.210 完成 Installed applications loaded，扫描前段 **13441 ms**、扫描后 **4087 ms**。
- 该次发生在系统开机/多进程竞争时段，不更新此前 18 个有效常态样本所建立的 Runtime P50 **6284.5 ms** / P95 **9042.8 ms**，也不作为候选版反馈样本。首次功能打开缺少独立埋点，仍无可比较数字。

正式环境 2026-10-09 07:57:20 左右 5 秒 CPU/内存/磁盘 I/O 快照（CPU 按单个逻辑核心计算）：

- Yanzi.Runtime PID **17244**：CPU **0.94%**；Working Set **1181.8 MiB**，Private **1946.5 MiB**；5 秒读 **2051 B**、写 **33437 B**，14 次读、11 次写。
- Yanzi Shell PID **900**：CPU **0%**；Working Set **391.9 MiB**，Private **297.6 MiB**；5 秒读 **3610 B**、写 **321 B**，4 次读、4 次写。
- 另有 Yanzi.Runtime PID **18368**：CPU **0%**，Working Set **51.4 MiB**，Private **13.6 MiB**，5 秒 I/O 为 0；仅登记，不在本轮处理第二个潜在瓶颈或终止该进程。
- 采样时全机 CPU 约 **12.05%**、可用内存约 **3.59 GiB**（单次系统快照）。以上均为短时采样，不得当作跨版本资源趋势或用作副作用百分比判断。

假设 → 不提出新的性能假设，继续沿用唯一候选“仅在后台 Runtime 中不解析已安装应用图标”。保留候选受控实验旧数据：基线 Runtime ready 5057 ms / 构造段 3272.6 ms；候选 Runtime ready 1821 ms / 构造段 109.7 ms，去诊断埋点后三次隔离启动中位数 1797 ms。旧受控结果不能替代实验后真实反馈。

最小改动 → **无性能代码改动**，仅更新本文件。未创建/重置/clean 任何工作树，未构建、合并、部署、push、发布，未删除任何用户数据。候选旧版 Release 构建和 17 项 Shared Runtime 检查、Shell 冒烟验证记录保持不变；本轮没有代码变化，因此没有新增构建/测试。

预期收益 / 回滚条件 → 沿用当前唯一候选的预期：正式 Runtime 启动 P50 争取至少改善 **20%**，P95 不恶化，Shell 首次打开及后台 CPU/内存/磁盘 I/O 不恶化超过 **10%**。正式反馈门槛满足后，若 P50 改善不足 **10%**、任一关键副作用恶化超过 **10%**、出现崩溃/启动失败或命令与 Shell/Runtime 协作回归，则回滚候选；**目前尚未达到可判定收益或触发回滚的实验后证据门槛**。

验证 / 结论 → 已核对 Git 祖先关系、隔离工作树、源码差异、已运行二进制的路径、Runtime/Shell 正式日志和 5 秒进程资源采样。**实验后有效样本 0，继续等待真实反馈；本轮只观察，不处理第二个性能瓶颈。**

下一次最早检查时间 → **2026-10-09 14:00:00 +08:00**（本轮后超过 6 小时；不是自动预约的任务）。届时先核查候选是否真实部署；若没有，仍只能收集数据并顺延。如果人工部署，反馈计时以**实际部署时刻**为起点，满足 **10 个有效候选后启动样本**可提前复查，否则至少等待实际部署后 **6 小时**。自然经过六小时不等于候选实验已经得到验证。

### 2026-10-09 第六轮（14:26，仅观察与采样）

观察 → 状态仍为“等待真实反馈”，唯一候选提交 `1525e8f0a648d97fad1b9851d390015574365f25` 尚未进入当前运行版本，故本轮严格只收集数据，不进行新性能代码实验，也不转向第二个瓶颈。

部署及工作树核验 → 14:23 +08:00 `main` HEAD `99a1a46cdc358fec4e3737fb0e85484f27c33039`；候选不是 main 祖先（merge-base --is-ancestor 返回 1）；main 源码没有候选 `resolveIconSource` 逻辑。独立工作树 `F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance` 为 `ai-loop/performance`，HEAD `1525e8f...`，状态干净。主工作树存在其他任务的未提交和未跟踪文件，本轮均未修改、reset、clean 或覆盖。

运行版本核验 → Runtime PID 11728，路径 `C:\Users\Administrator\AppData\Local\YanziRuntime\versions\20261009-141228-917\Yanzi.Runtime.exe`，14:12:38 启动；Shell PID 20364，`...\shells\20261009-141228-917\Yanzi.exe`，14:12:54 启动。当前 Runtime 可执行文件 ProductVersion 为 `1.0.0+99a1a46cdc358fec4e3737fb0e85484f27c33039`，与没有候选优化的 main 完全一致。**当前运行的不是候选实验版，确认有效实验后启动样本仍为 0**；未开始实际部署后的六小时反馈窗口。

正式启动观察（2026-10-09，本地 +08:00；关键段为主实例锁成功至 SettingsWindow InitializeComponent 完成，不等于完整可交互启动）：

- Runtime：14:12:38.987 → 14:12:44.696，**5709 ms**；14:12:40.698 完成应用加载，前段 **1711 ms**，后段 **3998 ms**。
- Shell：14:12:54.257 → 14:13:00.276，**6019 ms**；14:12:56.363 完成应用加载，前段 **2106 ms**，后段 **3913 ms**。
- 日志来自 `%LOCALAPPDATA%\OpenQuickHost\logs\runtime.log` 和 `shell.log`。它们是**未部署候选**的正式对照观察，不加入候选反馈。既有 18 个有效常态样本的 Runtime 启动 P50 **6284.5 ms** / P95 **9042.8 ms** 基线不变；首次功能打开仍无独立埋点。另观察到远程图标 SSL 下载失败日志，不在本轮另立优化课题。

14:25:27 +08:00 的正式进程五秒资源快照（CPU 为约占单个逻辑核心比例）：

- Runtime PID 11728：CPU **2.19%**，Working Set **877.6 MiB**，Private **1703.9 MiB**；读 **325 B** / 写 **33169 B**，4 次读 / 10 次写。
- Shell PID 20364：CPU **0.94%**，Working Set **390.7 MiB**，Private **297.6 MiB**；读 **3436 B** / 写 **325 B**，4 次读 / 4 次写。
- 全机可用物理内存约 **3.17 GiB**，总计 **15.84 GiB**。单次短窗口只能反映快照，不作为跨版本资源趋势和收益判断依据。

假设 → 不新设假设，继续等待唯一候选“Runtime 跳过不需要的已安装应用图标解析”。保留旧受控实验基线 Runtime ready **5057 ms**，候选 **1821 ms**（移除埋点后 3 次中位数 **1797 ms**），不得代替真实反馈。

最小改动 → **无性能代码变更**，仅追加本文件并更新顶部状态时间。不创建或更新工作树，不构建、合并、push、发布或删除数据。

验证与回滚 → 已核验 Git 祖先与两工作树、在运行的可执行文件版本标识、启动日志和 CPU/内存/I/O 快照。无代码改动，故本轮新增构建/测试不适用；此前 Release 构建和 17 项 Runtime 校验通过的记录维持。候选预期 Runtime P50 至少改善 **20%**、P95 不恶化，首次功能打开与 CPU/内存/磁盘 I/O 不恶化超过 **10%**。实际部署并达到样本门槛后，若 Runtime P50 改善不足 **10%**、任何关键副作用恶化超过 **10%**、出现崩溃/启动失败或功能回归，则按已有回滚条件撤销实验；当前没有判定收益或回滚的有效候选后数据。

结论 → **继续等待真实反馈；实验后有效样本 0；不继续改代码。**

下一次最早检查时间 → **2026-10-09 20:30:00 +08:00**（只记录建议，不创建自动任务）。下次先核验实际部署；若仍未部署，仍只采集数据并顺延。若此后人工部署，从实际部署时刻计至少 **6 小时**，或累计 **10 个有效实验后启动样本**后才能判断收益。自然经过六小时不等于自动通过验收。


### 2026-10-09 第七轮（17:19，仅采样；早于此前设定的门槛）

**观察**：已先阅读生产部署政策及性能状态文件。上轮状态为等待反馈，候选未安装，当前 17:19 早于 20:30 最早检查时间且实验后有效样本不足。因此本轮仅做只读核验和状态归档，不进行性能代码改动、构建、安装或发布。把原来容易误导的“等待真实反馈”更正为 awaiting-integration：反馈计时只能在真实候选部署后开始。

**源码／版本**：main HEAD 为 ff89bb055587ca300a6a8c22ab19c48f47ec8fcc；旧候选 1525e8f0a648d97fad1b9851d390015574365f25 仍不是 main 祖先。独立工作树 F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance 位于 ai-loop/performance，HEAD 仍为该候选，状态干净。主工作树同时有大量其他任务的未提交改动，尤其 src/OpenQuickHost/MainWindow.xaml.cs 有 8 行新增和 1 行删除。本轮未修改其他文件，也未 reset/clean。旧候选仅改此一个源文件，向 CommandItem 增加默认 resolveIconSource=true 参数，安装应用命令传入 !HostRuntimeProfile.IsRuntime，跳过后台不必要的图标解析；当前 main 源文件缺少这一实现。只读比较表明修改范围有限，但尚无基于 1.0.12 的移植、隔离基准或完整回归证据，故不能认定移植安全。

**当前真实运行**：Runtime PID 24512，路径 C:\Users\Administrator\AppData\Local\YanziRuntime\versions\20261009-164545-661\Yanzi.Runtime.exe，ProductVersion=1.0.0+ff89bb055587ca300a6a8c22ab19c48f47ec8fcc；Shell PID 5220，路径 C:\Users\Administrator\AppData\Local\YanziRuntime\shells\20261009-164545-661\Yanzi.exe，ProductVersion=1.0.12+ff89bb055587ca300a6a8c22ab19c48f47ec8fcc。缺乏安装二进制包含该旧候选的证据，所以有效候选后启动样本为 0。

**新正式启动对照日志**（本地 2026-10-09 16:45–16:46，非候选）：Runtime 主实例锁 16:45:56.335、应用加载 16:45:58.320、SettingsWindow 初始化 16:46:02.403；关键段 6068 ms，前段 1985 ms，后段 4083 ms。Shell 锁 16:46:11.440、应用加载 16:46:13.626、设置初始化 16:46:17.602；关键段 6162 ms，前段 2186 ms，后段 3976 ms。该段不等于完整可交互启动。既有 18 样本基线 Runtime P50=6284.5 ms、P95=9042.8 ms 保持不变；首次功能打开尚缺独立埋点。

**5 秒正式进程资源快照**（约 17:18:00 至 17:18:05，CPU 为单核近似比例）：Runtime PID 24512：CPU 2.76%，Working Set 530.3 MiB、Private 754.1 MiB，读 214926 B／写 750341 B，读 89／写 18 次。Shell PID 5220：CPU 0.92%，Working Set 388.4 MiB、Private 295.6 MiB，读 720604 B／写 835 B，读 12／写 12 次。可用系统内存约 1.99 GiB。单次快照不推断趋势、改善百分比或回滚依据。

**假设**：仅保留旧候选“后台 Runtime 应跳过前台应用图标解析”，旧隔离 ready 5057 ms 降至 1821 ms（去埋点三次中位数 1797 ms）仍是待复核历史证据。预期正式启动 P50 改善至少 20%，P95 不恶化；Shell 首次打开、CPU、内存与磁盘 I/O 任一不恶化超过 10%。

**最小改动与验证**：仅更新本 performance.md 状态和观测记录，性能源码改动 0。Git 祖先关系／分支状态、旧补丁与现有源文件、在运行文件的 ProductVersion、日志及 5 秒资源采样均已核验。本轮无新增构建、单测或正式业务冒烟（未做代码改动、部署或重启）。旧候选原有 Release 编译与 17 项 Shared Runtime 回归仅是历史记录，不能代替 1.0.12 验收。

**部署与公共发布门禁**：deploymentStatus=blocked/awaiting-integration；原因是移植、隔离基准、当前 1.0.12 回归和同文件脏改动的安全处理均未完成。releaseStatus=blocked；原因是尚无已审查的隔离 clean commit、同源安装包、本机部署及真实反馈；不可直接发布 GitHub。deploymentAttempted=false，installedVersion=现有 Shell 1.0.12+ff89bb0、Runtime 1.0.0+ff89bb0；backupLocation=null；smokePassed=null（未部署）；realWorldSamples=0；rollbackTriggered=false。

**回滚门槛**：待以后真实部署且有足够反馈，若 Runtime P50 改善不足 10%、P95 或 Shell 首次打开或 CPU／内存／I/O 任一恶化超过 10%、出现崩溃／功能回归，立即按部署备份回滚；本轮尚无新增安装需要回滚。

**下一次最早检查时间**：2026-10-09 20:30:00 +08:00，维持上一轮既定门槛，不因本次提前检查而缩短间隔。届时仅当源码状态允许时，才在指定独立工作树上为 1.0.12 定向移植旧候选、采集同条件隔离基准、运行回归；达标后方可考虑本机受控安装。真实样本门槛为从候选实际安装起至少 6 小时或至少 10 个有效候选后启动样本，未安装不能计时。

**回传字段**：taskId=performance-guard；changeId=perf-observation-20261009-1719；changedFiles=[docs/continuous-improvement/performance.md]；baseRevision=ff89bb055587ca300a6a8c22ab19c48f47ec8fcc；tests=git-status/version/log/proc-snapshot read-only passed, build not run；nextEligibleAt=2026-10-09T20:30:00+08:00。

### 2026-10-09 第八轮（17:36，修复未部署死锁：隔离移植/回归/受控预置）

**观察 / 现行优先级**：本轮执行用户最新的“先修复未部署死锁”指令，先阅读生产部署政策，不沿用前轮“因无真实样本而不移植”的循环限制。主工作树 HEAD 为 `ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`，有其他任务的大量未提交修改；当前正式进程仍在 `20261009-164545-661` 快照，Shell ProductVersion=`1.0.12+ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`，Runtime ProductVersion=`1.0.0+ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`。Git HEAD 原始 csproj 仍是 1.0.10，不可直接拿 clean HEAD 替换 1.0.12。

**假设**：保留唯一性能问题，即后台 Runtime 不需要为 360 个已安装应用（另含 2 个占位应用）同步解析 Shell 图标。历史性能补丁 `1525e8f` 为证据来源。

**最小性能改动**：独立工作树 `F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance` 新建分支 `ai-loop/performance-v1_0_12` 于当前 main HEAD；明确列出并逐文件 SHA-256 核验 50 个现有脏主工作树 src 文件，把其内容复制到隔离树仅为保持 1.0.12 已有能力，非新增性能功能、绝无全目录覆盖。随后仅在 `src/OpenQuickHost/MainWindow.xaml.cs` 应用原候选：构造函数可选 `resolveIconSource=true`，仅 Runtime 安装应用命令传 `!HostRuntimeProfile.IsRuntime`，Shell 默认不变。新增 `src/Yanzi.RuntimeProfileVerification/Program.cs` 正反两路 iconSourceOverride 自动断言。主工作树代码完全未改、未执行 git reset/clean/git add .。隔离树其它 uncommitted 源文件不满足公开发布 clean/review 门禁。

**隔离基线 / 实验**：同一机器、独立 Yanzi Runtime Verification 临时目录和 RPC 管道，基线 1.0.12 22 项生命周期检查通过；应用加载到 Settings 初始化单次 `4710 ms`。候选后此阶段 3 次 `919/880/900 ms`，中位数 `900 ms`，360 已安装应用和 2 占位应用均一致。该指标不是完整冷启动，也非正式 P50/P95。Shell/Runtime Release 构建通过，RuntimeProfile（含新增图标开关回归）通过，候选 Shared Runtime 22 项检查×3 通过，SyncVerification 完整回归通过（用户数据/凭证隔离保护），该最小改动文件的 `git diff --check` 为 0。全量 git diff --check 另显示当前主树带来的 `src/OpenQuickHost/Sync/LocalExtensionCatalog.cs:1674 new blank line at EOF`（未触碰），公开发布仍需独立处理审查门禁。

**预置/安全**：使用 `scripts/install-shared-runtime.ps1 -SkipBuild -ProjectRoot <性能独立工作树>`（不含 -Activate）创建受控快照：Runtime `C:\Users\Administrator\AppData\Local\YanziRuntime\versions\20261009-173450-222\Yanzi.Runtime.exe`；Shell `C:\Users\Administrator\AppData\Local\YanziRuntime\shells\20261009-173450-222\Yanzi.exe`。脚本证实 `RUNTIME_POINTER_UNCHANGED=True`，指针仍指向旧正式快照 `20261009-164545-661`（可回滚备份）。候选 Yanzi.dll SHA256=`3EA8256B327964F7660CE5CEF22ED77E1AE3468015E3796A1C936ED9B345B358`，与运行版二进制不同，但文件版本均报 1.0.12+ff89bb；正式后必须以 PID、真实加载路径和此 hash 复核补丁，而非仅比较 ProductVersion。

**当前硬阻断**：2026-10-09 17:36 +08:00，ChatGPT Bridge Job `aa3c8040-ddfc-4c3e-94b8-b879b209e0a6` 仍为 `running`，诊断 `in_progress/task_not_terminal`；不能中断该 Job 对应正式 Runtime/Shell。Windows 最近交互检测显示用户键鼠已闲置 4375 秒，故唯一明确部署阻断为活跃 Job。不得强制终止任务或绕过串行安装锁。Job 转终态后立即重新核验占用与源 SHA，再使用带 -Activate 的受控本机安装，健康/心跳/业务冒烟失败则从原位置恢复。该阻断与“没有安装后样本”无关。

**当前回传**：taskId=performance-guard；changeId=perf-icon-1525e8f-on-1.0.12-20261009；changedFiles=[src/OpenQuickHost/MainWindow.xaml.cs, src/Yanzi.RuntimeProfileVerification/Program.cs, docs/continuous-improvement/performance.md]，兼容性隔离快照另含 50 个未提交现有源码文件；baseRevision=ff89bb055587ca300a6a8c22ab19c48f47ec8fcc；tests=Release/RuntimeProfile/SharedRuntime22x3/SyncVerification passed；deploymentAttempted=true（仅安全预置，不是正式激活）；deploymentStatus=staged-blocked-active-job；installedVersion=旧正式 Shell 1.0.12+ff89bb / Runtime 1.0.0+ff89bb；backupLocation=YanziRuntime\versions 和 shells 的 `20261009-164545-661`；smokePassed=isolated=true/production=null；realWorldSamples=0；rollbackTriggered=false；releaseStatus=blocked；releaseBlocker=尚未受控激活及真实反馈≥6小时或≥10有效实验后样本，隔离 Git 非 clean/review commit、同源新版 Windows 安装包与 release notes 未生成；nextEligibleAt=2026-10-09T17:40:00+08:00（仅供检查，须以活跃 Job 结束为条件，并未登记自动任务）。若安装成功，反馈计时应改以实际 activatedAt 为起点。


### 2026-10-09 第八轮最终部署验收补充（17:46；替代本轮此前的 staged-blocked 状态）

**新事实（实际部署）**：17:39 Bridge Job `aa3c8040-ddfc-4c3e-94b8-b879b209e0a6` 已以 `error` 收尾，未重新派发该无法确认已被 ChatGPT 接受的任务；17:39:44 和部署前重复检查 `ACTIVE_BRIDGE_JOBS=0`。本机无人键鼠交互，使用 `F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance\.artifacts\performance\activate-reviewed.ps1` 调用 `scripts/install-shared-runtime.ps1 -SkipBuild -Activate -ProjectRoot F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance`，**2026-10-09T17:44:21.0431702+08:00 已在正式运行环境成功激活**，本轮不再是未部署或 waiting-for-integration。不得将先前本轮笔记中的 staged-blocked 当作当前状态。

**真实安装、进程、补丁证明**：正式 `runtime.json` 指针现指向 `C:\Users\Administrator\AppData\Local\YanziRuntime\versions\20261009-174418-888\Yanzi.Runtime.exe`；Shell 路径为 `C:\Users\Administrator\AppData\Local\YanziRuntime\shells\20261009-174418-888\Yanzi.exe`。Runtime PID 22416，Shell PID 4856；Shell ProductVersion=`1.0.12+ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`，Runtime ProductVersion=`1.0.0+ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`。原版和候选版共享原项目的文件版本元数据，故通过进程路径、实际 DLL SHA256=`3EA8256B327964F7660CE5CEF22ED77E1AE3468015E3796A1C936ED9B345B358`（与独立 Release 构建一致，与原正式版不同）、启动心跳与进程 PID 证明真实生效。**版本号相同不代表二进制未变化**。

**备份及回滚**：上线前保存当前运行指针文件 `F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance\.artifacts\performance\runtime-pointer-before-activate-20261009-174418.json`；原正式可执行快照保留在 `YanziRuntime\versions\20261009-164545-661\Yanzi.Runtime.exe` 及 `YanziRuntime\shells\20261009-164545-661\Yanzi.exe`；新发布本机快照另存于 `20261009-174418-888`。部署脚本安装健康失败时会切回旧版，外围脚本还复核真正运行路径及 DLL 哈希、Runtime 心跳、Shell attach 和关键小程序，不操作用户数据。本次 `rollbackTriggered=false`。

**生产冒烟**：`PRODUCTION_SMOKE_PASSED=True`；Runtime `backgroundServices.initialized=true`、`idleTrigger=true`、`idleTriggerObservedAt` 20 秒内新鲜、`agentApi=true`，Shell 新进程已 attach，11 个扩展实例已恢复；`clipboard-history` 和 `taskbar-calendar` 独立只读 `extension.status` 均 `success=true,isRunning=true,instanceCount=1`，燕子插件 `yanzi_ping` 返回 `ok=true`。未调用会改变用户记录的写接口。未进行整机重新开机，故本轮可给出进程真实启动日志，不可虚构“关机后冷启动/系统开机时长”。

**正式启动初样本（同机但不能用一个样本推断 P50/P95）**：正式 Runtime 日志 `2026-10-09 17:44:21.354` `TryAcquirePrimaryInstance=True` → `17:44:23.374` `Installed applications loaded` → `17:44:24.691` `SettingsWindow InitializeComponent completed`：主实例锁到初始化完成 **3337 ms**，其中图标/扫描后阶段 **1317 ms**，加载 360 个应用+2 个占位；部署前 16:45 版本同指标 **6068 ms**（扫描后 4083 ms）。第一条初样本单次约快 45%，不是正式 P50 改善结论。Shell `17:44:34.455` → `17:44:37.525` → `17:44:43.156`，主实例锁到初始化 **8701 ms**，加载后阶段 **5631 ms**，反而比部署前最近一次 Shell **6162 ms** 更慢约 41%；需高度关注非稳态重启竞争，等待不少于 6h 或≥10 有效生产启动样本后核查，不隐瞒负向观测；“首次打开具体小程序”另缺独立计时埋点，不用 Shell 启动代替首次打开指标。

**生产短期资源**：17:45 5 秒线程采样 Runtime PID22416 的 CPU 0%，Working Set约 651MiB、Private约1376MiB；Shell PID4856 CPU 0%，Working Set约375MiB、Private约284MiB。Runtime 17:45:53 的5s读约 78.6MB/写 4.6KB，是一条很高的短时读突发，17:46:08 重测5s读 **2279 B**、写 **33599 B**（回到部署前同量级），不能用一次突发断言持续高 I/O 或忽略后续观察；Shell 5s读3432 B/写323 B。采样时系统内存可用约1.06 GB，有多进程负载，不能把这组新旧非同条件瞬时内存对比当作长期趋势。没有达到样本门槛不能宣称正式 RAM/CPU 已改善。

**测试与代码**：保留上条笔记全部隔离构建/测试证据，并补充 Yanzi.CapabilityVerification Release 构建通过；任务取消/幂等 17 项、聊天40项、百度网盘19项均通过，变更只有性能源文件 `src/OpenQuickHost/MainWindow.xaml.cs` 及图标兼容回归 `src/Yanzi.RuntimeProfileVerification/Program.cs`；另外隔离快照 50 个现存1.0.12源文件是兼容上下文，绝不从脏 main 全量发布。主源码未提交、未覆盖，公开发布必须另行创建审阅后干净工作树与版本化安装包。

**门禁/回滚标准**：正式样本阈值为实际安装后的至少6小时或10个有效生产启动样本。若足量反馈显示 Runtime 启动 P50 改善不足10%、P95恶化>10%、Shell 首开或后台CPU/内存/磁盘I/O关键指标恶化>10%，或出现任何崩溃、图标业务缺失或运行回归，则切回留存旧版本，并审查问题。当前仅 **1 组生产启动初样本**、首次小程序打开未单独埋点；状态 `awaiting-production-feedback`；不是 blocked / 未部署，也不是已批准公开发布。

**本轮最终回传字段**：
- taskId=`performance-guard`，changeId=`perf-icon-1525e8f-on-1.0.12-20261009`，baseRevision=`ff89bb055587ca300a6a8c22ab19c48f47ec8fcc`。
- changedFiles=`src/OpenQuickHost/MainWindow.xaml.cs`、`src/Yanzi.RuntimeProfileVerification/Program.cs`、`docs/continuous-improvement/performance.md`（其他50文件仅已存在1.0.12源隔离兼容快照，不作为新性能改动）。
- tests=`release build passed; runtime profile/icon source skip assertions passed; shared runtime 22 checks×3 passed; SyncVerification passed; task 17/chat 40/baidu transfer19 passed; production process/heartbeat/extension/API smoke passed`。
- deploymentAttempted=`true`，deploymentStatus=`installed`，installedVersion=`Shell 1.0.12+ff89bb0; Runtime 1.0.0+ff89bb0; snapshot 20261009-174418-888; Yanzi.dll SHA256=3EA8256B327964F7660CE5CEF22ED77E1AE3468015E3796A1C936ED9B345B358`。
- backupLocation=`F:\Desktop\kaifa\OpenQuickHost-ai-loops\performance\.artifacts\performance\runtime-pointer-before-activate-20261009-174418.json` + `%LOCALAPPDATA%\YanziRuntime\versions\20261009-164545-661` / `shells\20261009-164545-661`；smokePassed=`true`；realWorldSamples=`1 pair of production startup traces`；rollbackTriggered=`false`。
- releaseStatus=`blocked`，releaseBlocker=`尚未达6h/10次真实启动反馈门槛，Shell单次启动疑似回归待核验；无clean reviewed release commit、新版本同源Windows installer及release notes，故未 dry-run/-Apply 公共发布脚本，未push/上架`。
- nextEligibleAt=`2026-10-09T23:44:21+08:00`，或更早集齐 10 个有效生产启动样本；到时复核 Runtime P50/P95、Shell 首开、CPU/RAM/I/O、首次打开后决定正式归档/回滚/单项公开发布（不是已设置后台定时任务）。
