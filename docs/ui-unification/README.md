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

## 第四轮：WebDAV 凭据窗口（源码迁移，待 Windows 验收）

- `WebDavCredentialWindow.xaml`：将用户名输入、密码输入、保存/取消按钮接入 `Yanzi.Input`、`Yanzi.Password`、`Yanzi.Button.Default/Outline`，并将硬编码背景、标签、已保存用户名和错误提示颜色替换为主题资源（错误提示使用语义色 `Yanzi.Brush.Danger`）。
- `WebDavCredentialWindow.xaml.cs`：构造时通过 `YanziUi.ApplyTo` 加载当前用户主题的窗口级资源，保留 DPAPI 存储、原验证与聚焦逻辑。
- 静态检查：XML 解析、共享样式键存在性、旧按钮样式消除、事件/控件名称保持、审计清单状态、Python 语法、`git diff --check`；Linux 无 dotnet，Windows Release 编译、实际深浅主题、密码显示及键盘焦点待验收。
- 本次迁移不等于视觉/交互验收；清单中的 176 项仍是候选源码而非实际页面总数。

## 第五轮：进程黑名单选择窗口（源码迁移，待 Windows 验收）

- `ProcessPickerWindow.xaml`：窗口定位、取消、确认按钮分别使用共享 `Yanzi.Button.Outline/Default`；错误提示色改为 `Yanzi.Brush.Danger`。保留所有按钮事件、黑名单模板及可编辑进程下拉框行为；全屏开关沿用旧 `RaycastSwitchStyle`，须先验证公共开关与其状态语义兼容后再迁移，不能直接替换。
- `ProcessPickerWindow.xaml.cs`：窗口初始化后按用户主题注入共享 WPF 资源，不改变全局样式。
- Linux 侧通过 XML 解析、资源键存在、事件/控件名称保持、源码审计、`git diff --check`；Windows Release 构建、进程列表过滤、窗口拾取、全屏开关、黑名单移除、深浅主题与键盘焦点尚未真机验收。
- `inventory.json` 的 `source-migrated-awaiting-windows-qa` 只代表静态迁移，不代表交互通过。

## 第六轮：已安装程序选择窗口（源码迁移，待 Windows 验收）

- `InstalledAppPickerDialog.xaml`：底部取消/确认使用公共 `Yanzi.Button.Outline` / `Yanzi.Button.Default`；窗口背景与程序图标底色移除硬编码，改为现有语义资源。
- `InstalledAppPickerDialog.xaml.cs`：构造时调用 `YanziUi.ApplyTo`，依据现有 `ThemeMode` 选择浅/深色公共资源，保留异步扫描、列表虚拟化、双击选择、搜索及关闭事件。
- **有意未替换**：局部 ScrollViewer/ScrollBar 模板、搜索框与顶部关闭按钮模板；需先验证公共组件的滚动、键盘和提示文字行为兼容性，不以改样式冒充验收。
- 静态证据：XAML XML 解析、按钮样式与资源引用、五个关键事件仍绑定、硬编码背景消除、Python 清单与 `git diff --check` 通过。
- **未完成**：Windows Release 构建、深浅主题截图、扫描/筛选/双击/键盘/关闭实际交互；不能标为最终验收。

## 第七轮：颜色选择器（源码迁移，待 Windows 验收）

- `ColorPickerDialog.xaml`：HEX、Alpha 输入框改为公共 `Yanzi.Input`，吸管按钮改为 `Yanzi.Button.Ghost`，底部取消/确认按钮改为 `Yanzi.Button.Outline/Default`；主面板背景改为共享宿主 `BrushDialogBG`。保留输入控件名、`TextChanged`、吸管、画布拖动、色相/透明度拖动及确认/取消事件。
- `ColorPickerDialog.xaml.cs`：构造时依据用户 `ThemeMode` 使用 `YanziUi.ApplyTo` 加载共享 WPF 资源。
- **有意保留**：颜色棋盘格、色相渐变、画布游标和色板的硬编码颜色属于颜色选择器本身的功能性颜色，不应被主题替换；顶部关闭按钮模板也暂不替换，待实际行为核查。
- Linux 静态验收：XML 解析、公共样式键及事件存在性、Python 语法与 `git diff --check`。Windows Release 编译、吸管取色、透明度/HEX 双向更新、拖动、深浅主题、键盘焦点及视觉对照仍待完成。

## 第八轮：便签编辑窗口（源码迁移，待 Windows 验收）

- `PastedTextEditorWindow.xaml`：标题输入使用 `Yanzi.Input`，三个次要按钮使用 `Yanzi.Button.Outline`、保存按钮使用 `Yanzi.Button.Default`；窗口背景、输入框、统计文字、边框和状态文字改用公共主题资源。保留控件名称、文件读写、Ctrl+S、搜索/复制全文/打开位置、修改提示与全部事件绑定。
- `PastedTextEditorWindow.xaml.cs`：仅在窗口初始化时应用当前用户的共享 WPF 深浅主题，避免改变其他窗口。
- **有意保留**：正文多行 TextBox 的原生 Tab/回车、滚动和键盘事件；必须先在 Windows 真机验证公共 Textarea 模板完全兼容后再迁移。便签图标的绿色为功能识别色，暂保留。
- Linux 静态测试：XML 解析、4 个按钮样式、标题输入样式、关键事件和控件名称、主题调用、清单状态、Python 编译与 `git diff --check` 均通过。**Windows 编译与真实 UI 操作未验收**。
- 当前清单 176 个仍是候选源码文件，不是页面总数；9 个 WPF XAML 已完成部分迁移，均处于待 Windows QA 状态。推送与合并仍须验证。

## 第九轮：AI 模型选择窗口（源码迁移，待 Windows 验收）

- `AiModelPickerWindow.xaml`：旧 `PrimaryBtn/SecondaryBtn` 改用共享 `Yanzi.Button.Default/Outline`，模型勾选框接入 `Yanzi.CheckBox`，窗口、列表容器及分隔线改用 `BrushDialogBG/BrushInputBG/BrushBorder`；“已添加”标识保留原显示逻辑并改用主题前景色。保留 `ItemsSource`、`IsSelected` 双向绑定、`CanSelect` 禁用逻辑及确认/取消事件。
- `AiModelPickerWindow.xaml.cs`：按用户设置向当前窗口加载共享 WPF 深浅主题资源，不修改全局主题。
- 静态验收：XML 解析、共享资源及原有事件/绑定断言、清单状态和 `git diff --check` 均通过。服务器没有 dotnet，Windows Release 编译、模型勾选与禁用、确认取消、深浅主题截图、键盘/无障碍测试仍未完成，**不能标记为最终验收**。
- `inventory.json` 仍为 176 个候选源码文件，当前 10 个 WPF XAML 标记为部分源码迁移、待 Windows QA。此数字不代表全部运行时页面，动态 UI/小程序仍需扩大覆盖。

## 第十轮：搜索标签管理窗口（源码迁移，待 Windows 验收）

- `SearchScopesManagerWindow.xaml`：上移、下移、取消、保存四个按钮改用 `Yanzi.Button.Outline/Default`；标签可见性与 Everything 启用开关改用公共 `Yanzi.CheckBox`。保留 `IsVisible` 双向绑定、列表顺序及四个 Click 事件。
- `SearchScopesManagerWindow.xaml.cs`：构造时根据 ThemeMode（忽略大小写）调用 `YanziUi.ApplyTo`，只加载窗口级主题。
- `inventory.json`：该文件标记为 `source-migrated-awaiting-windows-qa`，不将静态检查冒充实际 UI 验收。
- 静态证据：XAML XML 可解析、公共样式键存在、四个事件与绑定保留、Python 语法与 `git diff --check` 通过。
- 待验：Windows Release 构建、标签显隐/重排保存、Everything 切换、深浅主题和键盘/无障碍检查；尚未推送与合并。

## 第十一轮：系统提示词编辑器（源码迁移，待 Windows 交互验收）

- `SystemPromptEditorWindow.xaml`：将确认/取消按钮替换为共享 `Yanzi.Button.Default/Outline`，对话框背景与边框改用 `BrushDialogBG/BrushBorder`，移除原窗口硬编码暗色。
- `SystemPromptEditorWindow.xaml.cs`：构造时按用户主题加载窗口级 `YanziUi` 资源；保留原提示词赋值、光标置尾、确认/取消 DialogResult 和多行输入框的原生 Tab、回车、滚动行为。多行 TextBox 暂不机械替换为单行共享输入模板。
- 已在家里 Windows 电脑对前一批 10 个窗口所在提交 `ef2aad8` 完成完整 Release 编译（0 错误、18 警告），并实际启动 `Yanzi.UI.Gallery.exe`，看到“燕子 UI · 组件评估中心”窗口；**不代表十个业务窗口逐项交互验收**。本轮新改动需再次 Windows 编译和真实提示词编辑交互验证。
