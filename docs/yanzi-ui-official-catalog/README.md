# shadcn/ui x Yanzi UI: 64 item comparison register

Official directory: https://ui.shadcn.com/docs/components
Checked UTC: 2026-10-08T12:10:15.652653+00:00

Official and local catalog: 64/64 names, order and URLs match.
Components with independent WPF previews: 64/64.
Components independently reviewed for visual and full interaction parity: 0/64.
A reusable API entry is NOT proof of visual or behavioral parity.

| # | Official component | HTTP | Yanzi API | Individual preview | Visual review | Component-specific checklist |
|---:|---|---:|---|---|---|---|
| 01 | [Accordion](https://ui.shadcn.com/docs/components/base/accordion) | 200 | YanziAccordion | yes | pending | 展开/收起，单开与多开、转场、键盘与焦点 |
| 02 | [Alert](https://ui.shadcn.com/docs/components/base/alert) | 200 | YanziPrimitives.Alert | yes | pending | 图标、标题、说明、Default / Destructive 样式 |
| 03 | [Alert Dialog](https://ui.shadcn.com/docs/components/base/alert-dialog) | 200 | YanziDialog.Confirm | yes | pending | 蒙层、标题、描述、取消/确认、Esc、焦点约束 |
| 04 | [Aspect Ratio](https://ui.shadcn.com/docs/components/base/aspect-ratio) | 200 | YanziLayoutPrimitives.AspectRatio | yes | pending | 16:9 / 4:3 比例，图片裁切与响应式 |
| 05 | [Attachment](https://ui.shadcn.com/docs/components/base/attachment) | 200 | YanziContentPrimitives.Attachment | yes | pending | 文件预览、状态、移除和多附件排列 |
| 06 | [Avatar](https://ui.shadcn.com/docs/components/base/avatar) | 200 | YanziPrimitives.Avatar | yes | pending | 圆形头像、图片缺失时的 fallback、尺寸 |
| 07 | [Badge](https://ui.shadcn.com/docs/components/base/badge) | 200 | YanziBadge | yes | pending | Default / Secondary / Outline / Destructive，圆角与文字 |
| 08 | [Breadcrumb](https://ui.shadcn.com/docs/components/base/breadcrumb) | 200 | YanziLayoutPrimitives.Breadcrumb | yes | pending | 层级分隔符、省略、当前项与链接交互 |
| 09 | [Bubble](https://ui.shadcn.com/docs/components/base/bubble) | 200 | YanziContentPrimitives.MessageBubble | yes | pending | 消息气泡方向、形态、间距与长文本 |
| 10 | [Button](https://ui.shadcn.com/docs/components/base/button) | 200 | Yanzi.Button.* | yes | pending | 六种变体、尺寸、字重、加载/焦点 |
| 11 | [Button Group](https://ui.shadcn.com/docs/components/base/button-group) | 200 | YanziButtonGroup | yes | pending | 相邻按钮边框、分隔、圆角及箭头居中 |
| 12 | [Calendar](https://ui.shadcn.com/docs/components/base/calendar) | 200 | Yanzi.Calendar | yes | pending | 月份切换、日期范围、禁用日期和键盘 |
| 13 | [Card](https://ui.shadcn.com/docs/components/base/card) | 200 | Yanzi.Card | yes | pending | 容器阴影、Header / Content / Footer 间距 |
| 14 | [Carousel](https://ui.shadcn.com/docs/components/base/carousel) | 200 | YanziCarousel | yes | pending | 滚动按钮、吸附、圆点及循环控制 |
| 15 | [Chart](https://ui.shadcn.com/docs/components/base/chart) | 200 | YanziBarChart | yes | pending | 柱形/线图、坐标轴、提示、响应式 |
| 16 | [Checkbox](https://ui.shadcn.com/docs/components/base/checkbox) | 200 | Yanzi.CheckBox.Preview | yes | pending | 选中勾号、混合态、焦点、标签点击 |
| 17 | [Collapsible](https://ui.shadcn.com/docs/components/base/collapsible) | 200 | YanziLayoutPrimitives.Collapsible | yes | pending | 折叠触发器、内容高度、动效与状态 |
| 18 | [Combobox](https://ui.shadcn.com/docs/components/base/combobox) | 200 | YanziCombobox | yes | pending | 输入过滤、选中项、键盘方向与无结果 |
| 19 | [Command](https://ui.shadcn.com/docs/components/base/command) | 200 | YanziCommandPalette | yes | pending | 检索、分组、命令执行、快捷键 |
| 20 | [Context Menu](https://ui.shadcn.com/docs/components/base/context-menu) | 200 | YanziContextMenu | yes | pending | 右键定位、菜单项与子菜单交互 |
| 21 | [Data Table](https://ui.shadcn.com/docs/components/base/data-table) | 200 | Yanzi.DataGrid | yes | pending | 排序、列宽、行选择、分页和空状态 |
| 22 | [Date Picker](https://ui.shadcn.com/docs/components/base/date-picker) | 200 | Yanzi.DatePicker | yes | pending | 日期弹窗、输入格式、范围/禁用 |
| 23 | [Dialog](https://ui.shadcn.com/docs/components/base/dialog) | 200 | YanziContentDialog | yes | pending | 蒙层、可滚动内容、关闭与焦点 |
| 24 | [Direction](https://ui.shadcn.com/docs/components/base/direction) | 200 | YanziContentPrimitives.Direction | yes | pending | LTR / RTL 文字和布局方向切换 |
| 25 | [Drawer](https://ui.shadcn.com/docs/components/base/drawer) | 200 | YanziSheet.ShowDrawer | yes | pending | 边缘抽屉位置、拖动和关闭 |
| 26 | [Dropdown Menu](https://ui.shadcn.com/docs/components/base/dropdown-menu) | 200 | YanziDropdownMenu | yes | pending | 菜单弹层、键盘、子菜单、checkbox 项 |
| 27 | [Empty](https://ui.shadcn.com/docs/components/base/empty) | 200 | YanziPrimitives.EmptyState | yes | pending | 空状态图标、说明、行动按钮与留白 |
| 28 | [Field](https://ui.shadcn.com/docs/components/base/field) | 200 | YanziPrimitives.Field | yes | pending | FieldLabel、说明、错误反馈和必填 |
| 29 | [Hover Card](https://ui.shadcn.com/docs/components/base/hover-card) | 200 | YanziHoverCard.Attach | yes | pending | 悬停延时、离开关闭、定位和内容 |
| 30 | [Input](https://ui.shadcn.com/docs/components/base/input) | 200 | Yanzi.Input | yes | pending | 输入高度、Caret、placeholder、校验态 |
| 31 | [Input Group](https://ui.shadcn.com/docs/components/base/input-group) | 200 | YanziInputGroup | yes | pending | 输入框分组、按钮/图标嵌入边框 |
| 32 | [Input OTP](https://ui.shadcn.com/docs/components/base/input-otp) | 200 | YanziOtpInput | yes | pending | 单字符位、粘贴、移动焦点与错误态 |
| 33 | [Item](https://ui.shadcn.com/docs/components/base/item) | 200 | YanziItem | yes | pending | 图文排列、辅助动作、选中状态 |
| 34 | [Kbd](https://ui.shadcn.com/docs/components/base/kbd) | 200 | YanziPrimitives.Kbd | yes | pending | Kbd 边框、字体与组合键视觉 |
| 35 | [Label](https://ui.shadcn.com/docs/components/base/label) | 200 | YanziPrimitives.Field | yes | pending | 与输入框关联、点击聚焦、禁用 |
| 36 | [Marker](https://ui.shadcn.com/docs/components/base/marker) | 200 | YanziContentPrimitives.Marker | yes | pending | 标记注释、背景与状态组合 |
| 37 | [Menubar](https://ui.shadcn.com/docs/components/base/menubar) | 200 | Yanzi.Menubar | yes | pending | 顶栏菜单、弹出子项、焦点切换 |
| 38 | [Message](https://ui.shadcn.com/docs/components/base/message) | 200 | YanziContentPrimitives.MessageBubble | yes | pending | 消息区域、不同角色的状态与动作 |
| 39 | [Message Scroller](https://ui.shadcn.com/docs/components/base/message-scroller) | 200 | YanziMessageScroller | yes | pending | 自动滚到底部、保留位置、加载更多 |
| 40 | [Native Select](https://ui.shadcn.com/docs/components/base/native-select) | 200 | Yanzi.Select | yes | pending | 系统选择下拉、占位、禁用和视觉 |
| 41 | [Navigation Menu](https://ui.shadcn.com/docs/components/base/navigation-menu) | 200 | Yanzi.Menubar | yes | pending | 导航触发、活动项、响应式面板 |
| 42 | [Pagination](https://ui.shadcn.com/docs/components/base/pagination) | 200 | YanziPagination | yes | pending | 前后页、页号、禁用/边界状态 |
| 43 | [Popover](https://ui.shadcn.com/docs/components/base/popover) | 200 | YanziPopover.Attach | yes | pending | 触发定位、焦点恢复和外部点击关闭 |
| 44 | [Progress](https://ui.shadcn.com/docs/components/base/progress) | 200 | Yanzi.Progress | yes | pending | 进度条尺寸、状态与百分比 |
| 45 | [Questionnaire](https://ui.shadcn.com/docs/components/base/questionnaire) | 200 | YanziQuestionnaire | yes | pending | 问题类型、选项、确认和错误反馈 |
| 46 | [Radio Group](https://ui.shadcn.com/docs/components/base/radio-group) | 200 | YanziRadio / YanziRadioGroup | yes | pending | 单组互斥、方向键、禁用与 focus ring |
| 47 | [Resizable](https://ui.shadcn.com/docs/components/base/resizable) | 200 | YanziLayoutPrimitives.Resizable | yes | pending | 手柄、拖拽、min/max 和持久化 |
| 48 | [Scroll Area](https://ui.shadcn.com/docs/components/base/scroll-area) | 200 | YanziLayoutPrimitives.ScrollArea | yes | pending | 垂直/水平滚动条、滚动边缘与键盘 |
| 49 | [Select](https://ui.shadcn.com/docs/components/base/select) | 200 | Yanzi.Select | yes | pending | 选项弹层、搜索、定位、滚动状态 |
| 50 | [Separator](https://ui.shadcn.com/docs/components/base/separator) | 200 | YanziPrimitives.Separator | yes | pending | 分隔方向、厚度和语义颜色 |
| 51 | [Sheet](https://ui.shadcn.com/docs/components/base/sheet) | 200 | YanziSheet.ShowAt | yes | pending | 侧边抽屉、蒙层与焦点关闭 |
| 52 | [Sidebar](https://ui.shadcn.com/docs/components/base/sidebar) | 200 | YanziSidebar | yes | pending | 左侧导航折叠、选中与悬停 |
| 53 | [Skeleton](https://ui.shadcn.com/docs/components/base/skeleton) | 200 | YanziPrimitives.Skeleton | yes | pending | 骨架屏尺寸、闪动动画、加载退场 |
| 54 | [Slider](https://ui.shadcn.com/docs/components/base/slider) | 200 | Yanzi.Slider | yes | pending | 轨道、Thumb、键盘、步进和方向 |
| 55 | [Spinner](https://ui.shadcn.com/docs/components/base/spinner) | 200 | YanziLoadingRing | yes | pending | 旋转、加载语义、尺寸和对齐 |
| 56 | [Switch](https://ui.shadcn.com/docs/components/base/switch) | 200 | Yanzi.Switch.Shadcn | yes | pending | 轨道与滑块色差、位置、焦点和禁用 |
| 57 | [Table](https://ui.shadcn.com/docs/components/base/table) | 200 | Yanzi.DataGrid | yes | pending | 表头、间隔、边框与横向溢出 |
| 58 | [Tabs](https://ui.shadcn.com/docs/components/base/tabs) | 200 | Yanzi.Tabs | yes | pending | Tabs 切换、选中样式、键盘导航 |
| 59 | [Textarea](https://ui.shadcn.com/docs/components/base/textarea) | 200 | Yanzi.Textarea | yes | pending | 多行行高、光标、placeholder、滚动 |
| 60 | [Toast](https://ui.shadcn.com/docs/components/base/toast) | 200 | YanziToast.Show | yes | pending | Toast 出入动画、状态与持续时间 |
| 61 | [Toggle](https://ui.shadcn.com/docs/components/base/toggle) | 200 | Yanzi.ToggleButton | yes | pending | 单态按钮、pressed 状态和键盘 |
| 62 | [Toggle Group](https://ui.shadcn.com/docs/components/base/toggle-group) | 200 | YanziToggleGroup | yes | pending | 分组互斥/多选、间距与状态 |
| 63 | [Tooltip](https://ui.shadcn.com/docs/components/base/tooltip) | 200 | Yanzi.Tooltip | yes | pending | 出现延迟、方向、箭头、关闭行为 |
| 64 | [Typography](https://ui.shadcn.com/docs/components/base/typography) | 200 | TextBlock / tokens | yes | pending | H1–H6、正文、引用、代码、字距与行高 |

## Process
Run python scripts/audit-shadcn-components.py to compare against the current live official component index.
The gallery opens a distinct evaluation page for every item and persists six check results under the user's LocalAppData/OpenQuickHost/UiReview/ComponentAudit directory.
Only actual visual and interaction validation may turn the pending state into pass.