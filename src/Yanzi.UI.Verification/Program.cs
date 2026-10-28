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
            Check(carousel.Step > 0 && carousel.Count == 2 && carousel.SelectedIndex == 0,
                "Carousel exposes item count, position and scroll increment");
            Check(carousel.Next() && carousel.SelectedIndex == 1 && !carousel.Next(),
                "Carousel moves to next card and prevents overflow");
            Check(carousel.Previous() && carousel.SelectedIndex == 0 && !carousel.Previous(),
                "Carousel moves back and respects first-card boundary");
            carousel.Loop = true;
            Check(carousel.Previous() && carousel.SelectedIndex == 1,
                "Carousel optionally wraps last to first");
            carousel.GoTo(0);
            Check(carousel.SnapToItems && carousel.SelectedIndex == 0 && carousel.DragEnabled,
                "Carousel supports item-aligned pointer dragging without breaking existing navigation");
            var cardWidths = new[] { 115d, 115d, 115d, 115d };
            Check(YanziCarousel.FindSnapIndex(cardWidths, 0, 130, false) == 0,
                "Carousel snaps to first item when scrolled to the beginning");
            Check(YanziCarousel.FindSnapIndex(cardWidths, 112, 130, true) == 1,
                "Carousel snaps to nearest interior card");
            Check(YanziCarousel.FindSnapIndex(cardWidths, 130, 130, true) == 3
                && YanziCarousel.FindSnapIndex(cardWidths, 130, 130, false) == 2,
                "Carousel resolves clamped end-of-scroll ties by drag direction");
            var invalidCardWidthRejected = false;
            try { YanziCarousel.FindSnapIndex(new[] { 0d }, 0, 100, true); }
            catch (ArgumentOutOfRangeException) { invalidCardWidthRejected = true; }
            Check(invalidCardWidthRejected, "Carousel rejects invalid measured card widths");

            Check(YanziPrimitives.Avatar("YZ") is YanziAvatar,
                "Avatar primitive uses shared image-capable control");
            var photoAvatar = (YanziAvatar)YanziPrimitives.Avatar("YZ", 36);
            Check(photoAvatar.IsShowingFallback && photoAvatar.Diameter == 36,
                "Avatar uses initials when image is missing");
            photoAvatar.ImageSource = new DrawingImage();
            Check(!photoAvatar.IsShowingFallback, "Avatar accepts a real ImageSource");
            photoAvatar.ImageSource = null;
            Check(photoAvatar.IsShowingFallback, "Avatar restores initials when image is cleared");
            Check(YanziPrimitives.Kbd("Ctrl") is Border, "keyboard primitive");
            Check(YanziPrimitives.Separator() is Border, "separator primitive");
            Check(YanziPrimitives.Skeleton(100, 16) is Border, "skeleton primitive");
            Check(YanziPrimitives.Alert("Title", "Body") is Border, "alert primitive");
            Check(YanziPrimitives.EmptyState("Title", "Subtitle") is StackPanel, "empty state primitive");
            var linkedInput = new TextBox();
            var linkedLabel = YanziPrimitives.Label("姓名", linkedInput, required: true);
            Check(linkedLabel.Text.Contains("姓名") && linkedLabel.Text.Contains("*"),
                "Label supports a required marker");
            Check(ReferenceEquals(System.Windows.Automation.AutomationProperties.GetLabeledBy(linkedInput), linkedLabel),
                "Label is associated with the input for assistive technology");
            var field = YanziPrimitives.Field("Label", linkedInput);
            Check(field.Children[0] is YanziLabel && field.Children[1] == linkedInput,
                "Field composes reusable Label and its input");

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
            // Native Expander behavior keeps its state, while the entire chrome is public UI.
            window.Content = collapse;
            collapse.ApplyTemplate();
            Check(collapse.Template?.FindName("HeaderToggle", collapse) is ToggleButton,
                "Expander owns the shared trigger and chevron template");
            collapse.IsExpanded = true;
            Check(collapse.IsExpanded, "Expander retains native two-way expand/collapse semantics");
            var nativeRadio = YanziUi.WithStyle(new RadioButton { Content = "选项" }, YanziUi.Styles.Radio);
            window.Content = nativeRadio;
            nativeRadio.ApplyTemplate();
            Check(nativeRadio.Style != null && nativeRadio.Template != null,
                "legacy RadioButton now uses an explicit custom dot template");
            nativeRadio.IsChecked = true;
            Check(nativeRadio.IsChecked == true, "re-templated RadioButton retains native checked behavior");

            var publicBar = new YanziMenubar();
            publicBar.AddMenu("文件", m =>
            {
                m.AddAction("打开", () => { });
                m.AddSubmenu("最近", sub => sub.AddAction("工程", () => { }));
            });
            publicBar.AddMenu("编辑", m => m.AddCheck("自动保存", true, _ => { }));
            Check(publicBar.MenuCount == 2 && publicBar.Menus[0].Count == 2,
                "shared Menubar composes two non-native dropdowns and nested submenu");
            Check(!publicBar.Menus[0].PreferAbove,
                "Menubar opens below its trigger, unlike the legacy upward menu");

            var sharedTable = new YanziTable();
            sharedTable.SetData(new[] { "名称", "数字" }, new IReadOnlyList<string>[]
            {
                new[] { "Bob", "20" }, new[] { "Alice", "10" }
            });
            sharedTable.SortBy(0);
            Check(sharedTable.Rows[0][0] == "Alice" && !sharedTable.Descending,
                "shared Table sorts first click in ascending order");
            sharedTable.SortBy(0);
            Check(sharedTable.Rows[0][0] == "Bob" && sharedTable.Descending,
                "shared Table reverses sorting on second click");
            sharedTable.SelectRow(1);
            Check(sharedTable.SelectedIndex == 1 && sharedTable.RowCount == 2,
                "shared Table tracks selected row and count");

            var advanced = new YanziDataTable { PageSize = 3 };
            advanced.SetData(new[] { "编号", "状态", "用户" }, new IReadOnlyList<string>[]
            {
                new[] { "INV001", "已付款", "小王" },
                new[] { "INV002", "未付款", "小李" },
                new[] { "INV003", "已付款", "小陈" },
                new[] { "INV004", "已付款", "小周" },
                new[] { "INV005", "未付款", "小孙" },
                new[] { "INV006", "已付款", "小吴" },
                new[] { "INV007", "已付款", "小赵" }
            });
            Check(advanced.RowCount == 7 && advanced.PageCount == 3 &&
                  advanced.Table.RowCount == 3 && advanced.VisibleRowCount == 3,
                "Data Table renders only the current page and computes page count");
            advanced.ToggleVisibleRow(1);
            Check(advanced.SelectedCount == 1 && advanced.SelectedIds.Single() == 1,
                "Data Table selects and identifies a source row");
            Check(advanced.NextPage() && advanced.PageIndex == 1 &&
                  advanced.VisibleRowIds.SequenceEqual(new[] { 3, 4, 5 }),
                "Data Table moves forward without reordering original source IDs");
            advanced.ToggleVisibleRow(1);
            Check(advanced.SelectedCount == 2 && advanced.SelectedIds.Contains(4),
                "Data Table preserves multiple selected IDs across pages");
            advanced.SetFilter("已付款", 1);
            Check(advanced.FilteredRowCount == 5 && advanced.PageIndex == 0
                  && advanced.PageCount == 2,
                "Data Table column filtering resets to page one");
            advanced.SortBy(0);
            advanced.SortBy(0);
            Check(advanced.SortDescending && advanced.VisibleRowIds[0] == 6,
                "Data Table sorting operates over all filtered pages");
            Check(advanced.Table.SortColumn == 0 && advanced.Table.Descending,
                "Data Table keeps the sort arrow state on rerendered headers");
            advanced.SetColumnVisible(2, false);
            Check(advanced.VisibleColumns.Count == 2 && advanced.Table.ColumnCount == 2,
                "Data Table hides a column without deleting the underlying data");
            advanced.SetColumnVisible(0, false);
            advanced.SetColumnVisible(1, false);
            Check(advanced.VisibleColumns.Count == 1,
                "Data Table never allows hiding every column");
            advanced.SetFilter("NO_MATCH");
            Check(advanced.FilteredRowCount == 0 && advanced.PageCount == 1 &&
                  !advanced.NextPage() && advanced.Table.RowCount == 0,
                "Data Table handles empty filter results without invalid pages");
            advanced.SetFilter("");
            advanced.PageSize = 2;
            Check(advanced.PageCount == 4 && advanced.PreviousPage() == false,
                "Data Table page size and previous-page bounds");
            var many = new YanziDataTable { PageSize = 10 };
            many.SetData(new[] { "ID" }, Enumerable.Range(1, 10000)
                .Select(x => (IReadOnlyList<string>)new[] { x.ToString() }));
            Check(many.RowCount == 10000 && many.VisibleRowCount == 10 &&
                  many.Table.RowCount == 10,
                "Data Table bounds WPF row creation for large in-memory datasets");

            var nav = new YanziNavigationMenu();
            nav.AddGroup("入门", new[]
            {
                new YanziNavigationMenu.Link("介绍", "产品介绍", () => { }),
                new YanziNavigationMenu.Link("安装", "本地安装", () => { })
            });
            nav.AddGroup("组件", new[]
            {
                new YanziNavigationMenu.Link("数据表格", "筛选与分页", () => { })
            });
            Check(nav.GroupCount == 2 && !nav.IsOpen,
                "Navigation Menu has two groups and begins closed");
            Check(nav.ViewportMinWidth >= 455 && nav.GetLinks(0).Count == 2,
                "Navigation Menu uses a wide viewport and stores full link descriptions");
            Check(nav.GetLinks(1).Single().Title == "数据表格",
                "Navigation Menu preserves group-specific navigation destinations");
            nav.Close();
            Check(!nav.IsOpen && nav.ActiveGroupIndex == -1,
                "Navigation Menu dismisses safely without an on-screen owner");
            var scrollArea = YanziLayoutPrimitives.ScrollArea(new TextBlock(), 170);
            Check(scrollArea.Height == 170, "scroll area size");
            Check(window.TryFindResource("Yanzi.Scrollbar") is Style,
                "shared ScrollArea scrollbar chrome is registered for themed scrolling");
            var sideways = YanziLayoutPrimitives.ScrollArea(new StackPanel(), 90, horizontal: true);
            Check(sideways.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto &&
                sideways.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Horizontal Scroll Area enables x-axis and disables y-axis");
            Check(window.TryFindResource("Yanzi.Scrollbar.Horizontal") is Style,
                "Horizontal Scroll Area uses reusable horizontal scrollbar template");
            var resizable = YanziLayoutPrimitives.Resizable(new TextBlock(), new TextBlock(), 140, 180);
            Check(resizable.ColumnDefinitions.Count == 3, "resizable columns");
            var sharedGrip = resizable.Children.OfType<GridSplitter>().Single();
            Check(sharedGrip.KeyboardIncrement == 8 && sharedGrip.DragIncrement == 2,
                "Resizable preserves native pointer drag and keyboard resize increments");
            Check(System.Windows.Automation.AutomationProperties.GetName(sharedGrip) == "调整面板宽度"
                && window.TryFindResource("Yanzi.ResizableHandle") is Style,
                "Resizable uses the shared accessible themed grip");
            Check(sharedGrip.ResizeDirection == GridResizeDirection.Columns,
                "Horizontal Resizable explicitly binds splitter to columns");
            var upDown = YanziLayoutPrimitives.ResizableVertical(new TextBlock(),
                new TextBlock(), 115, 285);
            var heightGrip = upDown.Children.OfType<GridSplitter>().Single();
            Check(upDown.RowDefinitions.Count == 3
                && heightGrip.ResizeDirection == GridResizeDirection.Rows
                && System.Windows.Automation.AutomationProperties.GetName(heightGrip) == "调整面板高度",
                "Vertical Resizable exposes an accessible top/bottom splitter");
            Check(window.TryFindResource(YanziUi.Styles.ResizableHandleVertical) is Style,
                "Vertical Resizable renders its own horizontal grip template");
            YanziLayoutPrimitives.SetResizableFirstSize(upDown, 143, vertical: true);
            Check(Math.Abs(YanziLayoutPrimitives.GetResizableFirstSize(upDown, vertical: true)-143)<0.01,
                "Vertical Resizable snapshots and restores its height");
            YanziLayoutPrimitives.SetResizableFirstSize(upDown, 9999, vertical: true);
            Check(YanziLayoutPrimitives.GetResizableFirstSize(upDown, vertical: true) <= 215,
                "Vertical Resizable keeps at least 60 DIP for the lower pane");
            YanziLayoutPrimitives.SetResizableFirstSize(resizable, 48);
            Check(YanziLayoutPrimitives.GetResizableFirstSize(resizable) >= 60,
                "Horizontal Resizable clamps restored width to minimum");
            var invalidResizeRejected = false;
            try { YanziLayoutPrimitives.SetResizableFirstSize(upDown, double.NaN, vertical: true); }
            catch (ArgumentOutOfRangeException) { invalidResizeRejected = true; }
            Check(invalidResizeRejected, "Resizable rejects invalid saved sizes");
            var aspect = YanziLayoutPrimitives.AspectRatio(new Border(), 16.0 / 9.0, 160);
            Check(Math.Abs(aspect.Height - 90) < 0.01, "aspect ratio calculation");

            Check(YanziComponentRegistry.Components.All(c => c.Status is YanziComponentStatus.Ready or YanziComponentStatus.Preview),
                "all indexed components expose a reusable entry point or an explicitly marked preview");
            Check(YanziComponentRegistry.Components.Any(c => c.Name == "Data Table" && c.Status == YanziComponentStatus.Preview)
                && YanziComponentRegistry.Components.Any(c => c.Name == "Navigation Menu" && c.Status == YanziComponentStatus.Preview),
                "complex non-parity table and navigation variants are labeled Preview rather than fully Ready");
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
            var multiChart = new YanziChart { Width = 330, Height = 200 };
            multiChart.SetData(new[]
            {
                new YanziBarPoint("周一", 25),
                new YanziBarPoint("周二", 65),
                new YanziBarPoint("周三", 10)
            });
            Check(multiChart.PointCount == 3 && multiChart.RenderedPointCount == 3,
                "Shared Chart renders one interactive target per bar");
            var chartTree = (Grid)multiChart.Content;
            var chartPlot = chartTree.Children.OfType<Canvas>().Single();
            var focusableBars = chartPlot.Children.OfType<Button>().ToArray();
            Check(focusableBars.Length == 3 &&
                System.Windows.Automation.AutomationProperties.GetName(focusableBars[0]).StartsWith("周一"),
                "Chart uses real accessible Buttons for the three graphical data points");
            focusableBars[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(multiChart.SelectedIndex == 1,
                "Chart accessible point button actually selects its underlying datum");
            Check(window.TryFindResource("Yanzi.Color.Chart1") is System.Windows.Media.Brush
                && window.TryFindResource("Yanzi.Color.Chart5") is System.Windows.Media.Brush,
                "Five semantic chart colors follow both themes");
            var chartSelectedPoint = -1;
            multiChart.SelectedPointChanged += (_, i) => chartSelectedPoint = i;
            multiChart.SelectPoint(1);
            Check(multiChart.SelectedIndex == 1 && chartSelectedPoint == 1,
                "Shared Chart exposes selected data point");
            foreach (var kind in new[] { YanziChartKind.Line, YanziChartKind.Area, YanziChartKind.Pie })
            {
                multiChart.Kind = kind;
                Check(multiChart.Kind == kind && multiChart.RenderedPointCount == 3,
                    "Shared Chart renders " + kind + " with three interactive points");
            }
            var pieLegend = chartTree.Children.OfType<WrapPanel>().Single()
                .Children.OfType<Button>().ToArray();
            Check(pieLegend.Length == 3 &&
                System.Windows.Automation.AutomationProperties.GetName(pieLegend[0]).StartsWith("周一"),
                "Pie Chart exposes focusable data-driven legend choices");
            pieLegend[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(multiChart.SelectedIndex == 2,
                "Pie Chart interactive legend selects its matching sector");
            multiChart.SetData(new[] { new YanziBarPoint("整体", 20) });
            Check(multiChart.RenderedPointCount == 1,
                "Pie chart supports a single full-circle sector");
            multiChart.SetData(new[]
            {
                new YanziBarPoint("零", 0), new YanziBarPoint("仍零", 0)
            });
            Check(multiChart.PointCount == 2 && multiChart.RenderedPointCount == 0,
                "Pie chart handles zero-sum data without invalid geometry");
            var invalidChart = false;
            try { multiChart.SetData(new[] { new YanziBarPoint("无效", double.NaN) }); }
            catch (ArgumentOutOfRangeException) { invalidChart = true; }
            Check(invalidChart, "Shared Chart rejects non-finite values");

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
            string? menuMode = null;
            var menuRadios = customContext.AddRadioGroup("View mode",
                new[] { ("Comfortable", "comfortable"), ("Compact", "compact") },
                "comfortable", value => menuMode = value);
            Check(menuRadios.Items.Count == 2 && menuRadios.SelectedValue == "comfortable",
                "Context Menu radio group starts with a single checked choice");
            menuRadios.Select("compact");
            Check(menuMode == "compact" && menuRadios.Items[1].IsChecked
                && !menuRadios.Items[0].IsChecked,
                "Context Menu radio options remain exclusive and notify selection");
            bool submenuExecuted = false;
            var submenuTrigger = customContext.AddSubmenu("More tools", submenu =>
            {
                submenu.AddAction("Copy link", () => submenuExecuted = true);
                submenu.AddAction("Inspect", () => submenuExecuted = true);
            });
            Check(customContext.SubmenuCount == 2 && !customContext.IsSubmenuOpen,
                "Context Menu exposes two nested actions, initially collapsed");
            customContext.OpenAt(new Point(18, 24));
            customContext.OpenSubmenu();
            Check(customContext.IsSubmenuOpen,
                "Context Menu exposes a second adjacent panel without native popups");
            customContext.HideSubmenu();
            Check(!customContext.IsSubmenuOpen,
                "Context Menu submenu can collapse without closing the parent");
            customContext.OpenSubmenu(focusFirst: true);
            var nestedButton = customContext.Surface.Parent is StackPanel nestedRoot
                ? ((Border)nestedRoot.Children[1]).Child as StackPanel : null;
            var nestedAction = nestedButton?.Children.OfType<Button>().FirstOrDefault();
            Check(nestedAction is not null, "nested menu button lives in the same popup visual tree");
            nestedAction!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(submenuExecuted && !customContext.IsOpen,
                "nested menu action closes parent popup and calls the consumer");

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
            Check(dialog.Card.CornerRadius.TopLeft == 14
                && dialog.Card.Width == 384 && dialog.Card.Padding.Left == 16
                && dialog.Card.BorderThickness.Left == 1
                && !dialog.CancelButton.IsCancel && dialog.SaveButton.IsDefault,
                "Dialog has shadcn card geometry and animated cancellation (no WPF auto-close)");
            Check(System.Windows.Input.KeyboardNavigation.GetTabNavigation(dialog.Card) ==
                System.Windows.Input.KeyboardNavigationMode.Cycle,
                "Dialog Tab/Shift+Tab navigation cycles within the modal card");
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
            Check(bottomDrawer.DrawerGrip is not null
                && bottomDrawer.DrawerGrip.IsManipulationEnabled
                && bottomDrawer.DrawerSnapPoints.Count == 3,
                "Drawer provides mouse/touch drag surface and three snap positions");
            Check(bottomDrawer.SnapDrawerTo(.65)
                && Math.Abs(bottomDrawer.CurrentDrawerFraction - .65) < .001,
                "Drawer can settle on the middle snap point");
            var h = bottomDrawer.Height;
            Check(bottomDrawer.CompleteDrawerDrag(.89 * h)
                && Math.Abs(bottomDrawer.CurrentDrawerFraction - .90) < .001,
                "Drawer drag release snaps to the nearest height");
            Check(bottomDrawer.CompleteDrawerDrag(.42 * h)
                && Math.Abs(bottomDrawer.CurrentDrawerFraction - .40) < .001,
                "Drawer drag release can snap back to the lowest expanded height");
            bottomDrawer.Loaded += (_, _) =>
                bottomDrawer.Dispatcher.BeginInvoke(new Action(() =>
                    bottomDrawer.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            bottomDrawer.ShowDialog();
            Check(!bottomDrawer.IsVisible, "Drawer overlay closes without a native window titlebar");
            Check(!bottomDrawer.SnapDrawerTo(.50),
                "Drawer rejects unconfigured snap points");
            var closeByDrag = new YanziSheetOverlay(window, "Swipe-dismiss preview",
                new TextBlock { Text = "Dismiss test" }, YanziSheetSide.Bottom);
            bool thresholdDismissed = false;
            closeByDrag.Loaded += (_, _) =>
                closeByDrag.Dispatcher.BeginInvoke(new Action(() =>
                    thresholdDismissed = closeByDrag.CompleteDrawerDrag(closeByDrag.Height * .10)));
            closeByDrag.ShowDialog();
            Check(thresholdDismissed && !closeByDrag.IsVisible,
                "Dragging a Drawer below dismiss threshold closes the modal");
            var leftSheet = new YanziSheetOverlay(window, "Left sheet", new TextBlock { Text = "Left" },
                YanziSheetSide.Left);
            var topSheet = new YanziSheetOverlay(window, "Top sheet", new TextBlock { Text = "Top" },
                YanziSheetSide.Top);
            Check(leftSheet.Panel.HorizontalAlignment == HorizontalAlignment.Left
                && topSheet.Panel.VerticalAlignment == VerticalAlignment.Top,
                "Sheet supports all four edge placements through shared API");

            // Reference-representative controls must expose real interaction, not tiny placeholders.
            var featureAccordion = new YanziAccordion();
            featureAccordion.Add("Shipping", new TextBlock { Text = "Worldwide" });
            featureAccordion.Add("Returns", new TextBlock { Text = "30 days" });
            featureAccordion.Add("Support", new TextBlock { Text = "Email" });
            featureAccordion.Expand(1);
            Check(featureAccordion.SectionCount == 3 && featureAccordion.ExpandedIndex == 1,
                "Reference Accordion starts with three rows and the middle row expanded");
            featureAccordion.Expand(0);
            Check(featureAccordion.ExpandedIndex == 0,
                "Accordion single-open behavior switches visible row");
            var showcaseCalendar = new YanziCalendarMonth(new DateTime(2026, 10, 1));
            DateTime? selectedCalendarDay = null;
            showcaseCalendar.DateSelected += (_, day) => selectedCalendarDay = day;
            showcaseCalendar.SelectDate(new DateTime(2026, 10, 8));
            Check(showcaseCalendar.SelectedDate == new DateTime(2026, 10, 8)
                && selectedCalendarDay == new DateTime(2026, 10, 8),
                "Reference Calendar selects October 8 and raises a DateSelected event");
            showcaseCalendar.Navigate(1);
            Check(showcaseCalendar.DisplayedMonth.Month == 11
                && showcaseCalendar.DisplayedMonth.Year == 2026,
                "Custom month Calendar supports real forward/back navigation");
            var dateControl = new YanziDatePicker
            {
                SelectedDate = new DateTime(2026, 10, 8),
                Placeholder = "选择日期"
            };
            Check(dateControl.SelectedDate == new DateTime(2026, 10, 8)
                && !dateControl.IsDropDownOpen,
                "Shared DatePicker stores selected date and starts with popup closed");
            DateTime? dateControlResult = null;
            dateControl.SelectedDateChanged += (_, date) => dateControlResult = date;
            dateControl.SelectedDate = new DateTime(2026, 10, 15);
            Check(dateControlResult == new DateTime(2026, 10, 15),
                "Shared DatePicker announces selected date updates");
            var boundedMonth = new YanziCalendarMonth(new DateTime(2026, 10, 1))
            {
                MinimumDate = new DateTime(2026, 10, 10),
                MaximumDate = new DateTime(2026, 10, 20)
            };
            boundedMonth.SelectDate(new DateTime(2026, 10, 9));
            Check(boundedMonth.SelectedDate is null,
                "Calendar rejects dates below its minimum");
            boundedMonth.SelectDate(new DateTime(2026, 10, 15));
            boundedMonth.SelectDate(new DateTime(2026, 10, 21));
            Check(boundedMonth.SelectedDate == new DateTime(2026, 10, 15),
                "Calendar accepts dates within the allowed interval and rejects later ones");
            var refinedAlert = YanziPrimitives.Alert("Payment successful", "Receipt sent.", icon: "✓");
            Check(refinedAlert.Child is Grid alertLayout
                && alertLayout.ColumnDefinitions.Count == 2,
                "Shared Alert primitive now draws icon beside title and description");

            // Source-driven layout contract: representative layout, not merely API existence.
            var sourceAccordion = new YanziAccordion();
            sourceAccordion.Add("First", new TextBlock { Text = "One" });
            sourceAccordion.Add("Second", new TextBlock { Text = "Two" });
            var sourceAccordionStack = sourceAccordion.Content as StackPanel;
            Check(sourceAccordionStack is not null
                && sourceAccordionStack.Children.Count == 2
                && sourceAccordionStack.Children[1] is StackPanel finalSection
                && finalSection.Children.Count == 2,
                "Source Accordion renders not-last:border-b (no extra final divider)");
            var october2026 = new YanziCalendarMonth(new DateTime(2026, 10, 1));
            Check(october2026.VisibleWeeks == 5 && october2026.VisibleDayCount == 35,
                "Calendar adjusts October 2026 to five visible week rows");
            october2026.Navigate(-2);
            Check(october2026.VisibleWeeks == 6 && october2026.VisibleDayCount == 42,
                "Calendar dynamically shows six week rows in August 2026");
            var compactAlert = YanziPrimitives.Alert("Heading", "Copy");
            Check(compactAlert.Padding.Left == 10 && compactAlert.Padding.Top == 8,
                "Alert uses official px-2.5 py-2 spacing");
            var sourceCard = new YanziCard();
            sourceCard.SetHeader(new TextBlock { Text = "Header" });
            sourceCard.SetBody(new TextBox { Text = "Input" });
            sourceCard.SetFooter(new Button { Content = "Save" });
            Check(sourceCard.Header is TextBlock && sourceCard.Body is TextBox
                && sourceCard.Footer is Button
                && sourceCard.BorderThickness.Left == 1,
                "YanziCard exposes real Header, Content and Footer slots");
            Check(sourceCard.ClipToBounds,
                "Card clips nested content to its shared rounded outer boundary");
            var settingsCard = new YanziSettingsSectionCard("Settings", "General options");
            settingsCard.AddRow(new TextBlock { Text = "Theme" });
            settingsCard.AddRow(new TextBlock { Text = "Startup" },
                new Border { Height = 1 });
            settingsCard.AddRow(new TextBlock { Text = "Cloud" },
                new Border { Height = 1 });
            Check(settingsCard.RowCount == 3 && settingsCard.DividerCount == 2
                && settingsCard.Rows.Children.Count == 5
                && settingsCard.Rows.Children[^1] is TextBlock,
                "SettingsCard has separators only between rows and none after the last row");
            Check(settingsCard.Card.BodyPadding.Bottom == 4,
                "SettingsCard ends near the rounded outer border without trailing empty padding");

            // The second source batch must expose native behavior, not only Gallery examples.
            var sourceButton = YanziUi.WithStyle(new Button { Content = "Default" },
                YanziUi.Styles.DefaultButton);
            var sourceInput = YanziUi.WithStyle(new TextBox { Text = "Hello" },
                YanziUi.Styles.Input);
            // Dynamic styles resolve only once elements enter an owner with theme resources.
            var sourceStyleHost = new StackPanel();
            sourceStyleHost.Children.Add(sourceButton);
            sourceStyleHost.Children.Add(sourceInput);
            window.Content = sourceStyleHost;
            Check(sourceButton.MinHeight == 32 && sourceButton.MinWidth == 0
                && sourceButton.Padding.Left == 10 && sourceButton.FontSize == 14,
                "Button base uses source 32 DIP height, content width and 14 DIP text");
            Check(sourceInput.MinHeight == 32 && sourceInput.Padding.Left == 10
                && sourceInput.FontSize == 16,
                "Input exposes source 32 DIP height, 10 DIP padding, and 16 DIP text");
            var sourceSelect = new YanziSelect("Choose an option");
            sourceSelect.Add("Apple");
            sourceSelect.Add("Banana");
            string? selectedSourceValue = null;
            sourceSelect.SelectionChanged += (_, value) => selectedSourceValue = value;
            Check(sourceSelect.Options.Count == 2 && sourceSelect.SelectedValue is null,
                "Native Select holds two WPF options and an initially empty value");
            Check(sourceSelect.Select("Banana") && selectedSourceValue == "Banana"
                && sourceSelect.SelectedValue == "Banana",
                "Select updates chosen text and reports selection");
            Check(!sourceSelect.Select("Nonexistent") && sourceSelect.SelectedValue == "Banana",
                "Select rejects values that are not in its option collection");
            var selectedVisual = (Grid)sourceSelect.OptionButtons[1].Content;
            var unselectedVisual = (Grid)sourceSelect.OptionButtons[0].Content;
            Check(selectedVisual.Children.OfType<Viewbox>().Single().Visibility == Visibility.Visible
                && unselectedVisual.Children.OfType<Viewbox>().Single().Visibility == Visibility.Hidden,
                "Select uses a vector checkmark only for the currently selected item");
            bool toggleState = false;
            var checkedDropdown = new YanziDropdownMenu();
            var checkAction = checkedDropdown.AddCheck("Show status", true, value => toggleState = value);
            checkAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(toggleState == false, "Dropdown checkbox item toggles without relying on OS menu");
            string? radioValue = null;
            var sourceMenuRadios = checkedDropdown.AddRadioGroup("Theme", new[] { "Light", "Dark" },
                "Dark", value => radioValue = value);
            sourceMenuRadios[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(radioValue == "Light" && sourceMenuRadios.Count == 2,
                "Dropdown radio group updates one exclusive selection");
            var checkVisual = (Grid)checkAction.Content;
            var radioVisual = (Grid)sourceMenuRadios[0].Content;
            Check(checkVisual.Children.OfType<Viewbox>().Count() == 1
                && radioVisual.Children.OfType<Viewbox>().Count() == 1
                && radioVisual.Children.OfType<Viewbox>().Single().Visibility == Visibility.Visible,
                "Dropdown check and radio states use native vector indicators");
            var tree = new YanziDropdownMenu();
            Button? exportTrigger = null;
            Button? pdfAction = null;
            bool pdfInvoked = false;
            var shareTrigger = tree.AddSubmenu("Share", child =>
            {
                child.AddAction("Copy", () => {});
                exportTrigger = child.AddSubmenu("Export as", grandChild =>
                    pdfAction = grandChild.AddAction("PDF", () => pdfInvoked = true));
            });
            Check(tree.OpenSubmenu(shareTrigger) && tree.OpenDepth == 1,
                "Dropdown submenu expands into a second level in the same Popup");
            Check(tree.OpenSubmenu(exportTrigger!) && tree.OpenDepth == 2,
                "Dropdown submenu supports a third recursively nested level");
            pdfAction!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(pdfInvoked && !tree.IsOpen && tree.OpenDepth == 0,
                "Clicking a nested leaf runs its action and closes the complete hierarchy");
            var plainDropdown = new YanziDropdownMenu();
            var shortcutItem = plainDropdown.AddAction("Profile", () => {}, "Ctrl+P");
            Check(shortcutItem.Content is Grid contentRow && contentRow.ColumnDefinitions.Count == 2,
                "Dropdown action renders label and keyboard shortcut in separate columns");
            Check(plainDropdown.PreferAbove,
                "Dropdown preserves legacy upward placement unless explicitly overridden");
            plainDropdown.PreferAbove = false;
            Check(!plainDropdown.PreferAbove,
                "New source-aligned Dropdown can explicitly prefer below-trigger placement");
            var sourceDialog = new YanziContentDialog(window, "Edit", "Change.", new TextBox());
            Check(sourceDialog.Card.Width == 384 && sourceDialog.Card.Padding.Left == 16
                && sourceDialog.Card.CornerRadius.TopLeft == 14,
                "Dialog dimensions match source 384 DIP card and 16 DIP padding");
            Check(sourceDialog.Card.Opacity == 0
                && sourceDialog.Card.RenderTransform is ScaleTransform,
                "Dialog is initialized for opacity and scale entrance animations");
            var smallOwner = new Window { Width = 320, Height = 350 };
            smallOwner.Show();
            var narrowDialog = new YanziContentDialog(smallOwner, "Narrow", "Small window", new TextBox());
            Check(narrowDialog.Width <= 320 && narrowDialog.Height <= 350
                && narrowDialog.BodyScroller is not null,
                "Dialog dimensions respect narrow owner windows before layout");
            narrowDialog.Close();
            smallOwner.Close();

            // Native Select and PasswordBox must not fall back to unrounded OS chrome.
            var sourcePassword = YanziUi.WithStyle(new PasswordBox { Password = "sample-only" },
                YanziUi.Styles.Password);
            var nativeSelect = YanziUi.WithStyle(new ComboBox { Width = 230 },
                YanziUi.Styles.Select);
            nativeSelect.Items.Add("Option one");
            nativeSelect.Items.Add("Option two");
            nativeSelect.SelectedIndex = 0;
            var editableNativeSelect = YanziUi.WithStyle(new ComboBox
                { Width = 230, IsEditable = true }, YanziUi.Styles.Select);
            editableNativeSelect.Items.Add("剪贴板");
            editableNativeSelect.Items.Add("日历");
            sourceStyleHost.Children.Add(sourcePassword);
            sourceStyleHost.Children.Add(nativeSelect);
            sourceStyleHost.Children.Add(editableNativeSelect);
            window.UpdateLayout();
            sourcePassword.ApplyTemplate();
            nativeSelect.ApplyTemplate();
            editableNativeSelect.ApplyTemplate();
            var pwdChrome = sourcePassword.Template.FindName("Chrome", sourcePassword) as Border;
            var pwdHost = sourcePassword.Template.FindName("PART_ContentHost", sourcePassword);
            Check(pwdChrome?.CornerRadius.TopLeft == 8 && pwdHost is ScrollViewer
                && sourcePassword.Password.Length == 11,
                "Password native entry uses rounded 8 DIP chrome and real PART_ContentHost");
            var selectChrome = nativeSelect.Template.FindName("Chrome", nativeSelect) as Border;
            var selectPopup = nativeSelect.Template.FindName("PART_Popup", nativeSelect) as Popup;
            Check(selectChrome?.CornerRadius.TopLeft == 8 && selectPopup?.Child is Border
                && nativeSelect.SelectedItem?.ToString() == "Option one",
                "Native Select has rounded trigger and theme-owned real WPF Popup");
            Check(nativeSelect.ItemContainerStyle is not null
                && nativeSelect.ItemContainerStyle.TargetType == typeof(ComboBoxItem),
                "Native Select applies dark themed dropdown-item templates");
            var selectedLabelPresenter = nativeSelect.Template.FindName("ContentSite",
                nativeSelect) as ContentPresenter;
            var selectedTextBrush = selectedLabelPresenter is not null
                ? System.Windows.Documents.TextElement.GetForeground(selectedLabelPresenter) : null;
            Check(selectedTextBrush is SolidColorBrush selectedTextColor
                && nativeSelect.Foreground is SolidColorBrush nativeTextColor
                && selectedTextColor.Color == nativeTextColor.Color,
                "Native Select forwards semantic foreground into its selected-text presenter");
            var editPart = editableNativeSelect.Template.FindName("PART_EditableTextBox",
                editableNativeSelect) as TextBox;
            Check(editPart?.Visibility == Visibility.Visible && editableNativeSelect.IsEditable,
                "Native Select retains editable ComboBox mode and keyboard text-host");
            nativeSelect.SelectedIndex = 1;
            Check(nativeSelect.SelectedItem?.ToString() == "Option two",
                "Native Select still changes selected WPF item after template replacement");

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
