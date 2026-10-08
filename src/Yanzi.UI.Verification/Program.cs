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
            // Badge shape must be a short capsule, not a compressed oval.
            // Test real layout widths, content changes and every visual variant.
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var shortBadge = new YanziBadge { Content = "Badge", Variant = YanziBadgeVariant.Default };
            var mediumBadge = new YanziBadge { Content = "Outline", Variant = YanziBadgeVariant.Outline };
            var longBadge = new YanziBadge { Content = "Secondary", Variant = YanziBadgeVariant.Secondary };
            badgeRow.Children.Add(shortBadge);
            badgeRow.Children.Add(mediumBadge);
            badgeRow.Children.Add(longBadge);
            window.Content = badgeRow;
            badgeRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            badgeRow.Arrange(new Rect(0, 0, badgeRow.DesiredSize.Width, badgeRow.DesiredSize.Height));
            shortBadge.ApplyTemplate();
            mediumBadge.ApplyTemplate();
            longBadge.ApplyTemplate();
            var badgeChrome = shortBadge.Template.FindName("BadgeChrome", shortBadge) as Border;
            Check(badgeChrome is not null && Math.Abs(badgeChrome.CornerRadius.TopLeft - 11) < 0.01,
                "badge uses exact 11 DIP semicircular ends, not 999");
            Check(shortBadge.DesiredSize.Height == 22 && mediumBadge.DesiredSize.Height == 22,
                "badge variants share compact 22 DIP height");
            Check(shortBadge.DesiredSize.Width >= 52
                && shortBadge.DesiredSize.Width - shortBadge.DesiredSize.Height >= 30,
                "short Badge label retains a visible horizontal straight section");
            Check(longBadge.DesiredSize.Width > mediumBadge.DesiredSize.Width
                && mediumBadge.DesiredSize.Width > shortBadge.DesiredSize.Width,
                $"Badge variants size to label: Badge={shortBadge.DesiredSize.Width:0.#}, Outline={mediumBadge.DesiredSize.Width:0.#}, Secondary={longBadge.DesiredSize.Width:0.#}");
            // Reflow changed text inside an actual WPF window, rather than
            // measuring detached, cached controls.
            window.ShowInTaskbar = false;
            window.Left = -10000;
            window.Top = -10000;
            window.Show();
            window.UpdateLayout();
            badgeRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeOldWidth = shortBadge.DesiredSize.Width;
            shortBadge.Content = "Longer sample label";
            badgeRow.InvalidateMeasure();
            shortBadge.InvalidateMeasure();
            window.UpdateLayout();
            badgeRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(shortBadge.DesiredSize.Width > badgeOldWidth + 35,
                "badge expands when its caption changes");
            shortBadge.Content = "Badge";
            badgeRow.InvalidateMeasure();
            shortBadge.InvalidateMeasure();
            window.UpdateLayout();
            badgeRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(Math.Abs(shortBadge.DesiredSize.Width - badgeOldWidth) < 1,
                "badge shrinks again when its caption shortens");

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
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            pill.Arrange(new Rect(0, 0, pill.DesiredSize.Width, pill.DesiredSize.Height));
            Check(pillChrome != null && Math.Abs(pillChrome.CornerRadius.TopLeft - 14) < 0.01,
                "pill has an exact 14 DIP end radius instead of 999");
            Check(pill.ActualWidth >= 56 && pill.ActualHeight == 32,
                "pill preserves a minimum 56x32 capsule silhouette");
            Check(pill.ActualWidth - pill.ActualHeight >= 24,
                "pill keeps parallel top and bottom edges");
            var widthPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var outlineBtn = YanziUi.WithStyle(new Button { Content = "Outline" }, YanziUi.Styles.PillDefaultButton);
            var secondaryBtn = YanziUi.WithStyle(new Button { Content = "Secondary" }, YanziUi.Styles.PillSecondaryButton);
            widthPanel.Children.Add(outlineBtn);
            widthPanel.Children.Add(secondaryBtn);
            window.Content = widthPanel;
            // Test live layout with a real WPF visual tree and dispatcher, not a detached control.
            window.ShowInTaskbar = false;
            window.Left = -10000;
            window.Top = -10000;
            window.UpdateLayout();
            widthPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var outlineWidth = outlineBtn.DesiredSize.Width;
            var secondaryWidth = secondaryBtn.DesiredSize.Width;
            Check(secondaryWidth > outlineWidth + 5,
                $"gallery buttons size to their text: Outline={outlineWidth:0.#} DIP Secondary={secondaryWidth:0.#} DIP");
            Check(outlineWidth >= 56 && outlineWidth - 32 > 24,
                "short text still preserves two straight capsule edges");
            var beforeChange = outlineBtn.DesiredSize.Width;
            outlineBtn.Content = "Outline with a longer dynamic caption";
            outlineBtn.InvalidateMeasure();
            widthPanel.InvalidateMeasure();
            window.UpdateLayout();
            widthPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(outlineBtn.DesiredSize.Width > beforeChange + 40,
                "pill grows when the caption changes at runtime");
            outlineBtn.Content = "Outline";
            outlineBtn.InvalidateMeasure();
            widthPanel.InvalidateMeasure();
            window.UpdateLayout();
            widthPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(Math.Abs(outlineBtn.DesiredSize.Width - beforeChange) < 1,
                "pill contracts again when the caption becomes shorter");
            var chip = YanziUi.WithStyle(new Button { Content = "Chip" },
                YanziUi.Styles.ChipOutlineButton);
            window.Content = chip;
            chip.ApplyTemplate();
            var chipChrome = chip.Template.FindName("Chrome", chip) as Border;
            chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            chip.Arrange(new Rect(0, 0, chip.DesiredSize.Width, chip.DesiredSize.Height));
            Check(chipChrome != null && Math.Abs(chipChrome.CornerRadius.TopLeft - 10) < 0.01,
                "chip has an exact 10 DIP end radius");
            Check(chip.ActualWidth >= 42 && chip.ActualHeight == 24,
                "chip preserves a minimum 42x24 footprint");
            Check(chip.ActualWidth - chip.ActualHeight >= 18,
                "chip has a straight center section");
            var chipPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var smallChip = YanziUi.WithStyle(new Button { Content = "Chip" }, YanziUi.Styles.ChipDefaultButton);
            var longChip = YanziUi.WithStyle(new Button { Content = "Secondary" }, YanziUi.Styles.ChipSecondaryButton);
            chipPanel.Children.Add(smallChip);
            chipPanel.Children.Add(longChip);
            window.Content = chipPanel;
            chipPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Check(longChip.DesiredSize.Width > smallChip.DesiredSize.Width + 6,
                "chip width follows caption instead of a common fixed width");
            YanziUi.ApplyTo(window, YanziTheme.Light);
            window.Content = pill;
            pill.ApplyTemplate();
            Check(pill.Template.FindName("Chrome", pill) is Border lightChrome
                && Math.Abs(lightChrome.CornerRadius.TopLeft - 14) < 0.01,
                "pill half-height radius survives light theme");
            YanziUi.ApplyTo(window, YanziTheme.Dark);

            var pillGroup = new YanziSegmentedButtonGroup();
            var groupClickCount = 0;
            var firstSegment = pillGroup.Add("Group", () => groupClickCount++);
            var lastSegment = pillGroup.Add("More", () => groupClickCount++);
            Check(pillGroup.Count == 2, "segmented group has two actions");
            Check(firstSegment.MinWidth <= 54 && lastSegment.MinWidth <= 36,
                "segmented group no longer forces identical long minimum widths");
            firstSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            lastSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(groupClickCount == 2, "both segmented actions remain clickable");
            window.Content = pillGroup;
            firstSegment.ApplyTemplate();
            lastSegment.ApplyTemplate();
            Check(firstSegment.Template.FindName("Chrome", firstSegment) is Border firstChrome
                && Math.Abs(firstChrome.CornerRadius.TopLeft - 15) < 0.01
                && firstChrome.CornerRadius.TopRight == 0,
                "first segmented control rounds left side only");
            Check(lastSegment.Template.FindName("Chrome", lastSegment) is Border lastChrome
                && Math.Abs(lastChrome.CornerRadius.TopRight - 15) < 0.01
                && lastChrome.CornerRadius.TopLeft == 0,
                "last segmented control rounds right side only");

            // Search input is a real focusable TextBox, with a full-height content host.
            var searchBox = new YanziSearchBox("Name");
            window.Content = searchBox;
            window.UpdateLayout();
            Check(searchBox.Input.Template.FindName("PART_ContentHost", searchBox.Input)
                is ScrollViewer textHost, "search TextBox retains PART_ContentHost");
            var host = (ScrollViewer)searchBox.Input.Template.FindName("PART_ContentHost", searchBox.Input)!;
            Check(host.Margin == new Thickness(0),
                "search text viewport is not inset twice by the TextBox Padding");
            Check(searchBox.Input.CaretBrush is SolidColorBrush
                && searchBox.Input.Foreground is SolidColorBrush,
                "search field has explicit caret and foreground brushes");
            Check(host.ActualHeight >= 27,
                $"search text host remains tall enough for glyphs and caret ({host.ActualHeight:0.#} DIP)");
            Check(searchBox.Input.IsEnabled && searchBox.Input.Focusable,
                "search field remains keyboard-focusable");
            var searchVisual = (Grid)searchBox.Content;
            var prompt = (TextBlock)searchVisual.Children[1];
            Check(prompt.Visibility == Visibility.Visible && prompt.Text == "Name",
                "placeholder displayed only when input is empty");
            var placeholderX = prompt.TranslatePoint(new Point(0, 0), searchBox.Input).X;
            searchBox.Text = "Name";
            window.UpdateLayout();
            var caretStart = searchBox.Input.GetRectFromCharacterIndex(0, false);
            Check(Math.Abs(placeholderX - caretStart.X) <= 0.5,
                $"placeholder left edge matches native caret start: placeholder={placeholderX:0.##}, caret={caretStart.X:0.##} DIP");
            searchBox.Text = "中文测试 abc";
            searchBox.Input.CaretIndex = searchBox.Text.Length;
            window.UpdateLayout();
            Check(searchBox.Input.Text == "中文测试 abc"
                && searchBox.Input.CaretIndex == searchBox.Text.Length,
                "Chinese and Latin input visible with caret at end");
            Check(prompt.Visibility == Visibility.Collapsed,
                "placeholder hides when the input is non-empty");
            searchBox.Text = "";
            Check(prompt.Visibility == Visibility.Visible, "placeholder restores after deleting text");
            var lucideViewbox = searchVisual.Children.OfType<Viewbox>().Single();
            var iconCanvas = (Canvas)lucideViewbox.Child;
            Check(iconCanvas.Children.Count == 2
                && iconCanvas.Children[0] is System.Windows.Shapes.Ellipse
                && iconCanvas.Children[1] is System.Windows.Shapes.Line,
                "search icon is native Lucide circle plus handle, not a font glyph");
            var textarea = YanziUi.WithStyle(new TextBox { Text = "中文\nSecond line",
                Height = 85 }, YanziUi.Styles.TextareaSoft);
            window.Content = textarea;
            window.UpdateLayout();
            Check(textarea.VerticalContentAlignment == VerticalAlignment.Top,
                "multi-line text starts from the top instead of vertical center");
            var textareaHost = textarea.Template.FindName("PART_ContentHost", textarea) as ScrollViewer;
            Check(textareaHost is not null && textareaHost.Margin == new Thickness(0),
                "multi-line input content host is not clipped by duplicate padding");

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
