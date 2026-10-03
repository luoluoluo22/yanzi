# 手机末端能力与自动同步（2026-10-02）

手机通过已有同账号连接发布状态与能力。电脑可以读取缓存，也可以请求手机立即采集；实际调用优先加密局域网，不可达时使用云端设备消息。云端调用沿用指定设备、有效期、claim、完成回执和结果恢复机制。

## 已实现的手机信息与能力

| 能力 | 返回内容 | 范围 |
| --- | --- | --- |
| `mobile.status.get` | 设备、系统、应用版本、电量、充电、网络、IPv4、通知权限、可用存储 | 原生后台即时采集 |
| `mobile.capabilities.list` | 能力名、版本、只读标识和参数 | 当前 7 项已绑定处理器 |
| `mobile.files.list` | 文件名、大小、修改时间 | 燕子自己的 Documents，最多 200 项 |
| `mobile.files.read` | 文件名、大小和 Base64 `content` | 单文件最大 64 KiB，禁止路径穿越；大文件走已有附件传输 |
| `mobile.extensions.list` | 小程序标识、名称、版本、运行时 | 已缓存的账号小程序定义 |
| `mobile.sync.status` | 最近检查、最近更新、游标、缓存数量、错误 | 当前账号、当前服务器的独立同步状态 |
| `mobile.data.read` | 对象内容、版本、删除状态 | 已缓存的燕幕、小程序定义及共享数据 |

照片、其他任意大文件的字节使用既有附件协议传递，不随状态心跳上传。手机剪贴板、相册全文、联系人、位置和任意代码执行不在本次只读能力范围中。

## 自动同步节奏

- 状态每 5 秒采样，有变化时提交；正常心跳每 30 秒发布状态、能力目录与同步状态。
- 登录启动、实时通道连接与重连后立即检查账号对象。
- 每 60 秒进行增量检查；Worker 的 `sync-ready` 提示触发约 3 秒内的合并检查。
- 对象游标与缓存一起持久化，重复版本不重写；删除作为 tombstone 保留。没有变化只推进“最近检查”，不伪造“最近更新”。
- 缓存按账号和服务器隔离，处理中切换登录或服务器会中止旧任务。分页每次最多 100 页，缓存上限 2 MB，超限保留原游标并报告错误。
- 同步覆盖 `yanm.*`、`extensionData.v1.*`、`mobileExtension.v1.*`、`mobileExtensions.index.v1`。燕幕重建本地视图，小程序定义变化时复用现有定义获取逻辑。
- 定时任务复用原生前台连接服务，独立线程运行，离开首页仍可工作。系统强行停止应用或网络不可用时无法保证计时，恢复连接后补取。

电脑连接详情显示具体同步内容、最近检查和最近更新；连接通道本身不代表全部数据已同步。

## 电脑 Agent API

使用现有本地 API 令牌，先通过 `GET /v1/me/devices` 获得目标手机 ID。

| 本地接口 | 用途 |
| --- | --- |
| `GET /v1/me/devices` | 同账号设备列表，包括标识、在线状态和最近活动 |
| `GET /v1/me/devices/{deviceId}/state` | 最近上报的状态及同步进度，`source: cloud-cache` |
| `GET /v1/me/devices/{deviceId}/capabilities` | 手机上报的能力目录 |
| `POST /v1/me/devices/{deviceId}/invoke` | 立即调用手机能力 |
| `GET /v1/me/devices/{deviceId}/results/{messageId}` | 恢复云端调用结果，校验对应目标和能力请求类型 |

调用体示例：

```json
{"name":"mobile.status.get","arguments":{}}
```

局域网响应含 `source: lan`、`data.ok`、`data.result`；云端响应含 `source: cloud`、`messageId`、`status`、`result`，其中 `result.output` 是手机结果 JSON。25 秒内尚未完成返回 HTTP 202 和 `resultUrl`，可继续读取回执。缓存有 `collectedAt`，不能将离线缓存当作实时状态。

调用限制为当前 7 项只读能力。手机云端处理校验目标、有效期和账号/能力授权，LAN 处理要求账号所有者直连授权。云端执行结果先持久化再 ACK，重新投递可重放结果，不重复采集已完成请求。旧手机未声明协议版本时返回升级提示。

## 验证与发布边界

真机验证入口：`scripts/test-real-phone-message-bridge.ps1 -SkipBuild -VerifyAccountLan`。使用 OnePlus GM1900 Dev 包及隔离 Worker，备份恢复登录和服务器，保留生产包。

覆盖状态、7 项目录、后台调用、电脑 Agent API 的 LAN 调用、文件读取字节、增量对象、无变化定时检查、删除传播、路径穿越拒绝；并复用双向消息、附件校验、断线恢复与实际离线过期回归。

完整真机回归与最后的 `-CapabilitiesOnly` 针对性回归均通过，后者额外验证同账号设备发现、云端调用及按目标恢复结果。两轮结束均确认 Dev 配置/正式服务器地址恢复、生产包保持不变，并重新启动桌面。详细产物路径记录于 `mobile/android/DEVELOPMENT_STATUS.md`。

Worker 的实时同步提示需要通过 Git / Cloudflare CI 发布后才在正式服务生效。60 秒增量轮询不依赖该提示；本地隔离测试通过不等于正式服务已发布。
