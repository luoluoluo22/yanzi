# Yanzi UI v0.7.5 · 官网截图八组件视觉精修

日期：2026-10-08
输入基准：用户提供的 shadcn/ui Base UI 官网对照截图（Accordion、Alert、Attachment、Avatar、Button Group、Calendar、Card、Combobox）。

## 目标变化

从“有公共 API + 最小演示”升级到“以官网最具代表性的业务场景来展示真正的 WPF 组件”。主示例置于每一页最上方，单独的深色圆角预览画布最小高度 388 DIP、内边距 24–27 DIP、居中；API 说明及六项验收记录随后呈现。64 个目录和本地审查数据都保留。

## 对照截图逐项修复

| 组件 | 修复前 | 本轮修复 |
|---|---|---|
| Accordion | 两条带方形边框的最小 Expander | 公共 `YanziAccordion` 重绘为分隔线列表、46 DIP 行高、矢量 Chevron、单开模式；官网语义的三条配送/退货/客服问答，默认展开第二条 |
| Alert | 单个小型 Heads up 框 | 公共 `YanziPrimitives.Alert` 增加图标列、标题、正文与主题色；展示成功付款和功能更新两张警告卡片 |
| Attachment | 只有 proposal.pdf 小标签 | 三张办公场景图片缩略图、上传中 PDF 的进度条、普通代码文件、文件元信息和移除按钮；修复安装脚本复制嵌套 Assets 的缺陷 |
| Avatar | 只有 YZ fallback | 单人图像头像、在线状态绿点、三人头像重叠组合和 +3 溢出标签 |
| Button Group | “前一项/后一项”两按钮 | 独立返回按钮 + Archive/Report 分段 + Snooze/More 分段，缩减分段内距、保持相邻边界 |
| Calendar | 很小的原生 WPF Calendar | 新增公共 `YanziCalendarMonth`：居中、月份导航、周标题、完整月网格、当日与选中态、日期回调；示例预设 2026-10-08 |
| Card | 仅一句“Card / 标题、内容与操作区的间距” | 完整登录卡片，标题/Sign Up、描述、Email/Password、Forgot password、主登录和 Google 次按钮；不发送凭证 |
| Combobox | 同页显示大号“清空选择”按钮干扰主视觉 | 居中的 218 DIP 可搜索组合框，官网文案和五个选项，保留输入过滤、选择、键盘操作；去掉对照主舞台中的大号清空按钮 |

## 资源与部署

- 示例办公缩略图：`src/Yanzi.UI.Gallery/Assets/office-{1,2,3}.jpg`，人物头像图：`Assets/person-{1,2,3}.jpg`（示例图片取自 Unsplash，并不代表用户真实附件和联系人）。
- `Yanzi.UI.Gallery.csproj` 将 6 张资源复制到编译输出；`install-yanzi-ui-gallery.ps1` 添加 `-Recurse` 并且逐张验证最终安装文件，防止发布时图片静默消失。
- 评估中心的交互都是本地演示，不进行支付、上传、远程登录、文件删除。

## 验收

- WPF Gallery v0.7.5 Release：0 错误、0 警告。
- 共享设计组件验证：**257 项通过**，其中新增 Accordion 三条默认展开 / 单开切换、月份选择/导航及公共 Alert 图标结构测试。
- Windows UI Automation：8/8 个精修组件能正常进入，验证主预览在官方信息之前且真实功能元素存在。
- 官网目录：64/64 项导航成功、无预览空白、参考链接与核对入口保留。
- 原有 15 页：102 项 UI 自动化断言通过。
- 核对结果：原用户记录保留，可保存六维核对状态。
- Runtime 隔离兼容：18/18 项通过；构建零错误但历史遗留的宿主项目仍有 14 条警告；正式运行中 Runtime 没有替换。

## 实机截图

- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-showcase-card-v0.7.5.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-showcase-calendar-v0.7.5.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-showcase-attachment-v0.7.5-fixed.png`

注意：本轮属于“结构和代表性示例对齐”，并没有针对所有窗口 DPI/字号和浏览器渲染作像素级差值量测，因此六项正式核对仍按用户保存状态，不自动标记为通过。图片示例与官网图片不相同，但图文比例、排列、加载态和文件行状态按参考结构制作。下一轮若需要更精细的像素一致性，需固定 WPF/浏览器 DPI、缩放和主题，同框裁剪后做逐项尺寸、颜色与文本基线差异记录。

## 安全与边界

本次仅改动独立 Gallery 与公共 Yanzi.UI.Wpf，包括新增日历与 Accordion 样式逻辑；不强制更换所有既有小程序的旧 Calendar/Accordion UI，也不改动用户私人文件及应用数据。保留现场其他未提交内容，只做本地提交。
