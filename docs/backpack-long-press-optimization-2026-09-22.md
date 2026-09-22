# 右键长按背包响应优化

本轮保留一次性长按定时器和用户当前阈值，优化到期后的显示路径。没有更改为高频轮询、忙等待或强行缩短默认 250ms。

## 实现

- `QuickPanelWindow.LoadSlots` 完成后保留槽位快照；再次呼出时比较配置文件路径、修改时间、大小、本进程写入版本、前台进程和收藏模式，并比较命令引用序列。没有变化时复用槽位，不重新读整个配置、不重建组和槽位。
- 订阅命令属性变更以使快照失效；命令增加、删除、替换、重排、配置更新和应用切换也会失效。未完成的重建不可复用，读取文件元信息失败时回退重建。外部程序若刻意保持时间戳与长度不变，无法仅靠元信息检测，这是此缓存的边界。
- 同一命令清单未改变时，清除搜索也不再清空并重加全部槽位。
- 图标提取通过后台任务运行，同进程名称的并发请求共享任务；过期呼出结果不回写当前界面。对其他窗口的 WM_GETICON 改为每次最多 50ms 的 SendMessageTimeout，避免无界等待；文件系统/Shell 图标提取本身并非硬超时，但已移出 UI 线程。
- 打开背包的任务记录与引导更新延后到低于渲染优先级的回调，移出显示前路径。
- 删除长按分支重复执行的浮层抑制；前台 Process 对象及时释放；关闭面板时解绑新增缓存和原有鼠标事件并停止相关定时器。
- 长按设置仅限制在 50–1500ms，不再把合法的 120、350、500ms 改回 250ms。默认值仍为 250ms，未改用户配置文件。

SendMessageTimeout 的超时适用范围参考微软文档：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeouta 。本实现从后台线程向前台窗口发送消息，未使用允许无限延长等待的标志。

## 耗时记录

在应用日志中检索 `[BackpackLatency]`：

| 字段 | 含义 |
|---|---|
| thresholdMs / longPressElapsedMs | 设置阈值 / 从启动计时到钩子线程实际判定的耗时 |
| dispatchPreparationMs | 触发诊断及浮层抑制耗时 |
| uiQueueMs | 投递后等待 UI 开始处理的时间 |
| contextMs | 获取前台应用上下文 |
| prepareMs | 定位、搜索恢复、缓存检查与必要槽位加载 |
| showMs | 准备完成到窗口显示调用返回 |
| totalHandlerMs | 背包显示方法总耗时 |
| slotsReused | 本次是否复用了槽位 |
| afterRenderQueueMs | UI 获得低于渲染优先级回调的时间；不是显示器实际呈现时间 |

先检查稳定重复呼出能否出现 `slotsReused=True`，再区分计时超期、UI 排队和窗口准备成本。不要把 Show 返回或 Background 回调当作精确屏幕首帧。

## 验证

独立目录构建通过，14 个现有警告，0 错误。

`--backpack-safety` 覆盖阈值保留及边界、未变化快照复用、所有缓存 key 失效维度、命令增删替换重排、属性变更、构建中失效、未完成快照拒绝复用、事件解绑。`--interaction-safety` 和 `--sync-safety` 也通过。

```powershell
dotnet build src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj -p:SkipStopRunningApp=true -p:OutputPath=F:/Desktop/kaifa/OpenQuickHost/artifacts/backpack-review/ --no-restore
dotnet artifacts/backpack-review/Yanzi.SyncVerification.dll --backpack-safety
```

未重启或替换正在运行的程序，未操作真实右键和云端数据；尚无实机耗时前后对比，不能宣称加速百分比。新构建需实际运行后才能评估计时器是否值得换为钩子线程的截止时间等待方案。
