using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Ownerless, entirely WPF-drawn dropdown. Supports nested submenus (arbitrary depth),
/// vector selection indicators, keyboard navigation and legacy upward placement.
/// </summary>
public sealed class YanziDropdownMenu
{
    internal sealed class Level
    {
        internal readonly StackPanel Rows = new() { Margin = new Thickness(5) };
        internal readonly List<Button> Items = [];
        internal readonly Border Panel;
        internal Level? Parent;
        internal Button? ParentTrigger;
        internal Level()
        {
            Panel = new Border
            {
                Width = 194, MinWidth = 128, VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
                BorderThickness = new Thickness(1), SnapsToDevicePixels = true,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18, ShadowDepth = 7, Opacity = .30, Color = Colors.Black
                }, Child = Rows
            };
            Panel.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
            Panel.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        }
    }

    private readonly Level _root = new();
    private readonly List<Level> _visible = [];
    private readonly Dictionary<Button, Level> _submenus = [];
    private readonly Dictionary<Button, FrameworkElement> _arrows = [];
    private readonly StackPanel _surface = new() { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
    private readonly Popup _popup;
    private readonly List<Button> _actions = [];
    private Button? _trigger;
    private bool _screenPlacement;
    private Point _screenRoot;
    private Rect _screenWorkArea;
    private Canvas? _screenCanvas;
    private double _screenReservedWidth;
    private double? _minimumContentWidth;

    // Existing consumers expect menus above their trigger; source-oriented pages opt out.
    public bool PreferAbove { get; set; } = true;
    // Menubars align the popup's leading edge with the button, rather than its trailing edge.
    public bool AlignStart { get; set; }
    /// <summary>Expand child panels to the left of their parents (e.g. right-edge tray menus).</summary>
    public bool OpenChildrenToLeft { get; set; }
    public double SubmenuWidth { get; set; } = 194;

    /// <summary>
    /// Size the root menu to its widest item's rendered text, icon and padding,
    /// while enforcing a minimum. Other dropdowns keep their existing fixed width.
    /// </summary>
    public void UseContentWidth(double minimumWidth = 176)
    {
        if (double.IsNaN(minimumWidth) || double.IsInfinity(minimumWidth) || minimumWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        _minimumContentWidth = minimumWidth;
        _root.Panel.Width = double.NaN;
        RefreshContentWidth();
    }

    private void RefreshContentWidth()
    {
        if (_minimumContentWidth is not double minimum) return;

        // WPF star-sized Grid columns can report their *allocated* width after a
        // Popup has opened. Measure each row's real content with no width constraint
        // so adding/changing a long label still expands the whole menu.
        double needed = minimum;
        foreach (UIElement entry in _root.Rows.Children)
        {
            double rowWidth;
            if (entry is Button { Content: Grid grid } item)
            {
                var columns = new double[grid.ColumnDefinitions.Count];
                foreach (UIElement child in grid.Children)
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var index = Grid.GetColumn(child);
                    if (index >= 0 && index < columns.Length)
                        columns[index] = Math.Max(columns[index], child.DesiredSize.Width);
                }
                rowWidth = columns.Sum() + item.Padding.Left + item.Padding.Right +
                    item.BorderThickness.Left + item.BorderThickness.Right +
                    item.Margin.Left + item.Margin.Right;
            }
            else if (entry is TextBlock label)
            {
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                rowWidth = label.DesiredSize.Width;
            }
            else continue;

            needed = Math.Max(needed, rowWidth + _root.Rows.Margin.Left +
                _root.Rows.Margin.Right + _root.Panel.Padding.Left +
                _root.Panel.Padding.Right + _root.Panel.BorderThickness.Left +
                _root.Panel.BorderThickness.Right);
        }
        _root.Panel.MinWidth = Math.Ceiling(needed);
        _root.Panel.InvalidateMeasure();
    }

    private static double PanelWidth(Level level)
    {
        // FrameworkElement.Width is NaN for Auto; use WPF's measured intrinsic width.
        return double.IsNaN(level.Panel.Width) ? level.Panel.DesiredSize.Width : level.Panel.Width;
    }

    public IReadOnlyList<Button> Actions => _actions;
    public bool IsOpen
    {
        get => _popup.IsOpen;
        set
        {
            if (!value) CollapseChildren();
            else RefreshContentWidth();
            _popup.IsOpen = value;
        }
    }
    public int Count => _actions.Count;
    public int OpenDepth => _visible.Count - 1;
    public FrameworkElement Surface => _root.Panel;

    public YanziDropdownMenu()
    {
        _visible.Add(_root);
        _surface.Children.Add(_root.Panel);
        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Custom,
            StaysOpen = false,
            Focusable = false,
            Child = _surface,
            CustomPopupPlacementCallback = PlaceMenu,
            PopupAnimation = PopupAnimation.Fade
        };
        _popup.Opened += (_, _) =>
        {
            if (_root.Items.Count > 0) _root.Items[0].Focus();
        };
        _popup.Closed += (_, _) => CollapseChildren();
        _popup.PreviewKeyDown += HandleKeyDown;
    }

    /// <summary>Apply library tokens to a menu opened without a WPF visual-tree owner.</summary>
    public void UseStandaloneTheme(YanziTheme theme)
    {
        _surface.Resources.MergedDictionaries.Clear();
        _surface.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/Yanzi.UI.Wpf;component/Themes/Tokens.{theme}.xaml", UriKind.Relative)
        });
        _surface.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Yanzi.UI.Wpf;component/Themes/Controls.xaml", UriKind.Relative)
        });
    }

    /// <summary>Open from a notification-area icon, without requiring a visible WPF trigger.</summary>
    public void ShowAtScreenPoint(Point cursor, Rect workingArea)
    {
        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workingArea));
        IsOpen = false;
        // A reusable menu can be shown on another monitor or screen edge.
        // Detach the previous fixed canvas before attaching a new one.
        if (_screenCanvas is not null)
        {
            _screenCanvas.Children.Remove(_root.Panel);
            _surface.Children.Clear();
            _surface.Children.Add(_root.Panel);
            _screenCanvas = null;
        }
        _popup.PlacementTarget = null;
        _popup.Placement = PlacementMode.AbsolutePoint;
        _screenPlacement = true;
        _screenWorkArea = workingArea;
        RefreshContentWidth();
        _root.Panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = PanelWidth(_root);
        var height = _root.Panel.DesiredSize.Height;
        // Prefer left-edge click anchoring, flip only when the right side overflows.
        double proposedLeft = cursor.X + width <= workingArea.Right - 4
            ? cursor.X : cursor.X - width;
        double maxLeft = Math.Max(workingArea.Left + 4, workingArea.Right - width - 4);
        _screenRoot = new Point(
            Math.Clamp(proposedLeft, workingArea.Left + 4, maxLeft),
            Math.Clamp(cursor.Y - height, workingArea.Top + 4, Math.Max(workingArea.Top + 4, workingArea.Bottom - height - 4)));
        // Choose the submenu side from the actual available screen space, not
        // just whether the LEFT happens to have enough room. Prefer right when
        // it fits; use left as fallback, considering nested submenu depth.
        // Decide once, before opening the fixed-size HWND, to avoid flicker.
        int deepest = _submenus.Values.Select(level =>
        {
            int depth = 0;
            for (var current = level; current.Parent is not null; current = current.Parent)
                depth++;
            return depth;
        }).DefaultIfEmpty(0).Max();
        double availableRight = Math.Max(0, workingArea.Right - (_screenRoot.X + width) - 4);
        double availableLeft = Math.Max(0, _screenRoot.X - workingArea.Left - 4);
        double required = deepest * (SubmenuWidth + 4);
        OpenChildrenToLeft = deepest > 0 &&
            availableRight < required &&
            (availableLeft >= required || availableLeft > availableRight);
        foreach (var arrow in _arrows.Values)
            arrow.RenderTransform = OpenChildrenToLeft ? new ScaleTransform(-1, 1) : Transform.Identity;
        double available = OpenChildrenToLeft ? availableLeft : availableRight;
        int reserveLevels = Math.Min(deepest,
            Math.Max(0, (int)Math.Floor(available / (SubmenuWidth + 4))));
        _screenReservedWidth = reserveLevels * (SubmenuWidth + 4);
        _screenCanvas = new Canvas
        {
            Width = width + _screenReservedWidth,
            Height = Math.Max(height, workingArea.Bottom - _screenRoot.Y - 4),
            ClipToBounds = false
        };
        _surface.Children.Remove(_root.Panel);
        _screenCanvas.Children.Add(_root.Panel);
        Canvas.SetLeft(_root.Panel, OpenChildrenToLeft ? _screenReservedWidth : 0);
        Canvas.SetTop(_root.Panel, 0);
        _surface.Children.Add(_screenCanvas);
        _popup.PopupAnimation = PopupAnimation.None;
        UpdateScreenPlacement();
        IsOpen = true;
    }

    private void UpdateScreenPlacement()
    {
        if (!_screenPlacement) return;
        // Screen mode uses a fixed-size Canvas: popup position is constant throughout
        // hover transitions. No child-dependent width or height calculations here.
        double left = _screenRoot.X - (OpenChildrenToLeft ? _screenReservedWidth : 0);
        // Unnecessary Popup offset writes are particularly disruptive to WPF hit testing
        // when a pointer is moving between adjacent submenu triggers.
        if (Math.Abs(_popup.HorizontalOffset - left) > 0.01)
            _popup.HorizontalOffset = left;
        if (Math.Abs(_popup.VerticalOffset - _screenRoot.Y) > 0.01)
            _popup.VerticalOffset = _screenRoot.Y;
    }

    public void Attach(Button trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        _trigger = trigger;
        _screenPlacement = false;
        if (_screenCanvas is not null)
        {
            _screenCanvas.Children.Remove(_root.Panel);
            _surface.Children.Clear();
            _surface.Children.Add(_root.Panel);
            _screenCanvas = null;
        }
        _popup.Placement = PlacementMode.Custom;
        _popup.PlacementTarget = trigger;
        trigger.Click += (_, _) => IsOpen = !IsOpen;
    }

    /// <summary>Show a fresh menu at a button without subscribing to its Click event.
    /// Useful when the content must be rebuilt asynchronously for every opening.</summary>
    public void ShowFrom(Button trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (IsOpen) IsOpen = false;
        _trigger = trigger;
        _screenPlacement = false;
        if (_screenCanvas is not null)
        {
            _screenCanvas.Children.Remove(_root.Panel);
            _surface.Children.Clear();
            _surface.Children.Add(_root.Panel);
            _screenCanvas = null;
        }
        _popup.Placement = PlacementMode.Custom;
        _popup.PlacementTarget = trigger;
        IsOpen = true;
    }

    public void AddLabel(string text) => AddLabelTo(_root, text);
    public void AddSeparator() => AddSeparatorTo(_root);
    public Button AddAction(string text, Action action, string shortcut = "",
        bool destructive = false, bool closeOnInvoke = true, FrameworkElement? icon = null) =>
        AddActionTo(_root, text, action, shortcut, destructive, closeOnInvoke, icon);

    private static void AddLabelTo(Level level, string text)
    {
        var label = new TextBlock
        {
            Text = text, Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12, FontWeight = FontWeights.SemiBold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        level.Rows.Children.Add(label);
    }

    private static void AddSeparatorTo(Level level)
    {
        var line = new Border { Height = 1, Margin = new Thickness(5) };
        line.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
        level.Rows.Children.Add(line);
    }

    private Button AddActionTo(Level level, string text, Action action, string shortcut = "",
        bool destructive = false, bool closeOnInvoke = true, FrameworkElement? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(action);
        var row = new Grid();
        if (icon is not null)
        {
            // shadcn/ui DropdownMenuItem uses gap-2 (8 DIP) after its 16 DIP icon.
            // Auto sizing avoids the old centred 25-DIP column's ~3.5 DIP visual gap.
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
        }
        int textColumn = icon is null ? 0 : 1;
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock
        {
            Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty,
            destructive ? "Yanzi.Color.Destructive" : "Yanzi.Color.Foreground");
        Grid.SetColumn(caption, textColumn);
        row.Children.Add(caption);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var hint = new TextBlock
            {
                Text = shortcut, Margin = new Thickness(20, 0, 0, 0),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Grid.SetColumn(hint, textColumn + 1);
            row.Children.Add(hint);
        }

        var item = new Button
        {
            Content = row, Height = 30, MinHeight = 30,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        YanziUi.WithStyle(item, YanziUi.Styles.DropdownAction);
        AutomationProperties.SetName(item, text);
        // Submenu triggers have their own enter handler. Collapsing here first would
        // tear down the live popup before immediately rebuilding it: visible flicker.
        item.MouseEnter += (_, _) =>
        {
            if (!_submenus.ContainsKey(item)) CollapseAfter(level);
        };
        item.Click += (_, _) =>
        {
            if (closeOnInvoke) IsOpen = false;
            action();
            if (closeOnInvoke) _trigger?.Focus();
        };
        level.Rows.Children.Add(item);
        level.Items.Add(item);
        if (level == _root)
        {
            _actions.Add(item);
            RefreshContentWidth();
        }
        return item;
    }

    private static FrameworkElement AddIndicator(Button button, FrameworkElement indicator)
    {
        var row = (Grid)button.Content;
        // Check/radio indicators must use the same icon-to-text spacing as action icons.
        row.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
        foreach (UIElement child in row.Children)
            Grid.SetColumn(child, Grid.GetColumn(child) + 1);
        indicator.HorizontalAlignment = HorizontalAlignment.Left;
        indicator.Margin = new Thickness(0, 0, 8, 0);
        indicator.Visibility = Visibility.Hidden;
        row.Children.Add(indicator);
        Grid.SetColumn(indicator, 0);
        return indicator;
    }

    public Button AddCheck(string label, bool initial, Action<bool> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(changed);
        bool state = initial;
        var button = AddAction(label, () => {}, closeOnInvoke: false);
        var indicator = AddIndicator(button, YanziIcons.Check(14));
        indicator.Visibility = state ? Visibility.Visible : Visibility.Hidden;
        AutomationProperties.SetItemStatus(button, state ? "Checked" : "Unchecked");
        button.Click += (_, _) =>
        {
            state = !state;
            indicator.Visibility = state ? Visibility.Visible : Visibility.Hidden;
            AutomationProperties.SetItemStatus(button, state ? "Checked" : "Unchecked");
            changed(state);
        };
        return button;
    }

    public IReadOnlyList<Button> AddRadioGroup(string heading, IEnumerable<string> values,
        string selected, Action<string> onSelect)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(onSelect);
        var options = values.ToArray();
        if (!options.Contains(selected, StringComparer.Ordinal))
            throw new ArgumentException("Initial selection is not a listed option", nameof(selected));
        AddLabel(heading);
        var items = new List<(string Value, Button Button, FrameworkElement Icon)>();
        foreach (var value in options)
        {
            string choice = value;
            var item = AddAction(value, () => {}, closeOnInvoke: false);
            var icon = AddIndicator(item, YanziIcons.RadioDot(14));
            icon.Visibility = value == selected ? Visibility.Visible : Visibility.Hidden;
            AutomationProperties.SetItemStatus(item, value == selected ? "Selected" : "Not selected");
            items.Add((choice, item, icon));
            item.Click += (_, _) =>
            {
                selected = choice;
                foreach (var entry in items)
                {
                    bool active = entry.Value == choice;
                    entry.Icon.Visibility = active ? Visibility.Visible : Visibility.Hidden;
                    AutomationProperties.SetItemStatus(entry.Button, active ? "Selected" : "Not selected");
                }
                onSelect(choice);
            };
        }
        return items.Select(x => x.Button).ToArray();
    }

    /// <summary>Add a submenu whose contents may include further submenus.</summary>
    public Button AddSubmenu(string title, Action<YanziDropdownSubmenu> configure, FrameworkElement? icon = null) =>
        AddSubmenuTo(_root, title, configure, icon);

    private Button AddSubmenuTo(Level parent, string title, Action<YanziDropdownSubmenu> configure, FrameworkElement? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(configure);
        var trigger = AddActionTo(parent, title, () => {}, closeOnInvoke: false, icon: icon);
        var row = (Grid)trigger.Content;
        var arrow = YanziIcons.ChevronRight(14);
        if (OpenChildrenToLeft)
            arrow.RenderTransform = new ScaleTransform(-1, 1);
        Grid.SetColumn(arrow, row.ColumnDefinitions.Count - 1);
        row.Children.Add(arrow);
        var level = new Level { Parent = parent, ParentTrigger = trigger };
        level.Panel.Width = SubmenuWidth;
        _submenus.Add(trigger, level);
        _arrows.Add(trigger, arrow);

        var builder = new YanziDropdownSubmenu(
            (label, callback, leadingIcon) => AddActionTo(level, label, callback, icon: leadingIcon),
            (label, child, leadingIcon) => AddSubmenuTo(level, label, child, leadingIcon),
            label => AddLabelTo(level, label),
            () => AddSeparatorTo(level));
        configure(builder);
        trigger.MouseEnter += (_, _) => OpenSubmenu(trigger);
        trigger.Click += (_, _) => OpenSubmenu(trigger, focusFirst: true);
        return trigger;
    }

    public bool OpenSubmenu(Button trigger, bool focusFirst = false)
    {
        if (!_submenus.TryGetValue(trigger, out var next)) return false;
        var parent = next.Parent!;
        if (!_visible.Contains(parent)) return false;
        // Pointer movement within the active trigger must not remove/readd its panel.
        // The original implementation also moved the Popup twice per sibling switch.
        if (_visible.Contains(next))
        {
            if (focusFirst && next.Items.FirstOrDefault(x => x.IsEnabled) is Button first)
                first.Focus();
            return true;
        }
        CollapseAfter(parent, updatePlacement: false);
        if (_screenPlacement && _screenCanvas is not null)
        {
            // Absolute-position the child WITHOUT changing Popup's size or screen origin.
            next.Panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = Canvas.GetLeft(parent.Panel) +
                (OpenChildrenToLeft ? -(PanelWidth(next) + 4) : PanelWidth(parent) + 4);
            double y = Canvas.GetTop(parent.Panel) +
                Math.Max(0, trigger.TranslatePoint(new Point(0, 0), parent.Panel).Y);
            y = Math.Min(y, Math.Max(0, _screenCanvas.Height - next.Panel.DesiredSize.Height));
            next.Panel.Margin = new Thickness(0);
            Canvas.SetLeft(next.Panel, x);
            Canvas.SetTop(next.Panel, y);
            _screenCanvas.Children.Add(next.Panel);
        }
        else
        {
            double y = parent.Panel.Margin.Top +
                Math.Max(0, trigger.TranslatePoint(new Point(0, 0), parent.Panel).Y);
            next.Panel.Margin = OpenChildrenToLeft ? new Thickness(0, y, 4, 0) : new Thickness(4, y, 0, 0);
            if (OpenChildrenToLeft) _surface.Children.Insert(0, next.Panel);
            else _surface.Children.Add(next.Panel);
        }
        _visible.Add(next);
        // Preserve the parent's selected background when the pointer crosses into its submenu.
        trigger.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Accent");
        UpdateScreenPlacement();
        if (focusFirst && next.Items.Count > 0) next.Items[0].Focus();
        return true;
    }

    private void CollapseAfter(Level parent, bool updatePlacement = true)
    {
        while (_visible.Count > 1 && _visible[^1] != parent)
        {
            var remove = _visible[^1];
            _visible.RemoveAt(_visible.Count - 1);
            if (_screenPlacement && _screenCanvas is not null)
                _screenCanvas.Children.Remove(remove.Panel);
            else
                _surface.Children.Remove(remove.Panel);
            remove.ParentTrigger?.ClearValue(Control.BackgroundProperty);
        }
        if (updatePlacement) UpdateScreenPlacement();
    }

    private void CollapseChildren() => CollapseAfter(_root);

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement;
        var level = _visible.LastOrDefault(x =>
            x.Items.Any(item => ReferenceEquals(item, focused) || item.IsKeyboardFocusWithin)) ?? _root;
        if (e.Key == (OpenChildrenToLeft ? Key.Left : Key.Right) && focused is Button trigger && _submenus.ContainsKey(trigger))
        {
            OpenSubmenu(trigger, focusFirst: true);
            e.Handled = true;
        }
        else if (e.Key == (OpenChildrenToLeft ? Key.Right : Key.Left) && level.Parent is not null)
        {
            var previous = level.ParentTrigger;
            CollapseAfter(level.Parent);
            previous?.Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down or Key.Home or Key.End)
        {
            var enabled = level.Items.Where(x => x.IsEnabled).ToArray();
            if (enabled.Length == 0) return;
            int current = Array.FindIndex(enabled, x => x.IsKeyboardFocused);
            int next = e.Key switch
            {
                Key.Home => 0,
                Key.End => enabled.Length - 1,
                Key.Down => (current + 1) % enabled.Length,
                _ => (current < 0 ? 0 : current + enabled.Length - 1) % enabled.Length
            };
            enabled[next].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (level.Parent is not null)
            {
                var previous = level.ParentTrigger;
                CollapseAfter(level.Parent);
                previous?.Focus();
            }
            else
            {
                IsOpen = false;
                _trigger?.Focus();
            }
            e.Handled = true;
        }
    }

    private CustomPopupPlacement[] PlaceMenu(Size popup, Size target, Point offset)
    {
        // Keep the root menu anchored when a submenu adds width to the Popup.
        double rootWidth = PanelWidth(_root);
        double rootHeight = _root.Panel.ActualHeight > 0 ? _root.Panel.ActualHeight : popup.Height;
        double rootX = AlignStart ? TriggerHorizontalOffset(rootWidth, target.Width) : target.Width - rootWidth;
        var above = new CustomPopupPlacement(
            new Point(rootX, -rootHeight - 5), PopupPrimaryAxis.Horizontal);
        var below = new CustomPopupPlacement(
            new Point(rootX, target.Height + 5), PopupPrimaryAxis.Horizontal);
        return PreferAbove ? [above, below] : [below, above];
    }

    private double TriggerHorizontalOffset(double rootWidth, double targetWidth)
    {
        if (_trigger is null || PresentationSource.FromVisual(_trigger)?.CompositionTarget is not { } target)
            return 0;

        var pixel = _trigger.PointToScreen(new Point(0, 0));
        // The monitor's WORK area, not the primary monitor's global WorkArea,
        // is required for mixed-DPI and multiple-monitor setups.
        var monitor = MonitorFromPoint(new NativePoint
        {
            X = (int)Math.Round(pixel.X),
            Y = (int)Math.Round(pixel.Y)
        }, 2);
        if (monitor == IntPtr.Zero) return 0;

        var info = new NativeMonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return 0;

        var scale = target.TransformFromDevice;
        var triggerLeft = scale.Transform(pixel).X;
        var workLeft = scale.Transform(new Point(info.Work.Left, info.Work.Top)).X;
        var workRight = scale.Transform(new Point(info.Work.Right, info.Work.Bottom)).X;
        const double margin = 4;

        if (triggerLeft + rootWidth <= workRight - margin) return 0;

        // Flip only when it solves the overflow. Right-align to the trigger,
        // rather than making the menu originate from the far right by default.
        double flipped = targetWidth - rootWidth;
        if (triggerLeft + flipped >= workLeft + margin)
            return flipped;

        return Math.Max(workLeft + margin - triggerLeft,
            workRight - margin - rootWidth - triggerLeft);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);
}

/// <summary>Builder for submenu levels, including another nested submenu.</summary>
public sealed class YanziDropdownSubmenu
{
    private readonly Func<string, Action, FrameworkElement?, Button> _action;
    private readonly Func<string, Action<YanziDropdownSubmenu>, FrameworkElement?, Button> _submenu;
    private readonly Action<string> _label;
    private readonly Action _separator;
    internal YanziDropdownSubmenu(Func<string, Action, FrameworkElement?, Button> action,
        Func<string, Action<YanziDropdownSubmenu>, FrameworkElement?, Button> submenu,
        Action<string> label, Action separator)
    {
        _action = action;
        _submenu = submenu;
        _label = label;
        _separator = separator;
    }
    public Button AddAction(string title, Action action, FrameworkElement? icon = null) => _action(title, action, icon);
    public Button AddSubmenu(string title, Action<YanziDropdownSubmenu> configure, FrameworkElement? icon = null) =>
        _submenu(title, configure, icon);
    public void AddLabel(string title) => _label(title);
    public void AddSeparator() => _separator();
}
