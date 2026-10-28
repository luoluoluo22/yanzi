# 燕子 UI 0.7.7 · shadcn 官方源码第二批适配

日期：2026-10-08
上游基准：本地 `reference-package/manifest.json` 指定的 `base-nova` Registry 快照与 GitHub commit `0132174664c07d41262fb51012d0cc782e458e6c`（Registry 获取时间与 Git commit 可能存在差异）。

## 范围

Button、Input、Select、Dialog、Dropdown Menu：从已缓存的官方 Registry TSX 中提取 slot、class 与 spacing/radius；保持 Yanzi.UI.Wpf 原生实现，不加载 React、npm 或浏览器控件。`scripts/extract-shadcn-wpf-contract.py` 从 5 项扩展为 10 项，输出 `wpf-source-contract.json`。

| 组件 | 官方源码参考 | 公共库本轮修改 | 尚待对齐 |
|---|---|---|---|
| Button | 默认 `h-8` (32 CSS px)、`px-2.5` (10 CSS px)、`text-sm` | 公共按钮基础 Style：最小高度 32 DIP、自动内容宽度、左右 padding 10、字号 14；保留 Default/Secondary/Outline/Ghost 等 API | focus-visible 边框/3px ring 和 active 微位移、icon variants、大小变体 |
| Input | `h-8`、`px-2.5`、`text-base` | WPF TextBox 基础 Style：最小高度 32 DIP、左右 padding 10、字号 16、保留原来自绘 caret/focus；两种可编辑/disabled 的实际示例 | Placeholder 专属语义、invalid 状态、文件输入及输入法状态视觉 |
| Select | 单独的 trigger/content/item/popover，不等于可搜索 Combobox | 新增 `YanziSelect`，纯 WPF Popup，支持 5 类选项、选中回调、Enter/Space/上下键/Esc、主题资源与触发器宽度绑定；Gallery 不再用原生 ComboBox 作为本组件的主示例 | 分组/标签、边界避让、上下滚动按钮、RTL、完整焦点角色 |
| Dialog | 内容 `rounded-xl`、`p-4`、`sm:max-w-sm`、分离 footer | `YanziContentDialog` 调整到 384 DIP 内容卡、16 DIP padding、14 DIP 圆角；底部 footer 独立主题色及边线，保留模态/取消/保存和焦点循环 | 小窗口响应性、缩放进出动画、严格焦点环与 Screen Reader 验证 |
| Dropdown Menu | 独立 Popup、快捷键、checkbox/radio 项，菜单 `p-1 rounded-lg` | `YanziDropdownMenu` 增加文字/快捷键两列、键盘方向键、Check/Radio 功能、危险操作色及紧凑行高；**保留旧 API 默认向上展开**，新官网示例显式 `PreferAbove=false` 向下展开 | Unicode 的选择标记尚未改为官网 SVG；多级菜单与鼠标安全移动、RTL、动画仍不同 |

## 工程安全边界

- 不删除已发布的 `Yanzi.Select` 原生 `ComboBox` 样式，便于旧程序继续使用；新源码映射在组件注册表中为 `YanziSelect`。
- 之前原有 Dropdown Menu 默认自按钮**上方**打开。这轮官网预览选择下方，第一次全量测试发现旧专题回归失败，于是改为保留旧默认 + 新预览显式声明下方；重新运行旧测试后通过。
- 只更新公共 UI、Gallery 及验证程序；**未停止、替换或发布正式 Runtime**。登录、文件等皆是本地演示，不上传或触发外部动作。
- 页面处于“源码结构/基础行为对齐”阶段，不对没有做完的逐像素验收自动标注通过。

## 证据与测试

- Gallery 0.7.7 Release 构建：**0 错误、0 警告**。
- WPF 共享组件测试：**274/274** 通过（上一轮 263）。
- Gallery：**13/13** 个官网示例有实际控件，且主预览先于 API 说明。
- 官方完整目录：**64/64** 个页面可打开；审核状态与前后跳转通过。
- 旧专题：**15 页、102 项** UI Automation 回归通过。
- Select：真实 Windows UI Automation 打开选项并点击 Banana。
- Dropdown Menu：实机显示快捷键、勾选和 Radio，切换 Check 后仍保持菜单打开。
- Dialog：实机弹出内容、可见 Cancel 与 Save、Owner 遮罩，卡片无系统标题栏。
- Runtime 编译：0 错误、14 个既有宿主代码警告；独立验证 **18/18**。
- 官方参考包：**64 个目录项、61 个 Registry、60 个示例、125 个文件 SHA-256 验证通过**。
- 设计契约：**10 项组件**，两种 WPF 主题圆角规则一致。

实机截图：
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-source-driven-v0.7.7-dropdown-open.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-source-driven-v0.7.7-dialog-open.png`

## 下一步

优先改进 Select 和 Dropdown Menu 的 SVG 选中标记、Dialog 动画与大小适配、Dropdown Menu 多层菜单与焦点；再覆盖剩余官网目录控件。最终必须在同 DPI / 浏览器缩放 / 主题下验收视觉差异，不能用“目录打开成功”替代官网视觉一致性。
