using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Composable data table: filtering, sorting, column visibility, pagination and selection
/// layered on the shared YanziTable visual primitive. Only the active page builds WPF rows.
/// This is bounded rendering, not scroll-viewport virtualization or remote paging.
/// </summary>
public sealed class YanziDataTable : UserControl
{
    private sealed record Row(int Id, IReadOnlyList<string> Cells);

    private readonly List<string> _columns = [];
    private readonly List<Row> _source = [];
    private readonly List<Row> _filtered = [];
    private readonly List<Row> _visibleRows = [];
    private readonly HashSet<int> _selected = [];
    private readonly HashSet<int> _hiddenColumns = [];
    private readonly YanziTable _table = new();
    private readonly YanziSearchBox _search = new("筛选记录...");
    private readonly StackPanel _columnHost = new();
    private YanziDropdownMenu? _columnMenu;
    private readonly TextBlock _selection = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous;
    private readonly Button _next;
    private int _pageSize = 5;
    private int _pageIndex;
    private int _sortColumn = -1;
    private bool _sortDescending;
    private int _filterColumn = -1;

    public event EventHandler? ViewChanged;
    public event EventHandler<int>? SelectionChanged;

    public int RowCount => _source.Count;
    public int FilteredRowCount => _filtered.Count;
    public int VisibleRowCount => _visibleRows.Count;
    public int ColumnCount => _columns.Count;
    public int SelectedCount => _selected.Count;
    public int PageIndex => _pageIndex;
    public int PageCount => Math.Max(1, (FilteredRowCount + PageSize - 1) / PageSize);
    public int SortColumn => _sortColumn;
    public bool SortDescending => _sortDescending;
    public int FilterColumn => _filterColumn;
    public IReadOnlyList<int> SelectedIds => _selected.Order().ToArray();
    public IReadOnlyList<int> VisibleRowIds => _visibleRows.Select(x => x.Id).ToArray();
    public IReadOnlyList<string> VisibleColumns =>
        Enumerable.Range(0, _columns.Count).Where(i => !_hiddenColumns.Contains(i))
            .Select(i => _columns[i]).ToArray();
    public YanziTable Table => _table;

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value < 1 || value > 500) throw new ArgumentOutOfRangeException(nameof(value));
            if (_pageSize == value) return;
            _pageSize = value;
            _pageIndex = 0;
            Refresh();
        }
    }

    public string FilterText
    {
        get => _search.Text;
        set => _search.Text = value ?? "";
    }

    public YanziDataTable()
    {
        var main = new StackPanel();
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _search.Width = 210;
        _search.HorizontalAlignment = HorizontalAlignment.Left;
        _search.TextChanged += (_, _) => { _pageIndex = 0; Refresh(); };
        Grid.SetColumn(_search, 0);
        toolbar.Children.Add(_search);
        Grid.SetColumn(_columnHost, 1);
        toolbar.Children.Add(_columnHost);
        main.Children.Add(toolbar);

        _table.SelectedRowChanged += (_, visibleIndex) =>
        {
            if (visibleIndex < 0 || visibleIndex >= _visibleRows.Count) return;
            var id = _visibleRows[visibleIndex].Id;
            if (!_selected.Add(id)) _selected.Remove(id);
            UpdateSelectedVisuals();
            SelectionChanged?.Invoke(this, id);
        };
        _table.SortedByColumn += (_, visibleColumn) =>
        {
            var shown = Enumerable.Range(0, _columns.Count)
                .Where(i => !_hiddenColumns.Contains(i)).ToArray();
            if (visibleColumn >= shown.Length) return;
            SortBy(shown[visibleColumn]);
        };
        main.Children.Add(_table);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _selection.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        footer.Children.Add(_selection);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _pageLabel.Margin = new Thickness(0, 0, 12, 0);
        _pageLabel.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        right.Children.Add(_pageLabel);

        _previous = YanziUi.WithStyle(new Button { Content = "上一页", MinHeight = 30,
            Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 0) },
            YanziUi.Styles.OutlineButton);
        _next = YanziUi.WithStyle(new Button { Content = "下一页", MinHeight = 30,
            Padding = new Thickness(9, 5, 9, 5) }, YanziUi.Styles.OutlineButton);
        AutomationProperties.SetName(_previous, "上一页");
        AutomationProperties.SetName(_next, "下一页");
        _previous.Click += (_, _) => PreviousPage();
        _next.Click += (_, _) => NextPage();
        right.Children.Add(_previous);
        right.Children.Add(_next);
        Grid.SetColumn(right, 1);
        footer.Children.Add(right);
        main.Children.Add(footer);

        Content = main;
        Unloaded += (_, _) => { if (_columnMenu is not null) _columnMenu.IsOpen = false; };
        Refresh();
    }

    public void SetData(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0 || columns.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Column headers are required.", nameof(columns));
        var input = rows.Select((cells, id) =>
        {
            if (cells.Count != columns.Count)
                throw new ArgumentException("Row width differs from column headers.", nameof(rows));
            return new Row(id, cells.Select(x => x ?? "").ToArray());
        }).ToArray();
        _columns.Clear();
        _columns.AddRange(columns);
        _source.Clear();
        _source.AddRange(input);
        _selected.Clear();
        _hiddenColumns.Clear();
        _pageIndex = 0;
        _sortColumn = -1;
        _sortDescending = false;
        _filterColumn = -1;
        BuildColumnMenu();
        Refresh();
    }

    public void SetFilter(string query, int column = -1)
    {
        if (column < -1 || column >= _columns.Count)
            throw new ArgumentOutOfRangeException(nameof(column));
        _filterColumn = column;
        if (!string.Equals(FilterText, query, StringComparison.Ordinal))
            FilterText = query;
        else { _pageIndex = 0; Refresh(); }
    }

    public void SortBy(int column)
    {
        if (column < 0 || column >= _columns.Count)
            throw new ArgumentOutOfRangeException(nameof(column));
        _sortDescending = _sortColumn == column && !_sortDescending;
        _sortColumn = column;
        _pageIndex = 0;
        Refresh();
    }

    public bool IsColumnVisible(int column) => column >= 0 && column < _columns.Count && !_hiddenColumns.Contains(column);

    public void SetColumnVisible(int column, bool visible)
    {
        if (column < 0 || column >= _columns.Count) throw new ArgumentOutOfRangeException(nameof(column));
        if (!visible && _columns.Count - _hiddenColumns.Count <= 1 && !_hiddenColumns.Contains(column))
        {
            BuildColumnMenu(); // Keep the last enabled item visibly checked.
            return;
        }
        if (visible) _hiddenColumns.Remove(column);
        else _hiddenColumns.Add(column);
        BuildColumnMenu();
        Refresh();
    }

    public void GoToPage(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        _pageIndex = index;
        Refresh();
    }

    public bool NextPage()
    {
        if (_pageIndex + 1 >= PageCount) return false;
        GoToPage(_pageIndex + 1);
        return true;
    }

    public bool PreviousPage()
    {
        if (_pageIndex == 0) return false;
        GoToPage(_pageIndex - 1);
        return true;
    }

    public void ToggleVisibleRow(int localIndex)
    {
        if (localIndex < 0 || localIndex >= _visibleRows.Count)
            throw new ArgumentOutOfRangeException(nameof(localIndex));
        _table.SelectRow(localIndex);
    }

    private void BuildColumnMenu()
    {
        if (_columnMenu is not null) _columnMenu.IsOpen = false;
        _columnHost.Children.Clear();
        var trigger = YanziUi.WithStyle(new Button
        {
            Content = "显示列 ⌄", MinHeight = 36, Padding = new Thickness(10, 6, 10, 6)
        }, YanziUi.Styles.OutlineButton);
        AutomationProperties.SetName(trigger, "显示列");
        var menu = new YanziDropdownMenu { PreferAbove = false }; 
        _columnMenu = menu;
        menu.AddLabel("显示列");
        for (var i = 0; i < _columns.Count; i++)
        {
            var col = i;
            menu.AddCheck(_columns[i], !_hiddenColumns.Contains(i), visible => SetColumnVisible(col, visible));
        }
        menu.Attach(trigger);
        _columnHost.Children.Add(trigger);
    }

    private void Refresh()
    {
        _filtered.Clear();
        var query = FilterText.Trim();
        foreach (var row in _source)
        {
            var matches = query.Length == 0 || (_filterColumn >= 0
                ? row.Cells[_filterColumn].Contains(query, StringComparison.CurrentCultureIgnoreCase)
                : row.Cells.Any(text => text.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
            if (matches) _filtered.Add(row);
        }
        if (_sortColumn >= 0)
        {
            var column = _sortColumn;
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            _filtered.Sort((a, b) =>
            {
                var compared = comparer.Compare(a.Cells[column], b.Cells[column]);
                if (compared == 0) compared = a.Id.CompareTo(b.Id);
                return _sortDescending ? -compared : compared;
            });
        }
        _pageIndex = Math.Clamp(_pageIndex, 0, PageCount - 1);
        _visibleRows.Clear();
        _visibleRows.AddRange(_filtered.Skip(_pageIndex * _pageSize).Take(_pageSize));
        var shown = Enumerable.Range(0, _columns.Count).Where(i => !_hiddenColumns.Contains(i)).ToArray();
        if (shown.Length > 0)
            _table.SetData(shown.Select(i => _columns[i]).ToArray(),
                _visibleRows.Select(row => (IReadOnlyList<string>)shown.Select(i => row.Cells[i]).ToArray()),
                Array.IndexOf(shown, _sortColumn), _sortDescending);
        UpdateSelectedVisuals();
        _pageLabel.Text = $"第 {_pageIndex + 1} / {PageCount} 页";
        _previous.IsEnabled = _pageIndex > 0;
        _next.IsEnabled = _pageIndex + 1 < PageCount;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectedVisuals()
    {
        _table.SetHighlightedRows(_visibleRows.Select((row, index) => (row, index))
            .Where(x => _selected.Contains(x.row.Id)).Select(x => x.index));
        _selection.Text = $"已选 {_filtered.Count(x => _selected.Contains(x.Id))} / {_filtered.Count} 行";
    }
}
