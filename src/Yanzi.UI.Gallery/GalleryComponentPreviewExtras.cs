using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

/// <summary>
/// Official component catalog previews that were previously only present in grouped demo pages.
/// A preview proves the corresponding WPF control can be exercised, NOT shadcn parity.
/// </summary>
internal sealed partial class GalleryWindow
{
    private bool TryRenderPendingPreview(string name, StackPanel host)
    {
        var examples = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 315,
            MaxWidth = 560,
            Margin = new Thickness(0, 0, 0, 8)
        };
        void Add(UIElement child) => examples.Children.Add(child);
        Button Action(string caption, Action action)
        {
            var b = Button(caption, YanziUi.Styles.PillOutlineButton, action);
            b.Margin = new Thickness(0, 0, 8, 10);
            return b;
        }
        void Limit(string message) =>
            Add(Text("对齐差距：" + message, 12, false, "Yanzi.Color.MutedForeground",
                new Thickness(0, 12, 0, 8)));

        switch (name)
        {
            case "Combobox":
            {
                Add(Text("Select a framework", 13, true, "Yanzi.Color.Foreground",
                    new Thickness(0, 0, 0, 7)));
                var combo = new YanziCombobox("Select a framework");
                foreach (var value in new[] { "Next.js", "SvelteKit", "Nuxt.js", "Remix", "Astro" })
                    combo.Add(value);
                combo.SelectionChanged += (_, value) => Status("Combobox：" + (value ?? "未选择"));
                Add(combo);
                Add(Action("清空选择", () => combo.Clear()));
                Limit("新版已采用自绘输入与过滤弹层，支持无结果提示和键盘选中；多选 Chips、分组、无障碍组合角色和 RTL 尚未对齐。");
                break;
            }
            case "Command":
            {
                var commands = new YanziCommandPalette { Width = 325 };
                foreach (var item in new[] { "Open clipboard", "Open calendar", "Open settings", "Search files" })
                {
                    var caption = item;
                    commands.Add(caption, () => Status("Command：" + caption));
                }
                Add(commands);
                Limit("已有实时搜索与 Enter 执行；命令分类、快捷键和空状态未完成官网还原。");
                break;
            }
            case "Context Menu":
            {
                var area = new Border
                {
                    Width = 325, Height = 117,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Focusable = true
                };
                area.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
                area.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
                var label = Text("Right click here · 或按 Shift + F10", 13, false, "Yanzi.Color.Foreground");
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                area.Child = label;
                var menu = new YanziContextMenu();
                menu.AddLabel("Quick actions");
                menu.AddAction("Back", () => Status("Context Menu：Back"), "Alt+Left");
                menu.AddAction("Forward", () => Status("Context Menu：Forward"), "Alt+Right");
                menu.AddAction("Reload", () => Status("Context Menu：Reload"), "Ctrl+R");
                menu.AddSeparator();
                menu.AddCheck("Show bookmarks", true, value => Status("Show bookmarks：" + value));
                menu.AddSeparator();
                menu.AddRadioGroup("View mode", new[]
                {
                    ("Comfortable", "comfortable"),
                    ("Compact", "compact")
                }, "comfortable", value => Status("View mode：" + value));
                menu.AddSubmenu("More tools", child =>
                {
                    child.AddAction("Copy link", () => Status("Context Menu：Copy link"));
                    child.AddAction("Inspect", () => Status("Context Menu：Inspect"));
                });
                menu.AddSeparator();
                menu.AddAction("Delete", () => Status("Context Menu：Delete"), destructive: true);
                menu.Attach(area);
                Add(area);
                Add(Action("打开右键菜单（键盘预览）", () => menu.OpenAt(new Point(30, 36))));
                Limit("已有一级子菜单、单选组和键盘方向导航；多级嵌套、悬停延时、RTL 与边界避让仍待视觉校对。");
                break;
            }
            case "Data Table":
            {
                var data = new YanziDataTable { Width = 485, PageSize = 4 };
                data.SetData(new[] { "订单号", "状态", "金额", "客户" },
                    new IReadOnlyList<string>[]
                    {
                        new[] { "INV001", "已付款", "$250", "王敏" },
                        new[] { "INV002", "待处理", "$150", "李峰" },
                        new[] { "INV003", "未付款", "$80", "赵丽" },
                        new[] { "INV004", "已付款", "$420", "陈晓" },
                        new[] { "INV005", "处理中", "$120", "刘杰" },
                        new[] { "INV006", "已付款", "$310", "张雪" },
                        new[] { "INV007", "待处理", "$60", "孙浩" },
                        new[] { "INV008", "已付款", "$740", "唐芳" },
                        new[] { "INV009", "未付款", "$95", "谢宇" },
                        new[] { "INV010", "已付款", "$430", "吴宁" },
                        new[] { "INV011", "待处理", "$45", "周静" },
                        new[] { "INV012", "已付款", "$155", "侯明" }
                    });
                data.SelectionChanged += (_, id) => Status("切换选择：INV" + (id + 1).ToString("000"));
                Add(data);
                Limit("可筛选、排序、切换可见列、跨页选择和分页；当前采用有界分页渲染，非窗口虚拟滚动。尚未实现服务端分页。");
                break;
            }
            case "Date Picker":
            {
                var date = new YanziDatePicker
                {
                    SelectedDate = new DateTime(2026, 10, 8),
                    Width = 240
                };
                date.SelectedDateChanged += (_, selected) =>
                    Status("Date Picker：" + selected?.ToString("yyyy-MM-dd"));
                Add(YanziPrimitives.Field("选择日期", date));
                var constrained = new YanziDatePicker
                {
                    Width = 240, Placeholder = "选择截止日期",
                    MinimumDate = new DateTime(2026, 10, 10),
                    MaximumDate = new DateTime(2026, 10, 20)
                };
                constrained.SelectedDateChanged += (_, selected) =>
                    Status("限定日期：" + selected?.ToString("yyyy-MM-dd"));
                Add(YanziPrimitives.Field("日期范围约束（10日—20日）", constrained));
                Limit("公共 Date Picker：Popover 日历、键盘关闭、选中态、禁用日期均已实现。多日区间与快捷预设仍待扩展。");
                break;
            }
            case "Dialog":
            {
                Add(Action("Edit profile", () =>
                {
                    var form = new StackPanel();
                    var nameInput = YanziUi.WithStyle(new TextBox { Text = "Yanzi Desktop" },
                        YanziUi.Styles.InputSoft);
                    var userInput = YanziUi.WithStyle(new TextBox { Text = "@yanzi" },
                        YanziUi.Styles.InputSoft);
                    form.Children.Add(YanziPrimitives.Field("Name", nameInput));
                    form.Children.Add(YanziPrimitives.Field("Username", userInput));
                    var dialog = new YanziContentDialog(this, "Edit profile",
                        "Make changes to your profile here. Click save when you're done.",
                        form, "Save changes");
                    var accepted = dialog.ShowDialog() == true;
                    Status(accepted ? "Dialog 示例保存：" + nameInput.Text : "Dialog 已取消，不写入数据");
                }));
                Limit("通用 Dialog 已增加 Tab 循环及关闭后焦点恢复；进入/退出动画、小屏适配和屏幕阅读器标签仍待核验。");
                break;
            }
            case "Drawer":
            {
                Add(Action("Open drawer", () => ShowPreviewSheet("Drawer")));
                Limit("底部 Drawer 新增手柄鼠标/触摸拖动、三档高度吸附及向下拖动关闭；速度阈值、回弹动画和触屏滚动冲突仍待验证。");
                break;
            }
            case "Hover Card":
            {
                var anchor = Action("@yanzi", () => Status("Hover Card 的触发操作已点击"));
                var card = new StackPanel { Width = 263 };
                card.Children.Add(Text("Yanzi Desktop", 14, true, "Yanzi.Color.Foreground"));
                card.Children.Add(Text("原生 WPF 组件与桌面小程序，支持双主题。", 12, false,
                    "Yanzi.Color.MutedForeground", new Thickness(0, 7, 0, 0)));
                YanziHoverCard.Attach(anchor, card);
                Add(anchor);
                Limit("延时关闭已支持，但出现延时、方位与触屏交互仍需专项核对。");
                break;
            }
            case "Input Group":
            {
                Add(Text("InputGroup · Text / Icon / Button / Kbd / Block Addon",
                    13, true, "Yanzi.Color.Foreground", new Thickness(0, 0, 0, 10)));
                var url = new YanziInputGroup("https://", ".com") { Width = 390 };
                url.Input.Text = "yanzi";
                url.Input.TextChanged += (_, _) => Status("URL: " + url.Input.Text);
                Add(url);
                var search = new YanziInputGroup { Width = 390,
                    Margin = new Thickness(0, 12, 0, 0) };
                search.AddText("⌕", leading: true);
                search.AddAddon(YanziPrimitives.Kbd("Ctrl K"));
                Add(search);
                var query = new YanziInputGroup { Width = 390,
                    Margin = new Thickness(0, 12, 0, 0) };
                var queryButton = YanziUi.WithStyle(new Button
                    { Content = "Search", Padding = new Thickness(8, 3, 8, 3) }, YanziUi.Styles.GhostButton);
                queryButton.Click += (_, _) => Status("Search: " + query.Input.Text);
                query.AddAddon(queryButton);
                Add(query);
                var amount = new YanziInputGroup("$", "USD") { Width = 390,
                    Margin = new Thickness(0, 12, 0, 0) };
                amount.Input.Text = "100";
                amount.AddBlockAddon(Text("Footer: currency is USD", 11, false,
                    "Yanzi.Color.MutedForeground", new Thickness(7, 6, 0, 0)));
                Add(amount);
                break;
            }
            case "Menubar":
            {
                var bar = new YanziMenubar { Width = 385 };
                bar.AddMenu("文件", menu =>
                {
                    menu.AddLabel("操作");
                    menu.AddAction("新建", () => Status("文件 / 新建"), "Ctrl+N");
                    menu.AddAction("打开", () => Status("文件 / 打开"), "Ctrl+O");
                    menu.AddSeparator();
                    menu.AddSubmenu("最近项目", submenu =>
                    {
                        submenu.AddAction("燕子", () => Status("打开 / 燕子"));
                        submenu.AddAction("棘轮", () => Status("打开 / 棘轮"));
                    });
                });
                bar.AddMenu("编辑", menu =>
                {
                    menu.AddAction("撤销", () => Status("编辑 / 撤销"), "Ctrl+Z");
                    menu.AddCheck("自动保存", true, enabled => Status("自动保存：" + enabled));
                });
                bar.AddMenu("视图", menu =>
                {
                    menu.AddRadioGroup("显示", new[] { "列表", "网格" }, "列表",
                        chosen => Status("视图：" + chosen));
                });
                bar.AddMenu("帮助", menu => menu.AddAction("关于", () => Status("关于燕子")));
                Add(bar);
                Limit("Menubar 使用公共 Dropdown Menu 弹层、复选项、单选项和多级子菜单；不再渲染 WPF Menu/MenuItem。");
                break;
            }
            case "Message Scroller":
            {
                var demo = new YanziMessageScroller { Width = 415, MaxVisible = 90 };
                demo.AddMessage("intro", "Hi, how can I help you today?", false);
                demo.AddMessage("question", "Show me the previous examples.", true);
                demo.AddMessage("answer", "Scroll up to read history without being pulled down.", false);
                for (int i = 0; i < 12; i++)
                    demo.AddMessage("thread-" + i, "Conversation turn " + i, i % 2 == 0);
                Add(demo);
                var controls = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
                controls.Children.Add(Action("追加消息", () =>
                {
                    demo.AddMessage("New message · " + Guid.NewGuid().ToString("N")[..8], false);
                    Status("Message Scroller：追加完成");
                }));
                controls.Children.Add(Action("插入历史", () =>
                    demo.PrependHistory(Enumerable.Range(0, 3).Select(i =>
                        ("old-" + Guid.NewGuid().ToString("N")[..8],
                        "Recovered earlier turn " + i, i % 2 == 0)))));
                controls.Children.Add(Action("模拟流式输出", () =>
                {
                    demo.AppendToMessage("answer", " Additional tokens without forced scroll.");
                    Status("Message Scroller：追加输出");
                }));
                controls.Children.Add(Action("跳到末尾", demo.JumpToLatest));
                Add(controls);
                break;
            }
            case "Navigation Menu":
            {
                var navigation = new YanziNavigationMenu { Width = 420 };
                navigation.AddGroup("快速开始", new[]
                {
                    new YanziNavigationMenu.Link("项目介绍", "了解燕子、能力和使用方式。", () => Status("导航：项目介绍")),
                    new YanziNavigationMenu.Link("安装指南", "安装和配置你的第一个小程序。", () => Status("导航：安装指南")),
                    new YanziNavigationMenu.Link("常见问题", "查找常见问题与解决办法。", () => Status("导航：常见问题")),
                    new YanziNavigationMenu.Link("版本更新", "查看最新版功能与维护记录。", () => Status("导航：版本更新"))
                });
                navigation.AddGroup("组件", new[]
                {
                    new YanziNavigationMenu.Link("公共组件", "按钮、输入框、菜单与日期选择器。", () => Status("导航：公共组件")),
                    new YanziNavigationMenu.Link("布局容器", "卡片、网格、面板和侧边栏。", () => Status("导航：布局容器")),
                    new YanziNavigationMenu.Link("交互模式", "弹层、导航和快捷操作。", () => Status("导航：交互模式")),
                    new YanziNavigationMenu.Link("主题系统", "语义令牌、深色和浅色主题。", () => Status("导航：主题系统"))
                });
                navigation.AddGroup("开发文档", new[]
                {
                    new YanziNavigationMenu.Link("API 文档", "调用公共库中的类型和方法。", () => Status("导航：API 文档")),
                    new YanziNavigationMenu.Link("代码示例", "如何从不同小程序中复用组件。", () => Status("导航：代码示例")),
                    new YanziNavigationMenu.Link("组件验收", "查看组件一致性测试规范。", () => Status("导航：组件验收"))
                });
                Add(navigation);
                Limit("共享导航：宽面板双列链接、分类切换、指示标记和键盘导航已实现；RTL 与浏览器路由由宿主提供。");
                break;
            }
            case "Popover":
            {
                var demoButtons = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var align in Enum.GetValues<YanziPopoverAlign>())
                {
                    var chosen = align;
                    var trigger = Action("Align " + chosen, () => { });
                    var content = new StackPanel { Width = 260 };
                    content.Children.Add(Text("Dimensions", 15, true, "Yanzi.Color.Foreground"));
                    content.Children.Add(Text("Set the dimensions for the layer.", 12, false,
                        "Yanzi.Color.MutedForeground", new Thickness(0, 4, 0, 10)));
                    var width = YanziUi.WithStyle(new TextBox { Text = "100%" }, YanziUi.Styles.InputSoft);
                    var height = YanziUi.WithStyle(new TextBox { Text = "25px" }, YanziUi.Styles.InputSoft);
                    content.Children.Add(YanziPrimitives.Field("Width", width));
                    content.Children.Add(YanziPrimitives.Field("Height", height));
                    YanziPopover.Attach(trigger, content, chosen);
                    demoButtons.Children.Add(trigger);
                }
                Add(demoButtons);
                break;
            }
            case "Questionnaire":
            {
                var questionnaire = new YanziQuestionnaire(new List<YanziQuestion>
                {
                    new("platform", "Where should the agent run?", YanziQuestionKind.Choice,
                        new[] { "Local workspace", "Cloud workspace" }),
                    new("environment", "Which cloud environment should it use?", YanziQuestionKind.Choice,
                        new[] { "Preview", "Staging", "Isolated sandbox" }, true, false,
                        "platform", "Cloud workspace"),
                    new("features", "What should the agent report?", YanziQuestionKind.MultipleChoice,
                        new[] { "Progress", "Decisions", "Risks", "Next steps" }, false, true),
                    new("notes", "Describe another task (optional)", YanziQuestionKind.Text,
                        Required: false, AllowSkip: true)
                });
                questionnaire.Width = 440;
                questionnaire.Completed += (_, answers) =>
                    Status("Questionnaire：保存 " + answers.Count + " 个选项（仅本地内存）");
                Add(questionnaire);
                break;
            }
            case "Sheet":
            {
                Add(Action("Open sheet", () => ShowPreviewSheet("Sheet")));
                Limit("已实现右侧自绘蒙层和入场动画；不同方向、完整焦点循环与 RTL 仍需实机对照。");
                break;
            }
            case "Sidebar":
            {
                var sidebar = new YanziSidebar { Width = 250, Height = 190 };
                sidebar.Add("Overview", () => Status("Sidebar: Overview"));
                sidebar.Add("Projects", () => Status("Sidebar: Projects"));
                sidebar.Add("Settings", () => Status("Sidebar: Settings"));
                Add(sidebar);
                Limit("列表选择可用；官网可折叠侧栏的宽度与快捷键行为尚未复刻。");
                break;
            }
            default:
                return false;
        }

        host.Children.Add(examples);
        return true;
    }

    private void ShowPreviewSheet(string type)
    {
        var content = new StackPanel();
        content.Children.Add(YanziPrimitives.Field("Name",
            YanziUi.WithStyle(new TextBox { Text = "Yanzi UI" }, YanziUi.Styles.InputSoft),
            "此处只做视觉演示，不会修改任何数据。"));
        content.Children.Add(Text("Press Close to return to the component catalog.",
            12, false, "Yanzi.Color.MutedForeground", new Thickness(0, 10, 0, 0)));
        if (type == "Drawer")
        {
            var drawer = new YanziSheetOverlay(this, "Drawer · Preview", content,
                YanziSheetSide.Bottom);
            var snapButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 14)
            };
            foreach (var (label, ratio) in new[] { ("40%", .40), ("65%", .65), ("90%", .90) })
            {
                var target = ratio;
                var button = Button(label, YanziUi.Styles.PillOutlineButton,
                    () => drawer.SnapDrawerTo(target));
                button.Margin = new Thickness(0, 0, 9, 0);
                snapButtons.Children.Add(button);
            }
            content.Children.Add(snapButtons);
            content.Children.Add(Text("可以拖动顶部横条改变高度，松开后吸附到最近档位。向下拖到底部可关闭。",
                12, false, "Yanzi.Color.MutedForeground"));
            drawer.ShowDialog();
        }
        else YanziSheet.Show(this, "Sheet · Preview", content);
    }
}
