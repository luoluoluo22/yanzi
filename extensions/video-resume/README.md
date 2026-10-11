# 看视频小程序 v1.5.0（设备专有配置）

## 行为

- 午间 12:00–13:00、晚间 19:00–20:00：右下角继续显示原有“看视频”按钮，点击续播。
- 电饭煲：最近 8 小时出现烹饪完成事件 → 程序观察到“保温中” → Home Assistant 收到原生 `EventRunStatus` 的 `cancel_...` 事件 → 状态由“保温中”转为“待机中”时，**直接打开浏览器中的续播视频网址，不显示按钮、不再次确认**。
- 仅保温结束、自动转待机、烹饪完成、设备暂时离线、过期取消事件、重复上报均不会打开视频。取消时间必须晚于程序观察到保温的时间；未观察到保温的历史事件不会在启动后被重放。
- Home Assistant 通过单条 WebSocket 订阅 `state_changed`；启动后只读获取一次初始状态，之后实时接收实体变化，不再每 5 秒轮询设备。独立的 5 秒 UI 定时器仅维护午晚按钮。
- 订阅内置认证、实体白名单、20 秒心跳、断线指数退避重连和取消令牌；HA 断线时取消待命条件，恢复连接先读取新基线，绝不补播历史事件。
- 原生事件 `cancel_` 比单纯状态推测可靠，但它证明“收到取消操作”，不能从接口上区分电饭煲实体按键与其他途径发起的同类取消动作。

## 设备策略（宿主级）

v1.5.0 的 `manifest.json` 已启用公共 `devicePolicy`：只有预先指定的台式机设备 ID 能显示和运行；账号私有库允许为目标设备保留代码包，但不会在笔记本上自动安装；`dataSync:device` 让本机播放数据独立。**需要支持设备策略的新宿主版本才能完整执行**；尚未升级宿主的设备要先停用旧版小程序的自动启动，避免误打开视频。规则详见 `docs/miniapps/device-policy.md`。

## 视频来源

- 从当前机器 Edge 或 Chrome 的历史中查找最近 45 天有效的实际视频页面，不按午间晚间历史过滤。
- 最近 7 天如果存在同一腾讯视频电视剧的多集访问记录，优先该剧最近一次打开的集数；否则回退到最近访问的视频页面。
- 过滤站点首页和推荐入口。浏览器历史不能证明视频实际观看到的秒数，不上传、不同步历史数据。

## 本机配置（不提交实际设备标识）

`rice-cooker-link.json` 在安装小程序目录中保存设备绑定：

```json
{
  "enabled": true,
  "eventEntityId": "event.your_cooker_cooking_finished",
  "statusEntityId": "sensor.your_cooker_status",
  "runStatusEventId": "event.your_cooker_runstatus"
}
```

- `enabled:false` 可关闭米家联动，原午晚定时按钮仍可使用。
- 复用“延时关机”小程序中以 Windows DPAPI 保护的 Home Assistant Token，不复制密钥、不发送操作命令。
- 未完成本机 Home Assistant 配置的其他电脑默认不激活米家联动。
- 源码和通用的禁用状态示例随仓库同步；具体个人设备实体 ID 与令牌不进入 Git。

## 验收记录

- 2026-10-11 12:19:50 HA `EventRunStatus` 出现 `cancel_...`。
- 2026-10-11 12:19:51 HA 电饭煲状态进入待机。
- 2026-10-11 12:19:53 燕子已记录米家自动续播。
- 2026-10-11 12:19:54 Edge History 新增《我不是大师》第 14 集的访问记录。
- 上述实际运行记录发生于 v1.2.0。v1.3.0 增加原生 cancel 事件强校验；v1.4.0 进一步改为 WebSocket 事件订阅，尚未重复进行物理取消操作的实测。
- v1.4.0 通过 37 项回归断言（含模拟 `state_changed` 事件路由），并已从本机 HA WebSocket 成功订阅 3 个真实实体，浏览器续播目标仍为第 14 集。
- 通用封装为 `YanziHaStateChangedStream.SubscribeAsync()`，具体业务判断与通讯层分离；未来小程序可复用同一套适配器。见 `docs/miniapps/home-assistant-event-triggers.md`。
- 持久化回归测试：`dotnet restore tests/video-resume/VideoResume.Verification.csproj --configfile tests/video-resume/NuGet.Config`，然后 `dotnet run --project tests/video-resume/VideoResume.Verification.csproj --no-restore`；追加 `-- --live` 可执行只读的 HA 和浏览器历史验证。
- 本机 debug.log 记录自动续播与故障信息。改动有 Raccoon 事务级原始字节备份，可回退。

## 已知限制

- 开机或断网期间错过完整的“保温→取消→待机”序列时，不会补播，以防止恢复后意外打开浏览器。
- 仅识别当前配置的米家设备和该型号暴露的 `cancel_` 事件；更换设备需重新核对 Home Assistant 实体及事件格式。