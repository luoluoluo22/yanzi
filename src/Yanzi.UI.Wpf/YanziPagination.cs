using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Accessible previous/next paging control for host-owned paged data.</summary>
public sealed class YanziPagination : UserControl
{
    private readonly Button _previous;
    private readonly Button _next;
    private readonly TextBlock _status;
    private int _page = 1;
    private int _pageCount = 1;

    public event EventHandler<int>? PageChanged;
    public int Page { get => _page; set => SetPage(value); }
    public int PageCount
    {
        get => _pageCount;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            _pageCount = value;
            SetPage(Math.Min(_page, value));
            Update();
        }
    }

    public YanziPagination()
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _previous = YanziUi.WithStyle(new Button { Content = "‹", MinWidth = 38 }, YanziUi.Styles.OutlineButton);
        _next = YanziUi.WithStyle(new Button { Content = "›", MinWidth = 38 }, YanziUi.Styles.OutlineButton);
        System.Windows.Automation.AutomationProperties.SetName(_previous, "上一页");
        System.Windows.Automation.AutomationProperties.SetName(_next, "下一页");
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        _previous.Click += (_, _) => SetPage(_page - 1);
        _next.Click += (_, _) => SetPage(_page + 1);
        group.Children.Add(_previous);
        group.Children.Add(_status);
        group.Children.Add(_next);
        Content = group;
        Update();
    }

    public void SetPage(int page)
    {
        var next = Math.Clamp(page, 1, _pageCount);
        if (next == _page) { Update(); return; }
        _page = next;
        Update();
        PageChanged?.Invoke(this, _page);
    }

    private void Update()
    {
        _status.Text = $"{_page} / {_pageCount}";
        _previous.IsEnabled = _page > 1;
        _next.IsEnabled = _page < _pageCount;
    }
}
