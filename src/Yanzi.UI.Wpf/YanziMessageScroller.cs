using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Bounded visual message list with opt-in autoscroll. The caller persists message history.
/// Rendering is capped so the window never accumulates an unbounded UI tree.
/// </summary>
public sealed class YanziMessageScroller : UserControl
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _list;
    private int _maxVisible = 120;
    public int MaxVisible
    {
        get => _maxVisible;
        set
        {
            if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value));
            _maxVisible = value;
            Trim();
        }
    }
    public int VisibleCount => _list.Children.Count;

    public YanziMessageScroller()
    {
        _list = new StackPanel { Margin = new Thickness(4) };
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list, Height = 180
        };
        Content = _scroll;
    }

    public void AddMessage(string content, bool outgoing)
    {
        VerifyAccess();
        var followBottom = _scroll.ExtentHeight - _scroll.ViewportHeight - _scroll.VerticalOffset < 24;
        _list.Children.Add(YanziContentPrimitives.MessageBubble(content, outgoing));
        Trim();
        if (followBottom || VisibleCount <= 2)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _scroll.ScrollToEnd());
    }

    public void Clear()
    {
        VerifyAccess();
        _list.Children.Clear();
    }

    private void Trim()
    {
        while (_list.Children.Count > _maxVisible) _list.Children.RemoveAt(0);
    }
}
