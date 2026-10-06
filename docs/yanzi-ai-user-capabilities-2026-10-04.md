# 燕子 AI 用户能力层进度（2026-10-04）

## 目标

把“燕子已经能做，但 AI 不知道怎么用”的产品功能统一暴露为可发现、Schema 描述、可调用的 Capability。

能力归燕子本身；浣熊继续负责开发与验证，不复制燕子的业务能力。

## 统一入口

- 能力目录：`GET /v1/agent/catalog`
- 能力说明：`GET /v1/capabilities/{name}`
- 能力调用：`POST /v1/capabilities/invoke`
- 调用日志：`GET /v1/capabilities/calls`

小程序 manifest 声明但当前未运行的能力增加 `onDemand=true`。调用时燕子会自动启动对应 Provider，完成后按现有生命周期策略释放。

## 能力元数据

所有 Capability 现在支持：

- `audience`：user / developer / system
- `category`
- `riskLevel`：low / medium / high
- `requiresConfirmation`
- `available`
- `onDemand`

开发依赖 `dependency.*` 已归类为 developer；`capability.list/describe` 归类为 system；用户能力默认归为 user。

## 本轮新增用户能力

### 设备
- `device.list`
- `device.status`
- `device.alias.set`

设备别名保存在本地，供其它能力统一使用，例如 `K70`、`我的手机`。

### 消息
- 已有并继续使用：`chat.send`
- 已有并继续使用：`chat.status`

### 截图
- `screenshot.capture`

截取 Windows 虚拟桌面并保存 PNG，可继续组合 `chat.send` / `files.send`。

### 笔记
- `notes.search`
- `notes.read`
- `notes.create`
- `notes.update`
- `notes.open`

直接复用 yanzi-notes 的账号级 `notes/sync.v1.json` 和本地 notes namespace；`notes.open` 支持电脑和燕子手机 handoff。

### 小程序
- `extension.list`
- `extension.status`
- `extension.open`
- `extension.stop`

### 文件
- `files.search`
- `files.open`
- `files.send`

`files.search` 复用 Everything；`files.send` 组合正式 `chat.send`。

### 日历
- 已有：`calendar.create/list/delete/fromClipboard`
- 新增：`calendar.upcoming`
- 新增：`calendar.update`

更新时若日历小程序正在运行，会先安全停止，写入持久数据后再恢复，避免内存状态覆盖磁盘。

### 关机
- `shutdown.schedule`
- `shutdown.cancel`
- `shutdown.pause`
- `shutdown.status`

`shutdown.schedule` 标记为 high risk + requiresConfirmation，并沿用延时关机小程序现有的日落后智能灯联动。

### 智能家居
- `home.light.get`
- `home.light.set`

复用延时关机小程序已有 Home Assistant 配置与 DPAPI 保护令牌，不输出或记录明文令牌。

### 相册
- 已有声明：`album.process`
- 新增：`album.status`

`album.process` 现在可通过 on-demand Provider 自动启动执行，不要求相册常驻后台。

### 灵感白板
- `board.create`
- `board.add`
- `board.connect`

直接写入 inspiration-board 的 .yzboard 持久格式。

### 镜子
- `mirror.open`
- `mirror.close`

## 真实验收

- Runtime Release 编译：通过，0 error。
- CapabilityVerification：65 checks 全部通过。
- ChatCapabilityVerification：34 checks 全部通过。
- 正式 Runtime/Shell 已部署。
- 能力目录：共 55 项。
- 本轮规划的 31 项能力：31/31 全部可发现；宿主能力 available=true，小程序声明能力支持 onDemand。
- `device.list / device.status / device.alias.set`：真实调用通过。
- `screenshot.capture`：真实截图通过，2560×1440。
- `notes.search / notes.read`：真实账号笔记读取通过。
- `calendar.upcoming / calendar.update`：真实调用通过。
- `home.light.get / home.light.set`：真实调用并确认设备最终状态通过。
- `album.process`：在相册未运行时通过 on-demand 自动启动并完成 grayscale 处理。
- `shutdown.status`：真实调用通过。
- `chat.send / chat.status`：此前已完成手机 ACK 闭环验收。

## 后续原则

新增小程序能力优先注册 Capability，而不是让 AI 搜索源码或重新实现内部逻辑。

用户任务应尽量由目标级能力完成，例如：

- “截图发到手机”：`screenshot.capture -> chat.send`
- “找文件发到手机”：`files.search -> files.send`
- “找笔记在手机打开”：`notes.search -> notes.open`
- “处理照片再回传”：`album.process -> chat.send`

底层开发工具继续留给浣熊；燕子 Capability 面向“AI 如何使用燕子”。
