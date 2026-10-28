# 燕子 Windows 小程序开发工作流（Agent 必读）

**生效日期：2026-10-12。适用对象：** 用户级 Windows 小程序（Extension），包括新的小程序、功能迭代、热修复、移植和跨设备版本修复。任何 AI Agent 在开始修改小程序之前，先读本文件及 [小程序开发规范](../extension-development-specification.md)。

## 不可违反的边界

- **源码、`manifest.json`、资源文件、业务脚本的唯一权威目录**：`%LOCALAPPDATA%\OpenQuickHost\Extensions\<extensionId>\`。在 PowerShell 中用 `Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\<extensionId>'` 定位，**不得写死用户名或盘符**。
- **用户业务数据**：`%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\<extensionId>\`，优先使用 `context.ExtensionDataDirectory` 或既有 Storage API。不要把运行日志、缓存、账号内容塞进 `manifest.json` 或源码。
- **严禁在宿主 Git 仓库中开发或保存用户级小程序副本**：包括 `extensions/`、`prototypes/`、`tools/`、网站商店分发目录及临时 Git worktree。不得为这类小程序创建宿主仓库的业务源码 PR、提交小程序源文件或纳入宿主安装包。
- **宿主仓库只管理公共能力**：SDK、通用依赖/运行时管理、扩展协议、安全边界、通用测试、文档和 CI。单个小程序需要新功能时，**首先在小程序内使用现有 API 实现**；确有公共平台缺口时再单独论证、单独验证公共 API 变更，不得为了一个业务小程序偷偷硬编码宿主。
- **账号令牌和订阅凭据**：从燕子既有**环境变量管理器**引用，只在小程序的同步设置中存储变量**名称**；不要在源码、manifest、上传包、普通同步 JSON、Git 或调试日志中保存真实凭据。
- **依赖**：优先复用现有宿主公共运行环境管理器与 `manifest.requires` 支持的 Provider。缺失的专属依赖按需准备，产物放独立设备运行时目录，不塞宿主代码或强制随所有用户安装；新增通用 Provider 需要单独作为平台工作验收。
- **云同步**：将小程序放在用户 `Extensions\<id>` 目录即进入燕子**既有的小程序发现与同步机制**，不需要再写一套云同步脚本、手动上传到宿主仓库或发布宿主安装包。**“已放入正确目录”≠“已确认云端/第二台设备收到更新”**；实际跨设备结果须以日志、包哈希及目标设备检查为准。
- 本规范针对 **Windows 用户小程序**。Edge 浏览器扩展、网页小程序、Android 工程有各自独立的源码/发布约定；不可将它们误判为本目录下的 Windows 扩展。

## 每次开发必须执行的七步

1. **定位而不是复制**：从 `%LOCALAPPDATA%\OpenQuickHost\Extensions` 搜索目标 `manifest.json` 和 `id`；若已安装，直接使用该目录现有版本，先读 manifest、入口源码和对应的业务数据路径。若新建，只在目标用户目录创建 `<extensionId>\manifest.json` 和入口文件。
2. **先检查差异和运行状况**：核对实际安装版本、是否有设备专属配置、是否有其他进程正在运行；已有开发副本或历史 Git 副本时，以用户目录为权威，逐文件审阅差异，不得用旧版整体覆盖正在使用的小程序。
3. **备份与隔离**：修改前对重要源文件和 manifest 做哈希与备份；备份放 `%LOCALAPPDATA%\OpenQuickHost\ExtensionMigrationBackups` 或其他**不被扫描为小程序**的目录，不能在 `Extensions` 下再创建含同一 `id` 的可发现副本。测试产物与临时依赖放系统 Temp。
4. **就地修改**：直接编辑用户目录中的入口文件、manifest、必要资源；只用现有 SDK 和公共组件，不挪回宿主代码树。变更版本时按实际兼容策略递增 `manifest.version`；不要无故更改 `id` 或覆盖用户设置。
5. **在不破坏正式环境的前提下验证**：运行静态检查、编译/针对性单元测试，再验证热重载或本地 Agent API 调用、启动和关闭；网络、代理、开机启动、删除、发送等有副作用的能力要做实际隔离和回退。不得把“编译通过”说成“已实机验证”。
6. **验收位置和同步**：确认 `%LOCALAPPDATA%\OpenQuickHost\Extensions\<id>\manifest.json` 与入口存在、ID 正确、版本准确；检查自动同步日志和冲突/版本规则。只有在确实可访问目标设备并完成核对时，才报告“第二台设备同步成功”。
7. **清理与报告**：删除本次临时构建/测试产物，不删除用户数据和运行时凭证；报告实际目录、版本、验证范围、未验证项目和回滚位置。不要顺便修改宿主、创建宿主 Release、推送业务小程序源码到 Git。

## 新建小程序最短命令（Windows PowerShell）

```powershell
$id = 'my-extension'
$dir = Join-Path $env:LOCALAPPDATA ("OpenQuickHost\Extensions\" + $id)
New-Item -ItemType Directory -Force -Path $dir | Out-Null
# 在 $dir 中创建 manifest.json 及独立入口文件；不得在 Git 仓库中创建。
# 变更后检查 (Get-Content (Join-Path $dir 'manifest.json') -Raw | ConvertFrom-Json).id
```

## 平台防线与历史

- 在宿主仓库运行 `scripts/verify-miniapp-repository-isolation.ps1`（本机可加 `-CheckInstalled`）。GitHub 工程 CI 已包含基础隔离检查，出现新的 Windows 小程序包会失败。
- 2026-10-12 的清理记录见 [用户目录源码迁移记录](user-directory-source-migration-2026-10-12.md)。此后不能重新恢复那些业务源码到宿主 Git 仓库作为“标准样板”。
- 需要修改宿主平台时，仍按根目录 [AGENTS.md](../../AGENTS.md) 的开发 / 验证 / 合并 / 按需发布规则进行；**纯小程序变更不需要宿主 Git 提交或安装包发布**。

**执行前最终检查：我现在修改的是用户目录中的真实小程序，还是错误地修改了宿主仓库里的另一份副本？**
