using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Reusable horizontally scrolling carousel with item-aligned navigation,
/// optional loop, bounded state and an accessible position indicator.
/// Maintains existing Add and pixel Scroll APIs for older consumers.
/// </summary>
public sealed class YanziCarousel : UserControl
{
    private readonly StackPanel _items;
    private readonly ScrollViewer _scroll;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly TextBlock _position;
    private int _selectedIndex;
    private Point _mouseDown;
    private double _mouseDownOffset;
    private bool _armed;
    private bool _dragging;
    public bool DragEnabled { get; set; } = true;

    public double Step { get; set; } = 190;
    public bool Loop { get; set; }
    public bool SnapToItems { get; set; } = true;
    public int Count => _items.Children.Count;
    public int SelectedIndex => _selectedIndex;
    public event EventHandler<int>? SelectedIndexChanged;

    public YanziCarousel()
    {
        var root = new DockPanel { LastChildFill = true };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(actions, Dock.Top);
        _position = new TextBlock
        {
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _position.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        AutomationProperties.SetName(_position, "当前轮播位置");
        _previous = YanziUi.WithStyle(new Button
        {
            Content = "‹", MinWidth = 33, MinHeight = 30
        }, YanziUi.Styles.OutlineButton);
        _next = YanziUi.WithStyle(new Button
        {
            Content = "›", MinWidth = 33, MinHeight = 30,
            Margin = new Thickness(7, 0, 0, 0)
        }, YanziUi.Styles.OutlineButton);
        AutomationProperties.SetName(_previous, "上一张");
        AutomationProperties.SetName(_next, "下一张");
        _previous.Click += (_, _) => Previous();
        _next.Click += (_, _) => Next();
        actions.Children.Add(_position);
        actions.Children.Add(_previous);
        actions.Children.Add(_next);
        root.Children.Add(actions);

        _items = new StackPanel { Orientation = Orientation.Horizontal };
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _items,
            CanContentScroll = false,
            Focusable = true
        };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if (Count == 0) return;
            if (e.Delta < 0) Next();
            else Previous();
            e.Handled = true;
        };
        _scroll.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!DragEnabled || Count == 0) return;
            _armed = true;
            _dragging = false;
            _mouseDown = e.GetPosition(_scroll);
            _mouseDownOffset = _scroll.HorizontalOffset;
        };
        _scroll.PreviewMouseMove += (_, e) =>
        {
            if (!_armed) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag(snap: false);
                return;
            }
            var distance = e.GetPosition(_scroll).X - _mouseDown.X;
            if (!_dragging && Math.Abs(distance) >= 6)
            {
                _dragging = true;
                _scroll.CaptureMouse();
            }
            if (_dragging)
            {
                _scroll.ScrollToHorizontalOffset(
                    Math.Clamp(_mouseDownOffset - distance, 0, _scroll.ScrollableWidth));
                e.Handled = true;
            }
        };
        _scroll.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var moved = _dragging;
            EndDrag(snap: moved);
            if (moved) e.Handled = true;
        };
        _scroll.LostMouseCapture += (_, _) =>
        {
            _armed = false;
            _dragging = false;
        };
        _scroll.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) { Previous(); e.Handled = true; }
            else if (e.Key == Key.Right) { Next(); e.Handled = true; }
        };
        Unloaded += (_, _) => EndDrag(snap: false);
        root.Children.Add(_scroll);
        Content = root;
        UpdateControls();
    }

    public void Add(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _items.Children.Add(element);
        UpdateControls();
    }

    public void GoTo(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        _selectedIndex = index;
        if (_scroll.IsLoaded)
            ScrollToSelected();
        else
            _scroll.Loaded += OnInitialLayout;
        UpdateControls();
        SelectedIndexChanged?.Invoke(this, index);
    }

    private void OnInitialLayout(object sender, RoutedEventArgs e)
    {
        _scroll.Loaded -= OnInitialLayout;
        _scroll.Dispatcher.BeginInvoke(new Action(ScrollToSelected),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public bool Next()
    {
        if (Count == 0 || (!Loop && _selectedIndex + 1 >= Count)) return false;
        GoTo((_selectedIndex + 1) % Count);
        return true;
    }

    public bool Previous()
    {
        if (Count == 0 || (!Loop && _selectedIndex == 0)) return false;
        GoTo((_selectedIndex - 1 + Count) % Count);
        return true;
    }

    private void ScrollToSelected()
    {
        if (!SnapToItems)
        {
            _scroll.ScrollToHorizontalOffset(Math.Clamp(_selectedIndex * Step, 0, _scroll.ScrollableWidth));
            return;
        }
        double offset = 0;
        for (var i = 0; i < _selectedIndex; i++)
        {
            var child = _items.Children[i];
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            offset += child.DesiredSize.Width;
        }
        _scroll.ScrollToHorizontalOffset(Math.Clamp(offset, 0, _scroll.ScrollableWidth));
    }

    /// <summary>
    /// Resolve a drag release to an item start; viewport-limited offsets intentionally
    /// prefer the farthest item in the gesture direction when targets overlap.
    /// </summary>
    public static int FindSnapIndex(IReadOnlyList<double> widths, double horizontalOffset,
        double scrollableWidth, bool forward)
    {
        ArgumentNullException.ThrowIfNull(widths);
        if (!double.IsFinite(horizontalOffset) || !double.IsFinite(scrollableWidth) || scrollableWidth < 0 ||
            widths.Any(width => !double.IsFinite(width) || width <= 0))
            throw new ArgumentOutOfRangeException(nameof(horizontalOffset));
        if (widths.Count == 0) return -1;
        var offset = Math.Clamp(horizontalOffset, 0, scrollableWidth);
        var start = 0d;
        var best = 0;
        var nearest = double.MaxValue;
        for (var i = 0; i < widths.Count; i++)
        {
            var distance = Math.Abs(offset - Math.Clamp(start, 0, scrollableWidth));
            if (distance < nearest - 0.0001 ||
                (Math.Abs(distance - nearest) <= 0.0001 &&
                    (forward ? i > best : i < best)))
            {
                best = i;
                nearest = distance;
            }
            start += widths[i];
        }
        return best;
    }

    private void EndDrag(bool snap)
    {
        if (!_armed) return;
        _armed = false;
        _dragging = false;
        if (_scroll.IsMouseCaptured) _scroll.ReleaseMouseCapture();
        if (!snap || Count == 0 || !SnapToItems) return;

        if (_scroll.ScrollableWidth <= 0) return;
        var sizes = new List<double>(Count);
        foreach (UIElement child in _items.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            sizes.Add(Math.Max(1, child.DesiredSize.Width));
        }
        var best = FindSnapIndex(sizes, _scroll.HorizontalOffset,
            _scroll.ScrollableWidth, _scroll.HorizontalOffset > _mouseDownOffset);
        if (best >= 0) GoTo(best);
    }

    public void Scroll(double offset) =>
        _scroll.ScrollToHorizontalOffset(
            Math.Clamp(_scroll.HorizontalOffset + offset, 0, _scroll.ScrollableWidth));

    private void UpdateControls()
    {
        _position.Text = Count == 0 ? "0 / 0" : $"{_selectedIndex + 1} / {Count}";
        _previous.IsEnabled = Count > 0 && (Loop || _selectedIndex > 0);
        _next.IsEnabled = Count > 0 && (Loop || _selectedIndex < Count - 1);
    }
}
