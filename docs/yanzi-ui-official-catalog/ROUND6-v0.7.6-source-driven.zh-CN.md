# Yanzi UI 0.7.6：以官方源码 + 实际渲染为基准

日期：2026-10-08

## 基准及边界

上游快照位于 `docs/yanzi-ui-official-catalog/reference-package/`，来自 shadcn/ui 官方 Base UI / base-nova Registry JSON、官方 examples 和 CSS，GitHub commit 为 `0132174664c07d41262fb51012d0cc782e458e6c`；Registry 为下载时的在线版本快照，与该 Git commit 可能存在部署时差。

本轮第一批覆盖 Accordion、Alert、Card、Calendar、Combobox 五项真实公共控件，同时不改变 Attachment/Avatar/Button Group 等其他代表性示例。参考用户提供的官网页面最终渲染截图核对主视觉；因本机浏览器独立 headless 截图返回“Multiple targets are not supported in headless mode”，**并未生成同一窗口、同一 DPI 的新官网逐像素对比结果**，因此不能把本轮标记为“视觉验收全部通过”。

## 可追溯设计契约

- 运行 `python scripts/extract-shadcn-wpf-contract.py` 提取 5 项 Registry 源码 slot、核心样式类和组件示例路径，写入 `docs/yanzi-ui-official-catalog/wpf-source-contract.json`。
- `--verify` 离线确认设计契约文件未漂移，并从两套 WPF XAML 主题读取圆角 Key 进行严格数值检查。
- CSS `--radius=0.625rem`，在网页根字号 16px、100% 缩放的假设下派生：
  - sm = 6 CSS px，md = 8 CSS px，lg = 10 CSS px，xl = 14 CSS px。
  - WPF 两套主题的 `Yanzi.Radius.Sm/Md/Lg/Xl/Control/Card` 已统一为 6/8/10/14/8/14 DIP。
  - CSS px 与 WPF DIP 只在相应的 DPI/缩放条件下可对照，不能无条件视为实际物理像素一致。

## 组件落地及差异

| 组件 | 源码约束/问题 | 本轮修改 | 仍待完善 |
|---|---|---|---|
| Accordion | `not-last:border-b`、`py-2.5`、`text-sm`；末项不应有底边线 | 公共控件最后一项不生成分隔线，垂直内边距 10、字号 14、保留单开状态与上下 chevron | 内容高度动画、hover underline、焦点 ring 和 ARIA 语义 |
| Alert | `rounded-lg`、`px-2.5 py-2 text-sm` | 内边距 10/8、圆角绑定 md=8、图标和标题/描述字号 14 | 逐状态 hover/焦点、链接富文本、颜色透明度一致性 |
| Card | 官方 `card-header` / `card-content` / `card-footer`、`rounded-xl`、`--card-spacing:16` | 新公共 `YanziCard` 三段插槽，Header/Body/Footer 16 DIP 间距、共享圆角 14；Gallery 完整登录示例直接使用组件，不再手写孤立边框 | footer muted 透明度、表单可访问性、完整宽度适配 |
| Calendar | `--cell-size:--spacing(7)`=28 CSS px、月份行数随日期变化 | 自绘月份视图统一 28 DIP 日期格，十月 2026 五周、八月 2026 六周，选中/月份导航继续工作 | 月份/年份下拉控件、范围选择、RTL、键盘方向导航 |
| Combobox | 触发区域和弹出列表、输入交互需要保持一致 | 箭头成为真实按钮；Popup 宽度绑定触发器 `ActualWidth`、字号 14，避免之前固定 285 DIP 导致弹层错位 | 分组、多选 chips、完整 focus/disabled、边界避让 |

现有非本轮组件依然保留相应官网对照入口。UI Automation 可证明可访问和可操作，**不等同于逐像素视觉一致**。

## 验证与实机截图

- Gallery 0.7.6 Release：0 错误、0 警告。
- WPF 公共组件验证：`263/263`（新增 6 项源规则与 YanziCard 插槽行为测试）。
- Gallery 官网组件独立页：64/64 可导航；8 个已精修示例的预览先于 API 说明。
- Gallery 原有 15 专题：102 项 UI Automation 通过。
- 审查记录：保存、相邻跳转与恢复原有记录通过。
- 隔离 Runtime：18/18 通过；生产宿主构建 0 错误，原有 14 个编译警告；正式 Runtime 未替换。
- 下载参考包：64 目录条目、61 Registry 包、60 示例、125 项 SHA256 校验通过。

实机截图：
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-source-driven-v0.7.6-card-final.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-source-driven-v0.7.6-combobox-open.png`

## 下一批开发优先级

1. 继续把源规则与输出截图联动：在相同主题、DPI、字体和浏览器缩放下截取**同一个组件矩形**，记录基线、字号、圆角、间距差，不能只凭全屏截图主观判定。
2. 将 Calendar 范围选择/键盘行为、Combobox 分组/状态等官网更完整的能力纳入公共 API；之后再推进其余组件。
3. 每次修改公共控件先跑 `extract-shadcn-wpf-contract.py --verify`，再做公共库构建、UIAutomation 和人工视觉签收。未人工校对项维持“待核对”。

本轮只更改公共 UI 源码、独立 Gallery、校验脚本和对照文档，不修改其他小程序和用户数据，不推送远端，不替换生产 Runtime。
