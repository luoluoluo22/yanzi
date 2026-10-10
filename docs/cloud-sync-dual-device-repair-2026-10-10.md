# 双设备云同步修复与验收（2026-10-10）

## 环境与结果
- 台式机：PC-202409131508；笔记本：DESKTOP-HSCA8C5。两端均运行版本化 Shared Runtime + Shell，并保留旧快照作为回滚。
- 共享设置属性：48/48 一致；环境变量值：4/4 按解密后 SHA-256 比对一致，任何令牌明文均未写入报告。
- 笔记：10 篇 Markdown + 1 个附件，共 11 个文件，两端字节散列一致。笔记插件从笔记本的 0.1.0 升级到 0.2.5；私有账户扩展仓库已发布 0.2.5、验证下载哈希。
- 灵感白板：合并台式机六张卡、笔记本三张卡，保留两组原有连线；双方 9 卡、7 连线，数据散列一致；workspace-config.json 保留设备专属路径。
- 日历：修复台式机历史同步日志误判 6 条待删除记录，恢复至双端 13 条，待同步记录清零，日历 JSON 散列一致。
- 剪贴板收藏：favorites-v2.json 双端散列一致。笔记、白板、日历、剪贴板等核查的主要业务数据与 6 个核心小程序版本一致。
- 求职小程序允许同步的轻量业务数据（设置、简历、每日记录及简历历史）：12 文件；11 字节完全一致，余 1 文件仅 UTF-8 BOM 差异，去除 BOM 的散列一致。
- 两台设备同步冲突数均为零。已经处理 settings.runtime 中环境变量描述字段的元数据冲突。

## 修复的系统原因
1. Gitee Contents API 对较大的扩展 ZIP 读取会产生截断：增加大包分片清单、分片 SHA-256 / 总体校验、修复旧截断包，并以模拟新设备验证完整扩展下载及第二次同步静默。
2. 扩展 ZIP 校验失败不得阻断个人小程序数据恢复：将 ReconcileCloudDataAsync 前置到扩展包同步之前。
3. 图片附件带 BOM 的 data URI 与云端去 BOM 文本视为内容等价，其他差异继续按哈希拒绝覆盖。
4. 云端用户数据发现目录补齐：笔记附件、灵感白板，以及单独约束的相册和求职数据。禁止将本机状态及未授权文件无差别同步。
5. 账号扩展新版本优先于旧设备安装版本：旧客户端不允许把更旧的插件版本覆盖到账号库；从云端安装较新包前先持久备份并校验完整性。

## 不应强行同步的设备数据
- 镜子 mirror_records.json 是本机进程、窗口与停留时长记录（台式机 500 条、笔记本 177 条）。
- 聊天网页桥接器、闲置任务、关机计时、截图保存路径、日历声音文件与本机同步日志、灵感白板上次打开路径，均与设备或浏览器环境有关。
- 微信标注的本地证据截图、职位抓取缓存 jobs.json 等数据可能敏感或体积较大，不应无筛选自动上传。求职简历核心数据采用独立有限范围。

## 验收与回滚
- 通过：Release 编译、extension-data-catalog-tests、sync-safety；先前 real-backend fresh-device-sync 已验证 78 个扩展包首轮下载以及二次同步不重复写入。
- 最新双端现场：48 项共享配置散列一致，4 项环境变量值一致，14 个主要持久文件散列一致，6 个核心插件版本一致。
- PC 用户数据备份：%LOCALAPPDATA%\OpenQuickHost\Backups\sync-repair-20261010-0734 及 sync-repair-20261010-extensions。
- 笔记本对应用户数据备份：同名 Backups 目录；插件安装前原始目录另保留于该目录中。
- 旧运行时快照保留在 %LOCALAPPDATA%\YanziRuntime\versions 和 shells；runtime.previous.json / runtime.json 记录指针，回滚应先停宿主再切换指针和 HKCU Run 注册项。
- 临时下载包及中转构建物清理约 1.0 GB；保留用户数据与回滚快照。
- 注：对笔记本笔记 UI 的自动打开调用未通过执行通道安全检查；业务文件及运行时/API 状态验证通过，但此项不写为“已完成真实 UI 交互验收”。

## 回归命令
```powershell
dotnet run --project src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -c Release -- --extension-data-catalog-tests
dotnet run --project src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -c Release -- --sync-safety
dotnet run --project src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -c Release -- --fresh-device-sync
```
