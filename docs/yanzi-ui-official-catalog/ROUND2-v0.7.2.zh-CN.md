# 燕子 UI 0.7.2 · 官网目录第二轮（补齐独立预览）

日期：2026-10-08

## 实际交付

- 官网完整目录保持 64 项，与网站当前的组件顺序和 Base UI 文档路径完全一致；本轮全部 64 个官方文档返回 HTTP 200。
- 独立、可操作的 WPF 预览从 48/64 提升到 **64/64**，不再以“独立演示：待补齐”作为组件页内容。
- 本轮新增独立预览：Combobox、Command、Context Menu、Data Table、Date Picker、Dialog、Drawer、Hover Card、Input Group、Menubar、Message Scroller、Navigation Menu、Popover、Questionnaire、Sheet、Sidebar。
- 每个页面继续保留官方链接、原公共 API、六项核对维度和持久化备注，所有视觉/交互的实际对齐状态仍为“待核对”而非自动通过。
- 旧专题、首页对照和组件 API 仍保留；正式 Runtime 不替换。

## 单独重写：YanziCombobox

此前只是 WPF `ComboBox { IsEditable = true }`，本轮新增公共可复用控件 `YanziCombobox`，组合无边框输入框、矢量下拉指示、圆角弹层和过滤候选列表，不实例化 Windows ComboBox。支持：

1. 输入文字后忽略大小写过滤选项；
2. 找不到匹配时显示英文空状态；
3. Down / Enter / Escape 键盘处理；
4. 选择项后同步显示值、SelectedValue 和 SelectionChanged 事件；
5. 清空选择后恢复候选列表；
6. 禁用时拒绝选择、重复值校验；
7. 使用 Geist 字体和共享主题颜色，提供独立 placeholder。

实机发现的文本框居中和 placeholder 丢失已再次修复；截图保存在
`F:\Desktop\Yanzi-UI-视觉对照\yanzi-official-combobox-v0.7.2.png`。

**官网未完整对齐的能力**：Combobox 多选 Chips、分组、自定义对象选项、aria-invalid、RTL 和弹层焦点/语义的全面无障碍检查仍待推进。其他 15 项预览中仍有基于原生 WPF 的近似实现，尤其是 Context Menu、Data Table、Date Picker、Dialog、Drawer、Menubar、Navigation Menu、Sheet。不能把“独立预览”作为“完整复刻成功”的依据。

## 验收记录

- Release 构建：0 warnings，0 errors。
- 公共组件单元/布局回归：**223 项通过**（含新 Combobox 的 8 项专项）。
- 官方目录运行时验收：**64/64** 页可打开，有官方引用、六项检查和实际预览；非占位状态。
- 原有专题 UI Automation：**102 项通过，15 个页面**。
- 本地核对记录：独立保存和相邻页面跳转成功，测试结束恢复用户旧有数据。
- 隔离 Runtime 验收：**18 项通过**；构建 0 错误、宿主已有 14 条警告；运行中的正式 Runtime 未替换。
- 官网差异台账：`docs/yanzi-ui-official-catalog/official-reference-snapshot.json` 和 `README.md`；当前独立预览 64、剩余独立预览 0，**完整视觉核对仍为 0/64 正式通过**。

## 下轮顺序

按官网目录顺序逐项比对，在同样的 100% 或指定 DPI、同样的暗色主题、同样大小的组件状态下，记录数值化的尺寸/字体/色彩/圆角/交互差异，再修改公共组件库，最后验证使用它的页面。

高优先级：Combobox（多选/分组/无障碍）、Context Menu（完全自绘）、Dialog（通用内容容器）、Drawer/Sheet（真正面板/动画）和 Data Table（列操作）。优先完成共用核心能力，避免在每个小程序中重新修补一遍。
