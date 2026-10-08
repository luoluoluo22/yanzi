# 燕子 UI 0.7.3 · Context Menu / Dialog / Sheet & Drawer 逐项升级

日期：2026-10-08
参考：
- https://ui.shadcn.com/docs/components/base/context-menu
- https://ui.shadcn.com/docs/components/base/dialog
- https://ui.shadcn.com/docs/components/base/sheet
- https://ui.shadcn.com/docs/components/base/drawer

## 范围和实机差异

本轮复用公共库能力，绝不把 WPF 已经具备的控件 API/页面“可操作”误判为与 shadcn 视觉像素、可访问性和所有行为一致。下面“本轮完成”是实现与实机基本交互通过；正式官网逐状态/逐像素验收仍未签收。

| 组件 | 原有实现的偏差 | 本轮完成 | 尚需修复／人工比对 |
|---|---|---|---|
| Context Menu | 原生 Windows ContextMenu / MenuItem，有系统弹层外框 | `YanziContextMenu` 独立 Popup；坐标定位、右键事件、Apps / Shift+F10 键、快捷键提示、标题/分隔、勾选、危险操作颜色、上下键选中和 Esc 关闭 | 子菜单与展开/隐藏延迟、互斥单选分组、完整 RTL、外边缘回退和逐像素对照 |
| Dialog | 通用内容直接调用 `YanziDialog.Confirm`，无法编辑任何自定义布局 | `YanziContentDialog`，支持任意 WPF 表单内容、滚动区、固定标题和底部操作区、蒙层、关闭 X、Esc、Cancel / Save、owner-modal；旧 `YanziAlertDialog` 不变 | 完整的 Tab 焦点环测试、动画、RTL、最大窗口/小 DPI 的自适应和浏览器对照 |
| Sheet | 带 Windows 系统标题栏的右侧第二窗口 | `YanziSheetOverlay`，owner 整体蒙层、无标题栏、四边方向、180ms 滑入、关闭 X、Esc、点击蒙层关闭；旧 `YanziSheet.Show` API 保留 | 动画出场、RTL、高 DPI、完整焦点陷阱和多窗口尺寸 |
| Drawer | 原本只是右侧 Sheet | 基于 `YanziSheet.ShowDrawer` 在下边缘展开，335 DIP 高度、上方两角 16 DIP 圆角和可见手柄 | 真正手势拖拽关闭、多级吸附 snap points、触屏滚动冲突、动画回弹 |

## 实机截图

- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-context-menu-v0.7.3-open.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-dialog-v0.7.3-live-check.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-sheet-v0.7.3-open.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-drawer-v0.7.3-open.png`

实机操作已核对：Context Menu 的独立 Popup 和快捷键显示、Dialog 的可编辑输入及 Cancel、Sheet 右侧遮罩、Drawer 底部面板和关闭按钮。所有示例的可见性已确认；“像素级完全一致”仍处于待核对。

## 工程验收

- Yanzi UI Gallery v0.7.3 Release 编译：0 error / 0 warning。
- 共享 WPF 组件检查 **238/238 通过**（上一轮 223，新加 15 项）。
- Windows UI Automation 进入 **64/64** 官网独立目录页，各页具有预览、参考文档、六项待核对状态和保存入口。
- 原有 15 个专题页面检查 **102/102 通过**。
- Avatar 审查备注与六项状态保存、下一项跳转测试通过且恢复原始记录。
- 生产 Runtime 不替换的构建 0 error、原有 14 warnings；隔离 Runtime 18/18 通过。
- 全部 64 官方目录页仍可进入；**64/64 独立预览，正式视觉/完整行为验收并非 64/64 通过**。

## 兼容性设计

- 不删除旧样式键 `Yanzi.Menu` 和确认 API `YanziDialog.Confirm`。
- `YanziSheet.Show(Window, string, UIElement, bool)` 原签名保留，并替换底层为 owner 绑定的 overlay；新增 `ShowAt` / `ShowDrawer`。
- 新组件代码只在 Yanzi.UI.Wpf 与独立 Gallery 预览中生效；未替换运行中的正式 Runtime、未修改其他小程序。
- 评估记录不自动改成“通过”，用户手动签收前仍显示“待核对”。

## 下一步重点

先补 Context Menu 的单选组和子菜单、扩展 Dialog/Sheet 的焦点循环与出场动画，再推进 Drawer 拖拽 snap points。每完成一项才更新对应 UI 评估页的说明和自动化检查，不靠统计数字代替视觉对照。
