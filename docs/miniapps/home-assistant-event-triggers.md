# 燕子小程序：Home Assistant WebSocket 事件触发（C#）

## 目的与边界

需要由实体的实际状态变化推动小程序执行任务时，可使用 `YanziHaStateChangedStream.SubscribeAsync`，而不是在 `DispatcherTimer` 里每隔几秒发 HTTP REST 请求。

本封装基于 Home Assistant 官方 `/api/websocket` 协议：`auth_required → auth → auth_ok → subscribe_events(state_changed) → get_states`，随后持续接收 `event` 消息。**get_states 仅在初次连接和断线重连后执行一次，用于建立基线；运行期间不轮询设备。**

注意：Home Assistant 集成自身是否仍轮询硬件取决于具体集成；这里只消除了“燕子 → HA”的定时请求。

## 复用模式（兼容现有宿主）

当前燕子 C# 运行时只编译小程序的 `main.cs`，没有自动加载其它 `.cs` 的约定。为了不强制宿主升级，公共源代码维护在：

- `sdk/miniapps/HomeAssistantStateChangedStream.cs`：唯一的通用订阅实现。
- `scripts/miniapps/Sync-HomeAssistantEventStream.ps1`：把公共实现嵌入各个小程序的 `main.cs`，使用标记 `YANZI_HA_EVENT_STREAM` 定位。修改公共实现后重新执行该工具。
- `extensions/video-resume/main.cs`：首个实际应用，包含生成的适配器及独立的电饭煲状态机，不会在宿主中硬编码电饭煲业务。

例如新建小程序后执行：

```powershell
./scripts/miniapps/Sync-HomeAssistantEventStream.ps1 -EntryPath 'path/to/new-miniapp/main.cs'
./scripts/miniapps/Sync-HomeAssistantEventStream.ps1 -EntryPath 'path/to/new-miniapp/main.cs' -Check
```

之后直接在同一小程序入口里调用：

```csharp
using var lifetime = new CancellationTokenSource();
var entities = new[] { "binary_sensor.example_presence" };
await YanziHaStateChangedStream.SubscribeAsync(
    homeAssistantUri, accessToken, entities,
    async update =>
    {
        // update.EntityId、update.State、update.Attributes、update.IsBaseline
        // 初始基线和重连后的实体状态不会算作新的触发动作
        if (update.IsBaseline) return;
        if (update.EntityId == entities[0] && update.State == "on")
            await ExecuteMiniappActionAsync();
    },
    (connected, reason) => Log(connected ? "事件订阅已连接" : reason),
    lifetime.Token);
```

业务负责保存 `lifetime` 并在小程序 Stop/Quit 时调用 `Cancel()`，确保不遗留后台 WebSocket。以上是接口用法示意，其中 `ExecuteMiniappActionAsync`、`Log` 和 HA 凭证由具体小程序提供。

## API 与安全

`YanziHaStateChangedStream.SubscribeAsync(Uri baseUri, string accessToken, IReadOnlyCollection<string> entityIds, Func<YanziHaEntityUpdate,Task> onChange, Action<bool,string>? onConnection, CancellationToken stop)`

- 只允许访问明确指定的实体 ID；当前通过服务端订阅 `state_changed`，收到 HA 广播后在客户端过滤指定实体，不分发其他设备数据给业务层。
- 实现心跳、70 秒接收超时、断线自动重连（指数退避最多约 45 秒）、消息大小限制（2 MiB）、退出取消和异步回调。
- 重连先读取全部状态，再仅向业务回调传递白名单内的初始基线。基线 `IsBaseline=true`，**不能当作现场触发事件**，避免重启时误执行历史动作。
- 凭证不嵌入代码或公开配置。电饭煲示例复用已经存放在本机并由 Windows DPAPI 加密的 HA Token；仓库仅提交禁用的实体配置示例。
- 当前抽象只支持订阅 Home Assistant 的 `state_changed`；不提供调用 HA 服务或写入设备状态的方法。此范围适合只读触发器。

## “看视频”案例

电饭煲联动订阅三个实体：煮饭完成、当前状态、原生 `EventRunStatus`。业务状态机仅在近期煮饭已完成、明确观察到保温、原生 `cancel_` 发生于保温之后、随后转待机时自动打开最近剧集；单纯关机/断线/重复事件不触发。午间和晚间原有按钮仍按 UI 定时器显示，与电饭煲实时订阅无关。

即使使用 WebSocket，这仍不是实时监听实体物理按键：`cancel_` 是米家集成提供的操作事件，不能在 HA 层绝对证明取消操作来自按键还是同类远程动作。

## 验收

在仓库根目录运行：

```powershell
./scripts/miniapps/Sync-HomeAssistantEventStream.ps1 -EntryPath 'extensions/video-resume/main.cs' -Check
dotnet restore tests/video-resume/VideoResume.Verification.csproj --configfile tests/video-resume/NuGet.Config
dotnet run --project tests/video-resume/VideoResume.Verification.csproj --no-restore
dotnet run --project tests/video-resume/VideoResume.Verification.csproj --no-restore -- --live
```

`--live` 只读验证当前 Home Assistant WebSocket 的认证、订阅及实体基线和 Edge 视频历史；不会发送电饭煲控制命令，不会为了测试打开视频。

当前方案尚未变成宿主级的**声明式 trigger manifest 配置**；它是可复用的 C# 小程序源码适配器。后续如需让 PowerShell/JS 小程序共享同一事件总线，应另行扩展宿主的受控事件路由和权限校验，而不是在各运行时中复制凭证。
