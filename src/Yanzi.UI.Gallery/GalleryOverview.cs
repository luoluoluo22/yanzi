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
            Margin = new Thickness(8, 12, 8, 34),
            MaxWidth = 690
        };
        _body.Children.Add(hero);
        var tag = new YanziBadge
        {
            Content = "Reusable desktop components  ↗",
            Variant = YanziBadgeVariant.Secondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        };
        hero.Children.Add(tag);
        var headline = Text("The foundation for Yanzi UI", 27, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 11));
        headline.TextAlignment = TextAlignment.Center;
        hero.Children.Add(headline);
        var description = Text("可组合、可扩展的 Windows 原生组件。直接操作下面的按钮、输入框、菜单和表单，观察真实的视觉与交互。", 13, false,
            "Yanzi.Color.MutedForeground", new Thickness(8, 0, 8, 20));
        description.TextAlignment = TextAlignment.Center;
        hero.Children.Add(description);
        var heroActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        heroActions.Children.Add(Button("浏览组件  →", YanziUi.Styles.PillDefaultButton, () => ShowPage(8)));
        heroActions.Children.Add(Button("视觉规范", YanziUi.Styles.PillOutlineButton, () => ShowPage(6)));
        hero.Children.Add(heroActions);

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
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        var search = YanziUi.WithStyle(new TextBox
        {
            ToolTip = "Search components",
            Text = "",
            Height = 38,
            Padding = new Thickness(11, 7, 30, 7)
        }, YanziUi.Styles.InputSoft);
        searchGrid.Children.Add(search);
        var searchHint = Text("Name", 12, false, "Yanzi.Color.MutedForeground");
        searchHint.Margin = new Thickness(13, 0, 30, 0);
        searchHint.HorizontalAlignment = HorizontalAlignment.Left;
        searchHint.IsHitTestVisible = false;
        search.TextChanged += (_, _) => searchHint.Visibility =
            string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
        searchGrid.Children.Add(searchHint);
        var searchIcon = Text("⌕", 19, false, "Yanzi.Color.MutedForeground");
        searchIcon.HorizontalAlignment = HorizontalAlignment.Right;
        searchIcon.Margin = new Thickness(0, 0, 13, 0);
        searchIcon.IsHitTestVisible = false;
        searchGrid.Children.Add(searchIcon);
        sampler.Children.Add(searchGrid);

        sampler.Children.Add(YanziUi.WithStyle(new TextBox
        {
            Text = "Message",
            Height = 78,
            Padding = new Thickness(12, 10, 12, 10),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, YanziUi.Styles.TextareaSoft));
        var tags = new WrapPanel { Margin = new Thickness(0, 12, 0, 11) };
        tags.Children.Add(PreviewBadge("Badge", YanziBadgeVariant.Default));
        tags.Children.Add(PreviewBadge("Secondary", YanziBadgeVariant.Secondary));
        tags.Children.Add(PreviewBadge("Outline", YanziBadgeVariant.Outline));
        sampler.Children.Add(tags);
        var toggles = new StackPanel { Orientation = Orientation.Horizontal };
        toggles.Children.Add(YanziUi.WithStyle(new CheckBox { IsChecked = true, ToolTip = "Enabled", Margin = new Thickness(0, 0, 9, 0) }, YanziUi.Styles.Toggle));
        toggles.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "Notify", IsChecked = true }, YanziUi.Styles.CheckBox));
        sampler.Children.Add(toggles);
        var footButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        footButtons.Children.Add(SmallButton("Alert Dialog", YanziUi.Styles.PillOutlineButton,
            () => YanziDialog.Confirm(this, "确认执行操作？", "这是组件展示，不会修改任何实际数据。", "确认")));
        var segmented = new YanziSegmentedButtonGroup { Margin = new Thickness(0, 0, 6, 5) };
        segmented.Add("Button Group", () => Status("Button Group 被点击"));
        var menuButton = segmented.Add("⌃", () => { });
        var menu = YanziUi.WithStyle(new ContextMenu(), YanziUi.Styles.Menu);
        foreach (var text in new[] { "复制", "查看详情", "设置" })
        {
            var copy = text;
            var item = YanziUi.WithStyle(new MenuItem { Header = text }, YanziUi.Styles.MenuItem);
            item.Click += (_, _) => Status("菜单：" + copy);
            menu.Items.Add(item);
        }
        menuButton.ContextMenu = menu;
        menuButton.Click += (_, _) => menu.IsOpen = true;
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
        button.MinWidth = pill ? 96 : 0;
        button.Height = pill ? 32 : 34;
        button.FontSize = 12;
        button.Padding = pill ? new Thickness(18, 5, 18, 5) : new Thickness(11, 5, 11, 5);
        button.Margin = new Thickness(0, 0, 6, 5);
        return button;
    }

    private static YanziBadge PreviewBadge(string content, YanziBadgeVariant variant) =>
        new() { Content = content, Variant = variant, Margin = new Thickness(0, 0, 7, 5) };

    private StackPanel InfoPanel(string eyebrow, string title, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        panel.Children.Add(Text(eyebrow, 10, false, "Yanzi.Color.MutedForeground", new Thickness(0, 0, 0, 7)));
        panel.Children.Add(Text(title, 14, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 6)));
        panel.Children.Add(Text(detail, 11, false, "Yanzi.Color.MutedForeground"));
        return panel;
    }
}
