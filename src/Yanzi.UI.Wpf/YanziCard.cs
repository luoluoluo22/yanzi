using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Source-aligned composite Card. Header, Content, and Footer share the same
/// 16-DIP content spacing; the footer owns its divider and muted surface.
/// Keeps the existing "Yanzi.Card" Border style available for legacy callers.
/// </summary>
public sealed class YanziCard : Border
{
    private readonly StackPanel _stack = new();
    private readonly Border _header = new() { Padding = new Thickness(16, 16, 16, 0),
        Visibility = Visibility.Collapsed };
    private readonly Border _content = new() { Padding = new Thickness(16, 16, 16, 16),
        Visibility = Visibility.Collapsed };
    private readonly Border _footer = new() { Padding = new Thickness(16),
        BorderThickness = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed };

    public UIElement? Header => _header.Child;
    public UIElement? Body => _content.Child;
    public UIElement? Footer => _footer.Child;

    public YanziCard()
    {
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Card");
        SetResourceReference(BorderBrushProperty, "Yanzi.Color.Border");
        SetResourceReference(CornerRadiusProperty, "Yanzi.Radius.Xl");
        _footer.SetResourceReference(BorderBrushProperty, "Yanzi.Color.Border");
        _footer.SetResourceReference(BackgroundProperty, "Yanzi.Color.Muted");
        ClipToBounds = true;
        _stack.Children.Add(_header);
        _stack.Children.Add(_content);
        _stack.Children.Add(_footer);
        Child = _stack;
    }

    public void SetHeader(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _header.Child = child;
        _header.Visibility = Visibility.Visible;
    }
    public void SetBody(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _content.Child = child;
        _content.Visibility = Visibility.Visible;
    }
    public void SetFooter(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _footer.Child = child;
        _footer.Visibility = Visibility.Visible;
    }
}
