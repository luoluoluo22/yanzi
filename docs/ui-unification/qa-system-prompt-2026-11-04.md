# 系统提示词编辑窗口公共 UI 改造验收 · 2026-11-04

- 源码：`src/OpenQuickHost/SystemPromptEditorWindow.xaml` 及 `.xaml.cs`；源自服务器独立开发提交 `8b8fc53`，经同步合并提交 `40b1e93`。
- Windows 测试设备：`PC-202409131508`，.NET 9 SDK 9.0.201，独立工作树 `F:\Desktop\kaifa\yanzi-ui-prompt-qa-20261104`，基线 main `a910827`。
- 重复 UI 改动：确认/取消按钮接入 `Yanzi.Button.Default/Outline`，背景边框接入主题资源；公共按钮使用 `DynamicResource`，避免窗口 `InitializeComponent()` 早于 `YanziUi.ApplyTo()` 时发生 XAML 资源异常。
- 保留 WPF 多行文本框的 `AcceptsReturn`、`AcceptsTab`、换行、滚动和光标逻辑。暂不将专用多行输入机械替换为单行 `Yanzi.Input`。
- Windows Release 编译 `dotnet build OpenQuickHost.sln -c Release`：退出码 0，没有编译错误。
- 真实窗口交互测试：`tests/WpfUiMigrationSmoke` 新增两项：取消不更改待保存提示词；确认精确保留换行和 Tab，验证公共按钮资源可用。
- 扩展套件：`RESULT pass=13 fail=0`，Windows 真机完成 11 个原窗口加系统提示词编辑窗口的测试，旧测试全部回归通过。
- 未验收：深浅主题视觉截图、屏幕阅读器/键盘导航、通过正式 AI 对话设置保存后的链路与云同步；仍保留 `windows-runtime-smoke-passed-awaiting-functional-visual-qa`，不能标为全部完成。
