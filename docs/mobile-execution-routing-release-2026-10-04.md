# Android 0.2.47：电脑执行路由与手机页

手机执行电脑小程序时，旧请求只指定 desktop 平台。账号存在历史电脑登记时，旧后端可能返回 409 target_device_required。新版优先指定已连接的局域网电脑，或账号下唯一在线电脑的 deviceId；无法唯一确定时保留后端拒绝，不任意选择多台在线电脑。

局域网执行成功后直接使用执行回执，不再为已完成的请求继续发送云端执行消息；手机保留即时结果供现有结果查询显示。局域网失败继续使用现有云端队列及操作标识。

手机底部“开发”改为“手机”。移除电脑小程序列表中的应用中心入口；手机页保留应用中心、小程序、文档和终端。

验证：Dev / Release 构建通过；Worker Node 28 项测试通过，包括唯一在线电脑与历史登记、多台在线电脑拒绝选择、幂等和设备路由。正式 APK 包名 cc.luoluoluo.yanzi.mobile，versionCode 47，versionName 0.2.47；签名证书与旧正式版一致。

APK 大小 90883444 字节；SHA256 47e11d683370cb8d1cc27dbf9d69930694a5887f1f56737eb4a1d8eb16d9d78e。证书 SHA256 8a0ec0b84d1a05edcc89dd020bf81901f9ed7f083887db7c05201c59b31e1ee3。

真实用户网络上的电脑小程序执行由用户升级后复测；本地与隔离测试不能代替现场结论。Worker 路由修复通过 main 推送触发 Git 集成发布，不执行本地 Worker 部署。

## 发布与真机验证完成

2026-10-04：代码提交 c7b5227 已推送 main，Workers Builds: yanzi-sync 成功。GitHub android-v0.2.47 已正式发布（非草稿、非预发布），资产大小和摘要一致；R2 手机 APK 与更新清单已发布。独立完整下载公开 APK，SHA256 与本地构建及公开清单一致。

一加 81f7e66d 的隔离 Dev 真机回归 `test-real-phone-message-bridge.ps1 -SkipBuild -CapabilitiesOnly` 通过：85 项设备协议检查、前后台消息 ACK、后台心跳、文件字节校验、增量与周期同步、删除墓碑及路径越界拒绝。结束时 DEV_SERVER_ADDRESS_RESTORED、PRODUCTION_PRESERVED、DEV_PREFERENCES_RESTORED=True 均通过。测试产物：`%TEMP%/YanziDev/message-bridge/f7f344d18ecf48deb0f48ff524862d74`。Dev 已覆盖升级到 0.2.47-dev；真实手机正式包保持 0.2.45，不清除用户数据。桌面已重新启动。

更新入口：https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-0.2.47.apk 。本次隔离回归没有操作用户电脑小程序业务，原始 409 场景仍由用户在真实账号和网络中升级后验收。


## Dev 404 follow-up and device removal semantics

The physical OnePlus Dev registration had been deleted from account device management. An execution request using that source ID returned `404 device_not_found`. The UI now checks registration on foreground entry and clears token/password when removal is confirmed. Transient network failures do not log the user out. Background registration remains forbidden for removed devices; only an explicit successful account login requests re-enrollment.

New login derives an account-scoped SHA-256 device ID from Android `ANDROID_ID` and the normalized account email. It retires the previous registration held by this installation after successful enrollment. Existing authenticated sessions retain their current registration until the next explicit login. Reinstallation under the same signing key, Android user, device and account derives the same ID. Factory reset, another system user or a changed signing key can change the Android ID. Old orphan records cannot be safely matched by model name alone.

Dev/debug builds default to excluding the offline Chinese Vosk wake model and `libvosk.so`. The wake feature reports that it is unavailable in the slim development build; ordinary system speech input remains available. Opt in with `-PYANZI_BUNDLE_WAKE_MODEL=true`; release retains the voice assets. APK contents were checked for zero wake-model files and zero Vosk native libraries. If an old incremental APK retains unused ZIP padding after changing packaging, remove the single generated APK and rebuild to compact it.

Physical verification uses the actual Android client via the isolated Dev instrumentation runner, selecting only `taskbar-calendar` and empty input. First run confirmed terminal success for the app/LAN route `d0b2146a2de14ab7ad9e0d703e8b7528` and cloud route `msg_7da665b9666f6c428b32dad9`; desktop logs corroborate execution. Additional disposable-registration checks verify removal rejection without deleting user devices. Production app installation is unchanged.


Final physical run passed all four assertions: APP/LAN `ada6b9fa458044d3b7178b35c8532b5d`, cloud `msg_a2ee67138127eda77b55cd98`, removed registration detected/background re-enrollment rejected, and login token/password cleared using the isolated test context. Desktop logs confirm both calendar requests. One intermediate run coincided with desktop restart: the command completed after the test's 35-second wait had ended; its eventual terminal success was checked before retesting, with no duplicate resend of that request. Installed final Dev APK is 9,064,046 bytes (about 9.1 MB). Formal 0.2.47 release artifacts remain the earlier published build.
