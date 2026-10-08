using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Fully-drawn, keyboard-accessible WPF dropdown, not native ContextMenu chrome.
/// Shows above a split-button trigger with right-edge alignment and screen fallback.
/// </summary>
public sealed class YanziDropdownMenu
{
    private readonly StackPanel _rows;
    private readonly Popup _popup;
    private readonly Border _panel;
    private readonly List<Button> _actions = [];
    private Button? _trigger;

    public bool IsOpen { get => _popup.IsOpen; set => _popup.IsOpen = value; }
    public int Count => _actions.Count;
    public FrameworkElement Surface => _panel;

    public YanziDropdownMenu()
    {
        _rows = new StackPanel { Margin = new Thickness(5) };
        _panel = new Border
        {
            Width = 194,
            MinWidth = 174,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(3),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18, ShadowDepth = 7, Opacity = 0.30,
                Color = Colors.Black
            },
            Child = _rows
        };
        _panel.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        _panel.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Custom,
            StaysOpen = false,
            Focusable = false,
            Child = _panel,
            CustomPopupPlacementCallback = PlaceAbove,
            PopupAnimation = PopupAnimation.Fade
        };
        _popup.Opened += (_, _) =>
        {
            if (_actions.Count > 0)
                _actions[0].Focus();
        };
        _popup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsOpen = false;
                _trigger?.Focus();
                e.Handled = true;
            }
        };
    }

    public void Attach(Button trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        _trigger = trigger;
        _popup.PlacementTarget = trigger;
        trigger.Click += (_, _) => IsOpen = !IsOpen;
    }

    public void AddLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Padding = new Thickness(8, 8, 8, 7),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        _rows.Children.Add(label);
    }

    public void AddSeparator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(5, 5, 5, 5) };
        line.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
        _rows.Children.Add(line);
    }

    public Button AddAction(string text, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(action);
        var item = new Button
        {
            Content = text,
            Height = 31,
            MinHeight = 31,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 5, 8, 5),
            Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        YanziUi.WithStyle(item, YanziUi.Styles.DropdownAction);
        item.Click += (_, _) =>
        {
            IsOpen = false;
            action();
            _trigger?.Focus();
        };
        _rows.Children.Add(item);
        _actions.Add(item);
        return item;
    }

    private static CustomPopupPlacement[] PlaceAbove(Size popup, Size target, Point offset) =>
    [
        new(new Point(target.Width - popup.Width, -popup.Height - 7), PopupPrimaryAxis.Horizontal),
        new(new Point(target.Width - popup.Width, target.Height + 7), PopupPrimaryAxis.Horizontal)
    ];
}
