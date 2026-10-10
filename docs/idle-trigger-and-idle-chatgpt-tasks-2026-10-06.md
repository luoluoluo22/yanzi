# 闲置触发与 ChatGPT 闲置任务（2026-10-06）

## 目标

利用电脑闲置时间执行小程序。宿主统一负责“什么时候算闲置”，小程序只负责收到 `launchSource=idle` 后做自己的工作。

闲置判定必须同时满足：

1. 键盘和鼠标持续没有输入；
2. 当前前台没有覆盖整个显示器的全屏窗口。

这避免了看全屏视频、演示、游戏时因为长期不操作键鼠而误触发。

## Manifest

```json
{
  "startup": {
    "idle": {
      "enabled": true,
      "afterMinutes": 5,
      "repeatMinutes": 10,
      "pauseWhenFullscreen": true
    }
  }
}
```

- `afterMinutes`：连续无输入多久以后允许第一次触发，范围 1–1440 分钟。
- `repeatMinutes`：同一段持续闲置中再次触发的最小间隔；0 表示这段闲置只触发一次。
- `pauseWhenFullscreen`：当前固定为 true 的推荐行为。
- 闲置执行时 `context.LaunchSource == "idle"`。
- 触发器属于宿主通用能力，不与某个小程序写死绑定。

宿主实现位于 `ExtensionIdleTriggerService.cs`。系统键鼠闲置时间来自 Win32 `GetLastInputInfo`；全屏判定使用前台窗口矩形与其所在显示器矩形比较，并排除桌面/任务栏 Shell 窗口。

## 编辑器

“新建/编辑小程序”右侧的“触发方式”已改为默认折叠的 Expander。展开后可配置：

- 开机自启
- 定时运行
- 闲置运行
- 全局快捷键
- 鼠标手势

折叠标题会显示已启用触发器数量，避免未来新增更多触发方式后长期占满右栏。

## 闲置任务小程序

源码：`tools/idle-chatgpt-tasks/`

本机安装：`%LOCALAPPDATA%\OpenQuickHost\Extensions\idle-chatgpt-tasks\`

任务数据：`%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\idle-chatgpt-tasks\tasks.json`

功能：

- 手动打开时显示任务列表，可新增、删除、重新排队、复制结果、立即执行下一项。
- 闲置触发时不打开窗口，只处理队列中最早的一项。
- 通过本机 ChatGPT 网页工作台 `127.0.0.1:53921` 的异步 Job API 提交任务。
- Job ID 一取得就持久化；后续只轮询相同 Job，工作台临时断开时不会重复提交。
- UI 每 2 秒重新读取任务数据，可看到等待、提交、排队、运行、成功/失败等状态和最终结果。
- 每个任务使用独立的新临时 ChatGPT 标签页，成功后自动关闭，避免复用旧工作台页造成 DOM/草稿状态干扰。
- `--run-next` 可用于验收或人工触发下一项。

## 验收

已完成两层验证：

1. 独立编译/真实 Bridge 测试：C# 小程序 0 编译错误，成功保存 Bridge Job ID，并得到“闲置任务链路测试成功”。
2. 生产 Runtime 实测：重新激活共享 Runtime 后，状态中 `idleTrigger=true`。当系统无键鼠输入且 `fullscreen=False` 时，Runtime 真实以 `launchSource=idle` 启动“闲置任务”，经过 ChatGPT 网页扩展后任务状态变为 `success`，结果为“燕子真实闲置触发链路测试成功”。

实测中发现复用旧 ChatGPT 工作台页偶发“等待发送按钮超时”，因此正式实现改为 `tabPolicy=new` 的隔离临时页，再次完整实测通过。

## 0.2.0：任务看板、重复任务与后台常驻

“闲置任务”从简单输入框升级为三栏任务看板：

- 左侧：按状态（全部 / 未执行 / 执行中 / 已完成 / 异常）和类型（ChatGPT / 小程序）筛选。
- 中间：任务卡片列表，显示类型、状态、重复、停用、自定义标签、最近执行结果与预计执行时间。
- 右侧：完整编辑器，支持新建、查看、编辑、删除、立即执行、启用/停用和重复执行。
- 搜索支持任务名称、内容、小程序 ID 和标签。

任务模型新增：

- `Kind`：`chatgpt` / `extension`。
- `Tags`：用户自定义标签。
- `RepeatEnabled`：成功后是否自动重新排队。
- `Enabled`：任务级启停。
- `RunCount / LastRunStatus / LastCompletedAt`：保留最近一次执行摘要。
- `QueuedAt`：公平队列时间。重复任务成功后排到队尾，避免长期重复任务饿死后来任务。

重复任务语义：

1. 单次任务成功后进入“已完成”。
2. 重复任务成功后保存结果和执行次数，再自动变回“未执行”，等待下一次闲置。
3. 重复任务失败不会自动再次排队，避免错误任务无限消耗 ChatGPT 或其他资源。
4. 多个重复任务按 `QueuedAt` 公平轮转。

“小程序”任务已接通本地 Agent API：`POST /v1/extensions/{id}/run`，可以传 input，因此闲置队列不再只服务 ChatGPT。

后台常驻方式也已调整：

- manifest 同时启用 `startup.mode = on_app_launch` 和 `startup.idle`。
- Runtime 启动后创建常驻的 `IdleTaskServiceHost`，但不弹出看板。
- 常驻对象注册到 `RunningExtensionRegistry`，因此系统托盘“正在运行的小程序”会显示“闲置任务”。
- 手动打开时只呼出已存在的看板，不启动第二个实例。
- 关闭看板窗口只关闭 UI，不停止后台服务；在“正在运行的小程序”中结束它才真正退出。

验证：

- 独立 C# 编译：0 warning / 0 error。
- Mock Agent 测试：重复小程序任务执行成功后回到 pending，RunCount=1；后面的单次任务随后成功完成，验证公平排队。
- 生产 Runtime 重启后，running snapshot 中存在唯一 `idle-chatgpt-tasks`，`launchSource=app-startup`，系统托盘计数由 6 增为 7。
- 手动启动时 Runtime 返回“已呼出正在运行的窗口”，没有创建第二实例。
