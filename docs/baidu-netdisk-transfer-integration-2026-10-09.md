# 百度网盘桌面传输闭环与燕子能力

日期：2026-10-09

## 结果与边界

- 通过燕子已有能力 `baiduNetdisk.upload` 调用 Windows 官方 Shell “上传到百度网盘”操作，成功将 `AI-baidu-desktop-roundtrip-20261009.txt`（107 bytes）交付给已登录百度网盘 8.8.3.101。原能力只承诺 `triggered_unconfirmed`。
- 百度官方客户端“传输 → 上传 → 已完成”独立显示该测试文件；本机 `upload.db` 中 `upload_history_file` 有相同本地路径、云端路径、107 bytes、`error_code=0`、上传开始/结束时间；对应上传活动表没有残留。
- 通过百度官方客户端云端首页找到测试文件，点击下载。下载历史 `transmission.db.download_history_file` 的云端路径与上传匹配，大小 107 bytes、`error_code=0`、有结束时间。下载文件已存在于默认下载目录 `F:\Backup\Downloads`，与原文件的 SHA-256 完全一致。
- 以上**证实客户端完成记录及实际上传下载回环**。它不代表查询了实时云端对象 API；因此能力输出始终包含 `liveCloudExistenceVerified=false`。
- 访问方式不导出 Cookies/Token，SQLite 用只读 `mode=ro` 打开，不写客户端状态。
- 上传/下载表在应用数据的账户子目录下；不能直接假设存在根目录。多个匹配账户目录拒绝自动选择。

## 燕子统一能力

此前已有：
- `baiduNetdisk.status`、`baiduNetdisk.open`、`baiduNetdisk.upload`。

本轮新增：
- `baiduNetdisk.transferStatus`：只读。输入 `{"kind":"upload","path":"完整本地文件路径"}`，或 `kind=download`；可加 `afterSeconds` 排除旧历史。输出 `found`、`completed`、`cloudPath`、`size`、`errorCode`、时间戳及证据来源。
- `baiduNetdisk.roundtripVerify`：只读。输入 `{"originalLocalFile":"原始本地文件","downloadedFile":"下载文件"}`，在本地两个历史记录间核对同一云端路径、相同文件大小、`error_code=0` 与两个文件内容 SHA-256。

源文件：
- `src/OpenQuickHost/YanziBaiduTransferCapabilityProvider.cs`
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_transfer_index.py`
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_capability_host.py`
- `src/Yanzi.CapabilityVerification/BaiduTransferCapabilityVerification.cs`
- `tests/BaiduNetdisk/test_transfer_index.py`

部署：`OpenQuickHost.csproj` 将桥接 Python 随宿主构建及发布。桥接使用 `python -I` 与固定本地脚本，JSON 入参和 JSON 结果，不提供通用 Shell。

## 验收

```powershell
python -m unittest discover -s tests\BaiduNetdisk -p test_transfer_index.py -v
dotnet build src\Yanzi.CapabilityVerification\Yanzi.CapabilityVerification.csproj --no-restore -c Debug -p:SkipStopRunningApp=true -p:OutputPath=F:\Desktop\cloud-drive-eval-20261009\yanzi-baidu-verifier
F:\Desktop\cloud-drive-eval-20261009\yanzi-baidu-verifier\Yanzi.CapabilityVerification.exe --baidu-transfer
```

- Python 测试 8/8 通过（包含真实传输历史、SHA-256、拒绝错误码、历史过期、多账户冲突）。
- 统一宿主调用 19/19 通过（注册、读取实际记录、跨记录校验、权限拒绝）。
- 首次宿主编译 **0 error**，测试资料仅匹配自己创建的测试文件。
- 当前运行中的燕子曾通过 `yanzi_catalog` 实际列出上述两项能力且标注 `available=true`；后续仍需继续保证 Git 与发布包持久化。

## 后续

1. 增加组合 `uploadVerified`（发起 Shell 上传后监听本机真实 FINISH 记录，严格限制超时与防重复）。
2. 继续研究百度官方客户端文件列表、路径定位、精确下载操作。不能将“提交下载”当作“下载成功”。
3. 下载默认由百度客户端设置决定；未经用户授权不得全量扫描和搬移私人网盘文件。


## 2026-10-09 17:40 后续：带完成判定的自动上传

新增 `baiduNetdisk.uploadVerified`，在燕子统一 Provider 中声明 `RiskLevel=medium`、`RequiresConfirmation=true`，实际处理函数再次强制要求 `confirm=true`，需要 `application.run`、`application.read`、`file.read`、`network.write` 权限。它复用 `baiduNetdisk.upload` 的官方 Windows Shell 上传动作，然后通过固定 Python 桥接只读轮询百度上传历史，绝不读取 Cookie 或私有 API。

结果明确区分：`client_completed`（文件大小及客户端完成记录成功、`errorCode=0`）、`client_failed`（客户端完成历史包含非零错误码，原码原样返回）、`pending_unconfirmed`（截止超时时无完成或明确失败记录）；后两类不会重复上传，也不声称实时云端对象可访问。调用支持 `timeoutSeconds`（10–240），并发上传会拒绝重复进入。

真实尝试：第一份 55 字节文件的客户端成功记录已确认；第二份 55 字节文件的客户端历史为 `errorCode=110000`。初版仅筛选成功记录，导致第二份长时间挂起并错误归类为 pending；已修复 `BaiduTransferIndex.resolve_latest()` 与 `baidu_capability_host.py`，让失败记录以 `failed=true, completed=false` 和原错误码直接暴露。这里不猜测 `110000` 的具体厂商含义，不自动重试。

测试：Python 9 项通过；宿主 27 项（包含实际成功与失败历史、权限和二次确认）通过；Debug 构建零错误。当前运行宿主尚未注册 `uploadVerified` 时，应先做独立正式包发布再激活，不能把源码修改描述为已上线。


## 2026-10-09 后续：进行中任务进度

`baiduNetdisk.transferStatus` 现优先查询任务历史（成功、失败），没有匹配历史时再只读查询客户端活动表 `upload_file` / `download_file`，返回 `phase=active`、`size`、`completedBytes`、`progressPercent` 及原始 `clientStatusCode`。如果文件下载尚未在磁盘产生完整目标文件，仍允许按预期保存路径查询活动任务；不依据部分文件大小判断失败。

状态：`completed`、`failed`、`active`、`unknown`。用户不应该把活动任务的数字状态码当成文档化的服务端错误码；只有实际客户端历史结束后才归档为完成或失败。不访问实时云端 API。已经使用临时、模拟的 SQLite 上传表验证 40/100 字节得到 40% 进度，且时间过滤生效，完全不写用户的真实网盘数据库。Python 回归现为 10/10 通过。

正式部署限制：向当前用户的 `%LOCALAPPDATA%\YanziRuntime\shells` 复制新版本曾被当前执行环境安全检查拦截，因此保留之前正常运行的正式燕子，不通过其他工具绕过拦截；构建包仍可留在独立实验目录，待具备正常授权部署路径再切换。


## 2026-10-09 后续：全网盘精确文件名搜索

新能力：`baiduNetdisk.searchExactVisible`（燕子统一 Provider，调用已登录的百度官方桌面客户端，而非私有百度 HTTP API）。

请求：

```json
{"filename":"AI-baidu-desktop-roundtrip-20261009.txt","waitSeconds":12}
```

它通过受控的 Windows UI Automation 搜索框 `EditControl(AutomationId=tags-input-ipt)` 输入完整文件名，使用系统 Unicode 键盘事件避免中文输入法截获，再按 Enter 提交搜索，等待搜索页出现针对当前文件名的标题与结果行。仅查找准确命名为目标文件的 `GroupControl`，忽略搜索栏/标题中的文本假阳性和画面之外的候选行。结果的 `visibleExactMatches` 为精确匹配个数，`advertisedFuzzyResultCount` 为客户端显示的模糊搜索条数。

已通过真实客户端测试：
- 搜索 `AI-baidu-desktop-roundtrip-20261009.txt`：模糊结果 4 条，当前可见完整文件名匹配 1 条，返回 `found=true`。
- 搜索 `ZZZ-UNMATCHED-NEVER-TEST-20261009.dat`：模糊结果为 0 条、精确匹配为 0，返回 `found=false`。
- 之后再次搜索自己的 107B 测试文件，精确匹配再次恢复为 1，证明搜索页随真实查询更新。
- 曾发现仅替换搜索框文字不一定刷新上一个查询的结果。因此现在必须使用 Enter 明确提交后再等结果，且核验搜索标题匹配本次关键词，否则拒绝。
- 百度窗口退到后台时 Chromium 无障碍树会短暂变空。查找窗口时先验证百度官方进程，再恢复窗口、激活并确认唯一搜索输入；输入前还核对窗口焦点，不会向其他程序盲打。

实现：
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_desktop_search.py`
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_unicode_input.py`
- `src/OpenQuickHost/CapabilityScripts/BaiduNetdisk/baidu_capability_host.py` 新增 `searchExact` 桥接操作
- `src/OpenQuickHost/YanziBaiduTransferCapabilityProvider.cs` 新增统一能力
- `tests/BaiduNetdisk/test_search_exact.py` 覆盖完整文件名、模糊/错误文本、陈旧搜索标题、多个同名行、离屏结果与错误文件名

**重要安全边界**：搜索返回的是当前已渲染结果页的精确名称存在性，不保证整库唯一、也不提供云端 `fid`，不会因找到行就断言文件身份；输出明确 `uniqueAcrossEntireCloud=false`、`cloudFidVerified=false`、`downloaded=false`。某次深层 UI 检查遭到执行环境阻止，未强行规避；自动按名选中和下载**尚未验收，不对外开放**。
