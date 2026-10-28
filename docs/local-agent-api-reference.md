# LocalAgentApi 接口清单

本文档汇总了燕子（OpenQuickHost）本地代理服务（LocalAgentApiServer）开放的所有 RESTful API 接口，供本地命令行工具、AI Agent 以及自动化测试脚本调用集成。

---

## 1. 服务概述与鉴权协议

* **默认服务地址**：`http://127.0.0.1:53919`
* **交互控制台**：在浏览器直接访问 `http://127.0.0.1:53919/` 即可打开内置的图形化 API 调试工作台。
* **程序调用认证**：携带 `Authorization: Bearer <AGENT_API_TOKEN>` 或 `X-Yanzi-Token`；在燕子设置 → 常规 → 本地 Agent API 中复制当前令牌。切勿在文档、脚本或仓库中写死 Token。
* **网页控制台免重复粘贴**：首次从设置中复制令牌，调用 `POST /v1/console/login`（`X-Yanzi-Token`），浏览器获得 HttpOnly、SameSite=Strict、有效期一年的签名 Cookie。`GET /v1/console/session` 会在使用时续期，`POST /v1/console/logout` 清除当前浏览器 Cookie。修改主 Token 可立即使旧 Cookie 失效。原始 Token 不保存到 localStorage，也不注入 `/docs` HTML。公用电脑请退出，清除浏览器 Cookie 后需重新验证。
* **兼容性**：对外 `/v1/extensions` 等路径和 JSON 的 `extensionId` 字段保留不变，UI 文案统一使用“小程序”。
* **数据编码**：所有请求体与响应体统一采用 **UTF-8 JSON** 格式。

---

## 2. 小程序管理接口

### 手机末端能力

伴随应用的文件工作流可调用真实宿主能力 `files.workflow.run`，指定 `jobId`、`transferId`（LAN）或 `attachmentId`（云端）、下游 `capability`、`parameters`。相册小程序启动后提供 `album.process`。认证后的文件上传/读取为 `PUT/GET /v1/companion/files/{ticket}`，GET 支持 `offset`；大文件分块为 `/v1/companion/transfer-sessions/{id}`。详见 [相册与通用文件工作流](yanzi-album-development-2026-10-02.md)。

通过现有认证读取 `GET /v1/me/devices` 选择同账号手机，再读取 `GET /v1/me/devices/{deviceId}/state`（最近状态快照）或 `GET /v1/me/devices/{deviceId}/capabilities`（真实能力目录）。立即调用使用 `POST /v1/me/devices/{deviceId}/invoke`，请求体为 `{"name":"mobile.status.get","arguments":{}}`，优先加密局域网，失败后使用云端设备消息。

目前提供手机状态、能力目录、燕子文件目录/小文件读取、小程序元数据、同步进度和同步对象读取 7 项只读能力。缓存与实时结果、LAN/cloud 返回格式及大文件边界见 [手机末端能力与自动同步](yanzi-mobile-endpoint-sync-2026-10-02.md)。

### 能力网络接口

AI 可先通过认证读取 `GET /v1/agent/catalog`。目录关联本机已安装的小程序与对应能力，包含 `id/title/runtime/isRunning/runEndpoint/capabilities`，宿主能力在 `hostCapabilities` 中。每项能力包含名称、描述、提供者、版本、输入输出 Schema、权限和 `available`。未运行小程序的 manifest 声明仍可被发现，但只有绑定了实际 Handler 的能力才标记 `available: true`。

| 接口 | 用途 |
| --- | --- |
| `GET /v1/agent/catalog` | AI 统一发现目录，返回所有已安装小程序及其能力 |
| `GET /v1/agent/openapi.json` | OpenAPI 3.1 文档，描述发现、启动与调用协议，不包含 Token |
| `GET /v1/extensions/{id}/capabilities` | 指定小程序的声明/注册能力；不存在返回 404 |

`GET /v1/extensions` 也新增 `capabilities` 字段，兼容已有字段。没有声明或注册能力的小程序返回空数组，不伪造可调用接口。上述接口沿用 Bearer / X-Yanzi-Token 认证；目录不会开放匿名访问。

AI 使用流程：读取目录 → 选定能力并检查 `inputSchema` → 若尚不可用，经用户授权调用对应 `runEndpoint`，随后重新查询 → 使用 `POST /v1/capabilities/invoke` 传递 `name/payload` → 检查 `success/data/errorCode`。启动请求完成不等于能力已经注册。

本地网页 `/docs` 增加“AI 能力目录与调用”：登录后自动读取目录，可筛选提供者、查看已安装小程序和能力数量、展开契约、编辑 JSON 参数并实际调用，还可启动未运行的提供者、读取 OpenAPI、复制不含 Token 的目录及查看调用记录。未注册能力禁用调用按钮，退出登录会清空能力目录。

以下接口均要求现有本地 Agent 认证：

| 方法与路径 | 用途 |
| --- | --- |
| `GET /v1/capabilities` | 返回 `{ "capabilities": [...] }`，每项包含名称、描述、提供者、版本、输入输出 Schema、所需权限 |
| `GET /v1/capabilities/{name}` | 查询能力描述；不存在返回 404 |
| `POST /v1/capabilities/invoke` | 统一调用：`{ "name": "system.time.now", "payload": {} }` |
| `GET /v1/capabilities/calls` | 返回 `{ "calls": [...] }`，最近最多 1000 条调用记录 |

调用响应为 `{ "success": true, "data": ..., "error": null, "errorCode": null }`；失败为 `success: false`，包含可读 `error` 和 `errorCode`。参数错误返回 400，未知能力返回 404，权限拒绝返回 403，执行异常返回 500。已鉴权的本地 Agent 使用管理权限，但仍验证参数。请求体中的调用者或权限字段不会改变授权。

WebView 小程序可使用 `yanzi.capability.list()`、`yanzi.capability.describe(name)` 和 `yanzi.capability.invoke(name, payload)`。调用权限来自该小程序的 manifest 声明，失败时 Promise reject。

### 2.1 获取已安装小程序列表
* **路径**：`GET /v1/extensions`
* **说明**：扫描并返回本地 `%LOCALAPPDATA%\OpenQuickHost\Extensions` 中已安装的所有扩展及其元数据。
* **响应示例**：
  ```json
  {
    "items": [
      {
        "id": "smart-action",
        "title": "智能识别",
        "subtitle": "智能识别选中的内容并自动路由...",
        "category": "效率工具",
        "source": "LocalExtension",
        "version": "1.0.0",
        "runtime": "csharp",
        "entry": "main.cs",
        "permissions": ["clipboard", "context.read"]
      }
    ]
  }
  ```

---

### 2.2 执行指定小程序（核心测试端点）
* **路径**：`POST /v1/extensions/{id}/run`
* **说明**：在宿主内触发指定扩展执行。

> [!IMPORTANT]
> **入参字段命名关键约束**
> 请求体中传递的输入参数字段名为 **`input`**（小写），**切勿误写为 `inputText`**。

* **请求参数（Body）**：
  ```json
  {
    "input": "https://github.com/luoluoluo22",
    "launchSource": "api"
  }
  ```
* **响应结构**：
  ```json
  {
    "ok": true,
    "success": true,
    "output": "已在默认浏览器打开: https://github.com/luoluoluo22",
    "error": "",
    "exitCode": 0
  }
  ```

---

### 2.3 强制终止正在运行的小程序
* **路径**：`POST /v1/extensions/{id}/stop`
* **说明**：请求终止由指定扩展拉起的托管工作线程、原生窗口或后台长周期任务。托管实例可能需要短暂时间退出。
* **响应结构**：`{ "ok": true, "stopRequested": true, "stopped": false, "isRunning": true }` 表示请求成功但尚未完全退出；完成后 `stopped` 为 `true`。可调用 `/status` 等待退出。

---

### 2.3a 版本化账号存储

`GET /v1/account-storage/{id}?key={key}` 返回 `ok / exists / revision / content / accountId`，通过已有账号 ObjectSync 读取。

`PUT /v1/account-storage/{id}` 的请求体是 `{ "key": "...", "content": "...", "expectedRevision": 0, "accountId": "读取返回的账号ID" }`。明确要求版本和账号；同一版本并发写入返回 `409 conflict`，缺少版本返回 400，账号变化返回 409，未登录返回 503。内容最大 256 KiB。均需本地 Agent 认证。

此接口不隐式修改扩展的本地文件，扩展自行维护离线日志和冲突处理。现有 `/v1/storage/{id}` 语义保持兼容。

### 2.4 查询扩展运行状态（已有接口，增强返回）
* **路径**：`GET /v1/extensions/{id}/status`
* **说明**：综合 API 任务与 `RunningExtensionRegistry` 的常驻实例，不再仅依赖 API 的执行任务是否完成。
* **响应字段**：`isRunning`、`apiTaskActive`、`instanceCount`、`instances`（实例 ID、运行时、启动时间和来源）、`isDeploying`、`version`。
* **不存在的扩展**：`404 extension_not_found`。

### 2.5 不停机预编译 C# 扩展
* **路径**：`POST /v1/extensions/{id}/build`
* **请求体**：`{}`，只支持具有源码入口的 C# 扩展。
* **说明**：复用现有 Roslyn 编译缓存，不停止旧实例；返回 `sourceSha256`、`artifact` 和 `elapsedMs`。
* **错误状态**：`422 compilation_failed`（包含 `diagnostics`）；`409 source_changed_during_build` 或 `deployment_in_progress`。

### 2.6 编译优先重载 C# 扩展
* **路径**：`POST /v1/extensions/{id}/reload`
* **请求体**：`{}`。
* **执行顺序**：编译并验证源码版本 → 若原实例正在运行，则请求停止、最多等待 5 秒、再次校验源码并启动新版本；**如果原实例尚未运行，只刷新宿主小程序目录、燕环轮盘与搜索，不会启动它**（防止刷新延时关机时意外执行关机）。
* **即时生效**：成功重载后调用宿主目录刷新；宿主也已监听 manifest.json 和 .cs 的磁盘修改（650 毫秒防抖），自动更新轮盘和快捷面板。对持有旧 CommandItem 的轮盘槽位，执行时按小程序 ID 重新读取当前 manifest。运行中的延时关机不可热重启，须先手动终止倒计时后再重载。
* **编译失败**：旧实例保持运行，返回 `422`；停止超时返回 `409`，不启动新版本。
* **限制**：重载成功不保证界面功能正确；若停止后出现运行时故障，当前版本**不提供自动回滚**，响应含 `recoveryRequired`。跨进程、未注册的外部子进程不保证被检测。

### 2.7 查询扩展或宿主日志
* **路径**：`GET /v1/extensions/{id}/logs?maxLines=100`；`GET /v1/logs?maxLines=100`（已有）。
* **说明**：前者读取对应扩展的 `debug.log`，最多返回末尾 500 行；后者查询宿主日志。若扩展尚未生成日志，`logs` 为空数组。

### 2.8 移动扩展至回收站（软删除）
* **路径**：`DELETE /v1/extensions/{id}`
* **说明**：将指定 ID 的扩展从活跃扩展目录安全隔离至回收站，可防止用户误删。

---

### 2.9 彻底清除回收站中的扩展（物理删除）
* **路径**：`DELETE /v1/extensions/recycle-bin/{id}`
* **说明**：永久删除回收站中指定 ID 的扩展物理文件。

---

## 3. 小程序界面自动化与截图（本地、受保护）

本组接口须提供 API Token，且浏览器的 `Origin` 必须与本地服务同源；所有窗口均从**已安装扩展 ID** 解析，禁止任意 PID/HWND 或全屏抓取。每个扩展的 UI 操作串行执行；正在部署时返回 409。

### 3.1 窗口发现、打开与控件枚举

- `GET /v1/extensions/{id}/ui/windows`：返回主窗口及本扩展已注册的其他窗口；不主动唤醒。
- `POST /v1/extensions/{id}/ui/open`：`{"launchIfNeeded": true}`。确保窗口可见，不使用容易意外收起窗口的 toggle。
- `GET /v1/extensions/{id}/ui/elements?maxDepth=4&limit=80&windowId=primary`：返回窗口内可见控件的 `automationId`、`name`、`type`，供下一步定位。无任意桌面控件查找。

### 3.2 截图

```http
POST /v1/extensions/taskbar-calendar/ui/capture
Content-Type: application/json

{"mode":"auto","windowId":"primary","launchIfNeeded":false}
```

- `mode=auto`（默认）：优先采用小程序的 `CaptureSnapshot(path)`；对于日历等失焦即隐藏的 WPF 弹窗，即使浏览器已让其隐藏，也会在后台渲染保留的视觉树，**不抢占浏览器焦点、不再次显示窗口**。响应包含 `backgroundCapture`、`windowVisible`。没有视觉适配器时尝试 `PrintWindow`，但要求窗口可见。`mode=visual` 仅用扩展视觉渲染；`mode=window` 仅截可见的原生窗口，隐藏时返回 409。`launchIfNeeded=true` 仅在窗口尚未初始化或必须显示才能截图时唤起它。
- 响应 201：`{"ok":true,"capture":{"captureId":"...","imageUrl":"/v1/captures/{captureId}/image","width":800,"height":600,"mode":"visual","expiresAtUtc":"..."}}`。
- `GET /v1/captures/{captureId}/image`：返回有鉴权的 `image/png`；`GET /v1/captures/{captureId}`：获取元数据。均需 Token；图片临时保存在本地 `AgentCaptures`，访问权限有效 12 小时。
- 视觉树截图可能不包含独立悬浮菜单与其他 HWND；Windows 无法提供可靠图像时返回 422，不会悄悄截取整块桌面。

### 3.3 自动化操作并截取操作后的状态

```http
POST /v1/extensions/taskbar-calendar/ui/actions
Content-Type: application/json

{
  "windowId":"primary",
  "captureEach":true,
  "actions":[
    {"type":"invoke","automationId":"calendar.add","waitFor":"calendar.editor.title"},
    {"type":"invoke","automationId":"calendar.editor.cancel"}
  ]
}
```

操作类型为 `invoke`、`setValue`（同时需 `value`）、`focus`、`toggle`、`select`、`expand`、`collapse`。点击浏览器按钮导致日历隐藏时，`/ui/actions` 默认先重新显示目标扩展的窗口，再执行操作；传入 `showIfHidden=false` 则不改变窗口可见状态，隐藏时返回 409。优先使用 `automationId`，也支持 `name`；单次最多 8 步。设置 `captureEach` 可生成每步后的独立截图，或设置 `captureAfter` 仅在最后截图。`waitFor` 等待指定控件变为可见。操作只限定在已确认的扩展窗口内，调用前建议从 `ui/elements` 发现实际控件。

当前版本仅支持已注册窗口、独立进程窗口及可访问的 Windows UI Automation 控件。**不包含全屏截图、无界面的脚本扩展、跨进程嵌入式弹窗的自动发现，也不会自动清理被真实保存的业务数据**。测试可采用打开编辑器后取消的无破坏场景。

### 3.4 自动化会话、蓝色提示与人工中断

直接调用 /ui/actions 或对可见窗口的 /ui/capture 且不携带 sessionId 时，会自动开启短暂的 scoped 会话：扩展窗口周围出现点击穿透的蓝色渐变边框，顶部浮现暂停、继续和停止工具条。完成或异常结束后自动关闭，截图本身不包含浮层。隐藏窗口的纯视觉截图则直接在后台完成，不出现边框，也不占用用户输入。

- POST /v1/extensions/{id}/ui/sessions：创建持久会话。请求示例：{"mode":"scoped","launchIfNeeded":true}。返回 201 和 session.sessionId；之后调用 actions/capture 时在请求 JSON 中携带 sessionId。全局最多一个 UI 测试会话。
- mode=foreground 必须显式提交 userConsent=true：各显示器蓝色边框和光标光环；当前版本不会注入物理鼠标输入、控制其他程序或锁定键盘。
- GET /v1/ui/sessions/{sessionId}：查询 active 或 paused 状态、阶段和暂停原因。
- POST /v1/ui/sessions/{sessionId}/pause：暂停测试；POST /v1/ui/sessions/{sessionId}/resume：恢复 API 主动暂停的会话。
- DELETE /v1/ui/sessions/{sessionId}：立即结束会话并清除所有提示。Ctrl+Alt+Shift+F12 为紧急停止快捷键。
- 通过 Windows 低级输入事件识别实际鼠标与键盘活动，忽略标记为 injected 的模拟输入；只在输入发生时判断用户是否操作了目标窗口，避免浏览器先前的点击因前台窗口切换被误判。检测到真实用户在目标窗口中操作，会暂停后续自动化步骤；此类暂停只能由用户在**桌面蓝色工具条**上确认继续。foreground 模式中任意真实输入触发暂停。
- 目标窗口关闭、API 停止、用户点停止或会话空闲超过 120 秒，都会自动清除提示。
- UI Automation 的单次 Invoke 可能是不可中断的同步调用；暂停在下一检查点生效，已执行的保存或删除操作无法自动撤销。

---

## 4. 扩展键值存储（Storage）接口

每个扩展拥有独立的本地与云端键值数据隔离空间：

### 4.1 读取扩展存储值
* **路径**：`GET /v1/storage/{id}?key={key}`
* **响应示例**：
  ```json
  {
    "key": "default_search_engine",
    "content": "google"
  }
  ```

### 4.2 写入/更新扩展存储值
* **路径**：`PUT /v1/storage/{id}`
* **请求参数（Body）**：
  ```json
  {
    "key": "default_search_engine",
    "content": "google"
  }
  ```

---

## 5. 桌面环境与系统接口

### 5.1 发送桌面系统通知
* **路径**：`POST /v1/app/notify`
* **说明**：无需通过小程序，直接从宿主层面拉起 Windows 桌面右下角气泡通知。
* **请求参数（Body）**：
  ```json
  {
    "title": "系统提醒",
    "message": "后台编译与数据同步已完成。"
  }
  ```

---

### 5.2 执行后台 Shell 脚本
* **路径**：`POST /v1/shell/run`
* **说明**：让燕子宿主作为宿主代理进程拉起独立的后台 PowerShell 指令。
* **请求参数（Body）**：
  ```json
  {
    "command": "Get-Process Yanzi | Select-Object Id, CPU"
  }
  ```

---

## 6. 快速测试脚本（Windows PowerShell 5.1）

在终端中可使用以下标准模板进行扩展调测：

```powershell
$token = "yanzi-local-dev-token"
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type"  = "application/json; charset=utf-8"
}

# 1. 查询所有已加载扩展
Invoke-RestMethod -Uri "http://127.0.0.1:53919/v1/extensions" -Headers $headers

# 2. 模拟测试运行 smart-action (输入网址测试)
$payload = @{
    input = "https://github.com/luoluoluo22"
    launchSource = "powershell_test"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://127.0.0.1:53919/v1/extensions/smart-action/run" `
    -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($payload))

Write-Host "Success: $($response.success), Output: $($response.output)"
```

## 7. 局域网二进制附件直传

设备消息协议发现：鉴权后 `GET /v1/me/devices/protocol`，返回协议版本、传输和路由方式及大小限制。完整约定见 `yanzi-device-message-protocol-v1.md`。本地消息显式未知版本返回 426；指向其他设备 ID 的请求返回 404，不在本机执行。

电脑和手机均支持内层 `POST /v1/lan/transfers/{transferId}`。外部请求经同账号自动连接后的 AES-GCM `POST /v1/lan/secure` 封装；不在局域网发送全权 Bearer Token。localhost 原生内部转发保留本地鉴权。`transferId` 为不带横线的 UUID（32 位十六进制）。

- 查询参数：`kind=photo|file`、URL 编码的 `name`；手机发电脑另需 `sourceDeviceId`，可附 `sourceDeviceName`。
- 请求头：`Content-Length`、`X-Content-Sha256`（64 位十六进制）、适当的 `Content-Type`。
- Body：原始文件字节；上限 30 MiB，不使用 Base64。
- 成功返回 `success: true`；电脑另返回 `messageId`、`output`、`transport: "lan"`。相同已完成传输重试返回 `deduplicated: true`。
- 桌面端元数据或校验和错误返回 400，ID 内容冲突返回 409，超限返回 413。

网络错误可使用相同 `clientTransferId` 回退云端。业务失败和完整性校验失败不能作为重复执行的理由。

手机远程文件和终端操作优先发送到桌面 `POST /v1/me/mobile/messages`，`kind` 分别为 `fs-list`、`fs-read`、`fs-write`、`run-powershell`。在 `payload.clientOperationId` 传入稳定 UUID，以便局域网重试和云端回退共享执行记录。接口返回真实处理结果；文件不存在等业务失败直接呈现给调用方。

### 7.1 配对与分块会话

电脑本机 `/docs` 鉴权后可打开 `/pair` 查看同账号连接状态。当前用户流程只需两端登录同一账号，不要求复制设备 ID 或密钥。原本机 POST/GET/DELETE `/v1/me/devices/pairs` 管理接口为兼容及测试保留；默认后台自动调用云端 POST `/v1/me/devices/lan-links`，不接受受限网页凭据，密钥不展示在页面。


≥2 MiB 使用内层 `/v1/lan/transfer-sessions/{UUIDN}`：POST 清单、GET 缺块、PUT `.../blocks/{index}` 保存 1 MiB 块、POST `.../commit` 完整性校验并提交、DELETE 取消活动会话。重试保持 ID，已保存块不重复发送，已完成提交返回原结果。外层身份和权限验证后才访问对应会话。

### 7.2 网页和新设备的受限云端调用

云端账号所有者通过 `POST /v1/me/devices/{id}/credentials` 授予 applicationId/scopes/targetDeviceIds/fileRoots/lifetimeSeconds。网页获准单个能力后不能借路由执行终端或越过文件根目录。凭据查询不返回 Token，支持撤销。接入及逐设备回执、期限、取消、claim 和 trace 详见 [协议 v1](yanzi-device-message-protocol-v1.md) 与 [SDK 示例](../protocol/sdk/README.md)。本机能力目录和 schema 仍由现有 `/v1/capabilities` 及公开注册目录接口提供。

### WebView 应用的版本化存储桥接

`yanzi.storage.get(key,{scope:"local"})` 读取本地文件而不触发云端刷新；`accountRead(key)` 返回 ok/exists/revision/content/accountId；`accountWrite(key,content,expectedRevision,accountId)` 使用 CAS，冲突返回 conflict。key 自动加入当前应用 storage.namespace，身份固定当前 extensionId。写入上限 256 KiB，拒绝缺少版本和账号切换。所有 WebView 应用可复用，业务合并在扩展中实现。

`POST /v1/extensions/{id}/run` 对有 app 配置的 WebView 应用创建或激活独立窗口，返回 `{ok:true,success:true,opened:true}`，opened 不代表业务同步完成。