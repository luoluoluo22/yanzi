# 燕子 VPN 管理（独立小程序）

## 范围
- `vpn-manager` 以 C# 原生窗口运行，不捆绑 VPN 协议或 WebView 运行时。功能：HTTPS Mihomo YAML 订阅、节点/策略组、节点延迟、规则/全局/直连、手动启动/停止核心、系统代理接管与回退。
- 默认不接管代理、不启用 TUN，不与正在运行的 Clash Verge 竞争 Windows 代理设置。
- 首次启用声明 `requires: ["mihomo>=1.19.31"]`：由燕子系统依赖管理器按需安装官方 Mihomo x64-compatible/ARM64 核心。安装器锁定官方 v1.19.31 ZIP SHA-256，提取后只保留单个 exe。
- 本机缓存使用 DPAPI 加密，临时 YAML 仅在运行期间解密到 `%LOCALAPPDATA%/OpenQuickHost/VpnManager/runtime`，正常关闭删除，窗口重启清理崩溃残留；不包含在扩展归档/恢复允许列表内。

## 配置、凭据、同步
- 在燕子 **设置 → 环境变量** 新建 `YANZI_VPN_SUB_URL`，值为完整 HTTPS 订阅 URL（包含令牌亦可）。或者在小程序中点击「导入 Clash Verge 订阅」，将原客户端的**第一条** HTTPS 订阅放入这个环境变量（覆盖已有值会确认）。
- 小程序云同步只保存 `vpn/settings.json`：**环境变量名称、代理模式、手动策略组的节点偏好**。原始订阅 URL、令牌和 YAML 不进入云配置对象或扩展包。
- 环境变量值由宿主的 `AppEnvironmentVariableStore`（本机 DPAPI）与 `AccountEnvironmentSecretVault`（已登录账号的 E2EE 对象）管理，插件只调用 `GetValue`，不复制令牌到自己的配置。
- 通过 `YanziActionContext.Storage.WriteJsonAsync(...,"both")` 使用现有扩展私有数据同步通道；本地 Agent API 关闭/不可达时，界面明确提示未确认云同步，不能把本地保存声称为跨设备成功。
- 新设备的恢复流程：登录账号 → 安装同步的小程序 → 恢复环境变量加密保险库 → 宿主按需安装 Mihomo → 拉取 `vpn/settings.json` → 重建本机配置 → 启动后重新应用节点选择。新设备服务/网卡/端口/系统代理不跨设备复制。

## 安全与回退
- 下载仅接受 HTTPS，不跟随未审核的重定向；拒绝超过 4MB 的配置、不支持 Base64 节点列表；把订阅 YAML 中任何 `tun`、外部控制入口、监听器和改写端口的顶层配置剔除，并强制 127.0.0.1:18990（API）、127.0.0.1:17990（mixed），TUN 关闭。
- API 密钥每次新下载随机生成，只用于本机回环通信；日志、提示不输出订阅 URL、Token、下载的 YAML。
- 检测到 Clash Verge 未退出时拒绝启动本小程序核心；系统代理已被其他程序接管时拒绝覆盖。只有用户明确点击才修改注册表。
- 接管代理前将原值保存到本地 `proxy-lease.json`，互斥锁保证单一接管实例；常规关闭立即恢复，崩溃留下的租约在下一次宿主启动（本小程序的 startup 任务）尝试恢复。**尚未验证整机掉电和权限异常场景**。
- **未实现**：Windows TUN 驱动/特权服务安装、VPN kill-switch、多个订阅切换、从服务端恢复到全新 Windows 的端到端真实验收。现阶段不宣称取代原客户端所有模式。

## 开发验证
```powershell
dotnet run --project tests/vpn-manager/VpnManager.Verification.csproj -c Release -- --ui-smoke --install-core
dotnet run --project src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -c Release -- --extension-data-catalog-tests
dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release
```

注意：本次工作属于隔离的开发工作区，不安装/重启正在运行的燕子正式版，也不会停止或改写现有 Clash Verge 服务。正式发布前需用无 Clash Verge 的全新 Windows 实机完成网络、云同步和回退验收，并检查 GPL-3.0 开源许可合规。
