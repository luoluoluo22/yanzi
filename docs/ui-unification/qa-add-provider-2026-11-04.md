# 2026-11-04 · 添加模型服务提供商窗口公共组件改造

- **来源**：`src/OpenQuickHost/AddProviderWindow.xaml` 与 `.xaml.cs`，源代码提交 `89567af2e7cacbcf068719272d3e935aa470bcc1`。
- **Windows 设备**：家里电脑 `PC-202409131508`；与正在运行的燕子 1.1.8 隔离的 Git worktree `F:\Desktop\kaifa\yanzi-ui-add-provider-qa-20261104`。
- **公共组件**：`Yanzi.Input` 输入框、`Yanzi.Select` 下拉框、`Yanzi.Button.Outline` 和 `Yanzi.Button.Default` 操作按钮、`Yanzi.Brush.Danger` 错误提示语义资源。窗口背景改用主题 `BrushDialogBG/BrushBorder`，并在构造后接入 `YanziUi.ApplyTo`。
- **初始化兼容性**：采用 `DynamicResource`，防止公共资源先于窗口级资源注册时解析失败。
- **功能保留**：默认 `OpenAI` 类型、全部既有提供商类型、名称两端空白剔除、必填验证、回车确认、取消不保存、原事件绑定不变。
- **Windows Release**：`dotnet build OpenQuickHost.sln -c Release --nologo -v quiet`，退出码 0，无编译错误（已有其他文件中的 nullable/unused-field 警告）。
- **真实 WPF 弹窗冒烟**：`tests/WpfUiMigrationSmoke` 新增两项合成数据场景，第一项测试空名称错误/不关闭并选择 Gemini 完成提交，第二项测试取消不保存；全套 `RESULT pass=15 fail=0`，退出码 0。

**未完成**：正式设置页保存提供商并检查云同步、浅色/深色视觉截图、屏幕阅读器与完整键盘导航、各模型服务商真实 API 联通。此窗口状态必须保持 `windows-runtime-smoke-passed-awaiting-functional-visual-qa`，不是最终验收通过。此次没有修改正式安装的燕子用户数据。
