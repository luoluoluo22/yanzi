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
using System.Text.Json.Serialization;
using System.IO;
using System.Windows.Threading;
using System.Windows.Markup;
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
    private const string CloudWebDavConfigId = "yanzi-webdav-settings";
    private const string SearchScopeAll = "all";
    private const string SearchScopeWeb = "web";

    private readonly List<CommandItem> _allCommands;
    private readonly CloudSyncClient? _cloudSyncClient;
    private readonly SyncOptions _syncOptions;
    private readonly SearchUsageMemory _searchUsageMemory;
    private AppSettings _appSettings;
    private readonly Dictionary<string, CommandItem> _localExtensionIndex;
    private readonly Dictionary<int, CommandItem> _registeredExtensionHotkeys = new();
    private CommandItem? _selectedCommand;
    private CommandItem? _lastActionableCommand;
    private HostedPluginSession? _activeHostedView;
    private string _activeQueryArgument = string.Empty;
    private string _hostedViewInput = string.Empty;
    private string _hostedViewOutput = string.Empty;
    private string _hostedViewStatus = "准备就绪。";
    private object? _hostedViewDynamicContent;
    private string _lastRunMessage = "准备就绪。输入关键字后按 Enter 运行。";
    private string _syncStatus = "云同步未初始化。";
    private HwndSource? _source;
    private bool _authPromptActive;
    private bool _isPinned;
    private int _nextExtensionHotkeyId = 0x5400;
    private QuickPanelWindow? _quickPanel;
    private readonly DispatcherTimer _backgroundWebDavSyncTimer;
    private bool _backgroundWebDavSyncRunning;
    private bool _backgroundWebDavSyncRequested;
    private SearchScopeTab? _selectedSearchScope;
    private bool _listenerServicesPaused;
    private readonly double _defaultWindowWidth;
    private readonly double _defaultWindowHeight;
    private readonly double _defaultMinWindowWidth;
    private readonly double _defaultMinWindowHeight;
    private readonly Dictionary<string, List<Action<string>>> _hostedViewStateBindings = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Controls.Control? _hostedViewPreferredFocusControl;
    private Window? _hostedViewEditorWindowToRestore;

    public MainWindow()
    {
        InitializeComponent();
        _defaultWindowWidth = Width;
        _defaultWindowHeight = Height;
        _defaultMinWindowWidth = MinWidth;
        _defaultMinWindowHeight = MinHeight;
        ApplyWindowIcon();
        HostAssets.EnsureCreated();
        _syncOptions = SyncConfigLoader.Load();
        _appSettings = AppSettingsStore.Load();
        _searchUsageMemory = SearchUsageMemory.Load();
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
        SearchScopes = new ObservableCollection<SearchScopeTab>(CreateSearchScopes());
        _selectedSearchScope = SearchScopes.First();
        SelectedCommand = FilteredCommands.FirstOrDefault();
        DataContext = this;
        ApplyFilter(string.Empty);
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;

        Closing += (s, e) => InputHookService.Stop();

        _quickPanel = new QuickPanelWindow(this);
        _backgroundWebDavSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(6)
        };
        _backgroundWebDavSyncTimer.Tick += (_, _) => QueueBackgroundWebDavSync("timer");
    }

    private void ApplyWindowIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/yanzi.ico", UriKind.Absolute));
        }
        catch
        {
            // Ignore icon failures so the launcher can still start.
        }
    }

    public ObservableCollection<CommandItem> FilteredCommands { get; }

    public ObservableCollection<SearchScopeTab> SearchScopes { get; }

    public SearchScopeTab? SelectedSearchScope
    {
        get => _selectedSearchScope;
        set
        {
            if (Equals(value, _selectedSearchScope))
            {
                return;
            }

            if (_selectedSearchScope != null)
            {
                _selectedSearchScope.IsSelected = false;
            }

            _selectedSearchScope = value;
            if (_selectedSearchScope != null)
            {
                _selectedSearchScope.IsSelected = true;
            }

            OnPropertyChanged();
            ApplyFilter(SearchBox.Text);
        }
    }

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

    public bool IsHostedViewDynamic => _activeHostedView?.Definition.UsesDynamicLayout == true;

    public Visibility HostedViewLegacyVisibility => IsHostedViewDynamic ? Visibility.Collapsed : Visibility.Visible;

    public Visibility HostedViewDynamicVisibility => IsHostedViewDynamic ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HostedViewFooterActionVisibility => IsHostedViewDynamic ? Visibility.Collapsed : Visibility.Visible;

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

    public object? HostedViewDynamicContent
    {
        get => _hostedViewDynamicContent;
        set
        {
            if (ReferenceEquals(value, _hostedViewDynamicContent))
            {
                return;
            }

            _hostedViewDynamicContent = value;
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
        SetSearchScopePopupOpen(true);
        StartBackgroundWebDavSync();

        if (_cloudSyncClient == null)
        {
            StartMousePanelService();
            QueueBackgroundWebDavSync("startup");
            return;
        }

        StartMousePanelService();
        if (!AppSettingsStore.Load().RefreshCloudOnStartup)
        {
            QueueBackgroundWebDavSync("startup");
            return;
        }

        await RefreshCloudStateAsync(allowLoginPrompt: false);
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
        else if (e.Key == Key.Tab)
        {
            CycleSearchScope(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
        }
    }

    private void SearchScopeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchScopeTab scope })
        {
            SelectedSearchScope = scope;
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    private void SearchScopeAddButton_Click(object sender, RoutedEventArgs e)
    {
        var command = ShowJsonExtensionEditorAsync(CreateWebSearchTemplateJson(), false);
        if (command != null)
        {
            LastRunMessage = $"已添加网页搜索扩展：{command.Title}";
            QueueBackgroundWebDavSync("web-search-extension-add");
        }

        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
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
        var parsed = ParseSearchQuery(query);
        UpdateSearchScopeCounts(parsed);
        _activeQueryArgument = string.Empty;
        var sourceCommands = parsed.IsEmpty
            ? _allCommands
                .Where(command => IsExtensionEnabled(command.ExtensionId))
                .Where(command => SearchScopeAllows(command, parsed.ScopeKey))
            : _allCommands
                .Where(command => IsExtensionEnabled(command.ExtensionId))
                .Where(command => SearchScopeAllows(command, parsed.ScopeKey))
                .Select(command => new
                {
                    Command = command,
                    Match = BuildCommandMatch(command, parsed.Term, AllowsRawQueryArgument(parsed.ScopeKey))
                })
                .Where(x => x.Match.IsMatch)
                .Select(x => x.Command);

        var matches = sourceCommands
            .DistinctBy(command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .Select(command => new
            {
                Command = command,
                Score = ScoreSearchResult(command, parsed.Term)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Command)
            .ToList();

        var leadingCommand = matches.FirstOrDefault(static command => command.SupportsQueryArgument);
        if (leadingCommand != null && !string.IsNullOrWhiteSpace(parsed.Term))
        {
            _activeQueryArgument = parsed.Term;
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

    private void CycleSearchScope(int delta)
    {
        if (SearchScopes.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedSearchScope == null ? 0 : SearchScopes.IndexOf(SelectedSearchScope);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + delta) % SearchScopes.Count;
        if (nextIndex < 0)
        {
            nextIndex = SearchScopes.Count - 1;
        }

        SelectedSearchScope = SearchScopes[nextIndex];
    }

    private static List<SearchScopeTab> CreateSearchScopes()
    {
        return
        [
            new(SearchScopeAll, "全部", "所有结果", true),
            new(SearchScopeWeb, "网页", "百度 / Bing / 谷歌"),
            new("baidu", "百度", "只显示百度搜索"),
            new("bing", "Bing", "只显示 Bing 搜索"),
            new("google", "谷歌", "只显示 Google 搜索"),
            new("extension", "扩展", "本地扩展"),
            new("command", "命令", "系统命令")
        ];
    }

    private SearchQueryState ParseSearchQuery(string? query)
    {
        var raw = (query ?? string.Empty).Trim();
        var scope = SelectedSearchScope?.Key ?? SearchScopeAll;
        if (!raw.StartsWith('@') && !raw.StartsWith('＠'))
        {
            return new SearchQueryState(scope, raw, string.IsNullOrWhiteSpace(raw));
        }

        var withoutAt = raw[1..].TrimStart();
        if (string.IsNullOrWhiteSpace(withoutAt))
        {
            return new SearchQueryState(scope, string.Empty, true);
        }

        var separator = withoutAt.IndexOf(' ');
        var token = separator < 0 ? withoutAt : withoutAt[..separator];
        var term = separator < 0 ? string.Empty : withoutAt[(separator + 1)..].Trim();
        return TryResolveSearchScopeAlias(token, out var parsedScope)
            ? new SearchQueryState(parsedScope, term, string.IsNullOrWhiteSpace(term))
            : new SearchQueryState(scope, raw, false);
    }

    private static bool TryResolveSearchScopeAlias(string token, out string scope)
    {
        scope = token.Trim().ToLowerInvariant() switch
        {
            "all" or "全部" => SearchScopeAll,
            "web" or "网页" or "搜索" => SearchScopeWeb,
            "baidu" or "bd" or "百度" => "baidu",
            "bing" or "必应" => "bing",
            "google" or "gg" or "谷歌" or "guge" => "google",
            "extension" or "ext" or "扩展" or "插件" => "extension",
            "command" or "cmd" or "命令" or "动作" => "command",
            _ => string.Empty
        };
        return scope.Length > 0;
    }

    private static bool SearchScopeAllows(CommandItem command, string scope)
    {
        return scope switch
        {
            SearchScopeAll => true,
            SearchScopeWeb => command.Source == CommandSource.LocalExtension &&
                              command.Category.Contains("网页搜索", StringComparison.OrdinalIgnoreCase),
            "baidu" => command.ExtensionId.Contains("baidu", StringComparison.OrdinalIgnoreCase) ||
                       command.Keywords.Any(keyword => keyword.Contains("baidu", StringComparison.OrdinalIgnoreCase) || keyword.Contains("百度", StringComparison.OrdinalIgnoreCase)),
            "bing" => command.ExtensionId.Contains("bing", StringComparison.OrdinalIgnoreCase) ||
                      command.Keywords.Any(keyword => keyword.Contains("bing", StringComparison.OrdinalIgnoreCase) || keyword.Contains("必应", StringComparison.OrdinalIgnoreCase)),
            "google" => command.ExtensionId.Contains("google", StringComparison.OrdinalIgnoreCase) ||
                        command.Keywords.Any(keyword => keyword.Contains("google", StringComparison.OrdinalIgnoreCase) || keyword.Contains("谷歌", StringComparison.OrdinalIgnoreCase)),
            "extension" => command.Source == CommandSource.LocalExtension,
            "command" => command.Source != CommandSource.LocalExtension,
            _ => true
        };
    }

    private int ScoreSearchResult(CommandItem command, string query)
    {
        var score = string.IsNullOrWhiteSpace(query)
            ? 0
            : BuildCommandMatch(command, query).Priority;
        score += command.Source == CommandSource.WebSearch ? 80 : 0;
        score += _searchUsageMemory.Score(command.ExtensionId);
        return score;
    }

    private void UpdateSearchScopeCounts(SearchQueryState parsed)
    {
        foreach (var scope in SearchScopes)
        {
            var scopedQuery = parsed with { ScopeKey = scope.Key };
            scope.Count = CountScopeResults(scopedQuery);
        }
    }

    private int CountScopeResults(SearchQueryState query)
    {
        var commandCount = query.IsEmpty
            ? 0
            : _allCommands.Count(command =>
                IsExtensionEnabled(command.ExtensionId) &&
                SearchScopeAllows(command, query.ScopeKey) &&
                BuildCommandMatch(command, query.Term, AllowsRawQueryArgument(query.ScopeKey)).IsMatch);
        return commandCount;
    }

    private static bool AllowsRawQueryArgument(string scopeKey) =>
        scopeKey is SearchScopeWeb or "baidu" or "bing" or "google";

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

        var runnable = ResolveRunnableCommand(SelectedCommand);
        var explicitInput = runnable.SupportsQueryArgument && !string.IsNullOrWhiteSpace(_activeQueryArgument)
            ? _activeQueryArgument
            : null;
        await ExecuteCommandAsync(runnable, explicitInput);
    }

    private async Task ExecuteCommandAsync(CommandItem runnable, string? explicitInput = null, string launchSource = "launcher")
    {
        var hasExternalInput = !string.IsNullOrWhiteSpace(explicitInput);
        if (runnable.HostedView != null)
        {
            RecordCommandUsage(runnable);
            if (!string.Equals(launchSource, "launcher", StringComparison.OrdinalIgnoreCase))
            {
                ShowPanel();
            }

            OpenHostedView(runnable, explicitInput);
            if (hasExternalInput && UsesScriptHostedView(runnable.HostedView))
            {
                await RefreshHostedViewOutputAsync();
            }

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

        var executionTarget = BuildExecutionTarget(runnable, explicitInput ?? SearchBox.Text, allowRawQuery: hasExternalInput);
        if (executionTarget is { Length: > 0 })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executionTarget,
                    UseShellExecute = true
                });
                RecordCommandUsage(runnable);
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

        if (runnable.SupportsQueryArgument && !string.Equals(launchSource, "launcher", StringComparison.OrdinalIgnoreCase))
        {
            OpenQueryCommandInLauncher(runnable);
            return;
        }

        HostAssets.AppendLog($"Command has no executable target: {runnable.Title}");
        LastRunMessage = runnable.Source == CommandSource.Cloud
            ? $"云端记录已存在，但当前机器没有安装对应扩展：{runnable.ExtensionId}。先下载扩展包或放入本地扩展目录。"
            : $"当前命令没有 openTarget，也没有脚本入口：{runnable.Title}";
    }

    private void OpenQueryCommandInLauncher(CommandItem command)
    {
        ShowPanel();
        var prefix = command.QueryPrefixes.FirstOrDefault() ?? command.Title;
        SearchBox.Text = $"{prefix} ";
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        LastRunMessage = $"已打开搜索输入：{command.Title}";
        HostAssets.AppendLog($"Opened query command in launcher: {command.Title}");
    }

    private void OpenHostedView(CommandItem command, string? initialInput = null)
    {
        if (command.HostedView == null)
        {
            return;
        }

        _activeHostedView = new HostedPluginSession(command, command.HostedView);
        ApplyHostedViewWindowMetrics(command.HostedView);
        HostedViewInput = (initialInput ?? string.Empty).Trim();
        InitializeHostedViewState(initialInput);
        HostedViewDynamicContent = command.HostedView.UsesDynamicLayout
            ? BuildHostedViewDynamicContent(_activeHostedView)
            : null;
        HostedViewOutput = command.HostedView.EmptyState ?? "等待插件输出。";
        HostedViewStatus = string.IsNullOrWhiteSpace(HostedViewInput)
            ? $"已进入 {command.Title}。输入内容后可直接在当前窗口完成操作。"
            : $"已进入 {command.Title}，并填入外部选中内容。";
        OnPropertyChanged(nameof(IsHostedViewOpen));
        OnPropertyChanged(nameof(IsHostedViewDynamic));
        OnPropertyChanged(nameof(HostedViewLegacyVisibility));
        OnPropertyChanged(nameof(HostedViewDynamicVisibility));
        OnPropertyChanged(nameof(HostedViewFooterActionVisibility));
        OnPropertyChanged(nameof(HostedViewTitle));
        OnPropertyChanged(nameof(HostedViewSubtitle));
        OnPropertyChanged(nameof(HostedViewCommandLabel));
        OnPropertyChanged(nameof(HostedViewInputLabel));
        OnPropertyChanged(nameof(HostedViewOutputLabel));
        OnPropertyChanged(nameof(HostedViewInputPlaceholder));
        OnPropertyChanged(nameof(HostedViewActionButtonText));
        LastRunMessage = $"已打开插件视图：{command.Title}";
        Dispatcher.BeginInvoke(() =>
        {
            if (_hostedViewPreferredFocusControl != null)
            {
                _hostedViewPreferredFocusControl.Focus();
                return;
            }

            HostedViewInputBox.Focus();
        });
    }

    private void CloseHostedView()
    {
        if (_activeHostedView == null)
        {
            return;
        }

        var title = _activeHostedView.Command.Title;
        _activeHostedView = null;
        _hostedViewStateBindings.Clear();
        _hostedViewPreferredFocusControl = null;
        HostedViewInput = string.Empty;
        HostedViewOutput = string.Empty;
        HostedViewDynamicContent = null;
        HostedViewStatus = "已关闭插件视图。";
        OnPropertyChanged(nameof(IsHostedViewOpen));
        OnPropertyChanged(nameof(IsHostedViewDynamic));
        OnPropertyChanged(nameof(HostedViewLegacyVisibility));
        OnPropertyChanged(nameof(HostedViewDynamicVisibility));
        OnPropertyChanged(nameof(HostedViewFooterActionVisibility));
        OnPropertyChanged(nameof(HostedViewTitle));
        OnPropertyChanged(nameof(HostedViewSubtitle));
        OnPropertyChanged(nameof(HostedViewCommandLabel));
        OnPropertyChanged(nameof(HostedViewInputLabel));
        OnPropertyChanged(nameof(HostedViewOutputLabel));
        OnPropertyChanged(nameof(HostedViewInputPlaceholder));
        OnPropertyChanged(nameof(HostedViewActionButtonText));
        RestoreHostedViewWindowMetrics();
        LastRunMessage = $"已返回命令列表：{title}";
        if (_hostedViewEditorWindowToRestore != null)
        {
            var editorWindow = _hostedViewEditorWindowToRestore;
            _hostedViewEditorWindowToRestore = null;
            editorWindow.Show();
            editorWindow.Activate();
        }
        SearchBox.Focus();
    }

    public async Task PreviewHostedViewForTestAsync(
        CommandItem command,
        string initialInput = "测试输入",
        Window? editorWindowToRestore = null)
    {
        if (command.HostedView == null)
        {
            return;
        }

        _hostedViewEditorWindowToRestore = editorWindowToRestore;
        ShowPanel();
        Activate();
        OpenHostedView(command, initialInput);
        if (command.HostedView.UsesDynamicLayout)
        {
            return;
        }

        if (UsesScriptHostedView(command.HostedView))
        {
            await RefreshHostedViewOutputAsync();
        }
        else if (!string.IsNullOrWhiteSpace(initialInput))
        {
            RefreshHostedViewOutput();
        }
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

    private void InitializeHostedViewState(string? initialInput)
    {
        if (_activeHostedView == null)
        {
            return;
        }

        _activeHostedView.State.Clear();
        _hostedViewStateBindings.Clear();
        _hostedViewPreferredFocusControl = null;

        foreach (var pair in _activeHostedView.Definition.InitialState)
        {
            _activeHostedView.State[pair.Key] = pair.Value ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(initialInput))
        {
            return;
        }

        if (_activeHostedView.State.ContainsKey("input"))
        {
            _activeHostedView.State["input"] = initialInput.Trim();
            return;
        }

        var firstBoundTextarea = _activeHostedView.Definition.Components
            .FirstOrDefault(component =>
                string.Equals(component.Type, "textarea", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(component.Bind));
        if (firstBoundTextarea != null && !string.IsNullOrWhiteSpace(firstBoundTextarea.Bind))
        {
            _activeHostedView.State[firstBoundTextarea.Bind] = initialInput.Trim();
        }
    }

    private UIElement BuildHostedViewDynamicContent(HostedPluginSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.Definition.XamlTemplate))
        {
            return BuildHostedViewXamlContent(session);
        }

        return ResolveHostedViewLayout(session.Definition) switch
        {
            "split-horizontal" => BuildSplitHorizontalHostedView(session),
            _ => BuildSinglePaneHostedView(session)
        };
    }

    private UIElement BuildHostedViewXamlContent(HostedPluginSession session)
    {
        try
        {
            var parserContext = CreateHostedViewXamlParserContext();
            var xaml = NormalizeHostedViewXaml(session.Definition.XamlTemplate!);
            var root = XamlReader.Parse(xaml, parserContext) switch
            {
                Window window => ExtractHostedViewWindowContent(window),
                System.Windows.Controls.UserControl userControl => ExtractHostedViewUserControlContent(userControl),
                FrameworkElement element => element,
                _ => null
            };

            if (root == null)
            {
                return BuildHostedViewXamlError("XAML 根元素必须是 Window、UserControl 或 FrameworkElement。");
            }

            root.DataContext = session.BindingContext;
            AttachHostedViewActions(root, session);
            return root;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"HostedViewXaml parse failed: {ex}");
            return BuildHostedViewXamlError(ex.Message);
        }
    }

    private static ParserContext CreateHostedViewXamlParserContext()
    {
        var assemblyName = typeof(HostedViewBridge).Assembly.GetName().Name ?? "Yanzi";
        var parserContext = new ParserContext
        {
            XamlTypeMapper = new XamlTypeMapper(Array.Empty<string>())
        };
        parserContext.XamlTypeMapper.AddMappingProcessingInstruction("oqh", "OpenQuickHost", assemblyName);
        parserContext.XmlnsDictionary.Add(string.Empty, "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        parserContext.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        parserContext.XmlnsDictionary.Add("oqh", $"clr-namespace:OpenQuickHost;assembly={assemblyName}");
        return parserContext;
    }

    private static string NormalizeHostedViewXaml(string xaml)
    {
        var assemblyName = typeof(HostedViewBridge).Assembly.GetName().Name ?? "Yanzi";
        const string plainNamespace = "xmlns:oqh=\"clr-namespace:OpenQuickHost\"";
        var qualifiedNamespace = $"xmlns:oqh=\"clr-namespace:OpenQuickHost;assembly={assemblyName}\"";
        var normalized = xaml;

        normalized = normalized.Replace(
            "xmlns=\"[http://schemas.microsoft.com/winfx/2006/xaml/presentation](http://schemas.microsoft.com/winfx/2006/xaml/presentation)\"",
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
            StringComparison.Ordinal);
        normalized = normalized.Replace(
            "xmlns:x=\"[http://schemas.microsoft.com/winfx/2006/xaml](http://schemas.microsoft.com/winfx/2006/xaml)\"",
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"",
            StringComparison.Ordinal);

        return normalized.Contains(plainNamespace, StringComparison.Ordinal)
            ? normalized.Replace(plainNamespace, qualifiedNamespace, StringComparison.Ordinal)
            : normalized;
    }

    private FrameworkElement ExtractHostedViewWindowContent(Window window)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("Window 类型的 XAML 必须包含可视内容。");
        }

        if (!double.IsNaN(window.Width) && window.Width > 0)
        {
            Width = Math.Max(window.Width, MinWidth);
        }

        if (!double.IsNaN(window.Height) && window.Height > 0)
        {
            Height = Math.Max(window.Height, MinHeight);
        }

        if (!double.IsNaN(window.MinWidth) && window.MinWidth > 0)
        {
            MinWidth = Math.Max(window.MinWidth, 320);
        }

        if (!double.IsNaN(window.MinHeight) && window.MinHeight > 0)
        {
            MinHeight = Math.Max(window.MinHeight, 240);
        }

        window.Content = null;
        if (window.Resources.Count > 0)
        {
            content.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                MergedDictionaries = { window.Resources }
            });
        }

        return content;
    }

    private FrameworkElement ExtractHostedViewUserControlContent(System.Windows.Controls.UserControl userControl)
    {
        if (userControl.Content is not FrameworkElement content)
        {
            return userControl;
        }

        userControl.Content = null;
        return content;
    }

    private FrameworkElement BuildHostedViewXamlError(string errorMessage)
    {
        return new Border
        {
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#331F2937")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFDC2626")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new TextBlock
            {
                Text = $"自定义 XAML 视图加载失败：{errorMessage}",
                Foreground = System.Windows.Media.Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            }
        };
    }

    private void AttachHostedViewActions(DependencyObject root, HostedPluginSession session)
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(root))
        {
            var actionText = HostedViewBridge.GetAction(button);
            if (string.IsNullOrWhiteSpace(actionText))
            {
                continue;
            }

            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await ExecuteHostedViewActionsAsync(session, ParseHostedViewActionString(actionText));
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
        }

        var preferredFocusName = HostedViewBridge.GetPreferredFocus(root as DependencyObject);
        if (!string.IsNullOrWhiteSpace(preferredFocusName) && root is FrameworkElement frameworkRoot)
        {
            if (frameworkRoot.FindName(preferredFocusName) is System.Windows.Controls.Control control)
            {
                _hostedViewPreferredFocusControl = control;
            }
        }

        var loadedActionText = HostedViewBridge.GetLoadedAction(root);
        if (!string.IsNullOrWhiteSpace(loadedActionText) && root is FrameworkElement loadedRoot)
        {
            RoutedEventHandler? loadedHandler = null;
            loadedHandler = async (_, _) =>
            {
                loadedRoot.Loaded -= loadedHandler;
                await ExecuteHostedViewActionsAsync(session, ParseHostedViewActionString(loadedActionText));
            };
            loadedRoot.Loaded += loadedHandler;
        }
    }

    private UIElement BuildSinglePaneHostedView(HostedPluginSession session)
    {
        var panel = new StackPanel();
        foreach (var component in session.Definition.Components)
        {
            panel.Children.Add(BuildHostedViewComponent(component, session));
        }

        return WrapHostedViewRegion(panel);
    }

    private UIElement BuildSplitHorizontalHostedView(HostedPluginSession session)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        var right = new StackPanel();
        foreach (var component in session.Definition.Components)
        {
            var region = string.IsNullOrWhiteSpace(component.Region) ? "left" : component.Region.Trim().ToLowerInvariant();
            var target = region == "right" ? right : left;
            target.Children.Add(BuildHostedViewComponent(component, session));
        }

        var leftBorder = WrapHostedViewRegion(left);
        var rightBorder = WrapHostedViewRegion(right);
        Grid.SetColumn(leftBorder, 0);
        Grid.SetColumn(rightBorder, 2);
        grid.Children.Add(leftBorder);
        grid.Children.Add(rightBorder);
        return grid;
    }

    private Border WrapHostedViewRegion(System.Windows.Controls.Panel content)
    {
        return new Border
        {
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF202020")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2E2E2E")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private FrameworkElement BuildHostedViewComponent(HostedViewComponentDefinition component, HostedPluginSession session)
    {
        var wrapper = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        if (!string.IsNullOrWhiteSpace(component.Label))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = component.Label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        FrameworkElement content = component.Type.Trim().ToLowerInvariant() switch
        {
            "text" => BuildHostedViewTextComponent(component, session, markdown: false),
            "markdown" => BuildHostedViewTextComponent(component, session, markdown: true),
            "textarea" => BuildHostedViewTextareaComponent(component, session),
            "button" => BuildHostedViewButtonComponent(component, session),
            _ => BuildHostedViewUnsupportedComponent(component)
        };

        wrapper.Children.Add(content);
        return wrapper;
    }

    private FrameworkElement BuildHostedViewTextComponent(HostedViewComponentDefinition component, HostedPluginSession session, bool markdown)
    {
        var textBlock = new TextBlock
        {
            Foreground = markdown
                ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFE5E5E5")!
                : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFD7D7D7")!,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };

        if (!string.IsNullOrWhiteSpace(component.Bind))
        {
            RegisterHostedViewStateBinding(component.Bind, value => textBlock.Text = value);
        }
        else
        {
            textBlock.Text = component.Text ?? string.Empty;
        }

        return new Border
        {
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF171717")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2E2E2E")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = textBlock
        };
    }

    private FrameworkElement BuildHostedViewTextareaComponent(HostedViewComponentDefinition component, HostedPluginSession session)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap,
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF171717")!,
            BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2E2E2E")!,
            BorderThickness = new Thickness(1),
            Foreground = System.Windows.Media.Brushes.White,
            CaretBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!,
            Padding = new Thickness(12),
            FontSize = 14,
            MinHeight = 180
        };

        if (_hostedViewPreferredFocusControl == null)
        {
            _hostedViewPreferredFocusControl = textBox;
        }

        if (!string.IsNullOrWhiteSpace(component.Bind))
        {
            var path = component.Bind;
            RegisterHostedViewStateBinding(path, value =>
            {
                if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
                {
                    textBox.Text = value;
                }
            });
            textBox.TextChanged += (_, _) => SetHostedViewState(path, textBox.Text);
        }

        var grid = new Grid();
        grid.Children.Add(textBox);
        if (!string.IsNullOrWhiteSpace(component.Placeholder))
        {
            var placeholder = new TextBlock
            {
                IsHitTestVisible = false,
                Margin = new Thickness(14, 12, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF555555")!,
                TextWrapping = TextWrapping.Wrap,
                Text = component.Placeholder
            };
            textBox.TextChanged += (_, _) =>
            {
                placeholder.Visibility = string.IsNullOrWhiteSpace(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            placeholder.Visibility = string.IsNullOrWhiteSpace(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            grid.Children.Add(placeholder);
        }

        return grid;
    }

    private FrameworkElement BuildHostedViewButtonComponent(HostedViewComponentDefinition component, HostedPluginSession session)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = component.Text ?? component.Label ?? "执行",
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF2563EB")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 10, 18, 10),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await ExecuteHostedViewActionsAsync(session, component.Actions);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private FrameworkElement BuildHostedViewUnsupportedComponent(HostedViewComponentDefinition component)
    {
        return new TextBlock
        {
            Text = $"暂不支持的组件类型：{component.Type}",
            Foreground = System.Windows.Media.Brushes.OrangeRed,
            FontSize = 12
        };
    }

    private void RegisterHostedViewStateBinding(string path, Action<string> updater)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_hostedViewStateBindings.TryGetValue(path, out var updaters))
        {
            updaters = [];
            _hostedViewStateBindings[path] = updaters;
        }

        updaters.Add(updater);
        updater(GetHostedViewState(path));
    }

    private string GetHostedViewState(string path)
    {
        if (_activeHostedView == null || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return _activeHostedView.State.TryGetValue(path, out var value) ? value : string.Empty;
    }

    private void SetHostedViewState(string path, string? value)
    {
        if (_activeHostedView == null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = value ?? string.Empty;
        _activeHostedView.State[path] = normalized;
        _activeHostedView.BindingContext.NotifyChanged();
        if (_hostedViewStateBindings.TryGetValue(path, out var updaters))
        {
            foreach (var updater in updaters)
            {
                updater(normalized);
            }
        }
    }

    private async Task ExecuteHostedViewActionsAsync(
        HostedPluginSession session,
        IReadOnlyList<HostedViewActionDefinition> actions)
    {
        if (actions.Count == 0)
        {
            HostedViewStatus = "当前按钮没有配置动作。";
            return;
        }

        foreach (var action in actions)
        {
            switch (action.Type.Trim().ToLowerInvariant())
            {
                case "setstate":
                    var value = !string.IsNullOrWhiteSpace(action.ValueFrom)
                        ? GetHostedViewState(action.ValueFrom)
                        : action.Value ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(action.Path))
                    {
                        if (action.Append)
                        {
                            var existingValue = GetHostedViewState(action.Path);
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                value = string.IsNullOrWhiteSpace(existingValue)
                                    ? value
                                    : $"{existingValue}{action.Separator ?? Environment.NewLine}{value}";
                            }
                        }

                        SetHostedViewState(action.Path, value);
                    }
                    HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
                        ? "已更新界面状态。"
                        : action.SuccessMessage;
                    break;
                case "runscript":
                    await ExecuteHostedViewScriptActionAsync(session, action);
                    break;
                case "loadstorage":
                    await ExecuteHostedViewLoadStorageActionAsync(session, action);
                    break;
                case "savestorage":
                    await ExecuteHostedViewSaveStorageActionAsync(session, action);
                    break;
                case "close":
                    HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
                        ? "正在关闭视图。"
                        : action.SuccessMessage;
                    CloseHostedView();
                    return;
                default:
                    HostedViewStatus = $"暂不支持的动作类型：{action.Type}";
                    break;
            }
        }
    }

    private async Task ExecuteHostedViewScriptActionAsync(HostedPluginSession session, HostedViewActionDefinition action)
    {
        if (!ScriptExtensionRunner.CanExecute(session.Command))
        {
            HostedViewStatus = "当前扩展没有可执行的脚本入口。";
            return;
        }

        var input = !string.IsNullOrWhiteSpace(action.InputFrom)
            ? GetHostedViewState(action.InputFrom)
            : ResolveDefaultHostedViewInput(session);
        HostedViewStatus = $"正在执行 {session.Command.Title} 脚本...";
        var result = await ScriptExtensionRunner.ExecuteAsync(session.Command, input, "hosted-view-v2");
        var outputPath = string.IsNullOrWhiteSpace(action.OutputTo) ? "output" : action.OutputTo;
        SetHostedViewState(outputPath, result.Success ? result.Output : result.Error);
        HostedViewStatus = result.Success
            ? (string.IsNullOrWhiteSpace(action.SuccessMessage) ? "脚本执行完成。" : action.SuccessMessage)
            : $"脚本执行失败：{result.Error}";
    }

    private async Task ExecuteHostedViewLoadStorageActionAsync(HostedPluginSession session, HostedViewActionDefinition action)
    {
        var statePath = string.IsNullOrWhiteSpace(action.Path) ? action.ValueFrom : action.Path;
        if (string.IsNullOrWhiteSpace(statePath))
        {
            HostedViewStatus = "loadStorage 缺少 path。";
            return;
        }

        var storageKey = string.IsNullOrWhiteSpace(action.Key) ? $"{statePath}.txt" : action.Key;
        var readResult = await ExtensionStorageService.ReadTextAsync(session.Command.ExtensionId, storageKey, action.Scope);
        var value = readResult.Found ? readResult.Content ?? string.Empty : action.DefaultValue ?? string.Empty;
        SetHostedViewState(statePath, value);
        HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
            ? (readResult.Found ? $"已从 {readResult.Source} 加载存储数据。" : "未找到存储数据，已使用默认值。")
            : action.SuccessMessage;
    }

    private async Task ExecuteHostedViewSaveStorageActionAsync(HostedPluginSession session, HostedViewActionDefinition action)
    {
        var statePath = string.IsNullOrWhiteSpace(action.Path) ? action.ValueFrom : action.Path;
        var storageKey = string.IsNullOrWhiteSpace(action.Key)
            ? (!string.IsNullOrWhiteSpace(statePath) ? $"{statePath}.txt" : null)
            : action.Key;
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            HostedViewStatus = "saveStorage 缺少 key。";
            return;
        }

        var value = !string.IsNullOrWhiteSpace(action.ValueFrom)
            ? GetHostedViewState(action.ValueFrom)
            : !string.IsNullOrWhiteSpace(statePath)
                ? GetHostedViewState(statePath)
                : action.Value ?? string.Empty;
        var result = await ExtensionStorageService.WriteTextAsync(
            session.Command.ExtensionId,
            storageKey,
            value,
            action.Scope);
        HostedViewStatus = string.IsNullOrWhiteSpace(action.SuccessMessage)
            ? (result.CloudSaved ? "已保存到本地并同步到坚果云。" : "已保存到本地存储。")
            : action.SuccessMessage;
    }

    private static string ResolveHostedViewLayout(HostedPluginViewDefinition definition)
    {
        return string.IsNullOrWhiteSpace(definition.Type)
            ? "single-pane"
            : definition.Type.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<HostedViewActionDefinition> ParseHostedViewActionString(string actionText)
    {
        var actions = new List<HostedViewActionDefinition>();
        foreach (var rawAction in actionText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segments = rawAction.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            string type;
            string? path = null;
            string? value = null;
            string? valueFrom = null;
            string? inputFrom = null;
            string? outputTo = null;
            string? successMessage = null;
            var append = false;
            string? separator = null;
            string? key = null;
            string? scope = null;
            string? defaultValue = null;

            if (segments[0].Contains('='))
            {
                type = "setState";
            }
            else
            {
                type = segments[0];
                segments = segments.Skip(1).ToArray();
            }

            foreach (var segment in segments)
            {
                var parts = segment.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var propertyKey = parts[0].Trim();
                var parsedValue = parts[1].Trim();
                switch (propertyKey.ToLowerInvariant())
                {
                    case "type":
                        type = parsedValue;
                        break;
                    case "path":
                        path = parsedValue;
                        break;
                    case "value":
                        value = parsedValue;
                        break;
                    case "valuefrom":
                        valueFrom = parsedValue;
                        break;
                    case "inputfrom":
                        inputFrom = parsedValue;
                        break;
                    case "outputto":
                        outputTo = parsedValue;
                        break;
                    case "successmessage":
                        successMessage = parsedValue;
                        break;
                    case "append":
                        append = parsedValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                 parsedValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                                 parsedValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "separator":
                        separator = parsedValue
                            .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
                            .Replace("\\n", "\n", StringComparison.Ordinal)
                            .Replace("\\t", "\t", StringComparison.Ordinal);
                        break;
                    case "key":
                        key = parsedValue;
                        break;
                    case "scope":
                        scope = parsedValue;
                        break;
                    case "defaultvalue":
                        defaultValue = parsedValue
                            .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
                            .Replace("\\n", "\n", StringComparison.Ordinal)
                            .Replace("\\t", "\t", StringComparison.Ordinal);
                        break;
                }
            }

            actions.Add(new HostedViewActionDefinition(type, path, value, valueFrom, inputFrom, outputTo, successMessage, append, separator, key, scope, defaultValue));
        }

        return actions;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
        {
            yield break;
        }

        var childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private string ResolveDefaultHostedViewInput(HostedPluginSession session)
    {
        if (!string.IsNullOrWhiteSpace(HostedViewInput))
        {
            return HostedViewInput.Trim();
        }

        if (session.State.TryGetValue("input", out var stateInput) && !string.IsNullOrWhiteSpace(stateInput))
        {
            return stateInput;
        }

        var firstBoundTextarea = session.Definition.Components
            .FirstOrDefault(component =>
                string.Equals(component.Type, "textarea", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(component.Bind));
        return firstBoundTextarea != null && !string.IsNullOrWhiteSpace(firstBoundTextarea.Bind)
            ? GetHostedViewState(firstBoundTextarea.Bind)
            : string.Empty;
    }

    private void ApplyHostedViewWindowMetrics(HostedPluginViewDefinition definition)
    {
        var minWidth = NormalizeHostedViewDimension(definition.MinWindowWidth, _defaultMinWindowWidth);
        var minHeight = NormalizeHostedViewDimension(definition.MinWindowHeight, _defaultMinWindowHeight);
        var width = NormalizeHostedViewDimension(definition.WindowWidth, _defaultWindowWidth);
        var height = NormalizeHostedViewDimension(definition.WindowHeight, _defaultWindowHeight);

        MinWidth = minWidth;
        MinHeight = minHeight;
        Width = Math.Max(width, minWidth);
        Height = Math.Max(height, minHeight);
    }

    private void RestoreHostedViewWindowMetrics()
    {
        MinWidth = _defaultMinWindowWidth;
        MinHeight = _defaultMinWindowHeight;
        Width = Math.Max(_defaultWindowWidth, MinWidth);
        Height = Math.Max(_defaultWindowHeight, MinHeight);
    }

    private static double NormalizeHostedViewDimension(double? value, double fallback)
    {
        return value is > 0 ? value.Value : fallback;
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

    private async Task RefreshCloudStateAsync(bool allowLoginPrompt = true)
    {
        if (_cloudSyncClient == null)
        {
            await SyncPersonalWebDavAsync(showDisabledMessage: true);
            return;
        }

        try
        {
            SyncStatus = "正在读取账号状态和云端配置...";
            if (!await EnsureAuthenticatedAsync(allowPrompt: allowLoginPrompt))
            {
                return;
            }

            var me = await _cloudSyncClient.GetMeAsync();
            var pulledConfig = await PullWebDavConfigFromCloudAsync();
            _allCommands.RemoveAll(x => x.Source == CommandSource.Cloud);
            foreach (var command in _allCommands)
            {
                command.ClearCloudData();
            }
            ApplyFilter(SearchBox.Text);
            SyncStatus = $"已登录 {me?.Username ?? _cloudSyncClient.CurrentUserLabel}";
            LastRunMessage = pulledConfig
                ? "已同步账号状态，并更新了坚果云 / WebDAV 配置。"
                : "已同步账号状态。";
            OnPropertyChanged(nameof(SyncSummaryText));
        }
        catch (Exception ex)
        {
            if (allowLoginPrompt && await TryRecoverAuthenticationAsync(ex))
            {
                await RefreshCloudStateAsync();
                return;
            }

            SyncStatus = $"云同步读取失败：{FormatExceptionMessage(ex)}";
        }
    }

    private Task SyncSelectedCommandAsync()
    {
        SyncStatus = "Cloudflare 当前只同步账号状态和坚果云 / WebDAV 配置，扩展分享稍后接入。";
        return Task.CompletedTask;
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

    private Task AddJsonExtensionAsync()
    {
        try
        {
            var command = ShowJsonExtensionEditorAsync(
                string.Empty,
                isEditMode: false);
            if (command == null)
            {
                return Task.CompletedTask;
            }

            LastRunMessage = $"已添加本地 JSON 扩展：{command.Title}";
            QueueBackgroundWebDavSync("extension-add");
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"AddJsonExtensionAsync failed: {ex}");
            SyncStatus = $"添加扩展失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private Task EditSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可编辑的扩展。";
            return Task.CompletedTask;
        }

        var editable = ResolveRunnableCommand(sourceCommand);
        if (editable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接编辑。";
            return Task.CompletedTask;
        }

        try
        {
            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorAsync(manifestJson, isEditMode: true);
            if (updated == null)
            {
                return Task.CompletedTask;
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            QueueBackgroundWebDavSync("extension-edit");
        }
        catch (Exception ex)
        {
            SyncStatus = $"编辑失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
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
            $"确认删除扩展“{deletable.Title}”吗？\n这会删除本地扩展目录；如果已启用坚果云/WebDAV，同步器会在后台更新远端副本。",
            "删除扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            LocalExtensionCatalog.DeleteExtension(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            LastRunMessage = $"已删除本地扩展：{deletable.Title}";
            SyncStatus = $"已删除扩展：{deletable.Title}";
            QueueBackgroundWebDavSync("extension-delete");
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

    private async Task<bool> EnsureAuthenticatedAsync(bool forcePrompt = false, bool allowPrompt = true)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        if (forcePrompt || !_cloudSyncClient.HasCredential)
        {
            if (!allowPrompt)
            {
                SyncStatus = "未登录，已跳过云端账号同步。";
                return false;
            }

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
            if (!allowPrompt)
            {
                SyncStatus = $"云端账号同步失败，已跳过登录弹窗：{FormatExceptionMessage(ex)}";
                HostAssets.AppendLog($"Cloud silent auth failed: {FormatExceptionMessage(ex)}");
                return false;
            }

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

        _cloudSyncClient.ClearSessionOnly();
        if (await EnsureAuthenticatedAsync(allowPrompt: false))
        {
            return true;
        }

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
            var saved = SecureCredentialStore.Load();
            var dialog = new LoginWindow(saved?.LoginEmail);
            dialog.SendRegistrationCodeAsync = (email, username) => _cloudSyncClient.SendRegistrationCodeAsync(email, username);
            dialog.SendPasswordResetCodeAsync = (email) => _cloudSyncClient.SendPasswordResetCodeAsync(email);
            dialog.RegisterAsyncHandler = (email, username, password, code) => _cloudSyncClient.RegisterAsync(email, username, password, code);
            dialog.ResetPasswordAsyncHandler = (email, password, code) => _cloudSyncClient.ResetPasswordAsync(email, password, code);
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

            _cloudSyncClient.SetCredential(dialog.LoginEmail, dialog.Password, dialog.RememberCredential);
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

    private static List<CommandItem> CreateSeedCommands()
    {
        return [];
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
        LocalExtensionCatalog.EnsureSampleExtension();
        _allCommands.RemoveAll(x => x.Source == CommandSource.LocalExtension);
        _localExtensionIndex.Clear();
        foreach (var command in LocalExtensionCatalog.LoadCommands())
        {
            UpsertLocalExtensionCommand(command);
        }

        ApplyFilter(SearchBox.Text);
        SyncStatus = "已通过外部 Agent API 刷新本地扩展。";
    }

    private void ReloadLocalExtensionsFromWebDav()
    {
        _allCommands.RemoveAll(x => x.Source == CommandSource.LocalExtension);
        _localExtensionIndex.Clear();
        foreach (var command in LocalExtensionCatalog.LoadCommands())
        {
            UpsertLocalExtensionCommand(command);
        }

        ApplyFilter(SearchBox.Text);
    }

    private async Task SyncPersonalWebDavAsync(bool showDisabledMessage)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWebDavSync)
        {
            if (showDisabledMessage)
            {
                SyncStatus = "未启用个人 WebDAV 扩展同步。";
            }

            return;
        }

        try
        {
            var service = new WebDavSyncService(settings);
            var result = await service.SyncExtensionsAsync();
            ReloadLocalExtensionsFromWebDav();
            LastRunMessage = $"个人扩展同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。";
        }
        catch (Exception ex)
        {
            SyncStatus = $"个人扩展同步失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void StartBackgroundWebDavSync()
    {
        if (AppSettingsStore.Load().EnableWebDavSync && !_backgroundWebDavSyncTimer.IsEnabled)
        {
            _backgroundWebDavSyncTimer.Start();
        }
    }

    private void QueueBackgroundWebDavSync(string reason)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWebDavSync)
        {
            return;
        }

        StartBackgroundWebDavSync();
        if (_backgroundWebDavSyncRunning)
        {
            _backgroundWebDavSyncRequested = true;
            HostAssets.AppendLog($"WebDAV background sync queued while running: {reason}");
            return;
        }

        _ = RunBackgroundWebDavSyncAsync(reason);
    }

    private async Task RunBackgroundWebDavSyncAsync(string reason)
    {
        _backgroundWebDavSyncRunning = true;
        try
        {
            HostAssets.AppendLog($"WebDAV background sync started: {reason}");
            var service = new WebDavSyncService(AppSettingsStore.Load());
            var result = await service.SyncExtensionsAsync();
            ReloadLocalExtensionsFromWebDav();
            SyncStatus = $"个人扩展后台同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。";
            HostAssets.AppendLog($"WebDAV background sync completed: {reason}, uploaded={result.UploadedCount}, pulled={result.PulledCount}");
        }
        catch (Exception ex)
        {
            var message = FormatExceptionMessage(ex);
            SyncStatus = $"个人扩展后台同步失败：{message}";
            HostAssets.AppendLog($"WebDAV background sync failed: {reason} -> {message}");
        }
        finally
        {
            _backgroundWebDavSyncRunning = false;
            if (_backgroundWebDavSyncRequested)
            {
                _backgroundWebDavSyncRequested = false;
                QueueBackgroundWebDavSync("queued");
            }
        }
    }

    private async Task<bool> PullWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var snapshot = await _cloudSyncClient.GetUserConfigAsync<CloudWebDavConfigSnapshot>(CloudWebDavConfigId);
        if (snapshot == null)
        {
            HostAssets.AppendLog("WebDAV cloud pull: no user config found.");
            if (ShouldSyncLocalWebDavConfigToCloud())
            {
                await PushWebDavConfigToCloudAsync("cloud-refresh-bootstrap");
            }
            return false;
        }

        HostAssets.AppendLog(
            $"WebDAV cloud pull: enabled={snapshot.EnableWebDavSync}, serverUrl={snapshot.WebDavServerUrl}, rootPath={snapshot.WebDavRootPath}, username={snapshot.WebDavUsername}, hasPassword={!string.IsNullOrWhiteSpace(snapshot.WebDavPassword)}");

        var settings = AppSettingsStore.Load();
        var shouldDefaultEnable = snapshot.EnableWebDavSync || HasWebDavConfigValues(snapshot.WebDavServerUrl, snapshot.WebDavRootPath, snapshot.WebDavUsername, snapshot.WebDavPassword);
        var resolvedEnabled = settings.WebDavSyncManuallyDisabled ? false : shouldDefaultEnable;
        var changed =
            settings.EnableWebDavSync != resolvedEnabled ||
            !string.Equals(settings.WebDavServerUrl, snapshot.WebDavServerUrl, StringComparison.Ordinal) ||
            !string.Equals(settings.WebDavRootPath, snapshot.WebDavRootPath, StringComparison.Ordinal) ||
            !string.Equals(settings.WebDavUsername, snapshot.WebDavUsername, StringComparison.Ordinal);
        var credential = WebDavCredentialStore.Load();
        var passwordChanged = !string.Equals(credential?.Password, snapshot.WebDavPassword, StringComparison.Ordinal);
        if (!changed)
        {
            if (passwordChanged && !string.IsNullOrWhiteSpace(snapshot.WebDavPassword))
            {
                HostAssets.AppendLog("WebDAV cloud pull: applying password-only update.");
                SaveWebDavCredential(snapshot.WebDavUsername ?? string.Empty, snapshot.WebDavPassword);
                NotifySettingsWindowWebDavConfigChanged();
                return true;
            }

            HostAssets.AppendLog("WebDAV cloud pull: no local changes detected.");
            return false;
        }

        settings.EnableWebDavSync = resolvedEnabled;
        settings.WebDavServerUrl = string.IsNullOrWhiteSpace(snapshot.WebDavServerUrl)
            ? settings.WebDavServerUrl
            : snapshot.WebDavServerUrl.Trim();
        settings.WebDavRootPath = string.IsNullOrWhiteSpace(snapshot.WebDavRootPath)
            ? "/yanzi"
            : snapshot.WebDavRootPath.Trim();
        settings.WebDavUsername = snapshot.WebDavUsername?.Trim() ?? string.Empty;
        if (resolvedEnabled)
        {
            settings.WebDavSyncManuallyDisabled = false;
        }
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        if (!string.IsNullOrWhiteSpace(snapshot.WebDavPassword))
        {
            SaveWebDavCredential(snapshot.WebDavUsername ?? string.Empty, snapshot.WebDavPassword);
        }
        if (settings.EnableWebDavSync)
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        HostAssets.AppendLog(
            $"WebDAV cloud pull applied: enabled={settings.EnableWebDavSync}, serverUrl={settings.WebDavServerUrl}, rootPath={settings.WebDavRootPath}, username={settings.WebDavUsername}, passwordSaved={!string.IsNullOrWhiteSpace(snapshot.WebDavPassword)}");
        NotifySettingsWindowWebDavConfigChanged();
        return true;
    }

    private void QueueCloudWebDavConfigSync(string reason)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _ = PushWebDavConfigToCloudSafeAsync(reason);
    }

    private async Task PushWebDavConfigToCloudSafeAsync(string reason)
    {
        try
        {
            await PushWebDavConfigToCloudAsync(reason);
            HostAssets.AppendLog($"Cloud WebDAV config synced: {reason}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Cloud WebDAV config sync skipped: {reason} -> {FormatExceptionMessage(ex)}");
        }
    }

    private async Task PushWebDavConfigToCloudAsync(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential || !ShouldSyncLocalWebDavConfigToCloud())
        {
            HostAssets.AppendLog($"WebDAV cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        var credential = WebDavCredentialStore.Load();
        HostAssets.AppendLog(
            $"WebDAV cloud push: reason={reason}, enabled={settings.EnableWebDavSync}, serverUrl={settings.WebDavServerUrl}, rootPath={settings.WebDavRootPath}, username={settings.WebDavUsername}, hasPassword={!string.IsNullOrWhiteSpace(credential?.Password)}");
        await _cloudSyncClient.UpsertUserConfigAsync(CloudWebDavConfigId, new CloudWebDavConfigSnapshot
        {
            EnableWebDavSync = settings.EnableWebDavSync,
            WebDavServerUrl = settings.WebDavServerUrl,
            WebDavRootPath = settings.WebDavRootPath,
            WebDavUsername = settings.WebDavUsername,
            WebDavPassword = credential?.Password
        });
    }

    private static bool ShouldSyncLocalWebDavConfigToCloud()
    {
        var settings = AppSettingsStore.Load();
        return !string.IsNullOrWhiteSpace(settings.WebDavServerUrl) &&
               !string.IsNullOrWhiteSpace(settings.WebDavRootPath) &&
               !string.IsNullOrWhiteSpace(settings.WebDavUsername);
    }

    private CommandItem? ShowJsonExtensionEditorAsync(string initialJson, bool isEditMode)
    {
        return ShowJsonExtensionEditorForOwner(initialJson, isEditMode, this);
    }

    private CommandItem? ShowJsonExtensionEditorForOwner(string initialJson, bool isEditMode, Window? owner)
    {
        var currentJson = initialJson;

        while (true)
        {
            var dialog = new AddJsonExtensionWindow(currentJson, isEditMode)
            {
                Owner = owner
            };
            dialog.ShowDialog();
            if (!dialog.WasAccepted)
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
                    Owner = owner
                };
                retryDialog.ShowError(ex.Message);
                retryDialog.ShowDialog();
                if (!retryDialog.WasAccepted)
                {
                    return null;
                }

                currentJson = retryDialog.JsonContent;
            }
        }
    }

    private static string CreateWebSearchTemplateJson()
    {
        var id = $"web-search-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var manifest = new LocalExtensionManifest
        {
            Id = id,
            Name = "自定义网页搜索",
            Version = "1.0.0",
            Category = "网页搜索",
            Description = "输入关键词后打开指定网站的搜索结果。",
            Keywords = ["网页", "搜索", "自定义"],
            QueryPrefixes = ["搜索", "web"],
            QueryTargetTemplate = "https://www.example.com/search?q={query}",
            Icon = "mdi:magnify"
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
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
        return ShowJsonExtensionEditorAsync(string.Empty, false);
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
        SetSearchScopePopupOpen(true);
    }

    public void ShowMousePanel()
    {
        _quickPanel?.ShowAtMouse();
    }

    public void StartMousePanelService()
    {
        if (_listenerServicesPaused)
        {
            return;
        }

        InputHookService.Start(
            () => _quickPanel?.ShowAtMouse(),
            () => _quickPanel?.ExecuteHoveredSlotFromHoldRelease());
    }

    public void StopMousePanelService()
    {
        InputHookService.Stop();
    }

    public bool IsMousePanelServiceRunning => InputHookService.IsRunning;

    public bool AreListenerServicesPaused => _listenerServicesPaused;

    public void PauseListenerServices()
    {
        _listenerServicesPaused = true;
        StopMousePanelService();
        UnregisterLauncherHotkey();
        UnregisterExtensionHotkeys();
        SyncStatus = "已暂停快捷键、扩展快捷键和鼠标面板监听。";
    }

    public void ResumeListenerServices()
    {
        _listenerServicesPaused = false;
        InputHookService.ReloadSettings();
        StartMousePanelService();
        RefreshLauncherHotkeyRegistration();
        RefreshExtensionHotkeys();
        SyncStatus = "已恢复快捷键、扩展快捷键和鼠标面板监听。";
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
        SetSearchScopePopupOpen(false);
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
        if (!_listenerServicesPaused)
        {
            RefreshLauncherHotkeyRegistration();
            RefreshExtensionHotkeys();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            if (_source != null)
            {
                UnregisterExtensionHotkeys();
                UnregisterLauncherHotkey();
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
            SetSearchScopePopupOpen(false);
            HideToTray();
        }
        else if (IsActive)
        {
            SetSearchScopePopupOpen(true);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        SetSearchScopePopupOpen(false);

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

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        SetSearchScopePopupOpen(true);
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SetSearchScopePopupOpen(IsVisible && IsActive);
    }

    private void SetSearchScopePopupOpen(bool isOpen)
    {
        if (!IsInitialized)
        {
            return;
        }

        SearchScopePopup.IsOpen = isOpen && IsVisible && WindowState != WindowState.Minimized;
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
                     .Where(command => IsExtensionEnabled(command.ExtensionId))
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

        UnregisterLauncherHotkey();
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

    private void UnregisterLauncherHotkey()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotKeyId);
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
            HostAssets.AppendLog("PromptLoginFromSettingsAsync: authentication succeeded, pulling WebDAV config.");
            await PullWebDavConfigFromCloudAsync();
            NotifySettingsWindowWebDavConfigChanged();
            
            return true;
        }
        catch (Exception ex)
        {
            SyncStatus = $"登录失败：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    private async Task SyncWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        try
        {
            var config = await _cloudSyncClient.FetchWebDavConfigAsync();
            if (config != null)
            {
                var localSettings = AppSettingsStore.Load();
                var resolvedEnabled = localSettings.WebDavSyncManuallyDisabled
                    ? false
                    : (config.Enabled || HasWebDavConfigValues(config.ServerUrl, config.RootPath, config.Username, config.Password));
                // Apply configuration to local settings
                SaveWebDavSettings(
                    resolvedEnabled,
                    config.ServerUrl ?? string.Empty,
                    config.RootPath ?? string.Empty,
                    config.Username ?? string.Empty
                );
                
                // Save credential if provided
                if (!string.IsNullOrWhiteSpace(config.Password))
                {
                    SaveWebDavCredential(config.Username ?? string.Empty, config.Password);
                }
                
                // Notify SettingsWindow to refresh UI if open
                NotifySettingsWindowWebDavConfigChanged();
                
                System.Diagnostics.Debug.WriteLine("WebDAV configuration synced from cloud successfully.");
            }
        }
        catch (Exception ex)
        {
            // Log error but don't block login process
            System.Diagnostics.Debug.WriteLine($"Failed to sync WebDAV config from cloud: {ex.Message}");
        }
    }

    private static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username, string? password)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username) ||
               !string.IsNullOrWhiteSpace(password);
    }

    private void NotifySettingsWindowWebDavConfigChanged()
    {
        // If SettingsWindow is open, refresh its WebDAV UI
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshWebDavConfigFromExternal();
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

        HostAssets.AppendLog(
            $"SignOutFromSettings: before clear sessionExists={File.Exists(SyncSessionStore.SessionPath)}, credentialExists={File.Exists(SecureCredentialStore.CredentialPath)}");
        _cloudSyncClient.ClearCredential();
        SyncStatus = "已退出登录。";
        OnPropertyChanged(nameof(SyncSummaryText));
        NotifySettingsWindowAccountChanged();
        HostAssets.AppendLog(
            $"SignOutFromSettings: after clear sessionExists={File.Exists(SyncSessionStore.SessionPath)}, credentialExists={File.Exists(SecureCredentialStore.CredentialPath)}");
    }

    private void NotifySettingsWindowAccountChanged()
    {
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshAccountFromExternal();
    }

    public void RefreshAppSettings()
    {
        var settings = AppSettingsStore.Load();
        _appSettings = settings;
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
            RefreshLauncherHotkeyRegistration();
            RefreshExtensionHotkeys();
        }
        SyncStatus = settings.LaunchAtStartup
            ? "设置已保存。开机启动已启用。"
            : settings.RefreshCloudOnStartup
                ? "设置已保存。"
                : "设置已保存。启动后自动刷新云状态已关闭。";
    }

    public string GetLauncherHotkey() => AppSettingsStore.Load().LauncherHotkey;

    public AppSettings GetCurrentAppSettings() => AppSettingsStore.Load();

    public void SaveWebDavSettings(bool enabled, string serverUrl, string rootPath, string username)
    {
        var settings = AppSettingsStore.Load();
        settings.EnableWebDavSync = enabled;
        settings.WebDavSyncManuallyDisabled = !enabled && HasWebDavConfigValues(serverUrl, rootPath, username, null);
        settings.WebDavServerUrl = serverUrl.Trim();
        settings.WebDavRootPath = string.IsNullOrWhiteSpace(rootPath) ? "/yanzi" : rootPath.Trim();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        if (enabled)
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        QueueCloudWebDavConfigSync("settings-saved");
    }

    public void SaveWebDavCredential(string username, string password)
    {
        WebDavCredentialStore.Save(new SavedWebDavCredential
        {
            Username = username.Trim(),
            Password = password
        });

        var settings = AppSettingsStore.Load();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        StartBackgroundWebDavSync();
        QueueBackgroundWebDavSync("credential-saved");
        QueueCloudWebDavConfigSync("credential-saved");
    }

    public bool HasWebDavCredential()
    {
        var credential = WebDavCredentialStore.Load();
        return !string.IsNullOrWhiteSpace(credential?.Username) &&
               !string.IsNullOrWhiteSpace(credential?.Password);
    }

    public async Task<(bool ok, string message)> ProbeWebDavAsync()
    {
        try
        {
            var service = new WebDavSyncService(AppSettingsStore.Load());
            await service.ProbeAsync();
            return (true, $"WebDAV 连接正常：{service.SyncRootDisplay}");
        }
        catch (Exception ex)
        {
            return (false, $"WebDAV 测试失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message)> SyncWebDavNowAsync()
    {
        try
        {
            var service = new WebDavSyncService(AppSettingsStore.Load());
            var result = await service.SyncExtensionsAsync();
            ReloadLocalExtensionsFromWebDav();
            return (true, $"个人扩展同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。");
        }
        catch (Exception ex)
        {
            return (false, $"个人扩展同步失败：{FormatExceptionMessage(ex)}");
        }
    }

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

    public IReadOnlyList<CommandItem> GetExtensionsForSettings()
    {
        return _localExtensionIndex.Values
            .OrderBy(static x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsExtensionEnabled(string extensionId) =>
        !_appSettings.DisabledExtensionIds.Contains(extensionId, StringComparer.OrdinalIgnoreCase);

    public void SetExtensionEnabled(string extensionId, bool enabled)
    {
        var settings = AppSettingsStore.Load();
        settings.DisabledExtensionIds ??= [];
        settings.DisabledExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            settings.DisabledExtensionIds.Add(extensionId);
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        RefreshExtensionHotkeys();
        ApplyFilter(SearchBox.Text);
    }

    public IReadOnlyList<CommandItem> GetQuickPanelRecommendedCommands(ForegroundAppContext? context, IEnumerable<string> excludeExtensionIds, int maxCount = 8)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.ProcessName))
        {
            return [];
        }

        var exclude = new HashSet<string>(excludeExtensionIds, StringComparer.OrdinalIgnoreCase);
        var aliases = BuildContextAliases(context.ProcessName, context.WindowTitle);

        return _allCommands
            .Where(static command => !IsInternalCommand(command))
            .Where(command => IsExtensionEnabled(command.ExtensionId))
            .Where(command => !exclude.Contains(command.ExtensionId))
            .Select(command => new
            {
                Command = command,
                Score = ScoreQuickPanelRecommendation(command, aliases, context.WindowTitle)
            })
            .Where(static item => item.Score > 0)
            .OrderByDescending(static item => item.Score)
            .ThenBy(item => item.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maxCount))
            .Select(static item => item.Command)
            .ToList();
    }

    public Task<(bool ok, string message)> EditExtensionFromSettingsAsync(string extensionId, Window? owner = null)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var editable))
            {
                return Task.FromResult((false, "没有找到对应扩展。"));
            }

            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorForOwner(manifestJson, isEditMode: true, owner);
            if (updated == null)
            {
                return Task.FromResult((false, string.Empty));
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            QueueBackgroundWebDavSync("extension-edit-settings");
            return Task.FromResult((true, $"已更新扩展：{updated.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"编辑失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> EditExtensionFromQuickPanelAsync(string extensionId, Window? owner = null)
    {
        return EditExtensionFromSettingsAsync(extensionId, owner);
    }

    public Task<(bool ok, string message)> DeleteExtensionFromSettingsAsync(string extensionId, Window? owner = null)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var deletable))
            {
                return Task.FromResult((false, "没有找到对应扩展。"));
            }

            var confirm = System.Windows.MessageBox.Show(
                owner ?? this,
                $"确认删除扩展“{deletable.Title}”吗？\n这会删除本地扩展目录；如果已启用坚果云/WebDAV，同步器会在后台更新远端副本。",
                "删除扩展",
                MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return Task.FromResult((false, string.Empty));
            }

            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            LocalExtensionCatalog.DeleteExtension(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            LastRunMessage = $"已删除本地扩展：{deletable.Title}";
            SyncStatus = $"已删除扩展：{deletable.Title}";
            QueueBackgroundWebDavSync("extension-delete-settings");
            return Task.FromResult((true, $"已删除扩展：{deletable.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"删除失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public bool TryOpenExtensionDirectory(string extensionId, out string message)
    {
        message = string.Empty;
        if (!_localExtensionIndex.TryGetValue(extensionId, out var command) ||
            string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) ||
            !Directory.Exists(command.ExtensionDirectoryPath))
        {
            message = "扩展目录不存在。";
            return false;
        }

        var directoryPath = command.ExtensionDirectoryPath!;
        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true
        });
        return true;
    }

    public Task<(bool ok, string message)> UpdateExtensionShortcutFromSettingsAsync(string extensionId, string? shortcut)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(shortcut) &&
                !TryParseHotkey(shortcut, out _, out _))
            {
                return Task.FromResult((false, "快捷键格式无效。示例：Ctrl+Alt+T"));
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extensionId, shortcut);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            QueueBackgroundWebDavSync("extension-shortcut-settings");

            var message = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";
            return Task.FromResult((true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"设置快捷键失败：{FormatExceptionMessage(ex)}"));
        }
    }

    private void OpenCommandActionsMenu()
    {
        CommandList.Focus();
        if (!UpdateCommandContextMenuState() || CommandList.ContextMenu == null || !CommandList.ContextMenu.HasItems)
        {
            return;
        }

        // 获取当前选中的列表项
        var selectedItem = CommandList.ItemContainerGenerator.ContainerFromItem(SelectedCommand) as FrameworkElement;
        
        if (selectedItem != null)
        {
            // 在选中的列表项右侧显示菜单
            CommandList.ContextMenu.PlacementTarget = selectedItem;
            CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        }
        else
        {
            // 如果找不到选中项，则在列表控件上显示
            CommandList.ContextMenu.PlacementTarget = CommandList;
            CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
        }
        
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

    private Task RenameSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可重命名的扩展。";
            return Task.CompletedTask;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接重命名。";
            return Task.CompletedTask;
        }

        var dialog = new SimpleTextInputWindow("重命名扩展", "输入新的扩展名称。", extension.Title)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var renamed = LocalExtensionCatalog.RenameExtension(extension.ExtensionId, dialog.ValueText);
            UpsertLocalExtensionCommand(renamed);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(renamed.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已重命名扩展：{renamed.Title}";
            QueueBackgroundWebDavSync("extension-rename");
        }
        catch (Exception ex)
        {
            SyncStatus = $"重命名失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
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
            LastRunMessage = $"已添加到鼠标面板第 {index + 1} 个槽位：{command.Title}";
        }
        else
        {
            SyncStatus = "鼠标面板已满（28 个槽位），请先在面板中移除旧扩展。";
        }
    }

    private Task SetSelectedExtensionShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可设置快捷键的扩展。";
            return Task.CompletedTask;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接设置快捷键。";
            return Task.CompletedTask;
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
            return Task.CompletedTask;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(dialog.ShortcutText) &&
                !TryParseHotkey(dialog.ShortcutText, out _, out _))
            {
                SyncStatus = "快捷键格式无效。示例：Ctrl+Alt+T";
                return Task.CompletedTask;
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extension.ExtensionId, dialog.ShortcutText);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(updated.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";
            QueueBackgroundWebDavSync("extension-shortcut");
        }
        catch (Exception ex)
        {
            SyncStatus = $"设置快捷键失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
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

    private static CommandMatch BuildCommandMatch(CommandItem command, string query, bool allowRawQueryArgument = false)
    {
        var argument = ExtractQueryArgument(command, query, allowRawQueryArgument);
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

    private static string ExtractQueryArgument(CommandItem command, string rawInput, bool allowRawQuery = false)
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

        if (allowRawQuery)
        {
            return input;
        }

        return string.Empty;
    }

    private static string? BuildExecutionTarget(CommandItem command, string? rawInput, bool allowRawQuery = false)
    {
        if (command.SupportsQueryArgument)
        {
            var argument = ExtractQueryArgument(command, rawInput ?? string.Empty, allowRawQuery);
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
            RecordCommandUsage(runnable);
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
            return;
        }

        HostAssets.AppendLog($"Script extension failed: {runnable.Title} -> {result.Error}");
        LastRunMessage = $"脚本执行失败：{runnable.Title}";
        SyncStatus = $"脚本执行失败：{result.Error}";
        System.Windows.MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(result.Error) ? "脚本执行失败。" : result.Error.Trim(),
            $"{runnable.Title} 执行失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    // --- Quick Panel Support ---

    public List<CommandItem> GetAllCommands() => _allCommands.ToList();

    public void ExecuteCommandExternally(CommandItem command, string? explicitInput = null, string launchSource = "quick-panel")
    {
        _ = ExecuteCommandAsync(ResolveRunnableCommand(command), explicitInput ?? string.Empty, launchSource);
    }

    private void RecordCommandUsage(CommandItem command)
    {
        _searchUsageMemory.Record(command.ExtensionId);
        SearchUsageMemory.Save(_searchUsageMemory);
        QueueBackgroundWebDavSync("search-memory");
    }

    private static List<string> BuildContextAliases(string processName, string windowTitle)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            processName
        };

        if (processName.Contains("weixin", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("wechat", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("wechat");
            aliases.Add("weixin");
            aliases.Add("微信");
        }

        if (processName.Contains("code", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.Contains("visual studio code", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("code");
            aliases.Add("vscode");
            aliases.Add("visual studio code");
            aliases.Add("编辑器");
        }

        if (processName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("chrome");
            aliases.Add("浏览器");
        }

        if (processName.Contains("explorer", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("explorer");
            aliases.Add("资源管理器");
            aliases.Add("文件");
        }

        foreach (var token in windowTitle.Split([' ', '-', '_', '|', '·', ':', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 2)
            {
                aliases.Add(token);
            }
        }

        return aliases.ToList();
    }

    private static int ScoreQuickPanelRecommendation(CommandItem command, IReadOnlyList<string> aliases, string windowTitle)
    {
        var score = 0;
        foreach (var alias in aliases)
        {
            if (command.Title.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (command.Category.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                score += 6;
            }

            if (command.Subtitle.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (command.Keywords.Any(keyword => keyword.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            {
                score += 12;
            }
        }

        if (windowTitle.Contains(command.Title, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (command.HasHostedView)
        {
            score += 1;
        }

        if (command.Source == CommandSource.LocalExtension)
        {
            score += 2;
        }

        return score;
    }
}

public sealed record ForegroundAppContext(string ProcessName, string WindowTitle)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(WindowTitle) ? ProcessName : $"{ProcessName} · {WindowTitle}";
}

public readonly record struct CommandMatch(bool IsMatch, int Priority);

public readonly record struct SearchQueryState(string ScopeKey, string Term, bool IsEmpty);

public sealed class SearchScopeTab : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _count;

    public SearchScopeTab(string key, string label, string tooltip, bool isSelected = false)
    {
        Key = key;
        Label = label;
        Tooltip = tooltip;
        _isSelected = isSelected;
    }

    public string Key { get; }

    public string Label { get; }

    public string Tooltip { get; }

    public string DisplayLabel => Count > 0 ? $"{Label}{Count}" : Label;

    public int Count
    {
        get => _count;
        set
        {
            if (value == _count)
            {
                return;
            }

            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

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
    string? EmptyState,
    double? WindowWidth,
    double? WindowHeight,
    double? MinWindowWidth,
    double? MinWindowHeight,
    string? XamlTemplate,
    IReadOnlyDictionary<string, string> InitialState,
    IReadOnlyList<HostedViewComponentDefinition> Components)
{
    public bool UsesDynamicLayout => Components.Count > 0 || !string.IsNullOrWhiteSpace(XamlTemplate);
}

public sealed class HostedPluginSession
{
    public HostedPluginSession(CommandItem command, HostedPluginViewDefinition definition)
    {
        Command = command;
        Definition = definition;
        BindingContext = new HostedViewStateBindingContext(this);
    }

    public CommandItem Command { get; }

    public HostedPluginViewDefinition Definition { get; }

    public Dictionary<string, string> State { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HostedViewStateBindingContext BindingContext { get; }
}

public sealed record HostedViewComponentDefinition(
    string Id,
    string Type,
    string? Label,
    string? Text,
    string? Bind,
    string? Placeholder,
    string? Region,
    IReadOnlyList<HostedViewActionDefinition> Actions);

public sealed record HostedViewActionDefinition(
    string Type,
    string? Path,
    string? Value,
    string? ValueFrom,
    string? InputFrom,
    string? OutputTo,
    string? SuccessMessage,
    bool Append,
    string? Separator,
    string? Key,
    string? Scope,
    string? DefaultValue);

public sealed class HostedViewStateBindingContext : INotifyPropertyChanged
{
    private readonly HostedPluginSession _session;

    public HostedViewStateBindingContext(HostedPluginSession session)
    {
        _session = session;
    }

    public string this[string key]
    {
        get => _session.State.TryGetValue(key, out var value) ? value : string.Empty;
        set
        {
            _session.State[key] = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public void NotifyChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class HostedViewBridge
{
    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.RegisterAttached(
            "Action",
            typeof(string),
            typeof(HostedViewBridge),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PreferredFocusProperty =
        DependencyProperty.RegisterAttached(
            "PreferredFocus",
            typeof(string),
            typeof(HostedViewBridge),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LoadedActionProperty =
        DependencyProperty.RegisterAttached(
            "LoadedAction",
            typeof(string),
            typeof(HostedViewBridge),
            new PropertyMetadata(string.Empty));

    public static void SetAction(DependencyObject element, string value) => element.SetValue(ActionProperty, value);

    public static string GetAction(DependencyObject element) => (string)element.GetValue(ActionProperty);

    public static void SetPreferredFocus(DependencyObject element, string value) => element.SetValue(PreferredFocusProperty, value);

    public static string GetPreferredFocus(DependencyObject element) => (string)element.GetValue(PreferredFocusProperty);

    public static void SetLoadedAction(DependencyObject element, string value) => element.SetValue(LoadedActionProperty, value);

    public static string GetLoadedAction(DependencyObject element) => (string)element.GetValue(LoadedActionProperty);
}

public sealed class CloudWebDavConfigSnapshot
{
    [JsonPropertyName("enabled")]
    public bool EnableWebDavSync { get; set; }

    [JsonPropertyName("serverUrl")]
    public string WebDavServerUrl { get; set; } = "https://dav.jianguoyun.com/dav/";

    [JsonPropertyName("rootPath")]
    public string WebDavRootPath { get; set; } = "/yanzi";

    [JsonPropertyName("username")]
    public string WebDavUsername { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string? WebDavPassword { get; set; }

    [JsonPropertyName("enableWebDavSync")]
    public bool? LegacyEnabled
    {
        get => null;
        set
        {
            if (value.HasValue)
            {
                EnableWebDavSync = value.Value;
            }
        }
    }

    [JsonPropertyName("webDavServerUrl")]
    public string? LegacyServerUrl
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                WebDavServerUrl = value;
            }
        }
    }

    [JsonPropertyName("webDavRootPath")]
    public string? LegacyRootPath
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                WebDavRootPath = value;
            }
        }
    }

    [JsonPropertyName("webDavUsername")]
    public string? LegacyUsername
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                WebDavUsername = value;
            }
        }
    }

    [JsonPropertyName("webDavPassword")]
    public string? LegacyPassword
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                WebDavPassword = value;
            }
        }
    }
}

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
        string? inlineScriptSource = null,
        string? iconReference = null)
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
        IconReference = iconReference;
        IconSource = ExtensionIconLibrary.ResolveImageSource(iconReference, extensionDirectoryPath);
        VectorIcon = ExtensionIconLibrary.ResolveVectorIcon(iconReference);
    }

    public string Glyph { get; }

    public string DisplayGlyph => Glyph;

    public string? IconReference { get; }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon;

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
        : Source == CommandSource.WebSearch
            ? "网页"
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
        CommandSource.WebSearch => "网页搜索",
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
    Cloud,
    WebSearch
}
