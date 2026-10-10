using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Compact horizontally grouped native buttons. Commands remain caller-owned.</summary>
public sealed class YanziButtonGroup : UserControl
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    public int Count => _row.Children.Count;
    public YanziButtonGroup() { Content = _row; }

    public Button Add(string label, Action onClick, string? style = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);
        var button = YanziUi.WithStyle(new Button { Content = label, Margin = new Thickness(0, 0, 2, 0),
            MinWidth = 40 }, style ?? YanziUi.Styles.OutlineButton);
        button.Click += (_, _) => onClick();
        _row.Children.Add(button);
        return button;
    }
}
