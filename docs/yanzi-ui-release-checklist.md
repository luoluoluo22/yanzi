# Yanzi UI 0.1.0 发布与迁移检查表

- [x] 公共库独立 Release 编译成功、无警告
- [x] Yanzi.UI.Verification 全部通过（25/25）
- [x] Windows UI Automation 验证 8 页导航、输入、开关、列表、主题切换（28 项）
- [x] Gallery 构建成功
- [ ] Gallery 人工验收中文输入法、Tab、Esc、Enter、菜单、焦点框
- [x] 主宿主和 Runtime Release 编译成功；使用 SkipStopRunningApp 避免主动终止运行中进程（0 错误、14 个宿主原有代码警告）
- [x] 共享 Runtime 隔离回归验证通过（17/17）
- [x] Runtime 和宿主构建输出均包含 Yanzi.UI.Wpf.dll 0.1.0
- [x] 隔离 Runtime 内的小程序通过 Roslyn 动态编译并成功引用 Yanzi.UI.Wpf.dll（Runtime 共 18 项通过）
- [ ] 隔离环境运行完整小程序 WPF 窗口并调用 YanziUi.ApplyTo，进行渲染与生命周期验收
- [x] 已单独安装 UI 评估程序、桌面快捷方式及本地“组件评估”小程序入口（不替换生产 Runtime）
- [ ] 正式 Runtime 版本化激活和故障回退经过验证
- [ ] 旧剪贴板、日历、截图和白板在更新后正常运行
- [ ] 100% / 125% / 150% DPI、高对比度和完整键盘操作人工验收
- [ ] 第一个真实小程序试点有备份、回归和开发耗时数据

阶段验收严禁混淆：源代码已构建、组件自检、实际窗口手工验收、正式部署，是四种不同状态。
