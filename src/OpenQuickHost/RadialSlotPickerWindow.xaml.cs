using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class RadialSlotPickerWindow : Window
{
    public enum PickerAction
    {
        AddCommand,
        AddChildPage
    }

    private readonly Func<string, IReadOnlyList<CommandItem>> _provider;
    private readonly Func<Window, CommandItem?>? _createExtension;

    public RadialSlotPickerWindow(
        Func<string, IReadOnlyList<CommandItem>> provider,
        bool allowAddChildPage = false,
        Func<Window, CommandItem?>? createExtension = null)
    {
        InitializeComponent();
        App.EnableSilentLoading(this);
        _provider = provider;
        _createExtension = createExtension;
        Results = [];
        CommandListBox.ItemsSource = Results;
        AddChildPageButton.Visibility = allowAddChildPage ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            RefreshResults();
            SearchBox.Focus();
        };
    }

    public ObservableCollection<CommandItem> Results { get; }

    public CommandItem? SelectedCommand { get; private set; }

    public PickerAction SelectedAction { get; private set; } = PickerAction.AddCommand;

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshResults();
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down && Results.Count > 0)
        {
            CommandListBox.SelectedIndex = 0;
            CommandListBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
    }

    private void CommandListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
    }

    private void CommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CommitSelection();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AddChildPageButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = PickerAction.AddChildPage;
        DialogResult = true;
    }

    private void CreateExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_createExtension == null)
        {
            return;
        }

        var command = _createExtension(this);
        if (command == null)
        {
            SearchBox.Focus();
            return;
        }

        SelectedAction = PickerAction.AddCommand;
        SelectedCommand = command;
        DialogResult = true;
    }

    private void RefreshResults()
    {
        Results.Clear();
        foreach (var command in _provider(SearchBox.Text ?? string.Empty))
        {
            Results.Add(command);
        }

        if (Results.Count > 0)
        {
            CommandListBox.SelectedIndex = 0;
        }

        ResultSummaryText.Text = Results.Count == 0 ? "无匹配结果" : $"找到 {Results.Count} 项";
    }

    private void CommitSelection()
    {
        if (CommandListBox.SelectedItem is not CommandItem command)
        {
            return;
        }

        SelectedAction = PickerAction.AddCommand;
        SelectedCommand = command;
        DialogResult = true;
    }
}
