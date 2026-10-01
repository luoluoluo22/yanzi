# 燕子 Android / 手机端开发状态

> 更新时间：2026-09-30  
> 适用范围：`mobile/android`、账号 Object Sync、Windows ↔ Android 跨端小程序数据、真实手机 Dev 开发闭环。  
> 本文记录“当前真实状态”，规则类约束仍以 `.agents/AGENTS.md` 为准。

---

## 1. 当前结论

燕子 Android 已从“仅发送文本的 MVP”推进到可用于真实账号验证的跨端客户端。

当前已经真实验证：

- Android 可登录现有燕子账号并注册手机设备。
- 手机可读取账号下的 Windows 小程序目录。
- 手机可读取燕幕组件。
- Windows 与 Android 已共享同一套 Object Sync 协议。
- 同一小程序可在 Windows / Android 使用相同 `extensionId`。
- Android `mobile-js` 已具备账号级小程序存储：
  `context.storage.readText / writeText / deleteText`。
- 小程序业务数据统一使用：
  `extensionData.v1.<sha256(extensionId + "\0" + key)>`。
- 已在真实账号、真实手机上完成：
  **Windows 写 → 手机读 → 手机改 → Windows 回读 → tombstone 删除**。
- 真机 Dev 与生产/历史版燕子可以并存，测试不会覆盖生产包。
- 手机开启 Clash VPN 时，燕子云请求可以绕过异常 Fake-IP 链路，改走底层 Wi-Fi/蜂窝网络。

截至本次验收，真实账号缓存中确认：

- Windows 小程序目录：27 个。
- 燕幕组件：11 个。
- 正式 `mobileExtension.v1.*`：当前账号尚未产生。
- 正式 `extensionData.v1.*`：测试对象均已 tombstone 清理，无活动测试垃圾。

---

## 2. Android 包与开发隔离

### 生产/历史包

```text
applicationId: cc.luoluoluo.yanzi.mobile
```

真实手机上的既有生产/历史版本不得因开发测试而：

- 卸载；
- `pm clear`；
- 覆盖安装；
- 修改其账号数据。

### Dev 包

```text
applicationId: cc.luoluoluo.yanzi.mobile.dev
label: 燕子 Dev
versionName: 0.2.24-dev
```

Dev 包与生产包可同时安装。

Dev build type 使用稳定 Android debug 签名，避免不同开发进程使用不同 `debug.keystore` 时无法原地覆盖，从而丢失 Dev 登录态。

构建：

```powershell
.\scripts\build-android-mvp.ps1 -Configuration dev
```

输出：

```text
mobile\android\app\build\manual-dev\yanzi-mobile-dev.apk
```

真机安装/启动 smoke：

```powershell
.\scripts\dev-real-phone.ps1
```

该脚本必须在安装前后核对生产包版本与 APK 路径，确保生产包未变化。

---

## 3. 当前跨平台架构

### 3.1 小程序身份

同一个小程序跨平台使用同一个 `extensionId`。

Windows 包和 Android `mobile-js` 是同一小程序的不同 runtime：

- 手机存在本地 runtime：优先本机执行。
- 手机无本地 runtime、账号存在 Windows 小程序：转发到电脑执行。
- 同时存在多个 runtime：允许显式选择执行端。

### 3.2 手机小程序定义

账号权威对象：

```text
mobileExtensions.index.v1
mobileExtension.v1.<sha256(extensionId)>
```

设备本地：

```text
SharedPreferences.mobileExtensions
```

只作为缓存/兼容状态，不作为账号权威来源。

Android 客户端实现：

```text
MobileExtensionDefinitionSyncClient.java
```

当前真实账号还没有正式写入过 `mobileExtension.v1.*`。因此要区分：

- 定义同步协议已实现并通过临时测试账号验证；
- 真实账号尚无正式手机小程序定义。

### 3.3 小程序业务数据

统一对象：

```text
extensionData.v1.<sha256(extensionId + "\0" + key)>
```

Windows：

```text
AccountExtensionDataStore
ExtensionStorageService
```

Android：

```text
MobileExtensionStorageClient
context.storage.readText(...)
context.storage.writeText(...)
context.storage.deleteText(...)
```

Windows 与 Android 共享：

- objectId；
- 全局 revision；
- expectedRevision；
- 409 conflict；
- tombstone 删除语义。

> Object Sync revision 是账号级全局递增号，不是每个对象从 1 开始。测试只能断言 revision 单调递增且各端看到同一 revision，不能假定 1 → 2 → 3。

---

## 4. 真实账号双向写入验收

2026-09-30 已在真实账号 + 真实 Android 手机完成完整闭环。

最终成功的一轮：

```text
Windows 初始写入       revision 811
手机读取               revision 811
手机修改               revision 812
Windows 回读           revision 812
Windows tombstone      revision 813
```

真机日志明确出现：

```text
Account storage diagnostic PASSED
```

该测试使用随机临时 `extensionId/key`，结束后执行 tombstone。

随后扫描本轮所有测试前缀：

```text
diag-readback-*
diag-primary-read-*
real-account-roundtrip-*
real-account-storage-diag-*
```

最终确认：

```text
ACTIVE_TEST_OBJECTS = 0
```

即真实账号没有遗留活动测试对象。

---

## 5. Cloudflare Worker / Object Sync 关键修复

### 5.1 直接根因：线上缺失单对象 GET 路由

本轮真机联调发现：

```text
PUT  /v1/sync/objects/{objectId}    成功
GET  /v1/sync/objects              列表可看到对象
GET  /v1/sync/objects/{objectId}   404
```

最终确认：

**单对象 GET 路由只存在于本地未提交代码，线上 Worker 当时没有部署该处理器。**

修复提交：

```text
8fc8acf  fix: make sync object reads primary-consistent
```

Cloudflare 自动部署后已在线验证：

- 新对象 PUT 成功；
- 同 objectId 立即 GET 成功；
- 手机 `context.storage` 可以读到 Windows 写入值；
- 双向 revision 正常推进。

### 5.2 D1 一致性保护

单对象读取增加兼容性保护：

```js
env.DB.withSession("first-primary")
```

运行时支持 D1 Sessions API 时使用 `first-primary`，否则退回普通 `env.DB`。

本轮已经证明的**直接故障根因是线上缺少单对象 GET 路由**；不要把它误记成“已经证明是 D1 副本延迟”。

### 5.3 回归测试补强

`scripts/test-cloud-object-sync.ps1` 已补上：

> PUT 创建对象后，必须立刻执行单对象 GET，并核对 objectId、revision、payload。

两种模式已通过：

```text
objectsAuthoritative=false   PASS
objectsAuthoritative=true    PASS
```

---

## 6. Clash / VPN 网络兼容

真实手机开启 Clash VPN 后，发现：

```text
sync.luoluoluo.cc.cd
→ Clash Fake-IP
→ HTTPS 请求约 10 秒后 Connection reset
```

这不是账号密码错误，也不是 Worker 返回 401/403。

当前 Android 已增加：

```text
MobileNetworkRouting.java
```

策略：

1. 正常网络可用时保持默认连接。
2. 活动网络为 VPN 时，查找底层 Wi-Fi/蜂窝：
   - `NET_CAPABILITY_INTERNET`
   - `NET_CAPABILITY_NOT_VPN`
3. Cloud 请求可通过底层物理网络建立 HTTPS。

已真实验证：

```text
默认 Clash 路径：Connection reset
底层 Wi-Fi 回退：成功
/health：ok=true
```

Manifest 已加入：

```text
android.permission.ACCESS_NETWORK_STATE
```

---

## 7. 设备在线状态

2026-10-01 真机联调发现：手机系统时钟相对 PC / 云端快约 4 分钟，而旧 Android 逻辑使用：

```text
手机本机时间 - desktop.lastSeenAt <= 120 秒
```

判断电脑是否在线，因此 PC 虽然持续每 30 秒正常心跳，手机仍会误判为“未上线”。

已修改为服务端权威时间：

1. Worker `GET /v1/me/devices` 新合同返回 `serverNow`。
2. 每个设备返回由服务端根据 `last_seen_at` 计算的 `online`。
3. Android 优先信任服务端 `online`。
4. 兼容尚未升级的旧 Worker：
   - 无 `online` 时优先使用 `serverNow - lastSeenAt`；
   - 若连 `serverNow` 也没有，最后才退回手机本机时间。

Worker 提交：

```text
9295a2d  fix: make device presence server-authoritative
```

新增回归：

```text
scripts/test-device-presence.ps1
```

真机验证时，即使手机时钟偏差已经超过旧版 120 秒在线窗口，燕子 Dev 仍正确显示：

```text
(云端在线)
```

因此“PC 实际在线但手机显示未上线”的问题已在 Android 兼容路径上实测修复。

## 8. 登录与账号状态

登录 UI 已改进：

- 点击后按钮显示“登录中…”；
- 弹窗内直接显示登录进度；
- 邮箱/密码为空立即反馈；
- 网络/API 失败直接显示原因；
- 日志只记录状态，不记录密码、Token。

登录成功后：

- Token 持久化；
- 注册手机设备；
- 自动刷新小程序目录；
- 自动刷新燕幕；
- 更新个人页状态。

真实账号登录已验证。

---

## 9. Android 稳定性修复

### Headless mobile-js WebView

后台运行 `mobile-js` 时：

- WebView 保持强引用直到 `done/fail`；
- 执行结束后主动 destroy；
- 避免执行过程中被回收。

### Headless UI 空指针

曾出现：

> mobile-js 业务已完成，但结束回调 `setStatus()` 访问尚未创建的 TextView，导致 App 随后崩溃。

已修复：

- `setStatus()` 允许 UI View 尚未创建；
- UI 更新切回 UI 线程；
- 集成测试增加 crash buffer 断言。

修复后：

```text
emulator crash buffer empty
full development smoke test PASSED
```

### Dev / Production 隔离

以下运行时标识不得写死生产 applicationId：

- Broadcast Action；
- Widget Action；
- taskAffinity；
- FileProvider authority。

统一基于：

```text
BuildConfig.APPLICATION_ID
${applicationId}
```

防止 Dev 与生产包串扰。

---

## 10. 当前主要文件

Android：

```text
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MainActivity.java
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MobileExtensionStorageClient.java
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MobileExtensionDefinitionSyncClient.java
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MobileNetworkRouting.java
```

Windows：

```text
src/OpenQuickHost/ExtensionStorageService.cs
src/OpenQuickHost/Sync/AccountExtensionDataStore.cs
src/OpenQuickHost/Sync/CloudSyncClient.cs
```

验证：

```text
src/Yanzi.SyncVerification/AccountExtensionBridgeVerification.cs
scripts/test-cloud-object-sync.ps1
scripts/test-device-presence.ps1
scripts/test-mobile-extension-storage.ps1
scripts/test-cross-platform-extension-storage.ps1
scripts/test-mobile-extension-definition-sync.ps1
scripts/test-real-phone-dev-object-sync.ps1
scripts/test-unified-extension-catalog.ps1
```

开发闭环：

```text
scripts/dev-check.ps1
scripts/dev-emulator.ps1
scripts/dev-android-loop.ps1
scripts/dev-desktop-loop.ps1
scripts/dev-real-phone.ps1
scripts/dev-smoke.ps1
```

---

## 11. 常用开发命令

```powershell
# 环境检查
.\scripts\dev-check.ps1

# Android Dev 构建
.\scripts\build-android-mvp.ps1 -Configuration dev

# 真机 Dev smoke
.\scripts\dev-real-phone.ps1

# 完整回归
.\scripts\dev-smoke.ps1

# Cloud Object Sync
.\scripts\test-cloud-object-sync.ps1 -AuthorityMode false
.\scripts\test-cloud-object-sync.ps1 -AuthorityMode true

# 设备在线状态
.\scripts\test-device-presence.ps1 -RequireOnlineDesktop

# 真机 Dev Object Sync（临时测试账号）
.\scripts\test-real-phone-dev-object-sync.ps1
```

---

## 12. 当前未完成项

### P0：整理并提交当前 Android / Sync 工作区

Worker 修复 `8fc8acf` 与设备在线修复 `9295a2d` 已提交并上线；本轮大量 Android、Windows 验证桥和开发脚本改动仍在工作区。

正式提交前至少执行：

1. review 当前 diff；
2. `git diff --check`；
3. Android Dev 构建；
4. Windows build；
5. Cloud Object Sync 两种模式；
6. 真机 Dev smoke；
7. full smoke；
8. 确认生产包未变化。

### P1：正式手机小程序管理能力

真实账号目前还没有正式 `mobileExtension.v1.*`。

下一步需要做用户可操作的：

- 手机小程序创建；
- 编辑；
- 删除；
- runtime 展示；
- 同 ID 的 Windows / Android runtime 管理；
- 冲突状态与 revision 展示。

完成后应在真实账号创建第一条正式手机小程序定义，验证：

```text
手机创建
→ 云端 Object Sync
→ Windows 目录可见
→ Windows 修改
→ 手机刷新
```

### P1：小程序云数据管理 UI

底层 `extensionData` 已通过真实账号双向写入验收，但目前主要由 runtime API 使用。

后续可增加：

- 按小程序查看 key；
- revision；
- 更新时间；
- 内容预览；
- tombstone；
- 冲突信息。

### P2：Object Sync Sessions 扩展

当前 `first-primary` 主要用于单对象 GET。

后续可评估把 changes/history/PUT 冲突判定等关键路径统一到 D1 Sessions API。

---

## 13. 安全边界

任何 AI / 自动开发流程都必须遵守：

- 不把密码、Token、Cookie 写进 Git。
- 不记录手机序列号、IMEI、Android ID 等唯一设备标识。
- 不为了签名冲突卸载或清空生产包。
- 正式账号测试只使用随机、明确前缀的临时对象。
- 测试完成必须 tombstone 清理。
- 真机破坏性测试只允许隔离 Dev 包。
- Worker 只通过 GitHub → Cloudflare 自动构建部署，不执行本地 `wrangler deploy`。
- Cloudflare Secrets 不写进仓库。
- 读取账号状态时，只输出非敏感结论，例如“Token 存在”，禁止打印 Token 本体。

---

## 14. AI 冷启动顺序

以后继续手机端开发时：

1. 阅读本文件。
2. 阅读 `.agents/AGENTS.md` 第 6 节。
3. 执行：
   ```powershell
   git status --short
   git diff --check
   ```
4. 检查 ADB：
   ```powershell
   adb devices
   ```
5. 确认真机生产包和 Dev 包并存。
6. 优先在模拟器做破坏性回归。
7. 真机只通过 `cc.luoluoluo.yanzi.mobile.dev` 验证。
8. 跨端数据问题先跑 Cloud Object Sync 单对象 GET 回归，再排查 Android。
9. 完成测试后确认活动测试对象为 0。

这样可以避免以后再次从 UI 现象重新反推整条同步链路。

---

## 15. 2026-10-01 消息、通知与双端状态闭环

Android 新增实际云端消息前台服务：5 秒串行收件、30 秒心跳、系统通知、持久化收据与 ACK、断网退避、通知禁用时保留 pending。服务从 Activity `onResume` 启动；通知权限请求码 9101 与相机 9001 分离。

Windows 启动消息轮询兜底，与 SSE 共用串行处理及进程内执行结果缓存。LAN 通知增加 Bearer 鉴权、UTF-8 字节读取和通知失败返回；Dev 使用 42982，生产使用 42981。

专项完整真机回归已通过：前台/后台/熄屏通知、通知权限恢复、断网补收、手机发往 Windows 的文本/命令及结果读取、LAN 鉴权与中文、进程重启补收、双端 120 秒离线过期和重新上线。测试使用隔离 Dev 包与临时本地 Worker，生产包保持原状并恢复 Dev 原配置。

入口：`scripts/test-real-phone-message-bridge.ps1`；详见 `docs/mobile-message-bridge-verification-2026-10-01.md`。这轮覆盖消息通信专项，Android 13+/14+ 真机仍需额外验证。未接入 FCM；PC 去重仅在当前进程有效。

本机 PC 的旧 API 端口 53919 位于 Windows 保留范围，已改为 42980 并验证 `/health` 返回成功。PC 主界面已由用户确认恢复。

最终 Dev APK 的完整专项回归产物：`%TEMP%\YanziDev\message-bridge\b0b341c429694c3082f399e45f6f77e3`。恢复账号后公网双向文本/通知也已通过；一次通知遇到网络超时后自动补收，实际 ACK 耗时 47 秒，不能保证固定 5 秒送达。

### 聊天窗口入口补齐

用户实际从 PC 的手机聊天窗口回复时，暴露出这个入口仍只支持 LAN；上一轮公网验证使用云 API，未覆盖这个窗口的发送逻辑。

已修改 `MobileMessageToastWindow`：从消息历史保留接收手机的真实 deviceId，没有 IP 或直连失败时，将文字投递到该手机的云队列；发送失败保留输入，云提交成功显示“已交云端，等待手机接收”。照片/文件目前仍要求直连，错误提示明确区分。

Android 将 `YanziChat` 云文本写入 `desktop_chat_history`，按 messageId 防止重复记录，并更新当前聊天界面。真机回归新增实际 WPF 聊天窗口发送入口测试：无 IP、失效 IP 两种路径均验证手机聊天记录；公网入口为 `scripts/test-real-phone-public-chat.ps1`，使用已有账号，仅发送明确测试前缀的聊天文字，不修改手机配置。

完整专项回归通过：`%TEMP%\YanziDev\message-bridge\8c7341c4a7a04f6a970431e8088451f4`。公网窗口无直连、直连失败转云端两种场景均通过：`%TEMP%\YanziDev\public-chat\9a22095dc7da43b590cfdf7acda5ea01`。PC 已构建并自动重启，手机更新仅作用于 Dev 包。

## 16. 2026-10-01 实时消息与双向附件补齐

在第 15 节基础上增加账号隔离的 Durable Object WebSocket、连接恢复补收、30 秒查漏兜底、断线 5 秒兼容轮询。Windows 保留旧 SSE，并在网络错误后继续重试心跳。Android 心跳与附件接收分别调度。服务端先写 D1 再发事件，接收 ACK 反馈到发送端。

PC 聊天窗口的文字、照片、文件均可转云端；Android 照片和文件发送不再依赖账号 WebDAV。云附件使用鉴权私有 R2 存储、30 MiB 限制、7 天清理、SHA256/大小校验、Range 续传。手机展示图片并可通过 FileProvider 打开文件；PC 历史保留本地附件路径。发送 clientMessageId 支持幂等和内容冲突检查。PC 命令收据持久化，重复处理与清空内存后读取收据均验证通过；中途退出不会自动重复执行，结果未知时提示人工确认。

用户确认尚未开通推送服务：实际 FCM SDK 可选构建和 HTTP v1 发送适配器已接好，HTTPS webhook 为厂商服务桥接入口。签名/失败分支使用模拟传输测试通过，带临时假配置的 FCM 构建通过；最终真机安装的是无 FCM 配置 APK。尚未验证真实系统推送唤醒，也未接入具体国产厂商 SDK。

最终不熄屏完整真机回归通过：`%TEMP%\YanziDev\message-bridge\b86dfc8f65d24fadae5731e740239e11`。真实 WPF 窗口发文字/文件/图片到实体手机、手机上传文件/图片到 PC 均通过内容哈希核对；WebSocket 连接、前后台通知、权限禁用后恢复、断网补收、手机命令/电脑结果、LAN 鉴权、进程恢复、双端 120 秒离线与恢复均通过。生产包保持 0.2.8，Dev 偏好已恢复。10 条本地消息提交到 ACK：中位数 68 ms，样本最慢/p95 87 ms，仅代表本机 Worker + adb reverse；不代表公网时延。

补充后端回归通过：`%TEMP%\YanziDev\mobile-backend\2a89db97512348a5ace6e413ce36cf72`，覆盖分段内容、越界 Range 416、错误哈希、跨账号不可读、删除、消息幂等/409、公开列表不暴露私有附件。PC 已构建并自动重启；最终 Dev APK 安装/启动 smoke 通过，无崩溃。

**尚未部署新后端**：线上旧版暂无新附件接口/实时服务。部署必须经过 Git main 自动构建，先应用 D1 0018 迁移并创建 DEVICE_RELAY。公网不同网络、真实推送以及 Android 13/14+ 后台限制仍待上线/服务开通后验收。接入及发布说明见 `docs/mobile-realtime-attachments-2026-10-01.md`。

用户操作偏好：日常回归不主动熄屏；`test-real-phone-message-bridge.ps1` 默认禁用主动熄屏，确需最终验收时显式 `-IncludeFinalScreenOff`，仅在最后执行一次。不要把日常无熄屏回归写成已重新验证熄屏；本轮早先的熄屏通知已通过，最终轮没有重复操作。

## 17. 2026-10-01 发布与公网真机验收

用户明确授权先 push 最新源码，再发布并验证公网。最新开发代码以 3bb3e0f 推送 main，Cloudflare Git 构建成功；线上应用 D1 0018 迁移，DEVICE_RELAY 绑定及 */30 清理 Cron 生效。发布修正 61acb66 也已推送且构建成功：按仓库要求移除已生效的首次 DO migration；pre-push 不再强制重新部署历史版本，保留 Secret 同步；PC 旧 SSE 兼容模式每两分钟重试实时连接。

最终公网验收通过：%TEMP%\YanziDev\public-chat\59948230f72141d68bc291e5fa7c21d4。使用已有账号和实体 Android Dev 包，没有 adb reverse、未主动熄屏：真实 PC 聊天窗口无直连/失效直连转云文字、双向公网文件和图片下载后哈希一致、两端 WebSocket 当前状态、手机强制停止期间 pending 保留及重新打开补收均通过。PC 构建后自动重启，/health 正常，生产手机包保持原状。临时云附件清理后再次核对数量为 0。

10 条最终公网通知提交到 ACK 的中位耗时 2934 ms，样本最慢/p95 4371 ms；前两组中位 2562/3253 ms，最慢 6563 ms，观察到 HTTP 超时自动恢复。不能把 ACK 当作通知显示延迟，也不能把本地 68 ms 当作公网指标；当前仍未达到稳定微信级时延。系统推送尚未开通，国产厂商 SDK、Android 13/14+ 真机及不同运营商覆盖没有在本轮验证。

公网验证脚本曾误读下载中的 .part 文件、依赖已轮转的旧连接日志，已改为校验最终文件并检查当前设备 realtime 能力状态。详见 docs/mobile-realtime-attachments-2026-10-01.md。

## 18. 2026-10-01 多设备聊天、旧正式版与公网更新

现场 hi/101 原来定向 K70 且一直 pending；K70 只有发送能力，在线不代表可收件。按用户要求本轮不操作 K70。一加在线实例此前是 Dev，正式包仍为 0.2.8、登录态过期且没有应用内更新入口。

新增 D1 0019 account_chat_receipts：聊天按账号同步、设备独立 ACK、离线/新设备补收，命令仍定向。HTTP/SSE/WS 共用收件规则；PC 登录后使用共享云队列并显示账号聊天，手机各注册入口报告版本及收件能力。原 pending YanziChat 保留 ID 转共享队列，hi/101/102 已在一加正式版可见。正式版和 Dev 的独立回执均已核对。

正式包签名一致后从 0.2.8 保留数据覆盖到 0.2.25，自动恢复凭据及实时收件。0.2.25 → 0.2.26 公网自动检查、90.8 MB 后台下载、APK 校验和系统安装器拉起已通过；Google Play Protect 正常扫描后提示“已屏蔽有害应用”，没有给出具体命中类别。未点击“仍要安装”、未关闭扫描、未以 ADB 绕过该次安装，正式版仍是 0.2.25，等待用户选择。不能写成完整自动升级已通过，也不能直接认定误报。

0.2.26 另包含旧内联 screenshot 兼容、更新包签名/版本/SHA256 和安装权限返回续接，构建通过但最终安装/新兼容图片仍需验收。公网更新清单暂保持 0.2.25。发布脚本自动更新 R2 清单并保留 Windows latest，未来 APK 发布必须使用 scripts/upload-release-installer.ps1 / scripts/publish-mobile-update.ps1，不能只发 GitHub 而遗忘公网清单。

后端回归通过：57759b6029a647c6b6f2e08dac416df8；真实 WPF 公网文字回归通过：198f4e814a9241eeaaaf58b77cf3f17a。一台实体一加上的正式/Dev 两实例独立 ACK，不等于两部实体手机验收。无主动熄屏、无数据清空；真实系统推送仍未开通。详情见 docs/mobile-account-chat-update-2026-10-01.md。

## 19. 内置便签与独立日历（2026-10-01）

本轮按用户选择实现两个模式：`quick-notes` 是燕子内可见 `mobile-js / mobile-view` 小程序；`cc.luoluoluo.yanzi.calendar` 是独立原生 Android 日历，Dev 包后缀 `.dev`。宿主升级到 0.2.27 增加通用签名权限 + extensionScopes 的存储 Provider，令牌不传给独立 APP。便签源文件和定义位于 `extensions/quick-notes`，Gradle 自动生成内置目录；存储调用改为异步，网络请求不阻塞便签编辑。

电脑日历 1.1.0 保留旧事项 ID、原 JSON 与首次备份，通过账号 ObjectSync 的 `taskbar-calendar / calendar.v1.json` 同步。两端均有持久化待上传修改、记录版本、整体 expectedRevision 和删除墓碑。同事项冲突保留本地版本，提供显式处理；账号切换不交叉上传。Windows 宿主增加通用 `/v1/account-storage/{id}` 版本化读写接口，其他小程序也可复用。

验证：Windows 构建/自动启动与日历动态编译重载通过；宿主与独立日历 Dev/Release 构建通过；模拟器集成测试验证 Windows↔Android 读写、CAS、记录冲突、离线草稿持久化及重连、删除、范围及签名权限拒绝。原 Windows↔Android 共享存储回归通过（含新异步桥接）。一加 Dev 真机公网完成日历手机新增→电脑、电脑改名→手机、电脑删除→手机，测试事项清理后原两条事项逐字段保持一致。便签草稿在结束进程后恢复，公网保存和删除可由 Windows API 核对。

本轮开始设备检查时，一加正式包已是 0.2.26（此前停留 0.2.25 的记录属于历史状态）；本轮没有执行该正式版升级。仅覆盖 Dev 到 0.2.27-dev，正式版 0.2.26 路径和版本保持一致。正式 0.2.27 APK 已构建，尚未安装或发布，公网更新清单未改。

边界：日历前台每 10 秒同步及保存后同步；独立日历关闭后的后台自动刷新、手机原生闹钟通知和系统日历写入尚未实现，闹钟由电脑端响铃。未做息屏测试、未操作 K70、未开通推送服务。操作、协议及完整验证见 `docs/mobile-notes-calendar-2026-10-01.md`；回归脚本为 `scripts/test-calendar-companion.ps1`，只能清理模拟器测试包。


## 20. 2026-10-01 应用中心与统一数据平台

- Android 0.2.28 新增通用应用中心：公共目录、账号应用库、内嵌定义获取、独立 APK 下载校验与系统安装确认。
- 云端新增 /v1/applications 与 /v1/extension-data 接口，兼容现有 extensionData.v1 对象；专用授权默认只读、有效期 1 小时，可撤销且不能访问账号级接口。
- JavaScript SDK 提供账号隔离的持久化草稿、CAS、断网恢复与显式冲突处理。授权暂为用户手动复制令牌，后续网页自动登录需要 OAuth/PKCE。
- Node 4 项测试及本地真实 Worker/D1 集成测试通过；Dev/Release 构建通过，OnePlus Dev 安装验证通过，正式手机版本 0.2.26 保持不变。公网发布与应用中心下载验证继续执行。
- 详见 docs/application-platform.md。

### 20.1 最终接入与验证

- 最终主应用 0.2.29 在开发页提供应用中心，获取后立即刷新；敏感授权令牌不进入剪贴板同步。独立日历 0.1.1 接入共用 Android data-sdk。
- Android SDK 提供 HostStorage、ScopedCloudStorage、YanziDocument；跨平台 JavaScript SDK 同时可用于网页与 Node。
- 日历和 Android SDK 共 3 项 instrumentation 测试通过，Windows/Android 往返与冲突验证通过；真实公网授权、撤销、两项下载哈希校验通过。
- 无本地应用缓存的第二客户端（模拟器）已获取账号中的便签定义和应用库选择，验证跨设备分发。OnePlus 真机仅更新 Dev，正式燕子仍为 0.2.26，全程未息屏。

### 20.2 公网发布闭环

- 代码已推送 main，Cloudflare Git 构建 564f5c2 已成功部署；没有本地部署 Worker。
- 公网更新清单为 android-v0.2.29，主 APK 实际下载及 SHA-256 校验通过。应用目录含便签 0.1.0 和独立日历 0.1.1。
- GitHub platform-sdk-v0.1.0 已发布 Android AAR 与浏览器/Node ES module。
- 模拟器通过应用中心下载日历 0.1.1，经过系统安装确认成功安装，首次打开显示已同步电脑日历；真机 Dev 日历 0.1.1 同样显示已同步。
- 已清理模拟器中的测试账号登录与日历缓存。物理手机正式燕子仍为 0.2.26，需要用户正常确认主应用升级；未绕过系统安装流程，未息屏。

## 21. 外部应用线上授权接口（2026-10-01）

- 主应用 0.2.30 新增应用中心 AI 接入地址，按小程序 ID + 数据 key + 最大权限创建七天邀请地址。GET 仅发现说明，POST 才产生五分钟待确认申请。
- 手机前台显示原生授权确认，后台消息服务运行时显示高优先级授权通知；每五秒独立检查，不占用聊天接收队列。电脑托盘提供接入窗口和原生确认弹窗。
- 用户确认后外部调用方凭私人轮询秘密自动领取一小时限定令牌，不需要主账号密码或人工复制令牌。只读、跨 key、撤销和重复决定均受服务端约束。
- 日历声明 records-v1 schema，通用服务提供逐条 GET/POST/PATCH/DELETE，与原共享存储保持同一对象和版本语义；修改删除必须携带记录 expectedVersion。
- 当前 Node/SQLite 授权与记录测试 5 项通过，真实本地 Worker/D1 集成通过。桌面构建并重启成功，Android Dev/release 构建成功，OnePlus Dev 0.2.30 安装 smoke 通过，正式 0.2.26 未变。公网和两端 UI 验证待发布后记录。
- 接入协议与例子见 docs/external-data-api.md。尚未配置系统推送，强制停止手机应用后不保证授权通知到达；不主动息屏。
