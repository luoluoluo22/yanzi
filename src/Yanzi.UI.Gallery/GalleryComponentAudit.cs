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

    private static string NormalizeAuditResult(string? value) => value switch
    {
        "通过" => "通过",
        "不一致" or "不通过" => "不通过",
        _ => "待核对"
    };

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
        if (_page == 0 && _scroll != null) _overviewReturnScrollOffset = _scroll.VerticalOffset;
        _activeComponent = item;
        _page = -1;
        _pageTitle.Text = item.Name;
        _pageSubtitle.Text = "官方组件独立对照 · " + (GetComponentIndex(item) + 1).ToString("00") + " / 64";
        _body.Children.Clear();
        _body.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "Yanzi.Font.Geist");
        _overviewGrid = null;
        _overviewCatalogGrid = null;
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
        var backToWall = Button("← 返回全部组件", YanziUi.Styles.PillOutlineButton, ReturnToOverviewWall);
        System.Windows.Automation.AutomationProperties.SetName(backToWall, "返回全部组件首页");
        actions.Children.Add(backToWall);
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

        var review = Card("逐项对比清单",
            "点击「通过」或「不通过」立即保存；未选择的项目保持未核对。备注可以单独保存。");
        var oldRecord = LoadComponentAudit(item);
        var states = Enumerable.Range(0, ComparisonDimensions.Length)
            .Select(i => NormalizeAuditResult(oldRecord.Results.ElementAtOrDefault(i))).ToArray();
        var notes = YanziUi.WithStyle(new TextBox
        {
            Text = oldRecord.Notes, MinHeight = 94, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top
        }, YanziUi.Styles.TextareaSoft);
        System.Windows.Automation.AutomationProperties.SetName(notes, "组件对比备注");

        void SaveCurrentAudit()
        {
            var record = new ComponentAuditRecord
            {
                Name = item.Name, Notes = notes.Text, UpdatedAt = DateTimeOffset.Now,
                Results = states.ToArray()
            };
            try
            {
                var path = AuditPath(item);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(record,
                    new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                File.Move(temporary, path, true);
                Status(item.Name + " 已保存：通过 " + record.Results.Count(x => x == "通过")
                    + " / 不通过 " + record.Results.Count(x => x == "不通过")
                    + " / 未核对 " + record.Results.Count(x => x == "待核对"));
            }
            catch (Exception ex) { Status("保存失败：" + ex.Message); }
        }

        for (int i = 0; i < ComparisonDimensions.Length; i++)
        {
            var index = i;
            var itemRow = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemRow.Children.Add(Text((index + 1).ToString("00") + "  " + ComparisonDimensions[index],
                13, false, "Yanzi.Color.Foreground"));
            var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            Button? pass = null;
            Button? fail = null;
            void RefreshButtons()
            {
                if (pass is null || fail is null) return;
                YanziUi.WithStyle(pass, states[index] == "通过"
                    ? YanziUi.Styles.PillDefaultButton : YanziUi.Styles.PillOutlineButton);
                YanziUi.WithStyle(fail, states[index] == "不通过"
                    ? YanziUi.Styles.PillDefaultButton : YanziUi.Styles.PillOutlineButton);
            }
            pass = Button("通过", YanziUi.Styles.PillOutlineButton, () =>
            {
                states[index] = "通过";
                RefreshButtons();
                SaveCurrentAudit();
            });
            fail = Button("不通过", YanziUi.Styles.PillOutlineButton, () =>
            {
                states[index] = "不通过";
                RefreshButtons();
                SaveCurrentAudit();
            });
            foreach (var button in new[] { pass, fail })
            {
                button.Width = 72;
                button.Height = 29;
                button.FontSize = 12;
                button.Margin = new Thickness(0, 0, 5, 0);
            }
            System.Windows.Automation.AutomationProperties.SetName(pass,
                "通过：" + ComparisonDimensions[index]);
            System.Windows.Automation.AutomationProperties.SetName(fail,
                "不通过：" + ComparisonDimensions[index]);
            choices.Children.Add(pass);
            choices.Children.Add(fail);
            RefreshButtons();
            Grid.SetColumn(choices, 1);
            itemRow.Children.Add(choices);
            review.Children.Add(itemRow);
        }

        review.Children.Add(Text("问题、截图坐标与修复建议", 12, true, "Yanzi.Color.Foreground",
            new Thickness(0, 13, 0, 8)));
        review.Children.Add(notes);
        review.Children.Add(Button("保存备注", YanziUi.Styles.PillDefaultButton, SaveCurrentAudit));

        var progress = Card("验收说明",
            "参照官网的实际渲染与交互手工判断；编译、自动测试通过不等于人工通过。");
        progress.Children.Add(Text("每项只需点击「通过」或「不通过」，立即持久化；未点击保留待核对状态。修改批注不会丢失。",
            12, false, "Yanzi.Color.MutedForeground"));
        if (oldRecord.UpdatedAt is not null)
            progress.Children.Add(Text("上次保存：" + oldRecord.UpdatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                + " · 通过 " + states.Count(x => x == "通过") + "/6"
                + " · 不通过 " + states.Count(x => x == "不通过"), 12, false,
                "Yanzi.Color.Foreground", new Thickness(0, 9, 0, 0)));
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
                var month = new YanziCalendarMonth(new DateTime(2026, 10, 1));
                month.SetSelectedDate(new DateTime(2026, 10, 8));
                month.DateSelected += (_, day) => Status("日历：" + day.ToString("yyyy-MM-dd"));
                Add(month);
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
                var directionDemo = new StackPanel { Width = 405 };
                var directionControls = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12)
                };
                var directionLabel = Text("LTR", 13, true, "Yanzi.Color.Foreground");
                var directionPanel = new StackPanel { Margin = new Thickness(10) };
                var directionBorder = new Border
                {
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10), Child = directionPanel
                };
                directionBorder.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
                directionBorder.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
                directionPanel.Children.Add(Text("RTL / LTR · العربية", 14, true, "Yanzi.Color.Foreground"));
                var mirroredRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0)
                };
                mirroredRow.Children.Add(YanziUi.WithStyle(new TextBox { Width = 150,
                    Text = "مرحباً" }, YanziUi.Styles.InputSoft));
                mirroredRow.Children.Add(YanziUi.WithStyle(new Button
                {
                    Content = "Action", Margin = new Thickness(8, 0, 0, 0)
                }, YanziUi.Styles.OutlineButton));
                directionPanel.Children.Add(mirroredRow);
                var changeDirection = Button("切换 RTL / LTR", YanziUi.Styles.PillOutlineButton, () =>
                {
                    var rtl = directionBorder.FlowDirection != FlowDirection.RightToLeft;
                    directionBorder.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                    directionLabel.Text = rtl ? "RTL" : "LTR";
                    Status("Direction：" + directionLabel.Text);
                });
                directionControls.Children.Add(changeDirection);
                directionControls.Children.Add(directionLabel);
                directionDemo.Children.Add(directionControls);
                directionDemo.Children.Add(directionBorder);
                Add(directionDemo);
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
                var markers = new StackPanel { Width = 410 };
                markers.Children.Add(YanziContentPrimitives.MarkerVariant("An inline marker for notes."));
                markers.Children.Add(YanziContentPrimitives.MarkerVariant("A border marker for row boundaries.", "border"));
                markers.Children.Add(YanziContentPrimitives.MarkerVariant("End of conversation", "separator"));
                markers.Children.Add(YanziContentPrimitives.Marker("Running tests", true));
                Add(markers);
                break;
            case "Message":
                var messages = new StackPanel { Width = 455 };
                messages.Children.Add(YanziContentPrimitives.MessageRow(
                    "Hello, what can I help with today?", false, "AI", "Assistant", "10:24 AM"));
                messages.Children.Add(YanziContentPrimitives.MessageRow(
                    "Show me how to compose a message.", true, "ME", "You", "Sent · 10:25 AM"));
                messages.Children.Add(YanziContentPrimitives.MessageRow(
                    "Messages can include an avatar, header, bubble and footer.", false, "AI",
                    "Assistant", "Completed"));
                Add(messages);
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

                var top = new Border { Padding = new Thickness(12) };
                top.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
                top.Child = Text("上面板（拖动水平手柄）", 13, false, "Yanzi.Color.Foreground");
                var bottom = new Border { Padding = new Thickness(12) };
                bottom.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
                bottom.Child = Text("下面板", 13, false, "Yanzi.Color.Foreground");
                var vertical = YanziLayoutPrimitives.ResizableVertical(top, bottom, 112, 260);
                vertical.Width = 320;
                vertical.Margin = new Thickness(0, 16, 0, 0);
                Add(vertical);

                var saved = YanziLayoutPrimitives.GetResizableFirstSize(vertical, vertical: true);
                var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                controls.Children.Add(Button("保存面板高度", YanziUi.Styles.OutlineButton,
                    () =>
                    {
                        saved = YanziLayoutPrimitives.GetResizableFirstSize(vertical, vertical: true);
                        Status("已保存上面板高度：" + saved.ToString("0") + " DIP");
                    }));
                controls.Children.Add(Button("恢复高度", YanziUi.Styles.OutlineButton,
                    () =>
                    {
                        YanziLayoutPrimitives.SetResizableFirstSize(vertical, saved, vertical: true);
                        Status("已恢复上面板高度");
                    }));
                Add(controls);
                break;
            case "Scroll Area":
                var scrolling = new StackPanel();
                for (int i = 1; i <= 14; i++)
                    scrolling.Children.Add(Text("Item " + i, 13, false,
                        "Yanzi.Color.Foreground", new Thickness(7, 6, 7, 6)));
                var area = YanziLayoutPrimitives.ScrollArea(scrolling, 150);
                area.Width = 260;
                Add(area);
                var horizontalContent = new StackPanel { Orientation = Orientation.Horizontal };
                for (int i = 1; i <= 12; i++)
                {
                    var chip = YanziUi.WithStyle(new Button
                    {
                        Content = "项目 " + i,
                        Margin = new Thickness(0, 0, 8, 0),
                        MinHeight = 34
                    }, YanziUi.Styles.OutlineButton);
                    horizontalContent.Children.Add(chip);
                }
                var horizontalArea = YanziLayoutPrimitives.ScrollArea(horizontalContent, 70, horizontal: true);
                horizontalArea.Width = 275;
                horizontalArea.Margin = new Thickness(0, 12, 0, 0);
                Add(horizontalArea);
                break;
            case "Skeleton":
                var skeletons = new StackPanel { Width = 400 };
                var skeletonRow = new StackPanel { Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 18) };
                skeletonRow.Children.Add(YanziPrimitives.Skeleton(44, 44));
                var skeletonLines = new StackPanel { Margin = new Thickness(12, 2, 0, 0) };
                skeletonLines.Children.Add(YanziPrimitives.Skeleton(175, 13));
                skeletonLines.Children.Add(YanziPrimitives.Skeleton(115, 13));
                skeletonRow.Children.Add(skeletonLines);
                skeletons.Children.Add(skeletonRow);
                skeletons.Children.Add(YanziPrimitives.Skeleton(390, 112));
                skeletons.Children.Add(YanziPrimitives.Skeleton(260, 12));
                skeletons.Children.Add(YanziPrimitives.Skeleton(340, 12));
                var formSkeleton = new StackPanel { Margin = new Thickness(0, 15, 0, 0) };
                formSkeleton.Children.Add(YanziPrimitives.Skeleton(90, 12));
                formSkeleton.Children.Add(YanziPrimitives.Skeleton(380, 35));
                skeletons.Children.Add(formSkeleton);
                Add(skeletons);
                break;
            case "Table":
                var table = new YanziTable { Width = 360 };
                table.SetData(new[] { "姓名", "岗位" }, new IReadOnlyList<string>[]
                {
                    new[] { "Alice", "设计师" },
                    new[] { "Bob", "开发者" }
                });
                table.SelectedRowChanged += (_, row) => Status("选中：" + table.Rows[row][0]);
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
            case "Input":
            {
                var inputExamples = new StackPanel { Width = 430 };
                var textInput = YanziUi.WithStyle(new TextBox
                { Text = "Jordan Lee", FontSize = 14 }, YanziUi.Styles.InputSoft);
                inputExamples.Children.Add(YanziPrimitives.ValidatedField(
                    "Name", textInput, "Basic text input"));
                var email = YanziUi.WithStyle(new TextBox { Text = "invalid-email" },
                    YanziUi.Styles.InputSoft);
                inputExamples.Children.Add(YanziPrimitives.ValidatedField(
                    "Email", email, "Enter a valid email.", "Please enter a valid email address.", true));
                var pwd = YanziUi.WithStyle(new PasswordBox(), YanziUi.Styles.Password);
                inputExamples.Children.Add(YanziPrimitives.ValidatedField("Password", pwd,
                    "Native masked entry"));
                var amount = YanziUi.WithStyle(new TextBox { Text = "42" },
                    YanziUi.Styles.InputSoft);
                amount.PreviewTextInput += (_, e) =>
                    e.Handled = !e.Text.All(char.IsDigit);
                inputExamples.Children.Add(YanziPrimitives.Field("Number", amount,
                    "Digits only; invalid keys are ignored."));
                inputExamples.Children.Add(YanziPrimitives.Field("Disabled",
                    YanziUi.WithStyle(new TextBox { Text = "Cannot edit", IsEnabled = false },
                        YanziUi.Styles.InputSoft)));
                var filePicker = Button("选择文件…", YanziUi.Styles.PillOutlineButton, () =>
                {
                    var picker = new Microsoft.Win32.OpenFileDialog();
                    if (picker.ShowDialog(this) == true)
                        Status("Selected: " + System.IO.Path.GetFileName(picker.FileName));
                });
                inputExamples.Children.Add(YanziPrimitives.Field("File", filePicker));
                var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
                var searchInput = YanziUi.WithStyle(new TextBox { Width = 276 },
                    YanziUi.Styles.InputSoft);
                searchRow.Children.Add(searchInput);
                searchRow.Children.Add(Button("Search", YanziUi.Styles.PillDefaultButton,
                    () => Status("Search: " + searchInput.Text)));
                inputExamples.Children.Add(YanziPrimitives.Field("Inline", searchRow));
                Add(inputExamples);
                break;
            }
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
                var legacyRadio = YanziUi.WithStyle(new RadioButton
                {
                    Content = "兼容旧式 RadioButton（使用公共模板）",
                    IsChecked = true,
                    Margin = new Thickness(0, 8, 0, 0)
                }, YanziUi.Styles.Radio);
                Add(legacyRadio);
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
                var progressDemo = new StackPanel { Width = 400 };
                var progressHeading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var valueText = Text("56%", 13, true, "Yanzi.Color.Foreground");
                DockPanel.SetDock(valueText, Dock.Right);
                progressHeading.Children.Add(valueText);
                progressHeading.Children.Add(Text("Upload progress", 13, true, "Yanzi.Color.Foreground"));
                progressDemo.Children.Add(progressHeading);
                var bar = YanziUi.WithStyle(new ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = 56,
                    Height = 9, Width = 392
                }, YanziUi.Styles.Progress);
                progressDemo.Children.Add(bar);
                var adjust = YanziUi.WithStyle(new Slider
                {
                    Minimum = 0, Maximum = 100, Value = 56,
                    Width = 392, Margin = new Thickness(0, 14, 0, 0)
                }, YanziUi.Styles.Slider);
                adjust.ValueChanged += (_, _) =>
                {
                    bar.Value = adjust.Value;
                    valueText.Text = Math.Round(adjust.Value).ToString() + "%";
                };
                progressDemo.Children.Add(adjust);
                Add(progressDemo);
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
            {
                var chartPane = new StackPanel { Width = 500 };
                var chartKind = YanziUi.WithStyle(new ComboBox
                {
                    Width = 160, HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 12)
                }, YanziUi.Styles.Select);
                foreach (var title in new[] { "柱状图", "折线图", "面积图", "饼图" })
                    chartKind.Items.Add(title);
                chartKind.SelectedIndex = 0;
                chartPane.Children.Add(chartKind);
                var chart = new YanziChart { Width = 460, Height = 230 };
                chart.SetData(new[]
                {
                    new YanziBarPoint("周一", 40),
                    new YanziBarPoint("周二", 88),
                    new YanziBarPoint("周三", 60),
                    new YanziBarPoint("周四", 72),
                    new YanziBarPoint("周五", 55)
                });
                chartKind.SelectionChanged += (_, _) =>
                {
                    chart.Kind = (YanziChartKind)chartKind.SelectedIndex;
                    Status("Chart 图形：" + chartKind.SelectedItem);
                };
                chart.SelectedPointChanged += (_, index) =>
                    Status($"Chart 数据：{chart.Points[index].Label} {chart.Points[index].Value:0.##}");
                chartPane.Children.Add(chart);
                Add(chartPane);
                break;
            }
            case "Tabs":
                var tabs = YanziUi.WithStyle(new TabControl { Width = 300, Height = 120 },
                    YanziUi.Styles.Tabs);
                tabs.SetResourceReference(TabControl.ItemContainerStyleProperty, YanziUi.Styles.TabItem);
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
                System.Windows.Automation.AutomationProperties.SetName(combo, "Native Select choices");
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
            case "Label":
            {
                var labels = new StackPanel { Width = 410 };
                var entry = YanziUi.WithStyle(new TextBox { Text = "jordan@example.com" },
                    YanziUi.Styles.InputSoft);
                labels.Children.Add(YanziPrimitives.ValidatedField("Email address", entry,
                    "Clicking the label focuses the input.", required: true));
                var agree = YanziUi.WithStyle(new CheckBox
                { Content = "Accept terms and conditions" }, YanziUi.Styles.CheckBoxPreview);
                labels.Children.Add(YanziPrimitives.Label("Terms", agree));
                labels.Children.Add(agree);
                var disabled = YanziUi.WithStyle(new TextBox
                { IsEnabled = false, Text = "Disabled" }, YanziUi.Styles.InputSoft);
                labels.Children.Add(YanziPrimitives.Label("Disabled label", disabled));
                labels.Children.Add(disabled);
                Add(labels);
                break;
            }
            case "Field":
            {
                var form = YanziPrimitives.FieldSet("Profile",
                    "This information will appear on invoices and emails.");
                form.Width = 435;
                form.Children.Add(YanziPrimitives.ValidatedField("Full name",
                    YanziUi.WithStyle(new TextBox { Text = "Jordan Lee" }, YanziUi.Styles.InputSoft),
                    "Enter the name to display.", required: true));
                form.Children.Add(YanziPrimitives.ValidatedField("Email",
                    YanziUi.WithStyle(new TextBox { Text = "invalid-email" }, YanziUi.Styles.InputSoft),
                    "We'll send updates to this address.", "Please enter a valid email."));
                form.Children.Add(YanziPrimitives.ValidatedField("Readonly",
                    YanziUi.WithStyle(new TextBox
                    { Text = "Read-only field", IsReadOnly = true }, YanziUi.Styles.InputSoft)));
                var inline = new StackPanel { Orientation = Orientation.Horizontal };
                var search = YanziUi.WithStyle(new TextBox { Width = 265 },
                    YanziUi.Styles.InputSoft);
                inline.Children.Add(search);
                inline.Children.Add(Button("Search", YanziUi.Styles.PillDefaultButton,
                    () => Status("Field: " + search.Text)));
                form.Children.Add(YanziPrimitives.Field("Inline search", inline));
                Add(form);
                break;
            }
            case "Kbd":
                Add(YanziPrimitives.Kbd("␣ 空格"));
                Add(YanziPrimitives.Kbd("↵ 回车"));
                Add(YanziPrimitives.Kbd("⇥ 制表"));
                Add(YanziPrimitives.KbdGroup("Ctrl", "K"));
                break;
            case "Toast":
                var toastControls = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var kind in new[]
                    { YanziToastKind.Info, YanziToastKind.Success, YanziToastKind.Warning, YanziToastKind.Error, YanziToastKind.Loading })
                {
                    var copy = kind;
                    toastControls.Children.Add(Button("Show " + copy,
                        YanziUi.Styles.PillOutlineButton,
                        () => YanziToast.Show(this, copy + ": operation completed", copy,
                            description: "This notification is owned by the shared WPF toaster.",
                            actionLabel: copy == YanziToastKind.Success ? "Undo" : null,
                            onAction: copy == YanziToastKind.Success ? () => Status("Undo selected") : null)));
                }
                Add(toastControls);
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

    private static Button WithToolTip(Button button, string tip)
    {
        button.ToolTip = YanziUi.WithStyle(new ToolTip { Content = tip }, YanziUi.Styles.Tooltip);
        return button;
    }
}
