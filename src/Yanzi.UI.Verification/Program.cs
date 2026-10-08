using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Yanzi.UI.Wpf;

internal static class Program
{
    private static int _checked;

    [STAThread]
    private static int Main()
    {
        try
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new Window { Title = "Yanzi.UI.Verification", Width = 300, Height = 200 };

            Check(window.Resources.MergedDictionaries.Count == 0, "legacy windows are untouched before opt-in");
            YanziUi.ApplyTo(window);
            Check(window.Resources.MergedDictionaries.Count == 2, "tokens and controls installed per window");

            var dark = RequireBrush(window, "Yanzi.Brush.Window").Color;
            Check(dark == Color.FromRgb(0x0A, 0x0A, 0x0A), "dark token");
            foreach (var key in new[]
            {
                YanziUi.Styles.DefaultButton,
                YanziUi.Styles.OutlineButton,
                YanziUi.Styles.GhostButton,
                YanziUi.Styles.LinkButton,
                YanziUi.Styles.DestructiveButton,
                YanziUi.Styles.Badge,
                YanziUi.Styles.PrimaryButton,
                YanziUi.Styles.SecondaryButton,
                YanziUi.Styles.DangerButton,
                YanziUi.Styles.Input,
                YanziUi.Styles.InputSoft,
                YanziUi.Styles.TextareaSoft,
                YanziUi.Styles.Toggle,
                YanziUi.Styles.CheckBox,
                YanziUi.Styles.Menu,
                YanziUi.Styles.MenuItem,
                YanziUi.Styles.List,
                YanziUi.Styles.ListItem,
                YanziUi.Styles.Loading,
                YanziUi.Styles.Card,
                YanziUi.Styles.TextSecondary
            })
            {
                Check(window.TryFindResource(key) is Style, "style " + key);
            }

            var button = YanziUi.WithStyle(new Button { Content = "保存" }, YanziUi.Styles.PrimaryButton);
            window.Content = button;
            var buttonStyle = button.Style;
            Check(buttonStyle != null, "runtime-resolved primary button style");
            Check(buttonStyle?.TargetType == typeof(Button), "style has correct WPF type");

            YanziUi.ApplyTo(window);
            Check(window.Resources.MergedDictionaries.Count == 2, "idempotent installation");
            YanziUi.ApplyTo(window, YanziTheme.Light);
            Check(window.Resources.MergedDictionaries.Count == 2, "theme switch does not leak dictionaries");
            Check(RequireBrush(window, "Yanzi.Brush.Window").Color == Color.FromRgb(0xFF, 0xFF, 0xFF), "light token");
            Check(button.Style != null, "controls survive theme switching");
            YanziUi.ApplyTo(window, YanziTheme.Dark);
            Check(RequireBrush(window, "Yanzi.Brush.Window").Color == dark, "restore dark theme");

            foreach (var variant in Enum.GetValues<YanziBadgeVariant>())
            {
                var item = new YanziBadge { Content = "Example", Variant = variant };
                window.Content = item;
                Check(item.Style != null, "badge resolves style: " + variant);
                Check(item.Style?.TargetType == typeof(YanziBadge), "badge style type: " + variant);
            }
            var spinnerBadge = new YanziBadge { Content = "Generating", IsLoading = true, LeadingIcon = "✓" };
            window.Content = spinnerBadge;
            Check(spinnerBadge.IsLoading && spinnerBadge.LeadingIcon == "✓", "badge spinner/icon properties");
            var expectedKeys = new[] { "Yanzi.Color.Background", "Yanzi.Color.Foreground",
                "Yanzi.Color.Primary", "Yanzi.Color.PrimaryForeground", "Yanzi.Color.Secondary",
                "Yanzi.Color.Destructive", "Yanzi.Color.Accent", "Yanzi.Color.Input",
                "Yanzi.Color.Ring", "Yanzi.Color.Sidebar", "Yanzi.Color.ChartBar" };
            foreach (var key in expectedKeys)
                Check(window.TryFindResource(key) is SolidColorBrush, "semantic token " + key);

            var additionalStyles = new[] {
                YanziUi.Styles.Textarea, YanziUi.Styles.Password, YanziUi.Styles.Select,
                YanziUi.Styles.Slider, YanziUi.Styles.Progress, YanziUi.Styles.Radio,
                YanziUi.Styles.Tabs, YanziUi.Styles.TabItem, YanziUi.Styles.Expander,
                YanziUi.Styles.Calendar, YanziUi.Styles.DatePicker, YanziUi.Styles.Tooltip,
                YanziUi.Styles.DataGrid,
                YanziUi.Styles.Scrollbar
            };
            foreach (var key in additionalStyles)
                Check(window.TryFindResource(key) is Style, "native reusable component style " + key);

            var names = YanziComponentRegistry.Components.Select(c => c.Name).ToList();
            Check(names.Count >= 60, "registry covers shadcn component inventory");
            Check(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count,
                "registry does not duplicate component identities");
            Check(YanziComponentRegistry.Components.Any(c => c.Name == "Badge" && c.Status == YanziComponentStatus.Ready),
                "registered reusable badge");
            Check(YanziComponentRegistry.Components.Any(c => c.Name == "Menubar" && c.Status == YanziComponentStatus.Ready),
                "registry includes native menubar adaptation");

            var otp = new YanziOtpInput(6);
            otp.SetValue("123456");
            Check(otp.Value == "123456" && otp.IsComplete, "OTP value");
            otp.Clear();
            Check(otp.Value.Length == 0, "OTP clear");
            var badOtpRejected = false;
            try { otp.SetValue("a12345"); }
            catch (ArgumentException) { badOtpRejected = true; }
            Check(badOtpRejected, "OTP rejects invalid values");

            var pagination = new YanziPagination { PageCount = 5 };
            int changedPage = 0;
            pagination.PageChanged += (_, page) => changedPage = page;
            pagination.SetPage(3);
            Check(pagination.Page == 3 && changedPage == 3, "pagination changes page and emits event");
            pagination.SetPage(99);
            Check(pagination.Page == 5, "pagination clamps to last page");
            pagination.Page = 0;
            Check(pagination.Page == 1, "pagination clamps to first page");

            var carousel = new YanziCarousel();
            carousel.Add(new TextBlock { Text = "Card 1" });
            carousel.Add(new TextBlock { Text = "Card 2" });
            Check(carousel.Step > 0, "carousel exposes scroll increment");

            Check(YanziPrimitives.Avatar("YZ") is Border, "avatar primitive");
            Check(YanziPrimitives.Kbd("Ctrl") is Border, "keyboard primitive");
            Check(YanziPrimitives.Separator() is Border, "separator primitive");
            Check(YanziPrimitives.Skeleton(100, 16) is Border, "skeleton primitive");
            Check(YanziPrimitives.Alert("Title", "Body") is Border, "alert primitive");
            Check(YanziPrimitives.EmptyState("Title", "Subtitle") is StackPanel, "empty state primitive");
            Check(YanziPrimitives.Field("Label", new TextBox()) is StackPanel, "field primitive");

            var toggle = new YanziToggleGroup();
            toggle.Add("Left");
            toggle.Add("Center");
            string? selection = null;
            toggle.SelectionChanged += (_, value) => selection = value;
            toggle.Select(1);
            Check(toggle.SelectedValue == "Center" && selection == "Center", "toggle group selection");

            var trigger = new Button { Content = "Anchor" };
            var popup = YanziPopover.Attach(trigger, new TextBlock { Text = "Popover content" });
            Check(popup.PlacementTarget == trigger && !popup.IsOpen, "attached popover stays closed before interaction");
            var crumb = YanziLayoutPrimitives.Breadcrumb(
                ("Home", (Action?)(() => { })), ("Components", null));
            Check(crumb.Children.Count == 3, "breadcrumb segments and separator");
            var collapse = YanziLayoutPrimitives.Collapsible("Section", new TextBlock { Text = "Content" });
            Check(!collapse.IsExpanded, "collapsible initial state");
            var scrollArea = YanziLayoutPrimitives.ScrollArea(new TextBlock(), 170);
            Check(scrollArea.Height == 170, "scroll area size");
            var resizable = YanziLayoutPrimitives.Resizable(new TextBlock(), new TextBlock(), 140, 180);
            Check(resizable.ColumnDefinitions.Count == 3, "resizable columns");
            var aspect = YanziLayoutPrimitives.AspectRatio(new Border(), 16.0 / 9.0, 160);
            Check(Math.Abs(aspect.Height - 90) < 0.01, "aspect ratio calculation");

            Check(YanziComponentRegistry.Components.All(c => c.Status == YanziComponentStatus.Ready),
                "all indexed components have reusable WPF entry points");
            Check(window.TryFindResource(YanziUi.Styles.ToggleButton) is Style, "toggle button style");
            Check(window.TryFindResource(YanziUi.Styles.Menubar) is Style, "menubar style");

            var accordion = new YanziAccordion();
            accordion.Add("First", new TextBlock { Text = "A" });
            accordion.Add("Second", new TextBlock { Text = "B" });
            Check(accordion.SectionCount == 2 && accordion.SingleOpen, "accordion builds reusable sections");
            var buttonGroup = new YanziButtonGroup();
            int buttonActions = 0;
            buttonGroup.Add("A", () => buttonActions++);
            buttonGroup.Add("B", () => buttonActions++);
            Check(buttonGroup.Count == 2, "button group reusable children");

            var inputGroup = new YanziInputGroup("https://", ".com");
            inputGroup.Input.Text = "yanzi";
            Check(inputGroup.Input.Text == "yanzi", "input group exposes editable control");

            var chart = new YanziBarChart();
            chart.SetData(new[] { new YanziBarPoint("Mon", 5), new YanziBarPoint("Tue", 8) });
            Check(chart.Content is UniformGrid, "chart builds native layout");
            Check(chart.PointCount == 2, "chart preserves data point count");
            chart.SelectedIndex = 1;
            Check(chart.SelectedIndex == 1, "chart supports selected column");
            var negativeRejected = false;
            try { chart.SetData(new[] { new YanziBarPoint("No", -1) }); }
            catch (ArgumentOutOfRangeException) { negativeRejected = true; }
            Check(negativeRejected, "chart rejects invalid values");

            int commandRuns = 0;
            var commands = new YanziCommandPalette();
            commands.Add("Check", () => commandRuns++);
            commands.Execute();
            Check(commandRuns == 1, "command palette invokes selected action");

            var itemRow = new YanziItem("Title", "Body", "*");
            Check(itemRow.Content != null, "item row layout");
            var side = new YanziSidebar();
            side.Add("Overview");
            side.Add("Settings");
            side.Select(1);
            Check(side.Count == 2 && side.SelectedIndex == 1, "sidebar selection");

            var messageScroller = new YanziMessageScroller { MaxVisible = 2 };
            messageScroller.AddMessage("A", false);
            messageScroller.AddMessage("B", true);
            messageScroller.AddMessage("C", true);
            Check(messageScroller.VisibleCount == 2, "bounded message scroller");

            var questionnaire = new YanziQuestionnaire(new[]
            {
                new YanziQuestion("question", "Question?", YanziQuestionKind.Text)
            });
            Check(!questionnaire.Next(), "required questionnaire answer");
            var completed = false;
            var optional = new YanziQuestionnaire(new[]
            {
                new YanziQuestion("memo", "Optional?", YanziQuestionKind.Text, Required: false)
            });
            optional.Completed += (_, _) => completed = true;
            Check(optional.Next() && completed, "questionnaire completes and emits answers");

            Check(YanziContentPrimitives.MessageBubble("Hello", true) is Border, "message bubble");
            Check(YanziContentPrimitives.Marker("Online") is StackPanel, "status marker");
            Check(YanziContentPrimitives.Attachment("file.txt") is Border, "attachment chip");
            var rtl = YanziContentPrimitives.Direction(new TextBlock(), FlowDirection.RightToLeft);
            Check(rtl.FlowDirection == FlowDirection.RightToLeft, "direction primitive");
            var hover = YanziHoverCard.Attach(new Button(), new TextBlock { Text = "Hover" });
            Check(!hover.IsOpen, "hover card default state");
            Check(typeof(YanziSheet).GetMethod(nameof(YanziSheet.Show)) != null, "right-side sheet API");

            // Pill, Chip and Segmented have explicit reusable style resources.
            foreach (var styleKey in new[]
            {
                YanziUi.Styles.PillDefaultButton,
                YanziUi.Styles.PillSecondaryButton,
                YanziUi.Styles.PillOutlineButton,
                YanziUi.Styles.PillGhostButton,
                YanziUi.Styles.PillDestructiveButton,
                YanziUi.Styles.ChipDefaultButton,
                YanziUi.Styles.ChipSecondaryButton,
                YanziUi.Styles.ChipOutlineButton,
                YanziUi.Styles.SegmentedFirstButton,
                YanziUi.Styles.SegmentedBaseButton,
                YanziUi.Styles.SegmentedLastButton
            })
                Check(window.TryFindResource(styleKey) is Style, "pill/chip/segmented style " + styleKey);

            var pill = YanziUi.WithStyle(new Button { Content = "Pill" },
                YanziUi.Styles.PillDefaultButton);
            window.Content = pill;
            pill.ApplyTemplate();
            var pillChrome = pill.Template.FindName("Chrome", pill) as Border;
            Check(pillChrome != null && pillChrome.CornerRadius.TopLeft >= 500,
                "pill default has fully rounded chrome");
            var chip = YanziUi.WithStyle(new Button { Content = "Chip" },
                YanziUi.Styles.ChipOutlineButton);
            window.Content = chip;
            chip.ApplyTemplate();
            var chipChrome = chip.Template.FindName("Chrome", chip) as Border;
            Check(chipChrome != null && chipChrome.CornerRadius.TopLeft >= 500,
                "chip outline has fully rounded chrome");
            YanziUi.ApplyTo(window, YanziTheme.Light);
            window.Content = pill;
            pill.ApplyTemplate();
            Check(pill.Template.FindName("Chrome", pill) is Border lightChrome
                && lightChrome.CornerRadius.TopLeft >= 500,
                "pill template survives light theme");
            YanziUi.ApplyTo(window, YanziTheme.Dark);

            var pillGroup = new YanziSegmentedButtonGroup();
            var groupClickCount = 0;
            var firstSegment = pillGroup.Add("Group", () => groupClickCount++);
            var lastSegment = pillGroup.Add("More", () => groupClickCount++);
            Check(pillGroup.Count == 2, "segmented group has two actions");
            firstSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            lastSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(groupClickCount == 2, "both segmented actions remain clickable");
            window.Content = pillGroup;
            firstSegment.ApplyTemplate();
            lastSegment.ApplyTemplate();
            Check(firstSegment.Template.FindName("Chrome", firstSegment) is Border firstChrome
                && firstChrome.CornerRadius.TopLeft >= 500
                && firstChrome.CornerRadius.TopRight == 0,
                "first segmented control rounds left side only");
            Check(lastSegment.Template.FindName("Chrome", lastSegment) is Border lastChrome
                && lastChrome.CornerRadius.TopRight >= 500
                && lastChrome.CornerRadius.TopLeft == 0,
                "last segmented control rounds right side only");

            var secondWindow = new Window();
            Check(secondWindow.Resources.MergedDictionaries.Count == 0, "separate old window remains unchanged");
            Check(YanziUi.WithStyle(new ProgressBar(), YanziUi.Styles.Loading) is ProgressBar, "loading control helper");

            secondWindow.Close();
            window.Close();
            application.Shutdown();
            Console.WriteLine($"PASS: {_checked} design system checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static SolidColorBrush RequireBrush(Window window, string key)
    {
        if (window.TryFindResource(key) is not SolidColorBrush brush)
            throw new InvalidOperationException("Missing brush: " + key);
        return brush;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checked++;
        Console.WriteLine("OK: " + label);
    }
}
