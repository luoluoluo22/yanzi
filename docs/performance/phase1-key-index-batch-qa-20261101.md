# 第一阶段：扩展数据目录索引批量去重（2026-11-01）

状态：**已完成源码修复与离线真实源码 QA；跨设备实际云同步 HTTP 减量仍待验收**。本文件与四阶段总台账配套，避免和并行的 PR #121 发生编辑冲突。

## 发现

`ExtensionStorageService.Recovery.cs` 已调用 `PublishExtensionDataKeysAsync(extensionId, indexKeys)`，其中 `indexKeys` 包含本地数据文件的完整索引；调用会对云端索引读取、合并，必要时写入并读取确认。随后每个本地二进制文件仍调用 `PublishExtensionBinaryAssetIfAbsentAsync`，后者在比较/发布二进制内容后又调用 `PublishExtensionDataKeysAsync(extensionId, [key])`，导致在一个恢复周期内按图片数量重复读取同一键索引。

## 修复

- 在 `PersonalSyncService.ExtensionDataCatalog.cs` 中保持已有公开方法的默认行为（单文件操作仍发布并确认对应的索引）；增加内部 `PublishPreIndexedExtensionBinaryAssetAsync`，专供已提前发布完整索引的批量恢复路径使用。
- 保留远端二进制内容比对与“不同则不覆盖”语义；仅省略成功的预索引批量文件在此之后的再次索引读取。
- `ReconcileCloudDataAsync` 在遍历本地资源前仍需成功发布全量 `indexKeys`，失败就中断此扩展的后续恢复，不得跳过确认。
- 添加日志计数 `binaryKeyIndexReadsAvoided`，记录此次恢复中成功的预索引资源个数（对应旧实现至少一次索引读取），便于新版真实网络运行时验证。

## 真实代码级测试（不需要凭证）

`dotnet run --project tests/ExtensionDataIndexBatchVerification/ExtensionDataIndexBatchVerification.csproj -c Release`

本验证项目直接编译燕子源码 `PersonalSyncService.ExtensionDataCatalog.cs`，只替换 `_backend` 与少量依赖为可计数的模拟远端，避免修改生产数据。

- 新建 40 个二进制对象：首次索引请求读取 2 次、写入 1 次并确认；40 个对象后不再重复读取、写入索引。
- 第二轮同一批资源：完整索引读取 1 次且无更新，40 个已存在二进制资产不会重复写入。
- 索引包含 40 个可发现的资源、远端二进制字节与本地一致；新增单文件调用继续写入索引并得到 41 个可发现键。
- 远端同路径二进制内容不同：不覆盖。首次索引写入失败：发布入口抛错，不能当成成功。
- 上述数据来自内存后端实际调用次数；**不是生产环境 Gitee HTTP 实测数量**。

## 本机安装 QA

- 在 PC-202409131508 上从此分支 Release 编译成功（退出码 0，旧文件中的警告仍存在）。
- 使用 `%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-index-batch-20261101\Yanzi.exe --dev --show` 独立开发版安装（SHA-256 `C797C4037CC8701E4457C553D2E67A2E5E2B6111FD493226418359FEE93A8513`，初次编译的程序集哈希）。
- 初次启动 PID 5228，50/50 窗口消息响应，无超时，最长约 9ms；增加指标后重新编译、重新安装同一隔离目录并启动 PID 3236，最终程序集 SHA-256 `48C847DF7B2269F5B30909F5D898B4E04956A309DF45566B6A8D48177AEB5924`，再次 50/50 窗口消息响应、最长约 11ms。正式版 PID 23788 始终保持正常响应。
- 最终加入计数日志后已重新编译并复验；上方首次 SHA-256 仅为中间版本，最终 SHA-256 以 `48C847DF7B2269F5B30909F5D898B4E04956A309DF45566B6A8D48177AEB5924` 为准。隔离程序未连接生产同步，不冒充实网验收。

## 遗留与回归边界

还需要：最终代码重建并重装 Canary、真实运行时 `binaryKeyIndexReadsAvoided` 指标、跨端恢复/冲突/删除行为、正式版本更新后同负载真实 HTTP 请求数。不得因单个离线测试通过而标记阶段一全部完成；正式 GitHub Release 仍由延迟队列处理。
