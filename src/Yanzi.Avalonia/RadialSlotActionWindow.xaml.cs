using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class RadialSlotActionWindow : Window
{
    private readonly Func<string, IReadOnlyList<CommandItem>> _commandProvider;
    private readonly Action<CommandItem> _assignCommand;
    private readonly Action _addChildPage;
    private readonly Action _deleteCommand;
    private readonly Action _deleteChildPage;
    private readonly bool _allowDeleteCommand;
    private readonly bool _allowDeleteChildPage;

    public ObservableCollection<CommandItem> Results { get; } = [];

    public string TitleText { get; }
    private TextBox? SearchBoxControl { get; set; }
    private ListBox? ResultListControl { get; set; }

    public RadialSlotActionWindow(
        string title,
        Func<string, IReadOnlyList<CommandItem>> commandProvider,
        Action<CommandItem> assignCommand,
        Action addChildPage,
        Action deleteCommand,
        Action deleteChildPage,
        bool allowDeleteCommand,
        bool allowDeleteChildPage)
    {
        InitializeComponent();
        DataContext = this;
        TitleText = title;
        _commandProvider = commandProvider;
        _assignCommand = assignCommand;
        _addChildPage = addChildPage;
        _deleteCommand = deleteCommand;
        _deleteChildPage = deleteChildPage;
        _allowDeleteCommand = allowDeleteCommand;
        _allowDeleteChildPage = allowDeleteChildPage;
        SearchBoxControl = this.FindControl<TextBox>("SearchBox");
        ResultListControl = this.FindControl<ListBox>("ResultList");

        Loaded += (_, _) =>
        {
            RefreshResults();
            SearchBoxControl?.Focus();
            SearchBoxControl?.SelectAll();
        };

        UpdateButtonState();
    }



    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshResults();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Results.Count > 0)
        {
            if (ResultListControl != null)
            {
                ResultListControl.SelectedIndex = 0;
                ResultListControl.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ResultList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBoxControl?.Focus();
            if (SearchBoxControl != null)
                SearchBoxControl.CaretIndex = SearchBoxControl.Text?.Length ?? 0;
            e.Handled = true;
        }
    }

    private void ResultList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        CommitSelection();
    }

    private void AddChildPage_Click(object? sender, RoutedEventArgs e)
    {
        _addChildPage();
        Close();
    }

    private void DeleteCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (!_allowDeleteCommand)
            return;

        _deleteCommand();
        Close();
    }

    private void DeleteChildPage_Click(object? sender, RoutedEventArgs e)
    {
        if (!_allowDeleteChildPage)
            return;

        _deleteChildPage();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshResults()
    {
        Results.Clear();
        var keyword = SearchBoxControl?.Text?.Trim() ?? string.Empty;
        var candidates = _commandProvider(keyword);
        foreach (var command in candidates)
        {
            Results.Add(command);
        }

        if (Results.Count > 0 && ResultListControl != null && ResultListControl.SelectedIndex < 0)
        {
            ResultListControl.SelectedIndex = 0;
        }
    }

    private void CommitSelection()
    {
        if (ResultListControl?.SelectedItem is not CommandItem command)
            return;

        _assignCommand(command);
        Close();
    }

    private void UpdateButtonState()
    {
        if (this.FindControl<Button>("DeleteCommandButton") is { } deleteCommandButton)
        {
            deleteCommandButton.IsEnabled = _allowDeleteCommand;
        }

        if (this.FindControl<Button>("DeleteChildPageButton") is { } deleteChildPageButton)
        {
            deleteChildPageButton.IsEnabled = _allowDeleteChildPage;
        }
    }
}
