# 燕子能力 Provider SDK 状态

日期：2026-10-02

## 已完成

- Provider 注册 SDK
- 能力自动进入 Registry
- Runtime Binding 自动建立
- 内置系统 Provider

当前能力：

- system.info
- system.process.list
- system.time.now

## 下一步

1. 接入 clipboard-history Provider
2. 将 ClipboardService 封装为 clipboard.latest/search 能力
3. 接入 taskbar-calendar Provider
4. 增加权限校验
5. 增加 EventBus 自动触发

## 契约与权限补充

Provider SDK 现在支持 `Version`、`InputSchema`、`OutputSchema`、`Permissions`；注册时复制 Schema 与权限，并同步写入 Runtime Registry。使用 `YanziCapabilityRuntimeRegistry.RemoveByExtension(extensionId)` 注销 Provider。

Handler 收到经过校验的 `System.Text.Json.JsonElement`。权限通过调用者上下文传递，省略上下文时按匿名、无权限处理。WebView 使用宿主持有的 manifest 权限，不能通过请求参数自行授权。

内置节点新增 `capability.list` 和 `capability.describe`；`clipboard.latest` / `clipboard.set` 要求 `clipboard`，`system.process.list` 要求 `system.process.read`。`clipboard.set` 输入必须为 `{ "text": "内容" }`。

权限校验、契约查询、历史搜索和真实日历 Provider 已接入并验证。C# 小程序使用 `context.Capabilities.Register` 注册 manifest 声明的 Handler，使用 `InvokeAsync` 调用其他小程序。停止和热重载由运行时租约自动清理。EventBus 自动触发仍是可扩展项。具体接入方法见 `yanzi-capability-runtime-integration-2026-10-02.md`。
