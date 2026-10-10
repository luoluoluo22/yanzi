# 浣熊 MCP 小程序：统一依赖、空白 Windows 部署和跨设备调用

## 包结构

小程序位于 `extensions/raccoon-manager/`，包括 C# 管理窗口、PowerShell 启动器、完整的 Node 服务源码、`package.json` 和 `package-lock.json`。用户可以独立安装、卸载与禁用。

**Node.js 不随小程序包同步，也不在小程序中下载。** 小程序 `manifest.json` 声明 `"requires": ["node>=22.16.0"]`，燕子宿主的 `YanziCapabilityRequirementResolver` 负责检查、自动准备运行时、确认版本及 PATH。宿主统一 Provider 优先复用已安装 Node，缺失时尝试 WinGet；若 WinGet 不存在或失败，使用官方 Node LTS ZIP 安装到当前用户目录 `%LOCALAPPDATA%\OpenQuickHost\Runtimes\node`，验证官方 `SHASUMS256.txt` 后再解压、原子迁移。该便携版运行时可由其他声明 `node` 的小程序复用。

`stage-service.ps1` 只校验宿主准备好的 `node.exe`、`npm.cmd` 和最低版本，然后将服务代码部署到 `McpRuntime\raccoon\app`，基于 lockfile 的 SHA-256 缓存判断是否调用 `npm ci --omit=dev`。**`node_modules`、Node 二进制、密钥及运行时数据不进入小程序同步包。**

在小程序以外直接调用 `stage-service.ps1` 时，它要求环境已准备好；若系统缺少 Node，必须先通过燕子启动小程序或调用宿主 `dependency.ensure`，不会偷偷触发重复下载实现。

## 从空白电脑恢复

1. 安装燕子并登录账号，恢复「浣熊 MCP」小程序。
2. 燕子在启动前解析 `manifest.requires`，优先复用已有 Node。未满足时交由公共 Provider 安装（WinGet 或经官方 SHA-256 验证的免管理员便携版）。
3. 公共 Provider 更新当前进程 PATH，浣熊读取并校验 Node/npm，安装 lockfile 中的 npm 包。安装失败则不启动 MCP。启动器验证 `/health` 返回的服务名称，不能把其他 MCP 误认为浣熊；默认 3766 被其他程序占用且未配置公网隧道时，自动在 3767–3799 选择空闲端口并保存在本机 `.env`。 启动前会清理从其他 MCP 启动器继承的 `RACCOON_*` 环境变量，再以本机 `.env` 恢复端口、令牌和 Shell 权限，禁止外部会话配置覆盖本机隔离策略。
4. 首次启动时各设备本机独立生成随机 MCP token，监听 `127.0.0.1`；不跨设备复制该 token。设备登录与心跳向燕子账号目录注册。
5. 在网页或燕子工具侧选中同账号在线设备后，可调用其已授权的 MCP 能力。

## 安全与远程

- 网页「我的设备」读取燕子云端 `/v1/me/devices`，不会把 MCP 隧道地址或任何设备凭证同步到其他电脑。
- `device.raccoon.invoke` 通过已登录设备消息中继读取状态、工具清单或执行具体工具。高风险远程调用必须在目标机器的浣熊面板开启「允许同账号网页远程调用本机浣熊 MCP」。
- 新设备默认 `RACCOON_ENABLE_SHELL=0`。即使开启远程授权，Shell 操作也需由目标设备独立开启。
- `list_devices` 和工具参数 `deviceName` / `deviceId` 可以通过账号目录路由到其他 Windows 设备。手动 `remoteDevices` 配置继续兼容。
- 当前网页 MCP 入口仍依赖至少一台在线设备作为网关；此改造不提供永久在线云网关。

## 验证边界

必须覆盖：宿主构建、`dependency.status` / `dependency.ensure` 回归、Node 首次便携安装、重复安装缓存、npm 安装/缓存、独立扩展 C# 语法、Node 服务原有测试。安装成功不能代替在**另一台真正全新电脑**上的端到端验证。发布宿主和安装小程序是独立步骤。

不得将 `.env`、设备身份、账号 token、OAuth 状态、执行日志、缓存或用户数据加入源代码或小程序同步包。
