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
    // Preserve existing upward-menu callers. New shadcn previews opt into below.
    public bool PreferAbove { get; set; } = true;
    public IReadOnlyList<Button> Actions => _actions;

    public bool IsOpen { get => _popup.IsOpen; set => _popup.IsOpen = value; }
    public int Count => _actions.Count;
    public FrameworkElement Surface => _panel;

    public YanziDropdownMenu()
    {
        _rows = new StackPanel { Margin = new Thickness(5) };
        _panel = new Border
        {
            Width = 194,
            MinWidth = 128,
            CornerRadius = new CornerRadius(8),
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
            if (e.Key is Key.Down or Key.Up or Key.Home or Key.End)
            {
                var enabled = _actions.Where(x => x.IsEnabled).ToArray();
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
            Padding = new Thickness(6, 4, 6, 4),
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

    public Button AddAction(string text, Action action, string shortcut = "",
        bool destructive = false, bool closeOnInvoke = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(action);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
        caption.SetResourceReference(TextBlock.ForegroundProperty,
            destructive ? "Yanzi.Color.Destructive" : "Yanzi.Color.Foreground");
        row.Children.Add(caption);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var hotkey = new TextBlock { Text = shortcut, Margin = new Thickness(20, 0, 0, 0),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            hotkey.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Grid.SetColumn(hotkey, 1);
            row.Children.Add(hotkey);
        }
        var item = new Button
        {
            Content = row,
            Height = 30,
            MinHeight = 30,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        YanziUi.WithStyle(item, YanziUi.Styles.DropdownAction);
        System.Windows.Automation.AutomationProperties.SetName(item, text);
        item.Click += (_, _) =>
        {
            if (closeOnInvoke) IsOpen = false;
            action();
            if (closeOnInvoke) _trigger?.Focus();
        };
        _rows.Children.Add(item);
        _actions.Add(item);
        return item;
    }

    public Button AddCheck(string label, bool initial, Action<bool> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(changed);
        bool state = initial;
        var button = AddAction((initial ? "☑  " : "□  ") + label,
            () => {}, closeOnInvoke: false);
        var row = (Grid)button.Content;
        var title = (TextBlock)row.Children[0];
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            state = !state;
            title.Text = (state ? "☑  " : "□  ") + label;
            changed(state);
        };
        return button;
    }

    /// <summary>Exclusive choices styled using the same Popup and menu item rendering.</summary>
    public IReadOnlyList<Button> AddRadioGroup(string heading, IEnumerable<string> values,
        string selected, Action<string> onSelect)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(onSelect);
        var options = values.ToArray();
        if (!options.Contains(selected, StringComparer.Ordinal))
            throw new ArgumentException("Initial selection is not a listed option", nameof(selected));
        AddLabel(heading);
        var radios = new List<(string Value, Button Button, TextBlock Text)>();
        foreach (var value in options)
        {
            string choice = value;
            var item = AddAction((value == selected ? "●  " : "○  ") + value,
                () => {}, closeOnInvoke: false);
            var text = (TextBlock)((Grid)item.Content).Children[0];
            System.Windows.Automation.AutomationProperties.SetName(item, value);
            radios.Add((choice, item, text));
            item.Click += (_, _) =>
            {
                selected = choice;
                foreach (var radio in radios)
                    radio.Text.Text = (radio.Value == selected ? "●  " : "○  ") + radio.Value;
                onSelect(choice);
            };
        }
        return radios.Select(x => x.Button).ToArray();
    }

    private CustomPopupPlacement[] PlaceAbove(Size popup, Size target, Point offset)
    {
        var above = new CustomPopupPlacement(
            new Point(target.Width - popup.Width, -popup.Height - 5), PopupPrimaryAxis.Horizontal);
        var below = new CustomPopupPlacement(
            new Point(target.Width - popup.Width, target.Height + 5), PopupPrimaryAxis.Horizontal);
        return PreferAbove ? [above, below] : [below, above];
    }
}
