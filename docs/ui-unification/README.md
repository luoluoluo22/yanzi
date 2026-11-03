# 全量 UI 公共组件迁移追踪（进行中）

基线：`76d98ff`。`inventory.json` 由 `python3 scripts/inventory-ui-unification.py` 根据 Git 跟踪的源码生成。它是**候选源码清单**，不是已确认的实际页面清单，也不是 UI 验收通过证明。每个候选都需逐项确认运行时入口、嵌套弹层、公共组件调用和测试结果；动态生成的窗口与小程序需要补充运行时入口枚举，不能仅依赖文件名。`docs/` 的 TSX 为文档/预览候选，须区分真实运行页面；不应把文件数当作页面数。

本轮初步收录 176 个候选文件（桌面 XAML 59、桌面动态窗口候选 24、Android XML 布局 8、Android 动态 UI 类候选 19、Web HTML 1、Web 动态候选 5、文档 TSX 候选 60）。仍需扩展对 Android 非典型命名 Activity、C# partial 类、Java/JS 内联动态 UI、小程序单独仓库/同步扩展和服务端 HTML 字符串的覆盖，未声称全覆盖。

## 本轮代码迁移

- `src/OpenQuickHost/HotkeyCaptureWindow.xaml`：4 个旧 `PrimaryBtn/SecondaryBtn` 按钮改为共享 `Yanzi.Button.Default/Outline`，保留按钮事件、名称、可见性、默认操作。
- `src/OpenQuickHost/HotkeyCaptureWindow.xaml.cs`：构造时按用户主题调用 `YanziUi.ApplyTo(this, ...)`，只给窗口加载公共主题资源，不污染全局样式。
- 静态检查：Python 语法检查、清单 JSON 解析、迁移样式/主题调用断言、`git diff --check` 通过。
- **待验收**：Linux 服务器缺少 dotnet，无法编译 WPF 或运行 Windows UI；需 Windows 设备完成 Release 编译、快捷键录制/清空/取消/确认、浅深主题、键盘焦点及截图比对后才能把该窗口标为通过。

下一轮：补全真实 UI 入口枚举并按运行时页面建立逐页状态，优先修复现存 WPF 窗口的旧按钮/输入控件与共享库接入；持续对 Android、扩展、动态小程序进行独立技术栈组件复用审计。

## 第二轮：鼠标触发录制窗口（源码迁移，等待 Windows 验收）

- `MouseTriggerCaptureWindow.xaml`：移除窗口及录制区域硬编码深色背景/文字颜色，改用现有 `DialogWindow`、`BrushDialogBG`、`BrushTextMain`、`BrushTextSec`；取消按钮使用 `Yanzi.Button.Outline`，目标选择框使用 `Yanzi.Select`。保留原控件名称、Tag、SelectedIndex、事件绑定和录制逻辑。
- `MouseTriggerCaptureWindow.xaml.cs`：仅在此窗口初始化后按 `AppSettingsStore.Load().ThemeMode` 加载共享 WPF 主题，不更改宿主全局资源。
- 修复清单扫描器的 Java/C# 文件名及共享 UI 引用正则转义错误；该错误此前可能漏报或误报。重新生成 176 个源码候选项，仍非 176 个运行时界面。
- Linux 静态验收：XAML XML 解析、共享样式与主题调用断言、清单状态检查、Python 语法检查、`git diff --check` 均通过。
- Windows 真机待验：深浅主题、下拉选项、鼠标录制、Esc/取消及焦点行为；Linux 服务器没有 dotnet，不能将本次标为 UI 已验收。

## 第三轮：Skill 导出与通用文本输入窗口（源码迁移，等待 Windows 验收）

- `SkillExportOptionsWindow.xaml`：两个 Agent/作用域下拉框统一为 `Yanzi.Select`，取消/导出按钮改用 `Yanzi.Button.Outline/Default`，保留 `Tag`、`SelectedIndex` 和原事件。
- `SimpleTextInputWindow.xaml`：输入框改用 `Yanzi.Input`，取消/确认按钮使用共享按钮；移除背景/边框硬编码色值，改为 `BrushDialogBG` 和 `BrushBorder`；校验提示改用共享 `BrushAccent`，**警示语义色仍需 Windows 视觉复核**。
- 两个窗口的构造函数调用 `YanziUi.ApplyTo` 加载窗口级公共主题资源，未修改应用全局主题；保留回车确认、非空校验、焦点和选择逻辑。
- 静态测试：两份 XAML XML 解析、公共控件样式和事件绑定断言、`git diff --check` 通过；服务器没有 .NET SDK，**Windows 编译与真实界面交互未验收**。
- GitHub 推送仍受远程 Ubuntu 的 HTTPS/`gh` 未登录阻塞；本地提交须保留，认证恢复后推送并进行主线合并。
