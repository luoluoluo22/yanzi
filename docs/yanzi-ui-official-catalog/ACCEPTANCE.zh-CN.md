# Yanzi UI 0.7.1 · 官网 64 组件目录对齐验收

基准网址：https://ui.shadcn.com/docs/components

日期：2026-10-08

## 本轮已实现

- 官网完整列表严格对照：64 项，排列顺序与 /docs/components/base/ 对应的 64 个真实文档完全一致；已通过全部页面 HTTP 200 验证。
- Gallery 左侧把原有的 15 个专题入口收纳到“展开专题演示”，默认显示总览、全部组件索引、评价与记录和 64 项官方顺序组件。可按名称过滤，不移除任何官方项。
- 64 个组件分别有独立页面：真实官网入口、本地公共 API、需要检查的具体要点、独立预览（如已具备）、六项核对状态和备注保存。
- 六项核对维度：布局/尺寸、字体/图标、色彩/边框、Hover/Focus/Disabled、鼠标/键盘/交互、深浅主题/缩放。
- 结果从“待核对”开始，用户可标记“通过”或“不一致”，保存在 %LOCALAPPDATA%\OpenQuickHost\UiReview\ComponentAudit\ 下。未核对项不能冒充通过。
- 每页都有“上一项/下一项”，支持从 Accordion 一直按官网顺序核对到 Typography。
- 首批 16 个官方组件（Accordion → Checkbox）全部有可操作的独立演示；总计现有 **48/64** 独立演示。

## 未完成的 16 个独立演示

Combobox、Command、Context Menu、Data Table、Date Picker、Dialog、Drawer、Hover Card、Input Group、Menubar、Message Scroller、Navigation Menu、Popover、Questionnaire、Sheet、Sidebar。

这些条目**没有遗漏**：都拥有官方链接、公共 API 索引、独立核对页面以及“去旧专题查看”入口。尚未具备专属演示或未完成全功能对标，不能被认定为和 shadcn 等价。

**当前经过正式视觉与完整交互核验的数量为 0/64**（不是失败数量，而是尚未完成相同缩放截图与状态对照）。软件构建及部分功能单元测试通过，不能替代逐组件的视觉验收。

## 工程验收

- Gallery 0.7.1 Release 构建：0 错误，0 警告。
- 组件公共库回归：215 项检查通过（含官网 64 名称、顺序、官方 Base URL 的断言）。
- 官网抓取：64/64 组件文档可访问，生成 official-reference-snapshot.json 与 README.md 全量表格。
- Windows UI Automation：64/64 组件页分别成功导航，独立参考入口、保存入口与六项核对控件均可用。
- 原有专题回归：15 页、102 项桌面检查通过。
- 持久化回归：Avatar 备注和六项待核对状态存储成功；下一项能进入 Badge；测试后恢复此前用户的本地审查数据。
- 隔离 Runtime 兼容：18/18 通过；构建无错误，宿主存在原有 14 条警告。**正式 Runtime 未替换。**

## 再次审查与维护

运行下列命令以重查官网内容或桌面控件：

- python scripts/audit-shadcn-components.py
- powershell -File scripts/verify-yanzi-ui-official-catalog.ps1
- powershell -File scripts/verify-yanzi-ui-component-audit.ps1
- powershell -File scripts/verify-yanzi-ui-gallery.ps1

官网新增、删减或重新排序组件时，审计脚本会报告具体差异并失败，不会悄悄沿用过期的 64 项清单。

现场截图：F:\Desktop\Yanzi-UI-视觉对照\yanzi-official-catalog-v0.7.1-accordion.png

本轮只修改公共 UI 库与独立组件评估中心；保留其他仓库开发改动，不推送远程。
