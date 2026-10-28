# Yanzi UI 0.5.0 · 胶囊圆角对齐

日期：2026-10-08

## 需求及设计决策

针对截图中 shadcn/ui 首页展示卡的按钮从“小圆角矩形”改为“完全胶囊圆角”。此次**不是全局调大 BorderRadius**，否则会改变老小程序中保存、删除、确认等业务按钮的现有手感和排版。

只在共享 WPF 库中增量新增可调用的展示变体，并在 Gallery 首页采用；原 `Yanzi.Button.Default/Secondary/Outline` 及旧别名维持现状。

## 公共 API

通过 `YanziUi.ApplyTo(window)` 装载公共资源后：

| 类别 | 资源键 / API | 作用 |
| --- | --- | --- |
| Pill | `YanziUi.Styles.PillDefaultButton` | 白底/黑字胶囊主按钮 |
| Pill | `PillSecondaryButton`, `PillOutlineButton` | 胶囊次按钮、细框胶囊按钮 |
| Pill | `PillGhostButton`, `PillDestructiveButton` | 其他语义选项 |
| Chip | `ChipDefaultButton`, `ChipSecondaryButton`, `ChipOutlineButton` | 22–24 DIP 紧凑胶囊 |
| Segmented | `YanziSegmentedButtonGroup` | Add(label, callback)，左右端圆角、中间分隔，独立按钮动作 |
| 原生旧样式 | `DefaultButton`, `OutlineButton`, `DangerButton` 等 | 保留不变，不强制迁移 |

胶囊的 CornerRadius 使用 999（WPF 对实际大小进行裁切）。焦点边框也是全胶囊；保留悬停、点击、禁用语义。分段控件左右端独立圆角，键盘焦点可单独到达，每段有各自操作回调。

## Gallery

总览顶部 Default / Secondary / Outline，以及底部 Alert Dialog 使用 Pill 样式；原独立 Dropdown 替换为更接近官方截图的“Button Group | ▴”分段组合，展开菜单仍可操作。按钮专页另提供 Pill / Chip / Segmented 三种类别的实时交互演示，供以后扩展复用。

## 自动测试、截图

- 单独 Release 构建：0 错误、0 警告。
- `Yanzi.UI.Verification`：136 项通过，新增圆角模板、两端圆角、两段点击、浅色主题等断言。
- `verify-yanzi-ui-gallery.ps1`：81 项通过，覆盖 15 页及新版 Pill/Chip/Segmented 元素。
- 隔离 Runtime 回归：18 项通过，构建只出现宿主原有 14 个警告，未激活线上 Runtime。
- 新版 Gallery 已安装为 `yanzi-ui-gallery` 0.5.0，桌面快捷方式已更新。

本机实拍与对照存放于：

`F:\Desktop\Yanzi-UI-视觉对照\Yanzi-UI-胶囊按钮-修改前后对比.png`

截图裁剪分别来自同一 Windows 虚拟桌面的 0.4.3 和 0.5.0，实际屏幕坐标相同。对比只针对这块组件卡，不包含网页字号、字体、DPI 等完整像素对照。

## 后续视觉验收

建议继续比较按钮文字大小、箭头图标、组间间隔、Hover、Disabled、125/150/200% DPI。WPF 字体和 CSS 的子像素渲染可能不同，不应以仅字体边缘不同判定功能异常。

**生产 Runtime 本轮不替换。**
