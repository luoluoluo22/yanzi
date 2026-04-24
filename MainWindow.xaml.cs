using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenQuickHost.Sync;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Controls;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int HotKeyId = 0x5301;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotKey = 0x0312;

    private readonly List<CommandItem> _allCommands;
    private readonly CloudSyncClient? _cloudSyncClient;
    private readonly SyncOptions _syncOptions;
    private readonly Dictionary<string, CommandItem> _localExtensionIndex;
    private readonly Dictionary<int, CommandItem> _registeredExtensionHotkeys = new();
    private CommandItem? _selectedCommand;
    private CommandItem? _lastActionableCommand;
    private HostedPluginSession? _activeHostedView;
    private string _activeQueryArgument = string.Empty;
    private string _hostedViewInput = string.Empty;
    private string _hostedViewOutput = string.Empty;
    private string _hostedViewStatus = "准备就绪。";
    private string _lastRunMessage = "准备就绪。输入关键字后按 Enter 运行。";
    private string _syncStatus = "云同步未初始化。";
    private HwndSource? _source;
    private bool _authPromptActive;
    private bool _isPinned;
    private int _nextExtensionHotkeyId = 0x5400;
    private QuickPanelWindow? _quickPanel;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowIcon();
        HostAssets.EnsureCreated();
        _syncOptions = SyncConfigLoader.Load();
        LocalExtensionCatalog.EnsureSampleExtension();
        if (_syncOptions.IsConfigured)
        {
            _cloudSyncClient = new CloudSyncClient(_syncOptions);
        }

        _allCommands = CreateSeedCommands();
        _allCommands.AddRange(LocalExtensionCatalog.LoadCommands());
        _localExtensionIndex = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension)
            .GroupBy(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        FilteredCommands = new ObservableCollection<CommandItem>(_allCommands);
        SelectedCommand = FilteredCommands.FirstOrDefault();
        DataContext = this;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;

        Closing += (s, e) => InputHookService.Stop();

        _quickPanel = new QuickPanelWindow(this);
    }

    private void ApplyWindowIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/logo.png", UriKind.Absolute));
        }
        catch
        {
            // Ignore icon failures so the launcher can still start.
        }
    }

    public ObservableCollection<CommandItem> FilteredCommands { get; }

    public bool AllowClose { get; set; }

    public CommandItem? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (Equals(value, _selectedCommand))
            {
                return;
            }

            _selectedCommand = value;
            if (value != null && !IsInternalCommand(value))
            {
                _lastActionableCommand = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveSelectedCommand));
            OnPropertyChanged(nameof(FooterHint));
        }
    }

    public string LastRunMessage
    {
        get => _lastRunMessage;
        set
        {
            if (value == _lastRunMessage)
            {
                return;
            }

            _lastRunMessage = value;
            OnPropertyChanged();
        }
    }

    public string VisibleCountText => $"{FilteredCommands.Count} 条结果";

    public string FooterHint => SelectedCommand == null
        ? "Up / Down 切换   Enter 执行   Ctrl+K 动作   Esc 收起"
        : SelectedCommand.SupportsQueryArgument && !string.IsNullOrWhiteSpace(_activeQueryArgument)
            ? $"{SelectedCommand.Title}   ·   {_activeQueryArgument}   ·   Ctrl+K 动作"
            : $"{SelectedCommand.Title}   ·   {SelectedCommand.Category}   ·   Ctrl+K 动作";

    public bool IsHostedViewOpen => _activeHostedView != null;

    public System.Windows.Media.Brush PinButtonBrush => _isPinned
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFF59E0B")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF777777")!;

    public string PinButtonTooltip => _isPinned ? "已固定，失去焦点时不自动关闭" : "点击固定，失去焦点时不自动关闭";

    public string HostedViewTitle => _activeHostedView?.Definition.Title ?? "插件视图";

    public string HostedViewSubtitle => _activeHostedView?.Definition.Description ?? "插件正在当前窗口中运行。";

    public string HostedViewCommandLabel => _activeHostedView == null
        ? "未激活"
        : $"{_activeHostedView.Command.Title} · {_activeHostedView.Command.ExtensionId}";

    public string HostedViewInputLabel => _activeHostedView?.Definition.InputLabel ?? "输入";

    public string HostedViewOutputLabel => _activeHostedView?.Definition.OutputLabel ?? "输出";

    public string HostedViewInputPlaceholder => _activeHostedView?.Definition.InputPlaceholder ?? "输入内容后开始执行。";

    public string HostedViewActionButtonText => _activeHostedView?.Definition.ActionButtonText ?? "执行";

    public string HostedViewInput
    {
        get => _hostedViewInput;
        set
        {
            if (value == _hostedViewInput)
            {
                return;
            }

            _hostedViewInput = value;
            OnPropertyChanged();
        }
    }

    public string HostedViewOutput
    {
        get => _hostedViewOutput;
        set
        {
            if (value == _hostedViewOutput)
            {
                return;
            }

            _hostedViewOutput = value;
            OnPropertyChanged();
        }
    }

    public string HostedViewStatus
    {
        get => _hostedViewStatus;
        set
        {
            if (value == _hostedViewStatus)
            {
                return;
            }

            _hostedViewStatus = value;
            OnPropertyChanged();
        }
    }

    public CommandItem? EffectiveSelectedCommand =>
        SelectedCommand == null
            ? null
            : IsInternalCommand(SelectedCommand) && _lastActionableCommand != null
                ? ResolveRunnableCommand(_lastActionableCommand)
                : ResolveRunnableCommand(SelectedCommand);

    public string SyncStatus
    {
        get => _syncStatus;
        set
        {
            if (value == _syncStatus)
            {
                return;
            }

            _syncStatus = value;
            OnPropertyChanged();
        }
    }

    public string SyncSummaryText =>
        _cloudSyncClient == null
            ? "云同步未配置"
            : $"用户 {_cloudSyncClient.CurrentUserLabel} · {_allCommands.Count(x => x.Source == CommandSource.Cloud)} 个云扩展";

    public string SyncBaseUrl => _syncOptions.BaseUrl;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();

        if (_cloudSyncClient == null)
        {
            InputHookService.Start(() => _quickPanel?.ShowAtMouse());
            return;
        }

        InputHookService.Start(() => _quickPanel?.ShowAtMouse());
        if (!AppSettingsStore.Load().RefreshCloudOnStartup)
        {
            return;
        }

        await RefreshCloudStateAsync();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            RunSelectedCommand();
            e.Handled = true;
        }
    }

    private void CommandList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is CommandItem item)
        {
            SelectedCommand = item;
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        RunSelectedCommand();
    }

    private void CommandList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSelectedCommand();
            e.Handled = true;
        }
    }

    private void CommandList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dependencyObject = e.OriginalSource as DependencyObject;
        while (dependencyObject != null && dependencyObject is not ListBoxItem)
        {
            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        if (dependencyObject is ListBoxItem item && item.DataContext is CommandItem command)
        {
            SelectedCommand = command;
            CommandList.SelectedItem = command;
        }
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        RunSelectedCommand();
    }

    private void CloseHostedViewButton_Click(object sender, RoutedEventArgs e)
    {
        CloseHostedView();
    }

    private async void HostedViewRunButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshHostedViewOutputAsync();
    }

    private void HostedViewInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_activeHostedView == null)
        {
            return;
        }

        if (UsesScriptHostedView(_activeHostedView.Definition))
        {
            HostedViewStatus = "脚本视图已更新输入，点击右下角按钮执行。";
            return;
        }

        RefreshHostedViewOutput();
    }

    private async void CreateDesktopShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await CreateDesktopShortcutAsync();
    }

    private async void RenameCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RenameSelectedExtensionAsync();
    }

    private async void EditExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedExtensionAsync();
    }

    private async void SetCommandShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SetSelectedExtensionShortcutAsync();
    }

    private async void DeleteExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedExtensionAsync();
    }

    private async void RefreshCloudButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudStateAsync();
    }

    private async void SyncSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await SyncSelectedCommandAsync();
    }

    private async void DownloadSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await DownloadSelectedCommandAsync();
    }

    private async void AddJsonExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await AddJsonExtensionAsync();
    }

    private async void EditJsonExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedExtensionAsync();
    }

    private async void DeleteExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedExtensionAsync();
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        SignOutFromSettings();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppSettingsStore.Load().CloseToTray)
        {
            HideToTray();
            return;
        }

        AllowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (IsHostedViewOpen)
            {
                CloseHostedView();
                e.Handled = true;
                return;
            }

            if (FooterQuickMenuPopup.IsOpen)
            {
                FooterQuickMenuPopup.IsOpen = false;
                return;
            }

            HideToTray();
            return;
        }

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenCommandActionsMenu();
            e.Handled = true;
        }
    }

    private void FooterQuickMenuButton_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = !FooterQuickMenuPopup.IsOpen;
    }

    private async void FooterAddExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        await AddJsonExtensionAsync();
    }

    private async void QuickMenuAddExtension_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        await AddJsonExtensionAsync();
    }

    private void QuickMenuInstallSkill_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        ExportSkillsToFolder();
    }

    private void QuickMenuOpenSettings_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        if (System.Windows.Application.Current is App app)
        {
            app.OpenSettingsWindow("general");
            LastRunMessage = "已打开设置。";
        }
    }

    private async void QuickMenuRefreshCloud_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        await RefreshCloudStateAsync();
    }

    private void QuickMenuOpenDocs_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = HostAssets.DocsReadmePath,
                UseShellExecute = true
            });
            LastRunMessage = "已打开帮助文档。";
        }
        catch (Exception ex)
        {
            SyncStatus = $"打开文档失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void QuickMenuOpenAbout_Click(object sender, RoutedEventArgs e)
    {
        FooterQuickMenuPopup.IsOpen = false;
        if (System.Windows.Application.Current is App app)
        {
            app.OpenSettingsWindow("about");
            LastRunMessage = "已打开关于页面。";
        }
    }

    private void ExportSkillsToFolder()
    {
        var optionsDialog = new SkillExportOptionsWindow
        {
            Owner = this
        };
        if (optionsDialog.ShowDialog() != true)
        {
            return;
        }

        string? destinationRoot = null;
        if (optionsDialog.SelectedScope == SkillExportScope.Project)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "选择项目根目录",
                UseDescriptionForTitle = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            destinationRoot = dialog.SelectedPath;
        }

        try
        {
            var previewPath = SkillInstallerService.GetExportPath(destinationRoot, optionsDialog.SelectedTarget, optionsDialog.SelectedScope);
            var result = SkillInstallerService.ExportSkills(
                HostAssets.SkillsPath,
                destinationRoot,
                optionsDialog.SelectedTarget,
                optionsDialog.SelectedScope);
            LastRunMessage = $"已导出 {result.SkillCount} 个 Skill 到 {result.Target} {result.Scope}";
            SyncStatus = $"已导出到 {previewPath}（相对路径：{result.RelativePath}）";
        }
        catch (Exception ex)
        {
            SyncStatus = $"导出 Skill 失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void CommandList_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs? e)
    {
        if (!UpdateCommandContextMenuState() && e != null)
        {
            e.Handled = true;
        }
    }

    private bool UpdateCommandContextMenuState()
    {
        var current = SelectedCommand;
        var actionable = current != null && !IsInternalCommand(current) ? current : _lastActionableCommand;
        var resolved = actionable == null ? null : ResolveRunnableCommand(actionable);

        if (resolved == null)
        {
            return false;
        }

        CreateDesktopShortcutMenuItem.IsEnabled = resolved.OpenTarget is { Length: > 0 } && !IsInternalCommand(resolved);
        var canManageLocalExtension = resolved.Source == CommandSource.LocalExtension;
        SetCommandShortcutMenuItem.IsEnabled = canManageLocalExtension;
        RenameCommandMenuItem.IsEnabled = canManageLocalExtension;
        EditExtensionMenuItem.IsEnabled = canManageLocalExtension;
        DeleteExtensionMenuItem.IsEnabled = canManageLocalExtension;
        return true;
    }

    private void ApplyFilter(string? query)
    {
        var normalized = (query ?? string.Empty).Trim();
        _activeQueryArgument = string.Empty;
        var matches = string.IsNullOrWhiteSpace(normalized)
            ? _allCommands
            : _allCommands
                .Select(command => new
                {
                    Command = command,
                    Match = BuildCommandMatch(command, normalized)
                })
                .Where(x => x.Match.IsMatch)
                .OrderByDescending(x => x.Match.Priority)
                .ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Command)
                .ToList();

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var leadingCommand = matches.FirstOrDefault();
            if (leadingCommand != null)
            {
                _activeQueryArgument = ExtractQueryArgument(leadingCommand, normalized);
            }
        }

        FilteredCommands.Clear();
        foreach (var item in matches)
        {
            FilteredCommands.Add(item);
        }

        SelectedCommand = FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));
    }

    private void MoveSelection(int delta)
    {
        if (FilteredCommands.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedCommand == null ? -1 : FilteredCommands.IndexOf(SelectedCommand);
        var nextIndex = currentIndex + delta;
        if (nextIndex < 0)
        {
            nextIndex = 0;
        }
        else if (nextIndex >= FilteredCommands.Count)
        {
            nextIndex = FilteredCommands.Count - 1;
        }

        SelectedCommand = FilteredCommands[nextIndex];
        CommandList.SelectedItem = SelectedCommand;
        CommandList.ScrollIntoView(SelectedCommand);
    }

    private async void RunSelectedCommand()
    {
        if (SelectedCommand == null)
        {
            LastRunMessage = "没有可执行的命令。";
            return;
        }

        await ExecuteCommandAsync(ResolveRunnableCommand(SelectedCommand));
    }

    private async Task ExecuteCommandAsync(CommandItem runnable, string? explicitInput = null, string launchSource = "launcher")
    {
        if (runnable.HostedView != null)
        {
            OpenHostedView(runnable);
            return;
        }

        if (HandleInternalCommand(runnable))
        {
            return;
        }

        if (ScriptExtensionRunner.CanExecute(runnable))
        {
            await ExecuteScriptCommandAsync(runnable, explicitInput ?? BuildScriptInput(runnable, SearchBox.Text), launchSource);
            return;
        }

        var executionTarget = BuildExecutionTarget(runnable, SearchBox.Text);
        if (executionTarget is { Length: > 0 })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executionTarget,
                    UseShellExecute = true
                });
                HostAssets.AppendRecent(runnable.Title);
                HostAssets.AppendLog($"Executed command: {runnable.Title} -> {executionTarget}");
                LastRunMessage = $"已运行：{runnable.Title} -> {executionTarget}";
                return;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Command failed: {runnable.Title} -> {ex.Message}");
                LastRunMessage = $"运行失败：{runnable.Title}，{ex.Message}";
                return;
            }
        }

        HostAssets.AppendLog($"Command has no executable target: {runnable.Title}");
        LastRunMessage = runnable.Source == CommandSource.Cloud
            ? $"云端记录已存在，但当前机器没有安装对应扩展：{runnable.ExtensionId}。先下载扩展包或放入本地扩展目录。"
            : $"当前命令没有 openTarget，也没有脚本入口：{runnable.Title}";
    }

    private void OpenHostedView(CommandItem command)
    {
        if (command.HostedView == null)
        {
            return;
        }

        _activeHostedView = new HostedPluginSession(command, command.HostedView);
        HostedViewInput = string.Empty;
        HostedViewOutput = command.HostedView.EmptyState ?? "等待插件输出。";
        HostedViewStatus = $"已进入 {command.Title}。输入内容后可直接在当前窗口完成操作。";
        OnPropertyChanged(nameof(IsHostedViewOpen));
        OnPropertyChanged(nameof(HostedViewTitle));
        OnPropertyChanged(nameof(HostedViewSubtitle));
        OnPropertyChanged(nameof(HostedViewCommandLabel));
        OnPropertyChanged(nameof(HostedViewInputLabel));
        OnPropertyChanged(nameof(HostedViewOutputLabel));
        OnPropertyChanged(nameof(HostedViewInputPlaceholder));
        OnPropertyChanged(nameof(HostedViewActionButtonText));
        LastRunMessage = $"已打开插件视图：{command.Title}";
        Dispatcher.BeginInvoke(() => HostedViewInputBox.Focus());
    }

    private void CloseHostedView()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        var title = _activeHostedView.Command.Title;
        _activeHostedView = null;
        HostedViewInput = string.Empty;
        HostedViewOutput = string.Empty;
        HostedViewStatus = "已关闭插件视图。";
        OnPropertyChanged(nameof(IsHostedViewOpen));
        OnPropertyChanged(nameof(HostedViewTitle));
        OnPropertyChanged(nameof(HostedViewSubtitle));
        OnPropertyChanged(nameof(HostedViewCommandLabel));
        OnPropertyChanged(nameof(HostedViewInputLabel));
        OnPropertyChanged(nameof(HostedViewOutputLabel));
        OnPropertyChanged(nameof(HostedViewInputPlaceholder));
        OnPropertyChanged(nameof(HostedViewActionButtonText));
        LastRunMessage = $"已返回命令列表：{title}";
        SearchBox.Focus();
    }

    private void RefreshHostedViewOutput()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        var input = HostedViewInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            HostedViewOutput = _activeHostedView.Definition.EmptyState ?? "等待插件输出。";
            HostedViewStatus = "输入内容后即可执行插件动作。";
            return;
        }

        HostedViewOutput = ExecuteHostedView(_activeHostedView.Definition, input);
        HostedViewStatus = $"已更新 {_activeHostedView.Command.Title} 输出。";
    }

    private async Task RefreshHostedViewOutputAsync()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        if (!UsesScriptHostedView(_activeHostedView.Definition))
        {
            RefreshHostedViewOutput();
            return;
        }

        var input = HostedViewInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            HostedViewOutput = _activeHostedView.Definition.EmptyState ?? "等待插件输出。";
            HostedViewStatus = "输入内容后即可执行插件动作。";
            return;
        }

        if (!ScriptExtensionRunner.CanExecute(_activeHostedView.Command))
        {
            HostedViewOutput = "当前宿主视图声明为脚本模式，但扩展没有有效的脚本入口。";
            HostedViewStatus = "脚本入口缺失。";
            return;
        }

        HostedViewStatus = $"正在执行 {_activeHostedView.Command.Title} 脚本...";
        var result = await ScriptExtensionRunner.ExecuteAsync(_activeHostedView.Command, input, "hosted-view");
        HostedViewOutput = result.Success
            ? string.IsNullOrWhiteSpace(result.Output) ? "脚本执行完成，但没有返回输出。" : result.Output
            : $"脚本执行失败{Environment.NewLine}{Environment.NewLine}{result.Error}";
        HostedViewStatus = result.Success
            ? $"已更新 {_activeHostedView.Command.Title} 输出。"
            : $"脚本执行失败：{result.Error}";
    }

    private static string ExecuteHostedView(HostedPluginViewDefinition definition, string input)
    {
        return definition.ActionType switch
        {
            "template" when !string.IsNullOrWhiteSpace(definition.OutputTemplate)
                => definition.OutputTemplate.Replace("{input}", input, StringComparison.Ordinal),
            "uppercase" => input.ToUpperInvariant(),
            "reverse" => new string(input.Reverse().ToArray()),
            "mock-translate" => BuildMockTranslation(input),
            _ when !string.IsNullOrWhiteSpace(definition.OutputTemplate)
                => definition.OutputTemplate.Replace("{input}", input, StringComparison.Ordinal),
            _ => input
        };
    }

    private static bool UsesScriptHostedView(HostedPluginViewDefinition definition)
    {
        return string.Equals(definition.ActionType, "script", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMockTranslation(string input)
    {
        var trimmed = input.Trim();
        return
            $"[译文预览]{Environment.NewLine}{Environment.NewLine}" +
            $"EN: {trimmed}{Environment.NewLine}{Environment.NewLine}" +
            $"说明：当前是宿主内置的示例翻译输出，用来验证“双栏插件界面”协议。" +
            $"{Environment.NewLine}后续你可以把这个 actionType 替换成真正的翻译服务或脚本执行器。";
    }

    private async Task RefreshCloudStateAsync()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        try
        {
            SyncStatus = "正在读取云端扩展和用户同步状态...";
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            var me = await _cloudSyncClient.GetMeAsync();
            var cloudExtensions = await _cloudSyncClient.GetExtensionsAsync();
            var userExtensions = await _cloudSyncClient.GetUserExtensionsAsync();
            MergeCloudCommands(cloudExtensions, userExtensions);
            var autoSyncedCount = await AutoSyncLocalExtensionsAsync();
            if (autoSyncedCount > 0)
            {
                cloudExtensions = await _cloudSyncClient.GetExtensionsAsync();
                userExtensions = await _cloudSyncClient.GetUserExtensionsAsync();
                MergeCloudCommands(cloudExtensions, userExtensions);
            }

            SyncStatus = $"已登录 {me?.Username ?? _cloudSyncClient.CurrentUserLabel}";
            LastRunMessage = autoSyncedCount > 0
                ? $"已刷新云端扩展：{cloudExtensions.Count} 个，并自动同步了 {autoSyncedCount} 个本地扩展。"
                : $"已刷新云端扩展：{cloudExtensions.Count} 个，全局用户扩展：{userExtensions.Count} 条。";
            OnPropertyChanged(nameof(SyncSummaryText));
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await RefreshCloudStateAsync();
                return;
            }

            SyncStatus = $"云同步读取失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task SyncSelectedCommandAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法写入。";
            return;
        }

        if (SelectedCommand == null)
        {
            SyncStatus = "没有可同步的命令。";
            return;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            SyncStatus = $"正在同步 {SelectedCommand.Title} ...";
            var version = SelectedCommand.DeclaredVersion;
            await _cloudSyncClient.UpsertExtensionAsync(SelectedCommand);
            var packageBytes = ExtensionPackageService.BuildPackage(SelectedCommand, version);
            await _cloudSyncClient.UploadExtensionArchiveAsync(SelectedCommand, packageBytes, version);
            await _cloudSyncClient.UpsertUserExtensionAsync(SelectedCommand);
            SelectedCommand.MarkAsSynced(version);
            LastRunMessage = $"已把命令和扩展包同步到云端：{SelectedCommand.Title}";
            await RefreshCloudStateAsync();
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await SyncSelectedCommandAsync();
                return;
            }

            SyncStatus = $"同步失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task DownloadSelectedCommandAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法下载。";
            return;
        }

        if (SelectedCommand == null)
        {
            SyncStatus = "没有可下载的命令。";
            return;
        }

        if (!SelectedCommand.HasArchive)
        {
            SyncStatus = "当前命令在云端没有扩展包。";
            return;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            SyncStatus = $"正在下载 {SelectedCommand.Title} 的扩展包 ...";
            var packageBytes = await _cloudSyncClient.DownloadExtensionArchiveAsync(SelectedCommand.ExtensionId);
            var version = SelectedCommand.CloudVersion ?? "0.1.0";
            var path = await ExtensionPackageService.SavePackageAsync(SelectedCommand.ExtensionId, version, packageBytes);
            SelectedCommand.SetLocalPackagePath(path);
            LastRunMessage = $"扩展包已下载到本地：{path}";
            SyncStatus = $"下载完成：{SelectedCommand.Title}";
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await DownloadSelectedCommandAsync();
                return;
            }

            SyncStatus = $"下载失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task AddJsonExtensionAsync()
    {
        try
        {
            var command = ShowJsonExtensionEditorAsync(
                LocalExtensionCatalog.CreateTemplateJson(),
                isEditMode: false);
            if (command == null)
            {
                return;
            }

            LastRunMessage = $"已添加本地 JSON 扩展：{command.Title}";
            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(command);
                await RefreshCloudStateAsync();
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"AddJsonExtensionAsync failed: {ex}");
            SyncStatus = $"添加扩展失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task EditSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可编辑的扩展。";
            return;
        }

        var editable = ResolveRunnableCommand(sourceCommand);
        if (editable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接编辑。";
            return;
        }

        try
        {
            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorAsync(manifestJson, isEditMode: true);
            if (updated == null)
            {
                return;
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(updated);
                await RefreshCloudStateAsync();
            }
        }
        catch (Exception ex)
        {
            SyncStatus = $"编辑失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task DeleteSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可删除的扩展。";
            return;
        }

        var deletable = ResolveRunnableCommand(sourceCommand);
        if (deletable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接删除。";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确认删除扩展“{deletable.Title}”吗？\n这会删除本地扩展目录，并卸载当前用户的云端安装记录。",
            "删除扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            LocalExtensionCatalog.DeleteExtension(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await _cloudSyncClient.RemoveUserExtensionAsync(deletable.ExtensionId);
                await RefreshCloudStateAsync();
            }

            LastRunMessage = $"已删除本地扩展：{deletable.Title}";
            SyncStatus = $"已删除扩展：{deletable.Title}";
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await DeleteSelectedExtensionAsync();
                return;
            }

            SyncStatus = $"删除失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync(bool forcePrompt = false)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        if (forcePrompt || !_cloudSyncClient.HasCredential)
        {
            if (!ShowLoginDialog())
            {
                SyncStatus = "未登录，云同步不可用。";
                return false;
            }
        }

        try
        {
            await _cloudSyncClient.EnsureAuthenticatedAsync();
            OnPropertyChanged(nameof(SyncSummaryText));
            return true;
        }
        catch (Exception ex)
        {
            if (ShowLoginDialog(FormatExceptionMessage(ex)))
            {
                await _cloudSyncClient.EnsureAuthenticatedAsync();
                OnPropertyChanged(nameof(SyncSummaryText));
                return true;
            }

            SyncStatus = "未登录，云同步不可用。";
            return false;
        }
    }

    private async Task<bool> TryRecoverAuthenticationAsync(Exception ex)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        if (!message.Contains("401", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("登录", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("凭据", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _cloudSyncClient.ClearCredential();
        return await EnsureAuthenticatedAsync(forcePrompt: true);
    }

    private bool ShowLoginDialog(string? errorMessage = null)
    {
        if (_cloudSyncClient == null || _authPromptActive)
        {
            return false;
        }

        _authPromptActive = true;
        try
        {
            ShowPanel();
            var saved = SecureCredentialStore.Load();
            var dialog = new LoginWindow(saved?.Username);
            dialog.SendRegistrationCodeAsync = (email, username) => _cloudSyncClient.SendRegistrationCodeAsync(email, username);
            dialog.RegisterAsyncHandler = (email, username, password, code) => _cloudSyncClient.RegisterAsync(email, username, password, code);
            if (IsVisible)
            {
                dialog.Owner = this;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                dialog.ShowError(errorMessage);
            }

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return false;
            }

            _cloudSyncClient.SetCredential(dialog.Username, dialog.Password, dialog.RememberCredential);
            return true;
        }
        finally
        {
            _authPromptActive = false;
        }
    }

    private static string FormatExceptionMessage(Exception ex)
    {
        var messages = new List<string>();
        Exception? current = ex;
        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message.Trim());
            }

            current = current.InnerException;
        }

        return string.Join(" | ", messages.Distinct(StringComparer.Ordinal));
    }

    private async Task<int> AutoSyncLocalExtensionsAsync()
    {
        if (_cloudSyncClient == null)
        {
            return 0;
        }

        var pendingCommands = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension && (!x.ExistsInCloud || !x.InstalledForUser))
            .ToList();
        if (pendingCommands.Count == 0)
        {
            return 0;
        }

        var syncedCount = 0;
        foreach (var command in pendingCommands)
        {
            await SyncCommandToCloudAsync(command);
            command.MarkAsSynced(command.DeclaredVersion);
            syncedCount++;
        }

        return syncedCount;
    }

    private static List<CommandItem> CreateSeedCommands()
    {
        return
        [
            new("谷", "谷歌", "输入“谷歌 关键词”后直接搜索。", "搜索", "#FF3B82F6", null, ["谷歌", "google", "guge", "gg", "搜索", "网页搜索"], queryPrefixes: ["谷歌", "guge", "google", "gg"], queryTargetTemplate: "https://www.google.com/search?q={query}"),
            new("设", "设置", "打开设置面板。", "命令", "#FF4F46E5", "oqh://settings", ["设置", "preferences", "config"]),
            new("加", "添加扩展", "添加一个单文件 JSON 扩展。", "命令", "#FF16A34A", "oqh://add-extension", ["扩展", "json", "添加"]),
            new("编", "编辑扩展", "编辑当前选中的本地扩展。", "命令", "#FF0284C7", "oqh://edit-extension", ["扩展", "编辑", "manifest"]),
            new("删", "删除扩展", "删除当前选中的本地扩展。", "命令", "#FFDC2626", "oqh://delete-extension", ["扩展", "删除", "remove"]),
            new("库", "应用库", "打开本地扩展目录。", "目录", "#FFE66A32", HostAssets.ExtensionsPath, ["应用", "扩展", "library"]),
            new("市", "插件市场", "打开本地扩展市场说明页。", "文件", "#FF9FE870", HostAssets.MarketplacePath, ["市场", "商店", "publish"]),
            new("工", "工作区", "打开开发工作区目录。", "目录", "#FFFACC15", @"F:\Desktop\kaifa", ["工作区", "folder"]),
            new("Q", "Quicker 动作目录", "打开当前 Quicker 相关开发目录。", "目录", "#FFA78BFA", @"F:\Desktop\kaifa\applanuch", ["quicker", "action"]),
            new("文", "帮助文档", "打开本地帮助文档。", "文件", "#FFFB7185", HostAssets.DocsReadmePath, ["文档", "help"]),
            new("近", "最近运行", "打开最近执行记录。", "文件", "#FF34D399", HostAssets.RecentCommandsPath, ["最近", "history"]),
            new("志", "日志", "打开日志文件。", "文件", "#FF60A5FA", HostAssets.HostLogPath, ["日志", "diagnose"])
        ];
    }

    private async Task SyncCommandToCloudAsync(CommandItem command)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        var packageBytes = ExtensionPackageService.BuildPackage(command, command.DeclaredVersion);
        await _cloudSyncClient.UpsertExtensionAsync(command);
        await _cloudSyncClient.UploadExtensionArchiveAsync(command, packageBytes, command.DeclaredVersion);
        await _cloudSyncClient.UpsertUserExtensionAsync(command);
    }

    private void MergeCloudCommands(
        IReadOnlyList<CloudExtensionRecord> cloudExtensions,
        IReadOnlyList<UserExtensionRecord> userExtensions)
    {
        var userMap = userExtensions.ToDictionary(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase);

        foreach (var command in _allCommands)
        {
            command.ClearCloudData();
        }

        _allCommands.RemoveAll(x => x.Source == CommandSource.Cloud);
        var localById = _allCommands
            .Where(x => x.Source != CommandSource.Cloud)
            .ToDictionary(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase);

        foreach (var extension in cloudExtensions)
        {
            var extensionId = extension.ExtensionId;
            if (localById.TryGetValue(extensionId, out var existing))
            {
                existing.ApplyCloudData(
                    extension.DisplayName,
                    extension.LatestVersion,
                    true,
                    userMap.ContainsKey(extensionId),
                    extension.ArchiveKey);
                continue;
            }

            var cloudCommand = new CommandItem(
                glyph: "C",
                title: extension.DisplayName,
                subtitle: $"来自云端扩展库，扩展 ID：{extension.ExtensionId}",
                category: "云扩展",
                accentHex: "#FF4ADE80",
                openTarget: null,
                keywords: [extension.ExtensionId, extension.DisplayName, "cloud", "extension"],
                source: CommandSource.Cloud,
                extensionId: extension.ExtensionId);
            cloudCommand.ApplyCloudData(
                extension.DisplayName,
                extension.LatestVersion,
                true,
                userMap.ContainsKey(extensionId),
                extension.ArchiveKey);
            _allCommands.Add(cloudCommand);
        }

        ApplyFilter(SearchBox.Text);
        OnPropertyChanged(nameof(SyncSummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpsertLocalExtensionCommand(CommandItem command)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        _allCommands.Add(command);
        _localExtensionIndex[command.ExtensionId] = command;
        RefreshExtensionHotkeys();
    }

    private void RemoveLocalExtensionCommand(string extensionId)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        _localExtensionIndex.Remove(extensionId);
        RefreshExtensionHotkeys();
    }

    public void ReloadLocalExtensionsFromExternal()
    {
        _allCommands.RemoveAll(x => x.Source == CommandSource.LocalExtension);
        _localExtensionIndex.Clear();
        foreach (var command in LocalExtensionCatalog.LoadCommands())
        {
            UpsertLocalExtensionCommand(command);
        }

        ApplyFilter(SearchBox.Text);
        SyncStatus = "已通过外部 Agent API 刷新本地扩展。";
    }

    private CommandItem? ShowJsonExtensionEditorAsync(string initialJson, bool isEditMode)
    {
        var currentJson = initialJson;

        while (true)
        {
            var dialog = new AddJsonExtensionWindow(currentJson, isEditMode)
            {
                Owner = this
            };
            var result = dialog.ShowDialog();
            if (result != true)
            {
                return null;
            }

            try
            {
                var command = LocalExtensionCatalog.SaveJsonExtension(dialog.JsonContent);
                UpsertLocalExtensionCommand(command);
                ApplyFilter(SearchBox.Text);
                SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                CommandList.SelectedItem = SelectedCommand;
                return command;
            }
            catch (Exception ex)
            {
                currentJson = dialog.JsonContent;
                var retryDialog = new AddJsonExtensionWindow(currentJson, isEditMode)
                {
                    Owner = this
                };
                retryDialog.ShowError(ex.Message);
                if (retryDialog.ShowDialog() != true)
                {
                    return null;
                }

                currentJson = retryDialog.JsonContent;
            }
        }
    }

    private CommandItem ResolveRunnableCommand(CommandItem command)
    {
        if (command.Source != CommandSource.Cloud)
        {
            return command;
        }

        return _localExtensionIndex.TryGetValue(command.ExtensionId, out var localExtension)
            ? localExtension
            : command;
    }

    public CommandItem? OpenAddExtensionForSlot()
    {
        var templateJson = LocalExtensionCatalog.CreateTemplateJson();
        return ShowJsonExtensionEditorAsync(templateJson, false);
    }

    public void ShowPanel()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void PinAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        LastRunMessage = _isPinned ? "已固定窗口，失去焦点不会自动关闭。" : "已取消固定，失去焦点将自动关闭。";
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    public void TogglePanelVisibility()
    {
        if (IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowPanel();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source == null)
        {
            return;
        }

        _source.AddHook(WndProc);
        RefreshLauncherHotkeyRegistration();
        RefreshExtensionHotkeys();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            if (_source != null)
            {
                UnregisterExtensionHotkeys();
                UnregisterHotKey(_source.Handle, HotKeyId);
                _source.RemoveHook(WndProc);
            }

            return;
        }

        if (!AppSettingsStore.Load().CloseToTray)
        {
            AllowClose = true;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isPinned || !IsVisible)
        {
            return;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return;
        }

        if (FooterQuickMenuPopup.IsOpen || CommandList.ContextMenu?.IsOpen == true)
        {
            return;
        }

        HideToTray();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            TogglePanelVisibility();
            handled = true;
        }
        else if (msg == WmHotKey && _registeredExtensionHotkeys.TryGetValue(wParam.ToInt32(), out var command))
        {
            ExecuteCommandFromGlobalHotkey(command);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ExecuteCommandFromGlobalHotkey(CommandItem command)
    {
        var runnable = ResolveRunnableCommand(command);
        if (runnable.HasHostedView || string.Equals(runnable.HotkeyBehavior, "show-view", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel();
        }

        _ = ExecuteCommandAsync(runnable, string.Empty, "hotkey");
    }

    private void RefreshExtensionHotkeys()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterExtensionHotkeys();
        _nextExtensionHotkeyId = 0x5400;

        foreach (var command in _localExtensionIndex.Values
                     .Where(static x => !string.IsNullOrWhiteSpace(x.GlobalShortcut))
                     .OrderBy(static x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseHotkey(command.GlobalShortcut!, out var modifiers, out var key))
            {
                HostAssets.AppendLog($"Invalid global shortcut skipped: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            if (command.SupportsQueryArgument && !command.HasHostedView)
            {
                HostAssets.AppendLog($"Query shortcut skipped without hosted view: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            var id = _nextExtensionHotkeyId++;
            var success = RegisterHotKey(
                _source.Handle,
                id,
                modifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!success)
            {
                HostAssets.AppendLog($"Failed to register global shortcut: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            _registeredExtensionHotkeys[id] = command;
        }
    }

    private void UnregisterExtensionHotkeys()
    {
        if (_source == null)
        {
            _registeredExtensionHotkeys.Clear();
            return;
        }

        foreach (var hotkey in _registeredExtensionHotkeys.Keys.ToArray())
        {
            UnregisterHotKey(_source.Handle, hotkey);
        }

        _registeredExtensionHotkeys.Clear();
    }

    private static bool TryParseHotkey(string shortcut, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var segments = shortcut
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;
            if (!isLast)
            {
                switch (segment.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        continue;
                    case "alt":
                        modifiers |= ModAlt;
                        continue;
                    case "shift":
                        modifiers |= ModShift;
                        continue;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        continue;
                    default:
                        return false;
                }
            }

            try
            {
                key = segment.ToLowerInvariant() switch
                {
                    "space" => Key.Space,
                    "enter" => Key.Enter,
                    "tab" => Key.Tab,
                    "esc" or "escape" => Key.Escape,
                    _ => (Key)new KeyConverter().ConvertFromInvariantString(segment)!
                };
            }
            catch
            {
                return false;
            }
        }

        return modifiers != 0 && key != Key.None;
    }

    private bool RefreshLauncherHotkeyRegistration()
    {
        if (_source == null)
        {
            return false;
        }

        UnregisterHotKey(_source.Handle, HotKeyId);
        var shortcut = AppSettingsStore.Load().LauncherHotkey;
        if (!TryParseHotkey(shortcut, out var modifiers, out var key))
        {
            HostAssets.AppendLog($"Invalid launcher hotkey skipped: {shortcut}");
            return false;
        }

        var success = RegisterHotKey(
            _source.Handle,
            HotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!success)
        {
            HostAssets.AppendLog($"Failed to register launcher hotkey: {shortcut}");
        }

        return success;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public async Task<bool> PromptLoginFromSettingsAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置。";
            return false;
        }

        try
        {
            var ok = ShowLoginDialog();
            if (!ok)
            {
                return false;
            }

            await _cloudSyncClient.EnsureAuthenticatedAsync();
            OnPropertyChanged(nameof(SyncSummaryText));
            SyncStatus = "已登录，可进行云同步。";
            return true;
        }
        catch (Exception ex)
        {
            SyncStatus = $"登录失败：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    public async Task RefreshCloudFromSettingsAsync()
    {
        await RefreshCloudStateAsync();
    }

    public void SignOutFromSettings()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _cloudSyncClient.ClearCredential();
        SyncStatus = "已退出登录。";
        OnPropertyChanged(nameof(SyncSummaryText));
    }

    public void RefreshAppSettings()
    {
        var settings = AppSettingsStore.Load();
        RefreshLauncherHotkeyRegistration();
        SyncStatus = settings.LaunchAtStartup
            ? "设置已保存。开机启动已启用。"
            : settings.RefreshCloudOnStartup
                ? "设置已保存。"
                : "设置已保存。启动后自动刷新云状态已关闭。";
    }

    public string GetLauncherHotkey() => AppSettingsStore.Load().LauncherHotkey;

    public bool TryUpdateLauncherHotkey(string shortcut, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut) || !TryParseHotkey(shortcut, out _, out _))
        {
            message = "快捷键格式无效。示例：Ctrl+Shift+Space";
            return false;
        }

        var settings = AppSettingsStore.Load();
        var previous = settings.LauncherHotkey;
        settings.LauncherHotkey = shortcut.Trim();
        AppSettingsStore.Save(settings);

        if (!RefreshLauncherHotkeyRegistration())
        {
            settings.LauncherHotkey = previous;
            AppSettingsStore.Save(settings);
            RefreshLauncherHotkeyRegistration();
            message = "主程序快捷键注册失败，可能与系统或其他程序冲突。";
            return false;
        }

        message = $"主程序快捷键已更新为 {settings.LauncherHotkey}";
        return true;
    }

    public IReadOnlyList<CommandItem> GetLocalExtensionsForSettings()
    {
        return _localExtensionIndex.Values
            .OrderBy(static x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<(bool ok, string message)> UpdateExtensionShortcutFromSettingsAsync(string extensionId, string? shortcut)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(shortcut) &&
                !TryParseHotkey(shortcut, out _, out _))
            {
                return (false, "快捷键格式无效。示例：Ctrl+Alt+T");
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extensionId, shortcut);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);

            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(updated);
            }

            var message = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";
            return (true, message);
        }
        catch (Exception ex)
        {
            return (false, $"设置快捷键失败：{FormatExceptionMessage(ex)}");
        }
    }

    private void OpenCommandActionsMenu()
    {
        CommandList.Focus();
        if (!UpdateCommandContextMenuState() || CommandList.ContextMenu == null || !CommandList.ContextMenu.HasItems)
        {
            return;
        }

        CommandList.ContextMenu.PlacementTarget = CommandList;
        CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        CommandList.ContextMenu.IsOpen = true;
    }

    public void OpenSettingsWindow(string? sectionKey = null)
    {
        var settingsWindow = new SettingsWindow(this);
        if (sectionKey != null) settingsWindow.NavigateTo(sectionKey);
        settingsWindow.Show();
    }

    private async Task CreateDesktopShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可创建快捷方式的命令。";
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (string.IsNullOrWhiteSpace(command.OpenTarget) || IsInternalCommand(command))
        {
            SyncStatus = "当前命令不支持创建桌面快捷方式。";
            return;
        }

        try
        {
            var path = DesktopShortcutService.CreateShortcut(command.Title, command.OpenTarget);
            LastRunMessage = $"已创建桌面快捷方式：{path}";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SyncStatus = $"创建快捷方式失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task RenameSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可重命名的扩展。";
            return;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接重命名。";
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名扩展", "输入新的扩展名称。", extension.Title)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var renamed = LocalExtensionCatalog.RenameExtension(extension.ExtensionId, dialog.ValueText);
            UpsertLocalExtensionCommand(renamed);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(renamed.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已重命名扩展：{renamed.Title}";

            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(renamed);
                await RefreshCloudStateAsync();
            }
        }
        catch (Exception ex)
        {
            SyncStatus = $"重命名失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void AddToQuickPanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        
        if (sourceCommand == null) return;
        var command = ResolveRunnableCommand(sourceCommand);

        var settings = AppSettingsStore.Load();
        var slots = settings.QuickPanelSlots.ToList();
        
        // Find first empty slot
        var index = slots.FindIndex(string.IsNullOrEmpty);
        if (index >= 0)
        {
            slots[index] = command.ExtensionId;
            AppSettingsStore.Save(settings with { QuickPanelSlots = slots });
            LastRunMessage = $"已添加到快捷面板第 {index + 1} 个槽位：{command.Title}";
        }
        else
        {
            SyncStatus = "快捷面板已满（28 个槽位），请先在面板中移除旧扩展。";
        }
    }

    private async Task SetSelectedExtensionShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可设置快捷键的扩展。";
            return;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接设置快捷键。";
            return;
        }

        var dialog = new HotkeyCaptureWindow(
            "设置快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。需要清除时可点“清空”。",
            extension.GlobalShortcut ?? string.Empty,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(dialog.ShortcutText) &&
                !TryParseHotkey(dialog.ShortcutText, out _, out _))
            {
                SyncStatus = "快捷键格式无效。示例：Ctrl+Alt+T";
                return;
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extension.ExtensionId, dialog.ShortcutText);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(updated.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";

            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(updated);
            }
        }
        catch (Exception ex)
        {
            SyncStatus = $"设置快捷键失败：{FormatExceptionMessage(ex)}";
        }
    }

    private bool HandleInternalCommand(CommandItem command)
    {
        switch (command.OpenTarget)
        {
            case "oqh://settings":
                if (System.Windows.Application.Current is App app)
                {
                    app.OpenSettingsWindow();
                    LastRunMessage = "已打开设置。";
                }

                return true;
            case "oqh://add-extension":
                _ = AddJsonExtensionAsync();
                return true;
            case "oqh://edit-extension":
                _ = EditSelectedExtensionAsync();
                return true;
            case "oqh://delete-extension":
                _ = DeleteSelectedExtensionAsync();
                return true;
            default:
                return false;
        }
    }

    private static bool IsInternalCommand(CommandItem command)
    {
        return command.OpenTarget?.StartsWith("oqh://", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static CommandMatch BuildCommandMatch(CommandItem command, string query)
    {
        var argument = ExtractQueryArgument(command, query);
        if (command.SupportsQueryArgument && argument.Length > 0)
        {
            return new CommandMatch(true, 300);
        }

        if (command.SupportsQueryArgument && command.QueryPrefixes.Any(prefix =>
                prefix.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandMatch(true, 260);
        }

        if (command.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 220);
        }

        if (command.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 160);
        }

        if (command.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 120);
        }

        if (command.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandMatch(true, 140);
        }

        return new CommandMatch(false, 0);
    }

    private static string ExtractQueryArgument(CommandItem command, string rawInput)
    {
        if (!command.SupportsQueryArgument || string.IsNullOrWhiteSpace(rawInput))
        {
            return string.Empty;
        }

        var input = rawInput.Trim();
        foreach (var prefix in command.QueryPrefixes)
        {
            if (input.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (input.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                return input[(prefix.Length + 1)..].Trim();
            }

            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && input.Length > prefix.Length)
            {
                return input[prefix.Length..].Trim();
            }
        }

        return string.Empty;
    }

    private static string? BuildExecutionTarget(CommandItem command, string? rawInput)
    {
        if (command.SupportsQueryArgument)
        {
            var argument = ExtractQueryArgument(command, rawInput ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(argument))
            {
                return command.QueryTargetTemplate!.Replace("{query}", Uri.EscapeDataString(argument), StringComparison.Ordinal);
            }
        }

        return command.OpenTarget;
    }

    private static string BuildScriptInput(CommandItem command, string? rawInput)
    {
        if (command.SupportsQueryArgument)
        {
            var argument = ExtractQueryArgument(command, rawInput ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(argument))
            {
                return argument;
            }
        }

        return (rawInput ?? string.Empty).Trim();
    }

    private async Task ExecuteScriptCommandAsync(CommandItem runnable, string? input, string launchSource)
    {
        SyncStatus = $"正在执行脚本：{runnable.Title}";
        var result = await ScriptExtensionRunner.ExecuteAsync(runnable, input, launchSource);
        if (result.Success)
        {
            HostAssets.AppendRecent(runnable.Title);
            HostAssets.AppendLog($"Executed script extension: {runnable.Title} -> {runnable.EntryPoint}");
            var summary = string.IsNullOrWhiteSpace(result.Output)
                ? "脚本执行完成。"
                : result.Output.ReplaceLineEndings(" ").Trim();
            if (summary.Length > 180)
            {
                summary = summary[..180] + "...";
            }

            LastRunMessage = $"已执行脚本：{runnable.Title} -> {summary}";
            SyncStatus = "脚本执行完成。";
            ShowScriptResultDialog(runnable.Title, result.Output, false);
            return;
        }

        HostAssets.AppendLog($"Script extension failed: {runnable.Title} -> {result.Error}");
        LastRunMessage = $"脚本执行失败：{runnable.Title}";
        SyncStatus = $"脚本执行失败：{result.Error}";
        ShowScriptResultDialog(runnable.Title, result.Error, true);
    }

    private void ShowScriptResultDialog(string title, string content, bool isError)
    {
        var message = string.IsNullOrWhiteSpace(content)
            ? "脚本执行完成，但没有返回输出。"
            : content.Trim();
        System.Windows.MessageBox.Show(
            this,
            message,
            isError ? $"{title} 执行失败" : $"{title} 返回结果",
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);
    }

    // --- Quick Panel Support ---

    public List<CommandItem> GetAllCommands() => _allCommands.ToList();

    public void ExecuteCommandExternally(CommandItem command)
    {
        _ = ExecuteCommandAsync(ResolveRunnableCommand(command), string.Empty, "quick-panel");
    }
}

public readonly record struct CommandMatch(bool IsMatch, int Priority);

public sealed record HostedPluginViewDefinition(
    string Type,
    string? Title,
    string? Description,
    string? InputLabel,
    string? InputPlaceholder,
    string? OutputLabel,
    string? ActionButtonText,
    string? ActionType,
    string? OutputTemplate,
    string? EmptyState);

public sealed record HostedPluginSession(CommandItem Command, HostedPluginViewDefinition Definition);

public sealed class CommandItem : INotifyPropertyChanged
{
    public CommandItem(
        string glyph,
        string title,
        string subtitle,
        string category,
        string accentHex,
        string? openTarget,
        IEnumerable<string> keywords,
        CommandSource source = CommandSource.Local,
        string? extensionId = null,
        string? declaredVersion = null,
        string? extensionDirectoryPath = null,
        IEnumerable<string>? queryPrefixes = null,
        string? queryTargetTemplate = null,
        HostedPluginViewDefinition? hostedView = null,
        string? globalShortcut = null,
        string? hotkeyBehavior = null,
        string? runtime = null,
        string? entryPoint = null,
        IEnumerable<string>? permissions = null,
        string? entryMode = null,
        string? inlineScriptSource = null)
    {
        Glyph = glyph;
        Title = title;
        Subtitle = subtitle;
        Category = category;
        OpenTarget = openTarget;
        Keywords = keywords.ToArray();
        AccentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(accentHex)!;
        Source = source;
        ExtensionId = string.IsNullOrWhiteSpace(extensionId)
            ? CloudSyncClient.CreateExtensionId(this)
            : extensionId;
        DeclaredVersion = string.IsNullOrWhiteSpace(declaredVersion) ? "0.1.0" : declaredVersion;
        ExtensionDirectoryPath = extensionDirectoryPath;
        QueryPrefixes = queryPrefixes?.ToArray() ?? [];
        QueryTargetTemplate = queryTargetTemplate;
        HostedView = hostedView;
        GlobalShortcut = globalShortcut;
        HotkeyBehavior = hotkeyBehavior;
        Runtime = runtime;
        EntryPoint = entryPoint;
        Permissions = permissions?.ToArray() ?? [];
        EntryMode = entryMode;
        InlineScriptSource = inlineScriptSource;
    }

    public string Glyph { get; }

    public ImageSource? IconSource
    {
        get
        {
            if (string.IsNullOrEmpty(Glyph)) return null;
            if (Glyph.StartsWith("http") || Glyph.Contains(":\\") || Glyph.StartsWith("/"))
            {
                try
                {
                    return new BitmapImage(new Uri(Glyph, Glyph.StartsWith("http") ? UriKind.Absolute : UriKind.RelativeOrAbsolute));
                }
                catch { }
            }
            return null;
        }
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Category { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string? OpenTarget { get; }

    public IReadOnlyList<string> Keywords { get; }

    public CommandSource Source { get; }

    public string ExtensionId { get; }

    public string DeclaredVersion { get; }

    public string? ExtensionDirectoryPath { get; }

    public IReadOnlyList<string> QueryPrefixes { get; }

    public string? QueryTargetTemplate { get; }

    public HostedPluginViewDefinition? HostedView { get; }

    public string? GlobalShortcut { get; }

    public string? HotkeyBehavior { get; }

    public string? Runtime { get; }

    public string? EntryPoint { get; }

    public IReadOnlyList<string> Permissions { get; }

    public string? EntryMode { get; }

    public string? InlineScriptSource { get; }

    public bool SupportsQueryArgument => QueryPrefixes.Count > 0 && !string.IsNullOrWhiteSpace(QueryTargetTemplate);

    public bool HasHostedView => HostedView != null;

    public bool HasScriptEntry => !string.IsNullOrWhiteSpace(Runtime) && !string.IsNullOrWhiteSpace(EntryPoint);

    public bool HasGlobalShortcut => !string.IsNullOrWhiteSpace(GlobalShortcut);

    public string ShortcutLabel => GlobalShortcut ?? string.Empty;

    public string? CloudVersion { get; private set; }

    public bool ExistsInCloud { get; private set; }

    public bool InstalledForUser { get; private set; }

    public bool HasArchive { get; private set; }

    public string? LocalPackagePath { get; private set; }

    public string VersionLabel => string.IsNullOrWhiteSpace(CloudVersion) ? SourceLabel : $"v{CloudVersion}";

    public string ItemKindLabel => Source == CommandSource.Cloud
        ? "云端"
        : HasHostedView
            ? "插件界面"
            : HasScriptEntry
                ? "脚本"
                : Category;

    public string CloudSummary =>
        ExistsInCloud
            ? InstalledForUser
                ? $"云端已收录，并已挂到当前用户。{ArchiveSummary} 来源：{SourceLabel}。"
                : $"云端已收录，但当前用户还没安装。{ArchiveSummary} 来源：{SourceLabel}。"
            : $"当前仅存在于本地。来源：{SourceLabel}。";

    private string SourceLabel => Source switch
    {
        CommandSource.Cloud => "云端",
        CommandSource.LocalExtension => "本地扩展",
        _ => "本地"
    };
    private string ArchiveSummary => HasArchive ? "已包含扩展包。" : "当前还没有扩展包。";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyCloudData(string? displayName, string? version, bool existsInCloud, bool installedForUser, string? archiveKey)
    {
        CloudVersion = version;
        ExistsInCloud = existsInCloud;
        InstalledForUser = installedForUser;
        HasArchive = !string.IsNullOrWhiteSpace(archiveKey);
        NotifyCloudChanged();
    }

    public void ClearCloudData()
    {
        CloudVersion = null;
        ExistsInCloud = false;
        InstalledForUser = false;
        HasArchive = false;
        NotifyCloudChanged();
    }

    public void MarkAsSynced(string? version)
    {
        CloudVersion = version ?? "0.1.0";
        ExistsInCloud = true;
        InstalledForUser = true;
        HasArchive = true;
        NotifyCloudChanged();
    }

    public void SetLocalPackagePath(string path)
    {
        LocalPackagePath = $"本地包：{path}";
        NotifyCloudChanged();
    }

    private void NotifyCloudChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CloudVersion)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExistsInCloud)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstalledForUser)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasArchive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CloudSummary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalPackagePath)));
    }
}

public enum CommandSource
{
    Local,
    LocalExtension,
    Cloud
}
