# 燕子 UI 评估中心 v0.8.0：首页全量公共组件墙

2026-10-09

## 核心实现
- 唯一组件清单：`src/Yanzi.UI.Wpf/YanziComponentRegistry.cs`，保持官网已登记的 64 个条目及原顺序；不在首页复制维护第二份名称。
- 主页新增 `src/Yanzi.UI.Gallery/GalleryOverviewComponentWall.cs`，通过 `YanziComponentRegistry.Components` 生成 64 张卡片。
- 首页每个卡片用现有 `TryRenderIsolatedPreview(name, host)` 工厂创建 *真实的* 公共 WPF 控件实例；详情页 `ShowComponent(item)` 使用同一预览工厂，因此公共库控件与样式修改后重新构建 Gallery，两个位置同步更新。不是预渲染图片，也不是热更新已加载的 DLL。
- 缩略图仅将详情预览的外层舞台压缩成一致的定高窗口，并以 WPF Viewbox 等比例展示；控件和资源引用仍然来自公共库。
- 标题 `查看组件 {Name}` 可点击打开独立对照详情。详情新增 `返回全部组件首页`，返回保留搜索词和原滚动位置。
- 首页搜索 `首页筛选组件` 按组件名、类别分组或 API 即时过滤。响应式每行 2/3/4 张卡片。完整 64 项纵向滚动，避免把所有可交互控件缩成单屏难以辨认的图标。
- 原精选业务演示保留在全量组件墙下方，保证旧控件演示与既有自动化兼容。
- 独立 Gallery 版本提升到 `0.8.0`，未重启燕子正式 Runtime。

## 测试与验收
- Release: 0 个错误、0 个警告。
- 公共组件库：289 项设计系统检查通过。
- `scripts/verify-yanzi-ui-home-wall.ps1 -ProcessId <PID>`：主页 64/64 控件标题、Badge 搜索 64→1、点进 Badge 详情、返回并保持筛选、清空回到 64、Typography 末项导航及返回均通过。
- `scripts/verify-yanzi-ui-gallery.ps1 -ProcessId <PID>`：15 个页面、104 项 Windows UI Automation 检查通过。
- `scripts/verify-yanzi-ui-official-catalog.ps1 -ProcessId <PID>`：64 个详情页逐个访问，0 错误。
- 真正独立 WPF Gallery 程序渲染截图（自动化屏幕输出，不是伪造原型）：`F:\Desktop\kaifa\OpenQuickHost\.tmp\ui-gallery-home-v0.8.0.png`。
- 全量控件预览造成一定启动渲染开销；如未来目录进一步增大，可加虚拟化可视区域，但不要退化为静态占位图。

## 发布方式与文件
- `scripts/install-yanzi-ui-gallery.ps1 -SkipBuild -Launch`：生成新的只读版本快照，并将桌面快捷方式、`yanzi-ui-gallery/manifest.json` 的 openTarget 指向新版 exe。
- 已安装 v0.8.0：`%LOCALAPPDATA%\OpenQuickHost\Tools\YanziUiGallery\preview-20261009-103458-876\Yanzi.UI.Gallery.exe`。
- 旧版安装目录仍然保留，可以从原快照运行；未覆盖、停止或重启正式燕子宿主。
- 只改 Gallery 源文件、安装器及新增专项测试，不提交仓库中其他工作流的未提交修改。

## 后续维护约束
- 新组件应登记在共享 `YanziComponentRegistry` 并为 `TryRenderIsolatedPreview` 增加真实控件预览；首页会自动纳入。
- 不在首页另写控制按钮/徽标/输入框的静态相似物；所有预览直接调用共享工厂。
- 控件本身发生源码变化后需重新构建并重新打开 Gallery 进程（不是 DLL 热重载），以反映该变更。
