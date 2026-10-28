# Windows 小程序源码归位（2026-10-12）

## 规定

Windows 用户级小程序的唯一开发、安装与实际运行目录是 `%LOCALAPPDATA%\OpenQuickHost\Extensions\<id>\`。宿主 Git 仓库只保留扩展 API/SDK、通用运行时、测试和发布工具，**不再保存小程序源码副本或网页商店发布包的重复清单**。

安装在用户目录后由燕子的既有个人小程序云同步机制处理；本次没有额外创建同步任务。跨设备是否真实完成上传/恢复仍需通过实际账号及第二台电脑核验。

## 已从主仓库迁出

| 旧目录 | 用户目录中的权威小程序 |
| --- | --- |
| `extensions/raccoon-manager/` | `raccoon-manager` |
| `extensions/video-resume/` | `video-resume` |
| `extensions/lanzou-cloud/` | `lanzou-cloud` |
| `prototypes/job-agent/` | `ext_e9e37068c8284b1e808f5454181fb418`（求职） |
| `prototypes/capability-lab/` | `capability-lab` |
| `tools/idle-chatgpt-tasks/` | `idle-chatgpt-tasks` |
| `tools/android-automation/extension/` | `yanzi-android-automation` |
| `tools/android-automation/recovery/` | 旧回收工具，未作为活动小程序安装，仅保存迁移备份 |
| `website/store-packages/open-yanzi-homepage/` | `open-yanzi-homepage` |
| `website/store-packages/open-yanzi-github/` | `open-yanzi-github` |
| `src/Yanzi.Capture/Extension/` | `yanzi-capture`，保留宿主/独立截图编译项目 |

上表对应 11 组仓库目录、10 个已安装 ID。VPN 小程序另已直接安装至用户目录 `vpn-manager`，不纳入宿主仓库。

## 差异处理

迁移前逐个比较文件 SHA-256。默认以**用户目录现存版本为权威**，没有批量覆盖；逐项复查后发现两处应当合并的安全修正：**求职小程序**的仓库版本加入招聘方入站消息核实、安全会话队列，并禁用无法确认收件人的旧模板自动回复，因此备份后将经过独立编译的安全版 `JobAgent.cs` 安装到用户目录、升为 0.4.1；**浣熊 MCP**保留用户版本的自定义 OAuth access token 有效期，同时合入仓库中的 refresh 授权缺省 resource 兼容性、回归测试和基于实际监听端口识别 Node 进程的修正，升为 0.4.4。能力实验室用户版本已有公共 WPF 组件的改进，手机购物助手版本仅编码有别，均保持用户版本。看视频源码主要只有编码差异；仓库独有的三个非运行配置文件已经复制到对应用户小程序目录：

- `video-resume/rice-cooker-link.example.json`
- `ext_e9e37068c8284b1e808f5454181fb418/HOST-INTEGRATION.md`
- `capability-lab/README.md`

迁移前已将全部 11 组源码归档，并记录 10 个在用 manifest 的哈希，保存在：
`%LOCALAPPDATA%\OpenQuickHost\ExtensionMigrationBackups\repo-extracted-20261012-083154\`

此文件夹不在 Extensions 扫描/同步目录内，不会生成重复小程序。**清理 Git 工作树并不会删除旧 Git 历史**；回滚时可从迁移备份或旧 commit 查阅特定差异，不应盲目覆盖现用版本。

## 依赖和验收

- `scripts/test-raccoon-startup-env-isolation.ps1` 使用用户目录的浣熊入口。
- `scripts/test-idle-task-list-capability.ps1`、`scripts/verify-idle-business-outcome.ps1` 和 `tests/IdleTaskRecoveryVerification` 使用用户目录闲置任务源码。
- `tests/video-resume` 直接编译用户目录中的看视频代码。
- `scripts/install-yanzi-capture.ps1` 读取已同步/安装的截图 manifest，不再维护独立仓库模板。
- `tools/android-automation/install-extension.ps1` 保留旧备份目录的迁移/验收能力，不再从仓库覆盖用户的小程序。
- `scripts/verify-miniapp-repository-isolation.ps1` 禁止再次跟踪真实 Windows 小程序包；`-CheckInstalled` 验证本机用户安装。

非 Windows 小程序保留项：`browser-extension/manifest.json` 属于 Edge 浏览器扩展，`docs/yanzi-ui-official-catalog/reference-package/manifest.json` 属于 UI 参考文件，并非用户小程序；这些文件不迁出。历史开发报告中旧路径仅为当时事实，不做历史抹除。

本轮只调整开发/源码结构，不重启燕子、不覆盖正式小程序、不触发正式安装包发布。
