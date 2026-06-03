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
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Specialized;
using System.Data;
using System.Globalization;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using Forms = System.Windows.Forms;
using System.Text;

namespace OpenQuickHost;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int HotKeyId = 0x5301;
    private const int YanmHotKeyId = 0x5302;
    private const int RadialHotKeyId = 0x5303;
    private const int WindowSnapAssistHotKeyId = 0x5304;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotKey = 0x0312;
    private const int WmDpiChanged = 0x02E0;
    private const string CloudPersonalSyncConfigId = "yanzi-personal-sync-settings";
    private const string CloudLegacyWebDavConfigId = "yanzi-webdav-settings";
    private const string CloudQuickPanelConfigId = "yanzi-quickpanel-settings";
    private const string SearchScopeAll = "all";
    private const string SearchScopeExtension = "extension";
    private const string SearchScopeApplication = "application";
    private const string SearchScopeFile = "file";
    private const string SearchScopeSystem = "system";
    private const string SearchScopeYanyu = "yanyu";

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
    private RadialMenuWindow? _radialMenu;
    private YanmOverlayWindow? _yanmOverlay;
    private MobileMessageToastWindow? _mobileMessageToastWindow;
    private readonly WindowBoundExtensionsService _windowBoundExtensionsService;
    private readonly WindowSnapAssistService _windowSnapAssistService;
    private readonly DispatcherTimer _backgroundWebDavSyncTimer;
    private readonly DispatcherTimer _backgroundWebDavSyncDelayTimer;
    private readonly DispatcherTimer _cloudReconnectTimer;
    private readonly DispatcherTimer _mobileMessagePollTimer;
    private readonly DispatcherTimer _fileSearchDebounceTimer;
    private int _fileSearchRequestVersion;
    private DateTimeOffset _lastFileSearchManualInitPromptAt = DateTimeOffset.MinValue;
    private bool _backgroundWebDavSyncRunning;
    private bool _backgroundWebDavSyncRequested;
    private string? _pendingBackgroundWebDavSyncReason;
    private bool _cloudReconnectInProgress;
    private bool _mobileMessagePollRunning;
    private CancellationTokenSource? _mobileMessageBridgeCts;
    private Task? _mobileMessageBridgeTask;
    private DateTimeOffset _lastMobileMessageEmptyLogAt = DateTimeOffset.MinValue;
    private int _cloudReconnectAttemptCount;
    private string? _cloudReconnectPendingReason;
    private string? _desktopDeviceId;
    private string _pendingFileSearchTerm = string.Empty;
    private string _activeFilterScopeKey = SearchScopeAll;
    private string _pendingProviderSearchTerm = string.Empty;
    private string _pendingProviderSearchScopeKey = string.Empty;
    private CommandItem? _pendingProviderSearchCommand;
    private string _searchInlineCompletionSuffix = string.Empty;
    private double _searchInlineCompletionPrefixWidth;
    private SearchScopeTab? _selectedSearchScope;
    private bool _listenerServicesPaused;
    private readonly double _defaultWindowWidth;
    private readonly double _defaultWindowHeight;
    private readonly double _defaultMinWindowWidth;
    private readonly double _defaultMinWindowHeight;
    private DateTimeOffset _autoHideSuppressedUntil = DateTimeOffset.MinValue;
    private readonly Dictionary<string, List<Action<string>>> _hostedViewStateBindings = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Controls.Control? _hostedViewPreferredFocusControl;
    private Window? _hostedViewEditorWindowToRestore;
    private QuickPanelClipboardItem? _quickPanelClipboard;
    private System.Windows.Point? _commandListDragStartPoint;
    private CommandItem? _commandListDragSource;
    private readonly ObservableCollection<AttachedFileItem> _attachedFiles = [];
    private CommandActionsMenuOrigin _commandActionsMenuOrigin = CommandActionsMenuOrigin.ResultsList;
    private bool _hasAppliedFilterOnce;
    private string _lastAppliedFilterText = string.Empty;
    private string _lastAppliedFilterScopeKey = SearchScopeAll;
    private uint _lastKnownWindowDpi = 96;
    private bool _dpiRefreshRequested;

    public MainWindow()
    {
        InitializeComponent();
        UpdateFooterMenuHint(isMenuOpen: false);
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

        _backgroundWebDavSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(6)
        };
        _backgroundWebDavSyncTimer.Tick += (_, _) => QueueBackgroundWebDavSync("timer");

        _backgroundWebDavSyncDelayTimer = new DispatcherTimer();
        _backgroundWebDavSyncDelayTimer.Tick += (_, _) =>
        {
            _backgroundWebDavSyncDelayTimer.Stop();
            var reason = _pendingBackgroundWebDavSyncReason ?? "delayed";
            _pendingBackgroundWebDavSyncReason = null;
            QueueBackgroundWebDavSync($"delayed-{reason}", forceImmediate: true);
        };

        _cloudReconnectTimer = new DispatcherTimer();
        _cloudReconnectTimer.Tick += CloudReconnectTimer_Tick;

        _mobileMessagePollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _mobileMessagePollTimer.Tick += MobileMessagePollTimer_Tick;

        _fileSearchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _fileSearchDebounceTimer.Tick += FileSearchDebounceTimer_Tick;

        _allCommands = CreateSeedCommands();
        _allCommands.AddRange(LocalExtensionCatalog.LoadCommands());
        _allCommands.AddRange(CreateInstalledApplicationCommands());
        _localExtensionIndex = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension)
            .GroupBy(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var localCommand in _localExtensionIndex.Values)
        {
            ApplyNewExtensionState(localCommand);
        }

        FilteredCommands = new ObservableCollection<CommandItem>(_allCommands);
        _attachedFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AttachedFilesVisibility));
        SearchScopes = new ObservableCollection<SearchScopeTab>(BuildSearchScopes());
        _selectedSearchScope = SearchScopes.First();
        SelectedCommand = FilteredCommands.FirstOrDefault();
        DataContext = this;
        ApplyFilter(string.Empty);
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        LocationChanged += MainWindow_PositionChanged;
        SizeChanged += MainWindow_PositionChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;

        _quickPanel = new QuickPanelWindow(this);
        _radialMenu = new RadialMenuWindow(this);
        _yanmOverlay = new YanmOverlayWindow(this);
        _windowBoundExtensionsService = new WindowBoundExtensionsService(this);
        _windowSnapAssistService = new WindowSnapAssistService();
        _windowSnapAssistService.DisabledByUser += () =>
        {
            var settings = AppSettingsStore.Load();
            settings.EnableWindowSnapAssist = false;
            AppSettingsStore.Save(settings);
            _appSettings = settings;
            UnregisterWindowSnapAssistHotkey();
            HostAssets.AppendLog("Window snap assist disabled by user via context menu.");
        };

        Closing += (s, e) =>
        {
            InputHookService.Stop();
            KeyboardDoubleTapService.Stop();
            YanyuTriggerService.Stop();
            YarnSelectService.Stop();
            _windowBoundExtensionsService.Stop();
            _windowSnapAssistService.Stop();
            _mobileMessageBridgeCts?.Cancel();
            _mobileMessagePollTimer.Stop();
        };

        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
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

    public ObservableCollection<AttachedFileItem> AttachedFiles => _attachedFiles;

    public Visibility AttachedFilesVisibility => _attachedFiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

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

            var previousScopeKey = _selectedSearchScope?.Key ?? SearchScopeAll;
            var wasAiMode = string.Equals(previousScopeKey, SearchScopeAi, StringComparison.OrdinalIgnoreCase);

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
            OnPropertyChanged(nameof(IsAiChatMode));
            OnPropertyChanged(nameof(AiChatVisibility));
            OnPropertyChanged(nameof(NormalLauncherVisibility));
            OnPropertyChanged(nameof(AiChatModelDisplayText));

            if (!string.Equals(previousScopeKey, SearchScopeAi, StringComparison.OrdinalIgnoreCase))
            {
                _lastNonAiSearchScopeKey = previousScopeKey;
            }

            ApplyFilter(SearchBox.Text);
            if (IsAiChatMode)
            {
                ActivateAiChatMode();
            }
            else if (wasAiMode)
            {
                // 从 AI Chat 模式切换到其他模式时，恢复窗口尺寸
                RestoreDefaultWindowSize();
                SetSearchScopePopupOpen(true);
            }
        }
    }

    private IEnumerable<SearchScopeTab> BuildSearchScopes()
    {
        yield return new SearchScopeTab(SearchScopeAll, "全部", "所有结果", true);
        yield return new SearchScopeTab(SearchScopeExtension, "扩展", "所有扩展");
        yield return new SearchScopeTab(SearchScopeApplication, "应用", "已安装应用");
        yield return new SearchScopeTab(SearchScopeFile, "文件", "Everything 文件结果");
        yield return new SearchScopeTab(SearchScopeSystem, "系统", "Windows 系统与设置");
        yield return new SearchScopeTab(SearchScopeYanyu, "燕语", "文本指令与扩展触发词");
        yield return new SearchScopeTab(SearchScopeAi, "AI对话", "切换到 AI 对话模式");

        foreach (var pinnedCommand in GetPinnedSearchScopeCommands())
        {
            yield return SearchScopeTab.CreatePinnedCommand(pinnedCommand.ExtensionId, pinnedCommand.Title, $"固定扩展：{pinnedCommand.Title}");
        }
    }

    private void RestoreDefaultWindowSize()
    {
        if (WindowState == WindowState.Normal)
        {
            // 保存当前窗口的顶部中心点
            var centerX = Left + Width / 2;
            var centerY = Top;
            
            // 先恢复最小约束，再恢复默认窗口大小，避免 AI 模式较大的 MinWidth 把 Width 卡住
            MinWidth = _defaultMinWindowWidth;
            MinHeight = _defaultMinWindowHeight;
            Width = _defaultWindowWidth;
            Height = _defaultWindowHeight;
            
            // 根据新尺寸重新计算位置，保持顶部中心不变
            Left = centerX - Width / 2;
            Top = centerY;
        }
    }

    private void SuppressAutoHideFor(TimeSpan duration)
    {
        var until = DateTimeOffset.Now.Add(duration);
        if (until > _autoHideSuppressedUntil)
        {
            _autoHideSuppressedUntil = until;
        }
    }

    private bool IsAutoHideSuppressed()
    {
        return DateTimeOffset.Now < _autoHideSuppressedUntil;
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

    public string VisibleCountText => string.Equals(_activeFilterScopeKey, SearchScopeFile, StringComparison.OrdinalIgnoreCase)
        ? $"{FilteredCommands.Count} 个文件结果"
        : string.Equals(_activeFilterScopeKey, SearchScopeAi, StringComparison.OrdinalIgnoreCase)
            ? $"{AiChatMessages.Count} 条对话"
            : $"{FilteredCommands.Count} 条结果";

    public string SearchInlineCompletionSuffix
    {
        get => _searchInlineCompletionSuffix;
        private set
        {
            if (value == _searchInlineCompletionSuffix)
            {
                return;
            }

            _searchInlineCompletionSuffix = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchInlineCompletionVisibility));
        }
    }

    public double SearchInlineCompletionPrefixWidth
    {
        get => _searchInlineCompletionPrefixWidth;
        private set
        {
            if (Math.Abs(value - _searchInlineCompletionPrefixWidth) < 0.1)
            {
                return;
            }

            _searchInlineCompletionPrefixWidth = value;
            OnPropertyChanged();
        }
    }

    public Visibility SearchInlineCompletionVisibility => string.IsNullOrWhiteSpace(SearchInlineCompletionSuffix)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string FooterHint => SelectedCommand == null
        ? "Up / Down 切换   Enter 执行   → 菜单   Esc 收起"
        : SelectedCommand.IsFileSystemResult
            ? "Up / Down 切换   Enter 打开   右键原生菜单   Esc 收起"
        : SelectedCommand.SupportsQueryArgument && !string.IsNullOrWhiteSpace(_activeQueryArgument)
            ? $"{SelectedCommand.Title}   ·   {BuildQueryPreviewText(SelectedCommand, _activeQueryArgument)}"
            : $"{SelectedCommand.Title}   ·   {SelectedCommand.Category}   ·   → 菜单";

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
        _windowBoundExtensionsService.Start(_appSettings.WindowBindings);
        if (_appSettings.EnableWindowSnapAssist)
        {
            _windowSnapAssistService.Start();
        }

        if (_cloudSyncClient == null)
        {
            StartMousePanelService();
            StartMouseGestureService();
            QueueBackgroundWebDavSync("startup");
            return;
        }

        StartMousePanelService();
        StartMouseGestureService();
        if (!AppSettingsStore.Load().RefreshCloudOnStartup)
        {
            StartMobileMessageBridge("startup-no-cloud-refresh");
            QueueBackgroundWebDavSync("startup");
            return;
        }

        await RefreshCloudStateAsync(allowLoginPrompt: false);
        if (_cloudSyncClient != null && _cloudSyncClient.HasCredential)
        {
            ScheduleSilentCloudReconnect("startup-post-refresh");
            StartMobileMessageBridge("startup-post-refresh");
        }
        StartStartupExtensions();

        // 异步预热快捷菜单和面板窗口以消除首次显示时的卡顿
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_radialMenu != null)
                {
                    var oLeft = _radialMenu.Left;
                    var oTop = _radialMenu.Top;
                    var oShowActivated = _radialMenu.ShowActivated;
                    var oShowInTaskbar = _radialMenu.ShowInTaskbar;
                    var oOpacity = _radialMenu.Opacity;

                    _radialMenu.Left = -10000;
                    _radialMenu.Top = -10000;
                    _radialMenu.ShowActivated = false;
                    _radialMenu.ShowInTaskbar = false;
                    _radialMenu.Opacity = 0;
                    _radialMenu.Show();
                    _radialMenu.Hide();

                    _radialMenu.Left = oLeft;
                    _radialMenu.Top = oTop;
                    _radialMenu.ShowActivated = oShowActivated;
                    _radialMenu.ShowInTaskbar = oShowInTaskbar;
                    _radialMenu.Opacity = oOpacity;
                }

                if (_quickPanel != null)
                {
                    var oLeft = _quickPanel.Left;
                    var oTop = _quickPanel.Top;
                    var oShowActivated = _quickPanel.ShowActivated;
                    var oShowInTaskbar = _quickPanel.ShowInTaskbar;
                    var oOpacity = _quickPanel.Opacity;

                    _quickPanel.Left = -10000;
                    _quickPanel.Top = -10000;
                    _quickPanel.ShowActivated = false;
                    _quickPanel.ShowInTaskbar = false;
                    _quickPanel.Opacity = 0;
                    _quickPanel.Show();
                    _quickPanel.Hide();

                    _quickPanel.Left = oLeft;
                    _quickPanel.Top = oTop;
                    _quickPanel.ShowActivated = oShowActivated;
                    _quickPanel.ShowInTaskbar = oShowInTaskbar;
                    _quickPanel.Opacity = oOpacity;
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Error warming up windows: {ex}");
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text);
        UpdateSearchInlineCompletion();
    }

    private void SearchBox_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        UpdateSearchBoxDropEffects(e);
        e.Handled = true;
    }

    private void SearchBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        UpdateSearchBoxDropEffects(e);
        e.Handled = true;
    }

    private async void SearchBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDroppedFilePaths(e, out var filePaths))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Copy;
        e.Handled = true;
        await AddAttachedFilesAsync(filePaths);
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySearchSelectionToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            FocusCommandList();
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
            TryHandleSearchScopeTabNavigation(e);
        }
        else if (e.Key == Key.Right && AcceptSearchInlineCompletion())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Right && CanOpenCommandMenuFromSearchBox())
        {
            OpenCommandActionsMenu(CommandActionsMenuOrigin.SearchBox);
            e.Handled = true;
        }
    }

    private bool CanOpenCommandMenuFromSearchBox()
    {
        if (FilteredCommands.Count == 0 ||
            SelectedCommand == null ||
            SearchBox.SelectionLength > 0 ||
            SearchBox.CaretIndex != SearchBox.Text.Length ||
            !string.IsNullOrWhiteSpace(SearchInlineCompletionSuffix))
        {
            return false;
        }

        return true;
    }

    private void CopySearchSelectionToClipboard()
    {
        var text = SearchBox.SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            ClipboardService.SetText(text);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Search box copy failed: {FormatExceptionMessage(ex)}");
            SyncStatus = $"复制搜索框内容失败：{FormatExceptionMessage(ex)}";
        }
    }

    private static bool TryGetDroppedFilePaths(System.Windows.DragEventArgs e, out string[] filePaths)
    {
        filePaths = [];
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0)
        {
            return false;
        }

        filePaths = droppedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return filePaths.Length > 0;
    }

    private void UpdateSearchBoxDropEffects(System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetDroppedFilePaths(e, out _) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
    }

    private static void UpdateExtensionCreationDropEffects(System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetDroppedFilePaths(e, out _) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
    }

    private void CreateExtensionsFromDroppedPaths(IEnumerable<string> filePaths)
    {
        var createdCommands = new List<CommandItem>();
        foreach (var filePath in filePaths.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var command = CreateQuickOpenExtensionFromPath(filePath);
                createdCommands.Add(command);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Create extension from dropped path failed: path={filePath}, error={FormatExceptionMessage(ex)}");
                SyncStatus = $"拖拽创建扩展失败：{Path.GetFileName(filePath)}，{FormatExceptionMessage(ex)}";
            }
        }

        if (createdCommands.Count == 0)
        {
            return;
        }

        foreach (var command in createdCommands)
        {
            MarkExtensionAsNewFromQuickPanel(command);
        }

        var latestCommand = createdCommands[^1];
        SelectedCommand = latestCommand;
        CommandList.SelectedItem = latestCommand;
        CommandList.ScrollIntoView(latestCommand);
        LastRunMessage = createdCommands.Count == 1
            ? $"已创建扩展：{latestCommand.Title}"
            : $"已创建 {createdCommands.Count} 个扩展。";
    }

    private Task AddAttachedFilesAsync(IEnumerable<string> filePaths)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var path in paths)
        {
            if (_attachedFiles.Any(item => item.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var item = new AttachedFileItem(path, Directory.Exists(path));
            _attachedFiles.Add(item);
            _ = LoadAttachedFileIconAsync(item);
        }

        LastRunMessage = $"已附加 {_attachedFiles.Count} 个文件。";
        return Task.CompletedTask;
    }

    private async Task LoadAttachedFileIconAsync(AttachedFileItem item)
    {
        var icon = await Task.Run(() => NativeFileIconService.GetIcon(item.FullPath, item.IsFolder));
        if (icon == null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() => item.SetIconSource(icon));
    }

    private void CopyAttachedFilePathButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AttachedFileItem item })
        {
            return;
        }

        try
        {
            ClipboardService.SetText(item.FullPath);
            LastRunMessage = $"已复制路径：{item.DisplayName}";
        }
        catch (Exception ex)
        {
            SyncStatus = $"复制文件路径失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void RemoveAttachedFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AttachedFileItem item })
        {
            return;
        }

        if (!_attachedFiles.Remove(item))
        {
            return;
        }

        LastRunMessage = _attachedFiles.Count == 0
            ? "已移除全部附件。"
            : $"已移除附件：{item.DisplayName}";
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

    private void RemovePinnedSearchScopeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchScopeTab scope })
        {
            RemovePinnedSearchScope(scope);
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    private void SearchScopeAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentCommandToPinnedSearchScopes();
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

    private void CommandList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryResolveCommandListItem(sender, e.OriginalSource as DependencyObject, out var command))
        {
            _commandListDragStartPoint = e.GetPosition(CommandList);
            _commandListDragSource = command;
        }
        else
        {
            _commandListDragStartPoint = null;
            _commandListDragSource = null;
        }
    }

    private void CommandList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TryResolveCommandListItem(sender, e.OriginalSource as DependencyObject, out var command))
        {
            MarkExtensionAsSeen(command);
        }
    }

    private void CommandList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TryStartCommandListDrag(e);
    }

    private void CommandListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommandList_PreviewMouseLeftButtonDown(sender, e);
    }

    private void CommandListItem_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TryStartCommandListDrag(e);
    }

    private void TryStartCommandListDrag(System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _commandListDragStartPoint == null ||
            _commandListDragSource == null)
        {
            return;
        }

        var currentPoint = e.GetPosition(CommandList);
        if (Math.Abs(currentPoint.X - _commandListDragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _commandListDragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var sourceCommand = _commandListDragSource;
        _commandListDragStartPoint = null;
        _commandListDragSource = null;

        var runnable = ResolveRunnableCommand(sourceCommand);
        if (runnable.IsFileSystemResult)
        {
            return;
        }

        var payload = new System.Windows.DataObject(typeof(CommandItem), runnable);
        WindowBindingDropOverlayWindow? bindingOverlay = null;
        if (runnable.Source is CommandSource.LocalExtension or CommandSource.Cloud)
        {
            bindingOverlay = new WindowBindingDropOverlayWindow(runnable, _appSettings.WindowBindings?.MarginPixels ?? 14);
            bindingOverlay.BindingDropped += (hwnd, corner, offsetX, offsetY) =>
            {
                _ = BindExtensionToWindowHandleAsync(runnable, hwnd, corner, restorePanel: false, offsetX, offsetY);
            };
            bindingOverlay.ShowFullDesktop();
        }

        try
        {
            DragDrop.DoDragDrop(CommandList, payload, System.Windows.DragDropEffects.Copy);
        }
        finally
        {
            if (bindingOverlay?.IsVisible == true)
            {
                bindingOverlay.Close();
            }
        }
    }

    private static bool TryResolveCommandListItem(object sender, DependencyObject? originalSource, out CommandItem? command)
    {
        if (sender is ListBoxItem { DataContext: CommandItem directCommand })
        {
            command = directCommand;
            return true;
        }

        var dependencyObject = originalSource;
        while (dependencyObject != null && dependencyObject is not ListBoxItem)
        {
            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        if (dependencyObject is ListBoxItem item && item.DataContext is CommandItem resolvedCommand)
        {
            command = resolvedCommand;
            return true;
        }

        command = null;
        return false;
    }

    private void CommandList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSelectedCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            OpenCommandActionsMenu(CommandActionsMenuOrigin.ResultsList);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
    }

    private void FocusCommandList()
    {
        if (FilteredCommands.Count == 0)
        {
            return;
        }

        CommandList.Focus();
        Keyboard.Focus(CommandList);
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
            if (command.IsFileSystemResult || command.IsProviderResult)
            {
                e.Handled = true;
            }
        }
    }

    private void CommandList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
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
            if (command.IsFileSystemResult)
            {
                e.Handled = true;
                ShowFileResultContextMenu(item);
                return;
            }

            if (command.IsProviderResult)
            {
                e.Handled = true;
                ShowGenericResultContextMenu(item);
            }
        }
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
        var command = GetCommandFromMenuItem(sender) ?? SelectedCommand;
        if (IsYanyuRuleCommand(command))
        {
            EditYanyuRuleForCommand(command);
            return;
        }

        if (command != null)
        {
            SelectedCommand = command;
            CommandList.SelectedItem = command;
        }

        await EditSelectedExtensionAsync();
    }

    private async void SetCommandShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SetSelectedExtensionShortcutAsync();
    }

    private async void DeleteExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = GetCommandFromMenuItem(sender) ?? SelectedCommand;
        if (IsYanyuRuleCommand(command))
        {
            DeleteYanyuRuleForCommand(command);
            return;
        }

        if (command != null)
        {
            SelectedCommand = command;
            CommandList.SelectedItem = command;
        }

        await DeleteSelectedExtensionAsync();
    }

    private void ToggleYanyuEnabledMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleYanyuRuleForCommand(GetCommandFromMenuItem(sender) ?? SelectedCommand);
    }

    private async void PublishExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var ok = await PublishSelectedExtensionAsync();
        if (!ok)
        {
            System.Windows.MessageBox.Show(
                this,
                SyncStatus,
                "发布到商店失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            SyncStatus,
            "发布到商店",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CopyExtensionStoreLinkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = GetCommandFromMenuItem(sender) ?? SelectedCommand;
        if (command == null)
        {
            return;
        }

        try
        {
            var result = CopyExtensionStoreLink(command.ExtensionId);
            SyncStatus = result.message;
        }
        catch (Exception ex)
        {
            SyncStatus = $"复制商店链接失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void OpenExtensionStoreLinkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var command = GetCommandFromMenuItem(sender) ?? SelectedCommand;
        if (command == null)
        {
            return;
        }

        try
        {
            var result = OpenExtensionStoreLink(command.ExtensionId);
            SyncStatus = result.message;
        }
        catch (Exception ex)
        {
            SyncStatus = $"打开商店链接失败：{FormatExceptionMessage(ex)}";
        }
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
            OpenCommandActionsMenu(SearchBox.IsKeyboardFocusWithin ? CommandActionsMenuOrigin.SearchBox : CommandActionsMenuOrigin.ResultsList);
            e.Handled = true;
        }
    }

    private void CommandListContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ContextMenu menu)
        {
            return;
        }

        UpdateFooterMenuHint(isMenuOpen: true);

        Dispatcher.BeginInvoke(() =>
        {
            var firstEnabledItem = menu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(static item => item.IsEnabled && item.Visibility == Visibility.Visible);
            firstEnabledItem?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommandListContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        UpdateFooterMenuHint(isMenuOpen: false);

        if (_commandActionsMenuOrigin == CommandActionsMenuOrigin.SearchBox)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            return;
        }

        CommandList.Focus();
    }

    private void CommandListContextMenu_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Left || e.Key == Key.Escape)
        {
            if (sender is System.Windows.Controls.ContextMenu menu)
            {
                menu.IsOpen = false;
            }

            if (_commandActionsMenuOrigin == CommandActionsMenuOrigin.SearchBox)
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
            else
            {
                CommandList.Focus();
            }

            e.Handled = true;
        }
    }

    private enum CommandActionsMenuOrigin
    {
        SearchBox,
        ResultsList
    }

    private void UpdateFooterMenuHint(bool isMenuOpen)
    {
        if (FooterMenuHintText == null || FooterMenuHintKeyText == null)
        {
            return;
        }

        FooterMenuHintText.Text = isMenuOpen ? "左箭头返回" : "右箭头菜单";
        FooterMenuHintKeyText.Text = isMenuOpen ? "←" : "→";
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

    private void FooterAddExtensionButton_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        UpdateExtensionCreationDropEffects(e);
        e.Handled = true;
    }

    private void FooterAddExtensionButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        UpdateExtensionCreationDropEffects(e);
        e.Handled = true;
    }

    private void FooterAddExtensionButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDroppedFilePaths(e, out var filePaths))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Copy;
        e.Handled = true;
        CreateExtensionsFromDroppedPaths(filePaths);
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
        if (SelectedCommand?.IsFileSystemResult == true || SelectedCommand?.IsProviderResult == true)
        {
            if (e != null)
            {
                e.Handled = true;
            }

            return;
        }

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

        if (resolved == null || resolved.IsFileSystemResult)
        {
            return false;
        }

        CreateDesktopShortcutMenuItem.IsEnabled = resolved.OpenTarget is { Length: > 0 } && !IsInternalCommand(resolved);
        var canManageLocalExtension = resolved.Source == CommandSource.LocalExtension;
        var isYanyuRule = IsYanyuRuleCommand(current);
        SetCommandShortcutMenuItem.IsEnabled = canManageLocalExtension;
        SetCommandShortcutMenuItem.Visibility = Visibility.Collapsed;
        RenameCommandMenuItem.IsEnabled = canManageLocalExtension;
        RenameCommandMenuItem.Visibility = Visibility.Collapsed;
        EditExtensionMenuItem.IsEnabled = canManageLocalExtension || isYanyuRule;
        EditExtensionMenuItem.Header = isYanyuRule ? "编辑燕语" : "编辑扩展";
        PublishExtensionMenuItem.IsEnabled = canManageLocalExtension && _cloudSyncClient != null;
        PublishExtensionMenuItem.Visibility = isYanyuRule ? Visibility.Collapsed : Visibility.Visible;
        CopyExtensionStoreLinkMenuItem.IsEnabled = canManageLocalExtension;
        CopyExtensionStoreLinkMenuItem.Visibility = Visibility.Collapsed;
        OpenExtensionStoreLinkMenuItem.IsEnabled = canManageLocalExtension;
        OpenExtensionStoreLinkMenuItem.Visibility = isYanyuRule ? Visibility.Collapsed : Visibility.Visible;
        DeleteExtensionMenuItem.IsEnabled = canManageLocalExtension || isYanyuRule;
        DeleteExtensionMenuItem.Header = isYanyuRule ? "删除燕语" : "删除";
        ToggleYanyuEnabledMenuItem.Visibility = isYanyuRule ? Visibility.Visible : Visibility.Collapsed;
        ToggleYanyuEnabledMenuItem.IsEnabled = isYanyuRule;
        ToggleYanyuEnabledMenuItem.Header = isYanyuRule && IsYanyuRuleEnabled(current) ? "停用燕语" : "启用燕语";
        CopyExtensionMenuItem.IsEnabled = true;
        CutExtensionMenuItem.IsEnabled = true;
        PasteExtensionMenuItem.IsEnabled = true;
        SetCommandContextMenuCommand(current);
        return true;
    }

    private void SetCommandContextMenuCommand(CommandItem? command)
    {
        CreateDesktopShortcutMenuItem.CommandParameter = command;
        SetCommandShortcutMenuItem.CommandParameter = command;
        RenameCommandMenuItem.CommandParameter = command;
        EditExtensionMenuItem.CommandParameter = command;
        PublishExtensionMenuItem.CommandParameter = command;
        CopyExtensionStoreLinkMenuItem.CommandParameter = command;
        OpenExtensionStoreLinkMenuItem.CommandParameter = command;
        DeleteExtensionMenuItem.CommandParameter = command;
        ToggleYanyuEnabledMenuItem.CommandParameter = command;
        CopyExtensionMenuItem.CommandParameter = command;
        CutExtensionMenuItem.CommandParameter = command;
        PasteExtensionMenuItem.CommandParameter = command;
        AddToQuickPanelMenuItem.CommandParameter = command;
    }

    private static CommandItem? GetCommandFromMenuItem(object? sender)
    {
        return sender is MenuItem { CommandParameter: CommandItem command } ? command : null;
    }

    private async Task BindExtensionToForegroundWindowAsync(CommandItem command, string corner)
    {
        var launcherHandle = new WindowInteropHelper(this).Handle;
        var startedAt = DateTimeOffset.Now;

        try
        {
            LastRunMessage = "窗口绑定：请切换到目标窗口（10 秒内）。";
            SyncStatus = "窗口绑定：请切换到目标窗口（10 秒内）。";
            WindowState = WindowState.Minimized;
            await Task.Delay(120);

            IntPtr target = IntPtr.Zero;
            while (DateTimeOffset.Now - startedAt < TimeSpan.FromSeconds(10))
            {
                target = GetForegroundWindow();
                if (target != IntPtr.Zero &&
                    target != launcherHandle &&
                    IsWindowVisible(target) &&
                    !IsIconic(target) &&
                    TryGetWindowProcessId(target, out var pid) &&
                    pid != 0 &&
                    pid != (uint)Environment.ProcessId)
                {
                    break;
                }

                target = IntPtr.Zero;
                await Task.Delay(100);
            }

            if (target == IntPtr.Zero)
            {
                LastRunMessage = "窗口绑定：超时，未检测到目标窗口。";
                SyncStatus = "窗口绑定：超时，未检测到目标窗口。";
                return;
            }

            await BindExtensionToWindowHandleAsync(command, target, corner, restorePanel: false);
        }
        catch (Exception ex)
        {
            LastRunMessage = $"窗口绑定失败：{FormatExceptionMessage(ex)}";
            SyncStatus = $"窗口绑定失败：{FormatExceptionMessage(ex)}";
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                WindowState = WindowState.Normal;
                ShowPanel();
            });
        }
    }

    private Task BindExtensionToWindowHandleAsync(
        CommandItem command,
        IntPtr target,
        string corner,
        bool restorePanel,
        int offsetX = 0,
        int offsetY = 0)
    {
        try
        {
            var processName = GetProcessName(target);
            var windowClass = GetWindowClassName(target);
            var windowTitle = GetWindowTitle(target);

            if (string.IsNullOrWhiteSpace(processName))
            {
                LastRunMessage = "窗口绑定：读取窗口进程失败。";
                SyncStatus = "窗口绑定：读取窗口进程失败。";
                return Task.CompletedTask;
            }

            var settings = AppSettingsStore.Load();
            settings.WindowBindings ??= new WindowBindingSettings();
            settings.WindowBindings.Rules ??= [];
            settings.WindowBindings.Enabled = true;
            settings.WindowBindings.Rules.RemoveAll(rule =>
                rule.Enabled &&
                rule.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
                rule.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) &&
                rule.WindowClass.Equals(windowClass, StringComparison.OrdinalIgnoreCase) &&
                WindowBindingCorners.Normalize(rule.Corner) == corner);

            settings.WindowBindings.Rules.Add(new WindowBindingRuleSettings
            {
                Enabled = true,
                ExtensionId = command.ExtensionId,
                ProcessName = processName,
                WindowClass = windowClass,
                TitleContains = string.Empty,
                Corner = WindowBindingCorners.Normalize(corner),
                OffsetX = offsetX,
                OffsetY = offsetY
            });

            AppSettingsStore.Save(settings);
            _appSettings = AppSettingsStore.Load();
            _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
            _windowBoundExtensionsService.RefreshForWindow(target);

            var cornerText = ToWindowBindingCornerText(corner);
            LastRunMessage = $"窗口绑定成功：{command.Title}（{processName} · {cornerText}）";
            SyncStatus = $"窗口绑定成功：{command.Title}（{processName}）";
            HostAssets.AppendLog($"Window binding saved: extension={command.ExtensionId}, process={processName}, class={windowClass}, title={windowTitle}, corner={corner}.");

            if (restorePanel)
            {
                ShowPanel();
            }
        }
        catch (Exception ex)
        {
            LastRunMessage = $"窗口绑定失败：{FormatExceptionMessage(ex)}";
            SyncStatus = $"窗口绑定失败：{FormatExceptionMessage(ex)}";
            HostAssets.AppendLog($"Window binding save failed: extension={command.ExtensionId}, error={ex}");
        }

        return Task.CompletedTask;
    }

    public Task BindExtensionToWindowFromDropAsync(
        CommandItem command,
        IntPtr target,
        string corner,
        int offsetX = 0,
        int offsetY = 0)
    {
        return BindExtensionToWindowHandleAsync(command, target, corner, restorePanel: false, offsetX, offsetY);
    }

    public int GetWindowBindingMarginPixels() => _appSettings.WindowBindings?.MarginPixels ?? 14;

    public void ShowWindowBindingContextMenu(CommandItem command, string bindingRuleId, WindowBoundExtensionOverlayWindow placementTarget)
    {
        SelectCommandForExtensionAction(command);

        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        AddMenuItem(menu, "编辑扩展", "pen", async () => await EditSelectedExtensionAsync(), command.Source == CommandSource.LocalExtension);
        AddMenuItem(menu, "发布到商店", "publish", async () =>
        {
            var ok = await PublishSelectedExtensionAsync();
            if (!ok)
            {
                SyncStatus = string.IsNullOrWhiteSpace(SyncStatus) ? "发布到商店失败。" : SyncStatus;
            }
        }, command.Source == CommandSource.LocalExtension && _cloudSyncClient != null);
        AddMenuItem(menu, "打开商店链接", "link", () => OpenExtensionStoreLinkMenuItem_Click(CreateMenuSender(command), new RoutedEventArgs()), command.Source == CommandSource.LocalExtension);
        AddMenuItem(menu, "删除", "delete", async () => await DeleteSelectedExtensionAsync(), command.Source == CommandSource.LocalExtension);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "复制扩展", "copy", () => CopyExtensionMenuItem_Click(CreateMenuSender(command), new RoutedEventArgs()), true);
        AddMenuItem(menu, "剪切扩展", "cut", () => CutExtensionMenuItem_Click(CreateMenuSender(command), new RoutedEventArgs()), true);
        AddMenuItem(menu, "粘贴扩展", "paste", () => PasteExtensionMenuItem_Click(CreateMenuSender(command), new RoutedEventArgs()), true);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "添加到鼠标面板", "plus", () => AddCurrentCommandToQuickPanel(), true);
        menu.Items.Add(new Separator());
        var hoverModeEnabled = IsWindowBindingHoverMode(bindingRuleId);
        AddMenuItem(menu, hoverModeEnabled ? "始终显示" : "悬停时显示", "pin", () => ToggleWindowBindingHoverMode(bindingRuleId), true);
        AddMenuItem(menu, "取消窗口绑定", "delete", () => RemoveWindowBinding(bindingRuleId, command.Title), true);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "添加桌面快捷方式", "desktop-shortcut", async () => await CreateDesktopShortcutAsync(), command.OpenTarget is { Length: > 0 } && !IsInternalCommand(command));

        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuSender(CommandItem command) => new() { CommandParameter = command };

    private void AddMenuItem(ContextMenu menu, string header, string iconReference, Action action, bool isEnabled)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled,
            Icon = CreateMenuIcon(iconReference)
        };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static object? CreateMenuIcon(string iconReference)
    {
        var geometry = ExtensionIconLibrary.ResolveVectorIcon($"mdi:{iconReference}");
        if (geometry == null)
        {
            return null;
        }

        return new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 174, 192)),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform
        };
    }

    private void SelectCommandForExtensionAction(CommandItem command)
    {
        SelectedCommand = command;
        CommandList.SelectedItem = command;
        _lastActionableCommand = command;
    }

    public void UpdateWindowBindingOffset(string ruleId, int offsetX, int offsetY)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var rule = settings.WindowBindings?.Rules?.FirstOrDefault(item => item.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule == null)
        {
            return;
        }

        rule.OffsetX = RoundToGrid(offsetX, 10);
        rule.OffsetY = RoundToGrid(offsetY, 10);
        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        LastRunMessage = $"已移动窗口绑定位置：{rule.OffsetX}, {rule.OffsetY}";
    }

    private void RemoveWindowBinding(string ruleId, string commandTitle)
    {
        var settings = AppSettingsStore.Load();
        var removed = settings.WindowBindings?.Rules?.RemoveAll(rule => rule.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase)) ?? 0;
        if (removed <= 0)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        LastRunMessage = $"已取消窗口绑定：{commandTitle}";
        SyncStatus = $"已取消窗口绑定：{commandTitle}";
    }

    private bool IsWindowBindingHoverMode(string ruleId)
    {
        var rule = _appSettings.WindowBindings?.Rules?.FirstOrDefault(r => r.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        return rule?.HoverMode ?? false;
    }

    private void ToggleWindowBindingHoverMode(string ruleId)
    {
        var settings = AppSettingsStore.Load();
        var rule = settings.WindowBindings?.Rules?.FirstOrDefault(r => r.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule == null)
        {
            return;
        }

        rule.HoverMode = !rule.HoverMode;
        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        SyncStatus = rule.HoverMode ? "已设为悬停时显示" : "已设为始终显示";
    }

    private static int RoundToGrid(int value, int gridSize)
    {
        return (int)Math.Round(value / (double)gridSize, MidpointRounding.AwayFromZero) * gridSize;
    }

    private static string ToWindowBindingCornerText(string corner)
    {
        return WindowBindingCorners.Normalize(corner) switch
        {
            WindowBindingCorners.TopRight => "右上",
            WindowBindingCorners.BottomLeft => "左下",
            WindowBindingCorners.BottomRight => "右下",
            WindowBindingCorners.InsideTopLeft => "内左上",
            WindowBindingCorners.InsideTopRight => "内右上",
            WindowBindingCorners.InsideBottomLeft => "内左下",
            WindowBindingCorners.InsideBottomRight => "内右下",
            _ => "左上"
        };
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        try
        {
            var builder = new StringBuilder(256);
            _ = GetClassName(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var builder = new StringBuilder(1024);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            if (!TryGetWindowProcessId(hwnd, out var pid) || pid == 0)
            {
                return string.Empty;
            }

            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryGetWindowProcessId(IntPtr hwnd, out uint pid)
    {
        pid = 0;
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out pid);
            return true;
        }
        catch
        {
            pid = 0;
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);


    private async void RunSelectedCommand()
    {
        if (SelectedCommand == null)
        {
            LastRunMessage = "没有可执行的命令。";
            return;
        }

        MarkExtensionAsSeen(SelectedCommand);
        var runnable = ResolveRunnableCommand(SelectedCommand);
        var explicitInput = runnable.SupportsQueryArgument && !string.IsNullOrWhiteSpace(_activeQueryArgument)
            ? _activeQueryArgument
            : null;
        await ExecuteCommandAsync(runnable, explicitInput);
    }

    private async Task ExecuteCommandAsync(CommandItem runnable, string? explicitInput = null, string launchSource = "launcher")
    {
        var hasExternalInput = !string.IsNullOrWhiteSpace(explicitInput);
        if (runnable.App != null)
        {
            RecordCommandUsage(runnable);
            if (AppExtensionWindow.TryActivateExisting(runnable))
            {
                HostAssets.AppendRecent(runnable.Title);
                LastRunMessage = $"已激活应用扩展：{runnable.Title}";
                return;
            }

            var window = new AppExtensionWindow(runnable, explicitInput, launchSource)
            {
                ShowInTaskbar = true
            };
            window.Show();
            HostAssets.AppendRecent(runnable.Title);
            LastRunMessage = $"已打开应用扩展：{runnable.Title}";
            return;
        }

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

        if (TryExecuteSimulatedKeystroke(runnable))
        {
            return;
        }

        if (!runnable.IsProviderResult && runnable.SearchProvider != null)
        {
            OpenSearchProviderInLauncher(runnable, explicitInput);
            return;
        }

        if (runnable.IsProviderResult && !runnable.IsFileSystemResult)
        {
            await ExecuteGenericResultAsync(runnable);
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
                    Arguments = runnable.LaunchArguments ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(runnable.WorkingDirectory) ? string.Empty : runnable.WorkingDirectory,
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



    private static List<CommandItem> CreateSeedCommands()
    {
        return
        [
            CreateSystemCommand("设", "系统设置", "打开 Windows 设置首页。", "#FF3B82F6", "ms-settings:", "system-settings-home", "mdi:settings", ["系统", "设置", "windows", "preferences"]),
            CreateSystemCommand("态", "网络状态", "打开网络连接状态总览。", "#FF38BDF8", "ms-settings:network-status", "system-network-status", "mdi:globe", ["系统", "网络", "状态", "internet"]),
            CreateSystemCommand("高", "网络高级设置", "打开高级网络适配器与共享设置。", "#FF0EA5E9", "ms-settings:network-advancedsettings", "system-network-advanced", "mdi:globe", ["系统", "网络", "高级设置", "适配器", "网卡"]),
            CreateSystemCommand("无", "Wi‑Fi", "打开 Wi‑Fi 设置。", "#FF06B6D4", "ms-settings:network-wifi", "system-network-wifi", "mdi:globe", ["系统", "wifi", "无线网络", "网络"]),
            CreateSystemCommand("已", "已知网络", "管理已保存的 Wi‑Fi 网络。", "#FF0891B2", "ms-settings:network-wifisettings", "system-network-known-wifi", "mdi:globe", ["系统", "wifi", "已知网络", "保存的网络"]),
            CreateSystemCommand("以", "以太网", "打开有线网络设置。", "#FF0284C7", "ms-settings:network-ethernet", "system-network-ethernet", "mdi:globe", ["系统", "以太网", "有线网络", "网线"]),
            CreateSystemCommand("代", "代理", "打开代理服务器设置。", "#FF0369A1", "ms-settings:network-proxy", "system-network-proxy", "mdi:globe", ["系统", "代理", "proxy", "翻墙"]),
            CreateSystemCommand("虚", "VPN", "打开 VPN 连接设置。", "#FF2563EB", "ms-settings:network-vpn", "system-network-vpn", "mdi:globe", ["系统", "vpn", "虚拟专用网络"]),
            CreateSystemCommand("热", "移动热点", "打开热点共享设置。", "#FF0F766E", "ms-settings:network-mobilehotspot", "system-network-hotspot", "mdi:globe", ["系统", "热点", "共享网络", "mobile hotspot"]),
            CreateSystemCommand("飞", "飞行模式", "打开飞行模式设置。", "#FF64748B", "ms-settings:network-airplanemode", "system-network-airplane", "mdi:globe", ["系统", "飞行模式", "无线关闭"]),
            CreateSystemCommand("网", "网络和 Internet", "打开网络设置。", "#FF0EA5E9", "ms-settings:network", "system-settings-network", "mdi:globe", ["系统", "设置", "网络", "wifi", "代理"]),
            CreateSystemCommand("蓝", "蓝牙和设备", "打开蓝牙与设备设置。", "#FF8B5CF6", "ms-settings:bluetooth", "system-settings-bluetooth", "mdi:settings", ["系统", "设置", "蓝牙", "设备", "鼠标", "键盘"]),
            CreateSystemCommand("连", "已连接设备", "打开已连接设备管理。", "#FF7C3AED", "ms-settings:connecteddevices", "system-connected-devices", "mdi:settings", ["系统", "设备", "已连接设备", "蓝牙设备"]),
            CreateSystemCommand("鼠", "鼠标和触摸板", "打开鼠标和基础触控设置。", "#FF8B5CF6", "ms-settings:mousetouchpad", "system-mouse-touchpad", "mdi:settings", ["系统", "鼠标", "触摸板", "滚轮"]),
            CreateSystemCommand("触", "触摸板", "打开触摸板手势和灵敏度设置。", "#FF6D28D9", "ms-settings:devices-touchpad", "system-touchpad", "mdi:settings", ["系统", "触摸板", "手势"]),
            CreateSystemCommand("相", "相机设置", "打开摄像头设备设置。", "#FF0F766E", "ms-settings:camera", "system-camera-settings", "mdi:settings", ["系统", "摄像头", "相机", "camera"]),
            CreateSystemCommand("打", "打印机和扫描仪", "打开打印机、扫描仪和设备管理。", "#FF6366F1", "ms-settings:printers", "system-settings-printers", "mdi:file", ["系统", "设置", "打印机", "扫描仪", "设备", "打印"]),
            CreateSystemCommand("U", "USB", "打开 USB 设备相关设置。", "#FF475569", "ms-settings:usb", "system-usb", "mdi:settings", ["系统", "usb", "设备"]),
            CreateSystemCommand("自", "自动播放", "打开 AutoPlay 自动播放设置。", "#FF334155", "ms-settings:autoplay", "system-autoplay", "mdi:settings", ["系统", "自动播放", "u盘", "cd"]),
            CreateSystemCommand("声", "声音设置", "打开扬声器、耳机和麦克风设置。", "#FF06B6D4", "ms-settings:sound", "system-settings-sound", "mdi:settings", ["系统", "设置", "声音", "音量", "耳机", "扬声器", "麦克风", "音频输出"]),
            CreateSystemCommand("输", "默认输出设备", "打开默认扬声器或耳机属性。", "#FF0284C7", "ms-settings:sound-defaultoutputproperties", "system-settings-default-output", "mdi:settings", ["系统", "设置", "默认输出", "扬声器", "耳机", "输出设备", "音频输出"]),
            CreateSystemCommand("入", "默认输入设备", "打开默认麦克风属性。", "#FF0F766E", "ms-settings:sound-defaultinputproperties", "system-settings-default-input", "mdi:settings", ["系统", "设置", "默认输入", "麦克风", "输入设备", "录音"]),
            CreateSystemCommand("设", "声音设备", "打开全部播放和录音设备列表。", "#FF0369A1", "ms-settings:sound-devices", "system-settings-sound-devices", "mdi:settings", ["系统", "设置", "声音设备", "耳机", "扬声器", "麦克风", "播放设备", "录音设备"]),
            CreateSystemCommand("混", "音量混合器", "打开应用音量与设备首选项。", "#FF0891B2", "ms-settings:apps-volume", "system-settings-apps-volume", "mdi:settings", ["系统", "设置", "音量混合器", "应用音量", "耳机", "扬声器"]),
            CreateSystemCommand("辅", "辅助功能音频", "打开单声道音频和辅助听觉设置。", "#FF14B8A6", "ms-settings:easeofaccess-audio", "system-settings-accessibility-audio", "mdi:settings", ["系统", "设置", "辅助功能", "音频", "单声道", "听觉"]),
            CreateSystemCommand("显", "显示设置", "打开显示、缩放与分辨率设置。", "#FF22C55E", "ms-settings:display", "system-settings-display", "mdi:window", ["系统", "设置", "显示", "分辨率", "缩放", "屏幕"]),
            CreateSystemCommand("高", "高级显示", "打开刷新率、色彩和高级显示属性。", "#16A34A", "ms-settings:display-advanced", "system-display-advanced", "mdi:window", ["系统", "显示", "高级显示", "刷新率", "hdr"]),
            CreateSystemCommand("夜", "夜间模式", "打开夜间灯光设置。", "#65A30D", "ms-settings:nightlight", "system-night-light", "mdi:window", ["系统", "夜间模式", "夜灯", "护眼"]),
            CreateSystemCommand("图", "图形设置", "打开 GPU 和图形性能偏好。", "#22C55E", "ms-settings:display-advancedgraphics", "system-graphics-settings", "mdi:window", ["系统", "图形设置", "gpu", "显卡", "游戏"]),
            CreateSystemCommand("电", "电源和电池", "打开电源、睡眠和电池设置。", "#FF84CC16", "ms-settings:powersleep", "system-settings-power", "mdi:pin", ["系统", "设置", "电源", "电池", "睡眠", "省电"]),
            CreateSystemCommand("省", "节电模式", "打开电池节省器设置。", "#84CC16", "ms-settings:batterysaver", "system-battery-saver", "mdi:pin", ["系统", "节电", "电池节省", "省电"]),
            CreateSystemCommand("储", "存储设置", "打开磁盘存储与清理建议。", "#FF10B981", "ms-settings:storagesense", "system-settings-storage", "mdi:file", ["系统", "设置", "存储", "磁盘", "空间", "清理"]),
            CreateSystemCommand("建", "存储感知", "打开自动清理与存储感知策略。", "#059669", "ms-settings:storagepolicies", "system-storage-sense", "mdi:file", ["系统", "存储感知", "自动清理", "磁盘空间"]),
            CreateSystemCommand("卷", "磁盘和卷", "打开磁盘与卷管理设置。", "#047857", "ms-settings:disksandvolumes", "system-disks-volumes", "mdi:file", ["系统", "磁盘", "卷", "分区"]),
            CreateSystemCommand("剪", "剪贴板", "打开剪贴板历史和同步设置。", "#0F766E", "ms-settings:clipboard", "system-clipboard", "mdi:file", ["系统", "剪贴板", "复制粘贴", "剪贴板历史"]),
            CreateSystemCommand("通", "通知", "打开通知与提醒设置。", "#2563EB", "ms-settings:notifications", "system-notifications", "mdi:settings", ["系统", "通知", "提醒", "弹窗"]),
            CreateSystemCommand("专", "专注助手", "打开专注模式与勿扰设置。", "#1D4ED8", "ms-settings:quiethours", "system-focus-assist", "mdi:settings", ["系统", "专注助手", "勿扰", "专注模式"]),
            CreateSystemCommand("多", "多任务处理", "打开窗口贴靠和多任务设置。", "#4F46E5", "ms-settings:multitasking", "system-multitasking", "mdi:window", ["系统", "多任务", "分屏", "贴靠窗口"]),
            CreateSystemCommand("投", "投影到此电脑", "打开无线投影设置。", "#7C3AED", "ms-settings:project", "system-project-to-pc", "mdi:window", ["系统", "投影", "投屏", "无线显示"]),
            CreateSystemCommand("远", "远程桌面", "打开远程桌面设置。", "#9333EA", "ms-settings:remotedesktop", "system-remote-desktop", "mdi:window", ["系统", "远程桌面", "rdp"]),
            CreateSystemCommand("关", "关于本机", "打开设备规格和系统版本页。", "#64748B", "ms-settings:about", "system-about", "mdi:settings", ["系统", "关于", "版本", "设备规格"]),
            CreateSystemCommand("个", "个性化", "打开主题、壁纸和颜色设置。", "#FFEC4899", "ms-settings:personalization", "system-settings-personalization", "mdi:dashboard", ["系统", "设置", "个性化", "壁纸", "主题", "颜色"]),
            CreateSystemCommand("背", "背景", "打开桌面背景设置。", "#EC4899", "ms-settings:personalization-background", "system-background", "mdi:dashboard", ["系统", "背景", "壁纸", "桌面"]),
            CreateSystemCommand("色", "颜色", "打开主题色和深浅模式设置。", "#DB2777", "ms-settings:personalization-colors", "system-colors", "mdi:dashboard", ["系统", "颜色", "主题色", "深色模式"]),
            CreateSystemCommand("主", "主题", "打开主题设置。", "#BE185D", "ms-settings:themes", "system-themes", "mdi:dashboard", ["系统", "主题", "皮肤"]),
            CreateSystemCommand("锁", "锁屏", "打开锁屏背景与状态设置。", "#9D174D", "ms-settings:lockscreen", "system-lockscreen", "mdi:dashboard", ["系统", "锁屏", "锁屏壁纸"]),
            CreateSystemCommand("栏", "任务栏", "打开任务栏行为和图标设置。", "#A21CAF", "ms-settings:taskbar", "system-taskbar-settings", "mdi:dashboard", ["系统", "任务栏", "开始菜单", "托盘"]),
            CreateSystemCommand("字", "字体", "打开字体管理。", "#C026D3", "ms-settings:fonts", "system-fonts", "mdi:dashboard", ["系统", "字体", "font"]),
            CreateSystemCommand("始", "开始菜单", "打开开始菜单布局设置。", "#D946EF", "ms-settings:personalization-start", "system-start-menu", "mdi:dashboard", ["系统", "开始菜单", "推荐", "固定应用"]),
            CreateSystemCommand("更", "Windows 更新", "打开更新与安全检查。", "#FF2563EB", "ms-settings:windowsupdate", "system-settings-windows-update", "mdi:refresh", ["系统", "设置", "更新", "windows update", "补丁"]),
            CreateSystemCommand("历", "更新历史", "打开 Windows 更新历史记录。", "#1D4ED8", "ms-settings:windowsupdate-history", "system-windows-update-history", "mdi:refresh", ["系统", "更新历史", "补丁记录"]),
            CreateSystemCommand("选", "更新高级选项", "打开 Windows 更新高级选项。", "#1E40AF", "ms-settings:windowsupdate-options", "system-windows-update-options", "mdi:refresh", ["系统", "更新", "高级选项"]),
            CreateSystemCommand("可", "可选更新", "打开驱动和可选更新页。", "#1E3A8A", "ms-settings:windowsupdate-optionalupdates", "system-windows-update-optional", "mdi:refresh", ["系统", "可选更新", "驱动更新"]),
            CreateSystemCommand("安", "Windows 安全中心", "打开安全中心总览。", "#DC2626", "ms-settings:windowsdefender", "system-windows-security", "mdi:settings", ["系统", "安全中心", "defender", "杀毒"]),
            CreateSystemCommand("恢", "恢复", "打开重置此电脑和恢复选项。", "#B91C1C", "ms-settings:recovery", "system-recovery", "mdi:settings", ["系统", "恢复", "重置此电脑", "高级启动"]),
            CreateSystemCommand("疑", "疑难解答", "打开自动修复与故障排除。", "#991B1B", "ms-settings:troubleshoot", "system-troubleshoot", "mdi:settings", ["系统", "疑难解答", "故障排除"]),
            CreateSystemCommand("激", "激活", "打开 Windows 激活状态。", "#7F1D1D", "ms-settings:activation", "system-activation", "mdi:settings", ["系统", "激活", "许可证"]),
            CreateSystemCommand("开", "开发者选项", "打开开发者模式和调试设置。", "#7C2D12", "ms-settings:developers", "system-developers", "mdi:settings", ["系统", "开发者", "开发者选项", "调试"]),
            CreateSystemCommand("隐", "麦克风权限", "打开麦克风隐私与访问权限。", "#FF14B8A6", "ms-settings:privacy-microphone", "system-settings-microphone-privacy", "mdi:settings", ["系统", "设置", "麦克风", "权限", "隐私", "耳机"]),
            CreateSystemCommand("摄", "摄像头权限", "打开摄像头隐私与访问权限。", "#FF0F766E", "ms-settings:privacy-webcam", "system-settings-webcam-privacy", "mdi:settings", ["系统", "设置", "摄像头", "相机", "权限", "隐私"]),
            CreateSystemCommand("位", "位置权限", "打开定位隐私与访问权限。", "#0F766E", "ms-settings:privacy-location", "system-privacy-location", "mdi:settings", ["系统", "位置", "定位", "隐私"]),
            CreateSystemCommand("图", "图片权限", "打开图片库访问权限。", "#0D9488", "ms-settings:privacy-pictures", "system-privacy-pictures", "mdi:settings", ["系统", "图片", "相册", "隐私"]),
            CreateSystemCommand("文", "文档权限", "打开文档访问权限。", "#14B8A6", "ms-settings:privacy-documents", "system-privacy-documents", "mdi:settings", ["系统", "文档", "隐私", "权限"]),
            CreateSystemCommand("下", "下载文件夹权限", "打开下载文件夹访问权限。", "#2DD4BF", "ms-settings:privacy-downloadsfolder", "system-privacy-downloads", "mdi:settings", ["系统", "下载", "文件夹权限", "隐私"]),
            CreateSystemCommand("通", "通知权限", "打开应用通知访问权限。", "#5EEAD4", "ms-settings:privacy-notifications", "system-privacy-notifications", "mdi:settings", ["系统", "通知权限", "隐私", "通知"]),
            CreateSystemCommand("文", "文件系统权限", "打开文件系统广泛访问权限。", "#99F6E4", "ms-settings:privacy-broadfilesystemaccess", "system-privacy-filesystem", "mdi:settings", ["系统", "文件系统", "权限", "隐私"]),
            CreateSystemCommand("账", "你的信息", "打开 Microsoft 账户和本机身份信息。", "#F59E0B", "ms-settings:yourinfo", "system-account-yourinfo", "mdi:settings", ["系统", "账户", "你的信息", "头像"]),
            CreateSystemCommand("登", "登录选项", "打开密码、PIN、Windows Hello 设置。", "#D97706", "ms-settings:signinoptions", "system-account-signin", "mdi:settings", ["系统", "登录选项", "pin", "密码", "hello"]),
            CreateSystemCommand("邮", "电子邮件和账户", "打开邮件与应用账户管理。", "#B45309", "ms-settings:emailandaccounts", "system-account-email", "mdi:settings", ["系统", "邮箱", "账户", "邮件账户"]),
            CreateSystemCommand("其", "其他用户", "打开家庭与其他用户管理。", "#92400E", "ms-settings:otherusers", "system-account-otherusers", "mdi:settings", ["系统", "其他用户", "家庭成员", "子账户"]),
            CreateSystemCommand("接", "访问工作或学校", "打开组织账户连接设置。", "#78350F", "ms-settings:workplace", "system-account-workplace", "mdi:settings", ["系统", "工作或学校账户", "组织账户"]),
            CreateSystemCommand("同", "同步设置", "打开 Windows 设置同步页。", "#A16207", "ms-settings:sync", "system-account-sync", "mdi:settings", ["系统", "同步", "设置同步"]),
            CreateSystemCommand("默", "默认应用", "打开默认浏览器和默认应用设置。", "#FFF59E0B", "ms-settings:defaultapps", "system-settings-default-apps", "mdi:file", ["系统", "设置", "默认应用", "默认浏览器", "文件关联"]),
            CreateSystemCommand("应", "应用和功能", "打开已安装应用管理。", "#FFF97316", "ms-settings:appsfeatures", "system-settings-appsfeatures", "mdi:file", ["系统", "设置", "应用", "卸载", "程序"]),
            CreateSystemCommand("网", "网站关联应用", "打开“应用处理网站链接”设置。", "#FACC15", "ms-settings:appsforwebsites", "system-apps-for-websites", "mdi:file", ["系统", "网站关联", "默认打开方式", "应用"]),
            CreateSystemCommand("可", "可选功能", "打开 Windows 可选功能管理。", "#EAB308", "ms-settings:optionalfeatures", "system-optional-features", "mdi:file", ["系统", "可选功能", "windows 功能"]),
            CreateSystemCommand("视", "视频播放", "打开 HDR 和视频增强设置。", "#CA8A04", "ms-settings:videoplayback", "system-video-playback", "mdi:file", ["系统", "视频播放", "hdr", "视频增强"]),
            CreateSystemCommand("地", "离线地图", "打开离线地图管理。", "#A16207", "ms-settings:maps", "system-offline-maps", "mdi:file", ["系统", "离线地图", "地图"]),
            CreateSystemCommand("启", "启动应用", "打开 Windows 启动项管理。", "#FFEAB308", "ms-settings:startupapps", "system-settings-startupapps", "mdi:pin", ["系统", "设置", "启动", "开机启动"]),
            CreateSystemCommand("日", "日期和时间", "打开日期、时间和时区设置。", "#F97316", "ms-settings:dateandtime", "system-date-time", "mdi:settings", ["系统", "日期", "时间", "时区"]),
            CreateSystemCommand("语", "语言和区域", "打开语言包与区域设置。", "#FB923C", "ms-settings:regionlanguage", "system-language-region", "mdi:settings", ["系统", "语言", "区域", "输入法"]),
            CreateSystemCommand("格", "区域格式", "打开日期数字和地区格式。", "#FDBA74", "ms-settings:regionformatting", "system-region-format", "mdi:settings", ["系统", "区域格式", "日期格式", "数字格式"]),
            CreateSystemCommand("键", "键盘", "打开键盘布局和语言设置。", "#EA580C", "ms-settings:keyboard", "system-keyboard", "mdi:settings", ["系统", "键盘", "输入法"]),
            CreateSystemCommand("高", "高级键盘设置", "打开默认输入法和语言栏设置。", "#C2410C", "ms-settings:keyboard-advanced", "system-keyboard-advanced", "mdi:settings", ["系统", "高级键盘", "默认输入法", "语言栏"]),
            CreateSystemCommand("讲", "语音", "打开语音识别与语音语言设置。", "#9A3412", "ms-settings:speech", "system-speech", "mdi:settings", ["系统", "语音", "语音识别"]),
            CreateSystemCommand("控", "控制面板", "打开经典控制面板。", "#FF64748B", "control.exe", "system-control-panel", "mdi:settings", ["系统", "控制面板", "传统设置"]),
            CreateSystemCommand("任", "任务管理器", "打开任务管理器。", "#FFEF4444", "taskmgr.exe", "system-task-manager", "mdi:window", ["系统", "任务管理器", "进程", "性能"]),
            CreateSystemCommand("设", "设备管理器", "打开设备管理器。", "#FF475569", "devmgmt.msc", "system-device-manager", "mdi:settings", ["系统", "设备管理器", "驱动", "硬件", "蓝牙", "耳机"]),
            CreateSystemCommand("服", "服务", "打开 Windows 服务管理。", "#FF334155", "services.msc", "system-services", "mdi:task", ["系统", "服务", "windows 服务", "service"]),
            CreateSystemCommand("盘", "磁盘管理", "打开磁盘分区与卷管理。", "#FF0F766E", "diskmgmt.msc", "system-disk-management", "mdi:file", ["系统", "磁盘管理", "分区", "硬盘", "卷"]),
            CreateSystemCommand("事", "事件查看器", "打开系统日志与事件查看器。", "#FF7C3AED", "eventvwr.msc", "system-event-viewer", "mdi:dashboard", ["系统", "事件查看器", "日志", "报错"]),
            CreateSystemCommand("注", "注册表编辑器", "打开注册表编辑器。", "#FFB91C1C", "regedit.exe", "system-registry-editor", "mdi:pen", ["系统", "注册表", "regedit"]),
            CreateSystemCommand("环", "环境变量", "打开环境变量编辑窗口。", "#FF9333EA", "rundll32.exe", "system-environment-variables", "mdi:settings", ["系统", "环境变量", "path", "java", "python"], launchArguments: "sysdm.cpl,EditEnvironmentVariables"),
            CreateSystemCommand("高", "高级系统设置", "打开系统属性的高级设置。", "#FF7C3AED", "SystemPropertiesAdvanced.exe", "system-advanced-properties", "mdi:settings", ["系统", "高级系统设置", "性能", "环境变量", "启动和故障恢复"]),
            CreateSystemCommand("声", "经典声音面板", "打开经典声音控制面板。", "#FF0EA5E9", "rundll32.exe", "system-classic-sound", "mdi:settings", ["系统", "声音", "耳机", "扬声器", "录音设备", "播放设备"], launchArguments: "shell32.dll,Control_RunDLL mmsys.cpl"),
            CreateSystemCommand("播", "播放设备", "直达经典声音面板的播放设备页。", "#FF0284C7", "rundll32.exe", "system-playback-devices", "mdi:settings", ["系统", "播放设备", "扬声器", "耳机", "输出设备"], launchArguments: "shell32.dll,Control_RunDLL mmsys.cpl,,0"),
            CreateSystemCommand("录", "录音设备", "直达经典声音面板的录音设备页。", "#FF0F766E", "rundll32.exe", "system-recording-devices", "mdi:settings", ["系统", "录音设备", "麦克风", "输入设备"], launchArguments: "shell32.dll,Control_RunDLL mmsys.cpl,,1"),
            CreateSystemCommand("效", "系统声音", "直达经典声音面板的系统声音页。", "#FF7C3AED", "rundll32.exe", "system-sounds-tab", "mdi:settings", ["系统", "系统声音", "提示音", "声音方案"], launchArguments: "shell32.dll,Control_RunDLL mmsys.cpl,,2"),
            CreateSystemCommand("通", "通信音频", "直达经典声音面板的通信页。", "#FF0891B2", "rundll32.exe", "system-communications-audio", "mdi:settings", ["系统", "通信", "通话", "音频", "耳机"], launchArguments: "shell32.dll,Control_RunDLL mmsys.cpl,,3")
        ];
    }

    private static CommandItem CreateSystemCommand(
        string glyph,
        string title,
        string subtitle,
        string accentHex,
        string openTarget,
        string extensionId,
        string iconReference,
        IEnumerable<string> keywords,
        string? launchArguments = null)
    {
        return new CommandItem(
            glyph: glyph,
            title: title,
            subtitle: subtitle,
            category: "系统",
            accentHex: accentHex,
            openTarget: openTarget,
            keywords: keywords,
            source: CommandSource.Local,
            extensionId: extensionId,
            iconReference: iconReference,
            launchArguments: launchArguments,
            iconSourceOverride: NativeFileIconService.GetSystemCommandIcon(openTarget, extensionId));
    }

    private static List<CommandItem> CreateInstalledApplicationCommands()
    {
        try
        {
            var entries = InstalledApplicationCatalog.Load();
            HostAssets.AppendLog($"Installed applications loaded: count={entries.Count}.");
            return entries
                .Select(entry => new CommandItem(
                    glyph: InferApplicationGlyph(entry.Title),
                    title: entry.Title,
                    subtitle: entry.Subtitle,
                    category: "应用",
                    accentHex: "#FF4B5563",
                    openTarget: entry.LaunchTarget,
                    keywords: entry.Keywords,
                    source: CommandSource.Application,
                    extensionId: entry.ExtensionId,
                    iconReference: entry.IconPath,
                    launchArguments: entry.Arguments,
                    workingDirectory: entry.WorkingDirectory))
                .ToList();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"InstalledApplicationCatalog.Load failed: {ex.Message}");
            return [];
        }
    }

    private static string InferApplicationGlyph(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "A";
        }

        var trimmed = title.Trim();
        return trimmed[..1].ToUpperInvariant();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }




    public List<CommandItem> GetAllCommands() => _allCommands.ToList();

    public QuickPanelClipboardItem? GetQuickPanelClipboard() => _quickPanelClipboard;

    public void ClearQuickPanelClipboard()
    {
        _quickPanelClipboard = null;
        LastRunMessage = "已清空扩展剪贴板。";
    }

    public void SetQuickPanelClipboard(CommandItem command, bool isCut, QuickPanelSlotReference? sourceSlot)
    {
        _quickPanelClipboard = new QuickPanelClipboardItem(command.ExtensionId, command.Title, isCut, sourceSlot);
        var action = isCut ? "剪切" : "复制";
        LastRunMessage = sourceSlot == null
            ? $"已{action}扩展：{command.Title}。现在可以在鼠标面板槽位右键粘贴。"
            : $"已{action}鼠标面板中的扩展：{command.Title}。";
    }

    public bool TryImportExtensionFromSystemClipboard(out CommandItem? command, out string message)
    {
        command = null;
        message = string.Empty;

        string clipboardText;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                message = "系统剪贴板里没有文本内容。";
                return false;
            }

            clipboardText = System.Windows.Clipboard.GetText();
        }
        catch (Exception ex)
        {
            message = $"读取系统剪贴板失败：{FormatExceptionMessage(ex)}";
            return false;
        }

        var normalizedJson = ExtractExtensionJsonFromClipboard(clipboardText);
        if (string.IsNullOrWhiteSpace(normalizedJson))
        {
            message = "系统剪贴板里没有可导入的扩展 JSON。";
            return false;
        }

        try
        {
            command = PersistJsonExtensionFromDialog(normalizedJson, isEditMode: false);
            QueueBackgroundWebDavSync("extension-import-clipboard");
            return true;
        }
        catch (Exception ex)
        {
            message = $"剪贴板里的扩展 JSON 无法导入：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    private static string ExtractExtensionJsonFromClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
            if (lines.Count >= 2 && lines[0].StartsWith("```", StringComparison.Ordinal))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count > 0 && lines[^1].StartsWith("```", StringComparison.Ordinal))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            trimmed = string.Join(Environment.NewLine, lines).Trim();
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            trimmed = trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    public void ExecuteCommandExternally(CommandItem command, string? explicitInput = null, string launchSource = "quick-panel")
    {
        _ = ExecuteCommandAsync(ResolveRunnableCommand(command), explicitInput ?? string.Empty, launchSource);
    }

    private void RecordCommandUsage(CommandItem command)
    {
        _searchUsageMemory.Record(command.ExtensionId);
        SearchUsageMemory.Save(_searchUsageMemory);
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

    public SearchScopeTab(string key, string label, string tooltip, bool isSelected = false, bool isPinnedCommand = false, string? pinnedCommandId = null)
    {
        Key = key;
        Label = label;
        Tooltip = tooltip;
        _isSelected = isSelected;
        IsPinnedCommand = isPinnedCommand;
        PinnedCommandId = pinnedCommandId;
    }

    public string Key { get; }

    public string Label { get; }

    public string Tooltip { get; }

    public bool IsPinnedCommand { get; }

    public string? PinnedCommandId { get; }

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

    public static SearchScopeTab CreatePinnedCommand(string extensionId, string label, string tooltip)
    {
        return new SearchScopeTab(CreatePinnedCommandKey(extensionId), label, tooltip, isPinnedCommand: true, pinnedCommandId: extensionId);
    }

    public static string CreatePinnedCommandKey(string extensionId) => $"pin:{extensionId}";

    public static bool TryParsePinnedCommandScope(string scopeKey, out string extensionId)
    {
        const string prefix = "pin:";
        if (scopeKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && scopeKey.Length > prefix.Length)
        {
            extensionId = scopeKey[prefix.Length..];
            return true;
        }

        extensionId = string.Empty;
        return false;
    }
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
    IReadOnlyList<HostedViewComponentDefinition> Components,
    IReadOnlyDictionary<string, HostedViewInlineScriptDefinition> Scripts)
{
    public bool UsesDynamicLayout => Components.Count > 0 || !string.IsNullOrWhiteSpace(XamlTemplate);
}

public sealed record HostedViewInlineScriptDefinition(
    string? Runtime,
    string? EntryMode,
    string? Entry,
    IReadOnlyList<string> Permissions,
    string? Source);

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
    string? Script,
    string? ValueFrom,
    string? InputFrom,
    string? OutputTo,
    string? SuccessMessage,
    bool Append,
    string? Separator,
    string? Key,
    string? Scope,
    string? DefaultValue);

public sealed record ExtensionStartupDefinition(
    string? Mode,
    string? Schedule);

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

public sealed class CloudQuickPanelConfigSnapshot
{
    [JsonPropertyName("updatedAtUtc")]
    public string? UpdatedAtUtc { get; set; }

    [JsonPropertyName("quickPanelSlots")]
    public List<string?> QuickPanelSlots { get; set; } = Enumerable.Repeat<string?>(null, 28).ToList();

    [JsonPropertyName("quickPanelGlobalGroups")]
    public List<QuickPanelGroupSettings> QuickPanelGlobalGroups { get; set; } = [];

    [JsonPropertyName("quickPanelContextGroups")]
    public List<QuickPanelGroupSettings> QuickPanelContextGroups { get; set; } = [];

    [JsonPropertyName("selectedQuickPanelGlobalGroupId")]
    public string SelectedQuickPanelGlobalGroupId { get; set; } = "global-default";

    [JsonPropertyName("selectedQuickPanelContextGroupId")]
    public string SelectedQuickPanelContextGroupId { get; set; } = "context-default";

    [JsonPropertyName("globalFavoriteExtensionIds")]
    public List<string> GlobalFavoriteExtensionIds { get; set; } = [];

    [JsonPropertyName("contextFavoriteExtensionIds")]
    public List<string> ContextFavoriteExtensionIds { get; set; } = [];

    [JsonPropertyName("quickPanelMouseTriggers")]
    public QuickPanelMouseTriggerSettings QuickPanelMouseTriggers { get; set; } = new();

    [JsonPropertyName("mouseGestureTriggerMode")]
    public string MouseGestureTriggerMode { get; set; } = MouseGestureTriggerModes.RightDrag;

    [JsonPropertyName("windowSnapAssistMouseTriggerMode")]
    public string WindowSnapAssistMouseTriggerMode { get; set; } = MouseTriggerModes.None;

    [JsonPropertyName("yarnSelect")]
    public YarnSelectSettings? YarnSelect { get; set; }

    [JsonPropertyName("radialMenu")]
    public RadialMenuSettings? RadialMenu { get; set; }

    [JsonPropertyName("yanyuRules")]
    public List<YanyuRuleSettings>? YanyuRules { get; set; }

    [JsonPropertyName("yanm")]
    public YanmSettings? Yanm { get; set; }

    [JsonPropertyName("aiBaseUrl")]
    public string? AiBaseUrl { get; set; }

    [JsonPropertyName("aiApiKey")]
    public string? AiApiKey { get; set; }

    [JsonPropertyName("aiModel")]
    public string? AiModel { get; set; }

    public static CloudQuickPanelConfigSnapshot FromSettings(AppSettings settings)
    {
        return new CloudQuickPanelConfigSnapshot
        {
            QuickPanelSlots = settings.QuickPanelSlots.ToList(),
            QuickPanelGlobalGroups = CloneGroups(settings.QuickPanelGlobalGroups),
            QuickPanelContextGroups = CloneGroups(settings.QuickPanelContextGroups),
            SelectedQuickPanelGlobalGroupId = settings.SelectedQuickPanelGlobalGroupId,
            SelectedQuickPanelContextGroupId = settings.SelectedQuickPanelContextGroupId,
            GlobalFavoriteExtensionIds = settings.GlobalFavoriteExtensionIds.ToList(),
            ContextFavoriteExtensionIds = settings.ContextFavoriteExtensionIds.ToList(),
            QuickPanelMouseTriggers = CloneTriggers(settings.QuickPanelMouseTriggers),
            MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(settings.MouseGestureTriggerMode),
            WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(settings.WindowSnapAssistMouseTriggerMode),
            YarnSelect = CloneByJson(settings.YarnSelect),
            RadialMenu = CloneByJson(settings.RadialMenu),
            YanyuRules = CloneByJson(settings.YanyuRules),
            Yanm = CloneByJson(settings.Yanm),
            AiBaseUrl = settings.AiBaseUrl,
            AiApiKey = settings.AiApiKey,
            AiModel = settings.AiModel,
            UpdatedAtUtc = string.IsNullOrWhiteSpace(settings.LauncherConfigUpdatedAtUtc)
                ? DateTime.UtcNow.ToString("O")
                : settings.LauncherConfigUpdatedAtUtc
        };
    }

    public AppSettings ToAppSettings()
    {
        return new AppSettings
        {
            QuickPanelSlots = QuickPanelSlots.ToList(),
            QuickPanelGlobalGroups = CloneGroups(QuickPanelGlobalGroups),
            QuickPanelContextGroups = CloneGroups(QuickPanelContextGroups),
            SelectedQuickPanelGlobalGroupId = SelectedQuickPanelGlobalGroupId,
            SelectedQuickPanelContextGroupId = SelectedQuickPanelContextGroupId,
            GlobalFavoriteExtensionIds = GlobalFavoriteExtensionIds.ToList(),
            ContextFavoriteExtensionIds = ContextFavoriteExtensionIds.ToList(),
            QuickPanelMouseTriggers = CloneTriggers(QuickPanelMouseTriggers),
            MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(MouseGestureTriggerMode),
            WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(WindowSnapAssistMouseTriggerMode),
            YarnSelect = YarnSelect == null ? new YarnSelectSettings() : CloneByJson(YarnSelect),
            RadialMenu = RadialMenu == null ? new RadialMenuSettings() : CloneByJson(RadialMenu),
            YanyuRules = YanyuRules == null ? [] : CloneByJson(YanyuRules),
            Yanm = Yanm == null ? new YanmSettings() : CloneByJson(Yanm),
            AiBaseUrl = AiBaseUrl ?? string.Empty,
            AiApiKey = AiApiKey ?? string.Empty,
            AiModel = AiModel ?? string.Empty,
            LauncherConfigUpdatedAtUtc = UpdatedAtUtc ?? string.Empty
        };
    }

    private static T CloneByJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json) ?? value;
    }

    private static List<QuickPanelGroupSettings> CloneGroups(IEnumerable<QuickPanelGroupSettings> groups)
    {
        return groups.Select(group => new QuickPanelGroupSettings
        {
            Id = group.Id,
            Name = group.Name,
            ContextProcessName = group.ContextProcessName,
            ContextDisplayName = group.ContextDisplayName,
            Slots = group.Slots.ToList(),
            SlotItems = group.SlotItems
                .Select(CloneQuickPanelSlotItem)
                .ToList()
        }).ToList();
    }

    private static QuickPanelSlotItem? CloneQuickPanelSlotItem(QuickPanelSlotItem? item)
    {
        return item == null
            ? null
            : new QuickPanelSlotItem
            {
                ItemType = item.ItemType,
                ExtensionId = item.ExtensionId,
                FolderName = item.FolderName,
                FolderExtensionIds = item.FolderExtensionIds.ToList(),
                FolderSlotItems = item.FolderSlotItems.Select(CloneQuickPanelSlotItem).ToList()
            };
    }

    private static QuickPanelMouseTriggerSettings CloneTriggers(QuickPanelMouseTriggerSettings trigger)
    {
        return new QuickPanelMouseTriggerSettings
        {
            MiddleButtonDown = trigger.MiddleButtonDown,
            X1ButtonDown = trigger.X1ButtonDown,
            X2ButtonDown = trigger.X2ButtonDown,
            CtrlLeftClick = trigger.CtrlLeftClick,
            CtrlRightClick = trigger.CtrlRightClick,
            MiddleButtonLongPress = trigger.MiddleButtonLongPress,
            RightButtonLongPress = trigger.RightButtonLongPress,
            RightButtonDrag = trigger.RightButtonDrag,
            MiddleButtonDrag = trigger.MiddleButtonDrag,
            HorizontalWheel = trigger.HorizontalWheel,
            ExecuteOnButtonRelease = trigger.ExecuteOnButtonRelease,
            LongPressMilliseconds = trigger.LongPressMilliseconds,
            DragThresholdPixels = trigger.DragThresholdPixels
        };
    }
}

public sealed class CommandItem : INotifyPropertyChanged
{
    private string? _queryPreviewSubtitle;
    private string? _queryPreviewActionLabel;
    private ImageSource? _iconSource;
    private bool _hasNewBadge;
    private bool _isCSharpPrebuilding;

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
        AppExtensionDefinition? app = null,
        IEnumerable<string>? queryPrefixes = null,
        string? queryTargetTemplate = null,
        HostedPluginViewDefinition? hostedView = null,
        string? globalShortcut = null,
        string? hotkeyBehavior = null,
        string? runtime = null,
        string? uiMode = null,
        string? entryPoint = null,
        IEnumerable<string>? permissions = null,
        string? entryMode = null,
        string? inlineScriptSource = null,
        string? iconReference = null,
        ExtensionStartupDefinition? startup = null,
        string? launchArguments = null,
        string? workingDirectory = null,
        ImageSource? iconSourceOverride = null,
        CommandSearchProviderDefinition? searchProvider = null,
        ResultItemKind resultKind = ResultItemKind.None,
        string? resultProviderTitle = null)
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
        App = app;
        QueryPrefixes = queryPrefixes?.ToArray() ?? [];
        QueryTargetTemplate = queryTargetTemplate;
        HostedView = hostedView;
        GlobalShortcut = globalShortcut;
        HotkeyBehavior = hotkeyBehavior;
        Runtime = runtime;
        UiMode = uiMode;
        EntryPoint = entryPoint;
        Permissions = permissions?.ToArray() ?? [];
        EntryMode = entryMode;
        InlineScriptSource = inlineScriptSource;
        IconReference = iconReference;
        _iconSource = iconSourceOverride ?? ExtensionIconLibrary.ResolveImageSource(iconReference, extensionDirectoryPath);
        VectorIcon = ExtensionIconLibrary.ResolveVectorIcon(iconReference);
        Startup = startup;
        LaunchArguments = launchArguments;
        WorkingDirectory = workingDirectory;
        SearchProvider = searchProvider;
        ResultKind = resultKind;
        ResultProviderTitle = resultProviderTitle;
    }

    public string Glyph { get; }

    public string DisplayGlyph => Glyph;

    public string? IconReference { get; }

    public ImageSource? IconSource => _iconSource;

    public Geometry? VectorIcon { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon;

    public string Title { get; }

    public string Subtitle { get; }

    public string DisplaySubtitle => string.IsNullOrWhiteSpace(_queryPreviewSubtitle) ? Subtitle : _queryPreviewSubtitle;

    public string EffectiveSubtitle => IsCSharpPrebuilding
        ? "正在编译扩展，首次运行完成后会更快"
        : DisplaySubtitle;

    public string Category { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string? OpenTarget { get; }

    public IReadOnlyList<string> Keywords { get; }

    public CommandSource Source { get; }

    public string ExtensionId { get; }

    public string DeclaredVersion { get; }

    public string? ExtensionDirectoryPath { get; }

    public AppExtensionDefinition? App { get; }

    public IReadOnlyList<string> QueryPrefixes { get; }

    public string? QueryTargetTemplate { get; }

    public HostedPluginViewDefinition? HostedView { get; }

    public string? GlobalShortcut { get; }

    public string? HotkeyBehavior { get; }

    public string? Runtime { get; }

    public string? UiMode { get; }

    public string? EntryPoint { get; }

    public IReadOnlyList<string> Permissions { get; }

    public string? EntryMode { get; }

    public ExtensionStartupDefinition? Startup { get; }

    public string? InlineScriptSource { get; }

    public string? LaunchArguments { get; }

    public string? WorkingDirectory { get; }

    public CommandSearchProviderDefinition? SearchProvider { get; }

    public ResultItemKind ResultKind { get; }

    public string? ResultProviderTitle { get; }

    public bool SupportsQueryArgument => QueryPrefixes.Count > 0 && (!string.IsNullOrWhiteSpace(QueryTargetTemplate) || HasScriptEntry || HasHostedView);

    public bool ShouldCaptureSelectedInput =>
        HasPermission("context.read");

    public bool HasSearchProvider => SearchProvider != null;

    public bool HasHostedView => HostedView != null;

    public bool HasApp => App != null;

    public bool HasScriptEntry =>
        !string.IsNullOrWhiteSpace(Runtime) &&
        (string.Equals(EntryMode, "inline", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(InlineScriptSource)
            : !string.IsNullOrWhiteSpace(EntryPoint));

    private bool HasPermission(string permission)
    {
        return Permissions.Any(item => string.Equals(item, permission, StringComparison.OrdinalIgnoreCase));
    }

    public bool UsesNativeWindowUi =>
        string.Equals(UiMode, "native-window", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(UiMode, "external-window", StringComparison.OrdinalIgnoreCase);

    public bool HasGlobalShortcut => !string.IsNullOrWhiteSpace(GlobalShortcut);

    public bool HasNewBadge => _hasNewBadge;

    public bool IsCSharpPrebuilding => _isCSharpPrebuilding;

    public bool IsFileSystemResult => ResultKind is ResultItemKind.File or ResultItemKind.Folder;

    public bool IsProviderResult => ResultKind != ResultItemKind.None;

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
        : Source == CommandSource.Application
            ? "应用"
        : ResultKind == ResultItemKind.Folder
            ? "文件夹"
        : ResultKind == ResultItemKind.File
            ? "文件"
        : HasHostedView
            ? "插件界面"
            : UsesNativeWindowUi
                ? "原生窗口"
                : HasScriptEntry
                ? "脚本"
                : Category;

    public string DisplayTypeLabel => Source == CommandSource.Application
        ? "应用"
        : ResultKind is ResultItemKind.File or ResultItemKind.Folder
            ? "文件"
        : Source == CommandSource.WebSearch
            ? "网页"
        : Category.Contains("燕语", StringComparison.OrdinalIgnoreCase)
            ? "燕语"
        : Category.Contains("系统", StringComparison.OrdinalIgnoreCase)
            ? "系统"
            : "扩展";

    public bool HasDisplayTypeLabel => !string.IsNullOrWhiteSpace(DisplayTypeLabel);

    public string DisplayActionLabel => string.IsNullOrWhiteSpace(_queryPreviewActionLabel) ? DisplayTypeLabel : _queryPreviewActionLabel;

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
        CommandSource.Application => "应用",
        CommandSource.File => !string.IsNullOrWhiteSpace(ResultProviderTitle) ? ResultProviderTitle! : "文件结果",
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

    public void SetQueryPreview(string? subtitle, string? actionLabel)
    {
        if (string.Equals(_queryPreviewSubtitle, subtitle, StringComparison.Ordinal) &&
            string.Equals(_queryPreviewActionLabel, actionLabel, StringComparison.Ordinal))
        {
            return;
        }

        _queryPreviewSubtitle = subtitle;
        _queryPreviewActionLabel = actionLabel;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySubtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveSubtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayActionLabel)));
    }

    public void SetIconSource(ImageSource? iconSource)
    {
        if (ReferenceEquals(_iconSource, iconSource))
        {
            return;
        }

        _iconSource = iconSource;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImageIcon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseGlyphIcon)));
    }

    public void SetHasNewBadge(bool value)
    {
        if (_hasNewBadge == value)
        {
            return;
        }

        _hasNewBadge = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNewBadge)));
    }

    public void SetCSharpPrebuildState(bool value)
    {
        if (_isCSharpPrebuilding == value)
        {
            return;
        }

        _isCSharpPrebuilding = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCSharpPrebuilding)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveSubtitle)));
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

public sealed record AppExtensionDefinition(
    string Type,
    string Entry,
    bool SingleInstance,
    double? WindowWidth,
    double? WindowHeight,
    double? MinWindowWidth,
    double? MinWindowHeight,
    bool HideTitleBar,
    string StorageMode,
    string StorageEngine,
    string Sync,
    string? Namespace,
    IReadOnlyList<string> BridgeApis);

public enum CommandSource
{
    Local,
    LocalExtension,
    Cloud,
    WebSearch,
    Application,
    File
}

public sealed class AttachedFileItem : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public AttachedFileItem(string fullPath, bool isFolder)
    {
        FullPath = fullPath;
        IsFolder = isFolder;
        DisplayName = isFolder
            ? new DirectoryInfo(fullPath).Name
            : Path.GetFileName(fullPath);
    }

    public string FullPath { get; }

    public string DisplayName { get; }

    public bool IsFolder { get; }

    public ImageSource? IconSource => _iconSource;

    public bool HasIcon => _iconSource != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetIconSource(ImageSource? iconSource)
    {
        if (ReferenceEquals(iconSource, _iconSource))
        {
            return;
        }

        _iconSource = iconSource;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
    }
}
