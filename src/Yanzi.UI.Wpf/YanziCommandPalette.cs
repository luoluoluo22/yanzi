using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

public sealed record YanziCommandItem(string Title, Action Execute);

/// <summary>Searchable command list with Enter/double-click invocation. No global hotkey is installed.</summary>
public sealed class YanziCommandPalette : UserControl
{
    private readonly List<YanziCommandItem> _commands = new();
    private readonly TextBox _query;
    private readonly ListBox _list;

    public YanziCommandPalette()
    {
        var root = new StackPanel();
        _query = YanziUi.WithStyle(new TextBox { ToolTip = "搜索命令" }, YanziUi.Styles.Input);
        _list = YanziUi.WithStyle(new ListBox { Height = 150, DisplayMemberPath = nameof(YanziCommandItem.Title),
            Margin = new Thickness(0, 9, 0, 0) }, YanziUi.Styles.List);
        _query.TextChanged += (_, _) => Refresh();
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
                _list.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) { Execute(); e.Handled = true; }
        };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Execute(); e.Handled = true; } };
        _list.MouseDoubleClick += (_, _) => Execute();
        root.Children.Add(_query);
        root.Children.Add(_list);
        Content = root;
    }

    public void Add(string title, Action execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(execute);
        _commands.Add(new YanziCommandItem(title, execute));
        Refresh();
    }

    public void FocusSearch() => _query.Focus();

    public void Execute()
    {
        var item = _list.SelectedItem as YanziCommandItem
            ?? _list.Items.Cast<YanziCommandItem>().FirstOrDefault();
        item?.Execute();
    }

    private void Refresh()
    {
        _list.ItemsSource = _commands.Where(c =>
            c.Title.Contains(_query.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
}
