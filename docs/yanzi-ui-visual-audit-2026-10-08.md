# Yanzi UI 0.4.3 · shadcn 首页对照整改（第 2、3、4 项）

日期：2026-10-08

## 本轮范围

本轮只处理此前截图红框中的 **2 导航宽度、3 按钮/输入框、4 图表信息层次**；没有改变正式版燕子 Runtime 和旧小程序的界面。

### 2. 导航空间

- 总览页默认收起原先固定占用 226 DIP 的目录，在顶栏保留“☰ 目录”入口。
- 展开时的宽度为 192 DIP，可以通过按钮再次收起；手动选择会跨页面保留，其他组件页正常支持目录导航。
- 组件墙以 ScrollViewer 实际宽度为基准，大于等于 880 DIP 为三列，较窄时改为两列。这样比固定三列更不容易挤压文本或按钮。

### 3. 输入框 / Button

- 新增可复用的 `Yanzi.Input.Soft` 和 `Yanzi.Textarea.Soft`：语义化 Muted 背景、轻边框、输入焦点环、深浅主题兼容。
- 总览示例按钮高度调整至 34 DIP、字体为 12，水平 padding 调整，保留已有公共 Button 六变体。
- 搜索展示框采用真实空值+独立“Name”占位文字，用户编辑后占位消失；末端放搜索标识。不再把“Name”写作搜索框实际输入内容。
- 原有 `Yanzi.Input` 等接口保持不变，避免全量影响其他小程序。

### 4. 数据图表

- `YanziBarChart` 使用数据点实际数值确定柱高；新增 `SelectedIndex`、`MaxBarHeight`、`PointCount`，并保留原来的异常语义验证。
- Theme Tokens 增加 `Yanzi.Color.ChartBar` 深浅色版本，与 `Primary` 强调柱进行区分。
- 总览的 Contribution History 支持 Dec—Apr 月份标签和柱子 Tooltip 数值，并在下方附 Upcoming / May 2024 / Scheduled 状态区域与 View Full Report 按钮。
- 数据是 **纯演示用样例**，不代表真实业务统计。轻量柱图仍不等同于完整的专业图表引擎。

## 复测结果

- 独立组件和 Gallery Release 构建：0 错误，0 警告。
- 公共设计系统验证：118/118。
- Windows UI Automation：76/76（含目录展开/收起、15 页面、主题切换和组件展示）。
- 隔离 Runtime：18/18；宿主已有编译警告 14 条，0 错误。
- 本机已安装独立“组件评估”预览 0.4.3，并更新桌面快捷方式；**未激活生产 Runtime**。

## 对照截图

本机位置：`F:\Desktop\Yanzi-UI-视觉对照\`

- `yanzi-live-desktop.png`：旧版桌面参考截图。
- `yanzi-overview-0.4.3-final.png`：本轮完成后的桌面实拍。
- `shadcn-live-desktop.png`：同一台电脑 Edge 的官网参考截图。

下一轮仍需要实际验证 125% / 150% DPI、IME、Tab 焦点、表格滚动和窄窗口下二列布局，并进行更严格的统一视口比对。
