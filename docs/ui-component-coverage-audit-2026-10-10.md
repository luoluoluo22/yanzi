# 燕子公共 UI 组件覆盖审计（2026-10-10）

## 新一轮完成：Chart / Carousel / Scroll Area（2026-10-10）

- **Chart 新共享类型 `YanziChart`**：目前支持 Bar、Line、Area、Pie 四种基础图形，公共数据点 API、点击或键盘选择、主题化 Tooltip、五种语义图表颜色、饼图百分比图例、坐标辅助线和空数据状态。组件详情页使用公共类型及中文切换菜单，`GalleryCatalog` 也已引用；旧 `YanziBarChart` 保持兼容。真实窗口验证图形类别切换、柱状和饼图显示、图例，未执行每个绘制物的全量鼠标点击验收。
- **Carousel 拖动基础交互**：在原有上一张/下一张、循环、位置指示上增加鼠标拖动和松开后的邻近项目吸附；保留原来 Add/Scroll/GoTo。代码和位置状态回归已通过，真实拖动手势及触屏惯性尚未做完整 E2E 验收，因此不应声称官网 Embla 等价。
- **Scroll Area 横向模式**：`YanziLayoutPrimitives.ScrollArea(content, height, horizontal:true)` 配合独立 `Yanzi.Scrollbar.Horizontal` WPF 公共模板。真实窗口已经看到横向滚动条，保留原来纵向接口。
- **验收**：公共库、组件展示编译各 0 警告 0 错误，公共组件自动化测试 **342 项全部通过**，仓库 diff --check 正常。
- **状态口径更新**：64 项登记，Ready **61**、Preview **3**（Chart、Data Table、Navigation Menu）。Chart 不能因为新增四种基础图形就当作官网所有 Radar、Radial、Tooltip、交互图表变体均已实现。
- **剩余优先级**：Chart 进阶交互/多系列及 Radar/Radial；Carousel 原生拖动 E2E、过渡动画、RTL/纵向；Scroll Area 的 RTL/虚拟化性能；Resizable 垂直方向、状态保持；64 项×6 维官网视觉/键鼠验收记录。不要以 342 个自动化断言替代这一人工核对。


## 本轮迁移进度（在最初审计后追加）

- **P0 已处理（2026-10-10）**：组件评估中心 `GalleryCatalog.cs`、`GalleryIndex.cs`、`Program.cs` 的直接 `DataGrid / Menu / MenuItem / ContextMenu` 构造已迁移到公共 `YanziTable / YanziMenubar / YanziDropdownMenu / YanziContextMenu`。当前对 `src/Yanzi.UI.Gallery/**/*.cs` 的上述原生构造全文检索无结果。
- **组件索引验收**：真实运行显示 64 项，按名称搜索 Date Picker 后显示 1 项，清空后恢复 64 项；原有双击跳转入口保留并添加公共 `YanziTable.RowDoubleClicked` 事件。自动化搜索与数量检查已通过；鼠标双击行为仍应在人工 UI 验收中复核。
- **P1 Scroll Area 已处理基础视觉**：继续使用 WPF 原生滚动逻辑，但生成的竖向 ScrollBar 引用现有 `Yanzi.Scrollbar` 样式；增加 Thumb 的悬停和拖动状态，并在真实界面看到定制滚动条。
- **P1 Resizable 已处理基础视觉**：公共 `Yanzi.ResizableHandle` 提供可视拖动手柄、hover/focus/disabled；保持 GridSplitter 原生拖动及键盘增量，真实窗口 UIA 可识别“调整面板宽度”手柄。
- **P1 Avatar 已补公共 `YanziAvatar`**：支持 ImageSource、缩写回退、图片加载失败回退，已有头像参考页改成引用它；仍需多形状和上传占位等高级变体。
- **P1 Label 已补公共 `YanziLabel`**：可以聚焦目标控件、标记必填，并通过 AutomationProperties.LabeledBy 关联输入控件；Field 也改为引用公共 Label。
- **P1 Carousel 已完成基础导航**：新增项目吸附定位、前后切换、循环开关、当前位置状态及按钮边界禁用，保留旧 Add/Scroll 接口；官网复杂拖拽动画与高级触控体验仍需继续对照。
- **回归**：本轮公共库及展示程序编译均为 0 警告、0 错误，公共组件自动化验证 **331 项通过**。这与官网逐项视觉/交互人工验收仍是两种不同口径。

**下一轮剩余：** Chart 的非柱状变体和数据交互；Carousel 的动画、拖拽/触屏行为；Scroll Area 横向滚动/RTL 与 Resizable 方向和拖动约束；64 项逐项官网对照记录。下文 P0/P1 项列举的是**初次审计时发现的问题**，其中部分已在上述进度中处理，不能当作当前仍未修复的清单。


> 本文件用于后续迭代防遗漏。仅依据当前仓库源码、组件登记表和本机评估记录判断，不把编译通过当作官网视觉一致。

## 审计口径

| 维度 | 结果 | 来源 |
|---|---|---|
| shadcn/ui Base UI 名称映射 | 64/64 已登记 | `src/Yanzi.UI.Wpf/YanziComponentRegistry.cs` |
| 当前登记 Ready | 61（Chart 改为 Preview） | 同上（Ready 仅表示可复用接口） |
| 当前登记 Preview | 3：Chart、Data Table、Navigation Menu | 同上 |
| 官方组件单独入口 | 64/64 有独立预览（Card 由 Reference Showcase 绘制） | `GalleryComponentAudit.cs`、`GalleryShadcnShowcases.cs`、`GalleryComponentPreviewExtras.cs` |
| 每组件六项视觉/交互人工验收 | 本机尚无保存记录（0/64 项记录） | `%LOCALAPPDATA%\OpenQuickHost\UiReview\ComponentAudit` |
| 公共组件自动回归 | 最近一次 342 项通过 | `src/Yanzi.UI.Verification/Program.cs`，该数不能证明 64 个组件与官网等价 |

## 已确定仍需覆盖的地方

### P0：旧展示页绕过公共组件（可直接迁移）

1. `src/Yanzi.UI.Gallery/GalleryCatalog.cs`
   - 约第 248 行仍用 `DataGrid`；迁移到 `YanziTable` / `YanziDataTable`。
   - 约第 362 行仍用 `ContextMenu/MenuItem`；迁移到 `YanziContextMenu` / `YanziDropdownMenu`。
   - 约第 399 行仍用 `Menu/MenuItem`；迁移到 `YanziMenubar`。
   - 约第 184 行用原生 `Expander` 但已有共享 `Yanzi.Expander` 完整模板，可保留行为并复核视觉。
2. `src/Yanzi.UI.Gallery/GalleryIndex.cs` 约第 38 行仍用 `DataGrid` 做组件索引。替换需要保留文本筛选、状态筛选和双击跳转。
3. `src/Yanzi.UI.Gallery/Program.cs` 约第 512、534 行仍直接创建 `ContextMenu/MenuItem`；应统一复用公共菜单及正确的弹层定位。
4. 避免修改旧页面时重复实现一份局部样式；先引用公用 API，保留原有交互，再比较视觉。

### P1：虽然标记 Ready，但功能明显只是官网子集

| 组件 | 当前源码证据 | 主要不足 |
|---|---|---|
| Chart | `YanziBarChart.cs` 仅提供柱形数据 `SetData` | 缺少折线、面积、饼图、图例、坐标轴、Tooltip 等官网图表场景 |
| Carousel | `YanziCarousel.cs` 以原生 `ScrollViewer` 水平滚动和按步长按钮为主 | 吸附、循环、拖动、分页指示状态未完整对齐 |
| Scroll Area | `YanziLayoutPrimitives.ScrollArea` 直接返回 WPF `ScrollViewer` | 原生滚动条视觉与悬停行为未统一 |
| Resizable | `YanziLayoutPrimitives.Resizable` 使用 WPF `GridSplitter` | 手柄造型、方向、约束、键盘与状态记忆需要单独验收 |
| Avatar | `YanziPrimitives.Avatar` 仅绘制字母缩写 | 图片源、加载失败 fallback、形态变体尚未作为完整 API 覆盖 |
| Label | Registry 映射到 `YanziPrimitives.Field` | 标签与输入控件的焦点关联、独立禁用/必填语义需独立核对 |
| Calendar / Date Picker | `YanziCalendarMonth` 与 `YanziDatePicker` | 单日与上下限已实现；日期区间、快捷预设、月份/年份下拉等扩展未完成 |

这些不代表当前业务一定需要全部实现，但不能用 Ready 误表示“官网同等功能”。

### P2：登记为 Preview、已实现核心 MVP，仍不应当升级到 Ready

- Data Table：`YanziDataTable.cs` 已有本地筛选、排序、列隐藏、跨页选择、分页有界渲染；尚缺服务端分页、滚动视口虚拟化，以及官网的更完整高级操作。
- Navigation Menu：`YanziNavigationMenu.cs` 已有双列宽面板、链接分组与切换；RTL、窄屏响应式和精细过渡动画尚需对照验收。

## 验收要求与下一轮顺序

1. **优先消除旧原生调用**：GalleryCatalog → GalleryIndex → Program。为每处保留原先数据和交互的可自动核查项。
2. **接着核对明显“简单包装”控件**：Scroll Area、Resizable、Carousel、Avatar、Label；确认组件 API、暗色/浅色、键鼠与缩放。
3. **再做 Chart 与其他复杂变体**：分批实现，不因只新增一个示例就升级状态。
4. 为 64 项逐个填充 **布局尺寸 / 字体图标 / 颜色边框 / Hover Focus Disabled / 键鼠交互 / 深浅主题缩放** 6 个维度的评估记录；**默认待核对，不自动判通过**。
5. 每轮结束必须区分：已登记、可复用、已真实渲染、自动化回归、与官网人工对照通过。所有已做修改需经过构建与真实 UI 验收。

**边界说明**：本次为覆盖审计，不包含源代码迁移、宿主安装、正式发布；当前工作区还有其他未提交开发修改，不能因审计而批量提交。
