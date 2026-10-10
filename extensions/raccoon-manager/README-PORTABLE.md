# 浣熊 MCP 小程序 · 空白 Windows 部署和跨设备调用

## 软件包与设备数据

小程序发布包位于 `extensions/raccoon-manager/`，包含 C# 界面、PowerShell 启动器、Node 运行时安装器，以及 `service/` 中的完整 JavaScript 源码、`package.json`、`package-lock.json`。它不是宿主内置代码，必须允许用户单独安装、卸载或禁用。

`node_modules` 不进入同步包；安装时由 `npm ci --omit=dev` 按 lockfile 恢复。设备私有配置统一位于 `%LOCALAPPDATA%\OpenQuickHost\McpRuntime\raccoon`。不得将 .env、token、OAuth 状态、日志或执行缓存发布或同步。

## 完全空白机器安装

1. 安装燕子、登录同一个燕子账号，并安装「浣熊 MCP」小程序。
2. 小程序启动调用 `managed-start.ps1`，进而执行 `stage-service.ps1`。
3. `ensure-node.ps1` 先寻找受支持的本机 Node（>=22.16.0），若找不到，自动下载最新 Node 24 LTS Windows ZIP 到当前用户的 `OpenQuickHost\Runtimes\node`（无需管理员权限）。下载来源为 nodejs.org，使用官方 SHASUMS256.txt 校验，校验失败拒绝安装。
4. 将小程序中的完整 service 代码部署到 `McpRuntime\raccoon\app`，依据 lockfile 哈希变化执行 npm ci，失败则不启动服务；本机随机生成独立 token。服务只绑定 127.0.0.1。
5. 燕子登录后的设备心跳会将设备注册到同账号设备目录。网页「我的设备」可见设备和浣熊安装/授权状态；无需将公网 IP、Node 安装目录或私钥拷贝到其他电脑。

## 网页如何使用新电脑

- 网页的「我的设备」使用燕子云端 `/v1/me/devices` 发现已登录设备。
- 网页 ChatGPT 中的「燕子 MCP」通过 `device.list` 读取设备；通过 `device.raccoon.invoke` 选择目标设备（按设备名或 ID），可执行 `status`、`list` 或 `call`。
- 这些请求经过当前在线的燕子 MCP 网关，使用燕子云端已认证的设备消息执行通道转发给目标燕子宿主，再调用目标机器本地的浣熊 MCP；无需每台电脑都有公网隧道。
- **隐私与安全：**设备被发现不代表允许远程执行。需要在目标机器浣熊小程序勾选「允许同账号网页远程调用本机浣熊 MCP」。不允许远程执行时仅能读取状态与工具清单。公网 OAuth/Cloudflare 隧道需单独配置，不会因新电脑安装而自动暴露。
- **命令权限单独控制：**新安装的浣熊默认 `RACCOON_ENABLE_SHELL=0`。即使开启账号远程执行，`shell_run` 仍受此开关限制，只有用户在目标机明确配置并重启 MCP 后才能执行 Shell 命令。
- 同时，**浣熊 MCP 插件本身**的 `list_devices` 会通过本机已登录燕子的 Agent API 自动发现同账号 Windows 设备，并允许给任意现有 MCP 工具传入 `deviceName`（如 `DESKTOP-HSCA8C5`）或 `deviceId`；调用通过账号中继转发至远程电脑。现有 `remoteDevices` 手动 URL 配置仍兼容，用于不属于本燕子账号的独立 MCP 实例。
- 新路径是 **网页浣熊 MCP → 本机燕子账号设备目录 → 燕子认证设备中继 → 目标燕子宿主 → 本机浣熊 MCP**。远端必须安装并运行新版燕子与浣熊扩展，且高风险执行权限需要在远端显式开启。
- 当前入口燕子 MCP 网关本身仍需有一台在线机器承载；未部署独立云端网关时，若网关机器关机，网页插件不能通过这条通道唤醒它。

## 验证

测试项目：`ensure-node.ps1 -ForcePortable` 真正下载并校验 Node；隔离 `LOCALAPPDATA` 后进行 `stage-service.ps1` 与 npm ci；以服务运行时文件验证 `yanzi-bridge.js` 能读取工具列表并调用 ping；编译燕子宿主，确认网页设备页面能够识别能力状态。

不要把同一台电脑的设备身份或云端登录凭证复制到另一台电脑。另一个新电脑必须独立登录并注册自己的设备 ID。
