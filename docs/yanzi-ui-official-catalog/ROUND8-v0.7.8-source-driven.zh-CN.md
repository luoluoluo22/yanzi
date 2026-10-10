# 燕子 UI 0.7.8：矢量选中标记、递归菜单与 Dialog 动画

日期：2026-10-08
代码仓库：`F:\Desktop\kaifa\OpenQuickHost`
上游参考：`docs/yanzi-ui-official-catalog/reference-package/` 中锁定的官方 shadcn/ui Base UI 代码与主题变量。

## 已修改的公共组件

| 组件 | 实际实现 | 本次核对边界 |
|---|---|---|
| Select | `YanziSelect` 使用 `YanziIcons.Check` WPF 矢量 Path 表示选中项，未选中项保留图标占位，避免行文字跳动；选项附带 AutomationProperties.ItemStatus | 保留基础单选能力；禁用项、分组与可访问性角色还未完整对齐 |
| Dropdown Menu | 复用 `YanziIcons.Check`、`RadioDot`、`ChevronRight`，不再用 Unicode 字符假装勾选；新 `YanziDropdownSubmenu` 支持逐层递归添加操作/子菜单/标题/分隔，单个 WPF Popup 内相邻展开、键盘 Up/Down/Home/End/Right/Left/Escape 和叶节点点击关闭整个层级 | 已实机操作二级 Share / 三级 Export as / PDF；窗口边缘自动翻转、悬停安全移动路径、RTL 和完整屏幕阅读器状态仍待精修 |
| Dialog | `YanziContentDialog` 增加约 170ms 透明度与 scale 入场、约 115ms 透明度退场；出口同时设 240ms DispatcherTimer 兜底；不再让 `IsCancel` 自动跳过退场动画；按 owner / 工作区尺寸初始化视窗、正文滚动区根据实际可用高度收缩、卡片宽度最大 384 DIP 并适配窄窗口 | 正常表单与取消、保存路径已通过 WPF 回归；UI Automation 快速连续查询可能干扰动画调度，采用分离请求验证关闭。复杂嵌套焦点/高 DPI 小窗口仍待进一步人工验收 |

新菜单示例（主 UI Gallery）：Share → Export as → PDF / Markdown。点击 PDF 通过 Windows UI Automation 执行对应回调，并确认整个菜单关闭。

## 实机与自动化验证结果

- `dotnet build src/Yanzi.UI.Gallery/Yanzi.UI.Gallery.csproj -c Release`：0 错误、0 警告。
- `src/Yanzi.UI.Verification`：**281 项公共组件检查通过**，涵盖菜单两级子项与第三级操作、矢量选中图标、窄窗口 Dialog 和动画初始状态。
- `verify-yanzi-ui-gallery.ps1`：原先 15 个专题共 **102 项**检查通过。该脚本修复了“目录已展开却再次点击总开关，反而折叠”的测试前提错误。
- `verify-yanzi-ui-showcases.ps1`：**13/13** 个重点示例均有真实可操作控件和预览先于 API 说明的正确页面结构。
- `verify-yanzi-ui-official-catalog.ps1`：**64/64** 官方目录项可访问，官方参考链接及本地核对入口保留。
- `verify-yanzi-ui-component-audit.ps1`：六项评估记录保存、相邻页面跳转和原记录恢复通过。
- `extract-shadcn-wpf-contract.py --verify`：**10 项**官方源码设计契约与 WPF 深/浅主题圆角一致。
- `sync-shadcn-reference.py --verify`：64 目录、61 Registry、60 示例及 125 个缓存文件的 SHA-256 验证通过。
- Runtime 非侵入构建：0 错误，14 条此前已存在的宿主警告；`Yanzi.RuntimeVerification` **18/18** 通过，不停止或替换用户正式 Runtime。

## 视觉留档（Windows 桌面上可直接访问）

- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-dropdown-three-level-v0.7.8-open.png` —— 三级菜单同时展开、矢量勾选/单选及右侧子菜单箭头。
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-dialog-animated-v0.7.8-open.png` —— Dialog 卡片、正文与蒙层在入场完成后。

## 尚未完成的官方视觉完全对齐

1. 深层菜单靠近桌面右边界时的自动反向展开、鼠标跨菜单安全区、动画细节、RTL。
2. Dialog 非标准缩放/DPI、小屏幕极端内容及辅助技术焦点语义的专项验收。
3. Select 的分组选项、禁用项、虚拟化大列表及视觉状态矩阵。
4. 64 个组件的逐像素截图差异没有全部签收。目录回归成功不是 64 项视觉一致性通过。

## 部署与兼容

本次仅修改 `Yanzi.UI.Wpf` 和独立 `Yanzi.UI.Gallery` 的源码与预览、UI 测试脚本；正式燕子 Runtime 未替换、没有触发生产数据更改。0.7.8 在本地安装为独立组件评估中心；未推送远程 Git。
