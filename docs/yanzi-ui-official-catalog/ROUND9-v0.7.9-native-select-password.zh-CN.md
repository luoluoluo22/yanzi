# Yanzi UI 0.7.9 · Native Select 与 PasswordBox 优先修复

日期：2026-10-08

## 触发问题

根据用户提供的两张实际界面对比图：
1. 官方 Native Select 是深色、圆角、紧凑布局；燕子显示了 Windows 默认的白色下拉菜单与蓝色选中高亮，输入文字在选中状态还发生过前景色偏黑。
2. 旧“表单控件”中的密码输入框没有圆角，沿用了系统默认 PasswordBox 的矩形边框。

根因：`Yanzi.Select` 和 `Yanzi.Password` 之前只设置 WPF 控件的属性，**没有完全替换系统原生视觉 ControlTemplate**，导致深色主题没有穿透到 Popup、ComboBoxItem 和 PasswordBox chrome。

## 修改内容

| 公共样式 | 新实现 | 行为兼容 |
|---|---|---|
| `Yanzi.Password` | 和 `Yanzi.Input` 使用同一种 8 DIP 自绘圆角 Border、2 DIP 焦点轮廓、32 DIP 最小高度与 10 DIP 内边距；`PART_ContentHost` 继续是标准 WPF ScrollViewer | 保留真实 `PasswordBox`、安全输入、输入焦点与键盘、未保存密码内容 |
| `Yanzi.Select` | 自绘 ComboBox 圆角触发器、15 DIP 矢量下拉图标、Popover 深色 Popup、8 DIP 圆角选项列表、hover/selected 语义颜色；弹层宽度绑定触发器 | 保留 WPF `PART_Popup`、`PART_EditableTextBox`、键盘选择、`IsEditable` 等机制 |
| `Yanzi.Select.Item` | 新的列表项样式：30 DIP 高度、独立高亮和选中状态，不再出现 Windows 蓝色块 | 用 `ItemContainerStyle` 应用于 Native Select 所有列表项 |
| Card Showcase | 卡片登录表单的 PasswordBox 改为调用 `YanziUi.Styles.Password` | 避免只修旧“表单控件”而漏掉其他地方 |
| UI 验证 | Native Select 加入固定组件验收清单；PasswordBox 验证真实受保护的输入语义 | 累计 11 项源码契约、14 项代表性展示 |

特别修复：重新绑定 ComboBox 内部 ToggleButton 的 `Foreground` 与 ContentPresenter 的 `TextElement.Foreground`，消除“下拉菜单颜色正常，但输入框所选文字偏黑”的视觉问题。

官方上游参考：`reference-package/registry/native-select.json` 的 `rounded-lg`、`h-8`、`pr-8 pl-2.5`、`dark:bg-input/30`，以及 `reference-package/registry/input.json` 中的 `rounded-lg` 输入语义。保留 CSS px 与 WPF DIP 在不同系统 DPI 下的映射边界。

## 实机截图（用户 Windows 桌面）

- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-native-select-v0.7.9-trigger.png`（正常状态、深色前景文字）
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-native-select-v0.7.9-open.png`（展开状态、菜单与列表项）
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-password-input-v0.7.9-final.png`（圆角密码输入框）
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-password-input-v0.7.9-focus.png`（密码框获得焦点）

## 验证结果

- 公共组件：**287/287**，包含 PasswordBox 的自绘模板、`PART_ContentHost`、安全输入与 Native Select Popup/选项模板、文本颜色、editable 模式。
- 原有 Gallery：**15 页、104 项** UI Automation（新增密码框安全输入检查）。
- 官方目录：**64/64 页**。
- 官方代表性 Preview：**14/14**（新增 Native Select）。
- 评估记录保存与恢复：通过。
- 最新官方源码契约：**11 个目标组件**，本地缓存 **125 项 SHA256** 验证通过。
- Native Select UI Automation：展开选项可检测，选项的 WPF UI Automation 控件出现在预期位置；PasswordBox 真实 `IsPassword=True` 且可获得焦点。
- Runtime 隔离编译：0 错误，宿主原有 14 个警告；Runtime 隔离验证 **18/18** 通过。
- 发布：Gallery 独立预览安装到 `%LOCALAPPDATA%\OpenQuickHost\Tools\YanziUiGallery`。**未更换、停止或发布正式 Runtime**，未向远程推送。

注意：实机验证并非官网全部状态的逐像素签收；这次针对截图中的两处明显失败进行修复，视觉上的细微字体度量、缩放与长列表状态仍可在后续批次继续量测。

## 遗留与后续

对 `Yanzi.Select` 的极大列表、禁用选项与不同字体/DPI 展开方向进行后续专项验收；ComboBox 弹出列表默认采用统一深色界面，并不承诺与每个浏览器的操作系统原生 `<select>` 弹出列表完全像素一致。
