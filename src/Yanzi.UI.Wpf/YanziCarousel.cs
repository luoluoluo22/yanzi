using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Horizontal native scrolling carousel. Items can be any WPF UIElements.</summary>
public sealed class YanziCarousel : UserControl
{
    private readonly StackPanel _items;
    private readonly ScrollViewer _scroll;
    public double Step { get; set; } = 190;

    public YanziCarousel()
    {
        var root = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(actions, Dock.Top);
        var previous = YanziUi.WithStyle(new Button { Content = "‹", MinWidth = 33 }, YanziUi.Styles.OutlineButton);
        var next = YanziUi.WithStyle(new Button { Content = "›", MinWidth = 33, Margin = new Thickness(7, 0, 0, 0) },
            YanziUi.Styles.OutlineButton);
        System.Windows.Automation.AutomationProperties.SetName(previous, "上一张");
        System.Windows.Automation.AutomationProperties.SetName(next, "下一张");
        previous.Click += (_, _) => Scroll(-Step);
        next.Click += (_, _) => Scroll(Step);
        actions.Children.Add(previous);
        actions.Children.Add(next);
        root.Children.Add(actions);

        _items = new StackPanel { Orientation = Orientation.Horizontal };
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _items, CanContentScroll = false
        };
        root.Children.Add(_scroll);
        Content = root;
    }

    public void Add(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _items.Children.Add(element);
    }

    public void Scroll(double offset) => _scroll.ScrollToHorizontalOffset(
        Math.Clamp(_scroll.HorizontalOffset + offset, 0, _scroll.ScrollableWidth));
}
