# Yanzi UI · shadcn/ui 视觉对齐路线图

版本：0.2.0（2026-10-08）  
范围：Windows WPF 公共库，非 React 或 Tailwind 运行时。  
参照：<https://ui.shadcn.com/docs/components/base/badge>、<https://ui.shadcn.com/docs/components/base/button>、<https://ui.shadcn.com/docs/theming>。

## 已落地

1. 中性（Neutral）主题：Background/Foreground、Card/Popover、Primary/Secondary、Muted/Accent、Destructive、Border/Input/Ring、Sidebar、Radius，Light/Dark 两套语义资源。
2. 完整保留旧版 `Yanzi.Brush.*`、`Yanzi.Button.Primary` 和 `Yanzi.Button.Danger` 键。公共库通过窗口级显式加载，不改生产应用级资源。注意：兼容的是 API 和资源键，不承诺旧版颜色数值保持不变。
3. Button：Default、Secondary、Outline、Ghost、Destructive、Link 六种 WPF 样式及 hover / pressed / keyboard-focus / disabled 状态；兼容旧别名。
4. Badge：独立 `YanziBadge`，Default、Secondary、Destructive、Outline、Ghost、Link 六种变体，LeadingIcon / TrailingIcon / IsLoading；内容可在运行时改变，无需重新创建控件。Badge 的 Link 变体目前**仅为视觉样式**，没有自动导航。若需要真实导航，使用 Link/Button，并提供可访问名称。
5. Input：保留 WPF 的原生文字编辑、中文 IME、复制/撤销行为，统一 36px 高度、语义化边框与焦点环。
6. Card：沿用 `Yanzi.Card`，基于新的 Card/Border/radius token；列表、菜单、Toast、Dialog 继续使用兼容风格。
7. Gallery：九个分类页面，增加 Badge 全变体预览、图标、Spinner、运行时调节，并支持深浅主题切换和本地评价。

## 对齐清单与阶段

| 分类 | shadcn 对应 | 当前状态 | 下一轮必要工作 |
| --- | --- | --- | --- |
| 主题 | Neutral、light/dark、radius、ring | 初步完成 | 颜色实测标定，屏幕对照差异 |
| Badge | Badge variants/icons/spinner | 首版完成 | 自定义语义色、尺寸、RTL 可访问性 |
| Button | Default/Outline/Secondary/Ghost/Destructive/Link | 首版完成 | icon、xs/sm/lg、ButtonGroup、Loading |
| Input | Input | 首版视觉调整 | placeholder、invalid、read-only、InputGroup |
| Card | Card | 首版视觉调整 | Header/Description/Content/Footer API |
| Dialog / AlertDialog | Dialog / Alert Dialog | 旧兼容实现 | 新的边框、背景、Focus trap、键盘语义 |
| Menu | Dropdown / Context Menu | 旧兼容实现 | 完整 item / separator / shortcut / submenu 模板 |
| Tabs | Tabs | 未实现 | WPF TabControl 样式与可访问键盘行为 |
| Toast | Toast | 旧兼容实现 | shadcn 配色、布局、队列与 dismiss |
| Checkbox / Switch / Radio | Checkbox / Switch / Radio Group | 旧兼容实现 | 焦点环与语义状态 |
| Select / Combobox | Select / Combobox | 未实现 | Popup 样式、过滤、焦点与键盘导航 |
| Table / DataTable | Table / Data Table | 未实现 | 虚拟化、大数据性能、排序 |
| Tooltip / Popover | Tooltip / Popover | 未实现 | 定位、遮挡、键盘、无障碍 |
| Accordion / Collapsible | Accordion / Collapsible | 未实现 | 动画与展开状态 |
| Sidebar / Navigation | Sidebar / Navigation Menu | Gallery 初步使用 | 公共布局模板、响应尺寸 |
| Calendar / Date picker | Calendar / Date Picker | 未实现 | 周起始、语言和本地化行为 |
| Skeleton / Progress | Skeleton / Progress / Spinner | 部分具备 | shimmer 与动效节奏 |
| Form / Field / Label | Field / Label | 未实现 | 表单校验与错误提示统一 |
| Chart / Avatar / Pagination | 同名组件 | 未实现 | 根据具体业务复用成熟原生能力 |

## 交互约束

- 不因追求网页像素级效果破坏 WPF 键盘操作、中文输入、DPI 或用户已有数据。
- 鼠标悬停与键盘焦点不能互相替代；focus ring 独立处理。
- Badge 不伪装为可点击控件；仅在真实操作控件上处理导航、事件和权限。
- 每批在组件评估中心展示所有变体和主题，采用 Windows UI Automation、动态 C# 小程序引用验证，保证组件能在真实 Runtime 中加载。
- 每次仅升级可回滚的组件库和独立评估中心；完整宿主 Runtime 的替换，需要另外完成生产回归验证。

## 验收命令

    dotnet run --project src/Yanzi.UI.Verification/Yanzi.UI.Verification.csproj -c Release
    dotnet build src/Yanzi.UI.Gallery/Yanzi.UI.Gallery.csproj -c Release
    powershell -ExecutionPolicy Bypass -File scripts/install-yanzi-ui-gallery.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-gallery.ps1
    dotnet build src/Yanzi.Runtime/Yanzi.Runtime.csproj -c Release -p:SkipStopRunningApp=true
    dotnet run --project src/Yanzi.RuntimeVerification/Yanzi.RuntimeVerification.csproj -c Release -- src/Yanzi.Runtime/bin/Release/net9.0-windows/Yanzi.Runtime.exe

暂不重新启动或替换生产 Runtime；原仓库大量其它功能在并行开发，构建成功不代表可以部署。
