# 云同步与输入钩子审查（2026-09-22）

## 方案现状

- 账号同步：`CloudSyncClient`、`MainWindow.CloudSync` 对接 Cloudflare Worker。配置拆分为 revision 对象，私有扩展有归档历史，服务端存在 expectedRevision 检查和条件更新。这是现有方案应继续保留的方向。
- 个人同步：`PersonalSyncService` 通过统一后端适配 GitHub/Gitee/GitLab/Gitea/S3/WebDAV；扩展采用 `index.json` + SHA-256 命名 ZIP，配置/历史另存对象。登录账号后的 UploadOnlyBackup 用于备份，避免个人仓库反向覆盖账号私有库。
- 旧 `WebDavSyncService` 仍有调用入口，两条路径需要共同加固。
- `InputHookService` 和 `MouseGestureService` 均使用专用消息线程；自身合成输入有标记，日志已有异步队列。并非所有钩子工作都需要重写。

## 本次已改进

| 问题 | 风险与修复 |
|---|---|
| 损坏远端索引被当作空仓库 | 新旧路径统一严格读取；空文件、错误 JSON、缺失数组、未知版本、重复 ID 等终止同步，只有远端确实不存在才初始化。 |
| 远端 ID 直接参与本地删除/替换路径 | 要求单级合法目录名，校验父目录并拒绝目标目录联接/符号链接。 |
| 下载 ZIP 只检查格式 | 安装前验证 SHA-256 与索引一致。哈希用于完整性校验，不等同于发布者签名。 |
| 删除旧目录后再安装 | 先解压临时目录，提交前检查取消，旧目录改名备份；新目录移动失败时恢复旧目录。清理失败保留备份并记录日志。 |
| 404 自动剔除/修复索引 | 临时不可读不再视作删除；停止本轮同步并保留索引。真正损坏的远端包需要恢复后再重试。 |
| ZIP 文件顺序不固定 | 按相对路径稳定排序，减少相同内容重复上传。首次使用新排序可能产生一次包哈希变化。 |
| 配置检查构造后端 | 直接验证字段，不再为查询配置状态构造 HttpClient。 |
| 仅远端元数据变化也重新安装 | 个人同步比较包哈希和删除状态，内容一致时跳过下载与替换。 |
| 长按计时器与钩子线程竞争 | 定时器只投递消息，钩子线程执行状态转换；代次标识拒绝松开/取消后的过期消息。设置重载与外部重置也投递到钩子线程。 |
| 停止流程提前清空句柄 | 即使启动失败仍请求退出；句柄由所属线程 finally 释放。停止超时不丢失待释放句柄，状态日志反映真实结果。 |
| 移动快速放行误伤窗口吸附 | 吸附激活期间继续处理移动，保留已有 UI 移动合并机制。 |
| 轨迹诊断无界增长 | 最多记录 2048 点，无触发时最多打印 20 点，限制高回报率鼠标下的内存和日志格式化成本。 |

## 尚需后续处理的风险

1. **高：个人同步缺少统一的远端条件写协议。** `IPersonalSyncBackend.WriteBytesAsync` 没有携带读取时版本；进程内 SemaphoreSlim 不能防止两台设备同时读旧索引、先后覆盖。Git 内容 API 即使使用文件 SHA，也需要绑定原始读取版本，不能在保存前重新取最新 SHA 后覆盖。后续应让读取返回版本/ETag，提交带原版本，并在冲突时重新合并；WebDAV/S3 使用条件请求，Git 使用原始 blob/commit 版本。当前修复不宣称解决跨设备丢失更新。
2. **高：窗口吸附仍同步调用 UI。** `InputHookService.InvokeShowWindowSnap` 中的 `Dispatcher.Invoke` 会等待 UI；UI 卡顿可能让全局鼠标钩子超时。应将 bool 同步契约改成异步请求/确认，并设计松开先于确认、显示失败、取消的状态机，避免简单改 BeginInvoke 后吞掉正常点击。微软明确说明低级钩子超时可能被静默移除：https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc 。
3. **中：完整同步验证已存在覆盖缺口。** 17 个 AppSettings 字段未分类：AchievementPoints、CompletedQuestIds、DisableInFullScreen、GlobalServiceBlacklistedProcesses、HasOpenedBackpack、LauncherResultViewMode、MouseGestureBlacklistedProcesses、MouseGestureEnableRockerActions、MouseGestureEnableWheelActions、ProcessExecutablePaths、QuickPanelContextColumnCount、QuickPanelContextRowCount、QuickPanelGlobalColumnCount、QuickPanelGlobalRowCount、QuickPanelRowCount、ShowBlindOperationGuide、UnlockedBadges。必须明确同步/仅本机策略并补齐快照映射；不能仅改清单使测试变绿。
4. **中：大扩展全量打包和内存峰值。** BuildLocalSnapshot 每轮为全部扩展构建 ZIP，并将 byte[] 保留到合并结束；上传后又完整下载校验。建议增量文件指纹缓存、逐包处理和流式哈希；保留全量校验兜底，不能只依据目录 mtime。解压仍缺少条目数和展开总量配额。
5. **中：双钩子协调和生命周期。** 两个独立线程仍共享右键相关状态；设置重载、退出/重启、合成点击与真实输入交错需要实机压力回归。未来可统一输入状态所有权。此次代次校验仅针对长按回调，不等同于所有输入竞争已消除。
6. **中：崩溃恢复和来源信任。** 目录替换具有失败回滚，但两次改名并非断电事务，备份仍需启动恢复机制。个人仓库读写者可以同时改包和哈希；扩展执行仍依赖对仓库内容的信任。主配置秘密字段已有单独存储/清理机制，不应据此假设任意扩展文件都已脱敏或加密。

## 验证

- 独立目录构建通过；编译有现有空引用/未等待调用等警告。
- 新增 `--sync-safety` 定向回归：缺失与损坏索引、重复 ID、目录穿越、哈希不符、ZIP 穿越、解压失败、取消、目标被文件占用、成功替换和临时目录清理。
- 完整验证在原有同步覆盖清单检查处失败，列出的 17 个字段尚未分类，后续用例未执行；不报告为全量通过。
- 未连接真实云端执行写入，也未在用户桌面安装测试钩子；尚未验证休眠唤醒、快速右键连点、8kHz 鼠标和 UI 长时间阻塞下的实际表现。
- 构建新增 `SkipStopRunningApp` 开关，验证使用独立输出目录，避免构建目标强制结束正在运行的 Yanzi。

```powershell
dotnet build src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -p:SkipStopRunningApp=true -p:OutputPath=F:/Desktop/kaifa/OpenQuickHost/artifacts/sync-review/ --no-restore
dotnet artifacts/sync-review/Yanzi.SyncVerification.dll --sync-safety
```
