# 应用分发与统一数据接口

## 边界

应用目录、账号应用库、本机安装状态、业务数据是四个不同层次。账号应用库同步用户获取了哪些应用；每台手机自行确认安装 APK。便签使用 `mobile-js` 定义，在燕子内运行；日历使用独立 APK，继续对应电脑小程序 `taskbar-calendar`。

燕子 Android 0.2.28 增加“应用中心”。旧手机先通过主应用更新渠道升级，之后可以获取便签、下载安装日历。便签获取时写入既有账号定义对象与索引，其他手机刷新小程序列表即可获得定义。获取不迁移、不清空业务数据。独立日历要求正式燕子至少 0.2.27；开发版日历仍连接开发版宿主，正式版连接正式版。

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

`node --test cloudflare/src/application-platform.test.mjs sdk/javascript/yanzi-data.test.js` 验证作用域、只读、撤销、CAS、账号隔离、断网持久化及上传中再次编辑。`scripts/test-application-platform.ps1` 使用独立临时本地账号和 Worker 验证真实 JWT、D1 和旧同步接口隔离，不访问用户业务数据。
