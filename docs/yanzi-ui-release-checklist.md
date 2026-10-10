# Yanzi UI 0.4.2 · 组件评估与发布检查表

完成于 2026-10-08。该清单区分“组件有 API”、“已能展示”、“自动测试通过”、“生产可部署”四种不同状态。

- [x] 总览不再展示统计数字，改成真正可点击的 shadcn 风格组件墙。
- [x] 新增 5 个目录页面和 1 个完整组件索引，共 15 个体验页面。
- [x] 对照官方 64 个组件条目，建立 YanziComponentRegistry：全部有一个可调用的 WPF 原生适配 API/样式/控件类。
- [x] Button、Badge 六变体，主题令牌，深浅模式与旧资源键兼容。
- [x] Base WPF 组件：输入、选择、表格、菜单、日期、滑块、Tab、Toast、Dialog。
- [x] 复用类：OTP、Pagination、Carousel、Accordion、InputGroup、ButtonGroup、BarChart、CommandPalette、Item、Sidebar、Sheet、MessageScroller、Questionnaire、ToggleGroup、HoverCard、Popover 等。
- [x] 公共库和评估器独立 Release 编译 0 错误、0 警告。
- [x] 自动化组件单元验收通过（113 项，含 Scrollbar）。
- [x] 15 页 Windows UI Automation 通过（69 项）。
- [x] 独立 Runtime 构建通过（宿主现有 14 个警告；0 错误）。
- [x] 隔离 Runtime 回归 18 项通过，包含动态小程序对 Yanzi.UI.Wpf 的引用。
- [x] 评估中心作为独立预览安装，本地组件评估小程序和桌面快捷方式已更新。
- [ ] 高 DPI 100/125/150/200%、中文输入法、无障碍读屏、键盘完整人工验收。
- [ ] 对标 shadcn 官方每个组件的具体外观变体与所有细节行为逐项验证。
- [ ] 生产 Runtime 版本化激活、回滚和旧剪贴板/日历/截图/白板回归验收。
- [ ] 首个真实业务小程序完成渐进迁移并衡量开发耗时与缺陷率。

关键限制：
“64/64 有可调用入口”表示覆盖了同名能力的 WPF 原生可复用基线；不表示 64 个组件已与 React/shadcn 完全等价。
部分高级组件沿用系统原生交互，复杂焦点陷阱、滚动虚拟化、键盘操作、拖拽动画仍需专项验收。
生产 Runtime **没有被替换**。本轮构建使用 SkipStopRunningApp=true 避免主动停止运行中的实例。
