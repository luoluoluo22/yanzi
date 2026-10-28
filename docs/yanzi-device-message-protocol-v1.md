# 燕子设备消息协议 v1

更新：2026-10-02。云端代码与迁移仅在本地验证，尚未发布。旧 `/v1/me/mobile/messages` 路径继续兼容；设备类型不限定为手机。

## 身份与协商

鉴权读取 `GET /v1/me/devices/protocol`：`yanzi.device-messaging`、版本 1、支持版本、功能、路由及限制。未知显式版本返回 426；省略版本兼容旧 v1。平台是 1–32 位小写标识，不替代稳定 `deviceId`。`POST /v1/me/devices` 注册身份、显示名与真实 capabilities。

新设备声明 `deviceMessageProtocolVersions:[1]`；`receiveAccountChat:true` 才参加账号聊天（原 desktop/android 接收器兼容加入）。平台无需服务器新增枚举。执行适配器必须实现自己的 OS 权限、能力分发和持久化收件箱。

## 路由与标识

```json
{
  "protocolVersion": 1,
  "routing": "device",
  "sourceDeviceId": "web-device-001",
  "targetDeviceId": "desktop-device-001",
  "clientMessageId": "721737713e2e4fcbade159a2f52ece62",
  "operationId": "721737713e2e4fcbade159a2f52ece62",
  "traceId": "request-trace-001",
  "kind": "capability.invoke",
  "expiresAt": "2026-10-02T12:02:00Z",
  "payload": {"name": "notes.read", "payload": {}}
}
```

`targetDeviceId` 永远优先，定向消息不广播。`routing:account-chat` 用于 text/photo/file/screenshot，账号其他设备逐台接收；LAN 接收成功后仍把同一逻辑消息同步到云端供其他设备补投。执行请求要求单个明确目标；旧平台请求仅在候选设备恰好一个时固定目标，否则返回 `409 target_device_required`。

`clientMessageId` 在账号＋源设备内标识重试事务；相同内容返回原 messageId，递归对象字段顺序不影响身份，数组、真实值、显式 trace 改变返回 409。新客户端统一生成 UUID；附件传输 ID 使用 UUID N 格式，并保持跨 LAN/云端唯一。不同设备可使用相同消息事务 ID，不会互相吞消息。

`messageId` 是入口返回 ID；`operationId` 是业务操作 ID；`traceId` 贯通请求、设备和能力审计；correlationId/causationId 用于关联与因果。服务端把可信 context 写入 payload.messageContext，并在 HTTP/WS 返回根级字段。回退不生成新 operation/clientTransferId。源设备、目标设备和账户共同约束执行；显示名只用于 UI。

## 可靠投递与结果

发送前保存账号隔离的 Outbox 和附件快照；网络故障保留原请求，服务恢复后重试。权限、参数、内容冲突等明确拒绝进入终态，不阻塞其他消息。附件云端上传成功后先持久化元数据，再发送引用，避免丢 ACK 或重启重复上传。大附件补投与心跳、收件轮询独立运行。

WS/SSE 负责唤醒，D1 是持久真源。HTTP：`GET /v1/me/mobile/messages?deviceId=...&limit=50&after=...`，返回 items、hasMore、nextCursor、serverNow。sequence 由服务器插入顺序产生，不依赖设备时钟。重连先补读未 ACK 消息，不能仅用已保存 cursor 丢弃旧 pending 项。

命令默认期限 120 秒，上限 300 秒；接收器执行前再检查。`POST /{messageId}/claim` 以目标身份原子取得执行权；再次 claim 不会获取执行权。`POST /{messageId}/cancel` 仅源设备允许，只取消 pending，执行开始后返回 409。cancelled/expired 不被迟到 ACK 复活。

状态为 pending → executing → completed/failed/unknown，或 pending → cancelled/expired。接收器本地保存 saved/executing/最终结果，执行后先保存结果再 ACK。崩溃、执行权回复丢失等不确定情况标为 unknown；源端显示“结果待确认”，不自动重做。迟到的确认结果可以把 unknown 变成 completed/failed，但迟到未知结果不能覆盖已确认终态。

`POST /{messageId}/ack` 带接收 deviceId。命令必须带 success/result；`resultState:unknown` 表示不确定结果，不能等同失败。普通消息可仅 ACK。账号聊天回执按设备持久化，不因某台 ACK 而阻止其他设备；GET 单条消息包含 receipts。保证是至少一次投递＋业务去重，不宣称跨业务数据库的分布式恰好一次。

## 授权边界

所有者创建 `POST /v1/me/devices/{id}/credentials`，指定 applicationId、scopes、targetDeviceIds、fileRoots 与 lifetimeSeconds。设备凭据绑定账户、设备与应用；能力 scope 为 `capability.invoke:<name>`，扩展为 `extension.run:<id>`，终端为 terminal.execute，文件为 files.read/files.write 并要求绝对目录白名单。GET 同路径列出元数据，不返回 Token；DELETE `/v1/me/devices/credentials/{id}` 撤销。

网页只有 notes.read 时不能换成 notes.write、执行终端、读取另一设备收件箱或越过文件目录。服务端生成授权 context，客户端提交的 authorization 不作为授权证据。能力运行时继续验证自身权限与输入输出 schema。

## 加密局域网

UDP 只返回设备 ID、端口、secureProtocol 与 pairingRequired，不广播 Token，也不能更新可信路由。当前默认同账号自动连接，无需人工配对步骤。Windows/Android 心跳声明 autoAccountLan/lanPort，账号登录客户端 POST `/v1/me/devices/lan-links`，服务端仅向该账号已登记设备返回对应连接信息；受限应用凭据不能调用此接口。

每对设备的密钥由服务端 HMAC-SHA256 从服务器密钥、账号和排序后的两个设备 ID 派生，两端取得相同 pairId/key。连接信息禁止缓存，客户端只在 HTTPS 账号连接或 localhost 测试夹具内获取；本地用 DPAPI/Android Keystore 保护。有效期 7 天，在线心跳自动续期；切换账号不能复用旧连接。只有通过加密握手的实际地址进入 Peer Registry。界面展示连接状态，不展示密钥；旧本机管理 API 仅为兼容及隔离测试保留。


LAN 外层 POST `/v1/lan/secure`，携带 X-Yanzi-Pair、X-Yanzi-Frame（精确 UTF-8 metadata 的 Base64）、X-Yanzi-Nonce。metadata 包含 source、target、UUID requestId、毫秒 timestamp、原 path、method、contentType 和可选 sha256。AES-256-GCM、12 字节随机 nonce、128 位 tag，AAD 为原始 metadata 字符串；body 为 ciphertext||tag。外层不发送全权 Bearer。身份、目标、时间窗口、重放、路径和 scope 均检查后才执行。

回复外层 200、X-Yanzi-Status、X-Yanzi-Nonce，密文 AAD 为 `response\n<innerStatus>\n<originalMetadata>`。验证 AEAD 后才接受内层状态。固定 peer 身份，不接受跨设备隐式最后地址；PeerRegistry 按账户保存多个认证地址和验证时间。撤销对后续请求立即生效；已进入运行的操作遵守自己的取消协议。

这是应用层 AEAD，不是 TLS，也没有引入证书体系。远端旧明文 LAN 返回 426；localhost 开发/原生内部入口保留鉴权兼容。暂未取得同账号连接信息时使用 HTTPS 云端，不降级到明文。

## 附件与断点

1 字节至 30 MiB。小文件可内层 POST `/v1/lan/transfers/{UUIDN}`，包含 kind/name/sourceDeviceId 与 SHA-256。至少 2 MiB 自动使用分块协议，两端实现相同接口：

- POST `/v1/lan/transfer-sessions/{UUIDN}`：id/sourceDeviceId/name/kind/size/sha256/blockHashes/notificationPort 清单；返回 state/missingBlocks/result。
- PUT `.../blocks/{index}`：最多 1 MiB 的对应块，校验 SHA 后原子保存。
- GET 会话：读取已保存块状态。网络断开后用原 ID 重开会话，只发送缺失块；丢块 ACK 后也不重传已保存块。
- POST `.../commit`：完整文件 SHA 和长度校验后交给持久化收件箱。重复 commit 返回同一结果。
- DELETE 会话：活动传输取消，保存 tombstone；已完成不能伪装取消。其他配对设备不能读取或修改该会话。

活动会话最多 128，暂存 300 MiB，空间保留 10 MiB，活动暂存超过 24 小时清理并取消，终态保存 30 天。发件附件快照及 LAN 接收文件有 300 MiB 配额和保留期；云端下载 Range 保留不完整部分，完整哈希错误才删除。有效消息期限结束前不能删除用于抑制副作用的回执。磁盘空间不足明确报告 storage_exhausted / transfer_quota_exceeded。

## SDK 与验证

通用 JS SDK、IndexedDB 与 Node/SQLite 适配器见 `protocol/sdk/README.md`。黄金向量在 `protocol/vectors/lan-aead-v1.json`，JS/C#/Android 共用并验证。iOS/嵌入式 OS 的后台及推送适配仍需平台实现。

验收入口：Node 协议/SDK 测试；CapabilityVerification 的 --secure-lan、--transfer-sessions、--outbox；隔离 Worker 73 项检查；实际手机 Dev 双向照片/3 MiB 文件、终端和远程文件；完整云端回归。多设备为 2 手机＋2 电脑＋新设备类型的逻辑夹具；物理硬件为一台 Windows 和一台 OnePlus，不能称作四台真机验收。
