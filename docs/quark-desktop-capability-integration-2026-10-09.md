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