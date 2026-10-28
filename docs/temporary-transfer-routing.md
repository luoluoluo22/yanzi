# 燕子临时文件传输调度器（2026-10-24）

本模块用于按需文件传输，不替代账号云同步、Git、设备部署生命周期和手机原生消息通知。**它不会自动安装接收到的可执行文件。**

## 工作过程

1. 发件端调用 `files.temporary.plan`，提供 `source=home|office|phone|other`、`destination`、文件字节数；根据方向、容量、地区、适配器支持程度和失败反馈排序。
2. 调用 `files.temporary.publish` 指定现有的绝对文件路径。非公共 SFTP 线路不需要公开服务授权；83blue/tfLink 的匿名公共上传必须传 `allowPublicUpload=true`，上传的文件可能被知道链接的人取得，敏感资料必须先加密。
3. 默认仅上传首选线路；如果要保障接收时的自动降级，可设置 `backupCount=1`（或 2），额外上传备用副本。多上传会增加耗时和资源消耗。
4. 返回单个 `Receipt`（兼容）及 `Receipts` 数组。每份回执包含通道 ID、原文件名、字节数、SHA-256、取件位置与过期时间。可选 `notifyTarget` 才通知指定燕子设备，不填就只返回结果。
5. 接收端调用 `files.temporary.receive`，传 `receipt` 或 `receipts`。对回执列表逐项验证元数据，下载到临时 .part，大小和 SHA-256 都正确后才无覆盖地移动进隔离收件箱。首选失败时尝试下一回执。
6. 下载文件只进入用户数据目录 `temporary-transfer/inbox`，绝不自动安装或覆盖目标程序。

## 通道状态和容量

| 通道 | 接入情况 | 上限 | 使用条件 |
|---|---|---:|---|
| sg-sftp | 已实现并实测代码完整往返 | 当前双设备配置上限 64 MiB | SSH key、已固定 host key、受限 SFTP 账户 |
| 83blue | 已实现并实测代码完整往返 | 内核单文件保护上限 5 GiB，实际服务端限速/配额依服务商 | 每次必须显式授权公共上传 |
| tflink | API 适配器已实现；本次实测遇 HttpRequestException | 100 MiB | 每次必须显式授权；低优先级备用 |
| wenshushu-browser | 已登记；自动上传尚未完成 | 5 GiB（服务条款另限） | 登录后 Ego/Edge 网页操作，不作为无人值守适配器 |
| tmplink-browser | 已登记；自动上传尚未完成 | 标称 50 GiB，内核当前仅支持至 5 GiB | 登录网页操作，不能冒充后台 API |
| feichuan-browser | 已登记；网页游客流测试过 | 匿名游客约 100 MiB | 登录/游客分享会话，不作为无人值守适配器 |

服务商容量、价格、保存规则可能变化；本表不是实时额度承诺。

## 可配置通道

实际安装后设置文件：
`%LOCALAPPDATA%\\OpenQuickHost\\temporary-transfer\\channels.json`

示例（不会保存 SSH 私钥）：

```json
[
  {"id":"sg-sftp","enabled":true,"host":"YOUR_TRUSTED_SG_HOST","account":"YOUR_RESTRICTED_SFTP_USER",
   "remoteDirectory":"/home/YOUR_RESTRICTED_SFTP_USER/inbox",
   "knownHostsFile":"C:\\Users\\Administrator\\AppData\\Local\\OpenQuickHost\\temporary-transfer\\known_hosts",
   "maxFileBytes":67108864},
  {"id":"83blue","enabled":true},
  {"id":"tflink","enabled":true},
  {"id":"wenshushu-browser","enabled":true},
  {"id":"tmplink-browser","enabled":true},
  {"id":"feichuan-browser","enabled":true}
]
```

若配置不存在，内置通道全部处于禁用状态；这能避免升级后在用户不知情时将私有文件自动上传到外部服务。配置在设备本地，不需要随 Git/云同步传播 SSH 密钥。改变配置不会触碰燕子的原有云同步开关。手机目标默认启用 domestic-only 限制；目前大陆渠道尚无可信的无人值守上传 API 时，会明确返回无可用通道而不是降级为海外节点。

## 失败和验证规则

- 发送失败 → 按通道排名尝试下一个合格适配器。未授权公共上传时不能降级到公共文件站。
- 接收失败 → 如果发送人预先提供多个回执，尝试下一回执；不允许不同大小/哈希的候选混进同一次交付。
- 安装、执行和覆盖文件不属于此模块；收件箱由其他已授权流程在二次验收后处理。
- 测试：`scripts/verify-temporary-transfer.ps1`，覆盖选择、容量、公共权限、哈希、异常清理、发送/接收降级。SFTP/83blue 的真实 128 KiB 回环分别已通过；tfLink 此轮接口失败，已记录，不应宣称全部渠道稳定。
- 新加坡服务器的账号强制 `internal-sftp`，禁止任意远端 Shell；清理任务只删除超过 24 小时、指定目录的 `.bin` 文件。生产数据仍应按敏感等级加密。
