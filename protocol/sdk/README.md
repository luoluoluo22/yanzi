# 通用设备客户端 SDK

`device-client.mjs` 不依赖 Android、Windows UI 或平台枚举。设备用稳定 ID 注册，以 capability 描述接收能力。浏览器使用 `IndexedDbDeviceStore`；Node 22.13+ 使用 `FileDeviceStore` 的 SQLite 事务存储。手机/电脑原生客户端继续使用各自持久化接收器。

```js
import {DeviceClient, IndexedDbDeviceStore} from './device-client.mjs';

const client = new DeviceClient({
  baseUrl: 'https://your-yanzi-server.example',
  accountId: approvedAccountId,
  deviceId: persistentDeviceId,
  platform: 'web',
  displayName: '我的网页',
  store: new IndexedDbDeviceStore(),
  getToken: async () => approvedDeviceCredential,
  receive: async message => saveToAppInbox(message)
});
await client.register();
await client.send({kind: 'capability.invoke', targetDeviceId: approvedDesktopId,
  clientMessageId: crypto.randomUUID(), payload: {name: 'notes.read', payload: {}}});
await client.flush();
await client.sync();
```

这里的 `approvedDeviceCredential` 由账号所有者授权产生，网页不能自行申请全权权限。注册需要 `device.presence`；发送便签读取需要 `capability.invoke:notes.read` 和目标设备白名单；接收需要 `messages.receive`。不要向网页交付账号所有者 Token。

所有者接口：`POST /v1/me/devices/{deviceId}/credentials`，参数为 `applicationId`、`scopes`、`targetDeviceIds`、`fileRoots`、`lifetimeSeconds`。GET 同路径只返回授权元数据；DELETE `/v1/me/devices/credentials/{credentialId}` 即时撤销。凭据不写入 SDK 的消息队列，`getToken` 负责提供当前有效凭据。

Node 使用 `import {FileDeviceStore} from './file-device-store.mjs'` 和 `new FileDeviceStore(privateDirectory)`。目录必须由该设备用户拥有；Unix 文件为 0600、目录为 0700。关闭时调用 `store.close()`。浏览器需 HTTPS 安全上下文。远端明文 HTTP 被拒绝，localhost HTTP 仅用于开发夹具。

运行时自行实现 `execute(message)` 并只分发已授权、已实现的能力。操作系统权限仍由该平台适配器检查。SDK 在副作用前持久化并获取执行权，执行完成先持久化结果再 ACK；崩溃或丢失执行权响应返回 `unknown`，不会自动再执行。两个共享事务存储的实例仅一个能获取操作。传输保证为至少一次投递；它不提供跨操作系统业务数据库的分布式恰好一次事务。

发件箱先落盘再发送，网络失败保留原始 ID、期限与请求；每批最多 20 条，重试指数退避最高 60 秒。待发送配额 1000；终态记录保留 7 天/最多 5000 条。应用在连接恢复、实时唤醒以及定时调度时调用 `flush/sync`，不要把收到 WS 事件当作处理完成。

`sync()` 从未确认消息开始按服务器 sequence 分页补读，保存 cursor，但下次仍允许补读 cursor 之前丢失 ACK 的消息。Inbox 记录保留供业务审计；平台维护程序只能在消息期限及去重窗口结束后清理，不能清掉仍在执行或待确认的操作。

协议与加密黄金向量位于 `../vectors/lan-aead-v1.json`，同一向量由 JS、C#、Android 验证。原生设备同账号登录后会在后台自动取得设备间连接信息，再按 AEAD 协议封装；页面不展示密钥；本 SDK 的云端 HTTP 客户端不会尝试明文 LAN 或隐式发现设备。

验证：

```powershell
node --test protocol/sdk/*.test.mjs
node protocol/sdk/verify-worker.mjs http://127.0.0.1:8811
```

第二条只允许仓库的隔离 Worker 夹具（已应用迁移 0022–0024，临时测试密钥）。它验证受限网页凭据 → 新设备适配器 → 实际文件写入 → 结果回读，同时检查双实例去重与终端权限拒绝。不使用线上账号。

iOS、嵌入式设备可按相同存储、HTTP 和运行时接口接入；其后台唤醒、推送、文件权限需要平台实现，不能只注册一个 platform 字段就宣称已实现原生客户端。
