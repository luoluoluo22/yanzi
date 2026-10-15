using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Compact navigation sidebar. Item selection is explicitly owned by this component.</summary>
public sealed class YanziSidebar : UserControl
{
    private readonly StackPanel _items = new();
    private readonly List<(Button Button, string Name, Action? Action)> _entries = new();
    private int _selected = -1;
    public int SelectedIndex => _selected;
    public int Count => _entries.Count;
    public event EventHandler<string>? SelectionChanged;

    public YanziSidebar()
    {
        var scroller = new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Content = scroller;
    }

    public void Add(string name, Action? navigate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var index = _entries.Count;
        var button = YanziUi.WithStyle(new Button { Content = name, HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(13, 6, 13, 6) }, YanziUi.Styles.GhostButton);
        button.Click += (_, _) => Select(index);
        _entries.Add((button, name, navigate));
        _items.Children.Add(button);
        if (_selected < 0) Select(0);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == _selected) return;
        _selected = index;
        for (var i = 0; i < _entries.Count; i++)
            _entries[i].Button.SetResourceReference(Button.StyleProperty,
                i == index ? YanziUi.Styles.SecondaryButton : YanziUi.Styles.GhostButton);
        var current = _entries[index];
        current.Action?.Invoke();
        SelectionChanged?.Invoke(this, current.Name);
    }
}
