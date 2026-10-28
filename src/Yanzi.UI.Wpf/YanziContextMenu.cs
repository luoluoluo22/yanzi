using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Custom drawn context menu; no WPF ContextMenu/MenuItem chrome.
/// Pointer position and keyboard context-menu shortcuts are supported.
/// A popup is owned by a trigger and closes on outside click, Escape or activation.
/// </summary>
public sealed class YanziContextMenu
{
    private readonly Popup _popup;
    private readonly Border _surface;
    private readonly StackPanel _rows;
    private readonly StackPanel _popupRoot;
    private readonly StackPanel _submenuRows;
    private readonly Border _submenuSurface;
    private readonly List<Button> _actions = [];
    private readonly List<Button> _submenuActions = [];
    private Button? _submenuTrigger;
    private FrameworkElement? _anchor;

    public bool IsOpen { get => _popup.IsOpen; set => _popup.IsOpen = value; }
    public FrameworkElement Surface => _surface;
    public int Count => _actions.Count;
    public IReadOnlyList<Button> Actions => _actions;
    public bool IsSubmenuOpen => _submenuSurface.Visibility == Visibility.Visible;
    public int SubmenuCount => _submenuActions.Count;

    public YanziContextMenu()
    {
        _rows = new StackPanel { Margin = new Thickness(5) };
        _surface = new Border
        {
            Width = 222,
            MinWidth = 190,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(3),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 6,
                BlurRadius = 19,
                Opacity = 0.31
            },
            Child = _rows
        };
        _surface.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        _surface.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        _submenuRows = new StackPanel { Margin = new Thickness(5) };
        _submenuSurface = new Border
        {
            Width = 200,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Child = _submenuRows
        };
        _submenuSurface.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        _submenuSurface.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        _popupRoot = new StackPanel { Orientation = Orientation.Horizontal };
        _popupRoot.Children.Add(_surface);
        _popupRoot.Children.Add(_submenuSurface);
        _popup = new Popup
        {
            Placement = PlacementMode.RelativePoint,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = _popupRoot
        };
        _popup.Closed += (_, _) => HideSubmenu();
        _popup.Opened += (_, _) =>
        {
            if (_actions.FirstOrDefault(x => x.IsEnabled) is Button first)
                first.Focus();
        };
        _popupRoot.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Left && IsSubmenuOpen &&
                _submenuActions.Any(x => x.IsKeyboardFocusWithin))
            {
                HideSubmenu();
                _submenuTrigger?.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right && _submenuTrigger?.IsKeyboardFocused == true)
            {
                OpenSubmenu(focusFirst: true);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Down || e.Key == Key.Up)
            {
                var available = (IsSubmenuOpen && _submenuActions.Any(x => x.IsKeyboardFocusWithin)
                    ? _submenuActions : _actions).Where(x => x.IsEnabled).ToArray();
                if (available.Length == 0) return;
                var focused = available.FirstOrDefault(x => x.IsKeyboardFocused);
                var current = Array.IndexOf(available, focused);
                int direction = e.Key == Key.Down ? 1 : -1;
                available[(current + direction + available.Length) % available.Length].Focus();
                e.Handled = true;
            }
        };
    }

    public void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_anchor is not null)
            throw new InvalidOperationException("The context menu is already attached to a control.");
        _anchor = target;
        _popup.PlacementTarget = target;
        target.ContextMenuOpening += (_, e) => e.Handled = true;
        target.MouseRightButtonUp += (_, e) =>
        {
            OpenAt(e.GetPosition(target));
            e.Handled = true;
        };
        target.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Apps || (e.Key == Key.F10 &&
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                OpenAt(new Point(Math.Max(0, target.ActualWidth / 2),
                    Math.Max(0, target.ActualHeight / 2)));
                e.Handled = true;
            }
        };
        target.Unloaded += (_, _) => Close();
    }

    public void OpenAt(Point point)
    {
        if (_anchor is null)
            throw new InvalidOperationException("Attach the context menu to a target first.");
        if (!_anchor.IsEnabled) return;
        _popup.HorizontalOffset = point.X;
        _popup.VerticalOffset = point.Y;
        HideSubmenu();
        _popup.IsOpen = true;
    }

    public void Close()
    {
        _popup.IsOpen = false;
        _anchor?.Focus();
    }

    public void AddLabel(string title)
    {
        var label = new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(11, 9, 8, 5)
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        _rows.Children.Add(label);
    }

    public void AddSeparator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6) };
        line.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
        _rows.Children.Add(line);
    }

    public Button AddAction(string title, Action handler, string shortcut = "", bool destructive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(handler);
        var content = Row(title, shortcut, destructive);
        var button = NewItem(content, title);
        button.MouseEnter += (_, _) => HideSubmenu();
        button.Click += (_, _) =>
        {
            Close();
            handler();
        };
        return button;
    }

    public CheckBox AddCheck(string title, bool isChecked, Action<bool> onChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(onChange);
        var check = new CheckBox { Content = title, IsChecked = isChecked,
            MinHeight = 32, Padding = new Thickness(7, 3, 7, 3) };
        YanziUi.WithStyle(check, YanziUi.Styles.CheckBoxPreview);
        check.Checked += (_, _) => onChange(true);
        check.Unchecked += (_, _) => onChange(false);
        _rows.Children.Add(check);
        return check;
    }

    /// <summary>Menu radio items with exclusive semantics from YanziRadioGroup.</summary>
    public YanziRadioGroup AddRadioGroup(string label, IEnumerable<(string Label, string Value)> options,
        string selectedValue, Action<string> onSelect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onSelect);
        AddLabel(label);
        var group = new YanziRadioGroup { Margin = new Thickness(10, 5, 0, 1) };
        foreach (var (caption, value) in options)
            group.Add(caption, value);
        if (!group.Select(selectedValue))
            throw new ArgumentException("The initially selected radio value must be a declared option.", nameof(selectedValue));
        group.SelectionChanged += (_, choice) =>
        {
            if (choice is not null) onSelect(choice);
        };
        _rows.Children.Add(group);
        return group;
    }

    /// <summary>
    /// An inline adjacent submenu inside the SAME Popup, keeping the parent's
    /// menu open while keyboard focus moves into submenu actions.
    /// </summary>
    public Button AddSubmenu(string title, Action<YanziContextSubmenu> build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(build);
        if (_submenuTrigger is not null)
            throw new InvalidOperationException("Only one submenu trigger is supported per menu instance.");
        var trigger = NewItem(Row(title, "›", false), title);
        _submenuTrigger = trigger;
        build(new YanziContextSubmenu(this));
        trigger.MouseEnter += (_, _) => OpenSubmenu(focusFirst: false);
        trigger.Click += (_, _) => OpenSubmenu(focusFirst: true);
        return trigger;
    }

    internal Button AddSubmenuAction(string title, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(action);
        var item = new Button
        {
            Content = title, Height = 33,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(1)
        };
        YanziUi.WithStyle(item, YanziUi.Styles.DropdownAction);
        AutomationProperties.SetName(item, title);
        item.Click += (_, _) => { Close(); action(); };
        _submenuActions.Add(item);
        _submenuRows.Children.Add(item);
        return item;
    }

    public void OpenSubmenu(bool focusFirst = false)
    {
        if (_submenuTrigger is null) return;
        _submenuSurface.Margin = new Thickness(4,
            Math.Max(0, _submenuTrigger.TranslatePoint(new Point(0, 0), _surface).Y), 0, 0);
        _submenuSurface.Visibility = Visibility.Visible;
        if (focusFirst && _submenuActions.Count > 0)
            _submenuActions[0].Focus();
    }

    public void HideSubmenu() => _submenuSurface.Visibility = Visibility.Collapsed;

    private static Grid Row(string title, string shortcut, bool destructive)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        if (destructive)
            text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Destructive");
        row.Children.Add(text);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var hint = new TextBlock { Text = shortcut, Margin = new Thickness(15, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Grid.SetColumn(hint, 1);
            row.Children.Add(hint);
        }
        return row;
    }

    private Button NewItem(UIElement content, string automationName)
    {
        var item = new Button
        {
            Content = content,
            Height = 33,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        YanziUi.WithStyle(item, YanziUi.Styles.DropdownAction);
        AutomationProperties.SetName(item, automationName);
        _actions.Add(item);
        _rows.Children.Add(item);
        return item;
    }
}

/// <summary>A child command collection, whose buttons remain in the parent's popup.</summary>
public sealed class YanziContextSubmenu
{
    private readonly YanziContextMenu _owner;
    internal YanziContextSubmenu(YanziContextMenu owner) => _owner = owner;
    public Button AddAction(string title, Action callback) => _owner.AddSubmenuAction(title, callback);
}
