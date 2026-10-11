using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Chat transcript viewport with bounded rendering, follow-at-live-edge,
/// explicit scroll controls, and offset preservation on history prepends.
/// Caller is responsible for transport, persistence, and fetching older rows.
/// </summary>
public sealed class YanziMessageScroller : UserControl
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _list;
    private readonly Dictionary<string, FrameworkElement> _items = new();
    private readonly List<string> _orderedIds = new();
    private int _maxVisible = 120;
    private int _autoId;
    public bool FollowLiveEdge { get; set; } = true;
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
    public double ScrollOffset => _scroll.VerticalOffset;
    public bool IsNearLatest => _scroll.ScrollableHeight - _scroll.VerticalOffset <= 24;

    public YanziMessageScroller()
    {
        _list = new StackPanel { Margin = new Thickness(4) };
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list, Height = 180, Focusable = true
        };
        System.Windows.Automation.AutomationProperties.SetName(_scroll, "Messages");
        Content = _scroll;
    }

    public void AddMessage(string content, bool outgoing)
        => AddMessage("message-" + (++_autoId), content, outgoing);

    public void AddMessage(string id, string content, bool outgoing)
    {
        VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_items.ContainsKey(id)) throw new ArgumentException("Message IDs must be unique.", nameof(id));
        var follow = FollowLiveEdge && (IsNearLatest || VisibleCount <= 2);
        var bubble = YanziContentPrimitives.MessageBubble(content, outgoing);
        _list.Children.Add(bubble);
        _orderedIds.Add(id);
        _items.Add(id, bubble);
        Trim();
        if (follow)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _scroll.ScrollToEnd());
    }

    /// <summary>Insert older messages without jumping the visible reading position.</summary>
    public void PrependHistory(IEnumerable<(string Id, string Content, bool Outgoing)> history)
    {
        VerifyAccess();
        var incoming = history.ToArray();
        if (incoming.Length == 0) return;
        if (incoming.Any(x => string.IsNullOrWhiteSpace(x.Id) || _items.ContainsKey(x.Id)) ||
            incoming.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != incoming.Length)
            throw new ArgumentException("History must contain unique, nonempty message IDs.", nameof(history));

        _scroll.UpdateLayout();
        var oldOffset = _scroll.VerticalOffset;
        var oldExtent = _scroll.ExtentHeight;
        for (int i = incoming.Length - 1; i >= 0; i--)
        {
            var item = incoming[i];
            var visual = YanziContentPrimitives.MessageBubble(item.Content, item.Outgoing);
            _list.Children.Insert(0, visual);
            _orderedIds.Insert(0, item.Id);
            _items.Add(item.Id, visual);
        }
        // If the bounded buffer overflows, discard the oldest messages. The
        // host must retain them in persistent storage if history matters.
        Trim();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _scroll.UpdateLayout();
            _scroll.ScrollToVerticalOffset(Math.Max(0,
                oldOffset + _scroll.ExtentHeight - oldExtent));
        });
    }

    /// <summary>Update streamed text without moving a reader who scrolled away.</summary>
    public bool AppendToMessage(string id, string chunk)
    {
        VerifyAccess();
        if (!_items.TryGetValue(id, out var element) ||
            element is not Border { Child: TextBlock text }) return false;
        var follow = FollowLiveEdge && IsNearLatest;
        text.Text += chunk;
        if (follow)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _scroll.ScrollToEnd());
        return true;
    }

    public bool ScrollToMessage(string id)
    {
        VerifyAccess();
        if (!_items.TryGetValue(id, out var element)) return false;
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(
            element.TransformToAncestor(_list).Transform(new Point(0, 0)).Y);
        return true;
    }

    public void JumpToLatest()
    {
        VerifyAccess();
        _scroll.ScrollToEnd();
    }

    public void Clear()
    {
        VerifyAccess();
        _list.Children.Clear();
        _orderedIds.Clear();
        _items.Clear();
    }

    private void Trim()
    {
        while (_list.Children.Count > _maxVisible)
        {
            _list.Children.RemoveAt(0);
            _items.Remove(_orderedIds[0]);
            _orderedIds.RemoveAt(0);
        }
    }
}
