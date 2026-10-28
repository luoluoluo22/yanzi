# 夸克桌面网盘能力接入燕子（2026-10-09）

## 集成结论

基于 Windows 已登录的夸克网盘 7.3.5.1009 客户端，现已将经过独立测试的上传、下载和本地元数据检索能力移入 **燕子宿主统一能力注册表**。不需要厂商 CLI、不导出 Cookie/Token、不调用未经授权的云端私有 API。

实现源码：

- `src/OpenQuickHost/YanziQuarkTransferCapabilityProvider.cs`：6 项能力的权限、Schema、确认要求、执行超时，以及受控 Python 子进程调用
- `src/OpenQuickHost/CapabilityScripts/QuarkDrive/`：经过真实客户端传输验收的 Python 适配器、任务状态只读索引、文件缓存只读索引、UIA 文件名选中，以及必要的小型图像模板
- `src/OpenQuickHost/OpenQuickHost.csproj`：使脚本和模板随 Build/Publish 自动复制到 `CapabilityScripts/QuarkDrive`
- `src/OpenQuickHost/YanziBuiltinCapabilityRegistration.cs`：将新的六项能力接入 `yanzi-host`
- `src/Yanzi.CapabilityVerification/QuarkCapabilityVerification.cs`：宿主注册和真实只读数据验收
- `src/Yanzi.CapabilityVerification/Program.cs`：`--quark-capability` 验收入口

## 统一能力名称

| Name | 输入 | 作用 |
|---|---|---|
| quark.cloudDrive.adapterStatus | `{}` | 判断本机 Python 依赖及适配器就绪 |
| quark.cloudDrive.transferStatus | `{"path":"本地绝对路径","kind":"upload"}` | 从本机夸克 SQLite 只读查找 FINISH/fid，支持 kind=download |
| quark.cloudDrive.cachedLookup | `{"filename":"精确文件名","parentFid":"可选32位目录ID"}` | 返回历史缓存候选 fid，`liveVerified=false` |
| quark.cloudDrive.cachedFolder | `{"parentFid":"32位目录ID"}` | 只读返回缓存中的候选文件和 fid，非实时 |
| quark.cloudDrive.uploadVerified | `{"path":"现存本地文件","confirm":true,"timeoutSeconds":90}` | 通过客户端上传、等待 FINISH 并返回 fid |
| quark.cloudDrive.downloadVerified | `{"originalLocalFile":"本地原文件","targetFolder":"现存目录","confirm":true,"timeoutSeconds":90}` | 已显示文件夹内语义选中、下载及 FINISH/fid/SHA256 校验 |

**边界：**下载目前要求原文件存在、本机先前有完成上传的 fid，夸克当前文件夹能看见该文件。并不是无限制的全网盘搜索/下载。缓存只能作为候选，不代表实时存在。

## 安全

- 读操作权限：`application.read` 和必要时 `file.read`。
- 上传：`application.run`、`file.read`、`network.write`，`RequiresConfirmation=true`，实际执行还必须提供 `confirm=true`。
- 下载：`application.run`、`file.read`、`file.write`、`network.read`，同样双重确认。
- 原有燕子 Schema 不支持 `pattern`、`const`、`maxLength`；因此使用宿主支持的 Schema 语法，并在桥接执行层严格校验 32 位 fid、路径存在、超时和确认。
- 使用 `python -I` 加载随宿主打包的脚本；无通用 shell 执行入口。
- 目的文件存在时拒绝覆盖。不能证明完成时不会返回成功。
- 后台无窗口、实时跨目录搜索、完整文件列表 API 尚未打通。失败时不模拟成功。

## 环境与运维

客户端依赖当前登录的 Quark 桌面应用（7.3.5.1009 验收版本）。
Python 3.12（本机）及依赖：`pywin32`、`psutil`、`uiautomation`、`pynput`、`opencv-python`、`Pillow`、`numpy`。未安装时 `adapterStatus` 会报告缺失项，**不会自动联网安装依赖**。跨电脑须先安装夸克并登录、准备 Python 环境，适配器代码随燕子发布。

## 回归验收

在不重启现有燕子的情况下完成：

```powershell
dotnet build src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj --no-restore -c Debug -p:SkipStopRunningApp=true -p:OutputPath=F:\Desktop\cloud-drive-eval-20261009\yanzi-verification
F:\Desktop\cloud-drive-eval-20261009\yanzi-verification\Yanzi.CapabilityVerification.exe --quark-capability
```

验收实绩：宿主 Provider 注册、权限、Schema、读取自身测试文件的真实 SQLite FINISH/fid、缓存 fid 一致性、未确认上传拒绝等 **19/19 PASS**。独立适配器共 30 项自动回归及多轮真实上传/下载验收通过。构建产物包含全部 6 个夸克资源文件。

隔离编译产物目录：`F:\Desktop\cloud-drive-eval-20261009\yanzi-staging`。此编译没有停止正在运行的燕子。

## 上线状态

本次完成 **仓库源码集成 + 隔离构建 + 宿主级能力调用验收**。
正在运行的燕子是版本化运行目录里的旧进程。**没有强制结束该进程或覆盖正在被其他任务使用的正式宿主**；只有下一次安全更新宿主版本并重新启动后，这些新能力才会自动出现在当前运行中的能力注册表。不能把源码集成误称为已经在用户当前会话里激活。

下一步在确认其他任务可安全重启后，按现有版本化宿主部署流程切换，并在新宿主本地 Agent API 中检查 6 项能力登记和只读调用。

## 2026-10-09 正式激活尝试与回滚记录

用户明确允许替换正式桌面宿主，保留 Runtime 后台服务。
准备激活的构建（已经通过 19/19 宿主级验证）：

`%LOCALAPPDATA%\YanziRuntime\shells\quark-8a10364-20261009-134554\Yanzi.exe`

第一次切换时：
- 旧桌面宿主 PID 16944 停止，新版桌面宿主 PID 15928 成功启动；
- 后台 Runtime PID 14876 保持不变，健康探针确认客户端已成功附着、后台服务初始化状态为 true；
- **激活脚本最后一行用管道向 Get-Content 传递路径，导致 PowerShell 参数绑定异常。** 该异常触发预定回滚。
- 已核查旧桌面宿主恢复运行（PID 2924），正式 `runtime.json` 的 `stableShell` 仍指向 `20261009-095950-424\Yanzi.exe`，后台 Runtime 未中断。
- 修复脚本后再试时，执行环境返回“无法确定请求的安全状态，已拦截此工具调用”。没有绕开拦截，也没有继续强行停止进程。
- 因此状态为 **源码与发布资源完成、正式宿主尚未激活**；不能宣称 `quark.cloudDrive.*` 六项新能力已在当前 Agent API 生效。
- 原配置自动备份于 `%LOCALAPPDATA%\YanziRuntime\runtime.before-quark-8a10364-20261009-135947.json`。

### 网盘实时搜索静态研究新证据

来自安装包 `dist/renderer/index.js`：
- 应用封装的 `searchList({searchKey,page=1,size=100,needTotalNum=1,sort=[],isHL=1})`
  使用 `driveRequestCatch` 发起 `GET /1/clouddrive/file/search`，参数映射为
  `q`、`_page`、`_size`、`_fetch_total`、`_sort`、`_is_hl`。
- UI 搜索页的 `loadSearchFiles` 调用 `searchList`，将返回的 `data.list`
  写入 `file.changeListFile`，带分页和 `metadata._total` 信息。
- 搜索建议交互内的入口会根据
  `PanelTabEnum.SEARCH_CURRENT_FOLDER` 设置 `folderFid` 参数；
  全网盘搜索则不附加该目录范围参数。
- 现阶段仅分析本机已安装客户端打包源码，没有获取登录 Cookie 或调用未知私有 API，也没有建立独立程序的授权 RPC。

下一轮应完成正式宿主的安全激活和真实 Agent API 查询，然后探索通过客户端内置搜索页面按名称取得 `fid`。搜索结果必须实时验证文件身份，不能把历史缓存误判为云端最新数据。


## 2026-10-09 下午：第七项能力——跨文件夹全网盘搜索下载

从当前 Quark 7.3.5.1009 的源码与 GUI 实验确定：
- 内部列表搜索由 `searchList` 与 `GET /1/clouddrive/file/search` 承载，但本集成**不直接调用私有云端接口**。
- Quark `Ctrl+F` 面板有三类范围：“搜索网盘”“搜索当前文件夹”“搜索全网”。选择第一个后，必须点击随后出现的“搜索网盘文件”结果建议，才能进入客户端自己的“相关搜索结果”页面。
- Windows 中文输入法可能截获自动键入的数字并打开候选面板，导致错误搜索。新增 `quark_unicode_input.py`，通过 Windows `SendInput` 的 Unicode 键盘事件完整输入文件名，不修改用户剪贴板。
- 结果采用**模糊搜索**，即使输入完整文件名，可能返回大量候选；按 `TextPattern` 精确完整行匹配，拒绝同名不确定性，并用下载后的 Quark 原生 `download_task` 中 `fid/FINISH` 以及本地 SHA-256 核对真实身份。
- 文件搜索结果行经验证后出现操作按钮；新增仅包含图标的 `quark-global-search-download-control.png` 视觉模板，程序限制在目标行附近匹配，阈值 `0.93`，阻止图标位置改变后的盲点点击。
- 完成两次全网盘真实下载：根目录的 `AI-drive-io-roundtrip-20261009.txt`（93B）和此前子文件夹中的 `AI-quark-adapter-smoke-20261009.txt`（100B）。均无需预先进入对应目录；完整文件名、任务 FINISH、32 位 fid 与原始文件 SHA-256 一致。
- 第二次下载直接通过**随燕子构建打包**的 `quark_capability_host.py` 使用 `globalSearchDownloadVerified` 操作完成，返回 `remoteConfirmed=true`。无 Cookie/Token 提取。
- 已实现 `quark_global_search.py`，强制要求可信的本地已完成上传记录；找不到唯一精确文件、窗口不符合已验证布局、下载图标不匹配、下载目标已存在、完成状态/fid/hash 不一致时均拒绝报告成功。

本次新增能力的正式名称：

`quark.cloudDrive.globalSearchDownloadVerified`

输入格式：

```json
{
  "originalLocalFile": "F:\\Desktop\\cloud-drive-eval-20261009\\AI-drive-io-roundtrip-20261009.txt",
  "targetFolder": "F:\\Desktop\\cloud-drive-eval-20261009\\downloaded_global",
  "confirm": true,
  "timeoutSeconds": 90
}
```

这个操作在统一燕子 Provider 中声明 `requiresConfirmation=true`，调用桥接层也独立检查 `confirm=true`。核心输出包含 `remoteConfirmed`、`fid`、`sha256`、`targetPath`、`searchScope=all-cloud-files`。

### 测试和版本边界

- 独立适配器：共 36 项回归测试通过，其中 6 项是全网盘模块的拒绝操作测试。
- 新增随源码提供的离线测试：`tests/QuarkDrive/test_global_search_safety.py`，5/5 PASS。无需网盘账户，可测试文件缺失、目标目录缺失、覆盖阻止、超时参数、未验证上传拒绝。
- 燕子宿主级注册/权限/真实只读状态检查：**22/22 PASS**，包含第七项注册与确认声明。
- 另有两次真实跨目录文件下载回归：93B/100B，均完成 fid 与 SHA-256 校验。
- 旧的六项 `quark.cloudDrive.*` 已通过**当前正在运行燕子**的 `yanzi_catalog` 确认实际注册且 `available=true`。新版宿主在 2026-10-09 14:12 已由其他任务更新，包含之前六项；**第七项目前仅完成源码集成、隔离宿主验收及真实桥接验收，未在当前运行的宿主激活**。
- 项目工作区包含其他 Agent 的大量未提交修改，不应把未验证的整套工作区一并提交或盲目覆盖正在运行的燕子。发布时需沿用版本化 Shell、任务健康核查和回滚指针。

### 注意

该能力依赖夸克桌面客户端的当前已登录窗口，不是无窗口 RPC。仅支持“用户能提供原始本地文件且有可核对的本机上传历史”的文件；本机没有上传记录的任意私人文件不允许凭模糊名称自动下载。非标准窗口大小和 UI 变化会触发拒绝。跨平台/跨电脑必须重新验收客户端版本及依赖。
