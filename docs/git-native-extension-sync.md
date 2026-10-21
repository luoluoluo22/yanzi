# 燕子小程序：Git 原生云同步

## 设计边界

燕子宿主仓库只保存通用同步功能，小程序实际工作目录仍放在用户数据目录：
`%LOCALAPPDATA%\\OpenQuickHost\\Extensions\\<extensionId>\\`。该**小程序目录本身可以是独立 Git 仓库**（也支持 `.git` 文件形式的 linked worktree），与宿主 Git 仓库无关。运行状态、凭证继续放在小程序独立数据目录或既有安全存储中。

使用者可在此目录初始化 Git 仓库，提交源码，然后设置自己的 GitHub/Gitee `origin`。燕子账号云同步和个人/WebDAV 同步负责在设备间传递可执行源码/资源快照；Git 仓库负责提交历史、分支、合并与版本回退。**云同步永远不会复制或替换 `.git`**。

## 忽略规则

归档打包、依赖 DLL 枚举、扩展文件监听及最近变更扫描共用 `ExtensionSyncIgnorePolicy`：优先读取各级 `.gitignore` 和 `.yanziignore`，支持相对目录、通配符、`**`、目录匹配与 `!` 反向匹配（不能重新包含被排除的父目录）。`.yanziignore` 是追加排除层，不能取消 `.gitignore` 的排除。

无论忽略文件如何写，`.git`、运行时缓存、`node_modules`、典型密钥、`.env`（保留 `.env.example`）、备份及临时文件都不会进云同步包；`bin`／`obj` 排除沿用旧规则。安装包在正式解压前还会拒绝包含 Git 元数据/常见本机密钥的外来条目。

## 接收远端更新

如果本地还**没有** Git 仓库，沿用正常小程序安装逻辑（不自动初始化 Git，也不从 ZIP 引入 Git 元数据）。

如果本地已有 Git 仓库：

1. 内容一致直接跳过。
2. 对方上传的包来自**干净且已提交的 Git 工作树**时，附带 Git commit SHA（不含 Git objects/config）。本地必须同样是干净工作树，并且提交是本地 HEAD 的后代，才允许 `git merge --ff-only`。
3. 本地缺少该 Git 对象时，只尝试从**本地已配置的** `origin` 自动 fetch（不会信任 ZIP 指定的远端地址、不会提交或 push），然后再次验证快进条件。
4. 本地有未提交改动、出现分叉、提交不可获取、对方发来未提交的工作树快照：**不覆盖本地源码与 `.git`**。收到的包以 SHA-256 文件名保存至 `%LOCALAPPDATA%\\OpenQuickHost\\Backups\\git-extension-incoming\\<extensionId>\\`，记录异常；先用 Git 解决冲突再同步。
5. 远端个人同步中的删除／永久清除不会删除本地 Git 仓库，须本机显式操作。

**限制**：云同步不是完整 Git 托管服务，不传输 Git 对象/分支历史。设备首次安装小程序并不自动获得 Git 仓库；若需跨设备自动快进，设备应已连接同一个 Git `origin`，提交必须可 fetch。如果对方只修改未提交文件，云端可以备份，但 Git 工作树接收方会进入冲突保护，不自动生成合并提交。

## 首个试点：浣熊 MCP

现状为独立服务源码仓库 `raccoon-mcp`，与安装在用户数据目录的 `raccoon-manager` 小程序的服务代码副本并存。建议继续将运行服务保持独立进程，小程序负责控制／状态／安装；待安装**支持 Git 目录保护的新宿主版本**后，再将小程序目录作为单独 Git 工作树初始化并接入远端。不要用同步包的覆盖更新误替换正在运行的 MCP，且不能把设备密钥、运行时服务数据加入小程序仓库。

## 相关验证

`dotnet run --project src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -- --git-native-sync`

另外运行 `--package-stability`、`--sync-safety` 对现有小程序及云同步做回归测试。