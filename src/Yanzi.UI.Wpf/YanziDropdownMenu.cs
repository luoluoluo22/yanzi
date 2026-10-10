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
    private readonly StackPanel _surface = new() { Orientation = Orientation.Horizontal };
    private readonly Popup _popup;
    private readonly List<Button> _actions = [];
    private Button? _trigger;

    // Existing consumers expect menus above their trigger; source-oriented pages opt out.
    public bool PreferAbove { get; set; } = true;
    public IReadOnlyList<Button> Actions => _actions;
    public bool IsOpen
    {
        get => _popup.IsOpen;
        set
        {
            if (!value) CollapseChildren();
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

    public void Attach(Button trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        _trigger = trigger;
        _popup.PlacementTarget = trigger;
        trigger.Click += (_, _) => IsOpen = !IsOpen;
    }

    public void AddLabel(string text) => AddLabelTo(_root, text);
    public void AddSeparator() => AddSeparatorTo(_root);
    public Button AddAction(string text, Action action, string shortcut = "",
        bool destructive = false, bool closeOnInvoke = true) =>
        AddActionTo(_root, text, action, shortcut, destructive, closeOnInvoke);

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
        bool destructive = false, bool closeOnInvoke = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(action);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock
        {
            Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty,
            destructive ? "Yanzi.Color.Destructive" : "Yanzi.Color.Foreground");
        row.Children.Add(caption);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var hint = new TextBlock
            {
                Text = shortcut, Margin = new Thickness(20, 0, 0, 0),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Grid.SetColumn(hint, 1);
            row.Children.Add(hint);
        }

        var item = new Button
        {
            Content = row, Height = 30, MinHeight = 30,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        YanziUi.WithStyle(item, YanziUi.Styles.DropdownAction);
        AutomationProperties.SetName(item, text);
        item.MouseEnter += (_, _) => CollapseAfter(level);
        item.Click += (_, _) =>
        {
            if (closeOnInvoke) IsOpen = false;
            action();
            if (closeOnInvoke) _trigger?.Focus();
        };
        level.Rows.Children.Add(item);
        level.Items.Add(item);
        if (level == _root) _actions.Add(item);
        return item;
    }

    private static FrameworkElement AddIndicator(Button button, FrameworkElement indicator)
    {
        var row = (Grid)button.Content;
        row.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(21) });
        foreach (UIElement child in row.Children)
            Grid.SetColumn(child, Grid.GetColumn(child) + 1);
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
    public Button AddSubmenu(string title, Action<YanziDropdownSubmenu> configure) =>
        AddSubmenuTo(_root, title, configure);

    private Button AddSubmenuTo(Level parent, string title, Action<YanziDropdownSubmenu> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(configure);
        var trigger = AddActionTo(parent, title, () => {}, closeOnInvoke: false);
        var row = (Grid)trigger.Content;
        var arrow = YanziIcons.ChevronRight(14);
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);
        var level = new Level { Parent = parent, ParentTrigger = trigger };
        _submenus.Add(trigger, level);

        var builder = new YanziDropdownSubmenu(
            (label, callback) => AddActionTo(level, label, callback),
            (label, child) => AddSubmenuTo(level, label, child),
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
        CollapseAfter(parent);
        double y = parent.Panel.Margin.Top +
            Math.Max(0, trigger.TranslatePoint(new Point(0, 0), parent.Panel).Y);
        next.Panel.Margin = new Thickness(4, y, 0, 0);
        _surface.Children.Add(next.Panel);
        _visible.Add(next);
        if (focusFirst && next.Items.Count > 0) next.Items[0].Focus();
        return true;
    }

    private void CollapseAfter(Level parent)
    {
        while (_visible.Count > 1 && _visible[^1] != parent)
        {
            var remove = _visible[^1];
            _visible.RemoveAt(_visible.Count - 1);
            _surface.Children.Remove(remove.Panel);
        }
    }

    private void CollapseChildren() => CollapseAfter(_root);

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement;
        var level = _visible.LastOrDefault(x =>
            x.Items.Any(item => ReferenceEquals(item, focused) || item.IsKeyboardFocusWithin)) ?? _root;
        if (e.Key == Key.Right && focused is Button trigger && _submenus.ContainsKey(trigger))
        {
            OpenSubmenu(trigger, focusFirst: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && level.Parent is not null)
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
        double rootWidth = _root.Panel.Width;
        double rootHeight = _root.Panel.ActualHeight > 0 ? _root.Panel.ActualHeight : popup.Height;
        var above = new CustomPopupPlacement(
            new Point(target.Width - rootWidth, -rootHeight - 5), PopupPrimaryAxis.Horizontal);
        var below = new CustomPopupPlacement(
            new Point(target.Width - rootWidth, target.Height + 5), PopupPrimaryAxis.Horizontal);
        return PreferAbove ? [above, below] : [below, above];
    }
}

/// <summary>Builder for submenu levels, including another nested submenu.</summary>
public sealed class YanziDropdownSubmenu
{
    private readonly Func<string, Action, Button> _action;
    private readonly Func<string, Action<YanziDropdownSubmenu>, Button> _submenu;
    private readonly Action<string> _label;
    private readonly Action _separator;
    internal YanziDropdownSubmenu(Func<string, Action, Button> action,
        Func<string, Action<YanziDropdownSubmenu>, Button> submenu,
        Action<string> label, Action separator)
    {
        _action = action;
        _submenu = submenu;
        _label = label;
        _separator = separator;
    }
    public Button AddAction(string title, Action action) => _action(title, action);
    public Button AddSubmenu(string title, Action<YanziDropdownSubmenu> configure) =>
        _submenu(title, configure);
    public void AddLabel(string title) => _label(title);
    public void AddSeparator() => _separator();
}
