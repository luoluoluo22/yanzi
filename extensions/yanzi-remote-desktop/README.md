# 燕子远控 / Yanzi Remote Desktop

## 当前实现（2026-10-22）

技术基座：MeshCentral 1.3.0，Apache-2.0。使用其浏览器远控桌面、鼠标键盘和 Windows Agent 能力，不重新发明屏幕传输协议。

- Windows：独立燕子 C# 小程序，入口 `RemoteDesktop.cs`。打开系统默认浏览器远控界面。
- Android：`RemoteDesktopActivity` 通过同源 HTTPS WebView 使用同一远控界面，位于燕子手机「电脑」页面的「远控」入口；WebView 限制非同源跳转，阻断混合内容和证书错误。
- 首次连接需配置网关地址，配置保存在设备本机私有目录。PC 小程序使用 `/配置 https://remote.example.com`，`/状态` 查询，`/清除` 删除本机配置。Android 通过「连接设置」更改。源码不记录私人部署域名。
- 不将远控账号密码、设备令牌或 Session Cookie 同步到云端。远控凭据由 MeshCentral 自己维护。

## 网关

服务端目录：`/opt/yanzi-remote-desktop-staging/`。
systemd 服务：`yanzi-remote-desktop.service`（非 root 用户 `yanzi-remote`，Node 22，监听 127.0.0.1:8444）。
Nginx 站点：`/etc/nginx/sites-available/yanzi-remote-desktop`，使用独立 HTTPS 域名和 Let's Encrypt 证书。
**初始部署应在管理员账号与 MFA 完成前保持 HTTP 503 维护锁。** 用户私有服务器的真实域名与部署状态不应直接写入公共仓库。
服务可以仅经本机访问，严禁直接开放 8444 到公网。

## 解除维护锁前必须完成

1. 在服务器的非特权服务账号环境下创建唯一管理员账号；由所有者设置密码，在 MeshCentral 配置 TOTP 2FA，不把密码加入源码或日志。
2. 测试服务启动、登录与 MFA、退出登录后会话失效。只有检查登录和权限控制成功后，才移除 Nginx 维护锁。
3. 从管理页面创建用户专属设备组，给需要受控的 Windows 电脑安装官方 MeshAgent，确认设备上线、桌面实时画面、鼠标单击拖动与键盘输入。
4. 在安卓真机安装 dev APK 测试方向、滚动、触屏光标、软键盘、断线重连；验证至少手机→Windows 与 Windows→Windows 两条路径。
5. 检查代理安装签名警告：启动日志出现 `Too few bytes to read ASN.1 value`。签名前不可默认视为安全安装，需确认官方二进制完整性、签名处理原因。
6. 记录 E2E 证据、复核生产权限、再决定是否合并主线或正式发布。不应把编译成功、网页 200、设备在线或消息 pending 算作完成。

## 安全边界

- 用户应能够看到当前远控会话并随时终止；无人值守访问仅允许账号所有者授权。
- 默认最小权限，不允许不属于本账号的人连接。
- 设备端需正式安装官方远控 Agent，Raccoon MCP、截图命令不是实时远控的替代品。
- Windows UAC 安全桌面、多屏、音频、剪贴板、触控体验仍需真实测试确认。
