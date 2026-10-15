# Yanzi UI 0.6.4 — 自绘 Radio、Switch、Alert Dialog、上拉菜单

日期：2026-10-08

## 修复范围

目标是对齐 shadcn/ui 暗色首页的选择控件、弹窗与 Button Group 上拉菜单。不使用原生 Windows RadioButton 作为新组件，不再用系统工具窗口模仿网站 Dialog，不在总览里用原生 ContextMenu 或字体字符“⌃”。

### Radio / RadioGroup（0.6.3 起，本次一并保存）

- 新增 YanziRadio（继承 Control，而不是 RadioButton）和 YanziRadioGroup。
- 视觉为 16×16 DIP 圆形外圈、选中 8×8 DIP 实心点，24×24 DIP 可点击区域；支持 Hover、Disabled、Focus，深浅主题响应。
- Group 提供 SelectedValue 双向依赖属性、单选互斥、重复值拒绝、程序化 IsChecked 互斥、键盘 Left/Right/Up/Down/Home/End、roving Tab、无障碍 Selection 和 SelectionItem pattern。
- 总览同时放未选中 Radio、已选中 Radio、自绘 Checkbox 和 Switch；“选择”和“表单控件”页不再实例化原生 RadioButton。
- 旧 Yanzi.Radio 保持兼容；新样式键 Yanzi.Radio.Custom、Yanzi.CheckBox.Preview。

### Switch 修复

- 新增 opt-in Yanzi.Switch.Shadcn，旧 Yanzi.Toggle 保留。
- 轨道 36×20 DIP；滑块 16×16 DIP，Canvas.Left 在未选中 2 和选中 18 DIP 之间切换。
- 选中轨道使用 Yanzi.Color.Primary，滑块使用 Yanzi.Color.PrimaryForeground；关闭轨道使用 Yanzi.Color.Input。
- 避免“开启白轨道、白滑块”的对比失败；保留 Checkbox 键盘和无障碍语义。

### Alert Dialog 修复

- YanziDialog.Confirm 保持旧调用接口，内部采用 YanziAlertDialog。
- WindowStyle=None + AllowsTransparency=true，不显示原生 Windows 工具窗口标题栏；覆盖 owner 的半透明遮罩，中央是 440 DIP 宽、12 DIP 圆角、1 DIP 边框的卡片。
- 标题、描述、取消、确认布局按 shadcn 结构绘制；支持真正的 owner-modal、Esc/取消及 Enter/确认。
- 专项测试实际打开 ShowDialog，分别触发取消和确认，验证 bool 结果。

### 上拉菜单及图标

- 新增 YanziDropdownMenu（WPF Popup，不是 ContextMenu），主动作与右侧箭头保留 Button Group 双段结构。
- Popup 优先在触发按钮上方展开，右边缘对齐，空间不足时允许下方回退；独立深色圆角卡、操作标题、分隔线、hover 和选择项。
- 菜单的“复制 / 查看详情 / 设置”可点击，执行后关闭。
- 新增 YanziIcons.ChevronUp：24×24 几何坐标、16 DIP Viewbox、路径 M6,15 L12,9 L18,15，圆端、2 DIP 描边；上下居中，不再受字体基线影响。
- 主内容按钮和菜单触发按钮有独立操作。

## 验收

- UI Gallery v0.6.4 Release：0 错误、0 警告。
- Yanzi.UI.Verification：212 项通过，包含 Radio 互斥、无障碍、键盘路由、Switch 两态颜色和位置、菜单结构及回调、Alert Dialog 模态取消与确认。
- Gallery UI Automation：102 项通过、覆盖 15 页；菜单在触发按钮上方出现且包含可触发的操作项。
- 隔离 Runtime 验收：18/18，宿主已有 14 条编译警告、0 错误。生产 Runtime 未替换。
- 预览已安装至桌面快捷方式“燕子 UI 组件评估中心”和扩展“组件评估”，默认总览。
- 实机截图：F:\Desktop\Yanzi-UI-视觉对照\yanzi-switch-dialog-menu-v0.6.4-menu-open.png
- 模态弹窗示例：F:\Desktop\Yanzi-UI-视觉对照\yanzi-dialog-v0.6.4-check.png

提示：本次只对齐总览中的新版菜单与弹窗，以及选择相关的 WPF 预览样式。仓库其余小程序原有的原生 ContextMenu 和旧 Switch 样式保留，未强制更改，避免破坏兼容性。字体与子像素渲染仍可能与 Chromium 有细微差异。
