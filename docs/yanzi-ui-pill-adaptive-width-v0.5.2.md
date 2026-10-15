# Yanzi UI 0.5.2 — 胶囊按钮按内容自适应宽度

2026-10-08

## 修复原因

上一版为了保留胶囊的平行直线段，把 Pill、Chip 的 MinWidth 固定为 96/76 DIP，另在评估总览和按钮专页中重复设定相同值。这导致短文字（如 Outline）被不必要地撑宽，按钮看起来几乎等宽。

## 规则

- Pill：Height 32 DIP、Chrome CornerRadius 14 DIP、左右 Padding 各 18 DIP、MinWidth 56 DIP。
- Chip：Height 24 DIP、Chrome CornerRadius 10 DIP、左右 Padding 各 13 DIP、MinWidth 42 DIP。
- Segmented：每段根据实际标题的宽度加 Padding，自适应；首段 MinWidth 54 DIP，其余 36 DIP，不再强行设为 112/48。
- WPF ContentPresenter 显式使用 Content/ContentTemplate 模板绑定。窗口布局发生改变后，控件应自动重新测量。
- 通用业务按钮样式不受影响；深浅主题与已有资源键保持兼容。

## 实测

Windows UI Automation 读取的总览按钮外框（屏幕像素，当前显示缩放）：

- Default ↗：95 × 32
- Secondary：97 × 32
- Outline：83 × 32

另使用带真实窗口、Dispatcher 和 StackPanel 的单元测试，确认：

- Outline 约 80 DIP，Secondary 约 97 DIP；
- 文本改长会扩展宽度，再改短会恢复；
- Chip 的不同文字长度也会产生不同宽度；
- Pill / Chip 保持圆角半径、直线段和固定高度；
- 分段按钮可独立点击。

## 回归结果

- Gallery Release：0 错误、0 警告。
- 公共组件：146/146。
- Windows UI Automation：81/81，15 个页面。
- 隔离 Runtime：18/18，0 错误、原有 14 条宿主构建警告。
- 预览 0.5.2 已更新：本地组件评估小程序 + 桌面快捷方式；**未替换生产 Runtime**。

截图：F:\Desktop\Yanzi-UI-视觉对照\yanzi-pill-auto-width-0.5.2.png
