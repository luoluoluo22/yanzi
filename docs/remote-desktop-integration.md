# 燕子远控 — 平台接入

本仓库只负责**安卓应用的远控入口**，不包含 Windows 小程序包；遵守 `scripts/verify-miniapp-repository-isolation.ps1` 的仓库隔离要求。

- Windows 独立小程序：私有仓库 `luoluoluo22/yanzi-remote-desktop`（必须由仓库所有者授权访问）。
- Android 接入：`RemoteDesktopActivity.java` 与 `DesktopDashboardView.java`，在「电脑」页显示「远控」入口。`RemoteDesktopActivity` 声明配置变更由自身处理，横竖屏旋转不销毁 WebView/WebSocket；仍需 Redmi K70 真机验证连续桌面会话。
- 网关：由用户首次配置的 HTTPS MeshCentral 地址，保存在 Android 本地偏好设置。禁止把私有部署域名或远控账号、密码、令牌写入主仓库。
- 远控服务：自托管 MeshCentral，Nginx TLS 反向代理，Windows MeshAgent。

当前阶段：安卓编译已通过；真实手机远控、管理员 MFA、Agent 签名确认与公网开放尚未验证。**不得因本功能的源码已合并而默认启用无人值守控制。**

验收条件：账号授权、终止会话、Windows→Windows/Android→Windows 鼠标键盘与实时画面、断线重连、低延迟测试和安全审计。