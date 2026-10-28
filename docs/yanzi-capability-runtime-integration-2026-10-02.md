# 真实小程序能力调用接入与验收

日期：2026-10-02

## 完成范围

本地 C# 小程序可直接向宿主注册真实 Handler、发现契约、调用另一个正在运行的小程序。无需读取对方文件或反射业务对象。WebView 和已鉴权本地 Agent 也可调用同一批能力。

能力只在实际 Handler 注册后可发现。提供者尚未运行时返回 `capability_not_found`；可通过现有小程序启动接口启动提供者后重试。当前版本不自动启动提供者，也不提供外部进程的 Handler 注册接口。

## C# 接入

小程序源码和 manifest 必须位于 `%LOCALAPPDATA%\OpenQuickHost\Extensions\<id>`；业务数据使用自身既有存储或 `context.ExtensionDataDirectory`。宿主仓库不存放业务小程序源码。

在 manifest 中添加 `provides`，声明名称、版本、输入输出 Schema 和调用方所需权限：

```json
{
  "provides": [{
    "name": "example.echo",
    "description": "返回输入文本",
    "version": "1.0",
    "permissions": ["example.read"],
    "inputSchema": {
      "type": "object",
      "properties": {"text": {"type": "string"}},
      "required": ["text"],
      "additionalProperties": false
    },
    "outputSchema": {
      "type": "object",
      "properties": {"text": {"type": "string"}},
      "required": ["text"],
      "additionalProperties": false
    }
  }]
}
```

在运行时注册：

```csharp
context.Capabilities.Register("example.echo", payload =>
    Task.FromResult<object?>(new { text = payload.GetProperty("text").GetString() }));
```

另一小程序在自己的 manifest `permissions` 中声明 `example.read`，然后调用：

```csharp
var capabilities = await context.Capabilities.ListAsync();
var contract = await context.Capabilities.DescribeAsync("example.echo");
var result = await context.Capabilities.InvokeAsync("example.echo", new { text = "hello" });
```

注册函数收到 `JsonElement`，可返回普通对象或 `JsonElement`。调用返回独立的 `JsonElement`；失败抛出异常。宿主在调用前检查权限和输入，在真实 C# Handler 返回后检查输出。注册名称必须存在于当前小程序的 `provides`，不能覆盖其他提供者的能力。

注册、调用和清理由宿主注入的租约代理完成，不需要宿主程序集引用；小程序停止或执行结束后能力注销。常驻 Provider 要让 `RunAsync` 保持运行，直到自身服务停止。窗口和 Dispatcher 集合由小程序在 Handler 中自行调度，宿主不操作业务集合。

预编译小程序可保留旧版上下文：宿主只在新增接口属性存在且类型匹配时注入，缺少能力接口不影响原有入口和对象注册。旧 DLL 若要使用新增能力 API，需更新其上下文定义后重新编译。截图旧 DLL 的启动和真实屏幕捕获已做兼容性验证，基础回归增加至 31 项。

## 当前真实能力

| Provider | 能力 | 输入 | 所需权限 |
| --- | --- | --- | --- |
| 剪贴板 `clipboard-history` | `clipboard.search` | 可选 `query`、`from`、`to`、`onlyUrls`、`limit` | `clipboard.read` |
| 剪贴板 | `clipboard.delete` | `text`、`time` 精确匹配一条记录 | `clipboard.write` |
| 剪贴板 | `clipboard.remind` | `date`，可选 `query`、`from`、`to` | `clipboard.read`、`calendar.write` |
| 日历 `taskbar-calendar` | `calendar.create` | `date`、`title` | `calendar.write` |
| 日历 | `calendar.list` | `date` | `calendar.read` |
| 日历 | `calendar.delete` | `id` | `calendar.write` |
| 日历 | `calendar.fromClipboard` | `date`，可选 `query`、`from`、`to` | `calendar.write`、`clipboard.read` |

`date` 为有效 `YYYY-MM-DD`。日历创建的是指定日期的待办提醒，不启用闹钟。`calendar.create` 返回 `id/date/title/created`，可在真实日历中查看、修改和删除。

历史搜索只返回文本记录；`onlyUrls` 过滤完整 HTTP/HTTPS 网址。`from` 包含起点、`to` 不包含终点；接受日期或 ISO 时间，UTC 时间转换为本地时间。`limit` 默认为 20，允许 1–100，结果按时间从新到旧排列。返回 `items`（`text/time`）和 `count`。

双向组合：

- `clipboard.remind` 在自己的历史集合中查找网址，然后以剪贴板小程序身份调用 `calendar.create`。
- `calendar.fromClipboard` 以日历小程序身份调用 `clipboard.search`，再在自身 Reminder Manager 中创建并持久化提醒。

例如查找 2026-10-01 复制的网址，为 2026-10-03 创建日期提醒：

```json
{"name":"clipboard.remind","payload":{"from":"2026-10-01","to":"2026-10-02","date":"2026-10-03"}}
```

该请求通过 `POST /v1/capabilities/invoke` 发送，携带现有本地 Agent 认证。也可在有上述权限的小程序中使用 `context.Capabilities.InvokeAsync` 或 `yanzi.capability.invoke`。

## 验证入口与结果

```powershell
dotnet run --project src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-desktop-loop.ps1 -SkipBuild
powershell -STA -NoProfile -ExecutionPolicy Bypass -File scripts/test-capability-runtime.ps1
```

基础层：50 项通过。真实运行时：35 项通过，包括真实系统剪贴板捕获、时间边界、参数错误、第三个 C# 调用者及权限拒绝、双向调用和身份审计、提醒落盘、停止注销、重启恢复与热重载重新绑定。

真实测试要求本机安装并启动上表两个已接入的 Provider。测试使用唯一网址标记和未来日期的无闹钟提醒，按具体 ID 删除测试提醒，按文本和时间删除测试历史，恢复剪贴板格式，删除临时测试客户端。测试会暂时停止/启动及热重载两个 Provider；`-SkipLifecycle` 可跳过这些生命周期检查。

结果写入 `%TEMP%\YanziDev\capabilities\run-<id>\result.json`，不包含 Agent Token。最后完整通过的报告为 `run-95f3689f835e4c17a3a93da4bb211c6f\result.json`。

本次修改前的真实 Provider 源码和 manifest 备份保存在 `%TEMP%\YanziDev\capabilities\backup-20261002-115046`。

后续可扩展 AI 聊天工具路由、通用编排、事件触发和授权界面；这些不影响本次真实小程序互相调用的验收结果。
