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
            Check(names.Count == 64, "registry covers exactly 64 shadcn official components");
            var expectedOfficial = new[]
            {
                "Accordion",
                "Alert",
                "Alert Dialog",
                "Aspect Ratio",
                "Attachment",
                "Avatar",
                "Badge",
                "Breadcrumb",
                "Bubble",
                "Button",
                "Button Group",
                "Calendar",
                "Card",
                "Carousel",
                "Chart",
                "Checkbox",
                "Collapsible",
                "Combobox",
                "Command",
                "Context Menu",
                "Data Table",
                "Date Picker",
                "Dialog",
                "Direction",
                "Drawer",
                "Dropdown Menu",
                "Empty",
                "Field",
                "Hover Card",
                "Input",
                "Input Group",
                "Input OTP",
                "Item",
                "Kbd",
                "Label",
                "Marker",
                "Menubar",
                "Message",
                "Message Scroller",
                "Native Select",
                "Navigation Menu",
                "Pagination",
                "Popover",
                "Progress",
                "Questionnaire",
                "Radio Group",
                "Resizable",
                "Scroll Area",
                "Select",
                "Separator",
                "Sheet",
                "Sidebar",
                "Skeleton",
                "Slider",
                "Spinner",
                "Switch",
                "Table",
                "Tabs",
                "Textarea",
                "Toast",
                "Toggle",
                "Toggle Group",
                "Tooltip",
                "Typography",
            };
            Check(names.SequenceEqual(expectedOfficial, StringComparer.Ordinal),
                "all 64 official component names and ordering match shadcn docs exactly");
            Check(YanziComponentRegistry.Components.All(c =>
                c.OfficialUrl == "https://ui.shadcn.com/docs/components/base/"
                    + c.Name.ToLowerInvariant().Replace(' ', '-')),
                "all 64 components have the verified official Base UI documentation path");
            Check(YanziComponentRegistry.Components.Select(c => c.OfficialUrl).Distinct().Count() == 64,
                "all 64 official reference links are unique");

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

            var geistFont = window.TryFindResource("Yanzi.Font.Geist") as FontFamily;
            Check(geistFont != null, "Geist preview font family resource resolves");
            Check(geistFont!.Source.Contains("Geist", StringComparison.OrdinalIgnoreCase),
                "preview family refers to bundled Geist and CJK fallback");
            Console.WriteLine("FONT_PILOT family=" + geistFont.Source);
            var geistFace = new Typeface(geistFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            var hasGeistGlyph = geistFace.TryGetGlyphTypeface(out var geistGlyph);
            Console.WriteLine("FONT_PILOT loaded=" + hasGeistGlyph
                + " uri=" + (geistGlyph?.FontUri?.ToString() ?? "(fallback)"));
            Check(hasGeistGlyph && geistGlyph!.FontUri.ToString().Contains("Geist", StringComparison.OrdinalIgnoreCase),
                "Geist actual typeface resolves to bundled font resource");

            // The gallery-only typography is opt-in; library button and badge defaults stay stable.
            var badgePreview = YanziUi.WithStyle(
                new YanziBadge { Content = "Secondary", Variant = YanziBadgeVariant.Secondary },
                YanziUi.Styles.BadgeGeistPreview);
            window.Content = badgePreview;
            window.UpdateLayout();
            Check(badgePreview.FontFamily.Source.Contains("Geist", StringComparison.OrdinalIgnoreCase)
                && badgePreview.FontWeight == FontWeights.Medium
                && Math.Abs(badgePreview.FontSize - 12) < 0.01,
                "Geist preview badge is 12 DIP, Medium 500");
            var legacyBadge = YanziUi.WithStyle(new YanziBadge { Content = "Old" }, YanziUi.Styles.Badge);
            window.Content = legacyBadge;
            window.UpdateLayout();
            Check(legacyBadge.FontFamily.Source.Contains("Segoe UI", StringComparison.OrdinalIgnoreCase)
                && legacyBadge.FontWeight == FontWeights.SemiBold,
                "legacy badge remains Segoe UI and Semibold");
            var searchGeist = new YanziSearchBox("Name", useGeist: true);
            window.Content = searchGeist;
            window.UpdateLayout();
            Check(Math.Abs(searchGeist.Input.FontSize - 14) < 0.01
                && searchGeist.Input.FontFamily.Source.Contains("Geist", StringComparison.OrdinalIgnoreCase),
                "opt-in search uses Geist at 14 DIP");
            var searchHint = ((Grid)searchGeist.Content).Children.OfType<TextBlock>().First();
            Check(searchHint.FontFamily.Source.Contains("Geist", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(searchHint.FontSize - 14) < 0.01,
                "search input and placeholder use same font and size");
            searchGeist.Text = "中文 Search ABC";
            window.UpdateLayout();
            Check(searchGeist.Input.Text.Contains("中文") && searchGeist.Input.CaretBrush != null,
                "mixed Chinese Latin input and native caret remain functional");

            // Fully custom shadcn-like Radio: no native RadioButton is constructed.
            Check(window.TryFindResource(YanziUi.Styles.RadioCustom) is Style,
                "custom Radio control template is available");
            Check(window.TryFindResource(YanziUi.Styles.CheckBoxPreview) is Style,
                "shadcn checkbox chrome is available without a Windows checkbox glyph");
            var previewRadio = new YanziRadio { Value = "preview", IsChecked = false };
            window.Content = previewRadio;
            window.UpdateLayout();
            previewRadio.ApplyTemplate();
            var radioOuter = previewRadio.Template.FindName("RadioOuter", previewRadio)
                as System.Windows.Shapes.Ellipse;
            var radioDot = previewRadio.Template.FindName("RadioDot", previewRadio)
                as System.Windows.Shapes.Ellipse;
            Check(previewRadio.GetType().BaseType == typeof(Control),
                "Radio is a custom Control, not a native RadioButton");
            Check(previewRadio.Width == 24 && previewRadio.Height == 24
                && radioOuter?.Width == 16 && radioOuter.Height == 16
                && radioDot?.Width == 8 && radioDot.Height == 8,
                "Radio draws a 16 DIP outer ring and 8 DIP center dot in a 24 DIP hit area");
            Check(radioDot!.Visibility == Visibility.Collapsed, "unchecked Radio shows no center dot");
            previewRadio.Select();
            window.UpdateLayout();
            Check(previewRadio.IsChecked && radioDot.Visibility == Visibility.Visible,
                "checked Radio displays its center dot");
            previewRadio.Select();
            Check(previewRadio.IsChecked, "clicking a selected Radio never deselects it");
            previewRadio.IsEnabled = false;
            Check(!previewRadio.Select(), "disabled Radio ignores activation");
            previewRadio.IsEnabled = true;

            var exclusive = new YanziRadioGroup { Orientation = Orientation.Horizontal };
            var firstOption = exclusive.Add("Default", "default");
            var secondOption = exclusive.Add("Comfortable", "comfortable");
            var thirdOption = exclusive.Add("Compact", "compact");
            int exclusiveEvents = 0;
            exclusive.SelectionChanged += (_, _) => exclusiveEvents++;
            window.Content = exclusive;
            window.UpdateLayout();
            Check(exclusive.Select("default") && firstOption.IsChecked
                && !secondOption.IsChecked && !thirdOption.IsChecked,
                "first RadioGroup selection is exclusive");
            Check(exclusive.Select("comfortable") && secondOption.IsChecked
                && !firstOption.IsChecked && !thirdOption.IsChecked,
                "selecting second Radio clears previous selection");
            Check(exclusive.SelectedValue == "comfortable" && exclusiveEvents >= 2,
                "group exposes SelectedValue and change notifications");
            Check(!exclusive.Select("not-an-option")
                && exclusive.SelectedValue == "comfortable",
                "unknown RadioGroup option does not mutate selection");
            var disabledGroup = new YanziRadioGroup();
            var disabledEntry = disabledGroup.Add("Disabled", "disabled", isEnabled: false);
            Check(!disabledGroup.Select("disabled") && !disabledEntry.IsChecked,
                "disabled option cannot be chosen");
            disabledEntry.IsChecked = true;
            Check(!disabledEntry.IsChecked && disabledGroup.SelectedValue is null,
                "direct IsChecked assignment cannot bypass disabled group exclusivity");
            Check(firstOption.IsTabStop == false && secondOption.IsTabStop,
                "RadioGroup roves Tab stop to selected item");
            Check(typeof(YanziRadio).BaseType == typeof(Control),
                "custom Radio does not inherit Windows RadioButton");
            firstOption.IsChecked = true;
            Check(exclusive.SelectedValue == "default"
                && firstOption.IsChecked && !secondOption.IsChecked,
                "direct IsChecked assignment still enforces exclusivity");
            var arrowSource = PresentationSource.FromVisual(firstOption);
            Check(arrowSource is not null, "custom Radio belongs to an input-capable visual tree");
            var nextKey = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, arrowSource!, 0, System.Windows.Input.Key.Right)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
            };
            firstOption.RaiseEvent(nextKey);
            Check(nextKey.Handled && exclusive.SelectedValue == "comfortable"
                && secondOption.IsChecked && !firstOption.IsChecked,
                "Right key routed event selects the next option");
            var prevKey = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, arrowSource!, 0, System.Windows.Input.Key.Left)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
            };
            secondOption.RaiseEvent(prevKey);
            Check(prevKey.Handled && exclusive.SelectedValue == "default",
                "Left key routed event selects previous option");

            var radioPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(secondOption);
            Check(radioPeer?.GetAutomationControlType() ==
                    System.Windows.Automation.Peers.AutomationControlType.RadioButton,
                "custom Radio exposes radio semantics to screen readers");
            Check(radioPeer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)
                    is System.Windows.Automation.Provider.ISelectionItemProvider,
                "custom Radio exposes automation selection item pattern");
            var groupPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(exclusive);
            Check(groupPeer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Selection)
                    is System.Windows.Automation.Provider.ISelectionProvider,
                "RadioGroup exposes exclusive automation selection pattern");
            YanziUi.ApplyTo(window, YanziTheme.Light);
            window.Content = previewRadio;
            previewRadio.ApplyTemplate();
            Check(previewRadio.Template.FindName("RadioOuter", previewRadio) is System.Windows.Shapes.Ellipse,
                "custom Radio template survives light theme");
            YanziUi.ApplyTo(window, YanziTheme.Dark);

            // Switch default and checked visuals follow shadcn contrast and geometry.
            var shadcnSwitch = YanziUi.WithStyle(
                new CheckBox { IsChecked = true }, YanziUi.Styles.SwitchShadcn);
            window.Content = shadcnSwitch;
            window.UpdateLayout();
            shadcnSwitch.ApplyTemplate();
            var switchTrack = shadcnSwitch.Template.FindName("Track", shadcnSwitch) as Border;
            var switchThumb = shadcnSwitch.Template.FindName("Thumb", shadcnSwitch) as System.Windows.Shapes.Ellipse;
            Check(switchTrack?.Width == 36 && switchTrack.Height == 20
                && switchThumb?.Width == 16 && switchThumb.Height == 16,
                "Switch has a 36x20 track with a 16x16 sliding thumb");
            Check(Math.Abs(System.Windows.Controls.Canvas.GetLeft(switchThumb!) - 18) < 0.1,
                "Switch checked thumb sits on right end");
            Check(switchTrack!.Background is SolidColorBrush activeTrack
                && activeTrack.Color == RequireBrush(window, "Yanzi.Color.Primary").Color,
                "Switch checked track uses Primary token");
            Check(switchThumb!.Fill is SolidColorBrush activeThumb
                && activeThumb.Color == RequireBrush(window, "Yanzi.Color.PrimaryForeground").Color,
                "Switch checked thumb contrasts with active track");
            shadcnSwitch.IsChecked = false;
            window.UpdateLayout();
            Check(Math.Abs(System.Windows.Controls.Canvas.GetLeft(switchThumb) - 2) < 0.1,
                "Switch unchecked thumb returns to left end");
            Check(switchTrack.Background is SolidColorBrush inactiveTrack
                && inactiveTrack.Color == RequireBrush(window, "Yanzi.Color.Input").Color,
                "Switch unchecked track uses input-muted token");

            // Vector chevron must have a geometric center independent of glyph baseline.
            var chevron = YanziIcons.ChevronUp(16);
            Check(chevron is Viewbox vb && vb.Width == vb.Height
                && vb.HorizontalAlignment == HorizontalAlignment.Center
                && vb.VerticalAlignment == VerticalAlignment.Center,
                "ChevronUp is a centered 16x16 vector, not a text glyph");

            // Popup uses a custom border/card and menu-item Button controls.
            var dropdown = new YanziDropdownMenu();
            var menuClicked = false;
            dropdown.AddLabel("操作");
            var action = dropdown.AddAction("复制", () => menuClicked = true);
            dropdown.AddSeparator();
            dropdown.AddAction("设置", () => menuClicked = true);
            Check(dropdown.Count == 2 && dropdown.Surface is Border popupCard
                && popupCard.CornerRadius.TopLeft >= 8,
                "custom upward dropdown has rounded WPF card and reusable actions");
            var popupTrigger = new Button { Content = "Menu", Width = 45, Height = 32 };
            dropdown.Attach(popupTrigger);
            var menuHost = new StackPanel();
            menuHost.Children.Add(popupTrigger);
            window.Content = menuHost;
            window.UpdateLayout();
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(menuClicked && !dropdown.IsOpen,
                "dropdown action callback runs and menu closes");

            // Modal dialog is a borderless dimming overlay rather than a native tool window.
            var dialogPreview = new YanziAlertDialog(window, "确认执行操作？",
                "这是组件展示，不会修改实际数据。", "确认");
            Check(dialogPreview.WindowStyle == WindowStyle.None
                && dialogPreview.AllowsTransparency && !dialogPreview.ShowInTaskbar,
                "Alert Dialog does not render Windows native toolwindow chrome");
            Check(dialogPreview.Overlay.Background is SolidColorBrush overlayBrush
                && overlayBrush.Color.A >= 140,
                "Alert Dialog dims the entire owner surface");
            Check(dialogPreview.Card.CornerRadius.TopLeft == 12
                && dialogPreview.Card.BorderThickness.Left == 1
                && dialogPreview.Card.Width >= 420,
                "Alert Dialog has shadcn-style rounded, bordered content card");
            Check(dialogPreview.CancelButton.IsCancel && dialogPreview.ConfirmButton.IsDefault,
                "Alert Dialog retains keyboard cancellation and default confirmation");
            dialogPreview.Loaded += (_, _) =>
                dialogPreview.Dispatcher.BeginInvoke(new Action(() =>
                    dialogPreview.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Check(dialogPreview.ShowDialog() == false,
                "Alert Dialog visually opens modally and cancel closes without confirmation");
            var acceptPreview = new YanziAlertDialog(window, "Confirm test", "safe test only");
            acceptPreview.Loaded += (_, _) =>
                acceptPreview.Dispatcher.BeginInvoke(new Action(() =>
                    acceptPreview.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Check(acceptPreview.ShowDialog() == true,
                "Alert Dialog confirm action closes modal and reports success");

            // Autocomplete combobox uses a custom TextBox + Popup, never native ComboBox.
            var filterCombo = new YanziCombobox("Select a framework");
            filterCombo.Add("Next.js");
            filterCombo.Add("Nuxt.js");
            filterCombo.Add("SvelteKit");
            Check(filterCombo.Items.Count == 3
                && filterCombo.FilteredCount == 3
                && filterCombo.GetType().BaseType == typeof(UserControl),
                "Combobox is custom WPF control with three indexed suggestions");
            filterCombo.SearchText = "NUXT";
            Check(filterCombo.FilteredCount == 1,
                "Combobox filters suggestions case-insensitively");
            filterCombo.SearchText = "no-match";
            Check(filterCombo.FilteredCount == 0,
                "Combobox exposes empty search result state");
            string? selectedCombo = null;
            filterCombo.SelectionChanged += (_, value) => selectedCombo = value;
            Check(filterCombo.Select("SvelteKit")
                && filterCombo.SelectedValue == "SvelteKit"
                && filterCombo.SearchText == "SvelteKit" && selectedCombo == "SvelteKit",
                "Combobox selection synchronizes display text, selected value and callback");
            Check(!filterCombo.Select("unknown")
                && filterCombo.SelectedValue == "SvelteKit",
                "Combobox rejects values not present in options");
            filterCombo.Clear();
            Check(filterCombo.SelectedValue is null && filterCombo.SearchText == ""
                && filterCombo.FilteredCount == 3 && selectedCombo is null,
                "Combobox clear returns to full suggestions and notifies consumers");
            filterCombo.IsEnabled = false;
            Check(!filterCombo.Select("Next.js"), "disabled Combobox cannot select");
            filterCombo.IsEnabled = true;
            bool duplicateComboRejected = false;
            try { filterCombo.Add("NUXT.JS"); }
            catch (ArgumentException) { duplicateComboRejected = true; }
            Check(duplicateComboRejected, "Combobox rejects duplicate values ignoring case");

            // True shadcn-style context-menu surface: actions, checked state,
            // right-click coordinate placement and safe close on activation.
            var customContext = new YanziContextMenu();
            bool contextActivated = false;
            bool bookmarkChecked = true;
            customContext.AddLabel("Quick actions");
            var ctxAction = customContext.AddAction("Reload", () => contextActivated = true, "Ctrl+R");
            customContext.AddSeparator();
            var ctxCheck = customContext.AddCheck("Show bookmarks", true, state => bookmarkChecked = state);
            var rightClickRegion = new Border { Width = 240, Height = 85, Focusable = true };
            customContext.Attach(rightClickRegion);
            Check(customContext.Count == 1 && customContext.Surface is Border ctxSurface
                && ctxSurface.CornerRadius.TopLeft == 9,
                "Context Menu renders its own rounded WPF popup and action rows");
            Check(rightClickRegion.ContextMenu is null,
                "Context Menu does not instantiate Windows native ContextMenu");
            window.Content = rightClickRegion;
            window.UpdateLayout();
            customContext.OpenAt(new Point(34, 42));
            Check(customContext.IsOpen,
                "Context Menu opens at a relative pointer coordinate");
            ctxCheck.IsChecked = false;
            Check(!bookmarkChecked, "Context Menu checkbox updates its consumer");
            ctxAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(contextActivated && !customContext.IsOpen,
                "Context Menu action invokes callback and closes popup");

            // General Dialog owns editable child content independently of Alert Dialog.
            var dialogForm = new StackPanel();
            var dialogText = new TextBox { Text = "Initial profile" };
            dialogForm.Children.Add(dialogText);
            var dialog = new YanziContentDialog(window, "Edit profile",
                "Make changes here.", dialogForm);
            Check(dialog.WindowStyle == WindowStyle.None
                && dialog.AllowsTransparency && !dialog.ShowInTaskbar
                && dialog.BodyPresenter.Content == dialogForm,
                "Dialog is a borderless overlay with arbitrary editable content");
            Check(dialog.Card.CornerRadius.TopLeft == 12
                && dialog.Card.BorderThickness.Left == 1
                && dialog.CancelButton.IsCancel && dialog.SaveButton.IsDefault,
                "Dialog has shadcn card geometry and default/cancel actions");
            dialog.Loaded += (_, _) =>
                dialog.Dispatcher.BeginInvoke(new Action(() =>
                    dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Check(dialog.ShowDialog() == false,
                "General Dialog supports modal cancel without touching input");
            var savedForm = new StackPanel();
            var savedInput = new TextBox { Text = "Before" };
            savedForm.Children.Add(savedInput);
            var savedDialog = new YanziContentDialog(window, "Edit profile",
                "Change input.", savedForm);
            savedDialog.Loaded += (_, _) =>
                savedDialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    savedInput.Text = "After";
                    savedDialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
            Check(savedDialog.ShowDialog() == true && savedInput.Text == "After",
                "General Dialog preserves edited content and returns successful save");
            // Sheet/Drawer are now owner-sized overlays rather than OS titlebar windows.
            var sideBody = new StackPanel();
            sideBody.Children.Add(new TextBox { Text = "Demo" });
            var rightSheet = new YanziSheetOverlay(window, "Sheet test", sideBody,
                YanziSheetSide.Right);
            Check(rightSheet.WindowStyle == WindowStyle.None
                && rightSheet.AllowsTransparency && !rightSheet.ShowInTaskbar,
                "Sheet uses borderless WPF owner overlay instead of a tool window");
            Check(rightSheet.Overlay.Background is SolidColorBrush sheetDim
                && sheetDim.Color.A >= 150
                && rightSheet.Panel.HorizontalAlignment == HorizontalAlignment.Right
                && rightSheet.Panel.Width == 390,
                "Sheet right edge placement and backdrop alpha match overlay design");
            rightSheet.Loaded += (_, _) =>
                rightSheet.Dispatcher.BeginInvoke(new Action(() =>
                    rightSheet.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            rightSheet.ShowDialog();
            Check(!rightSheet.IsVisible, "Sheet modal closes via dedicated close button");

            var drawerBody = new StackPanel();
            drawerBody.Children.Add(new TextBlock { Text = "Drawer content" });
            var bottomDrawer = new YanziSheetOverlay(window, "Drawer test", drawerBody,
                YanziSheetSide.Bottom);
            Check(bottomDrawer.Panel.VerticalAlignment == VerticalAlignment.Bottom
                && bottomDrawer.Panel.Height == 335 && bottomDrawer.DrawerHandle is not null
                && bottomDrawer.Panel.CornerRadius.TopLeft == 16
                && bottomDrawer.Panel.CornerRadius.TopRight == 16,
                "Drawer has bottom anchored panel, rounded top corners and visual handle");
            bottomDrawer.Loaded += (_, _) =>
                bottomDrawer.Dispatcher.BeginInvoke(new Action(() =>
                    bottomDrawer.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            bottomDrawer.ShowDialog();
            Check(!bottomDrawer.IsVisible, "Drawer overlay closes without a native window titlebar");
            var leftSheet = new YanziSheetOverlay(window, "Left sheet", new TextBlock { Text = "Left" },
                YanziSheetSide.Left);
            var topSheet = new YanziSheetOverlay(window, "Top sheet", new TextBlock { Text = "Top" },
                YanziSheetSide.Top);
            Check(leftSheet.Panel.HorizontalAlignment == HorizontalAlignment.Left
                && topSheet.Panel.VerticalAlignment == VerticalAlignment.Top,
                "Sheet supports all four edge placements through shared API");

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
