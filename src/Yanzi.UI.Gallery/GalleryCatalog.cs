using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

internal sealed partial class GalleryWindow
{
    private void BasicsCatalog()
    {
        var baseCard = Card("基础内容 / Primitives", "所有基础组件来自 Yanzi.UI.Wpf 公共库，可在原生 Windows 小程序里复用。");
        var wrap = new WrapPanel();
        wrap.Children.Add(YanziPrimitives.Avatar("YZ", 44));
        wrap.Children.Add(YanziPrimitives.Avatar("AI", 44));
        wrap.Children.Add(YanziPrimitives.Avatar("DEV", 44));
        wrap.Children.Add(YanziPrimitives.Kbd("␣ 空格"));
        wrap.Children.Add(YanziPrimitives.Kbd("↵ 回车"));
        wrap.Children.Add(YanziPrimitives.Kbd("⇥ 制表"));
        wrap.Children.Add(YanziPrimitives.KbdGroup("Ctrl", "+", "B"));
        foreach (FrameworkElement element in wrap.Children)
            element.Margin = new Thickness(0, 0, 12, 10);
        baseCard.Children.Add(wrap);
        baseCard.Children.Add(YanziPrimitives.Separator());
        baseCard.Children.Add(Text("Alert  •  Informational", 12, true, "Yanzi.Color.Foreground", new Thickness(0, 9, 0, 9)));
        baseCard.Children.Add(YanziPrimitives.Alert("已连接到燕子", "组件状态采用语义化提示，而不是硬编码成功颜色。"));
        baseCard.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        baseCard.Children.Add(YanziPrimitives.Alert("危险操作", "删除或覆盖前必须给用户清晰的确认机会。", true));

        var skeleton = Card("Skeleton / Empty / Typography", "加载占位、空白状态、标题与辅助文字组合。");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        row.Children.Add(YanziPrimitives.Skeleton(44, 44));
        var lines = new StackPanel { Margin = new Thickness(13, 4, 0, 0) };
        lines.Children.Add(YanziPrimitives.Skeleton(185, 12));
        lines.Children.Add(new Border { Height = 9 });
        lines.Children.Add(YanziPrimitives.Skeleton(128, 11));
        row.Children.Add(lines);
        skeleton.Children.Add(row);
        skeleton.Children.Add(YanziPrimitives.EmptyState("目前没有记录", "你可以点击添加按钮创建第一项。"));
        skeleton.Children.Add(Button("创建新项目", YanziUi.Styles.OutlineButton,
            () => YanziToast.Show(this, "演示：创建新项目")));

        var typography = Card("标签、分割线、键盘提示 / Label & Kbd", "文字依照层次分组；屏幕上的语义颜色可随主题切换。");
        typography.Children.Add(Text("Typography h1", 25, true, "Yanzi.Color.Foreground"));
        typography.Children.Add(Text("Typography h2", 19, true, "Yanzi.Color.Foreground", new Thickness(0, 12, 0, 6)));
        typography.Children.Add(Text("Body text / 正文字体", 14, false, "Yanzi.Color.Foreground"));
        typography.Children.Add(Text("Muted / 次要说明文字", 12, false, "Yanzi.Color.MutedForeground", new Thickness(0, 9, 0, 0)));
        typography.Children.Add(YanziPrimitives.Separator());
        typography.Children.Add(new YanziBadge { Content = "Component", Variant = YanziBadgeVariant.Outline });

        var imageDemo = Card("Aspect Ratio / 宽高比例", "固定 16:9 的内容比例容器，适合预览图和媒体缩略图。");
        var fill = new Border();
        fill.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        fill.Child = Text("16 : 9", 20, true, "Yanzi.Color.Foreground");
        imageDemo.Children.Add(YanziLayoutPrimitives.AspectRatio(fill, 16.0 / 9.0, 265));

        var waiting = Card("更多组件状态", "完整 shadcn 清单中的特殊组件按能力逐步适配，未实现的不伪装成可用接口。");
        waiting.Children.Add(Text("Message Scroller · Questionnaire · 暂未支持的专业组件会保留在索引中",
            12, false, "Yanzi.Color.MutedForeground"));
    }

    private void FormsCatalog()
    {
        var form = Card("Field / Input / Input Group / Textarea", "输入值和错误反馈可实际交互；不要只是静态截图。");
        var name = YanziUi.WithStyle(new TextBox { Text = "Yanzi Desktop" }, YanziUi.Styles.Input);
        form.Children.Add(YanziPrimitives.Field("名称", name, "将在本机界面中显示的名称"));
        var description = YanziUi.WithStyle(new TextBox { Text = "支持多行编辑和中文输入法", Height = 90 }, YanziUi.Styles.Textarea);
        form.Children.Add(YanziPrimitives.Field("描述", description));
        var pwd = YanziUi.WithStyle(new PasswordBox(), YanziUi.Styles.Password);
        System.Windows.Automation.AutomationProperties.SetName(pwd, "Password input");
        form.Children.Add(YanziPrimitives.Field("密码 / Password", pwd, "这里只演示密码输入，不会保存到磁盘"));
        form.Children.Add(Button("验证表单", YanziUi.Styles.DefaultButton, () =>
        {
            var valid = !string.IsNullOrWhiteSpace(name.Text);
            YanziToast.Show(this, valid ? "表单输入有效" : "名称不能为空", valid ? YanziToastKind.Success : YanziToastKind.Error);
        }));

        var choices = Card("Select / Combobox / Radio Group / Checkbox / Switch", "选择器、组合框、单选、多选和开关使用真实 WPF 控件行为。");
        var select = YanziUi.WithStyle(new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 15) }, YanziUi.Styles.Select);
        foreach (var label in new[] { "深色主题", "浅色主题", "跟随系统" }) select.Items.Add(label);
        select.SelectedIndex = 0;
        choices.Children.Add(YanziPrimitives.Field("选择主题", select));
        var combo = YanziUi.WithStyle(new ComboBox
        {
            Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            IsEditable = true, IsTextSearchEnabled = true, Margin = new Thickness(0, 0, 0, 12)
        }, YanziUi.Styles.Select);
        foreach (var label in new[] { "剪贴板", "截图", "日历", "白板", "浏览器" }) combo.Items.Add(label);
        choices.Children.Add(YanziPrimitives.Field("搜索并选择小程序", combo, "可直接键入内容查找选项"));
        var radios = new YanziRadioGroup { Orientation = Orientation.Horizontal };
        radios.Add("紧凑", "compact");
        radios.Add("标准", "default");
        radios.Add("宽松", "comfortable");
        radios.Select("default");
        radios.SelectionChanged += (_, value) => Status("表单密度：" + value);
        choices.Children.Add(radios);
        var disabledRadio = new YanziRadioGroup();
        disabledRadio.Add("禁用状态 / Disabled", "disabled", isEnabled: false);
        choices.Children.Add(disabledRadio);
        choices.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "启动时自动运行", IsChecked = true }, YanziUi.Styles.CheckBox));
        choices.Children.Add(YanziUi.WithStyle(new CheckBox { Content = "启用通知", IsChecked = false }, YanziUi.Styles.SwitchShadcn));

        var wizard = Card("Questionnaire / 问卷向导", "真实的多步骤问卷，支持输入、单选、上一步和完成回调。");
        var survey = new YanziQuestionnaire(new[]
        {
            new YanziQuestion("topic", "你最关心哪个组件？", YanziQuestionKind.Choice,
                new[] { "Button", "Input", "Badge", "Dialog" }),
            new YanziQuestion("details", "希望如何改进它？", YanziQuestionKind.Text, Required: false),
            new YanziQuestion("agree", "是否保留深色模式？", YanziQuestionKind.YesNo)
        });
        survey.Completed += (_, answers) =>
            YanziToast.Show(this, $"已填写 {answers.Count} 项问卷内容（仅保存在内存）", YanziToastKind.Success);
        wizard.Children.Add(survey);

        var inputGroupCard = Card("Input Group / Button Group", "输入附属文字与组合按钮均来自公共库，而不是页面内的临时代码。");
        var url = new YanziInputGroup("https://", ".com");
        url.Input.Text = "yanzi";
        inputGroupCard.Children.Add(url);
        var buttons = new YanziButtonGroup { Margin = new Thickness(0, 13, 0, 0) };
        buttons.Add("复制", () => Status("已选择复制（演示）"));
        buttons.Add("编辑", () => Status("已选择编辑（演示）"));
        buttons.Add("更多", () => Status("已选择更多（演示）"));
        inputGroupCard.Children.Add(buttons);

        var values = Card("Slider / Date Picker / Calendar / Progress", "调节数值并观察标签更新。日期选择尊重系统区域格式。");
        var sliderValue = Text("65%", 13, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 8));
        values.Children.Add(sliderValue);
        var slider = YanziUi.WithStyle(new Slider { Minimum = 0, Maximum = 100, Value = 65, TickFrequency = 5,
            IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 18) }, YanziUi.Styles.Slider);
        slider.ValueChanged += (_, _) => sliderValue.Text = $"{slider.Value:0}%";
        values.Children.Add(slider);
        var date = new YanziDatePicker { SelectedDate = DateTime.Today, Width = 220 };
        date.SelectedDateChanged += (_, selected) => Status("日期：" + selected?.ToString("yyyy-MM-dd"));
        values.Children.Add(YanziPrimitives.Field("日期", date));
        values.Children.Add(YanziUi.WithStyle(new ProgressBar { Value = 45, Maximum = 100, Height = 8,
            Margin = new Thickness(0, 9, 0, 12) }, YanziUi.Styles.Progress));
        var calendar = new YanziCalendarMonth(DateTime.Today) { Margin = new Thickness(0, 4, 0, 10) };
        calendar.SetSelectedDate(DateTime.Today);
        calendar.DateSelected += (_, selected) => Status("日历：" + selected.ToString("yyyy-MM-dd"));
        values.Children.Add(calendar);

        var otp = Card("Input OTP / Button Group", "单字符输入框和组合按钮；键盘可逐格输入，方便检验焦点交互。");
        var otpInput = new YanziOtpInput(6);
        otp.Children.Add(otpInput);
        otp.Children.Add(Text("输入六位数字 · 自动移动焦点 · Backspace 可后退", 11, false,
            "Yanzi.Color.MutedForeground", new Thickness(0, 9, 0, 0)));
        var group = new YanziToggleGroup { Margin = new Thickness(0, 18, 0, 0) };
        group.Add("左对齐");
        group.Add("居中");
        group.Add("右对齐");
        group.SelectionChanged += (_, selected) => Status("对齐方式：" + selected);
        otp.Children.Add(group);
    }

    private void NavigationCatalog()
    {
        var tabs = Card("Tabs / 标签页", "Tab、TabItem 使用原生 WPF 内容切换，支持键盘焦点。");
        var tab = YanziUi.WithStyle(new TabControl { MinHeight = 145 }, YanziUi.Styles.Tabs);
        foreach (var (label, body) in new[]
        {
            ("账户", "查看当前用户配置"), ("设置", "修改界面与同步选项"), ("通知", "目前没有新通知")
        })
        {
            var item = YanziUi.WithStyle(new TabItem { Header = label }, YanziUi.Styles.TabItem);
            item.Content = Text(body, 13, false, "Yanzi.Color.Foreground", new Thickness(12));
            tab.Items.Add(item);
        }
        tabs.Children.Add(tab);

        var accordion = Card("Accordion / Collapsible / 折叠面板", "单击标题展开；原生 Expander 支持方向键和 Tab 导航。");
        foreach (var (header, detail) in new[]
        {
            ("如何使用组件？", "先 ApplyTo(window)，再使用公共库的样式和 YanziPrimitives。"),
            ("会修改我的旧小程序吗？", "不会。必须显式引用和加载资源，旧窗口不自动变化。"),
            ("如何切换深浅主题？", "使用 YanziUi.ApplyTo(window, YanziTheme.Light / Dark)。")
        })
        {
            var expander = YanziUi.WithStyle(new Expander { Header = header,
                Content = Text(detail, 12, false, "Yanzi.Color.MutedForeground", new Thickness(10, 8, 10, 12)),
                Margin = new Thickness(0, 0, 0, 8) }, YanziUi.Styles.Expander);
            accordion.Children.Add(expander);
        }

        var navigation = Card("Breadcrumb / Navigation Menu / Sidebar", "通过真实按钮模拟页面路径、导航项和选择状态。");
        var bread = YanziLayoutPrimitives.Breadcrumb(
            ("燕子", () => Status("导航到燕子")),
            ("组件库", () => Status("导航到组件库")),
            ("基础组件", null));
        navigation.Children.Add(bread);
        navigation.Children.Add(YanziPrimitives.Separator());
        var navRow = new WrapPanel();
        foreach (var name in new[] { "总览", "组件", "文档", "设置" })
        {
            var current = name;
            navRow.Children.Add(Button(name, name == "组件" ? YanziUi.Styles.SecondaryButton : YanziUi.Styles.GhostButton,
                () => Status("点击了导航：" + current)));
        }
        navigation.Children.Add(navRow);

        var pagination = Card("Pagination / 分页", "包含上一页、页码和下一页的真实状态变化。");
        var pager = new YanziPagination { PageCount = 5 };
        pager.PageChanged += (_, current) => Status("切换到第 " + current + " 页");
        pagination.Children.Add(pager);

        var reusableNav = Card("Accordion / Sidebar / Menu", "单开折叠面板与导航侧栏均使用可复用控件。");
        var accordionControl = new YanziAccordion();
        accordionControl.Add("开始使用", Text("引用 Yanzi.UI.Wpf 并调用 YanziUi.ApplyTo。", 12, false, "Yanzi.Color.Foreground", new Thickness(9)));
        accordionControl.Add("组件更新", Text("样式资源有版本兼容约束。", 12, false, "Yanzi.Color.Foreground", new Thickness(9)));
        reusableNav.Children.Add(accordionControl);
        var sidebarDemo = new YanziSidebar { Height = 145, Margin = new Thickness(0, 11, 0, 0) };
        sidebarDemo.Add("概览", () => Status("侧栏：概览"));
        sidebarDemo.Add("小程序", () => Status("侧栏：小程序"));
        sidebarDemo.Add("设置", () => Status("侧栏：设置"));
        reusableNav.Children.Add(sidebarDemo);

        var resize = Card("Resizable / Scroll Area", "拖动左右分栏中间的灰色边缘，观察空间分配；列表支持滚动。");
        var leftPanel = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(6) };
        leftPanel.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        leftPanel.Child = Text("左侧区域\n可以调整宽度", 12, false, "Yanzi.Color.Foreground");
        var messages = new StackPanel { Margin = new Thickness(12) };
        for (int i = 1; i <= 16; i++)
            messages.Children.Add(Text($"消息 {i:00}", 12, false, "Yanzi.Color.Foreground",
                new Thickness(0, 0, 0, 11)));
        var messageScroll = YanziLayoutPrimitives.ScrollArea(messages, 160);
        resize.Children.Add(YanziLayoutPrimitives.Resizable(leftPanel, messageScroll, 180, 160));
    }

    private void DataCatalog()
    {
        var tableCard = Card("Table / Data Table", "通过共享 YanziTable 呈现表头排序、行选择和主题状态。");
        var table = new YanziTable { Margin = new Thickness(0, 0, 0, 7) };
        table.SetData(new[] { "小程序", "状态", "类型" }, new IReadOnlyList<string>[]
        {
            new[] { "剪贴板", "运行中", "系统工具" },
            new[] { "日历", "已启用", "效率工具" },
            new[] { "截图 OCR", "等待中", "生产力" },
            new[] { "灵感白板", "已启用", "创作工具" }
        });
        table.SelectedRowChanged += (_, index) => Status("选中小程序：" + table.Rows[index][0]);
        tableCard.Children.Add(table);

        var chart = Card("Chart / 可视化", "原生 WPF 图形绘制，使用语义颜色；仅供验证外观，不代表真实业务数据。");
        var graph = new UniformGrid { Columns = 7, Height = 150 };
        var h = new[] { 48d, 84d, 105d, 60d, 125d, 99d, 71d };
        for (int i = 0; i < 7; i++)
        {
            var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center };
            var bar = new Border { Height = h[i], Width = 30, CornerRadius = new CornerRadius(5, 5, 0, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, i == 4 ? "Yanzi.Color.Primary" : "Yanzi.Color.Secondary");
            cell.Children.Add(bar);
            cell.Children.Add(Text((i + 1).ToString(), 10, false, "Yanzi.Color.MutedForeground",
                new Thickness(0, 5, 0, 0)));
            graph.Children.Add(cell);
        }
        chart.Children.Add(graph);

        var items = Card("Item / Avatar / Progress / Loading", "适合在设置列表、操作历史和执行结果中复用的内容组合。");
        foreach (var (user, label, status) in new[]
        {
            ("YZ", "本地同步", "已连接"), ("AI", "AI 任务", "进行中"), ("PC", "设备状态", "在线")
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
            row.Children.Add(YanziPrimitives.Avatar(user, 34));
            var info = new StackPanel { Margin = new Thickness(12, 1, 8, 0) };
            info.Children.Add(Text(label, 13, true, "Yanzi.Color.Foreground"));
            info.Children.Add(Text(status, 11, false, "Yanzi.Color.MutedForeground", new Thickness(0, 3, 0, 0)));
            row.Children.Add(info);
            items.Children.Add(row);
            items.Children.Add(YanziPrimitives.Separator());
        }
        items.Children.Add(YanziUi.WithStyle(new ProgressBar { Value = 62, Maximum = 100 }, YanziUi.Styles.Progress));
        items.Children.Add(new YanziLoadingRing { Height = 25, Width = 25, Margin = new Thickness(0, 13, 0, 0) });

        var carouselCard = Card("Carousel / 横向画廊", "点击左右按钮滚动卡片，控件已沉淀到 Yanzi.UI.Wpf。");
        var carousel = new YanziCarousel();
        foreach (var itemName in new[] { "剪贴板", "日历", "截图", "任务", "白板", "云同步" })
        {
            var tile = new Border { Width = 150, Height = 94, Margin = new Thickness(0, 0, 9, 0),
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12) };
            tile.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
            tile.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
            tile.Child = Text(itemName, 14, true, "Yanzi.Color.Foreground");
            carousel.Add(tile);
        }
        carouselCard.Children.Add(carousel);

        var messages = Card("Bubble / Message / Attachment / Marker", "可复用的聊天气泡、附件标签与状态标记；仅影响界面，不读取真实文件。");
        messages.Children.Add(YanziContentPrimitives.MessageBubble("欢迎来到燕子 UI 组件评估中心", false));
        messages.Children.Add(YanziContentPrimitives.MessageBubble("这套组件将用于桌面小程序。", true));
        messages.Children.Add(YanziContentPrimitives.Marker("已同步", true));
        messages.Children.Add(new Border { Height = 11 });
        var attachment = YanziContentPrimitives.Attachment("design-notes.txt",
            () => Status("附件移除操作已触发（演示）"));
        messages.Children.Add(attachment);

        var chatCard = Card("Message Scroller / 流式聊天窗口", "新消息会自动滚动到底部，渲染项有数量上限，避免无限增加界面节点。");
        var history = new YanziMessageScroller { MaxVisible = 80, Height = 170 };
        history.AddMessage("你好，燕子。", true);
        history.AddMessage("欢迎使用 Yanzi UI。", false);
        chatCard.Children.Add(history);
        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var edit = YanziUi.WithStyle(new TextBox { Width = 265, Margin = new Thickness(0, 0, 8, 0) }, YanziUi.Styles.Input);
        typeRow.Children.Add(edit);
        typeRow.Children.Add(Button("添加消息", YanziUi.Styles.DefaultButton, () =>
        {
            if (string.IsNullOrWhiteSpace(edit.Text)) return;
            history.AddMessage(edit.Text, true);
            edit.Clear();
        }));
        chatCard.Children.Add(typeRow);

        var reusableData = Card("Chart / Item", "共享 Chart 支持多图形模式、数值提示和数据点选择。");
        var dynamicChart = new YanziChart { Kind = YanziChartKind.Area };
        dynamicChart.SetData(new[] { new YanziBarPoint("Mon", 3), new YanziBarPoint("Tue", 8),
            new YanziBarPoint("Wed", 5), new YanziBarPoint("Thu", 9), new YanziBarPoint("Fri", 6) });
        dynamicChart.SelectedPointChanged += (_, i) => Status("图表：" + dynamicChart.Points[i].Label);
        reusableData.Children.Add(dynamicChart);
        reusableData.Children.Add(YanziPrimitives.Separator());
        reusableData.Children.Add(new YanziItem("剪贴板", "后台正常运行", "▣", () => Status("查看剪贴板（演示）")));
        reusableData.Children.Add(new YanziItem("日历", "2 个提醒任务", "▦", () => Status("查看日历（演示）")));

        var empty = Card("Empty / Skeleton", "无数据状态与异步占位效果。");
        empty.Children.Add(YanziPrimitives.EmptyState("暂无记录", "使用添加按钮创建你的第一个项目"));
        empty.Children.Add(YanziPrimitives.Skeleton(260, 16));
    }

    private void OverlayCatalog()
    {
        var dialog = Card("Dialog / Alert Dialog / Sheet / Drawer", "父窗口关联和取消流程保持原生 WPF 行为。Sheet 作为可关闭的右侧窗口演示。");
        var row = new WrapPanel();
        row.Children.Add(Button("Dialog", YanziUi.Styles.OutlineButton,
            () => YanziDialog.Confirm(this, "保存设置？", "这是普通对话框。", "保存")));
        row.Children.Add(Button("Alert Dialog", YanziUi.Styles.DestructiveButton,
            () => YanziDialog.Confirm(this, "确定要删除吗？", "这个演示不会删除数据。", "删除", true)));
        row.Children.Add(Button("Drawer / Sheet", YanziUi.Styles.SecondaryButton, ShowDrawerDemo));
        dialog.Children.Add(row);

        var menus = Card("Dropdown Menu / Context Menu / Popover", "点击按钮展开菜单或 Popover，支持鼠标退出后关闭。");
        var dropdown = Button("打开下拉菜单  ⌄", YanziUi.Styles.OutlineButton, () => { });
        var context = new YanziDropdownMenu { PreferAbove = false, AlignStart = true };
        foreach (var name in new[] { "新建", "复制", "重命名", "归档" })
        {
            var action = name;
            context.AddAction(name, () => Status("菜单：" + action));
        }
        context.Attach(dropdown);
        menus.Children.Add(dropdown);
        var popButton = Button("打开 Popover", YanziUi.Styles.SecondaryButton, () => { });
        menus.Children.Add(popButton);
        var popContent = new StackPanel();
        popContent.Children.Add(Text("Popover content", 13, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 9)));
        popContent.Children.Add(YanziUi.WithStyle(new TextBox { Text = "可以编辑内容" }, YanziUi.Styles.Input));
        var pop = YanziPopover.Attach(popButton, popContent);


        var hovers = Card("Tooltip / Hover Card / Toast", "鼠标悬停可查看提示；消息提示使用非阻塞浮层。");
        var tooltipButton = Button("悬停查看 Tooltip", YanziUi.Styles.OutlineButton, () => { });
        var tooltip = YanziUi.WithStyle(new ToolTip { Content = "这是一个真实的 WPF Tooltip" }, YanziUi.Styles.Tooltip);
        tooltipButton.ToolTip = tooltip;
        hovers.Children.Add(tooltipButton);
        hovers.Children.Add(Button("成功 Toast", YanziUi.Styles.DefaultButton,
            () => YanziToast.Show(this, "操作成功", YanziToastKind.Success)));
        hovers.Children.Add(Button("失败 Toast", YanziUi.Styles.DestructiveButton,
            () => YanziToast.Show(this, "操作失败，请重试", YanziToastKind.Error)));

        var more = Card("Hover Card / Menubar / Direction", "悬停提示卡、公共 Menubar 和右到左文字流向。");
        var hoverButton = Button("鼠标悬停这里", YanziUi.Styles.OutlineButton, () => { });
        var hoverBody = new StackPanel();
        hoverBody.Children.Add(Text("Yanzi UI", 14, true, "Yanzi.Color.Foreground"));
        hoverBody.Children.Add(Text("统一的原生桌面组件库。", 12, false, "Yanzi.Color.MutedForeground",
            new Thickness(0, 9, 0, 0)));
        YanziHoverCard.Attach(hoverButton, hoverBody);
        more.Children.Add(hoverButton);
        var menuBar = new YanziMenubar { Margin = new Thickness(0, 12, 0, 13) };
        foreach (var title in new[] { "文件", "编辑", "查看" })
        {
            var category = title;
            menuBar.AddMenu(category, menu =>
                menu.AddAction("查看示例", () => Status("菜单栏：" + category)));
        }
        more.Children.Add(menuBar);
        more.Children.Add(YanziContentPrimitives.Direction(Text("مرحبا · RTL direction", 13, false, "Yanzi.Color.Foreground"),
            FlowDirection.RightToLeft));

        var composable = Card("Command Palette / 共享命令面板", "支持键盘向下选择、回车执行、搜索过滤和鼠标双击。");
        var palette = new YanziCommandPalette();
        palette.Add("打开剪贴板", () => Status("执行：剪贴板（演示）"));
        palette.Add("打开截图", () => Status("执行：截图（演示）"));
        palette.Add("查看设置", () => Status("执行：设置（演示）"));
        composable.Children.Add(palette);

        var command = Card("Command / 命令面板", "通过搜索框实时筛选列表，是命令菜单的轻量 WPF 实现。");
        var search = YanziUi.WithStyle(new TextBox { Text = "" }, YanziUi.Styles.Input);
        command.Children.Add(search);
        var entries = new[] { "打开剪贴板", "打开日历", "启动截图", "搜索文件", "打开设置" };
        var list = YanziUi.WithStyle(new ListBox { Height = 165, Margin = new Thickness(0, 9, 0, 0) }, YanziUi.Styles.List);
        list.ItemsSource = entries;
        search.TextChanged += (_, _) => list.ItemsSource =
            entries.Where(e => e.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is string item) Status("命令（仅展示）：" + item);
        };
        command.Children.Add(list);
    }

    private void ShowDrawerDemo()
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(Text("在侧边窗口中编辑配置", 12, false, "Yanzi.Color.MutedForeground",
            new Thickness(0, 0, 0, 14)));
        body.Children.Add(YanziPrimitives.Field("名称",
            YanziUi.WithStyle(new TextBox { Text = "燕子" }, YanziUi.Styles.Input)));
        body.Children.Add(YanziPrimitives.Field("备注",
            YanziUi.WithStyle(new TextBox { Text = "组件展示，不保存真实数据。" }, YanziUi.Styles.Textarea)));
        YanziSheet.Show(this, "Drawer / Sheet · Preview", body);
    }

}
