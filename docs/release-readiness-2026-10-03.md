# 2026-10-03 发布候选与升级验收

本轮继续完成工程整改后的发布准备：构建真实 Release 产物，验证更新、旧数据迁移、会话文件保留和回滚兼容。未上传正式 GitHub Release，未覆盖实体手机生产包。

## 修复

- Windows 原卸载钩子启动 `cmd.exe`，延迟递归删除整个 `%LOCALAPPDATA%/OpenQuickHost`。已删除该行为；卸载只清理安装注册，保留配置、会话和小程序数据。常规 `Yanzi.SyncVerification` 增加卸载源码防线检查，阻止该删除逻辑回归。此项不是执行真实卸载的验证。
- 原打包脚本用普通 ZIP 覆盖 Velopack 便携包，丢失 `Update.exe`、`.portable` 和正确布局。已保留 Velopack 原生便携包；验收核对上述文件和 `current/Yanzi.Core.dll`。
- 桌面与 Android 构建支持独立 `ArtifactRoot` 和候选版本参数；桌面支持 prerelease 版本，Assembly/FileVersion 保持数字格式。正式版本号未改写，默认 Android 构建仍是 `42 / 0.2.42-dev`。
- 候选包可以跳过网络下载旧包并保留历史版本。桌面打包显式检查 `Yanzi.Core.dll` 和发布退出码；Android 独立中间目录避免原构建目录被占用。本轮首次非隔离 Android 构建遇到 dex 文件占用，隔离构建成功。

## 产物与构建

产物目录（本机临时验收目录，未上传）：

`C:/Users/Administrator/AppData/Local/Temp/YanziDev/release-readiness-1791011739415`

| 产物 | 版本 / 大小 | SHA-256 |
| --- | --- | --- |
| Windows Setup | 1.0.7-rc.1 / 88,812,171 bytes | `24ede40eb8712c3318c61d2cfd20a3a7ddf5121681ae1ecc905b64a7d5d6fecc` |
| Windows Portable | 1.0.7-rc.1 / 84,338,076 bytes | `16b56c1f53e215bd62bae9c2ff67abb38d20790c750fab70763f487bddbf2e6b` |
| Windows full | 1.0.7-rc.1 / 84,342,411 bytes | `cd5567f9820cc3a3f43b1db87a62bc376e378c7f712299a3826ba7807c974e95` |
| Windows delta | 1.0.6 → 1.0.7-rc.1 / 1,305,487 bytes | `9980a5e87198426d3cb3c76bb447621d5e9b4dc129fc8bf5c5624b9bfbb811ae` |
| Android Release APK | 43 / 0.2.43-rc.1 / 90,870,154 bytes | `9d079a1465664c944cce5044c37e550151393403689f2a61b8f87e753f4ef68b` |

Windows 为自包含 win-x64，使用项目对应 Velopack 1.2.0。Android minSdk 26 / targetSdk 35，Release 未启用 debuggable。Release 构建成功；新增验证程序 Debug 编译 0 errors；完整设置/同步验证通过，覆盖 89 项设置分类、凭据边界、对象冲突与恢复点等既有场景。桌面开发程序已重新独立启动。

## Windows 更新与兼容

- 使用真实 `Velopack.UpdateManager` 和 `TestVelopackLocator`，把下载、缓存和更新器定位在临时目录。更新发现 1.0.7-rc.1，选择 1.0.6 基线的 delta。
- 更新器日志明确确认实际 delta 应用成功，未走完整包回退。还原包与完整包的全部 ZIP 文件项名称及 SHA-256 相同。
- 将 delta 替换为损坏文件但保持原清单后，更新器校验失败并明确回退完整包；最终全部文件项哈希仍相同。
- 旧包缓存会被 Velopack 自动清理；独立保存的 1.0.6 原包哈希不变。因此回滚包必须单独备份，不能依赖更新缓存。
- 用便携包中实际 Release `Yanzi.dll` 加载旧配置和保存配置，测试主题偏好、中文内容、离线测试会话和用户文件保留。
- 用旧 1.0.6 包中的实际 `AppSettings` 模型读取候选版保存的设置，再按旧模型保存，新版重新读取成功。此项验证的是配置模型往返兼容，不代表所有新字段经过旧版保存后都保留。
- 没有在当前用户下运行真实 Setup/卸载或 Apply 安装切换：本机已有同 packId 的正式安装，Velopack 即使指定另一安装路径仍可能改写同一注册信息和快捷方式；Apply 也会执行安装钩子。完整安装、卸载、运行时回滚需在独立 Windows 用户或 VM 验证。更新下载/增量还原验证没有替代这项验收。

## Android 实际安装链路

仅操作 `emulator-5554`，使用历史生产包 0.2.36 和候选 Release。脚本拒绝所有非 `emulator-*` serial；使用合成离线 JWT，不读取真实账号凭据。

1. 安装 0.2.36，准备 75 条中文、多行旧 SharedPreferences 聊天、设备 ID、偏好和 1 MiB 本地文件。
2. `adb install -r` 覆盖安装 0.2.43-rc.1，实际启动并点击聊天页。
3. SQLite 中 75 条内容、顺序和账号归属精确一致；迁移标记存在。测试会话、设备标识、偏好、文件 SHA-256 保留。
4. 重复覆盖安装候选版并启动：数据仍相同，没有重复迁移。
5. root/userdebug 模拟器接受 `adb install -r -d` 降级 0.2.36；旧版实际启动聊天页无 Fatal，会话、偏好、本地文件和新版 SQLite 均保留。
6. 再次覆盖升级到候选版，75 条历史仍精确一致，没有重复；结束清空模拟器测试数据，重新启动干净 Release，通过前台 smoke。

模拟器允许降级不代表实体手机或普通用户可以绕过系统版本限制；常规生产回滚应使用更高 versionCode 发布修复版。会话保留验证检查本地离线凭据，未验证该合成 JWT 能在公网登录。此前公网真实账号与手机 Dev 验收见工程整改执行报告。

实体一加 `81f7e66d` 本轮未安装或清数据：正式包保持 `42 / 0.2.42`，Dev 保持 `42 / 0.2.42-dev`，两者安装路径均与前轮一致。

## 云端与正式发布条件

- 生产 `/health` 返回 `ok: true` 与 `foundationRevision: 2026-10-03-domains-v1`；工程 Worker 源码已由 `999c34e` 自动部署。本轮改动不涉及 Worker 业务代码。
- 截至本轮检查，后续 `076199b`、`02d7944` 自动构建均 stopped/terminated，之前日志记录初始化超时。Cloudflare 官方状态为 Workers Builds degraded_performance，事件为 “Issues with Workers Build failing to start”。本轮新提交仍只通过 main 推送触发自动构建，不能把健康接口当作新提交部署成功的证据。
- 官方状态来源：[Cloudflare status API](https://www.cloudflarestatus.com/api/v2/summary.json)。不采用本地 wrangler deploy 或手动触发 API 绕过项目部署约束。
- Windows Setup 的 Authenticode 状态为 `NotSigned`。Android 候选和历史 0.2.36 同证书，SHA-256 为 `8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3`，证书主体为 `CN=Android Debug`。本机 Release 签名环境未显式配置，Gradle 使用既有 debug fallback；不能把 Release 构建名等同正式签名。不能直接换 Android 密钥，否则已有安装无法覆盖升级。
- 正式发布前尚需：独立 Windows 安装/卸载/实际回滚验收、签名策略确认、持续在线耐久测试。macOS 运行与安装不在本轮 Windows 环境验证范围。

## 复现入口与证据

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-installer.ps1 -Version 1.0.7-rc.1 -ArtifactRoot ABSOLUTE_TEMP_DIRECTORY -SkipBaselineDownload -KeepHistoricalPackages
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build-android-mvp.ps1 -Configuration release -VersionCode 43 -VersionName 0.2.43-rc.1 -ArtifactRoot ABSOLUTE_TEMP_DIRECTORY
dotnet run --project src/Yanzi.SyncVerification -- --release-readiness ABSOLUTE_TEMP_DIRECTORY
python scripts/test-android-release-upgrade.py --serial emulator-5554 --baseline .artifacts/installer/yanzi-mobile-0.2.36.apk --candidate CANDIDATE_RELEASE.apk --output ABSOLUTE_TEMP_DIRECTORY/android-upgrade
```

桌面验证需先把 1.0.6 full 基线放入候选 installer 目录，候选只包含一个 `rc.*` full 和一个对应 delta。证据位于上述产物目录的 `desktop-release-verification.json`、`artifact-hashes.json`、`android-upgrade-final/android-release-verification.json`，Android 四个阶段的 SharedPreferences、SQLite 和文件哈希源样本均保留在临时目录，均为合成测试数据。
