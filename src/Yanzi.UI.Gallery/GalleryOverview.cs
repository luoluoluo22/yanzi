using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

internal sealed partial class GalleryWindow
{
    /// <summary>Interactive component wall, not a dashboard of implementation statistics.</summary>
    private void Overview()
    {
        var hero = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 2, 8, 16),
            MaxWidth = 690
        };
        _body.Children.Add(hero);
        var tag = new YanziBadge
        {
            Content = "Reusable desktop components  ↗",
            Variant = YanziBadgeVariant.Secondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        YanziUi.WithStyle(tag, YanziUi.Styles.BadgeGeistPreview);
        hero.Children.Add(tag);
        var headline = Text("The foundation for Yanzi UI", 23, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 7));
        headline.TextAlignment = TextAlignment.Center;
        hero.Children.Add(headline);
        var description = Text("可组合、可扩展的 Windows 原生组件。直接操作下面的按钮、输入框、菜单和表单，观察真实的视觉与交互。", 13, false,
            "Yanzi.Color.MutedForeground", new Thickness(8, 0, 8, 12));
        description.TextAlignment = TextAlignment.Center;
        hero.Children.Add(description);
        var heroActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        heroActions.Children.Add(PreviewGeistButton(Button("浏览组件  →", YanziUi.Styles.PillDefaultButton, () => _overviewCatalogHeading?.BringIntoView())));
        heroActions.Children.Add(PreviewGeistButton(Button("视觉规范", YanziUi.Styles.PillOutlineButton, () => ShowPage(6))));
        hero.Children.Add(heroActions);

        // The registry-driven component wall is the first section of the homepage.
        OverviewComponentWall();

        var threeColumns = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 6, 0) };
        _overviewGrid = threeColumns;
        _body.Children.Add(threeColumns);
        var left = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };
        var middle = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };
        var right = new StackPanel();
        threeColumns.Children.Add(left);
        threeColumns.Children.Add(middle);
        threeColumns.Children.Add(right);

        // Component sampler: real buttons, editable controls and checkable states.
        var sampler = PreviewCard(left, null);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(SmallButton("Default  ↗", YanziUi.Styles.PillDefaultButton,
            () => YanziToast.Show(this, "操作已完成（演示）", YanziToastKind.Success)));
        buttons.Children.Add(SmallButton("Secondary", YanziUi.Styles.PillSecondaryButton,
            () => Status("Secondary 按钮被点击")));
        buttons.Children.Add(SmallButton("Outline", YanziUi.Styles.PillOutlineButton,
            () => Status("Outline 按钮被点击")));
        sampler.Children.Add(buttons);
        var search = new YanziSearchBox("Name", useGeist: true)
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        sampler.Children.Add(search);

        var message = YanziUi.WithStyle(new TextBox
        {
            Text = "Message",
            Height = 78,
            Padding = new Thickness(12, 10, 12, 10),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, YanziUi.Styles.TextareaSoft);
        message.SetResourceReference(Control.FontFamilyProperty, "Yanzi.Font.Geist");
        message.FontSize = 14;
        sampler.Children.Add(message);
        var tags = new WrapPanel { Margin = new Thickness(0, 12, 0, 11) };
        tags.Children.Add(PreviewBadge("Badge", YanziBadgeVariant.Default));
        tags.Children.Add(PreviewBadge("Secondary", YanziBadgeVariant.Secondary));
        tags.Children.Add(PreviewBadge("Outline", YanziBadgeVariant.Outline));
        sampler.Children.Add(tags);
        // Match shadcn homepage: unchecked Radio, checked Radio, checked Checkbox, enabled Switch.
        // These two radios are independent state samples, while real exclusive groups use YanziRadioGroup.
        var states = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
        var radioUnchecked = new YanziRadio { Value = "sample-off", IsChecked = false,
            ToolTip = "Radio · 未选中", Margin = new Thickness(0, 0, 2, 0) };
        var radioChecked = new YanziRadio { Value = "sample-on", IsChecked = true,
            ToolTip = "Radio · 已选中", Margin = new Thickness(0, 0, 3, 0) };
        System.Windows.Automation.AutomationProperties.SetName(radioUnchecked, "Radio 未选中");
        System.Windows.Automation.AutomationProperties.SetName(radioChecked, "Radio 已选中");
        states.Children.Add(radioUnchecked);
        states.Children.Add(radioChecked);
        var checkbox = YanziUi.WithStyle(new CheckBox { IsChecked = true, ToolTip = "Checkbox · 已选中",
            Margin = new Thickness(0, 0, 4, 0) }, YanziUi.Styles.CheckBoxPreview);
        System.Windows.Automation.AutomationProperties.SetName(checkbox, "Checkbox 已选中");
        states.Children.Add(checkbox);
        var switchControl = YanziUi.WithStyle(new CheckBox { IsChecked = true, ToolTip = "Switch · 开启" }, YanziUi.Styles.Toggle);
        System.Windows.Automation.AutomationProperties.SetName(switchControl, "Switch 开启");
        switchControl.SetResourceReference(CheckBox.StyleProperty, YanziUi.Styles.SwitchShadcn);
        states.Children.Add(switchControl);
        sampler.Children.Add(states);
        var footButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        footButtons.Children.Add(SmallButton("Alert Dialog", YanziUi.Styles.PillOutlineButton,
            () => YanziDialog.Confirm(this, "确认执行操作？", "这是组件展示，不会修改任何实际数据。", "确认")));
        var segmented = new YanziSegmentedButtonGroup { Margin = new Thickness(0, 0, 6, 5) };
        PreviewGeistButton(segmented.Add("Button Group", () => Status("Button Group 被点击")));
        var menuButton = segmented.Add("展开菜单", () => { });
        menuButton.Content = YanziIcons.ChevronUp(16);
        menuButton.ToolTip = "展开操作菜单";
        menuButton.Width = 39;
        menuButton.MinWidth = 39;
        menuButton.Padding = new Thickness(0);
        menuButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        menuButton.VerticalContentAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(menuButton, "展开操作菜单");
        var menu = new YanziDropdownMenu();
        menu.AddLabel("操作");
        menu.AddAction("复制", () => Status("菜单：复制"));
        menu.AddAction("查看详情", () => Status("菜单：查看详情"));
        menu.AddSeparator();
        menu.AddAction("设置", () => Status("菜单：设置"));
        menu.Attach(menuButton);
        footButtons.Children.Add(segmented);
        sampler.Children.Add(footButtons);

        var navCard = PreviewCard(left, "Workspace");
        foreach (var (label, glyph) in new[] { ("总览", "◈"), ("组件", "▣"), ("设置", "⚙"), ("用户", "♙") })
        {
            var item = SmallButton(glyph + "  " + label, label == "总览" ? YanziUi.Styles.SecondaryButton : YanziUi.Styles.GhostButton,
                () => Status("导航：" + label));
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            item.HorizontalContentAlignment = HorizontalAlignment.Left;
            item.Margin = new Thickness(0, 0, 0, 4);
            navCard.Children.Add(item);
        }

        // Reusable chart + contextual status card, aligned with the homepage's layered card composition.
        var timeline = PreviewCard(middle, "Contribution History", "Last 5 months of activity");
        var chart = new YanziBarChart
        {
            Height = 178,
            MaxBarHeight = 128,
            SelectedIndex = 0,
            Margin = new Thickness(0, 2, 0, 16)
        };
        chart.SetData(new[]
        {
            new YanziBarPoint("Dec", 61),
            new YanziBarPoint("Jan", 89),
            new YanziBarPoint("Feb", 71),
            new YanziBarPoint("Mar", 94),
            new YanziBarPoint("Apr", 52)
        });
        timeline.Children.Add(chart);

        var upcoming = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13, 11, 13, 12),
            Margin = new Thickness(0, 2, 0, 14)
        };
        upcoming.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        var upcomingDetails = new StackPanel();
        upcomingDetails.Children.Add(Text("UPCOMING", 10, false, "Yanzi.Color.MutedForeground",
            new Thickness(0, 0, 0, 7)));
        upcomingDetails.Children.Add(Text("May 2024", 14, true, "Yanzi.Color.Foreground",
            new Thickness(0, 0, 0, 5)));
        upcomingDetails.Children.Add(Text("Scheduled", 11, false, "Yanzi.Color.MutedForeground"));
        upcoming.Child = upcomingDetails;
        timeline.Children.Add(upcoming);

        var wide = SmallButton("View Full Report", YanziUi.Styles.DefaultButton, () => ShowPage(11));
        wide.HorizontalAlignment = HorizontalAlignment.Stretch;
        wide.Margin = new Thickness(0);
        timeline.Children.Add(wide);

        var balance = PreviewCard(middle, "Storage overview", "本地演示数据");
        balance.Children.Add(Text("2,048  MB", 26, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 8)));
        balance.Children.Add(PreviewBadge("正常运行", YanziBadgeVariant.Outline));
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 68, Height = 6, Margin = new Thickness(0, 18, 0, 12) };
        balance.Children.Add(progress);
        balance.Children.Add(Text("已使用 68% · 这是一段用于展示布局的示例数据", 11, false, "Yanzi.Color.MutedForeground"));

        var profile = PreviewCard(right, "Account settings", "模拟真实应用中的表单布局");
        var avatar = new Border { Width = 59, Height = 59, CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 12) };
        avatar.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        avatar.Child = Text("燕", 27, true, "Yanzi.Color.Foreground");
        profile.Children.Add(avatar);
        var username = Text("Yanzi Desktop", 15, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 5));
        username.TextAlignment = TextAlignment.Center;
        profile.Children.Add(username);
        var subtitle = Text("本地优先 · 可扩展", 11, false, "Yanzi.Color.MutedForeground", new Thickness(0, 0, 0, 20));
        subtitle.TextAlignment = TextAlignment.Center;
        profile.Children.Add(subtitle);
        profile.Children.Add(Text("Display name", 12, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 7)));
        profile.Children.Add(YanziUi.WithStyle(new TextBox { Text = "Yanzi", Margin = new Thickness(0, 0, 0, 12) }, YanziUi.Styles.Input));
        profile.Children.Add(Text("布局密度", 12, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 7)));
        var density = new YanziToggleGroup { Margin = new Thickness(0, 0, 0, 16) };
        density.Add("紧凑");
        density.Add("舒适");
        density.SelectionChanged += (_, selected) => Status("布局密度（演示）：" + selected);
        profile.Children.Add(density);
        var save = SmallButton("保存设置", YanziUi.Styles.DefaultButton,
            () => YanziToast.Show(this, "模拟设置已保存", YanziToastKind.Success));
        save.HorizontalAlignment = HorizontalAlignment.Stretch;
        profile.Children.Add(save);

        var conversation = PreviewCard(right, "New chat", "在燕子里体验聊天输入区域");
        var placeholder = Text("◌", 35, true, "Yanzi.Color.MutedForeground", new Thickness(0, 21, 0, 8));
        placeholder.TextAlignment = TextAlignment.Center;
        conversation.Children.Add(placeholder);
        var welcome = Text("有什么新的想法？", 14, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 7));
        welcome.TextAlignment = TextAlignment.Center;
        conversation.Children.Add(welcome);
        var support = Text("可直接输入一段话，看看聊天卡片的实际效果。", 11, false, "Yanzi.Color.MutedForeground", new Thickness(0, 0, 0, 18));
        support.TextAlignment = TextAlignment.Center;
        conversation.Children.Add(support);
        var chatInput = YanziUi.WithStyle(new TextBox { Text = "", ToolTip = "输入演示消息", MinHeight = 42,
            Margin = new Thickness(0, 0, 0, 10) }, YanziUi.Styles.Input);
        conversation.Children.Add(chatInput);
        var send = SmallButton("发送演示消息  ↗", YanziUi.Styles.DefaultButton,
            () => YanziToast.Show(this, string.IsNullOrWhiteSpace(chatInput.Text) ? "请先输入内容" : "已记录本地演示消息",
                string.IsNullOrWhiteSpace(chatInput.Text) ? YanziToastKind.Info : YanziToastKind.Success));
        send.HorizontalAlignment = HorizontalAlignment.Stretch;
        conversation.Children.Add(send);

        var note = Text("以上均为真实 WPF 控件的交互展示，数据均为演示；主题切换、输入、弹窗、导航都可以直接操作。",
            11, false, "Yanzi.Color.MutedForeground", new Thickness(3, 13, 0, 15));
        _body.Children.Add(note);
    }

    private StackPanel PreviewCard(Panel parent, string? title, string? subtitle = null)
    {
        var border = YanziUi.WithStyle(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 14)
        }, YanziUi.Styles.Card);
        var body = new StackPanel();
        border.Child = body;
        if (!string.IsNullOrWhiteSpace(title))
            body.Children.Add(Text(title, 14, true, "Yanzi.Color.Foreground",
                new Thickness(0, 0, 0, subtitle is null ? 13 : 5)));
        if (!string.IsNullOrWhiteSpace(subtitle))
            body.Children.Add(Text(subtitle, 11, false, "Yanzi.Color.MutedForeground", new Thickness(0, 0, 0, 14)));
        parent.Children.Add(border);
        return body;
    }

    private Button SmallButton(string title, string style, Action action)
    {
        var button = Button(title, style, action);
        var pill = style.StartsWith("Yanzi.Button.Pill", StringComparison.Ordinal);
        button.MinWidth = pill ? 56 : 0;
        button.Height = pill ? 32 : 34;
        button.FontSize = pill ? 14 : 12;
        if (pill) PreviewGeistButton(button);
        button.Padding = pill ? new Thickness(18, 5, 18, 5) : new Thickness(11, 5, 11, 5);
        button.Margin = new Thickness(0, 0, 6, 5);
        return button;
    }

    private static YanziBadge PreviewBadge(string content, YanziBadgeVariant variant) =>
        YanziUi.WithStyle(new YanziBadge
        {
            Content = content, Variant = variant, Margin = new Thickness(0, 0, 7, 5)
        }, YanziUi.Styles.BadgeGeistPreview);

    private static Button PreviewGeistButton(Button button)
    {
        button.SetResourceReference(Control.FontFamilyProperty, "Yanzi.Font.Geist");
        button.SetResourceReference(Control.FontSizeProperty, "Yanzi.Font.PreviewButton");
        button.FontWeight = FontWeights.Medium;
        return button;
    }

    private StackPanel InfoPanel(string eyebrow, string title, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        panel.Children.Add(Text(eyebrow, 10, false, "Yanzi.Color.MutedForeground", new Thickness(0, 0, 0, 7)));
        panel.Children.Add(Text(title, 14, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 6)));
        panel.Children.Add(Text(detail, 11, false, "Yanzi.Color.MutedForeground"));
        return panel;
    }
}
