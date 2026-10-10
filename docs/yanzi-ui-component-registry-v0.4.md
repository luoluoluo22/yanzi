# Yanzi UI 0.4.0：shadcn 对齐进度与使用边界

更新日期：2026-10-08。代码入口：src/Yanzi.UI.Wpf。桌面评估器：src/Yanzi.UI.Gallery。

## 这次真正完成了什么

1. **总览页**从统计报表改成 shadcn 首页布局思路的交互式组件墙：Hero / Buttons / Badges / Input / Textarea / Switch / 状态数据卡 / 侧栏 / 表单 / 聊天。
2. **15 个评估页面**：总览、按钮、输入、选择、列表、反馈、设计令牌、Badge、基础组件、表单控件、导航与布局、数据展示、弹层与反馈、全部组件索引、评价与记录。
3. **公共 WPF 样式**：Button 六变体、Badge 六变体及图标加载、TextBox/Textarea、Password、ComboBox/Select、Tabs、TabItem、Slider、Radio、Calendar、DatePicker、Progress、Tooltip、DataGrid、Menubar、ToggleButton、Switch、List 等。
4. **可复用 WPF 组件类**：YanziBadge / YanziLoadingRing / YanziOtpInput / YanziPagination / YanziCarousel / YanziAccordion / YanziInputGroup / YanziButtonGroup / YanziBarChart / YanziCommandPalette / YanziItem / YanziSidebar / YanziMessageScroller / YanziQuestionnaire / YanziToggleGroup。
5. **可复用辅助 API**：YanziPrimitives（Alert、Avatar、Separator、Kbd、Skeleton、Empty、Field），YanziLayoutPrimitives（AspectRatio、Breadcrumb、Collapsible、ScrollArea、Resizable），YanziContentPrimitives（Bubble、Marker、Attachment、Direction），YanziPopover、YanziHoverCard、YanziSheet、YanziDialog、YanziToast。
6. **组件索引**：YanziComponentRegistry.Components；当前对应官方目录的 64 个条目均存在一个可调用的 WPF 原生实现、样式或公共工厂 API。

## “可复用”不等于完整 shadcn 功能等价

本轮是 **WPF 原生适配基线**。不少功能复用了 WPF 原生实现，这会使键盘、IME 和系统焦点行为相对稳定，但不能宣称完全复制官方组件的动画、响应式、无障碍行为、全部属性或像素效果。

尤其需要进一步专项验证：

- Combobox：目前用支持编辑的 WPF ComboBox；尚没有官方级多选、复杂搜索和键盘结果导航。
- Chart：YanziBarChart 是小数据柱状图，不是覆盖交互 Tooltip、各类图表和指标轴的图表系统。
- Message Scroller：最多渲染 MaxVisible 条气泡，调用方必须自行保留历史数据；不是无限滚动的消息数据库。
- Drawer/Sheet：WPF owner-bound 侧边窗口，尚不是带网页式平移动画的真正无边框抽屉。
- Navigation/Menu：沿用原生 Menu/ContextMenu 的可操作行为，复杂子菜单与视觉细节仍需专项打磨。
- DatePicker/Calendar/DataGrid：主题采用 WPF 资源样式，系统下拉和内部模板仍可能与 shadcn 的弹层不完全相同。
- Accordion / Toggle Group / Questionnaire：具备公共 API 和核心状态行为，但需要无障碍、RTL、125%-200% DPI 和边界值人工回归。

**不得**把组件索引 64/64 解释成“已经通过全部官方示例验收”或“每个组件都 1:1 复刻”。

## API 复用示例

在使用新版宿主和 Runtime（已包含 Yanzi.UI.Wpf.dll）的 WPF UI 线程中：

    YanziUi.ApplyTo(window, YanziTheme.Dark);
    var button = YanziUi.WithStyle(new Button { Content = "保存" },
        YanziUi.Styles.DefaultButton);
    var alert = YanziPrimitives.Alert("保存成功", "这是一条语义化提示");
    var list = new YanziCarousel();
    list.Add(new TextBlock { Text = "Card" });
    var pager = new YanziPagination { PageCount = 5 };
    pager.PageChanged += (_, page) => LoadData(page);
    var input = new YanziInputGroup("https://", ".com");
    var otp = new YanziOtpInput(6);
    var menu = new YanziCommandPalette();
    menu.Add("打开日历", () => OpenCalendar());

组件不能自己上传文件、发送消息或执行危险操作，必须由宿主经过权限检查再传递用户动作。

## 构建、安装和验收

    dotnet run --project src/Yanzi.UI.Verification/Yanzi.UI.Verification.csproj -c Release
    dotnet build src/Yanzi.UI.Gallery/Yanzi.UI.Gallery.csproj -c Release
    powershell -ExecutionPolicy Bypass -File scripts/install-yanzi-ui-gallery.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-gallery.ps1

该安装脚本会创建独立的不可变预览目录、桌面快捷方式和“组件评估”小程序入口；不会停止或替换正式版 Runtime。

正式 Runtime 需要另外单独执行版本化升级与完整回归，不能把“Gallery 通过”当成“所有旧小程序完成迁移”。仓库还有其他正在进行的工作，必须保持隔离并避免覆盖未提交改动。

## 后续专项目标

以官方首页截图为视觉基准，进一步按组件拍摄深/浅主题截图比对：字体、12/14px 字号、密度、间距、边框、阴影、focus ring、Hover、Disabled、Keyboard Navigation、高 DPI、Popup/Portal、中文输入法和内容溢出。先调整每一类组件，再推进剪贴板、日历、截图 OCR 三个试点，不允许一次性强制替换全部旧窗口。
