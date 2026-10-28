# Yanzi UI 已批注组件验收队列（2026-10-10）

数据来源：本机 `%LOCALAPPDATA%\OpenQuickHost\UiReview\ComponentAudit\*.json`。原始记录不迁移、不覆盖。

规则：`参考网站/参考网页` 表示要严格参照对应官网组件的结构、变体、尺寸、深浅主题、键盘和鼠标行为逐项复现，不能仅加入表面相似的展示。改动通过编译或自动化测试，不代表用户人工判定通过。

手工状态只有 **通过 / 不通过** 两个操作；没有选择时仍持久化为 `待核对`，兼容旧 `不一致` 记录。

| 组件 | 任务类型 | 用户原始批注 | 参考官网 | 当前人工状态 |
|---|---|---|---|---|
| Combobox | 具体问题修复 | 悬浮时还是原生的组件 需要修改 | [官方参考](https://ui.shadcn.com/docs/components/base/combobox) | 待人工核对 |
| Direction | 官网结构/视觉/交互全维对照 | 参考网页 | [官方参考](https://ui.shadcn.com/docs/components/base/direction) | 待人工核对 |
| Field | 具体问题修复 | 表单不完整 | [官方参考](https://ui.shadcn.com/docs/components/base/field) | 待人工核对 |
| Input Group | 官网结构/视觉/交互全维对照 | 参考网页 补充完整输入类型 | [官方参考](https://ui.shadcn.com/docs/components/base/input-group) | 待人工核对 |
| Input | 官网结构/视觉/交互全维对照 | 参考网页 补充完整不同的输入类型 | [官方参考](https://ui.shadcn.com/docs/components/base/input) | 待人工核对 |
| Label | 具体问题修复 | 需要补充完整 | [官方参考](https://ui.shadcn.com/docs/components/base/label) | 待人工核对 |
| Marker | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/marker) | 待人工核对 |
| Message Scroller | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/message-scroller) | 待人工核对 |
| Message | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/message) | 待人工核对 |
| Navigation Menu | 官网结构/视觉/交互全维对照 | 下拉箭头位置不对 参考网页 | [官方参考](https://ui.shadcn.com/docs/components/base/navigation-menu) | 待人工核对 |
| Popover | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/popover) | 待人工核对 |
| Progress | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/progress) | 待人工核对 |
| Questionnaire | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/questionnaire) | 待人工核对 |
| Skeleton | 官网结构/视觉/交互全维对照 | 参考网页 | [官方参考](https://ui.shadcn.com/docs/components/base/skeleton) | 待人工核对 |
| Toast | 官网结构/视觉/交互全维对照 | 参考网站 | [官方参考](https://ui.shadcn.com/docs/components/base/toast) | 待人工核对 |

目前已做的可验证修复：
- Combobox：替换原生 ListBoxItem 悬停/选中/焦点模板。
- Navigation Menu：修正箭头旋转原点与展开方向。
- Gallery 审核：通过/不通过快速按钮，状态点击自动持久化，备注另存。

后续每个“参考网站”任务必须把实际演示与官网按六个维度人工逐一比较，缺失的 API/交互要补到公共组件库，并在组件页补出对应可操作示例。
## 首轮修复记录（2026-10-10）

| 批注组件 | 首轮代码改动 | 仍须人工核对 |
|---|---|---|
| Combobox | 搜索选项使用自绘 hover/selected/focus 模板 | 官网过滤交互、键盘与色值 |
| Navigation Menu | 修正箭头旋转轴心及展开状态 | 动画、RTL 与不同视口 |
| Field / Label | FieldSet、关联标签、必填与错误提示、分组和行内搜索 | 官网所有 field 组合和可访问性 |
| Input / Input Group | 输入类型实例、一体化组合框、按钮、Kbd、上下附加内容 | 官网全部输入变体与尺寸 |
| Message / Message Scroller | 消息行头像/头尾、稳定 ID、流式追加、历史插入保持位置、消息跳转 | 全部 streaming/anchoring/虚拟化变体 |
| Questionnaire | 单选、多选、必填、条件问题、跳过与前后切换 | 完整问卷焦点/快捷键/条件组合 |
| Popover | Start/Center/End 位置、Esc 关闭、边界候选 | 多屏 DPI 与官网像素一致性 |
| Direction / Marker | LTR/RTL 动态预览、default/border/separator | 官网所有变体及 RTL 子组件 |
| Progress / Skeleton | 进度控制与标签、头像/卡片/文本/表单骨架 | 色彩、动画与缩放 |
| Toast | info/success/warning/error/loading、描述、关闭和操作按钮 | 多 toast 堆叠、滑动关闭、Promise 更新 |

自动检查：Gallery 与宿主源码构建成功；357 条公共组件验证、34 条新增专项验证、15 个批注页面真实窗口进入测试通过。

这些结果仅代表源码兼容与功能级回归，不代表官网像素级一致性。**用户原始 JSON 的 Results 仍为待核对，未自动写入通过。**
