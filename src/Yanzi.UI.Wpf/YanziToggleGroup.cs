using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Mutually exclusive toolbar toggle buttons with native keyboard tab focus.</summary>
public sealed class YanziToggleGroup : UserControl
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<Button> _buttons = new();
    private readonly List<string> _values = new();
    private int _selectedIndex = -1;
    public event EventHandler<string>? SelectionChanged;

    public int SelectedIndex => _selectedIndex;
    public string? SelectedValue => _selectedIndex < 0 ? null : _values[_selectedIndex];

    public YanziToggleGroup()
    {
        Content = _row;
    }

    public void Add(string label, string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var index = _values.Count;
        _values.Add(value ?? label);
        var button = YanziUi.WithStyle(new Button { Content = label, Margin = new Thickness(0, 0, 4, 0),
            MinWidth = 52 }, YanziUi.Styles.GhostButton);
        button.Click += (_, _) => Select(index);
        _buttons.Add(button);
        _row.Children.Add(button);
        if (index == 0) Select(0);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _buttons.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_selectedIndex == index) return;
        _selectedIndex = index;
        for (var i = 0; i < _buttons.Count; i++)
            _buttons[i].SetResourceReference(Button.StyleProperty,
                i == _selectedIndex ? YanziUi.Styles.SecondaryButton : YanziUi.Styles.GhostButton);
        SelectionChanged?.Invoke(this, _values[_selectedIndex]);
    }
}
