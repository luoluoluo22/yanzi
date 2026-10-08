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
                menu.AddAction("Delete", () => Status("Context Menu：Delete"), destructive: true);
                menu.Attach(area);
                Add(area);
                Add(Action("打开右键菜单（键盘预览）", () => menu.OpenAt(new Point(30, 36))));
                Limit("已自绘右键弹层和操作行；官网子菜单、单选菜单、完整 RTL 与动画仍待补齐。");
                break;
            }
            case "Data Table":
            {
                var grid = YanziUi.WithStyle(new DataGrid
                {
                    Width = 440, Height = 213,
                    AutoGenerateColumns = true,
                    CanUserSortColumns = true,
                    IsReadOnly = true,
                    SelectionMode = DataGridSelectionMode.Single,
                    ItemsSource = new[]
                    {
                        new { Id = "INV001", Status = "Paid", Amount = "$250" },
                        new { Id = "INV002", Status = "Pending", Amount = "$150" },
                        new { Id = "INV003", Status = "Unpaid", Amount = "$80" },
                        new { Id = "INV004", Status = "Paid", Amount = "$420" }
                    }
                }, YanziUi.Styles.DataGrid);
                System.Windows.Automation.AutomationProperties.SetName(grid, "Data Table 样例");
                Add(grid);
                Limit("仅排序和行选择示例；列过滤、分组、分页与官网 Table 组合能力尚待实现。");
                break;
            }
            case "Date Picker":
            {
                var date = YanziUi.WithStyle(new DatePicker
                {
                    SelectedDate = new DateTime(2026, 10, 8),
                    Width = 220,
                    HorizontalAlignment = HorizontalAlignment.Left
                }, YanziUi.Styles.DatePicker);
                date.SelectedDateChanged += (_, _) =>
                    Status("Date Picker：" + date.SelectedDate?.ToString("yyyy-MM-dd"));
                Add(YanziPrimitives.Field("Pick a date", date));
                Limit("当前使用 Windows 日期弹层；范围选择、月份切换外观及弹层样式待对齐。");
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
                Limit("已有自绘通用可编辑 Dialog，支持滚动、Esc、关闭、确认；多窗口遮罩细节、动画与焦点环专项验收待完善。");
                break;
            }
            case "Drawer":
            {
                Add(Action("Open drawer", () => ShowPreviewSheet("Drawer")));
                Limit("Drawer 现使用底部蒙层面板和独立手柄；拖拽关闭、触屏滑动与多段吸附尚未实现。");
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
                var search = new YanziInputGroup("https://", ".com") { Width = 365 };
                search.Input.Text = "yanzi";
                search.Input.TextChanged += (_, _) => Status("Input Group：" + search.Input.Text);
                Add(search);
                Limit("目前组合前后缀使用独立内容区；shadcn 的一体化边框仍待复刻。");
                break;
            }
            case "Menubar":
            {
                var bar = YanziUi.WithStyle(new Menu { Width = 385, HorizontalAlignment = HorizontalAlignment.Left },
                    YanziUi.Styles.Menubar);
                foreach (var label in new[] { "File", "Edit", "View", "Profiles" })
                {
                    var menu = new MenuItem { Header = label };
                    foreach (var child in new[] { "New", "Open", "Settings" })
                    {
                        var nameCopy = label + " / " + child;
                        var action = YanziUi.WithStyle(new MenuItem { Header = child }, YanziUi.Styles.MenuItem);
                        action.Click += (_, _) => Status("Menubar：" + nameCopy);
                        menu.Items.Add(action);
                    }
                    bar.Items.Add(menu);
                }
                Add(bar);
                Limit("目前 WPF Menu 作为行为基础，checkbox/radio 子项和子菜单视觉待对齐。");
                break;
            }
            case "Message Scroller":
            {
                var demo = new YanziMessageScroller { Width = 415 };
                demo.MaxVisible = 25;
                demo.AddMessage("Hi, how can I help you today?", false);
                demo.AddMessage("Show me the previous examples.", true);
                demo.AddMessage("The scroller stays near the latest message.", false);
                Add(demo);
                Add(Action("追加一条消息", () =>
                {
                    demo.AddMessage("New message · " + (demo.VisibleCount + 1), false);
                    Status("Message Scroller · 已追加");
                }));
                Limit("已有界消息和跟随滚动；官网复杂流式锚定、多角色显示待对齐。");
                break;
            }
            case "Navigation Menu":
            {
                var menu = YanziUi.WithStyle(new Menu
                {
                    Width = 405, HorizontalAlignment = HorizontalAlignment.Left
                }, YanziUi.Styles.Menubar);
                foreach (var title in new[] { "Getting started", "Components", "Documentation" })
                {
                    var header = new MenuItem { Header = title };
                    foreach (var link in new[] { "Overview", "Examples", "API Reference" })
                    {
                        var text = title + " / " + link;
                        var item = new MenuItem { Header = link };
                        item.Click += (_, _) => Status("导航：" + text);
                        header.Items.Add(item);
                    }
                    menu.Items.Add(header);
                }
                Add(menu);
                Limit("先复用顶栏 Menu；多栏浮层和鼠标指针动画仍不同于官网 Navigation Menu。");
                break;
            }
            case "Popover":
            {
                var button = Action("Open popover", () => { });
                var content = new StackPanel { Width = 250 };
                content.Children.Add(Text("Dimensions", 15, true, "Yanzi.Color.Foreground"));
                content.Children.Add(Text("Set the dimensions for the layer.", 12, false,
                    "Yanzi.Color.MutedForeground", new Thickness(0, 4, 0, 10)));
                var width = YanziUi.WithStyle(new TextBox { Text = "100%", Width = 190 },
                    YanziUi.Styles.InputSoft);
                content.Children.Add(YanziPrimitives.Field("Width", width));
                YanziPopover.Attach(button, content);
                Add(button);
                Limit("可输入并点击外部关闭；弹出位置、箭头和边缘回退尚需与官网校准。");
                break;
            }
            case "Questionnaire":
            {
                var questionnaire = new YanziQuestionnaire(new List<YanziQuestion>
                {
                    new("topic", "Which component do you use most?", YanziQuestionKind.Choice,
                        new[] { "Button", "Input", "Dialog" }),
                    new("note", "What should we improve?", YanziQuestionKind.Text, Required: false),
                    new("agree", "Keep dark theme?", YanziQuestionKind.YesNo)
                });
                questionnaire.Completed += (_, answers) =>
                    Status("Questionnaire：" + answers.Count + " 个回答（未上传）");
                Add(questionnaire);
                Limit("当前为分步表单；多选、跳过、快捷键和流式服务端问卷尚待补全。");
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
        if (type == "Drawer") YanziSheet.ShowDrawer(this, "Drawer · Preview", content);
        else YanziSheet.Show(this, "Sheet · Preview", content);
    }
}
