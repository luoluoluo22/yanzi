using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Compact contiguous shadcn-style segmented action group.
/// Each segment has a separate keyboard focus and click action.
/// </summary>
public sealed class YanziSegmentedButtonGroup : UserControl
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<Button> _buttons = new();

    public int Count => _buttons.Count;

    public YanziSegmentedButtonGroup()
    {
        var frame = new Border
        {
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        frame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        frame.Child = _row;
        Content = frame;
    }

    public Button Add(string label, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(action);
        var button = YanziUi.WithStyle(new Button
        {
            Content = label,
            MinWidth = 36,
            Height = 32,
            Padding = new Thickness(14, 5, 14, 5)
        }, YanziUi.Styles.SegmentedFirstButton);
        button.Click += (_, _) => action();
        if (_buttons.Count > 0)
        {
            var divider = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
            divider.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
            _row.Children.Add(divider);
        }
        _row.Children.Add(button);
        _buttons.Add(button);
        UpdateSegments();
        return button;
    }

    private void UpdateSegments()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var key = _buttons.Count == 1 ? YanziUi.Styles.PillOutlineButton
                : i == 0 ? YanziUi.Styles.SegmentedFirstButton
                : i == _buttons.Count - 1 ? YanziUi.Styles.SegmentedLastButton
                : YanziUi.Styles.SegmentedBaseButton;
            _buttons[i].MinWidth = i == 0 ? 54 : 36;
            _buttons[i].Padding = i == 0
                ? new Thickness(18, 5, 18, 5)
                : new Thickness(14, 5, 14, 5);
            _buttons[i].SetResourceReference(FrameworkElement.StyleProperty, key);
        }
    }
}
