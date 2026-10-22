# 燕子多浏览器连接池（2026-10-22）

## 实施范围

**一台燕子宿主同时管理多个浏览器助手 WebSocket 连接**，可包括 Ego、Edge、Chrome、Brave 等不同浏览器，也可以包括同一种浏览器的多个独立扩展实例。新连接不再踢掉旧连接。

本次仅修改浏览器助手连接层；它是后续设备/本机小程序通用连接注册表的基础，不代表已经完成所有外部设备/服务的通用接入协议。

## 路由协议（向后兼容）

- `GET /v1/browser/status`（原有鉴权）：增加 `connectedCount`、`connections` 列表，每条包含 `connectionId`、`browser`、`isDefault`、`connectedUtc`；原有 `connectedBrowser`、`extensionConnected`、`connectedBrowsers` 仍可使用。
- `POST /v1/browser/execute`（原有鉴权）：请求体可附加 `{"browser":"Ego"}` 或 `{"connectionId":"browser-123"}`。指定不存在/离线的目标会明确返回 503，不悄悄切到别的浏览器。返回结果增加实际 `connectionId` 和 `browser`。
- `POST /v1/browser/reload?browser=Edge` 或 `?connectionId=browser-123`：可精确重载指定连接上的浏览器助手，不会影响其他浏览器。
- `browser.workflow`、`browser.scrape`、`browser.snapshot`、`browser.autofill`、`browser.workflow.status` 能力均允许可选 `browser` 或 `connectionId`。原来的不带路由参数调用仍然工作。
- **默认优先 Ego**，Ego 离线则优先 Edge，然后其他在线浏览器；连接类型相同时优先最近上线的有效会话。旧 AI 网页转移功能优先已有登录态的 Edge，没有 Edge 时回退其他在线浏览器。
- `connectionId` 是**当次宿主运行时的连接 ID**，重连后会变化。业务调用前应重新查询列表，不要长期缓存。

## 任务隔离与安全
- 新增每连接独立发送锁，避免同一个 WebSocket 同时并发 SendAsync 造成协议状态错误。
- 服务端记录 `taskId → WebSocket` 的任务所有者映射，且只接受该连接发来的 `task_response`。非所有者回包或超时后回包会被拒绝；不能混淆不同 Agent/扩展的结果。
- 断开一个浏览器，只将它所属的未完成任务标为 `browser_connection_lost`，不重试写操作、不影响其他浏览器正在执行的任务。
- HTTP / WebSocket 仍受本机访问和现有鉴权策略约束；`browser=Ego` 是本机可信路由标签，不是独立授权凭据。多个扩展在同一个 Chrome/Edge Profile 下共享登录态，不等于独立账号隔离。

## 回归
```
dotnet run --project scripts/BrowserMultiSessionTests/BrowserMultiSessionTests.csproj -c Release
dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Debug --no-restore
```
多连接回归使用**正式 BrowserSessions.cs 源码链接编译**，覆盖两个浏览器共存、指定连接、重连优先级、错误回包拒绝、同连接 8 个任务同时发送、单浏览器断连、其他任务持续执行以及 Edge 自动回退。已加入 GitHub 工程 CI。

## 部署与版本保护
当前电脑安装版 `Yanzi.exe 1.0.40`，对应仓库提交 `3956cc8`；GitHub 主分支的 csproj 版本为 `1.0.27`，另有独立的 `ai-loop/release/v1.0.42` 发布分支。**严禁直接使用主分支构建覆盖正式安装包**，也不要用它的旧版 browser-extension 覆盖本地 `0.5.71`。必须以新版发布分支为基线进行兼容集成、验证扩展清单及用户数据隔离后，才可作为正式更新。
