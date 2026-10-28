# 账号聊天、旧版兼容与公网更新验收

## 用户现场问题

PC 于 16:24 发送的 `hi` 和 `101` 原来定向给 Redmi K70，均处于 pending、无 delivered_at。16:27 K70 仍可向 PC 发送，但能力只有 shareText/sendToDesktop，没有 receiveMobileMessages。在线表示最近访问过服务，不能证明旧 APK 有收件服务。一加在线的设备原先是 Dev 包；正式包仍是 0.2.8，登录态已过期，且没有应用内更新入口。

本轮按用户要求不操作 K70、默认不熄屏。没有卸载或清空正式包。

## 修复

- 用户聊天按账号同步至其他 Android/Windows 实例；命令保留原来的设备/平台定向路由。服务端决定 accountChat，客户端不能将命令伪装成广播聊天。
- D1 0019 增加 account_chat_receipts，每个接收设备独立 ACK。一台 ACK 后其他设备仍能补收，发送者不重复收到自己的消息。默认聊天补收期限 30 天，引用附件的消息不超过附件的 7 天存储期限。
- 原 pending 的 YanziChat 保留原消息 ID 和正文转为共享聊天，恢复 hi/101/102；历史已完成消息不批量重新投递。
- HTTP、SSE、WebSocket 查漏使用同一收件规则，实时广播和可选推送使用相同账号边界。
- PC 聊天显示“账号消息 · 所有设备”，登录后走云端共享队列，避免单一 LastKnownMobileIp 使某一部手机独占消息。回执提示“已有设备接收，其余设备会继续同步”。未登录时保留 LAN 兼容。
- 手机所有注册入口报告版本、包名、消息协议和收件能力；避免 MainActivity 把前台服务能力覆盖成只有发送能力。设备接口返回 needsMessageUpgrade。
- Android 0.2.26 增加旧 screenshot 内联图片接收兼容，更新 APK 的包名/升级版本/签名/SHA256 检查，以及安装权限授权返回后的续接。编译通过；0.2.26 的最终安装被系统扫描拦截，不能宣称已完成该版本的完整真机验证。

## 实际验证结果

后端回归通过：`%TEMP%\YanziDev\mobile-backend\57759b6029a647c6b6f2e08dac416df8`。覆盖账号聊天幂等、两台独立 ACK、发送者排除、新/离线设备在其他设备 ACK 后补收、命令隔离、附件有效期、Range/校验/删除/跨账号权限。

PC 构建成功并按 --tray 自动重启，14 个既有警告、0 错误。实际 WPF 窗口公网文字回归通过：`%TEMP%\YanziDev\public-chat\198f4e814a9241eeaaaf58b77cf3f17a`。无 LAN/失效 LAN 场景均在一加 Dev 历史落地；云端另核对这两条消息分别具有正式版和 Dev 的两个 ACK。本轮是一台实体一加上的两个应用实例，不代表已经验证两部实体手机。

一加正式包经签名比对后从 0.2.8 原地升级到 0.2.25，保留设备身份和原数据。前台服务自动重新登录恢复过期凭据、建立实时连接并补收。正式版 UI 确认 hi/101/102 可见；101 的正式版 ACK 为 2026-10-01T08:45:49.489Z，Dev ACK 为 08:42:17.665Z。

正式版 0.2.25 自动检查公网清单，发现 0.2.26，手机直接从 sync.luoluoluo.cc.cd 下载 90,790,669 字节，HTTP 200，约 20 秒下载完毕并通过 APK 校验。没有 adb reverse。经应用“立即安装”进入 Android 系统更新安装器；系统允许来源设置后，Play Protect 要求扫描。执行正常扫描后显示“已屏蔽有害应用 / 此应用可能有害”，详情没有具体类别，仅有“仍要安装”。没有点击该选项，也没有关闭扫描或用 ADB 绕过这次安装结果。当前正式包仍是 0.2.25；用户是否继续安装的选择待回复。

0.2.26 APK SHA256：`76717eb4a3f7434dedf4a6c56c8ba0c421c3894a46ed966fc34975d33f3dcc3c`。GitHub asset digest 与本地一致。此结论证明构建/发布文件一致，不能证明 Play Protect 是误报。拦截原因待进一步排查或开发者申诉；参考 [Google 官方开发者指南](https://developers.google.com/android/play-protect/warning-dev-guidance?hl=zh-CN)。

更新日志与拦截截图保存于 `%TEMP%\yanzi-production-update`。未重新验证熄屏、两部实体手机、真实系统推送或 0.2.26 的完整附件回归。

## 发布与后续入口

源码 10338c5 / a14aacd 已推送 main，Cloudflare Git 自动构建成功；没有执行本地 Worker deploy。0019 已在线应用并登记 d1_migrations。

APK 发布入口 scripts/upload-release-installer.ps1 现在自动调用 scripts/publish-mobile-update.ps1：验证生产包/版本/签名及 GitHub digest，先上传公网 R2 APK，再原子替换公开更新清单。Android Release 不再抢占 Windows 的 latest；Windows latest 已维持 v1.0.6。Release 创建失败会立即停止，避免对不存在的 Release 重复上传。

Android 0.2.25 / 0.2.26 已发布 GitHub 和 R2。由于 0.2.26 被 Play Protect 拦截，默认公网更新清单暂时保持 0.2.25，防止其他设备反复被提供尚未完成安装验收的版本；0.2.26 APK 保留供复核。待解决拦截后通过发布脚本恢复渠道版本。0.2.8 没有旧更新入口，首次升级需要正常覆盖安装，不能承诺旧 APK 无需更新即可获得新协议。

FCM/厂商系统推送仍未开通。后台强杀后的系统唤醒不属于本轮已验证结果。
