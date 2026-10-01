# 外部 AI / 应用在线数据接口

## 燕窝多数据入口

设置 → 燕窝 → 顶部「AI 数据接入」。先刷新并选择可申请的数据清单，再选择只读或增删改查，生成提示词。可复制提示词，或「复制并打开 AI」打开 ChatGPT 后粘贴发送；不会自动提交对话。手机应用中心同样提供清单和提示词。

主账号 GET `/v1/applications/access-resources` 获取已发布数据声明及自己已有共享数据的元数据目录，不包含数据正文。POST `/v1/applications/access-invites` body 支持 `{ "resources": "all", "access": "read" }`，或明确 resources 数组，每项有 extensionId、key、access。全部仅包含生成地址时清单中的具体资源，后续新增的数据不会进入旧授权。

AI GET 接入地址及其 `resourceList` 得到资源清单；POST requests 的 `scopes` 为所需资源数组，或 `"all"`。设备弹窗逐项显示并可取消选择。多资源申请批准时必须明确提交 scopes，旧客户端只会看到待确认申请，但无法用单资源的旧确认请求批准多资源权限。

批准后响应 `resources` 数组为最终允许范围，每项含 access 和独立 data 地址。未确认项、未选 key 和未来新数据均返回 403。各项 access 为准；逐条接口只用于有 records-v1 schema 的资源，其余通过 document 接口及 SDK 操作。

主账号 GET `/v1/applications/access-grants` 列出有效外部授权；DELETE `/v1/applications/access-grants/{requestId}` 立即撤销。燕窝「查看 / 撤销已授权应用」提供同样的操作。

电脑托盘菜单「AI / 外部应用授权」或手机应用中心「复制 AI / 外部应用接入地址」创建资源地址。日历选择 `taskbar-calendar` / `calendar.v1.json`；默认只读，勾选后可申请增删改查。地址有效 7 天，拥有地址只能申请，无法读取数据。每次申请须由已登录电脑或手机确认，申请有效 5 分钟，批准的令牌有效 1 小时。

把生成的完整 `https://sync.luoluoluo.cc.cd/connect/...` 地址交给能发 HTTP 请求的 AI。通用入口说明是 `https://sync.luoluoluo.cc.cd/ai`；通用入口不属于某个账号，不会弹授权窗口。

## AI 接入流程

1. GET 资源地址，按 JSON 的 `nextRequest` 向 `/connect/.../requests` POST `{ "clientName": "你的应用名称", "access": "read-write" }`。允许的最大权限在 discovery 中。GET 不产生弹窗，避免链接预览误触发申请。
2. 告诉用户返回的 `userCode`。申请方名称由申请方自行填写，不是身份认证；用户应核对自己刚发起的请求和核对码。
3. 使用返回的私人 `requestSecret` 作为 Bearer，至少每 3 秒 GET 返回的 `poll.url`。202 表示等待；403 拒绝，410 过期，429 等待后再试。不要公开 requestSecret 或访问令牌，也不要请求用户主账号密码。
4. 电脑或手机弹窗显示小程序、数据 key、读写权限和核对码。任一端批准后，轮询返回 `accessToken`、到期时间和 `data.records` / `data.document` 地址。以 `Authorization: Bearer accessToken` 请求数据。令牌只允许这一账号的这一小程序的这一 key。

## 日历逐条 CRUD

集合地址是 `/v1/extension-data/taskbar-calendar/records?key=calendar.v1.json`。所有请求携带批准的 Bearer，JSON 请求 Content-Type 为 `application/json`。

- GET 集合：返回 `fields` 字段定义与 `records` 数组，每条含 `id` 和 `version`。
- POST 集合：`{"id":"your-unique-id","title":"开会","date":"2026-10-02","isAlarm":false}`。id 可省略，但调用方为每次逻辑创建指定唯一 id 可以避免不确定网络重试重复创建。返回 201。
- GET `/records/{id}?key=calendar.v1.json`：读取单条记录。
- PATCH 单条：`{"expectedVersion":"最新 version","title":"改后的标题"}`。
- DELETE 单条：`{"expectedVersion":"最新 version"}`。保留删除标记以传播到手机和电脑。

`date` 为 YYYY-MM-DD；`alarmTime` 是 ISO 日期时间或 null；`isAlarm` 为布尔值。修改提醒条件会重置 IsTriggered。409 表示冲突，应重新 GET 最新记录、确认要修改的字段后重试。服务端可重试其他记录引起的整文档冲突，但不会自动覆盖同一条记录的新修改。逐条接口和原 SDK 使用同一个数据对象，现有电脑日历和手机日历继续同步。

开发者可在发布目录的 `apiSchema` 声明 records-v1 字段映射；记录接口由通用服务实现。没有声明此 schema 的应用仍可使用既有 `/v1/extension-data/{id}?key=...` 文档 GET / PUT 接口及 SDK，不需要为每个应用重新开发传输层。

## 撤销与限制

- 主账号 POST `/v1/applications/access-invites` 创建地址；DELETE `/v1/applications/access-invites/{地址最后一段}` 撤销地址并拒绝相关未处理申请，已经批准的授权仍以其独立到期时间为准。
- 主账号 GET `/v1/applications/access-requests` 查看待处理列表；POST `/v1/applications/access-requests/{requestId}/decision`，body 为 `{ "approve": true/false }`。
- 主账号 DELETE `/v1/applications/{extensionId}/grants/{requestId}` 立即撤销已批准的令牌；既有手动授权同样适用。
- 地址每分钟最多 3 次申请，每账号最多 3 条待处理申请、10 个有效地址。私人轮询秘密只保存哈希，接口 no-store。
- 当前授权通知每 5 秒检查一次，独立于聊天队列。手机需要燕子前台或消息前台服务运行、系统允许通知；尚未接入厂商系统推送，强制停止应用时不能保证弹窗。不会主动点亮或熄灭屏幕。

验证：Node + SQLite 测试覆盖未批准不可访问、跨 key 拒绝、只读、撤销、到期、私人轮询、重复决定和日历记录版本/删除标记；`scripts/test-application-platform.ps1` 使用真实本地 Worker/D1 验证匿名申请与主账号确认流程。


## 公网多资源回归

真实公网回归入口：

```powershell
.\scripts\test-public-external-access.ps1 -Serial <实体设备序列号>
```

该脚本只从已登录的燕子 Dev 进程私有偏好中临时读取会话，不打印账号令牌。它验证两资源邀请快照、`scopes=all` 申请、批准时缩小范围、未选资源 403、撤销后令牌失效和清理待确认申请。

需要验证真实手机逐项确认时：

```powershell
.\scripts\test-public-external-access.ps1 -Serial <实体设备序列号> -RequireDeviceApproval
```

脚本会等待手机原生授权页完成确认；验收时应至少取消一个 scope 再点“允许所选”，确保服务端最终令牌严格等于设备确认后的子集。历史 headless 诊断垃圾只可用 `-CleanStaleHeadlessDiagnostics` 清理；该开关仅匹配 `real-account-storage-headless-<数字> / roundtrip/state.json`，并在 tombstone 前核对对象哈希，避免误删正常业务数据。
