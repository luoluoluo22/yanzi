# 应用分发与统一数据接口

## 边界

应用目录、账号应用库、本机安装状态、业务数据是四个不同层次。账号应用库同步用户获取了哪些应用；每台手机自行确认安装 APK。便签使用 `mobile-js` 定义，在燕子内运行；日历使用独立 APK，继续对应电脑小程序 `taskbar-calendar`。

燕子 Android 0.2.29 在手机开发页和电脑小程序页增加“应用中心”。旧手机先通过主应用更新渠道升级，之后可以获取便签、下载安装日历。便签获取时写入既有账号定义对象与索引，其他手机刷新小程序列表即可获得定义。获取不迁移、不清空业务数据。独立日历要求正式燕子至少 0.2.27；开发版日历仍连接开发版宿主，正式版连接正式版。

## 分发协议

`GET /v1/applications/catalog` 无需登录，返回 `schemaVersion:1, applications:[]`。条目包含 applicationId/name/kind/version/downloadPath/size/sha256/minHostVersionCode；APK 额外声明 packageName/versionCode/certificateSha256。下载只能访问同服务 HTTPS 的 `/downloads/applications/`，先验证大小、SHA-256，再验证 APK 包名、版本和证书。发布者凭服务端 R2 权限发布，不能由普通账号覆盖公共目录。

`GET /v1/applications/library` 需要账号登录，返回 applicationId/enabled/revision。`PUT /v1/applications/library/{id}` 传 enabled、expectedRevision；不匹配返回 409。安装状态不写入共享选择对象，避免误认为另一台手机已经安装。

发布脚本 `scripts/publish-application-catalog.ps1 -CalendarApkPath <正式APK>` 从便签源码生成独立定义、校验正式日历 APK、先上传资源再发布目录。`-PrepareOnly` 仅生成本地产物。未来更多应用使用相同条目协议，不需要增加业务专用宿主入口。

## 统一数据 API

`GET /v1/extension-data/{extensionId}?key={key}` 返回 `{ok, accountId, exists, revision, content}`。

`PUT` 同路径传 `{accountId, expectedRevision, content}`；写入上限 256 KiB；过期版本返回 409，账号不一致返回 409。数据继续使用 `extensionData.v1.SHA256(extensionId + NUL + normalizedKey)`，与电脑和手机已有数据兼容。便签 key 为 `notes.v1.json`，日历为 `calendar.v1.json`。

宿主可使用账号登录令牌。第三方应用必须使用专用授权令牌：用户在“应用中心 → 授权开发者应用访问数据”填写小程序 ID、应用名称、只读或读写，并确认。令牌有效期一小时，默认只读；不能读取账号配置、设备列表、其他小程序或申请新的授权。

授权接口 `POST /v1/applications/{id}/grants` 仅宿主登录态可用，传 `{userConsent:true, clientName, access:"read"|"read-write"}`；返回 accessToken/grantId/expiresAt。`DELETE /v1/applications/{id}/grants/{grantId}` 立即撤销；即使原令牌尚未过期也失效。此阶段使用用户手动复制专用令牌，不包含网页自动登录跳转、OAuth PKCE 或长期刷新令牌；不要把账号令牌交给第三方。

## 开发者 SDK

`sdk/javascript/yanzi-data.js` 同时用于浏览器与 Node，封装认证、读写、版本校验、按账号持久化草稿和上传队列、断网重试及显式冲突处理。无需自行实现云对象 ID 或账号同步请求：

```js
import {YanziDataClient, YanziDocument} from './yanzi-data.js';
const client = new YanziDataClient({
  baseUrl: 'https://sync.luoluoluo.cc.cd', extensionId: 'quick-notes',
  token: () => userApprovedScopedToken
});
const initial = await client.read('notes.v1.json');
const document = new YanziDocument({client, key:'notes.v1.json',
  accountId:initial.accountId, storage:localStorage});
await document.sync();
document.save(JSON.stringify(myNotes)); // 立即持久化，不依赖联网
const result = await document.sync();
if (result.status === 'conflict') showConflictDialog();
// 用户选择后 document.resolve('local' 或 'cloud'); 再 sync()
```

调用方在恢复联网、回到前台或用户保存时调用 sync；SDK 不隐式启动后台轮询。授权过期后需要用户重新授权；409 返回 retry 时保留草稿，下一次 sync 获取最新状态。SDK 默认处理整个文档的冲突，不擅自决定业务记录的合并。已有日历的逐条记录合并继续保留；业务 schema 和冲突展示仍由开发者定义。

## 验证

Android 共用 SDK 为 `mobile/android/data-sdk`，Release 产物是 AAR。`HostStorage` 用于与燕子同签名的独立应用；`ScopedCloudStorage` 用于其他开发者签名的应用，调用统一 HTTPS 数据接口；两者都实现 `YanziStorage`。`YanziDocument` 在 SharedPreferences 中按账号和小程序持久化草稿，提供 save/sync/resolve；sync 必须在后台线程执行，保存不会被网络请求阻塞。独立日历 0.1.1 已实际接入 HostStorage，保持原来的逐条事项合并规则。

同签名伴生应用还需在 Manifest 声明 `<uses-permission android:name="宿主包名.permission.EXTENSION_STORAGE" />`，在 application 下声明 `<meta-data android:name="yanzi.extensionScopes" android:value="my-app" />`，并在 queries 中声明宿主包名。不同签名开发者使用 ScopedCloudStorage，不会获得宿主账号凭证。

```java
YanziStorage storage = new ScopedCloudStorage(baseUrl, "my-app", () -> approvedToken);
JSONObject first = storage.read("data.json");
YanziDocument document = new YanziDocument(storage, preferences, "my-app", "data.json", first.getString("accountId"));
document.sync();
document.save(jsonText);
executor.execute(() -> { /* document.sync(); 按返回状态展示冲突或等待重试 */ });
```

`node --test cloudflare/src/application-platform.test.mjs sdk/javascript/yanzi-data.test.js` 验证作用域、只读、撤销、CAS、账号隔离、断网持久化及上传中再次编辑。`scripts/test-application-platform.ps1` 使用独立临时本地账号和 Worker 验证真实 JWT、D1 和旧同步接口隔离，不访问用户业务数据。

Android 日历测试包含 3 项 instrumentation 测试，覆盖共用 SDK 草稿恢复和上传中编辑，以及日历逐条冲突与 Windows 往返。`scripts/test-public-application-platform.ps1` 仅读取真机 Dev 凭证到内存，验证公网目录下载哈希、限制访问和撤销。`scripts/test-second-device-application-library.ps1` 仅允许模拟器，Prepare/Verify/Cleanup 分别准备无缓存客户端、验证账号便签定义与应用选择、清理测试登录；不会清理物理手机。

## 已发布渠道

- 主应用：https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-0.2.29.apk
- 独立日历：https://sync.luoluoluo.cc.cd/downloads/applications/yanzi-calendar-0.1.1.apk
- SDK：https://github.com/luoluoluo22/yanzi/releases/tag/platform-sdk-v0.1.0
- 公共目录：https://sync.luoluoluo.cc.cd/v1/applications/catalog

2026-10-01 已验证：真机 Dev 获取便签并加入账号；无本地应用缓存的第二客户端获取同一账号的便签定义和应用库选择；模拟器通过公网目录校验下载、系统确认安装日历 0.1.1，首次打开成功读取电脑日历；主应用 0.2.29 公网 APK 哈希一致。正式真机仍为 0.2.26，升级需用户正常确认。
