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

    public RadialSlotActionWindow()
        : this("搜寻/设置", _ => Array.Empty<CommandItem>(), _ => {}, () => {}, () => {}, () => {}, false, false)
    {
    }

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

    private async void CopyPrompt_Click(object? sender, RoutedEventArgs e)
    {
        var prompt = @"请帮我写一个燕子启动器 (Yanzi) 的单文件 JSON 扩展。
要求格式如下：
{
  ""id"": ""唯一的英文id"",
  ""title"": ""扩展名称"",
  ""description"": ""扩展描述"",
  ""glyph"": ""图标(可以使用emoji)"",
  ""actionKind"": ""操作类型(LaunchApplication 或 KeyboardShortcut 或 AppleScript)"",
  ""applicationName"": ""当actionKind为LaunchApplication时提供，可以填应用名(如WeChat)或URL"",
  ""shortcutKey"": ""当actionKind为KeyboardShortcut时提供(如: a, c, space)"",
  ""shortcutCommand"": true/false,
  ""shortcutShift"": true/false,
  ""shortcutOption"": true/false,
  ""shortcutControl"": true/false,
  ""scriptSource"": ""当actionKind为AppleScript时提供的脚本代码""
}
注意：只输出 JSON 即可，不附带多余文本。";

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(prompt);
            
            if (sender is Button btn)
            {
                var oldContent = btn.Content;
                btn.Content = "已复制 ✓";
                await Task.Delay(2000);
                btn.Content = oldContent;
            }
        }
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
