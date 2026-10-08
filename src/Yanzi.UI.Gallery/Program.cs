using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.Run(new GalleryWindow());
    }
}

/// <summary>
/// Interactive catalog for reviewing actual Yanzi.UI.Wpf controls. This application runs
/// out of process and does not modify the production Yanzi Runtime or installed extensions.
/// </summary>
internal sealed partial class GalleryWindow : Window
{
    private static readonly (string Title, string Subtitle)[] Pages =
    {
        ("总览", "直接体验各类组件的组合效果"),
        ("按钮", "主次操作、危险操作、禁用态"),
        ("输入", "搜索、表单、中文输入和校验"),
        ("选择", "开关、勾选与菜单"),
        ("列表", "列表、选择和右键菜单"),
        ("反馈", "加载、提示和确认"),
        ("设计令牌", "颜色、间距、字号与圆角"),
        ("徽标 Badge", "六种变体、图标和加载状态"),
        ("基础组件", "标签、分割线、键帽、提示与骨架屏"),
        ("表单控件", "选择器、滑块、日期、密码与验证"),
        ("导航与布局", "标签页、面包屑、折叠、分页"),
        ("数据展示", "表格、进度、头像、空状态"),
        ("弹层与反馈", "弹出菜单、Tooltip、Dialog、抽屉"),
        ("全部组件索引", "查看官方组件逐项适配状态"),
        ("评价与记录", "评分和本地保存体验意见")
    };

    private readonly List<Button> _navigation = new();
    private readonly StackPanel _body = new();
    private readonly TextBlock _pageTitle = new();
    private readonly TextBlock _pageSubtitle = new();
    private readonly TextBlock _themeText = new();
    private readonly TextBlock _statusText = new();
    private readonly string _reviewsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "UiReview");
    private int _page;
    private YanziTheme _theme = YanziTheme.Dark;
    private ScrollViewer? _scroll;
    private Slider? _visualRating;
    private Slider? _interactionRating;
    private TextBox? _reviewNotes;

    public GalleryWindow()
    {
        Title = "燕子 UI · 组件评估中心";
        Width = 1180;
        Height = 820;
        MinWidth = 880;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;

        YanziUi.ApplyTo(this, _theme);
        SourceInitialized += (_, _) => GalleryWindowAppearance.Apply(this, _theme);
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Background");
        SetResourceReference(ForegroundProperty, "Yanzi.Color.Foreground");
        Content = BuildShell();
        KeyDown += OnKeyboardShortcut;
        ShowPage(0);
    }

    private UIElement BuildShell()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(226) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(16, 23, 16, 16) };
        sidebar.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Sidebar");
        sidebar.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Brush.Border");
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var sidebarLayout = new DockPanel { LastChildFill = true };
        sidebar.Child = sidebarLayout;

        var brand = new StackPanel { Margin = new Thickness(8, 0, 0, 30) };
        DockPanel.SetDock(brand, Dock.Top);
        var brandLine = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 10, 0) };
        logo.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Primary");
        var logoGlyph = new TextBlock { Text = "燕", FontWeight = FontWeights.Bold, FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        logoGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.PrimaryForeground");
        logo.Child = logoGlyph;
        brandLine.Children.Add(logo);
        brandLine.Children.Add(Text("Yanzi UI", 20, true, "Yanzi.Brush.Text", new Thickness(0, 4, 0, 0)));
        brand.Children.Add(brandLine);
        brand.Children.Add(Text("SHADCN DESIGN  /  v0.4.2", 10, false, "Yanzi.Brush.TextMuted", new Thickness(0, 11, 0, 0)));
        sidebarLayout.Children.Add(brand);

        var footer = new StackPanel { Margin = new Thickness(8, 12, 0, 3) };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(Text("Windows · WPF  /  Preview", 11, false, "Yanzi.Brush.TextMuted"));
        footer.Children.Add(Text("不修改运行中的燕子小程序", 11, false, "Yanzi.Brush.TextSecondary", new Thickness(0, 7, 0, 0)));
        sidebarLayout.Children.Add(footer);

        var nav = new StackPanel();
        var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        navScroll.Resources[typeof(ScrollBar)] = FindResource(YanziUi.Styles.Scrollbar);
        sidebarLayout.Children.Add(navScroll);
        nav.Children.Add(Text("组件目录", 12, true, "Yanzi.Brush.TextMuted", new Thickness(8, 0, 0, 14)));
        for (var i = 0; i < Pages.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = new TextBlock { Text = Pages[i].Title, TextAlignment = TextAlignment.Left, FontSize = 13, FontWeight = FontWeights.Medium },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(14, 0, 0, 0),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            YanziUi.WithStyle(button, YanziUi.Styles.GhostButton);
            button.Click += (_, _) => ShowPage(index);
            _navigation.Add(button);
            nav.Children.Add(button);
        }

        var main = new Grid();
        Grid.SetColumn(main, 1);
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(main);

        var header = new Grid { Margin = new Thickness(32, 26, 34, 24) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(header, 0);
        main.Children.Add(header);

        var titleBlock = new StackPanel();
        _pageTitle.FontSize = 25;
        _pageTitle.FontWeight = FontWeights.SemiBold;
        _pageTitle.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Brush.Text");
        _pageSubtitle.Margin = new Thickness(0, 8, 0, 0);
        _pageSubtitle.FontSize = 12;
        _pageSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Brush.TextSecondary");
        titleBlock.Children.Add(_pageTitle);
        titleBlock.Children.Add(_pageSubtitle);
        header.Children.Add(titleBlock);

        var themeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(themeStack, 1);
        header.Children.Add(themeStack);
        _themeText.VerticalAlignment = VerticalAlignment.Center;
        _themeText.Margin = new Thickness(0, 0, 12, 0);
        _themeText.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Brush.TextSecondary");
        themeStack.Children.Add(_themeText);
        var themeButton = Button("切换主题  ◑", YanziUi.Styles.SecondaryButton, () =>
        {
            _theme = _theme == YanziTheme.Dark ? YanziTheme.Light : YanziTheme.Dark;
            YanziUi.ApplyTo(this, _theme);
            RefreshThemeLabel();
            GalleryWindowAppearance.Apply(this, _theme);
            UpdateNavigation();
        });
        themeStack.Children.Add(themeButton);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(32, 0, 24, 22)
        };
        _scroll.Resources[typeof(ScrollBar)] = FindResource(YanziUi.Styles.Scrollbar);
        Grid.SetRow(_scroll, 1);
        main.Children.Add(_scroll);
        _scroll.Content = _body;

        var statusBar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(32, 11, 24, 11) };
        statusBar.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Brush.Border");
        Grid.SetRow(statusBar, 2);
        main.Children.Add(statusBar);
        var statusRow = new DockPanel();
        statusBar.Child = statusRow;
        _statusText.Text = "就绪 · 可以开始体验组件";
        _statusText.FontSize = 11;
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Brush.TextSecondary");
        statusRow.Children.Add(_statusText);
        var hint = Text("Ctrl+1～9 切换 · 更多在左侧目录", 11, false, "Yanzi.Brush.TextMuted");
        DockPanel.SetDock(hint, Dock.Right);
        statusRow.Children.Add(hint);

        RefreshThemeLabel();
        return root;
    }

    private void RefreshThemeLabel() => _themeText.Text = _theme == YanziTheme.Dark ? "● 深色" : "○ 浅色";

    private void UpdateNavigation()
    {
        for (var i = 0; i < _navigation.Count; i++)
        {
            var button = _navigation[i];
            button.FontWeight = _page == i ? FontWeights.SemiBold : FontWeights.Normal;
            button.SetResourceReference(Control.BackgroundProperty, _page == i ? "Yanzi.Color.SidebarAccent" : "Yanzi.Color.Transparent");
            button.SetResourceReference(Control.ForegroundProperty, _page == i ? "Yanzi.Color.SidebarForeground" : "Yanzi.Color.MutedForeground");
        }
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, Pages.Length - 1);
        _pageTitle.Text = Pages[_page].Title;
        _pageSubtitle.Text = Pages[_page].Subtitle;
        _visualRating = null;
        _interactionRating = null;
        _reviewNotes = null;
        _body.Children.Clear();
        UpdateNavigation();
        _statusText.Text = $"正在浏览：{Pages[_page].Title} · 所有操作均为可撤销或模拟测试";
        switch (_page)
        {
            case 0: Overview(); break;
            case 1: Buttons(); break;
            case 2: Inputs(); break;
            case 3: Selections(); break;
            case 4: Lists(); break;
            case 5: Feedback(); break;
            case 6: Tokens(); break;
            case 7: Badges(); break;
            case 8: BasicsCatalog(); break;
            case 9: FormsCatalog(); break;
            case 10: NavigationCatalog(); break;
            case 11: DataCatalog(); break;
            case 12: OverlayCatalog(); break;
            case 13: ComponentsIndex(); break;
            case 14: Review(); break;
        }
        if (_scroll != null) _scroll.ScrollToTop();
    }

    private void Buttons()
    {
        var section = Card("按钮 / Button", "比较主按钮、次要按钮和危险操作。鼠标悬停、点击、Tab 聚焦和禁用态均可测试。");
        var row = Row();
        var primary = Button("保存更改", YanziUi.Styles.PrimaryButton,
            () => { Status("保存成功（演示）"); YanziToast.Show(this, "操作完成 · 成功", YanziToastKind.Success); });
        var secondary = Button("取消", YanziUi.Styles.SecondaryButton, () => Status("已取消"));
        var danger = Button("删除记录", YanziUi.Styles.DangerButton,
            () => { var confirmed = YanziDialog.Confirm(this, "确定删除吗？", "演示环境中不会真正删除任何文件。", "确认删除", true); Status(confirmed ? "已确认删除（演示）" : "用户取消删除"); });
        row.Children.Add(primary);
        row.Children.Add(secondary);
        row.Children.Add(danger);
        section.Children.Add(row);

        var toggle = YanziUi.WithStyle(new CheckBox { Content = "禁用上方三个按钮", Margin = new Thickness(0, 20, 0, 0) }, YanziUi.Styles.CheckBox);
        toggle.Checked += (_, _) => { primary.IsEnabled = secondary.IsEnabled = danger.IsEnabled = false; Status("按钮禁用态"); };
        toggle.Unchecked += (_, _) => { primary.IsEnabled = secondary.IsEnabled = danger.IsEnabled = true; Status("按钮已恢复"); };
        section.Children.Add(toggle);

        var sample = Card("设计约定", "高频操作只保留一个主按钮，危险操作使用红色强调；焦点、禁用和点击反馈来自公共组件，而非每个小程序自行实现。");
        sample.Children.Add(CodeKey("Yanzi.Button.Default / Outline / Secondary / Ghost / Destructive / Link"));

        var variants = Card("shadcn 按钮变体", "官方六种语义变体：Default、Outline、Secondary、Ghost、Destructive、Link。");
        var wrap = new WrapPanel();
        foreach (var (label, style) in new[]
        {
            ("Default", YanziUi.Styles.DefaultButton),
            ("Outline", YanziUi.Styles.OutlineButton),
            ("Secondary", YanziUi.Styles.SecondaryButton),
            ("Ghost", YanziUi.Styles.GhostButton),
            ("Destructive", YanziUi.Styles.DestructiveButton),
            ("Link", YanziUi.Styles.LinkButton)
        })
        {
            var localLabel = label;
            var button = Button(localLabel, style, () => Status("Variant: " + localLabel));
            button.Margin = new Thickness(0, 0, 8, 10);
            wrap.Children.Add(button);
        }
        variants.Children.Add(wrap);
    }

    private void Inputs()
    {
        var section = Card("输入框 / Input", "编辑、选中、复制、粘贴、撤销和中文输入法。Tab 可以在组件间切换。");
        var search = YanziUi.WithStyle(new TextBox { Text = "", Tag = "搜索", Margin = new Thickness(0, 2, 0, 13), ToolTip = "输入要搜索的小程序名称" }, YanziUi.Styles.Input);
        var label = Text("在下方输入，可以实时看到字符数量：", 12, false, "Yanzi.Brush.TextSecondary", new Thickness(0, 0, 0, 8));
        section.Children.Add(label);
        section.Children.Add(search);
        var counter = Text("当前 0 个字符", 12, false, "Yanzi.Brush.TextSecondary", new Thickness(0, 2, 0, 0));
        search.TextChanged += (_, _) => counter.Text = $"当前 {search.Text.Length} 个字符";
        section.Children.Add(counter);

        var edit = Card("多行输入 / 编辑能力", "验证自动换行、多行滚动、IME，以及 Ctrl+A / Ctrl+Z 等常见编辑快捷键。");
        var multiline = YanziUi.WithStyle(new TextBox
        {
            Text = "这是一段用于测试多行输入的文字。\n试着输入中文，或者粘贴一段较长内容。",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 128
        }, YanziUi.Styles.Input);
        edit.Children.Add(multiline);
        edit.Children.Add(CodeKey("Yanzi.Input"));

        var validation = Card("基础校验 / 演示", "验证按钮依据内容是否为空给出明确提示。");
        var input = YanziUi.WithStyle(new TextBox { Margin = new Thickness(0, 0, 0, 14) }, YanziUi.Styles.Input);
        validation.Children.Add(input);
        validation.Children.Add(Button("检查内容", YanziUi.Styles.PrimaryButton,
            () => { var ok = !string.IsNullOrWhiteSpace(input.Text); YanziToast.Show(this, ok ? "内容有效" : "请输入内容", ok ? YanziToastKind.Success : YanziToastKind.Error); Status(ok ? "输入校验成功" : "输入校验失败"); }));
    }

    private void Selections()
    {
        var section = Card("开关 / Switch", "真实 CheckBox 实现，鼠标和空格键都应该可以切换。");
        var s1 = YanziUi.WithStyle(new CheckBox { Content = "允许后台同步", IsChecked = true, Margin = new Thickness(0, 5, 0, 12) }, YanziUi.Styles.Toggle);
        s1.Checked += (_, _) => Status("后台同步已开启（演示）");
        s1.Unchecked += (_, _) => Status("后台同步已关闭（演示）");
        section.Children.Add(s1);
        section.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "启用截图识别", IsChecked = true }, YanziUi.Styles.Toggle));
        section.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "禁用的开关", IsChecked = false, IsEnabled = false, Margin = new Thickness(0, 12, 0, 0) }, YanziUi.Styles.Toggle));

        var checklist = Card("多选与单选", "表单通过原生选择控件完成操作，保证系统级键盘行为。");
        checklist.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "自动启动", IsChecked = true }, YanziUi.Styles.CheckBox));
        checklist.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "保留操作日志" }, YanziUi.Styles.CheckBox));
        var radioRow = Row();
        radioRow.Margin = new Thickness(0, 12, 0, 0);
        radioRow.Children.Add(new RadioButton { Content = "紧凑", GroupName = "density", Foreground = ResolveBrush("Yanzi.Brush.Text"), IsChecked = true, Margin = new Thickness(0, 0, 18, 0) });
        radioRow.Children.Add(new RadioButton { Content = "舒适", GroupName = "density", Foreground = ResolveBrush("Yanzi.Brush.Text") });
        checklist.Children.Add(radioRow);
        checklist.Children.Add(CodeKey("Yanzi.Toggle / Yanzi.CheckBox"));

        var toggleDemo = Card("Toggle / 单独切换", "不同于 Switch，此处为可切换的工具按钮。点击后维持选中状态。");
        var toggleButton = YanziUi.WithStyle(new ToggleButton { Content = "加粗", Width = 86,
            HorizontalAlignment = HorizontalAlignment.Left }, YanziUi.Styles.ToggleButton);
        toggleButton.Checked += (_, _) => Status("工具按钮：已选择加粗");
        toggleButton.Unchecked += (_, _) => Status("工具按钮：已取消加粗");
        toggleDemo.Children.Add(toggleButton);

        var menu = Card("上下文菜单", "右键下方按钮，测试菜单打开、焦点和快捷键操作。");
        var target = Button("右键点击我  ···", YanziUi.Styles.SecondaryButton, () => Status("点击了菜单演示按钮"));
        var contextMenu = YanziUi.WithStyle(new ContextMenu(), YanziUi.Styles.Menu);
        foreach (var title in new[] { "复制", "重命名", "删除" })
        {
            var item = YanziUi.WithStyle(new MenuItem { Header = title }, YanziUi.Styles.MenuItem);
            item.Click += (_, _) => Status($"选择菜单：{title}（演示）");
            contextMenu.Items.Add(item);
        }
        target.ContextMenu = contextMenu;
        menu.Children.Add(target);
        menu.Children.Add(CodeKey("Yanzi.Menu / Yanzi.MenuItem"));
    }

    private void Lists()
    {
        var section = Card("列表 / ListItem", "点击条目并右键，观察悬停、选中和滚动；列表通过公共样式启用虚拟化。");
        var list = YanziUi.WithStyle(new ListBox { Height = 270, Margin = new Thickness(0, 7, 0, 0) }, YanziUi.Styles.List);
        foreach (var name in new[] { "剪贴板", "日历", "延时关机", "截图识别", "灵感白板", "笔记", "相册", "智能搜索", "快捷指令", "待办事项", "窗口管理", "文件搜索" })
            list.Items.Add(name);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string s) Status("已选中：" + s);
        };
        var menu = YanziUi.WithStyle(new ContextMenu(), YanziUi.Styles.Menu);
        var copy = YanziUi.WithStyle(new MenuItem { Header = "复制项目名称" }, YanziUi.Styles.MenuItem);
        copy.Click += (_, _) =>
        {
            if (list.SelectedItem is string selected)
            {
                Clipboard.SetText(selected);
                Status("已复制：" + selected);
            }
        };
        menu.Items.Add(copy);
        list.ContextMenu = menu;
        section.Children.Add(list);
        section.Children.Add(CodeKey("Yanzi.List / Yanzi.ListItem"));

        var note = Card("性能约定", "实际海量记录场景仍需按需加载、数据虚拟化与滚动性能基准测试。展示页只用于交互和视觉验收。");
        note.Children.Add(Paragraph("选中态必须比悬停态明确，同时保留键盘方向键切换、Tab 焦点以及禁用态。"));
    }

    private void Feedback()
    {
        var load = Card("加载 / Loading", "圆环与进度条用于真实的处理中状态；不能把“请求已发送”当成“任务已成功”。");
        var ringRow = Row();
        ringRow.VerticalAlignment = VerticalAlignment.Center;
        ringRow.Children.Add(new YanziLoadingRing { Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) });
        ringRow.Children.Add(Text("识别中，请稍候…", 13, false, "Yanzi.Brush.Text"));
        load.Children.Add(ringRow);
        var progress = YanziUi.WithStyle(new ProgressBar { Margin = new Thickness(0, 20, 0, 8) }, YanziUi.Styles.Loading);
        load.Children.Add(progress);
        load.Children.Add(CodeKey("YanziLoadingRing / Yanzi.Loading"));

        var notifications = Card("提示 / Toast", "默认短暂显示后自动消失。成功、失败、普通提示应该使用不同语义，而不是随意写死颜色。");
        var buttons = Row();
        buttons.Children.Add(Button("成功提示", YanziUi.Styles.PrimaryButton, () => YanziToast.Show(this, "保存成功", YanziToastKind.Success)));
        buttons.Children.Add(Button("失败提示", YanziUi.Styles.DangerButton, () => YanziToast.Show(this, "识别失败，可以重试", YanziToastKind.Error)));
        buttons.Children.Add(Button("普通消息", YanziUi.Styles.SecondaryButton, () => YanziToast.Show(this, "正在使用组件库预览", YanziToastKind.Info)));
        notifications.Children.Add(buttons);
        notifications.Children.Add(CodeKey("YanziToast.Show(...)"));

        var dialog = Card("确认弹窗 / Dialog", "危险操作确认必须有明确的取消与确认路径，并正确关联父窗口。");
        dialog.Children.Add(Button("打开删除确认", YanziUi.Styles.DangerButton, () =>
        {
            var result = YanziDialog.Confirm(this, "删除这个项目？", "本次仅用于体验，不会删除文件。按 Esc 可以取消。", "确认删除", true);
            Status(result ? "已确认（模拟）" : "已取消");
        }));
        dialog.Children.Add(CodeKey("YanziDialog.Confirm(...)"));
    }

    private void Tokens()
    {
        var section = Card("设计令牌 / Design Tokens", "所有基础语义色都能随右上角的深浅主题即时切换。");
        var swatches = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), ItemWidth = 155 };
        foreach (var (key, label) in new[]
        {
            ("Primary", "主色"), ("Secondary", "次要"), ("Background", "背景"),
            ("Card", "卡片"), ("Foreground", "前景"), ("MutedForeground", "弱化文字"),
            ("Accent", "悬停"), ("Border", "边框"), ("Ring", "焦点环"),
            ("Destructive", "危险"), ("Success", "成功"), ("Warning", "警示")
        })
        {
            var item = new StackPanel { Width = 146, Margin = new Thickness(0, 0, 8, 20) };
            var swatch = new Border { Height = 54, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
            swatch.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color." + key);
            swatch.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Brush.Border");
            item.Children.Add(swatch);
            item.Children.Add(Text(label, 12, true, "Yanzi.Brush.Text", new Thickness(0, 9, 0, 0)));
            item.Children.Add(Text("Color." + key, 10, false, "Yanzi.Brush.TextMuted", new Thickness(0, 4, 0, 0)));
            swatches.Children.Add(item);
        }
        section.Children.Add(swatches);

        var typography = Card("字体与节奏 / Typography & Spacing", "统一字号、间距和圆角，并允许复杂业务拥有自己的特殊视觉组件。");
        typography.Children.Add(Text("大标题  /  18", 18, true, "Yanzi.Brush.Text", new Thickness(0, 4, 0, 14)));
        typography.Children.Add(Text("正文示例  /  13px  ·  中文与 English 混排", 13, false, "Yanzi.Brush.Text"));
        typography.Children.Add(Text("辅助说明  /  12px", 12, false, "Yanzi.Brush.TextSecondary", new Thickness(0, 10, 0, 0)));
        typography.Children.Add(Paragraph("间距：4、8、12、16、24    圆角：sm 4 / md 6 / lg 8 / xl 12"));
    }

    private void Badges()
    {
        var section = Card("Badge / 六种变体",
            "参照 shadcn/ui 官方 Badge 页面：Default、Secondary、Destructive、Outline、Ghost、Link。");
        var variants = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
        foreach (var variant in Enum.GetValues<YanziBadgeVariant>())
        {
            variants.Children.Add(new YanziBadge
            {
                Content = variant.ToString(),
                Variant = variant,
                Margin = new Thickness(0, 0, 10, 10)
            });
        }
        section.Children.Add(variants);
        section.Children.Add(CodeKey("YanziBadge.Variant / Yanzi.Badge"));

        var icons = Card("图标和加载状态", "前后图标、生成中 Spinner 和状态指示共用同一套 Badge 模板。");
        var iconRow = new WrapPanel();
        iconRow.Children.Add(new YanziBadge
            { Content = "Verified", Variant = YanziBadgeVariant.Secondary, LeadingIcon = "✓",
                Margin = new Thickness(0, 0, 10, 10) });
        iconRow.Children.Add(new YanziBadge
            { Content = "Bookmark", Variant = YanziBadgeVariant.Outline, TrailingIcon = "↗",
                Margin = new Thickness(0, 0, 10, 10) });
        iconRow.Children.Add(new YanziBadge
            { Content = "Generating", Variant = YanziBadgeVariant.Secondary, IsLoading = true,
                Margin = new Thickness(0, 0, 10, 10) });
        iconRow.Children.Add(new YanziBadge
            { Content = "Deleting", Variant = YanziBadgeVariant.Destructive, IsLoading = true,
                Margin = new Thickness(0, 0, 10, 10) });
        icons.Children.Add(iconRow);

        var live = Card("实时调节", "修改下拉框中的变体，观察同一个实例的外观变化；切换主题也会同步更新。");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var selection = new ComboBox
        {
            Width = 172,
            Height = 34,
            Margin = new Thickness(0, 0, 20, 0),
            ItemsSource = Enum.GetValues<YanziBadgeVariant>(),
            SelectedIndex = 0
        };
        var preview = new YanziBadge { Content = "Preview" };
        selection.SelectionChanged += (_, _) =>
        {
            if (selection.SelectedItem is YanziBadgeVariant variant)
                preview.Variant = variant;
        };
        row.Children.Add(selection);
        row.Children.Add(preview);
        live.Children.Add(row);
        var loadingToggle = YanziUi.WithStyle(new CheckBox
        {
            Content = "显示加载状态",
            Margin = new Thickness(0, 17, 0, 0)
        }, YanziUi.Styles.CheckBox);
        loadingToggle.Checked += (_, _) => preview.IsLoading = true;
        loadingToggle.Unchecked += (_, _) => preview.IsLoading = false;
        live.Children.Add(loadingToggle);
    }

    private void Review()
    {
        var intro = Card("请给当前版本打分", "评价仅保存在这台电脑本地，不会上传、发送或影响燕子现有数据。");
        _visualRating = RatingSlider(intro, "视觉统一程度", 4);
        _interactionRating = RatingSlider(intro, "交互易用程度", 4);
        intro.Children.Add(Text("还想修改哪些地方？", 12, true, "Yanzi.Brush.Text", new Thickness(0, 17, 0, 8)));
        _reviewNotes = YanziUi.WithStyle(new TextBox
        {
            Height = 116,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "例如：列表选中太亮、按钮圆角太大、输入框偏小……"
        }, YanziUi.Styles.Input);
        intro.Children.Add(_reviewNotes);
        var actions = Row();
        actions.Margin = new Thickness(0, 20, 0, 0);
        actions.Children.Add(Button("保存评价", YanziUi.Styles.PrimaryButton, SaveReview));
        actions.Children.Add(Button("复制评价摘要", YanziUi.Styles.SecondaryButton, () =>
        {
            Clipboard.SetText(BuildReviewText());
            Status("评价摘要已复制到剪贴板");
            YanziToast.Show(this, "已复制评价摘要", YanziToastKind.Success);
        }));
        intro.Children.Add(actions);

        var storage = Card("评价存储位置", "开发时可以直接读取这份反馈文件，将你的体验转化为下一轮设计要求。");
        storage.Children.Add(Text(_reviewsDirectory, 12, false, "Yanzi.Brush.Text", new Thickness(0, 4, 0, 10)));
        storage.Children.Add(Button("打开评价目录", YanziUi.Styles.SecondaryButton, () =>
        {
            Directory.CreateDirectory(_reviewsDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _reviewsDirectory,
                UseShellExecute = true
            });
        }));
    }

    private Slider RatingSlider(Panel owner, string title, double initial)
    {
        var titleRow = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        titleRow.Children.Add(Text(title, 13, true, "Yanzi.Brush.Text"));
        var value = Text(initial.ToString("0") + " / 5", 12, true, "Yanzi.Brush.Accent");
        DockPanel.SetDock(value, Dock.Right);
        titleRow.Children.Add(value);
        owner.Children.Add(titleRow);
        var slider = new Slider
        {
            Minimum = 1,
            Maximum = 5,
            Value = initial,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            AutoToolTipPlacement = AutoToolTipPlacement.TopLeft,
            AutoToolTipPrecision = 0,
            Margin = new Thickness(2, 10, 2, 5)
        };
        slider.ValueChanged += (_, _) => value.Text = $"{slider.Value:0} / 5";
        owner.Children.Add(slider);
        return slider;
    }

    private void SaveReview()
    {
        try
        {
            Directory.CreateDirectory(_reviewsDirectory);
            var data = new
            {
                version = "0.4.2",
                time = DateTimeOffset.Now,
                visual = (int)(_visualRating?.Value ?? 4),
                interaction = (int)(_interactionRating?.Value ?? 4),
                theme = _theme.ToString(),
                comments = _reviewNotes?.Text ?? ""
            };
            var path = Path.Combine(_reviewsDirectory, $"review-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            Status("评价已保存在本机：" + Path.GetFileName(path));
            YanziToast.Show(this, "评价已保存到本机", YanziToastKind.Success);
        }
        catch (Exception ex)
        {
            Status("保存评价失败：" + ex.Message);
            YanziToast.Show(this, "评价保存失败：" + ex.Message, YanziToastKind.Error);
        }
    }

    private string BuildReviewText() =>
        $"Yanzi UI v0.1.0 评估\n主题：{(_theme == YanziTheme.Dark ? "深色" : "浅色")}\n视觉：{_visualRating?.Value ?? 4:0}/5\n交互：{_interactionRating?.Value ?? 4:0}/5\n改进建议：\n{_reviewNotes?.Text ?? ""}";

    private StackPanel Card(string title, string subtitle)
    {
        var root = YanziUi.WithStyle(new Border
        {
            Margin = new Thickness(0, 0, 4, 18),
            HorizontalAlignment = HorizontalAlignment.Stretch
        }, YanziUi.Styles.Card);
        var layout = new StackPanel();
        root.Child = layout;
        layout.Children.Add(Text(title, 16, true, "Yanzi.Brush.Text"));
        layout.Children.Add(Text(subtitle, 12, false, "Yanzi.Brush.TextSecondary", new Thickness(0, 9, 0, 18)));
        _body.Children.Add(root);
        return layout;
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal };

    private static TextBlock Text(string content, double fontSize, bool bold, string brush, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = content,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static TextBlock Paragraph(string content) =>
        Text(content, 12, false, "Yanzi.Brush.TextSecondary", new Thickness(0, 0, 0, 13));

    private UIElement CodeKey(string text)
    {
        var action = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        action.Children.Add(Text(text, 11, false, "Yanzi.Brush.TextMuted", new Thickness(0, 0, 14, 0)));
        var copy = Button("复制标识", YanziUi.Styles.SecondaryButton, () =>
        {
            Clipboard.SetText(text);
            Status("已复制：" + text);
        });
        copy.Height = 26;
        copy.FontSize = 11;
        action.Children.Add(copy);
        return action;
    }

    private Button Button(string label, string style, Action onClick)
    {
        var button = YanziUi.WithStyle(new Button
        {
            Content = label,
            Height = 35,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand
        }, style);
        button.Click += (_, _) => onClick();
        return button;
    }

    private Brush ResolveBrush(string key) => TryFindResource(key) as Brush ?? Brushes.White;

    private void Status(string message) => _statusText.Text = message;

    private void OnKeyboardShortcut(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            ShowPage(e.Key - Key.D1);
            e.Handled = true;
        }
    }
}
