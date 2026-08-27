using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public class SlotActionItemViewModel
{
    public CommandItem Command { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string CategoryTag { get; }
    public string Glyph { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon != null;
    public string TagBackground { get; }
    public string TagForeground { get; }

    public SlotActionItemViewModel(CommandItem command)
    {
        Command = command;
        Title = command.Title;
        Description = string.IsNullOrWhiteSpace(command.Description) 
            ? (command.ActionKind == CommandActionKind.LaunchApplication ? $"应用路径：{command.ApplicationName}" : "快捷操作")
            : command.Description;
        Glyph = string.IsNullOrWhiteSpace(command.Glyph) ? "⚡" : command.Glyph;

        if (command.ActionKind == CommandActionKind.LaunchApplication)
        {
            Category = "Apps";
            CategoryTag = "应用程序";
            TagBackground = "#203B82F6";
            TagForeground = "#FF3B82F6";
            if (!string.IsNullOrEmpty(command.ApplicationName))
            {
                Icon = MacIconExtractor.GetCachedBitmap(command.ApplicationName);
            }
        }
        else if (command.ActionKind == CommandActionKind.KeyboardShortcut)
        {
            Category = "Shortcuts";
            CategoryTag = "系统动作";
            TagBackground = "#2010B981";
            TagForeground = "#FF10B981";
        }
        else if (command.ActionKind == CommandActionKind.Snippet)
        {
            Category = "Custom";
            CategoryTag = "快捷短语";
            TagBackground = "#20EC4899";
            TagForeground = "#FFEC4899";
        }
        else
        {
            Category = "Custom";
            CategoryTag = "自定义小程序";
            TagBackground = "#208B5CF6";
            TagForeground = "#FF8B5CF6";
        }
    }
}

public partial class RadialSlotActionWindow : Window
{
    private readonly Func<string, IReadOnlyList<CommandItem>> _commandProvider;
    private readonly Action<CommandItem> _assignCommand;
    private readonly Action _addChildPage;
    private readonly Action _deleteCommand;
    private readonly Action _deleteChildPage;
    private readonly bool _allowDeleteCommand;
    private readonly bool _allowDeleteChildPage;

    public ObservableCollection<SlotActionItemViewModel> FilteredResults { get; } = [];
    private readonly List<SlotActionItemViewModel> _allCandidates = [];

    public string TitleText { get; }
    private TextBox? SearchBoxControl { get; set; }
    private ListBox? ResultListControl { get; set; }
    private string _currentCategory = "All";

    public RadialSlotActionWindow()
        : this("插槽小程序与应用配置", _ => Array.Empty<CommandItem>(), _ => {}, () => {}, () => {}, () => {}, false, false)
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
            LoadAllCandidates();
            ApplyFilter();
            SearchBoxControl?.Focus();
            SearchBoxControl?.SelectAll();
        };

        UpdateButtonState();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void LoadAllCandidates()
    {
        _allCandidates.Clear();
        var rawCandidates = _commandProvider(string.Empty);
        foreach (var cmd in rawCandidates)
        {
            // Skip invalid or empty commands
            if (string.IsNullOrWhiteSpace(cmd.Title)) continue;
            _allCandidates.Add(new SlotActionItemViewModel(cmd));
        }
    }

    private void Tab_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            _currentCategory = rb.Name switch
            {
                "TabApps" => "Apps",
                "TabShortcuts" => "Shortcuts",
                "TabCustom" => "Custom",
                _ => "All"
            };
            ApplyFilter();
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredResults.Clear();
        var query = SearchBoxControl?.Text?.Trim() ?? string.Empty;

        var matching = _allCandidates.Where(item =>
        {
            // Category filter
            if (_currentCategory != "All" && !string.Equals(item.Category, _currentCategory, StringComparison.OrdinalIgnoreCase))
                return false;

            // Search query filter
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return PinyinHelper.Matches(item.Title, query)
                || PinyinHelper.Matches(item.Description, query)
                || PinyinHelper.Matches(item.CategoryTag, query)
                || PinyinHelper.Matches(item.Command.ApplicationName, query);
        });

        foreach (var item in matching)
        {
            FilteredResults.Add(item);
        }

        if (FilteredResults.Count > 0 && ResultListControl != null)
        {
            ResultListControl.SelectedIndex = 0;
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && FilteredResults.Count > 0)
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

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private async void AddLocalFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择要添加的应用程序或文件",
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                var isApp = filePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase);
                var cmd = new CommandItem
                {
                    ExtensionId = $"custom-file-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    Title = fileName,
                    Description = $"打开：{filePath}",
                    ActionKind = CommandActionKind.LaunchApplication,
                    ApplicationName = isApp ? fileName : filePath,
                    Glyph = isApp ? "💻" : "📄"
                };
                _assignCommand(cmd);
                Close();
            }
        }
    }

    private void DeleteCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (!_allowDeleteCommand)
            return;

        _deleteCommand();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CopyPrompt_Click(object? sender, RoutedEventArgs e)
    {
        var prompt = @"请帮我写一个燕子 (Yanzi) 的单文件 JSON 小程序。
要求格式如下：
{
  ""id"": ""唯一的英文id"",
  ""title"": ""小程序名称"",
  ""description"": ""小程序描述"",
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

    private void CommitSelection()
    {
        if (ResultListControl?.SelectedItem is not SlotActionItemViewModel selected)
            return;

        _assignCommand(selected.Command);
        Close();
    }

    private void UpdateButtonState()
    {
        if (this.FindControl<Button>("DeleteCommandButton") is { } deleteCommandButton)
        {
            deleteCommandButton.IsEnabled = _allowDeleteCommand;
        }
    }
}

