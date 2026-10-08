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
    private readonly List<Button> _actions = [];
    private FrameworkElement? _anchor;

    public bool IsOpen { get => _popup.IsOpen; set => _popup.IsOpen = value; }
    public FrameworkElement Surface => _surface;
    public int Count => _actions.Count;
    public IReadOnlyList<Button> Actions => _actions;

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
        _popup = new Popup
        {
            Placement = PlacementMode.RelativePoint,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = _surface
        };
        _popup.Opened += (_, _) =>
        {
            if (_actions.FirstOrDefault(x => x.IsEnabled) is Button first)
                first.Focus();
        };
        _surface.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Down || e.Key == Key.Up)
            {
                var available = _actions.Where(x => x.IsEnabled).ToArray();
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
