using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Keyboard-accessible multi-section accordion with optional single-open behavior.</summary>
public sealed class YanziAccordion : UserControl
{
    private readonly StackPanel _stack = new();
    private readonly List<Expander> _panels = new();
    public bool SingleOpen { get; set; } = true;
    public int SectionCount => _panels.Count;

    public YanziAccordion() { Content = _stack; }

    public void Add(string title, UIElement content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        var expander = YanziUi.WithStyle(new Expander
        {
            Header = title, Content = content, Margin = new Thickness(0, 0, 0, 7)
        }, YanziUi.Styles.Expander);
        expander.Expanded += (_, _) =>
        {
            if (!SingleOpen) return;
            foreach (var other in _panels) if (!ReferenceEquals(other, expander)) other.IsExpanded = false;
        };
        _panels.Add(expander);
        _stack.Children.Add(expander);
    }
}
