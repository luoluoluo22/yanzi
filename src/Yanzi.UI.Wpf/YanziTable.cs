using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Lightweight reusable shadcn Table without Windows DataGrid chrome.
/// Supports sorted string columns, selected row, hover and keyboard focus.
/// For large virtualized datasets retain the compatibility DataGrid API.
/// </summary>
public sealed class YanziTable : UserControl
{
    private readonly Border _outline = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
    private readonly StackPanel _stack = new();
    private readonly List<string> _columns = [];
    private readonly List<IReadOnlyList<string>> _rows = [];
    private readonly List<Button> _rowBorders = [];
    private readonly HashSet<int> _highlightedRows = [];
    private int _sortColumn = -1;
    private bool _descending;

    public int RowCount => _rows.Count;
    public int ColumnCount => _columns.Count;
    public int SortColumn => _sortColumn;
    public bool Descending => _descending;
    public int SelectedIndex { get; private set; } = -1;
    public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

    public event EventHandler<int>? SelectedRowChanged;
    public event EventHandler<int>? RowDoubleClicked;
    public event EventHandler<int>? SortedByColumn;

    public YanziTable()
    {
        SetResourceReference(ForegroundProperty, "Yanzi.Color.Foreground");
        _outline.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        _outline.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        _outline.Child = _stack;
        Content = _outline;
        ClipToBounds = true;
    }

    public void SetData(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string>> rows,
        int sortedColumn = -1, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0 || columns.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one titled column is required.", nameof(columns));
        var fresh = rows.Select(row =>
        {
            if (row.Count != columns.Count) throw new ArgumentException("Every row must match column count.", nameof(rows));
            return (IReadOnlyList<string>)row.Select(x => x ?? string.Empty).ToArray();
        }).ToList();
        _columns.Clear();
        _columns.AddRange(columns);
        _rows.Clear();
        _rows.AddRange(fresh);
        if (sortedColumn < -1 || sortedColumn >= columns.Count)
            throw new ArgumentOutOfRangeException(nameof(sortedColumn));
        _sortColumn = sortedColumn;
        _descending = descending;
        SelectedIndex = -1;
        _highlightedRows.Clear();
        Render();
    }

    public void SortBy(int column)
    {
        if (column < 0 || column >= _columns.Count) throw new ArgumentOutOfRangeException(nameof(column));
        _descending = _sortColumn == column && !_descending;
        _sortColumn = column;
        var compare = StringComparer.CurrentCultureIgnoreCase;
        _rows.Sort((a,b) => compare.Compare(a[column],b[column]) * (_descending ? -1 : 1));
        SelectedIndex = -1;
        Render();
        SortedByColumn?.Invoke(this, column);
    }

    public void SelectRow(int row)
    {
        if (row < 0 || row >= _rows.Count) throw new ArgumentOutOfRangeException(nameof(row));
        SelectedIndex = row;
        _highlightedRows.Clear();
        _highlightedRows.Add(row);
        UpdateHighlights();
        SelectedRowChanged?.Invoke(this, row);
    }

    private void Render()
    {
        _stack.Children.Clear();
        _rowBorders.Clear();
        if (_columns.Count == 0) return;
        var headings = new Grid { MinHeight = 39 };
        for(int i=0;i<_columns.Count;i++)
        {
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
            var col=i;
            var button = YanziUi.WithStyle(new Button
            {
                Content = _columns[i] + (_sortColumn==i ? (_descending ? " ↓" : " ↑") : ""),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 38,
                Padding = new Thickness(12,6,8,6),
                FontSize=12
            },YanziUi.Styles.GhostButton);
            button.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.MutedForeground");
            AutomationProperties.SetName(button, "排序：" + _columns[i]);
            button.Click += (_,_) => SortBy(col);
            Grid.SetColumn(button,i);
            headings.Children.Add(button);
        }
        _stack.Children.Add(headings);

        if (_rows.Count == 0)
        {
            var noResults = new TextBlock
            {
                Text = "没有找到匹配项", MinHeight = 88,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                TextAlignment = TextAlignment.Center
            };
            noResults.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            AutomationProperties.SetName(noResults, "没有找到匹配项");
            _stack.Children.Add(noResults);
        }

        for(int r=0;r<_rows.Count;r++)
        {
            var rowIndex=r;
            var cells = new Grid { MinHeight = 39 };
            for(int col=0;col<_columns.Count;col++)
            {
                cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
                var text = new TextBlock
                {
                    Text = _rows[r][col],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize=13,
                    Margin=new Thickness(12,8,8,8)
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
                Grid.SetColumn(text,col);
                cells.Children.Add(text);
            }
            var item=YanziUi.WithStyle(new Button
            {
                Content=cells, BorderThickness=new Thickness(0,1,0,0), Cursor=Cursors.Hand,
                HorizontalContentAlignment=HorizontalAlignment.Stretch
            }, "Yanzi.Table.Row");
            AutomationProperties.SetName(item,"表格第"+(r+1)+"行");
            item.Click+=(_,_)=>{ SelectRow(rowIndex); item.Focus(); };
            item.MouseDoubleClick+=(_,e)=>{ SelectRow(rowIndex); RowDoubleClicked?.Invoke(this,rowIndex); e.Handled=true; };
            item.KeyDown+=(_,e)=>
            {
                if(e.Key is Key.Down or Key.Up)
                {
                    var next=Math.Clamp(rowIndex+(e.Key==Key.Down?1:-1),0,_rowBorders.Count-1);
                    _rowBorders[next].Focus();
                    e.Handled=true;
                }
            };
            _rowBorders.Add(item);
            _stack.Children.Add(item);
        }
        UpdateHighlights();
    }

    /// <summary>Highlight multiple rows without altering the public click event.</summary>
    public void SetHighlightedRows(IEnumerable<int> localIndices)
    {
        ArgumentNullException.ThrowIfNull(localIndices);
        var indexes = localIndices.ToArray();
        if (indexes.Any(i => i < 0 || i >= _rows.Count))
            throw new ArgumentOutOfRangeException(nameof(localIndices));
        _highlightedRows.Clear();
        foreach (var index in indexes) _highlightedRows.Add(index);
        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        for(int i=0;i<_rowBorders.Count;i++)
            _rowBorders[i].SetResourceReference(Control.BackgroundProperty,
                _highlightedRows.Contains(i)?"Yanzi.Color.Accent":"Yanzi.Color.Card");
    }
}
