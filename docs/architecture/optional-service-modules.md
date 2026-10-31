# 燕子可选后台服务（Service Modules）

服务与宿主分离，宿主只读取 `ServiceModules/*.json` 能力清单。除发布包目录外，也可在 `%LOCALAPPDATA%\OpenQuickHost\ServiceModules` 放置由用户批准的模块清单，模块本身不是宿主依赖项。

## 触发安装

1. 小程序 `manifest.json` 的 `capabilities.requires` 声明 `["files.p2p.status"]` 或其他服务能力时，执行小程序前检查。
2. 通过统一 `/v1/capabilities/invoke` 首次调用未安装的服务能力时，检查调用者权限并安装。
3. 从受信任的 Git 仓库获取固定 SHA-1 提交，验证版本及工作目录无未审查修改；只对发布信任目录里的服务声明执行安装脚本。无此服务需求的设备不会安装或启动。
4. 服务安装后由独立计划任务管理进程。燕子退出、更新或重启不应影响正在传输的任务。无法完成首次配对则只保留下载的模块仓库，并明确返回 `service_pairing_required`，不得偷用其他设备密钥。
5. 首次配对由模块自身管理，`%LOCALAPPDATA%\YanziTransferService\pairing.json` 包含配对分配的本机地址、对端地址/名称、网络名称、已安装 EasyTier 路径、仅该配对使用的 secret 文件及节点列表。不能在宿主代码或公共清单中硬编码私人设备的名称、地址或 secret。

## 服务声明

清单包含 `id`、`repository`、不可变 `revision`、`installScript`、`localDirectory`、本地 `tokenHeader`/`tokenFile`/`configFile`/`portField`，以及 `capabilities`（名称、权限、HTTP 方法和固定相对路径）。能力投影 `project` 可把 Worker 的 JSON 结果映射到宿主 API，不需新增每一个服务的编译时 Provider。

`files.p2p.*` 由 `yanzi-transfer-service` 提供。主进程仅有控制平面请求；服务进程负责独立 EasyTier 会话、持久任务和文件校验。未安装时能力列表不会冒充已经就绪；可用的模块目录通过 `/v1/agent/catalog` 的 `serviceModules` 属性列出。

## 发布安全

每次升级固定版本 SHA，并验证编译、能力目录、全新设备仅下载/需配对、既有设备无感知及跨宿主重启传输。禁止对无法验证来源的模块执行自动安装；服务升级必须避免中断活跃任务。