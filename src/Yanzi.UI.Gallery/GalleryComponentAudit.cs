using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

/// <summary>One independently navigable comparison and durable review record per official component.</summary>
internal sealed partial class GalleryWindow
{
    internal static readonly string[] ComparisonDimensions =
        ["布局与尺寸", "字体与图标", "颜色与边框", "Hover / Focus / Disabled", "鼠标 / 键盘 / 交互", "深浅主题 / 缩放"];

    // Key features to compare, in the EXACT official components list order. No generated blanket "passed" status.
    internal static readonly string[] OfficialFocus =
    [
        "展开/收起，单开与多开、转场、键盘与焦点",
        "图标、标题、说明、Default / Destructive 样式",
        "蒙层、标题、描述、取消/确认、Esc、焦点约束",
        "16:9 / 4:3 比例，图片裁切与响应式",
        "文件预览、状态、移除和多附件排列",
        "圆形头像、图片缺失时的 fallback、尺寸",
        "Default / Secondary / Outline / Destructive，圆角与文字",
        "层级分隔符、省略、当前项与链接交互",
        "消息气泡方向、形态、间距与长文本",
        "六种变体、尺寸、字重、加载/焦点",
        "相邻按钮边框、分隔、圆角及箭头居中",
        "月份切换、日期范围、禁用日期和键盘",
        "容器阴影、Header / Content / Footer 间距",
        "滚动按钮、吸附、圆点及循环控制",
        "柱形/线图、坐标轴、提示、响应式",
        "选中勾号、混合态、焦点、标签点击",
        "折叠触发器、内容高度、动效与状态",
        "输入过滤、选中项、键盘方向与无结果",
        "检索、分组、命令执行、快捷键",
        "右键定位、菜单项与子菜单交互",
        "排序、列宽、行选择、分页和空状态",
        "日期弹窗、输入格式、范围/禁用",
        "蒙层、可滚动内容、关闭与焦点",
        "LTR / RTL 文字和布局方向切换",
        "边缘抽屉位置、拖动和关闭",
        "菜单弹层、键盘、子菜单、checkbox 项",
        "空状态图标、说明、行动按钮与留白",
        "FieldLabel、说明、错误反馈和必填",
        "悬停延时、离开关闭、定位和内容",
        "输入高度、Caret、placeholder、校验态",
        "输入框分组、按钮/图标嵌入边框",
        "单字符位、粘贴、移动焦点与错误态",
        "图文排列、辅助动作、选中状态",
        "Kbd 边框、字体与组合键视觉",
        "与输入框关联、点击聚焦、禁用",
        "标记注释、背景与状态组合",
        "顶栏菜单、弹出子项、焦点切换",
        "消息区域、不同角色的状态与动作",
        "自动滚到底部、保留位置、加载更多",
        "系统选择下拉、占位、禁用和视觉",
        "导航触发、活动项、响应式面板",
        "前后页、页号、禁用/边界状态",
        "触发定位、焦点恢复和外部点击关闭",
        "进度条尺寸、状态与百分比",
        "问题类型、选项、确认和错误反馈",
        "单组互斥、方向键、禁用与 focus ring",
        "手柄、拖拽、min/max 和持久化",
        "垂直/水平滚动条、滚动边缘与键盘",
        "选项弹层、搜索、定位、滚动状态",
        "分隔方向、厚度和语义颜色",
        "侧边抽屉、蒙层与焦点关闭",
        "左侧导航折叠、选中与悬停",
        "骨架屏尺寸、闪动动画、加载退场",
        "轨道、Thumb、键盘、步进和方向",
        "旋转、加载语义、尺寸和对齐",
        "轨道与滑块色差、位置、焦点和禁用",
        "表头、间隔、边框与横向溢出",
        "Tabs 切换、选中样式、键盘导航",
        "多行行高、光标、placeholder、滚动",
        "Toast 出入动画、状态与持续时间",
        "单态按钮、pressed 状态和键盘",
        "分组互斥/多选、间距与状态",
        "出现延迟、方向、箭头、关闭行为",
        "H1–H6、正文、引用、代码、字距与行高"
    ];

    private sealed class ComponentAuditRecord
    {
        public string Name { get; set; } = "";
        public string[] Results { get; set; } = Enumerable.Repeat("待核对", 6).ToArray();
        public string Notes { get; set; } = "";
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private static readonly HashSet<string> IsolatedPreviewNames = new(StringComparer.Ordinal)
    {
        "Accordion", "Alert", "Alert Dialog", "Aspect Ratio", "Attachment", "Avatar",
        "Badge", "Breadcrumb", "Bubble", "Button", "Button Group", "Calendar", "Card",
        "Carousel", "Chart", "Checkbox", "Collapsible", "Combobox", "Command", "Context Menu",
        "Data Table", "Date Picker", "Dialog", "Direction", "Drawer", "Dropdown Menu",
        "Empty", "Field", "Hover Card", "Input", "Input Group", "Input OTP", "Item",
        "Kbd", "Label", "Marker", "Menubar", "Message", "Message Scroller",
        "Native Select", "Navigation Menu", "Pagination", "Popover", "Progress",
        "Questionnaire", "Radio Group", "Resizable", "Scroll Area", "Select",
        "Separator", "Sheet", "Sidebar", "Skeleton", "Slider", "Spinner", "Switch",
        "Table", "Tabs", "Textarea", "Toast", "Toggle", "Toggle Group", "Tooltip", "Typography"
    };

    private string AuditPath(YanziComponentDescriptor item) =>
        Path.Combine(_reviewsDirectory, "ComponentAudit",
            item.Name.ToLowerInvariant().Replace(' ', '-') + ".json");

    private void ShowComponent(YanziComponentDescriptor item)
    {
        _activeComponent = item;
        _page = -1;
        _pageTitle.Text = item.Name;
        _pageSubtitle.Text = "官方组件独立对照 · " + (GetComponentIndex(item) + 1).ToString("00") + " / 64";
        _body.Children.Clear();
        _body.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "Yanzi.Font.Geist");
        _overviewGrid = null;
        if (!_sidebarManuallySet) SetSidebarVisible(true, false);
        UpdateNavigation();
        _statusText.Text = "正在核对：" + item.Name + " · 默认均为待核对，不代表已还原";
        RenderComponentAudit(item);
        _scroll?.ScrollToTop();
    }

    private void RenderComponentAudit(YanziComponentDescriptor item)
    {
        var reference = Card("官方参考 / " + item.Name,
            "逐项对照官网及燕子的真实 WPF 实现。名称和 API 已登记，不等于视觉/交互已验收。");
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var openOfficial = Button("打开 shadcn 官方组件页面 ↗", YanziUi.Styles.PillDefaultButton, () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(item.OfficialUrl) { UseShellExecute = true });
                Status("已打开官网：" + item.Name);
            }
            catch (Exception ex) { Status("打开官网失败：" + ex.Message); }
        });
        System.Windows.Automation.AutomationProperties.SetName(openOfficial, "打开官方 " + item.Name);
        actions.Children.Add(openOfficial);
        actions.Children.Add(Button("查看燕子原有专题演示", YanziUi.Styles.PillOutlineButton,
            () => ShowPage(item.GalleryPage)));
        reference.Children.Add(actions);
        // Sequential reviewing lets the user cover all official components in order.
        var position = GetComponentIndex(item);
        var sequence = new WrapPanel { Margin = new Thickness(0, 0, 0, 11) };
        if (position > 0)
        {
            var previous = YanziComponentRegistry.Components[position - 1];
            sequence.Children.Add(Button("← 上一项：" + previous.Name,
                YanziUi.Styles.SecondaryButton, () => ShowComponent(previous)));
        }
        if (position + 1 < YanziComponentRegistry.Components.Count)
        {
            var following = YanziComponentRegistry.Components[position + 1];
            sequence.Children.Add(Button("下一项：" + following.Name + " →",
                YanziUi.Styles.SecondaryButton, () => ShowComponent(following)));
        }
        reference.Children.Add(sequence);
        reference.Children.Add(Text("官方文档：" + item.OfficialUrl, 11, false, "Yanzi.Color.MutedForeground"));
        reference.Children.Add(Text("本地 API：" + item.Api, 12, true, "Yanzi.Color.Foreground",
            new Thickness(0, 10, 0, 4)));
        reference.Children.Add(Text("原专题：" + item.Group + "  ·  当前注册状态：" + item.Status,
            11, false, "Yanzi.Color.MutedForeground"));
        reference.Children.Add(Text("本项重点：" + OfficialFocus[GetComponentIndex(item)],
            13, false, "Yanzi.Color.Foreground", new Thickness(0, 13, 0, 0)));

        var preview = Card("当前 WPF 预览", "依据官网典型场景制作的可操作示例。下方保留参考信息和人工核对清单。");
        var stageRoot = (UIElement)_body.Children[^1];
        if (!TryRenderIsolatedPreview(item.Name, preview))
            preview.Children.Add(Text("独立演示：待补齐。当前仅有对应公共库入口和原专题页。", 13,
                false, "Yanzi.Color.MutedForeground"));
        _body.Children.Remove(stageRoot);
        _body.Children.Insert(0, stageRoot);

        var review = Card("逐项对比清单", "每一项默认「待核对」，只有人工验证后才能设为通过或不一致。记录保存在本地，不会自动报喜。");
        var oldRecord = LoadComponentAudit(item);
        var selectors = new List<ComboBox>();
        for (int i = 0; i < ComparisonDimensions.Length; i++)
        {
            var itemRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemRow.Children.Add(Text((i + 1).ToString("00") + "  " + ComparisonDimensions[i],
                13, false, "Yanzi.Color.Foreground"));
            var select = YanziUi.WithStyle(new ComboBox
            {
                Width = 138, Height = 32, Margin = new Thickness(16, 0, 0, 0)
            }, YanziUi.Styles.Select);
            foreach (var state in new[] { "待核对", "通过", "不一致" })
                select.Items.Add(state);
            select.SelectedItem = oldRecord.Results.Length > i &&
                new[] { "待核对", "通过", "不一致" }.Contains(oldRecord.Results[i])
                ? oldRecord.Results[i] : "待核对";
            System.Windows.Automation.AutomationProperties.SetName(select, "核对：" + ComparisonDimensions[i]);
            Grid.SetColumn(select, 1);
            itemRow.Children.Add(select);
            review.Children.Add(itemRow);
            selectors.Add(select);
        }
        review.Children.Add(Text("问题、截图坐标与修复建议", 12, true, "Yanzi.Color.Foreground",
            new Thickness(0, 13, 0, 8)));
        var notes = YanziUi.WithStyle(new TextBox
        {
            Text = oldRecord.Notes, MinHeight = 94, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top
        }, YanziUi.Styles.TextareaSoft);
        System.Windows.Automation.AutomationProperties.SetName(notes, "组件对比备注");
        review.Children.Add(notes);
        review.Children.Add(Button("保存 " + item.Name + " 核对记录", YanziUi.Styles.PillDefaultButton, () =>
        {
            var record = new ComponentAuditRecord
            {
                Name = item.Name, Notes = notes.Text, UpdatedAt = DateTimeOffset.Now,
                Results = selectors.Select(x => x.SelectedItem?.ToString() ?? "待核对").ToArray()
            };
            try
            {
                var path = AuditPath(item);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(record,
                    new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                File.Move(temporary, path, true);
                Status(item.Name + " 核对记录已保存：" + record.Results.Count(x => x == "通过")
                    + "/6 通过、" + record.Results.Count(x => x == "不一致") + " 项不一致");
            }
            catch (Exception ex) { Status("保存失败：" + ex.Message); }
        }));
        var progress = Card("验收说明", "官方基准和本地实现需并排人工比较；自动化构建通过不能替代像素、交互和无障碍专项验收。");
        progress.Children.Add(Text("默认对照状态：待核对。完成六项并保存后，方可将该组件记为通过。若某项不一致，请在备注中记录差异及截图。", 12, false, "Yanzi.Color.MutedForeground"));
        if (oldRecord.UpdatedAt is not null)
            progress.Children.Add(Text("上次保存：" + oldRecord.UpdatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                + " · 通过 " + oldRecord.Results.Count(x => x == "通过") + "/6",
                12, false, "Yanzi.Color.Foreground", new Thickness(0, 9, 0, 0)));
    }

    private static int GetComponentIndex(YanziComponentDescriptor item)
    {
        for (int i = 0; i < YanziComponentRegistry.Components.Count; i++)
            if (YanziComponentRegistry.Components[i].Name == item.Name) return i;
        throw new InvalidOperationException("Unregistered component: " + item.Name);
    }

    private ComponentAuditRecord LoadComponentAudit(YanziComponentDescriptor item)
    {
        try
        {
            var path = AuditPath(item);
            if (File.Exists(path))
            {
                var record = JsonSerializer.Deserialize<ComponentAuditRecord>(File.ReadAllText(path, Encoding.UTF8));
                if (record?.Name == item.Name) return record;
            }
        }
        catch { /* Corrupt reviews never prevent navigating the component catalog. */ }
        return new ComponentAuditRecord { Name = item.Name };
    }

    private bool TryRenderIsolatedPreview(string component, StackPanel host)
    {
        var showcase = BuildReferenceShowcase(component);
        if (showcase is not null)
        {
            host.Children.Add(ReferenceStage(showcase));
            return true;
        }
        var line = new WrapPanel { Margin = new Thickness(0, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center };
        void Add(UIElement e) => line.Children.Add(e);
        switch (component)
        {
            case "Accordion":
                var accordion = new YanziAccordion { Width = 350 };
                accordion.Add("Is it accessible?", Text("WPF 自定义 Accordion 可用键盘和点击展开。", 12,
                    false, "Yanzi.Color.Foreground", new Thickness(8)));
                accordion.Add("Can I use it in my app?", Text("可以。多个项目可以使用相同公共控件。", 12,
                    false, "Yanzi.Color.Foreground", new Thickness(8)));
                Add(accordion);
                break;
            case "Aspect Ratio":
                var ratioLabel = Text("16:9", 17, true, "Yanzi.Color.Foreground");
                ratioLabel.HorizontalAlignment = HorizontalAlignment.Center;
                var ratio = YanziLayoutPrimitives.AspectRatio(ratioLabel, 16d / 9d, 260);
                ratio.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
                Add(ratio);
                break;
            case "Attachment":
                Add(YanziContentPrimitives.Attachment("proposal.pdf",
                    () => Status("移除文件（仅演示）")));
                break;
            case "Breadcrumb":
                Add(YanziLayoutPrimitives.Breadcrumb(
                    ("Home", () => Status("Home")),
                    ("Components", () => Status("Components")),
                    ("Breadcrumb", null)));
                break;
            case "Bubble":
                Add(YanziContentPrimitives.MessageBubble("Hello, what can I help with?", false));
                Add(YanziContentPrimitives.MessageBubble("Show me the components.", true));
                break;
            case "Calendar":
                Add(YanziUi.WithStyle(new Calendar
                {
                    SelectedDate = DateTime.Today,
                    Margin = new Thickness(0, 0, 10, 0)
                }, YanziUi.Styles.Calendar));
                break;
            case "Carousel":
                var carousel = new YanziCarousel { Width = 330, Height = 150 };
                for (var i = 1; i <= 4; i++)
                {
                    var tile = new Border
                    {
                        Width = 106, Height = 95,
                        CornerRadius = new CornerRadius(9),
                        Margin = new Thickness(0, 0, 9, 0)
                    };
                    tile.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
                    var number = Text(i.ToString(), 25, true, "Yanzi.Color.Foreground");
                    number.HorizontalAlignment = HorizontalAlignment.Center;
                    tile.Child = number;
                    carousel.Add(tile);
                }
                Add(carousel);
                break;
            case "Collapsible":
                Add(YanziLayoutPrimitives.Collapsible("显示更多内容",
                    Text("这里展示折叠区域的额外内容", 13, false, "Yanzi.Color.Foreground",
                    new Thickness(8)), false));
                break;
            case "Direction":
                var rtl = Text("يمين · Right-to-left · 方向", 14, false, "Yanzi.Color.Foreground");
                rtl.FlowDirection = FlowDirection.RightToLeft;
                Add(rtl);
                break;
            case "Empty":
                Add(YanziPrimitives.EmptyState("No results", "Try searching for a different item."));
                break;
            case "Input OTP":
                Add(new YanziOtpInput(6) { Width = 315 });
                break;
            case "Item":
                Add(new YanziItem("Notifications", "Configure notification preferences", "◉",
                    () => Status("选择通知设置")));
                break;
            case "Marker":
                Add(YanziContentPrimitives.Marker("Ready", true));
                Add(YanziContentPrimitives.Marker("Requires attention", false));
                break;
            case "Message":
                Add(YanziContentPrimitives.MessageBubble("Incoming sample message", false));
                Add(YanziContentPrimitives.MessageBubble("Outgoing reply", true));
                break;
            case "Pagination":
                var paging = new YanziPagination { PageCount = 8 };
                paging.PageChanged += (_, n) => Status("页码：" + n);
                Add(paging);
                break;
            case "Resizable":
                var left = new Border();
                left.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
                left.Child = Text("Panel A", 13, true, "Yanzi.Color.Foreground");
                var right = new Border();
                right.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
                right.Child = Text("Panel B", 13, true, "Yanzi.Color.Foreground");
                var resize = YanziLayoutPrimitives.Resizable(left, right, 135, 125);
                resize.Width = 320;
                Add(resize);
                break;
            case "Scroll Area":
                var scrolling = new StackPanel();
                for (int i = 1; i <= 14; i++)
                    scrolling.Children.Add(Text("Item " + i, 13, false,
                        "Yanzi.Color.Foreground", new Thickness(7, 6, 7, 6)));
                var area = YanziLayoutPrimitives.ScrollArea(scrolling, 150);
                area.Width = 260;
                Add(area);
                break;
            case "Skeleton":
                var sk = new StackPanel();
                sk.Children.Add(YanziPrimitives.Skeleton(220, 14));
                sk.Children.Add(YanziPrimitives.Skeleton(165, 14));
                Add(sk);
                break;
            case "Table":
                var table = YanziUi.WithStyle(new DataGrid
                {
                    Width = 320, Height = 155, AutoGenerateColumns = true, IsReadOnly = true,
                    ItemsSource = new[]
                    {
                        new { Name = "Alice", Role = "Designer" },
                        new { Name = "Bob", Role = "Developer" }
                    }
                }, YanziUi.Styles.DataGrid);
                Add(table);
                break;
            case "Toggle Group":
                var toggleGroup = new YanziToggleGroup();
                toggleGroup.Add("Left");
                toggleGroup.Add("Center");
                toggleGroup.Add("Right");
                toggleGroup.SelectionChanged += (_, selection) => Status("当前选择：" + selection);
                Add(toggleGroup);
                break;
            case "Button":
                foreach (var (title, style) in new[]
                    { ("Default", YanziUi.Styles.PillDefaultButton),
                      ("Secondary", YanziUi.Styles.PillSecondaryButton),
                      ("Outline", YanziUi.Styles.PillOutlineButton) })
                {
                    var caption = title;
                    var b = Button(caption, style, () => Status(component + " / " + caption));
                    b.Margin = new Thickness(0, 0, 9, 9);
                    Add(b);
                }
                break;
            case "Badge":
                foreach (var variant in Enum.GetValues<YanziBadgeVariant>())
                    Add(YanziUi.WithStyle(new YanziBadge { Content = variant.ToString(),
                        Variant = variant, Margin = new Thickness(0, 0, 9, 9) },
                        YanziUi.Styles.BadgeGeistPreview));
                break;
            case "Input": Add(new YanziSearchBox("Name", useGeist: true)); break;
            case "Textarea":
                Add(YanziUi.WithStyle(new TextBox { Width = 290, Height = 90,
                    AcceptsReturn = true, Text = "Message", TextWrapping = TextWrapping.Wrap },
                    YanziUi.Styles.TextareaSoft)); break;
            case "Checkbox":
                Add(YanziUi.WithStyle(new CheckBox { Content = "接受通知", IsChecked = true },
                    YanziUi.Styles.CheckBoxPreview)); break;
            case "Switch":
                Add(YanziUi.WithStyle(new CheckBox { Content = "启用通知", IsChecked = true },
                    YanziUi.Styles.SwitchShadcn)); break;
            case "Radio Group":
                var group = new YanziRadioGroup { Orientation = Orientation.Horizontal };
                group.Add("默认", "default");
                group.Add("舒适", "comfortable");
                group.Add("紧凑", "compact");
                group.Select("default");
                group.SelectionChanged += (_, value) => Status("选中：" + value);
                Add(group);
                break;
            case "Alert Dialog":
                Add(Button("打开 Alert Dialog", YanziUi.Styles.PillDefaultButton, () =>
                    YanziDialog.Confirm(this, "确认执行操作？", "这里只演示界面，不会修改实际数据。")));
                break;
            case "Dropdown Menu":
                var trigger = Button("展开菜单", YanziUi.Styles.PillOutlineButton, () => { });
                var menu = new YanziDropdownMenu();
                menu.AddLabel("操作");
                menu.AddAction("复制", () => Status("已选择复制"));
                menu.AddSeparator();
                menu.AddAction("设置", () => Status("已选择设置"));
                menu.Attach(trigger);
                Add(trigger);
                break;
            case "Button Group":
                var grouped = new YanziSegmentedButtonGroup();
                grouped.Add("前一项", () => Status("前一项"));
                grouped.Add("后一项", () => Status("后一项"));
                Add(grouped);
                break;
            case "Progress":
                Add(YanziUi.WithStyle(new ProgressBar { Width = 270, Height = 11,
                    Minimum = 0, Maximum = 100, Value = 67 }, YanziUi.Styles.Progress));
                break;
            case "Slider":
                Add(YanziUi.WithStyle(new Slider { Width = 260, Minimum = 0,
                    Maximum = 100, Value = 45 }, YanziUi.Styles.Slider));
                break;
            case "Spinner": Add(new YanziLoadingRing { Width = 28, Height = 28 }); break;
            case "Tooltip":
                Add(Button("鼠标悬停", YanziUi.Styles.PillOutlineButton,
                    () => Status("Tooltip 演示")) is Button hint
                    ? WithToolTip(hint, "来自 Yanzi UI 的 Tooltip") : new TextBlock());
                break;
            case "Chart":
                var chart = new YanziBarChart { Width = 280, Height = 178 };
                chart.SetData([new YanziBarPoint("Mon", 40),
                    new YanziBarPoint("Tue", 88), new YanziBarPoint("Wed", 60)]);
                Add(chart);
                break;
            case "Tabs":
                var tabs = YanziUi.WithStyle(new TabControl { Width = 300, Height = 120 },
                    YanziUi.Styles.Tabs);
                tabs.Items.Add(new TabItem { Header = "Overview",
                    Content = new TextBlock { Text = "Overview content", Margin = new Thickness(12) } });
                tabs.Items.Add(new TabItem { Header = "Details",
                    Content = new TextBlock { Text = "Details content", Margin = new Thickness(12) } });
                Add(tabs);
                break;
            case "Select":
            case "Native Select":
                var combo = YanziUi.WithStyle(new ComboBox { Width = 230 },
                    YanziUi.Styles.Select);
                combo.Items.Add("Option one");
                combo.Items.Add("Option two");
                combo.SelectedIndex = 0;
                Add(combo);
                break;
            case "Toggle":
                Add(YanziUi.WithStyle(new ToggleButton { Content = "Bold", IsChecked = true },
                    YanziUi.Styles.ToggleButton));
                break;
            case "Separator":
                var lineRule = YanziPrimitives.Separator();
                lineRule.Width = 290;
                Add(lineRule);
                break;
            case "Typography":
                Add(Text("The quick brown fox · 文字排版", 21, true,
                    "Yanzi.Color.Foreground"));
                break;
            case "Card":
                Add(Text("Card / 标题、内容与操作区的间距", 14, false,
                    "Yanzi.Color.Foreground"));
                break;
            case "Label":
            case "Field":
                Add(Text("Name", 13, true, "Yanzi.Color.Foreground"));
                Add(YanziUi.WithStyle(new TextBox { Width = 220, Text = "Sample" },
                    YanziUi.Styles.InputSoft));
                break;
            case "Kbd":
                Add(YanziPrimitives.Kbd("Ctrl + K"));
                break;
            case "Toast":
                Add(Button("显示 Toast", YanziUi.Styles.PillDefaultButton,
                    () => YanziToast.Show(this, "已保存（演示）", YanziToastKind.Success)));
                break;
            case "Alert":
                Add(YanziPrimitives.Alert("Heads up!", "You can add components to your app."));
                break;
            case "Avatar":
                Add(YanziPrimitives.Avatar("YZ", 44));
                break;
            default:
                var fallback = new StackPanel();
                if (!TryRenderPendingPreview(component, fallback)) return false;
                host.Children.Add(ReferenceStage(fallback));
                return true;
        }
        host.Children.Add(ReferenceStage(line));
        return true;
    }

    private static Button WithToolTip(Button button, string tip) { button.ToolTip = tip; return button; }
}
