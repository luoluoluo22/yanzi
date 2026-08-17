using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using OpenQuickHost.Sync;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private const string RadialSimulatedKeyPrefix = "keysim::";
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly MainWindow _mainWindow;
    private AppSettings _settings;
    private SettingsNavigationItem? _selectedNavigation;
    private string _accountTitle = "未登录";
    private string _accountSubtitle = "点击左上角账户卡片登录或切换账号。";
    private string _accountInitial = "燕";
    private bool _isAccountLoggedIn;
    private string _localExtensionSummary = "正在统计...";
    private string _settingsSearchText = string.Empty;
    private string _extensionSearchText = string.Empty;
    private string _radialMenuSearchText = string.Empty;
    private string _launcherHotkey = "Alt+Space";
    private string _syncStatusText = "同步服务状态未知。";
    private string _webDavStatusText = "未启用个人扩展同步。";
    private string _syncActivityLogText = "暂无同步记录。";
    private string _personalSyncCommitStatusText = "GitHub 同步启用后可查看最近提交。";
    private string _personalConfigRestoreStatusText = "完成一次个人仓库配置备份后会生成恢复点。";
    private string _personalExtensionSyncStatusText = "尚未生成扩展同步索引。";
    private string _extensionDataSyncStatusText = "尚无扩展私有数据同步记录。";
    private AccountSyncStatusView _accountSyncStatus = AccountSyncStatusView.Empty;
    private string _aiBaseUrl = string.Empty;
    private string _aiApiKey = string.Empty;
    private string _aiModel = string.Empty;
    private string _aiSystemPrompt = string.Empty;
    private string _aiSettingsStatusText = "尚未配置 AI。";
    private ObservableCollection<SettingsAiProviderVM> _aiServiceProvidersList = new();
    private string _providerSearchText = string.Empty;
    private SettingsAiProviderVM? _selectedServiceProvider;
    private string _checkApiKeyButtonText = "检测";
    private string _environmentStatusText = "尚未配置环境变量。";
    private string _radialPreviewDebugLog = "预览日志：等待交互。";
    private string _recycleBinSummary = "回收站为空。";
    private string _recycleBinSearchText = string.Empty;
    private bool _isExtensionsLoading;
    private int _extensionsRefreshVersion;
    private bool _hasLoadedExtensions; // 标记是否已加载过扩展
    private bool _hasInitializedRadialEditor;
    private IReadOnlyList<SettingsExtensionItem> _cachedExtensionItems = [];
    private IReadOnlyList<SettingsRecycleBinItem> _cachedRecycleBinItems = [];
    private SettingsExtensionItem? _selectedExtensionItem;
    private double _extensionCardWidth = 280;
    private bool _suppressWindowBoundsPersistence;
    private bool _isRefreshingRadialMenu;
    private bool _isRenamingRadialMenuPage;
    private bool _suspendActivationRefresh;
    private RadialMenuSlotEditorItem? _selectedRadialMenuSlot;
    private readonly Dictionary<string, WpfComboBox> _mouseTriggerTargetCombos = new(StringComparer.Ordinal);
    private bool _isUpdatingMouseTriggerTargetCombos;
    private bool _showPersonalSyncAdvancedOptions;
    private readonly List<SettingsSearchItem> _dynamicSettingsSearchItems = [];
    private readonly Dictionary<TextBlock, string> _searchHighlightSnapshots = new();
    private bool _isRecordingSnapAssistHotkey;
    private bool _isRecordingLauncherHotkey;
    private string? _lastLauncherDoubleTapCandidate;
    private DateTime _lastLauncherDoubleTapAtUtc;
    private HwndSource? _source;
    
    // 扩展名称缓存，避免重复读取文件
    private static readonly Dictionary<string, string> _extensionNameCache = new();
    
    // 窗口边界保存防抖定时器
    private DispatcherTimer? _windowBoundsPersistTimer;
    
    // AI设置变更追踪
    private string _originalAiBaseUrl = string.Empty;
    private string _originalAiApiKey = string.Empty;
    private string _originalAiModel = string.Empty;
    private string _originalAiSystemPrompt = string.Empty;
    private bool _hasAiSettingsChanged;
    private PersonalSyncSettings _personalSyncSettings = new();
    private PersonalSyncSecretBag _personalSyncSecrets = new();
    
    // 扩展筛选状态
    private string _extensionFilterMode = "all"; // all, published, disabled, shortcut, recycle

    public SettingsWindow(MainWindow mainWindow)
    {
        HostAssets.AppendLog($"SettingsWindow ctor start. thread={Environment.CurrentManagedThreadId}, will InitializeComponent().");
        InitializeComponent();
        HostAssets.AppendLog($"SettingsWindow InitializeComponent completed. Content={Content?.GetType().Name ?? "null"}, width={Width}, height={Height}.");
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 17, 17));
        Opacity = 1;
        App.EnableSilentLoading(this);
        HostAssets.AppendLog($"SettingsWindow silent loading attached. opacity={Opacity}, showActivated={ShowActivated}, background={Background}.");
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _personalSyncSettings = ClonePersonalSyncSettings(_settings.PersonalSync);
        _personalSyncSecrets = ClonePersonalSyncSecrets(_mainWindow.GetPersonalSyncSecrets());
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.Yanm ??= new YanmSettings();
        NavigationItems =
        [
            new SettingsNavigationItem("general", "mdi:settings", "常规", "#FF3B82F6"),
            new SettingsNavigationItem("ai", "mdi:ai", "模型服务", "#FF3B82F6"),
            new SettingsNavigationItem("environment", "mdi:key", "环境变量", "#FF14B8A6"),
            new SettingsNavigationItem("sync", "mdi:sync", "同步与备份", "#FF22C55E"),
            new SettingsNavigationItem("extensions", "mdi:dashboard", "扩展", "#FFF97316"),
            new SettingsNavigationItem("quickpanel", "mdi:mouse-panel", "鼠标触发", "#FFEC4899"),
            new SettingsNavigationItem("mousegestures", "mdi:gesture-tap", "鼠标手势", "#FFFB923C"),
            new SettingsNavigationItem("radial", "mdi:circle-outline", "燕环", "#FF3B82F6"),
            new SettingsNavigationItem("yarnselect", "mdi:shortcut", "燕选", "#FF14B8A6"),
            new SettingsNavigationItem("yanm", "mdi:monitor-dashboard", "燕幕", "#FF60A5FA"),
            new SettingsNavigationItem("yanwo", "mdi:home-group", "燕窝", "#FFA855F7"),
            new SettingsNavigationItem("about", "mdi:about", "关于", "#FF3B82F6")
        ];
        _selectedNavigation = NavigationItems.First();
        LaunchAtStartup = _settings.LaunchAtStartup;
        RefreshCloudOnStartup = _settings.RefreshCloudOnStartup;
        CloseToTray = _settings.CloseToTray;
        EnableAutoUpdate = _settings.EnableAutoUpdate;
        EnableEverything = _settings.EnableEverything;
        EnableWindowSnapAssist = _settings.EnableWindowSnapAssist;
        LauncherHotkey = _settings.LauncherHotkey;
        LoadPersonalSyncStateFromSettings();
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSystemPrompt = _settings.AiSystemPrompt;

        ReloadAiProvidersFromSettings();


        SubscribeUpdateEvents();
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        EnvironmentVariables = new ObservableCollection<EnvironmentVariableEditorItem>(
            AppEnvironmentVariableStore.Load().Select(static item => new EnvironmentVariableEditorItem(item.Name, item.Value, item.Description)));
        EnvironmentStatusText = BuildEnvironmentSummary();
        BaseUrl = _mainWindow.SyncBaseUrl;
        ExtensionsRootPath = LocalExtensionCatalog.CatalogRootPath;
        AppVersionText = AppVersionInfo.DisplayText;
        ShortcutItems = new ObservableCollection<SettingsShortcutItem>();
        ExtensionItems = new ObservableCollection<SettingsExtensionItem>();
        RecycleBinItems = new ObservableCollection<SettingsRecycleBinItem>();
        PersonalSyncCommitItems = new ObservableCollection<PersonalSyncCommitItem>();
        PersonalConfigRestorePoints = new ObservableCollection<PersonalConfigRestorePointItem>();
        ExtensionSyncConflictItems = new ObservableCollection<ExtensionSyncConflictItem>();
        ExtensionDataConflictItems = new ObservableCollection<ExtensionDataConflictItem>();
        YarnSelectRules = new ObservableCollection<YarnSelectRuleItem>();
        YarnSelectExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>();
        RadialMenuExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>();
        FilteredRadialMenuCommandOptions = new ObservableCollection<YarnSelectExtensionOption>();
        RadialMenuSlots = new ObservableCollection<RadialMenuSlotEditorItem>();
        RadialMenuPreviewSeparators = new ObservableCollection<RadialSeparatorViewModel>();
        RadialMenuPages = new ObservableCollection<RadialMenuPageEditorItem>();
        RadialMenuChildPageOptions = new ObservableCollection<RadialMenuPageEditorItem>();
        MouseGestureItems = new ObservableCollection<SettingsMouseGestureItem>();
        MouseGestureQuickBindItems = new ObservableCollection<MouseGestureQuickBindItem>();
        MouseGestureExtensionOptions = new ObservableCollection<MouseGestureExtensionOption>();
        MatchedSearchItems = new ObservableCollection<SearchDisplayItem>();
        UpdateBackupStatusText();
        DataContext = this;
        RefreshAccountObjectSyncStatus();
        // 延迟到Loaded事件中执行，避免构造函数卡顿
        // RefreshRadialMenuSlots();
        ApplySavedWindowBounds();
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        LocationChanged += SettingsWindow_BoundsChanged;
        SizeChanged += SettingsWindow_BoundsChanged;
        Closing += SettingsWindow_Closing;
        LoadLogoImage();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        _source?.AddHook(SettingsWindowWndProc);
        HostAssets.AppendLog($"SettingsWindow: OnSourceInitialized called. handle={_source?.Handle}, opacity={Opacity}, visibility={Visibility}, showActivated={ShowActivated}.");
        App.UpdateWindowDwmTheme(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(SettingsWindowWndProc);
        _source = null;
        
        var app = System.Windows.Application.Current as App;
        if (app != null && app.AgentApiServer != null)
        {
            app.AgentApiServer.BrowserConnectionChanged -= AgentApiServer_BrowserConnectionChanged;
            LocalAgentApiServer.MobileDeviceConnected -= LocalAgentApiServer_MobileDeviceConnected;
        }

        base.OnClosed(e);
    }

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; }
    public ObservableCollection<SearchDisplayItem> MatchedSearchItems { get; }

    public ObservableCollection<SettingsShortcutItem> ShortcutItems { get; }

    public ObservableCollection<SettingsExtensionItem> ExtensionItems { get; }

    public ObservableCollection<SettingsRecycleBinItem> RecycleBinItems { get; }

    public ObservableCollection<PersonalSyncCommitItem> PersonalSyncCommitItems { get; }

    public ObservableCollection<PersonalConfigRestorePointItem> PersonalConfigRestorePoints { get; }

    public ObservableCollection<ExtensionSyncConflictItem> ExtensionSyncConflictItems { get; }

    public bool HasExtensionSyncConflicts => ExtensionSyncConflictItems.Count > 0;

    public ObservableCollection<ExtensionDataConflictItem> ExtensionDataConflictItems { get; }

    public bool HasExtensionDataConflicts => ExtensionDataConflictItems.Count > 0;

    public AccountSyncStatusView AccountSyncStatus
    {
        get => _accountSyncStatus;
        private set
        {
            _accountSyncStatus = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<YarnSelectRuleItem> YarnSelectRules { get; }

    public ObservableCollection<YarnSelectExtensionOption> YarnSelectExtensionOptions { get; }

    public ObservableCollection<YarnSelectExtensionOption> RadialMenuExtensionOptions { get; }

    public ObservableCollection<YarnSelectExtensionOption> FilteredRadialMenuCommandOptions { get; }

    public ObservableCollection<RadialMenuSlotEditorItem> RadialMenuSlots { get; }

    public ObservableCollection<RadialSeparatorViewModel> RadialMenuPreviewSeparators { get; }

    public ObservableCollection<RadialMenuPageEditorItem> RadialMenuPages { get; }

    public ObservableCollection<RadialMenuPageEditorItem> RadialMenuChildPageOptions { get; }

    public ObservableCollection<SettingsMouseGestureItem> MouseGestureItems { get; }

    public ObservableCollection<MouseGestureQuickBindItem> MouseGestureQuickBindItems { get; }

    public ObservableCollection<MouseGestureAppOption> MouseGestureAppOptions { get; } = new();
    public ObservableCollection<MouseGestureExtensionOption> MouseGestureExtensionOptions { get; }

    public IReadOnlyList<YarnSelectActionTypeOption> YarnSelectActionOptions { get; } =
    [
        new(YarnSelectActionTypes.Copy, "复制"),
        new(YarnSelectActionTypes.Cut, "剪切"),
        new(YarnSelectActionTypes.Paste, "粘贴"),
        new(YarnSelectActionTypes.Search, "搜索"),
        new(YarnSelectActionTypes.Run, "运行文本"),
        new(YarnSelectActionTypes.SmartCopyPaste, "智能复制/粘贴"),
        new(YarnSelectActionTypes.RunExtension, "运行扩展")
    ];

    public IReadOnlyList<YanmActivationKeyOption> YanmActivationKeyOptions { get; } =
    [
        new(YanmActivationKeys.Win, "Win"),
        new(YanmActivationKeys.CapsLock, "CapsLock"),
        new(YanmActivationKeys.Custom, "自定义快捷键")
    ];

    public IReadOnlyList<YanmActivationKeyOption> RadialActivationKeyOptions { get; } =
    [
        new(RadialActivationKeys.None, "不启用"),
        new(RadialActivationKeys.Win, "Win"),
        new(RadialActivationKeys.CapsLock, "CapsLock"),
        new(RadialActivationKeys.Custom, "自定义快捷键")
    ];

    public IReadOnlyList<MouseTriggerOption> MouseTriggerOptions { get; } =
    [
        new(MouseTriggerModes.None, "不启用"),
        new(MouseTriggerModes.MiddleDown, "按下中键"),
        new(MouseTriggerModes.X1Down, "按下 X1 键"),
        new(MouseTriggerModes.X2Down, "按下 X2 键"),
        new(MouseTriggerModes.CtrlLeftClick, "Ctrl+左键单击"),
        new(MouseTriggerModes.CtrlRightClick, "Ctrl+右键单击"),
        new(MouseTriggerModes.MiddleLongPress, "长按中键"),
        new(MouseTriggerModes.RightLongPress, "长按右键"),
        new(MouseTriggerModes.RightDrag, "按右键移动"),
        new(MouseTriggerModes.HorizontalWheel, "滚轮左右")
    ];

    public IReadOnlyList<MouseTriggerOption> MouseGestureTriggerOptions { get; } =
    [
        new(MouseGestureTriggerModes.None, "不启用鼠标手势"),
        new(MouseGestureTriggerModes.RightDrag, "按住右键移动"),
        new(MouseGestureTriggerModes.MiddleDrag, "按住中键移动")
    ];

    public IReadOnlyList<SyncProviderOption> PersonalSyncProviderOptions { get; } =
    [
        new(PersonalSyncProviders.GitHub, "GitHub"),
        new(PersonalSyncProviders.Gitee, "Gitee"),
        new(PersonalSyncProviders.GitLab, "GitLab"),
        new(PersonalSyncProviders.Gitea, "Gitea"),
        new(PersonalSyncProviders.S3, "S3"),
        new(PersonalSyncProviders.WebDav, "WebDAV")
    ];

    public IReadOnlyList<AutoSyncDelayOption> PersonalSyncAutoSyncDelayOptions { get; } =
    [
        new(0, "禁用自动同步"),
        new(2, "修改后 2 秒"),
        new(3, "修改后 3 秒"),
        new(5, "修改后 5 秒"),
        new(10, "修改后 10 秒"),
        new(20, "修改后 20 秒"),
        new(30, "修改后 30 秒"),
        new(60, "修改后 1 分钟"),
        new(120, "修改后 2 分钟")
    ];

    private static readonly IReadOnlyList<MouseTriggerOption> StandardMouseTriggerTargetOptions =
    [
        new("None", "禁用"),
        new("Panel", "鼠标面板"),
        new("Radial", "燕环"),
        new("Yanm", "燕幕")
    ];

    private static readonly IReadOnlyList<MouseTriggerOption> GestureMouseTriggerTargetOptions =
    [
        new("None", "禁用"),
        new("Panel", "鼠标面板"),
        new("Radial", "燕环"),
        new("Yanm", "燕幕"),
        new("WindowSnap", "窗口排列"),
        new("Gesture", "鼠标手势")
    ];

    private static readonly IReadOnlyList<MouseGestureTemplateDefinition> CommonMouseGestureTemplates =
    [
        new("↑", "上划", "适合返回顶部、打开常用入口"),
        new("↓", "下划", "适合关闭、隐藏或最小化"),
        new("←", "左划", "适合后退、上一项"),
        new("→", "右划", "适合前进、下一项"),
        new("↓→", "L", "适合打开目录、窗口操作"),
        new("→↓←", "C", "适合复制、剪贴板动作"),
        new("↑→↓←", "P", "适合打开面板或固定扩展"),
        new("→↓←↑", "S", "适合搜索、选择类动作")
    ];


    // ==========================================
    //            Velopack 自动更新集成
    // ==========================================
    private string _updateCheckStatusText = "未检测";
    private bool _hasNewVersion;
    private bool _isCheckingUpdate;
    private bool _isDownloadingUpdate;
    private int _updateProgressValue;
    private bool _updateDownloaded;
    private string _newVersionInfo = "";
    private Velopack.UpdateInfo? _newVersionUpdateInfo;
    private bool _updateEventsSubscribed;

    public string UpdateCheckStatusText
    {
        get => _updateCheckStatusText;
        set { _updateCheckStatusText = value; OnPropertyChanged(); }
    }

    public bool HasNewVersion
    {
        get => _hasNewVersion;
        set { _hasNewVersion = value; OnPropertyChanged(); }
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        set { _isCheckingUpdate = value; OnPropertyChanged(); }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set { _isDownloadingUpdate = value; OnPropertyChanged(); }
    }

    public int UpdateProgressValue
    {
        get => _updateProgressValue;
        set { _updateProgressValue = value; OnPropertyChanged(); }
    }

    public bool UpdateDownloaded
    {
        get => _updateDownloaded;
        set { _updateDownloaded = value; OnPropertyChanged(); }
    }

    public string NewVersionInfo
    {
        get => _newVersionInfo;
        set { _newVersionInfo = value; OnPropertyChanged(); }
    }

    private void SubscribeUpdateEvents()
    {
        if (_updateEventsSubscribed) return;
        _updateEventsSubscribed = true;

        VelopackUpdateService.Instance.UpdateStatusChanged += status =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateCheckStatusText = status;
            });
        };

        VelopackUpdateService.Instance.DownloadProgressChanged += progress =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateProgressValue = progress;
                if (progress >= 100)
                {
                    IsDownloadingUpdate = false;
                    UpdateDownloaded = true;
                }
            });
        };

        VelopackUpdateService.Instance.UpdateReadyChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsUpdateReadyToRestart));
            });
        };
    }

    private async Task AutoCheckUpdateOnAboutOpenAsync()
    {
        SubscribeUpdateEvents();
        
        if (UpdateDownloaded) return;
        if (IsCheckingUpdate || IsDownloadingUpdate) return;

        IsCheckingUpdate = true;
        try
        {
            var updateInfo = await VelopackUpdateService.Instance.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                _newVersionUpdateInfo = updateInfo;
                NewVersionInfo = updateInfo.TargetFullRelease.Version.ToString();
                HasNewVersion = true;
            }
            else
            {
                HasNewVersion = false;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SettingsWindow: AutoCheckUpdateOnAboutOpen failed: {ex}");
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async void CheckForUpdatesManualButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SubscribeUpdateEvents();

        if (IsCheckingUpdate || IsDownloadingUpdate) return;
        
        UpdateDownloaded = false;
        HasNewVersion = false;
        IsCheckingUpdate = true;
        
        try
        {
            var updateInfo = await VelopackUpdateService.Instance.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                _newVersionUpdateInfo = updateInfo;
                NewVersionInfo = updateInfo.TargetFullRelease.Version.ToString();
                HasNewVersion = true;
            }
            else
            {
                HasNewVersion = false;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SettingsWindow: ManualCheckForUpdates failed: {ex}");
            System.Windows.MessageBox.Show(
                $"检测更新发生异常: {ex.Message}\n\n详细诊断日志已记录至:\n{HostAssets.HostLogPath}\n\n如需反馈，请将该日志文件一并发送给开发者。",
                "更新提示",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async void DownloadUpdatesButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_newVersionUpdateInfo == null) return;
        
        IsDownloadingUpdate = true;
        UpdateProgressValue = 0;
        UpdateDownloaded = false;

        try
        {
            var success = await VelopackUpdateService.Instance.DownloadUpdatesAsync(_newVersionUpdateInfo);
            if (success)
            {
                UpdateDownloaded = true;
            }
            else
            {
                UpdateDownloaded = false;
                var result = System.Windows.MessageBox.Show(
                    "增量文件下载合并失败，可能是由于网络连接异常。\n\n是否需要手动前往浏览器发布页面下载最新完整安装包？", 
                    "更新提示", 
                    System.Windows.MessageBoxButton.YesNo, 
                    System.Windows.MessageBoxImage.Warning);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://github.com/luoluoluo22/yanzi/releases",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception exLaunch)
                    {
                        HostAssets.AppendLog($"SettingsWindow: Failed to launch release URL: {exLaunch.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SettingsWindow: DownloadUpdates failed: {ex}");
            var result = System.Windows.MessageBox.Show(
                $"更新包下载发生异常: {ex.Message}\n\n是否需要手动前往浏览器发布页面下载最新完整安装包？",
                "更新提示",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/luoluoluo22/yanzi/releases",
                        UseShellExecute = true
                    });
                }
                catch (Exception exLaunch)
                {
                    HostAssets.AppendLog($"SettingsWindow: Failed to launch release URL: {exLaunch.Message}");
                }
            }
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private void ApplyUpdatesAndRestartButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_newVersionUpdateInfo == null) return;
        try
        {
            VelopackUpdateService.Instance.ApplyAndRestart(_newVersionUpdateInfo);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"应用更新并重启失败: {ex.Message}。您可以重试，或者手动重启软件完成更新。", "更新提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void RestartToUpdateButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var updateInfo = VelopackUpdateService.Instance.ReadyUpdateInfo ?? _newVersionUpdateInfo;
        if (updateInfo == null)
        {
            System.Windows.MessageBox.Show("更新信息不存在，无法执行重启。请前往关于面板手动检测并下载更新。", "更新提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            VelopackUpdateService.Instance.ApplyAndRestart(updateInfo);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"应用更新并重启失败: {ex.Message}。您可以重试，或者手动重启软件完成更新。", "更新提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void BrowseBackupDirectoryButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var currentDir = string.IsNullOrWhiteSpace(_settings.CustomBackupDirectory)
            ? Path.Combine(HostAssets.DataRootPath, "Backups")
            : _settings.CustomBackupDirectory;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择自动备份文件保存目录",
            InitialDirectory = Directory.Exists(currentDir) ? currentDir : HostAssets.DataRootPath
        };

        if (dialog.ShowDialog() == true)
        {
            CustomBackupDirectory = dialog.FolderName;
            AppSettingsStore.Save(_settings);
            _mainWindow.RefreshAppSettings();
        }
    }

    private async void CreateBackupButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出本地完整数据备份",
            Filter = "Swallow Backup File (*.zip)|*.zip",
            FileName = $"manual_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            var savePath = saveFileDialog.FileName;

            try
            {
                await Task.Run(() => BackupService.CreateBackup(savePath));
                System.Windows.MessageBox.Show("数据备份已成功导出！", "备份提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出备份失败: {ex.Message}", "备份提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }
    }

    private async void ImportBackupButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入本地完整数据备份",
            Filter = "Swallow Backup File (*.zip)|*.zip"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            var result = System.Windows.MessageBox.Show(
                "导入备份会覆盖您当前所有的配置和扩展插件！此操作无法撤销。\n\n在导入前，我们会自动关闭 Everything 引擎。导入成功后应用将自动重启。\n\n是否确定要导入该备份？",
                "导入提示",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null) btn.IsEnabled = false;

                var openPath = openFileDialog.FileName;

                try
                {
                    EverythingRuntimeService.KillAllYanziEverythingProcesses();
                    await Task.Run(() => BackupService.RestoreBackup(openPath));

                    System.Windows.MessageBox.Show("数据恢复成功！程序将立即重启以加载新数据。", "导入成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                    System.Diagnostics.Process.Start(System.Environment.ProcessPath!);
                    System.Windows.Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"导入失败: {ex.Message}", "导入失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    EverythingRuntimeService.EnsureStartedInBackground();
                }
                finally
                {
                    if (btn != null) btn.IsEnabled = true;
                }
            }
        }
    }

    private void AutoBackupFrequencyComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
    }

    public SettingsNavigationItem? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (Equals(value, _selectedNavigation))
            {
                return;
            }

            _selectedNavigation = value;
            HostAssets.AppendLog($"Settings navigation selected: key={_selectedNavigation?.Key ?? "null"}");
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSectionTitle));
            OnPropertyChanged(nameof(SelectedSectionDescription));
            OnPropertyChanged(nameof(IsNormalSettingsVisible));
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAiSelected));
            OnPropertyChanged(nameof(IsEnvironmentSelected));
            OnPropertyChanged(nameof(IsSyncSelected));
            OnPropertyChanged(nameof(IsExtensionsSelected));
            OnPropertyChanged(nameof(IsRecycleBinSelected));
            OnPropertyChanged(nameof(IsQuickPanelSelected));
            OnPropertyChanged(nameof(IsMouseGesturesSelected));
            OnPropertyChanged(nameof(IsRadialSelected));
            OnPropertyChanged(nameof(IsYarnSelectSelected));
            OnPropertyChanged(nameof(IsYanmSelected));
            OnPropertyChanged(nameof(IsYanwoSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
            RefreshSelectedSectionHighlights();
            if (IsExtensionsSelected && !_hasLoadedExtensions)
            {
                _ = RefreshExtensionsFromDiskAsync();
            }
            else if (IsRecycleBinSelected && !_hasLoadedExtensions)
            {
                _ = RefreshExtensionsFromDiskAsync();
            }
            else if (IsSyncSelected)
            {
                RefreshSyncActivityLog();
                RefreshAccountObjectSyncStatus();
                RefreshPersonalExtensionSyncStatus();
                _ = RefreshPersonalSyncCommitsAsync();
                _ = RefreshPersonalConfigRestorePointsAsync();
            }
            else if (IsRadialSelected)
            {
                EnsureRadialEditorLoaded();
            }
            else if (IsMouseGesturesSelected)
            {
                RefreshMouseGestureManagement();
            }
            else if (IsAboutSelected)
            {
                _ = AutoCheckUpdateOnAboutOpenAsync();
            }

            if (!IsExtensionsSelected)
            {
                ClearSelectedExtensionItem();
            }

            Dispatcher.BeginInvoke(RefreshSelectedSectionHighlights, DispatcherPriority.Background);
        }
    }

    public SettingsExtensionItem? SelectedExtensionItem
    {
        get => _selectedExtensionItem;
        private set
        {
            if (ReferenceEquals(_selectedExtensionItem, value))
            {
                return;
            }

            if (_selectedExtensionItem != null)
            {
                _selectedExtensionItem.IsSelected = false;
            }

            _selectedExtensionItem = value;

            if (_selectedExtensionItem != null)
            {
                _selectedExtensionItem.IsSelected = true;
            }

            UpdateExtensionDetailPanelState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExtensionDetailOpen));
        }
    }

    public bool IsExtensionDetailOpen => SelectedExtensionItem != null;

    public double ExtensionCardWidth
    {
        get => _extensionCardWidth;
        private set
        {
            if (Math.Abs(_extensionCardWidth - value) < 0.5)
            {
                return;
            }

            _extensionCardWidth = value;
            OnPropertyChanged();
        }
    }

    public bool EnableAgentApi
    {
        get => _settings.EnableAgentApi;
        set
        {
            if (value == _settings.EnableAgentApi)
            {
                return;
            }

            _settings = _settings with { EnableAgentApi = value };
            OnPropertyChanged();
        }
    }

    public int AgentApiPort
    {
        get => _settings.AgentApiPort;
        set
        {
            if (value == _settings.AgentApiPort)
            {
                return;
            }

            _settings = _settings with { AgentApiPort = value };
            OnPropertyChanged();
        }
    }

    public new string ThemeMode
    {
        get => _settings.ThemeMode;
        set
        {
            if (string.Equals(value, _settings.ThemeMode, StringComparison.Ordinal))
            {
                return;
            }

            _settings = _settings with { ThemeMode = value };
            OnPropertyChanged();
            App.ApplyTheme(value);
        }
    }

    public bool RefreshCloudOnStartup
    {
        get => _settings.RefreshCloudOnStartup;
        set
        {
            if (value == _settings.RefreshCloudOnStartup)
            {
                return;
            }

            _settings = _settings with { RefreshCloudOnStartup = value };
            OnPropertyChanged();
        }
    }

    public bool LaunchAtStartup
    {
        get => _settings.LaunchAtStartup;
        set
        {
            if (value == _settings.LaunchAtStartup)
            {
                return;
            }

            _settings = _settings with { LaunchAtStartup = value };
            OnPropertyChanged();
        }
    }

    public bool EnableEverything
    {
        get => _settings.EnableEverything;
        set
        {
            if (value == _settings.EnableEverything)
            {
                return;
            }

            _settings = _settings with { EnableEverything = value };
            OnPropertyChanged();
            OnPropertyChanged(nameof(EverythingRunningStatusText));
            OnPropertyChanged(nameof(EverythingRunningStatusBrush));
            if (value)
            {
                _ = Task.Run(async () =>
                {
                    EverythingRuntimeService.EnsureRunning();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(EverythingRunningStatusText));
                        OnPropertyChanged(nameof(EverythingRunningStatusBrush));
                        _mainWindow.RefreshAppSettings();
                    });
                });
            }
            else
            {
                EverythingRuntimeService.StopOwnedRuntime();
                EverythingRuntimeService.KillAllYanziEverythingProcesses();
                _mainWindow.RefreshAppSettings();
            }
        }
    }

    public string EverythingRunningStatusText => _settings.EnableEverything
        ? "服务已启用（Everything 后台运行中）"
        : "服务已停用（Everything 已退出）";

    public System.Windows.Media.Brush EverythingRunningStatusBrush => _settings.EnableEverything
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));

    private void RebuildEverythingIndexButton_Click(object sender, RoutedEventArgs e)
    {
        EverythingRuntimeService.RebuildDatabaseAndRestart();
        System.Windows.MessageBox.Show("已触发 Everything 索引数据库重建，正在后台重新扫描全盘...", "重建索引", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OpenEverythingDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(HostAssets.EverythingRuntimeDataPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = HostAssets.EverythingRuntimeDataPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打开目录失败: {ex.Message}");
        }
    }

    private void LoadLogoImage()
    {
        try
        {
            AboutLogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/logo-white.png", UriKind.Absolute));
        }
        catch
        {
            // Ignore logo load failures so settings can still open in published builds.
        }
    }

    public bool EnableAutoUpdate
    {
        get => _settings.EnableAutoUpdate;
        set
        {
            if (value == _settings.EnableAutoUpdate)
            {
                return;
            }

            _settings = _settings with { EnableAutoUpdate = value };
            OnPropertyChanged();
        }
    }

    private string _backupStatusText = string.Empty;

    public string AutoBackupFrequency
    {
        get => _settings.AutoBackupFrequency;
        set
        {
            if (value == _settings.AutoBackupFrequency) return;
            _settings = _settings with { AutoBackupFrequency = value };
            OnPropertyChanged();
        }
    }

    public string CustomBackupDirectory
    {
        get => string.IsNullOrWhiteSpace(_settings.CustomBackupDirectory) 
            ? "默认 (数据根目录\\Backups)" 
            : _settings.CustomBackupDirectory;
        set
        {
            if (value == _settings.CustomBackupDirectory) return;
            _settings = _settings with { CustomBackupDirectory = value };
            OnPropertyChanged();
            UpdateBackupStatusText();
        }
    }

    public string BackupStatusText
    {
        get => _backupStatusText;
        private set
        {
            if (value == _backupStatusText) return;
            _backupStatusText = value;
            OnPropertyChanged();
        }
    }

    private void UpdateBackupStatusText()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastAutoBackupTime))
        {
            BackupStatusText = "自动备份就绪。当前尚无最近备份记录。";
        }
        else
        {
            BackupStatusText = $"上次自动备份时间: {_settings.LastAutoBackupTime}";
        }
    }

    public bool IsUpdateReadyToRestart => VelopackUpdateService.Instance.IsUpdateReady;

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (value == _settings.CloseToTray)
            {
                return;
            }

            _settings = _settings with { CloseToTray = value };
            OnPropertyChanged();
        }
    }

    public bool EnableWindowSnapAssist
    {
        get => _settings.EnableWindowSnapAssist;
        set
        {
            if (value == _settings.EnableWindowSnapAssist)
            {
                return;
            }

            _settings = _settings with { EnableWindowSnapAssist = value };
            OnPropertyChanged();
            _mainWindow.SetWindowSnapAssistEnabled(value);
        }
    }

    public string WindowSnapAssistHotkey
    {
        get => string.IsNullOrWhiteSpace(_settings.WindowSnapAssistHotkey) ? "设置快捷键" : _settings.WindowSnapAssistHotkey;
        private set
        {
            _settings = _settings with { WindowSnapAssistHotkey = value };
            NotifySnapAssistHotkeyDisplayChanged();
        }
    }

    public string SnapAssistRecorderText => _isRecordingSnapAssistHotkey ? "请按下快捷键" : WindowSnapAssistHotkey;

    public System.Windows.Media.Brush SnapAssistRecorderForeground => _isRecordingSnapAssistHotkey
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF7DD3FC"))
        : string.IsNullOrWhiteSpace(_settings.WindowSnapAssistHotkey)
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF9CA3AF"))
            : (System.Windows.Media.Brush)FindResource("BrushTextMain");

    public string AccountTitle
    {
        get => _accountTitle;
        private set
        {
            if (value == _accountTitle)
            {
                return;
            }

            _accountTitle = value;
            OnPropertyChanged();
        }
    }

    public string AccountSubtitle
    {
        get => _accountSubtitle;
        private set
        {
            if (value == _accountSubtitle)
            {
                return;
            }

            _accountSubtitle = value;
            OnPropertyChanged();
        }
    }

    public string AccountInitial
    {
        get => _accountInitial;
        private set
        {
            if (value == _accountInitial)
            {
                return;
            }

            _accountInitial = value;
            OnPropertyChanged();
        }
    }

    public bool IsAccountLoggedIn
    {
        get => _isAccountLoggedIn;
        private set
        {
            if (value == _isAccountLoggedIn)
            {
                return;
            }

            _isAccountLoggedIn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SignInButtonText));
            OnPropertyChanged(nameof(SignInMenuText));
        }
    }

    public string SignInButtonText => IsAccountLoggedIn ? "切换账号" : "登录账号";

    public string SignInMenuText => IsAccountLoggedIn ? "切换账号" : "登录账号";

    public string BaseUrl { get; }

    public string ExtensionsRootPath { get; }

    public string AppVersionText { get; }

    public string LauncherHotkey
    {
        get => _launcherHotkey;
        private set
        {
            if (value == _launcherHotkey)
            {
                return;
            }

            _launcherHotkey = value;
            OnPropertyChanged();
            NotifyLauncherHotkeyDisplayChanged();
        }
    }

    public string LauncherRecorderText => _isRecordingLauncherHotkey ? "请按下快捷键" : GetLauncherHotkeyDisplayText();

    public System.Windows.Media.Brush LauncherRecorderForeground => _isRecordingLauncherHotkey
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF7DD3FC"))
        : string.IsNullOrWhiteSpace(_launcherHotkey)
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF9CA3AF"))
            : (System.Windows.Media.Brush)FindResource("BrushTextMain");

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set
        {
            if (value == _syncStatusText)
            {
                return;
            }

            _syncStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool EnableWebDavSync
    {
        get => EnablePersonalSync;
        set
        {
            EnablePersonalSync = value;
            OnPropertyChanged();
        }
    }

    public bool EnableLanSync
    {
        get => _settings.EnableLanSync;
        set
        {
            if (value == _settings.EnableLanSync) return;
            _settings = _settings with { EnableLanSync = value };
            OnPropertyChanged();
            AppSettingsStore.Save(_settings);
            UpdateMobileStatusUI(value);
        }
    }

    private void UpdateMobileStatusUI(bool isEnabled)
    {
        if (MobileStatusDot == null || MobileStatusText == null) return;
        
        if (isEnabled)
        {
            MobileStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            string mobileText = "直连监听中";
            if (!string.IsNullOrEmpty(LocalAgentApiServer.LastKnownMobileDeviceModel))
            {
                mobileText = $"已连接: {LocalAgentApiServer.LastKnownMobileDeviceModel}";
            }
            MobileStatusText.Text = mobileText;
            MobileStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            MobileStatusText.Tag = "Connected";

            if (MobileToolTipStatusText != null)
            {
                MobileToolTipStatusText.Text = mobileText;
                MobileToolTipStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            }
        }
        else
        {
            MobileStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            MobileStatusText.Text = "已禁用";
            MobileStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            MobileStatusText.Tag = "Disconnected";

            if (MobileToolTipStatusText != null)
            {
                MobileToolTipStatusText.Text = "已禁用";
                MobileToolTipStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            }
        }
    }

    public bool EnableBrowserHelper
    {
        get => _settings.EnableBrowserHelper;
        set
        {
            if (value == _settings.EnableBrowserHelper) return;
            _settings = _settings with { EnableBrowserHelper = value };
            OnPropertyChanged();
            AppSettingsStore.Save(_settings);
        }
    }

    public bool EnableWanPush
    {
        get => _settings.EnableWanPush;
        set
        {
            if (value == _settings.EnableWanPush) return;
            _settings = _settings with { EnableWanPush = value };
            OnPropertyChanged();
            AppSettingsStore.Save(_settings);
        }
    }

    private DispatcherTimer ShowSaveStatusTemporarily(DispatcherTimer? existingTimer, Action<bool> setVisibleAction)
    {
        setVisibleAction(true);
        existingTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            setVisibleAction(false);
        };
        timer.Start();
        return timer;
    }

    private DispatcherTimer? _wanPushSaveTimer;
    private DispatcherTimer? _wanPushStatusHideTimer;
    private bool _isWanPushSaveStatusVisible;

    public bool IsWanPushSaveStatusVisible
    {
        get => _isWanPushSaveStatusVisible;
        private set
        {
            if (_isWanPushSaveStatusVisible == value) return;
            _isWanPushSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    private void QueueWanPushSave(int delayMs = 500)
    {
        if (_wanPushSaveTimer == null)
        {
            _wanPushSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _wanPushSaveTimer.Tick += (s, e) => { _wanPushSaveTimer.Stop(); SaveWanPushUuid(); };
        }
        else
        {
            _wanPushSaveTimer.Stop();
            _wanPushSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _wanPushSaveTimer.Start();
    }

    private void FlushWanPushSave()
    {
        if (_wanPushSaveTimer != null && _wanPushSaveTimer.IsEnabled)
        {
            _wanPushSaveTimer.Stop();
            SaveWanPushUuid();
        }
    }

    public string WanPushUuid
    {
        get => _settings.WanPushUuid;
        set
        {
            if (value == _settings.WanPushUuid) return;
            _settings = _settings with { WanPushUuid = value };
            OnPropertyChanged();
            QueueWanPushSave(500);
        }
    }

    private void SaveWanPushUuidButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWanPushUuid();
    }

    private void SaveWanPushUuid()
    {
        AppSettingsStore.Save(_settings);
        _wanPushStatusHideTimer = ShowSaveStatusTemporarily(_wanPushStatusHideTimer, visible => IsWanPushSaveStatusVisible = visible);
    }

    private void EnableLanSync_Click(object sender, RoutedEventArgs e)
    {
        if (EnableLanSync)
        {
            var result = System.Windows.MessageBox.Show(
                "开启局域网直连需要向 Windows 防火墙添加例外，并允许 HTTP 端口监听。\n系统将弹出一个 UAC 管理员权限请求。\n\n是否继续？",
                "局域网直连提权说明",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                EnableLanSync = false;
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netsh http add urlacl url=http://*:{_settings.AgentApiPort}/ user=Everyone & netsh advfirewall firewall add rule name=\"Yanzi Agent API\" dir=in action=allow protocol=TCP localport={_settings.AgentApiPort} & netsh advfirewall firewall add rule name=\"Yanzi Discovery\" dir=in action=allow protocol=UDP localport=42980",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi)?.WaitForExit();
                
                System.Windows.MessageBox.Show("配置完成！请重启应用使设置生效。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"提权失败或被取消：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                EnableLanSync = false;
            }
        }
        else
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netsh http delete urlacl url=http://*:{_settings.AgentApiPort}/ & netsh advfirewall firewall delete rule name=\"Yanzi Agent API\" & netsh advfirewall firewall delete rule name=\"Yanzi Discovery\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch
            {
                // Ignore if user cancels removal
            }
        }
    }

    public string WebDavServerUrl
    {
        get => _personalSyncSettings.WebDav.Url;
        set
        {
            if (value == _personalSyncSettings.WebDav.Url)
            {
                return;
            }

            _personalSyncSettings.WebDav.Url = value;
            OnPropertyChanged();
        }
    }

    public string WebDavRootPath
    {
        get => _personalSyncSettings.WebDav.PathPrefix;
        set
        {
            if (value == _personalSyncSettings.WebDav.PathPrefix)
            {
                return;
            }

            _personalSyncSettings.WebDav.PathPrefix = value;
            OnPropertyChanged();
        }
    }

    public string WebDavUsername
    {
        get => _personalSyncSettings.WebDav.Username;
        set
        {
            if (value == _personalSyncSettings.WebDav.Username)
            {
                return;
            }

            _personalSyncSettings.WebDav.Username = value;
            OnPropertyChanged();
        }
    }

    public string WebDavStatusText
    {
        get => _webDavStatusText;
        private set
        {
            if (value == _webDavStatusText)
            {
                return;
            }

            _webDavStatusText = value;
            OnPropertyChanged();
        }
    }

    public string SyncActivityLogText
    {
        get => _syncActivityLogText;
        private set
        {
            if (value == _syncActivityLogText)
            {
                return;
            }

            _syncActivityLogText = value;
            OnPropertyChanged();
        }
    }

    public string PersonalSyncCommitStatusText
    {
        get => _personalSyncCommitStatusText;
        private set
        {
            if (value == _personalSyncCommitStatusText)
            {
                return;
            }

            _personalSyncCommitStatusText = value;
            OnPropertyChanged();
        }
    }

    public string PersonalConfigRestoreStatusText
    {
        get => _personalConfigRestoreStatusText;
        private set
        {
            if (value == _personalConfigRestoreStatusText) return;
            _personalConfigRestoreStatusText = value;
            OnPropertyChanged();
        }
    }

    public string PersonalExtensionSyncStatusText
    {
        get => _personalExtensionSyncStatusText;
        private set
        {
            if (value == _personalExtensionSyncStatusText) return;
            _personalExtensionSyncStatusText = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionDataSyncStatusText
    {
        get => _extensionDataSyncStatusText;
        private set
        {
            if (value == _extensionDataSyncStatusText) return;
            _extensionDataSyncStatusText = value;
            OnPropertyChanged();
        }
    }

    public string AiBaseUrl
    {
        get => _aiBaseUrl;
        set
        {
            if (value == _aiBaseUrl)
            {
                return;
            }

            _aiBaseUrl = value;
            OnPropertyChanged();
            CheckAiSettingsChanged();
        }
    }

    public string AiApiKey
    {
        get => _aiApiKey;
        set
        {
            if (value == _aiApiKey)
            {
                return;
            }

            _aiApiKey = value;
            OnPropertyChanged();
            CheckAiSettingsChanged();
        }
    }

    public string AiModel
    {
        get => _aiModel;
        set
        {
            if (value == _aiModel)
            {
                return;
            }

            _aiModel = value;
            OnPropertyChanged();
            CheckAiSettingsChanged();
        }
    }

    public string AiSystemPrompt
    {
        get => _aiSystemPrompt;
        set
        {
            if (value == _aiSystemPrompt)
            {
                return;
            }

            _aiSystemPrompt = value;
            OnPropertyChanged();
            CheckAiSettingsChanged();
        }
    }

    public bool HasAiSettingsChanged
    {
        get => _hasAiSettingsChanged;
        private set
        {
            if (value == _hasAiSettingsChanged)
            {
                return;
            }

            _hasAiSettingsChanged = value;
            OnPropertyChanged();
        }
    }

    public string AiSettingsStatusText
    {
        get => _aiSettingsStatusText;
        private set
        {
            if (value == _aiSettingsStatusText)
            {
                return;
            }

            _aiSettingsStatusText = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<SettingsAiProviderVM> AiServiceProvidersList => _aiServiceProvidersList;

    public string ProviderSearchText
    {
        get => _providerSearchText;
        set
        {
            if (_providerSearchText != value)
            {
                _providerSearchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredProviders));
            }
        }
    }

    public IEnumerable<SettingsAiProviderVM> FilteredProviders
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProviderSearchText))
                return AiServiceProvidersList;
            return AiServiceProvidersList.Where(p => p.Name.Contains(ProviderSearchText, StringComparison.OrdinalIgnoreCase));
        }
    }

    public SettingsAiProviderVM? SelectedServiceProvider
    {
        get => _selectedServiceProvider;
        set
        {
            if (_selectedServiceProvider != value)
            {
                if (_selectedServiceProvider != null)
                {
                    _selectedServiceProvider.PropertyChanged -= SelectedServiceProvider_PropertyChanged;
                }
                
                _selectedServiceProvider = value;
                
                if (_selectedServiceProvider != null)
                {
                    _selectedServiceProvider.PropertyChanged += SelectedServiceProvider_PropertyChanged;
                }
                
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetailsVisibility));
                OnPropertyChanged(nameof(SelectPromptVisibility));
            }
        }
    }

    public Visibility DetailsVisibility => SelectedServiceProvider == null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SelectPromptVisibility => SelectedServiceProvider == null ? Visibility.Visible : Visibility.Collapsed;

    public string CheckApiKeyButtonText
    {
        get => _checkApiKeyButtonText;
        set { _checkApiKeyButtonText = value; OnPropertyChanged(); }
    }

    private void SelectedServiceProvider_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        HasAiSettingsChanged = true;
    }


    public int PersonalSyncAutoSyncDelaySeconds
    {
        get => _settings.PersonalSyncAutoSyncDelaySeconds;
        set
        {
            var normalized = NormalizePersonalSyncAutoSyncDelay(value);
            if (normalized == _settings.PersonalSyncAutoSyncDelaySeconds)
            {
                return;
            }

            _settings = _settings with { PersonalSyncAutoSyncDelaySeconds = normalized };
            _mainWindow.SavePersonalSyncAutoSyncDelaySeconds(normalized);
            OnPropertyChanged();
        }
    }

    private void UpdatePersonalSyncValue(string? nextValue, Action<string> apply, string currentValue, [CallerMemberName] string propertyName = "")
    {
        var normalized = nextValue ?? string.Empty;
        if (string.Equals(normalized, currentValue, StringComparison.Ordinal))
        {
            return;
        }

        apply(normalized);
        OnPropertyChanged(propertyName);
    }

    public bool EnablePersonalSync
    {
        get => _personalSyncSettings.Enabled;
        set
        {
            if (value == _personalSyncSettings.Enabled)
            {
                return;
            }

            _personalSyncSettings.Enabled = value;
            OnPropertyChanged();
            RefreshWebDavSummary();
        }
    }

    public string SelectedPersonalSyncProvider
    {
        get => PersonalSyncProviders.Normalize(_personalSyncSettings.Provider);
        set
        {
            var normalized = PersonalSyncProviders.Normalize(value);
            if (normalized == PersonalSyncProviders.None || normalized == PersonalSyncProviders.Normalize(_personalSyncSettings.Provider))
            {
                return;
            }

            _personalSyncSettings.Provider = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPersonalSyncProviderDisplayName));
            OnPropertyChanged(nameof(PersonalSyncActionButtonText));
            OnPropertyChanged(nameof(SelectedPersonalSyncProviderQuickLinkText));
            OnPropertyChanged(nameof(SelectedPersonalSyncProviderQuickLinkUrl));
            OnPropertyChanged(nameof(HasSelectedPersonalSyncProviderQuickLink));
            OnPropertyChanged(nameof(IsSyncProviderGitHub));
            OnPropertyChanged(nameof(IsSyncProviderGitee));
            OnPropertyChanged(nameof(IsSyncProviderGitLab));
            OnPropertyChanged(nameof(IsSyncProviderGitea));
            OnPropertyChanged(nameof(IsSyncProviderS3));
            OnPropertyChanged(nameof(IsSyncProviderWebDav));

            OnPropertyChanged(nameof(IsGitSyncProvider));
            RefreshWebDavSummary();

            _ = RefreshPersonalSyncCommitsAsync();
        }
    }

    public bool IsSyncProviderGitHub => SelectedPersonalSyncProvider == PersonalSyncProviders.GitHub;

    public bool IsSyncProviderGitee => SelectedPersonalSyncProvider == PersonalSyncProviders.Gitee;

    public bool IsSyncProviderGitLab => SelectedPersonalSyncProvider == PersonalSyncProviders.GitLab;

    public bool IsSyncProviderGitea => SelectedPersonalSyncProvider == PersonalSyncProviders.Gitea;

    public bool IsSyncProviderS3 => SelectedPersonalSyncProvider == PersonalSyncProviders.S3;

    public bool IsSyncProviderWebDav => SelectedPersonalSyncProvider == PersonalSyncProviders.WebDav;

    public bool IsGitSyncProvider => IsSyncProviderGitHub || IsSyncProviderGitee || IsSyncProviderGitLab || IsSyncProviderGitea;

    private string _syncActiveSubTab = "cloud";
    public string SyncActiveSubTab
    {
        get => _syncActiveSubTab;
        set
        {
            if (_syncActiveSubTab == value)
            {
                return;
            }

            _syncActiveSubTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSyncCloudTabActive));
            OnPropertyChanged(nameof(IsSyncBackupTabActive));
            OnPropertyChanged(nameof(IsSyncHistoryTabActive));
        }
    }

    public bool IsSyncCloudTabActive => SyncActiveSubTab == "cloud";
    public bool IsSyncBackupTabActive => SyncActiveSubTab == "backup";
    public bool IsSyncHistoryTabActive => SyncActiveSubTab == "history";

    private bool _isAccountSyncObjectsExpanded;
    public bool IsAccountSyncObjectsExpanded
    {
        get => _isAccountSyncObjectsExpanded;
        set
        {
            if (_isAccountSyncObjectsExpanded == value)
            {
                return;
            }

            _isAccountSyncObjectsExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccountSyncObjectsToggleText));
        }
    }

    public string AccountSyncObjectsToggleText => IsAccountSyncObjectsExpanded ? "收起明细 ▴" : "查看明细 ▾";

    public bool ShowPersonalSyncAdvancedOptions
    {
        get => _showPersonalSyncAdvancedOptions;
        set
        {
            if (value == _showPersonalSyncAdvancedOptions)
            {
                return;
            }

            _showPersonalSyncAdvancedOptions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PersonalSyncAdvancedOptionsButtonText));
        }
    }

    public string PersonalSyncAdvancedOptionsButtonText => ShowPersonalSyncAdvancedOptions ? "隐藏高级配置" : "显示高级配置";

    public string SelectedPersonalSyncProviderDisplayName => PersonalSyncProviders.GetDisplayName(SelectedPersonalSyncProvider);

    public string PersonalSyncActionButtonText => $"立即同步至 {SelectedPersonalSyncProviderDisplayName}";

    public string SelectedPersonalSyncProviderQuickLinkText => SelectedPersonalSyncProvider switch
    {
        var provider when provider == PersonalSyncProviders.GitHub => "新建 GitHub Token",
        var provider when provider == PersonalSyncProviders.Gitee => "新建 Gitee Token",
        var provider when provider == PersonalSyncProviders.GitLab => "新建 GitLab Token",
        var provider when provider == PersonalSyncProviders.Gitea => "打开 Gitea 应用令牌",
        var provider when provider == PersonalSyncProviders.S3 => "打开 AWS IAM 安全凭证",
        var provider when provider == PersonalSyncProviders.WebDav => "打开坚果云安全设置",
        _ => string.Empty
    };

    public string SelectedPersonalSyncProviderQuickLinkUrl => SelectedPersonalSyncProvider switch
    {
        var provider when provider == PersonalSyncProviders.GitHub => "https://github.com/settings/tokens/new",
        var provider when provider == PersonalSyncProviders.Gitee => "https://gitee.com/profile/personal_access_tokens/new",
        var provider when provider == PersonalSyncProviders.GitLab => "https://gitlab.com/-/user_settings/personal_access_tokens",
        var provider when provider == PersonalSyncProviders.Gitea => "https://gitea.com/user/settings/applications",
        var provider when provider == PersonalSyncProviders.S3 => "https://console.aws.amazon.com/iam/home#/security_credentials",
        var provider when provider == PersonalSyncProviders.WebDav => "https://www.jianguoyun.com/#/account/security",
        _ => string.Empty
    };

    public bool HasSelectedPersonalSyncProviderQuickLink => !string.IsNullOrWhiteSpace(SelectedPersonalSyncProviderQuickLinkUrl);

    public string GitHubSyncOwner
    {
        get => _personalSyncSettings.GitHub.Username;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitHub.Username = current, _personalSyncSettings.GitHub.Username);
    }

    public string GitHubSyncRepo
    {
        get => _personalSyncSettings.GitHub.Repo;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitHub.Repo = current, _personalSyncSettings.GitHub.Repo);
    }

    public string GitHubSyncBranch
    {
        get => _personalSyncSettings.GitHub.Branch;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitHub.Branch = current, _personalSyncSettings.GitHub.Branch);
    }

    public string GitHubSyncPathPrefix
    {
        get => _personalSyncSettings.GitHub.PathPrefix;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitHub.PathPrefix = current, _personalSyncSettings.GitHub.PathPrefix);
    }

    public string GiteeSyncUsername
    {
        get => _personalSyncSettings.Gitee.Username;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitee.Username = current, _personalSyncSettings.Gitee.Username);
    }

    public string GiteeSyncRepo
    {
        get => _personalSyncSettings.Gitee.Repo;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitee.Repo = current, _personalSyncSettings.Gitee.Repo);
    }

    public string GiteeSyncBranch
    {
        get => _personalSyncSettings.Gitee.Branch;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitee.Branch = current, _personalSyncSettings.Gitee.Branch);
    }

    public string GiteeSyncPathPrefix
    {
        get => _personalSyncSettings.Gitee.PathPrefix;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitee.PathPrefix = current, _personalSyncSettings.Gitee.PathPrefix);
    }

    public string GitLabSyncBaseUrl
    {
        get => _personalSyncSettings.GitLab.BaseUrl;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitLab.BaseUrl = current, _personalSyncSettings.GitLab.BaseUrl);
    }

    public string GitLabSyncProjectPath
    {
        get => _personalSyncSettings.GitLab.ProjectPath;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitLab.ProjectPath = current, _personalSyncSettings.GitLab.ProjectPath);
    }

    public string GitLabSyncBranch
    {
        get => _personalSyncSettings.GitLab.Branch;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitLab.Branch = current, _personalSyncSettings.GitLab.Branch);
    }

    public string GitLabSyncPathPrefix
    {
        get => _personalSyncSettings.GitLab.PathPrefix;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.GitLab.PathPrefix = current, _personalSyncSettings.GitLab.PathPrefix);
    }

    public string GiteaSyncBaseUrl
    {
        get => _personalSyncSettings.Gitea.BaseUrl;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitea.BaseUrl = current, _personalSyncSettings.Gitea.BaseUrl);
    }

    public string GiteaSyncUsername
    {
        get => _personalSyncSettings.Gitea.Username;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitea.Username = current, _personalSyncSettings.Gitea.Username);
    }

    public string GiteaSyncRepo
    {
        get => _personalSyncSettings.Gitea.Repo;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitea.Repo = current, _personalSyncSettings.Gitea.Repo);
    }

    public string GiteaSyncBranch
    {
        get => _personalSyncSettings.Gitea.Branch;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitea.Branch = current, _personalSyncSettings.Gitea.Branch);
    }

    public string GiteaSyncPathPrefix
    {
        get => _personalSyncSettings.Gitea.PathPrefix;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.Gitea.PathPrefix = current, _personalSyncSettings.Gitea.PathPrefix);
    }

    public string S3SyncAccessKeyId
    {
        get => _personalSyncSettings.S3.AccessKeyId;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.S3.AccessKeyId = current, _personalSyncSettings.S3.AccessKeyId);
    }

    public string S3SyncRegion
    {
        get => _personalSyncSettings.S3.Region;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.S3.Region = current, _personalSyncSettings.S3.Region);
    }

    public string S3SyncBucket
    {
        get => _personalSyncSettings.S3.Bucket;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.S3.Bucket = current, _personalSyncSettings.S3.Bucket);
    }

    public string S3SyncEndpoint
    {
        get => _personalSyncSettings.S3.Endpoint;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.S3.Endpoint = current, _personalSyncSettings.S3.Endpoint);
    }

    public string S3SyncPathPrefix
    {
        get => _personalSyncSettings.S3.PathPrefix;
        set => UpdatePersonalSyncValue(value, current => _personalSyncSettings.S3.PathPrefix = current, _personalSyncSettings.S3.PathPrefix);
    }

    public ObservableCollection<EnvironmentVariableEditorItem> EnvironmentVariables { get; }

    public string EnvironmentStatusText
    {
        get => _environmentStatusText;
        private set
        {
            if (value == _environmentStatusText)
            {
                return;
            }

            _environmentStatusText = value;
            OnPropertyChanged();
        }
    }

    public string RecycleBinSummary
    {
        get => _recycleBinSummary;
        private set
        {
            if (value == _recycleBinSummary)
            {
                return;
            }

            _recycleBinSummary = value;
            OnPropertyChanged();
        }
    }

    public string LocalExtensionSummary
    {
        get => _localExtensionSummary;
        private set
        {
            if (value == _localExtensionSummary)
            {
                return;
            }

            _localExtensionSummary = value;
            OnPropertyChanged();
        }
    }

    private bool _isSearchPopupOpen;
    public bool IsSearchPopupOpen
    {
        get => _isSearchPopupOpen;
        set
        {
            if (_isSearchPopupOpen == value) return;
            _isSearchPopupOpen = value;
            OnPropertyChanged(nameof(IsSearchPopupOpen));
        }
    }

    private string _highlightKeyword = string.Empty;
    public string HighlightKeyword
    {
        get => _highlightKeyword;
        set
        {
            if (_highlightKeyword == value) return;
            _highlightKeyword = value;
            OnPropertyChanged(nameof(HighlightKeyword));
            RefreshSelectedSectionHighlights();
        }
    }

    public string SettingsSearchText
    {
        get => _settingsSearchText;
        set
        {
            if (value == _settingsSearchText)
            {
                return;
            }

            _settingsSearchText = value;
            OnPropertyChanged();
            ApplySettingsSearch(value);
        }
    }

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            OnPropertyChanged();
            RefreshExtensionItems();
        }
    }

    public string RadialMenuSearchText
    {
        get => _radialMenuSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _radialMenuSearchText)
            {
                return;
            }

            _radialMenuSearchText = value;
            OnPropertyChanged();
            RefreshRadialMenuCommandCandidates(value);
        }
    }

    public string RadialMenuSelectedSlotSummary => _selectedRadialMenuSlot == null
        ? "先点击左侧轮盘槽位，再搜索并添加；也可以右键槽位打开菜单。"
        : $"当前槽位：{_selectedRadialMenuSlot.Label} · 可添加扩展、程序、系统设置项，或右键添加子环。";

    public string RecycleBinSearchText
    {
        get => _recycleBinSearchText;
        set
        {
            if (value == _recycleBinSearchText)
            {
                return;
            }

            _recycleBinSearchText = value;
            OnPropertyChanged();
            RefreshRecycleBinItems();
        }
    }

    public string ExtensionSearchSummary =>
        IsExtensionsLoading
            ? "正在刷新..."
            : _extensionFilterMode == "recycle"
            ? RecycleBinSearchSummary
            : ExtensionItems.Count == 0
            ? "无匹配项"
            : $"显示 {ExtensionItems.Count} 项";

    public string RecycleBinSearchSummary =>
        IsExtensionsLoading
            ? "正在刷新..."
            : RecycleBinItems.Count == 0
            ? "无匹配项"
            : $"显示 {RecycleBinItems.Count} 项";

    public bool IsExtensionsLoading
    {
        get => _isExtensionsLoading;
        private set
        {
            if (value == _isExtensionsLoading)
            {
                return;
            }

            _isExtensionsLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExtensionsLoadingVisibility));
            OnPropertyChanged(nameof(ExtensionsListVisibility));
            OnPropertyChanged(nameof(RecycleBinListVisibility));
            OnPropertyChanged(nameof(CanRefreshExtensions));
            OnPropertyChanged(nameof(ExtensionSearchSummary));
            OnPropertyChanged(nameof(RecycleBinSearchSummary));
        }
    }

    public Visibility ExtensionsLoadingVisibility => IsExtensionsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ExtensionsListVisibility => IsExtensionsLoading || _extensionFilterMode == "recycle" ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RecycleBinListVisibility => IsExtensionsLoading || _extensionFilterMode != "recycle" ? Visibility.Collapsed : Visibility.Visible;

    public bool CanRefreshExtensions => !IsExtensionsLoading;

    public bool TriggerMiddleButtonDown
    {
        get => _settings.QuickPanelMouseTriggers.MiddleButtonDown;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.MiddleButtonDown = value);
    }

    public bool TriggerX1ButtonDown
    {
        get => _settings.QuickPanelMouseTriggers.X1ButtonDown;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.X1ButtonDown = value);
    }

    public bool TriggerX2ButtonDown
    {
        get => _settings.QuickPanelMouseTriggers.X2ButtonDown;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.X2ButtonDown = value);
    }

    public bool TriggerCtrlLeftClick
    {
        get => _settings.QuickPanelMouseTriggers.CtrlLeftClick;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.CtrlLeftClick = value);
    }

    public bool TriggerCtrlRightClick
    {
        get => _settings.QuickPanelMouseTriggers.CtrlRightClick;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.CtrlRightClick = value);
    }

    public bool TriggerMiddleButtonLongPress
    {
        get => _settings.QuickPanelMouseTriggers.MiddleButtonLongPress;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.MiddleButtonLongPress = value);
    }

    public bool TriggerRightButtonLongPress
    {
        get => _settings.QuickPanelMouseTriggers.RightButtonLongPress;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.RightButtonLongPress = value);
    }

    public bool TriggerRightButtonDrag
    {
        get => _settings.QuickPanelMouseTriggers.RightButtonDrag;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.RightButtonDrag = value);
    }

    public bool TriggerHorizontalWheel
    {
        get => _settings.QuickPanelMouseTriggers.HorizontalWheel;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.HorizontalWheel = value);
    }



    public bool ExecuteOnButtonRelease
    {
        get => _settings.QuickPanelMouseTriggers.ExecuteOnButtonRelease;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.ExecuteOnButtonRelease = value);
    }

    public bool EnableRadialMenu
    {
        get => _settings.RadialMenu.Enabled;
        set => UpdateRadialMenu(value, settings => settings.Enabled = value);
    }

    public bool EnableRadialCapsLockHold
    {
        get => _settings.RadialMenu.TriggerCapsLockHold;
        set => UpdateRadialMenu(value, settings => settings.TriggerCapsLockHold = value);
    }

    public string RadialActivationKey
    {
        get => RadialActivationKeys.Normalize(_settings.RadialMenu.ActivationKey);
        set => UpdateRadialMenu(RadialActivationKeys.Normalize(value), settings => settings.ActivationKey = RadialActivationKeys.Normalize(value));
    }

    public bool RadialUsesCustomShortcut => string.Equals(RadialActivationKey, RadialActivationKeys.Custom, StringComparison.OrdinalIgnoreCase);

    public string RadialCustomShortcut
    {
        get => _settings.RadialMenu.CustomShortcut;
        set => UpdateRadialMenu(value, settings => settings.CustomShortcut = value);
    }

    private DispatcherTimer? _quickPanelSaveTimer;
    private DispatcherTimer? _quickPanelStatusHideTimer;
    private bool _isQuickPanelSaveStatusVisible;

    public bool IsQuickPanelSaveStatusVisible
    {
        get => _isQuickPanelSaveStatusVisible;
        private set
        {
            if (_isQuickPanelSaveStatusVisible == value) return;
            _isQuickPanelSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    public string GlobalServiceBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.GlobalServiceBlacklistedProcesses ?? []);
        set
        {
            _settings.GlobalServiceBlacklistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
            QueueQuickPanelTriggerSave(500);
        }
    }

    public string RadialBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.RadialMenu.BlacklistedProcesses ?? []);
        set
        {
            _settings.RadialMenu.BlacklistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
            QueueQuickPanelTriggerSave(500);
        }
    }

    public string RadialWhitelistedProcessesText
    {
        get => string.Join(", ", _settings.RadialMenu.WhitelistedProcesses ?? []);
        set
        {
            _settings.RadialMenu.WhitelistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
            QueueQuickPanelTriggerSave(500);
        }
    }

    public string SelectedRadialMenuPageId
    {
        get
        {
            _settings.RadialMenu ??= new RadialMenuSettings();
            _settings.RadialMenu.Pages ??= [];
            if (_settings.RadialMenu.Pages.Count == 0)
            {
                return string.Empty;
            }

            if (_settings.RadialMenu.Pages.Any(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                return _settings.RadialMenu.SelectedPageId;
            }

            _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
            return _settings.RadialMenu.SelectedPageId;
        }
        set
        {
            value ??= string.Empty;
            if (_isRefreshingRadialMenu ||
                string.IsNullOrWhiteSpace(value) ||
                value == _settings.RadialMenu.SelectedPageId)
            {
                return;
            }

            SaveRadialMenuSlots();
            _settings.RadialMenu.SelectedPageId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRadialMenuPageName));
            RefreshRadialMenuSlots();
        }
    }

    public string SelectedRadialMenuPageName =>
        _settings.RadialMenu?.Pages?.FirstOrDefault(page => page.Id.Equals(SelectedRadialMenuPageId, StringComparison.OrdinalIgnoreCase))?.Name
        ?? "默认";

    public bool EnableYarnSelect
    {
        get => _settings.YarnSelect.Enabled;
        set => UpdateYarnSelect(value, settings => settings.Enabled = value);
    }

    public bool YarnSelectCopy
    {
        get => _settings.YarnSelect.LeftCToCopy;
        set => UpdateYarnSelect(value, settings => settings.LeftCToCopy = value);
    }

    public bool YarnSelectCut
    {
        get => _settings.YarnSelect.LeftXToCut;
        set => UpdateYarnSelect(value, settings => settings.LeftXToCut = value);
    }

    public bool YarnSelectPaste
    {
        get => _settings.YarnSelect.LeftVToPaste;
        set => UpdateYarnSelect(value, settings => settings.LeftVToPaste = value);
    }

    public bool YarnSelectSearch
    {
        get => _settings.YarnSelect.LeftSToSearch;
        set => UpdateYarnSelect(value, settings => settings.LeftSToSearch = value);
    }

    public bool YarnSelectRun
    {
        get => _settings.YarnSelect.LeftRToRun;
        set => UpdateYarnSelect(value, settings => settings.LeftRToRun = value);
    }

    public bool YarnSelectSmartCopyPaste
    {
        get => _settings.YarnSelect.LeftRightSmartCopyPaste;
        set => UpdateYarnSelect(value, settings => settings.LeftRightSmartCopyPaste = value);
    }

    public bool YarnSelectSidePaste
    {
        get => _settings.YarnSelect.LeftSideButtonPaste;
        set => UpdateYarnSelect(value, settings => settings.LeftSideButtonPaste = value);
    }

    private DispatcherTimer? _yarnSelectSaveTimer;
    private DispatcherTimer? _yarnSelectStatusHideTimer;
    private bool _isYarnSelectSaveStatusVisible;

    public bool IsYarnSelectSaveStatusVisible
    {
        get => _isYarnSelectSaveStatusVisible;
        private set
        {
            if (_isYarnSelectSaveStatusVisible == value) return;
            _isYarnSelectSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    public string YarnSelectBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.YarnSelect.BlacklistedProcesses ?? []);
        set
        {
            _settings.YarnSelect.BlacklistedProcesses = (value ?? string.Empty)
                .Split([',', ';', '，', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            OnPropertyChanged();
            QueueYarnSelectSave(500);
        }
    }

    public string YarnSelectWhitelistedProcessesText
    {
        get => string.Join(", ", _settings.YarnSelect.WhitelistedProcesses ?? []);
        set
        {
            _settings.YarnSelect.WhitelistedProcesses = (value ?? string.Empty)
                .Split([',', ';', '，', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            OnPropertyChanged();
            QueueYarnSelectSave(500);
        }
    }

    public string YarnSelectSummary
    {
        get
        {
            if (!_settings.YarnSelect.Enabled)
            {
            return "燕选已关闭。";
            }

            var whitelist = _settings.YarnSelect.WhitelistedProcesses ?? [];
            if (whitelist.Count > 0)
            {
                return $"燕选已启用，仅对白名单程序生效：{string.Join(", ", whitelist)}。";
            }

            var labels = (_settings.YarnSelect.Rules ?? [])
                .Where(static rule => rule.Enabled)
                .Select(rule => $"左键+{rule.TriggerKey} {GetYarnSelectActionLabel(rule.ActionType)}")
                .ToList();
            return labels.Count == 0 ? "燕选已启用，但没有开启任何动作。" : string.Join("、", labels);
        }
    }

    public bool EnableYanm
    {
        get => _settings.Yanm.Enabled;
        set => UpdateYanm(value, settings => settings.Enabled = value);
    }

    public string YanmActivationKey
    {
        get => YanmActivationKeys.Normalize(_settings.Yanm.ActivationKey);
        set => UpdateYanm(YanmActivationKeys.Normalize(value), settings => settings.ActivationKey = YanmActivationKeys.Normalize(value));
    }

    public bool YanmUsesCustomShortcut => string.Equals(YanmActivationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase);

    public string YanmCustomShortcut
    {
        get => _settings.Yanm.CustomShortcut;
        set => UpdateYanm(value, settings => settings.CustomShortcut = value);
    }

    private DispatcherTimer? _yanmSaveTimer;
    private DispatcherTimer? _yanmStatusHideTimer;
    private bool _isYanmSaveStatusVisible;

    public bool IsYanmSaveStatusVisible
    {
        get => _isYanmSaveStatusVisible;
        private set
        {
            if (_isYanmSaveStatusVisible == value) return;
            _isYanmSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    public string YanmBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.Yanm.BlacklistedProcesses ?? []);
        set
        {
            _settings.Yanm.BlacklistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
            QueueYanmSave(500);
        }
    }

    private static List<string> ParseProcessList(string? value) =>
        (value ?? string.Empty)
        .Split([',', ';', '，', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public string YanmWhitelistedProcessesText
    {
        get => string.Join(", ", _settings.Yanm.WhitelistedProcesses ?? []);
        set
        {
            _settings.Yanm.WhitelistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
            QueueYanmSave(500);
        }
    }

    public bool YanmTriggerHold
    {
        get => _settings.Yanm.TriggerWinHold;
        set => UpdateYanm(value, settings => settings.TriggerWinHold = value);
    }

    public bool YanmTriggerDoubleTap
    {
        get => _settings.Yanm.TriggerWinDoubleTap;
        set => UpdateYanm(value, settings => settings.TriggerWinDoubleTap = value);
    }

    public bool YanmMouseTriggerRightDrag
    {
        get => _settings.Yanm.TriggerRightButtonDrag;
        set => UpdateYanm(value, settings => settings.TriggerRightButtonDrag = value);
    }

    public string YanmMouseTriggerMode
    {
        get => MouseTriggerModes.Normalize(_settings.Yanm.MouseTriggerMode);
        set => UpdateYanm(MouseTriggerModes.Normalize(value), settings => settings.MouseTriggerMode = MouseTriggerModes.Normalize(value));
    }

    public string RadialMouseTriggerMode
    {
        get => MouseTriggerModes.Normalize(_settings.RadialMenu.MouseTriggerMode);
        set => UpdateRadialMenu(value, settings => settings.MouseTriggerMode = MouseTriggerModes.Normalize(value));
    }

    public string YanmSummary
    {
        get
        {
            if (!_settings.Yanm.Enabled)
            {
                return "燕幕已关闭。";
            }

            var key = YanmActivationKey;
            var actions = new List<string>();
            if (YanmUsesCustomShortcut)
            {
                actions.Add(string.IsNullOrWhiteSpace(_settings.Yanm.CustomShortcut)
                    ? "自定义快捷键未录制"
                    : $"按下 {_settings.Yanm.CustomShortcut} 显示");
            }
            else
            {
                if (_settings.Yanm.TriggerWinHold) actions.Add($"按住 {key} 临时显示");
                if (_settings.Yanm.TriggerWinDoubleTap) actions.Add($"双击 {key} 固定显示");
            }
            if (YanmAssignedMouseTriggerSummary != "未分配")
            {
                actions.Add($"鼠标：{YanmAssignedMouseTriggerSummary}");
            }
            return actions.Count == 0 ? "燕幕已启用，但没有开启触发方式。" : string.Join("；", actions);
        }
    }

    public string QuickPanelTriggerSummary
    {
        get
        {
            var labels = new List<string>();
            var trigger = _settings.QuickPanelMouseTriggers;
            if (trigger.MiddleButtonDown) labels.Add("按下中键");
            if (trigger.X1ButtonDown) labels.Add("按下 X1 键");
            if (trigger.X2ButtonDown) labels.Add("按下 X2 键");
            if (trigger.CtrlLeftClick) labels.Add("Ctrl+左键单击");
            if (trigger.CtrlRightClick) labels.Add("Ctrl+右键单击");
            if (trigger.MiddleButtonLongPress) labels.Add("长按中键");
            if (trigger.RightButtonLongPress) labels.Add("长按右键");
            if (trigger.RightButtonDrag) labels.Add("按右键移动");
            if (trigger.MiddleButtonDrag) labels.Add("按中键移动");
            if (trigger.HorizontalWheel) labels.Add("滚轮左右");

            return labels.Count == 0 ? "未启用鼠标触发，默认回退为长按中键。" : string.Join("、", labels);
        }
    }

    public string MouseGestureTriggerSummary => MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode) switch
    {
        MouseGestureTriggerModes.RightDrag => "按住右键移动",
        MouseGestureTriggerModes.MiddleDrag => "按住中键移动",
        _ => "未启用"
    };

    public string MouseGestureManagementSummary
    {
        get
        {
            var count = MouseGestureItems?.Count ?? 0;
            var trigger = MouseGestureTriggerSummary;
            return count == 0
                ? $"当前没有扩展绑定鼠标手势。全局触发方式：{trigger}。"
                : $"当前有 {count} 个扩展绑定鼠标手势。全局触发方式：{trigger}。";
        }
    }

    public Visibility MouseGestureEmptyVisibility =>
        MouseGestureItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string MouseGestureTriggerMode
    {
        get => MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode);
        set => UpdateMouseGestureTriggerMode(value);
    }

    public string RadialAssignedMouseTriggerSummary => BuildAssignedMouseTriggerSummary(
    [
        (_settings.RadialMenu.TriggerMiddleButtonDown, "按下中键"),
        (_settings.RadialMenu.TriggerX1ButtonDown, "按下 X1 键"),
        (_settings.RadialMenu.TriggerX2ButtonDown, "按下 X2 键"),
        (_settings.RadialMenu.TriggerCtrlLeftClick, "Ctrl+左键单击"),
        (_settings.RadialMenu.TriggerCtrlRightClick, "Ctrl+右键单击"),
        (_settings.RadialMenu.TriggerCtrlMiddleClick, "Ctrl+中键单击"),
        (_settings.RadialMenu.TriggerMiddleButtonLongPress, "长按中键"),
        (_settings.RadialMenu.TriggerRightButtonLongPress, "长按右键"),
        (_settings.RadialMenu.TriggerRightButtonDrag, "按右键移动"),
        (_settings.RadialMenu.TriggerMiddleButtonDrag, "按中键移动"),
        (_settings.RadialMenu.TriggerHorizontalWheel, "滚轮左右")
    ]);

    public string YanmAssignedMouseTriggerSummary => BuildAssignedMouseTriggerSummary(
    [
        (_settings.Yanm.TriggerMiddleButtonDown, "按下中键"),
        (_settings.Yanm.TriggerX1ButtonDown, "按下 X1 键"),
        (_settings.Yanm.TriggerX2ButtonDown, "按下 X2 键"),
        (_settings.Yanm.TriggerCtrlLeftClick, "Ctrl+左键单击"),
        (_settings.Yanm.TriggerCtrlRightClick, "Ctrl+右键单击"),
        (_settings.Yanm.TriggerCtrlMiddleClick, "Ctrl+中键单击"),
        (_settings.Yanm.TriggerMiddleButtonLongPress, "长按中键"),
        (_settings.Yanm.TriggerRightButtonLongPress, "长按右键"),
        (_settings.Yanm.TriggerRightButtonDrag, "按右键移动"),
        (_settings.Yanm.TriggerMiddleButtonDrag, "按中键移动"),
        (_settings.Yanm.TriggerHorizontalWheel, "滚轮左右")
    ]);

    public string RadialMenuSummary => _settings.RadialMenu.Enabled
        ? $"燕环已启用：键盘触发 {GetRadialActivationKeyDisplay()}；鼠标触发 {RadialAssignedMouseTriggerSummary}；支持滚轮切页、子环和搜索配置。"
        : "燕环未启用：当前仍使用传统鼠标面板。";

    private string GetRadialActivationKeyDisplay()
    {
        return RadialActivationKeys.Normalize(_settings.RadialMenu.ActivationKey) switch
        {
            RadialActivationKeys.Win when _settings.RadialMenu.TriggerCapsLockHold => "按住 Win",
            RadialActivationKeys.CapsLock when _settings.RadialMenu.TriggerCapsLockHold => "按住 CapsLock",
            RadialActivationKeys.Custom => string.IsNullOrWhiteSpace(_settings.RadialMenu.CustomShortcut) ? "自定义快捷键未录制" : $"按下 {_settings.RadialMenu.CustomShortcut}",
            _ => "未启用"
        };
    }

    private static string BuildAssignedMouseTriggerSummary(IEnumerable<(bool Enabled, string Label)> triggers)
    {
        var labels = triggers
            .Where(static item => item.Enabled)
            .Select(static item => item.Label)
            .ToList();

        return labels.Count == 0 ? "未分配" : string.Join("、", labels);
    }

    private string MouseTriggerLabel(string mode)
    {
        return MouseTriggerOptions.FirstOrDefault(option => string.Equals(option.Value, mode, StringComparison.OrdinalIgnoreCase))?.Label ?? mode;
    }

    public string SelectedSectionTitle => SelectedNavigation?.Title ?? "Settings";

    public string SelectedSectionDescription => SelectedNavigation?.Key switch
    {
        "general" => "控制燕子(Swallow)的基础行为，包括启动同步和托盘停驻策略。",
        "ai" => "配置 AI 对话使用的本地或远程兼容接口，包括地址、Key 和模型名。",
        "environment" => "配置 Notion、第三方 API 和应用型扩展可读取的用户环境变量。",
        "sync" => "管理云账号状态、同步入口和当前服务端连接信息。",
        "extensions" => "查看本地扩展目录和当前机器已发现的扩展数量。",
        "recycle" => "查看已删除扩展，支持恢复和彻底删除。",
        "quickpanel" => "统一分配鼠标动作给面板、燕环、燕幕、窗口排列和鼠标手势，避免触发方式重叠。",
        "mousegestures" => "管理扩展使用的鼠标轨迹，快速查看冲突并把常用手势绑定到扩展。",
        "radial" => "配置燕环的启用状态、键盘触发和轮盘内容；鼠标触发只在“鼠标触发”页统一分配。",
        "yarnselect" => "按住左键选中文本时，用字母或鼠标键快速复制、搜索、运行或粘贴。",
        "yanm" => "配置全局信息层燕幕，包括启用状态、按住显示和双击固定的触发键；鼠标触发只做只读展示。",
        "about" => "查看当前版本与这套设置窗口的结构定位。",
        _ => "燕子设置"
    };

    public bool IsGeneralSelected => SelectedNavigation?.Key == "general";

    public bool IsNormalSettingsVisible => !IsAiSelected && !IsExtensionsSelected;

    public bool IsAiSelected => SelectedNavigation?.Key == "ai";

    public bool IsEnvironmentSelected => SelectedNavigation?.Key == "environment";

    public bool IsSyncSelected => SelectedNavigation?.Key == "sync";

    public bool IsExtensionsSelected => SelectedNavigation?.Key == "extensions";

    public bool IsRecycleBinSelected => SelectedNavigation?.Key == "recycle";

    public bool IsQuickPanelSelected => SelectedNavigation?.Key == "quickpanel";

    public bool IsMouseGesturesSelected => SelectedNavigation?.Key == "mousegestures";

    public bool IsRadialSelected => SelectedNavigation?.Key == "radial";

    public bool IsYarnSelectSelected => SelectedNavigation?.Key == "yarnselect";

    public bool IsYanmSelected => SelectedNavigation?.Key == "yanm";

    public bool IsYanwoSelected => SelectedNavigation?.Key == "yanwo";

    public bool IsAboutSelected => SelectedNavigation?.Key == "about";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NavigateTo(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return;
        }

        var target = NavigationItems.FirstOrDefault(item =>
            item.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            SelectedNavigation = target;
        }
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog($"SettingsWindow Loaded. opacity={Opacity}, visibility={Visibility}, actualWidth={ActualWidth}, actualHeight={ActualHeight}.");
        // 初始化原始AI设置值
        _originalAiBaseUrl = _settings.AiBaseUrl;
        _originalAiApiKey = _settings.AiApiKey;
        _originalAiModel = _settings.AiModel;
        _originalAiSystemPrompt = _settings.AiSystemPrompt;
        
        RefreshAccountSummary();
        RefreshQuickPanelTriggerBindings();
        RefreshYarnSelectBindings();
        if (IsRadialSelected)
        {
            EnsureRadialEditorLoaded();
        }
        OnPropertyChanged(nameof(RadialMouseTriggerMode));
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshVisibleSectionData();
        
        // Initialize gesture card colors
        InitializeMouseTriggerTargetDropdowns();
        UpdateAllGestureCardColors();
        ScheduleExtensionCardWidthUpdate();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            HostAssets.AppendLog($"SettingsWindow deferred UI refresh. opacity={Opacity}, isLoaded={IsLoaded}, isVisible={IsVisible}.");
            RebuildDynamicSettingsSearchItems();
            RefreshSelectedSectionHighlights();
        }), DispatcherPriority.Loaded);

        UpdateMobileStatusUI(_settings.EnableLanSync);

        var app = System.Windows.Application.Current as App;
        if (app != null && app.AgentApiServer != null)
        {
            UpdateBrowserStatusUI(app.AgentApiServer.IsBrowserConnected);
            app.AgentApiServer.BrowserConnectionChanged += AgentApiServer_BrowserConnectionChanged;
            LocalAgentApiServer.MobileDeviceConnected += LocalAgentApiServer_MobileDeviceConnected;
        }
    }

    private void LocalAgentApiServer_MobileDeviceConnected(string deviceName)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateMobileStatusUI(_settings.EnableLanSync);
        });
    }

    private void AgentApiServer_BrowserConnectionChanged(bool isConnected)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateBrowserStatusUI(isConnected);
        });
    }

    private void UpdateBrowserStatusUI(bool isConnected)
    {
        if (BrowserStatusDot == null || BrowserStatusText == null) return;
        
        if (isConnected)
        {
            var app = System.Windows.Application.Current as App;
            var browserName = (app?.AgentApiServer != null) ? app.AgentApiServer.ConnectedBrowserName : "浏览器";
            if (string.IsNullOrEmpty(browserName)) browserName = "浏览器";

            BrowserStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            BrowserStatusText.Text = $"已连接: {browserName}";
            BrowserStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            BrowserStatusText.Tag = "Connected";

            if (BrowserToolTipStatusText != null)
            {
                BrowserToolTipStatusText.Text = $"已连接: {browserName}";
                BrowserToolTipStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            }
        }
        else
        {
            BrowserStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            BrowserStatusText.Text = "未连接";
            BrowserStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            BrowserStatusText.Tag = "Disconnected";

            if (BrowserToolTipStatusText != null)
            {
                BrowserToolTipStatusText.Text = "未连接";
                BrowserToolTipStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            }
        }
    }

    public void ReloadSettingsFromDisk()
    {
        if (Dispatcher.CheckAccess())
        {
            DoReloadSettingsFromDisk();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(DoReloadSettingsFromDisk));
        }
    }

    private void DoReloadSettingsFromDisk()
    {
        _settings = AppSettingsStore.Load();
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.Yanm ??= new YanmSettings();
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(RefreshCloudOnStartup));
        OnPropertyChanged(nameof(EnableEverything));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(EnableYanm));
        OnPropertyChanged(nameof(YanmActivationKey));
        OnPropertyChanged(nameof(YanmTriggerHold));
        OnPropertyChanged(nameof(YanmTriggerDoubleTap));
        OnPropertyChanged(nameof(YanmMouseTriggerMode));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(RadialMouseTriggerMode));
        LauncherHotkey = _settings.LauncherHotkey;
        RefreshQuickPanelTriggerBindings();
        RefreshYarnSelectBindings();
        if (IsRadialSelected || _hasInitializedRadialEditor)
        {
            EnsureRadialEditorLoaded(forceRefresh: true);
        }
        EnableWebDavSync = _settings.EnableWebDavSync;
        WebDavServerUrl = string.IsNullOrWhiteSpace(_settings.WebDavServerUrl) ? "https://dav.jianguoyun.com/dav/" : _settings.WebDavServerUrl;
        WebDavRootPath = _settings.WebDavRootPath;
        WebDavUsername = _settings.WebDavUsername;
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSystemPrompt = _settings.AiSystemPrompt;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        
        // 加载已保存的密码
        var credential = WebDavCredentialStore.Load();
        if (credential != null && !string.IsNullOrWhiteSpace(credential.Password))
        {
            WebDavPasswordBox.Password = credential.Password;
        }
        else
        {
            WebDavPasswordBox.Password = string.Empty;
        }
        
        RefreshAccountSummary();
        RefreshWebDavSummary();
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshVisibleSectionData();
        
        // Refresh gesture card colors after settings reload
        InitializeMouseTriggerTargetDropdowns();
        UpdateAllGestureCardColors();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        if (_isRenamingRadialMenuPage)
        {
            HostAssets.AppendLog("Settings activated skipped during radial page rename.");
            return;
        }

        if (_suspendActivationRefresh)
        {
            HostAssets.AppendLog("Settings activated skipped during modal slot edit.");
            return;
        }

        DoReloadSettingsFromDisk();
    }

    private void EnsureRadialEditorLoaded(bool forceRefresh = false)
    {
        if (_hasInitializedRadialEditor && !forceRefresh)
        {
            return;
        }

        RefreshRadialMenuSlots();
        _hasInitializedRadialEditor = true;
    }

    private void RefreshVisibleSectionData()
    {
        // 不再自动刷新扩展列表，避免频繁刷新
        // 只在用户明确操作（点击刷新按钮、删除/恢复扩展等）时才刷新
        
        if (IsSyncSelected)
        {
            RefreshSyncActivityLog();
            RefreshAccountObjectSyncStatus();
        }
    }

    private static string BuildAiSettingsSummary(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.AiApiKey) ||
            string.IsNullOrWhiteSpace(settings.AiModel))
        {
            return "尚未配置 AI。首次使用前请填写服务地址、API Key 和模型名。";
        }

        return $"当前使用 {settings.AiModel} · {settings.AiBaseUrl}";
    }

    private string BuildEnvironmentSummary()
    {
        var count = EnvironmentVariables.Count(item => !string.IsNullOrWhiteSpace(item.Name));
        return count == 0
            ? "尚未配置环境变量。应用扩展和脚本将只能读取系统环境变量。"
            : $"已配置 {count} 个用户环境变量，脚本运行和应用扩展桥接均可读取。";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginWindowDrag();
    }

    private void WindowFrame_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.GetPosition(this).Y > 64)
        {
            return;
        }

        BeginWindowDrag();
    }

    private void BeginWindowDrag()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button is released before WPF starts the drag loop.
        }
    }

    private void ResizeBottomRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(rightDelta: e.HorizontalChange, bottomDelta: e.VerticalChange);
    }

    private void ResizeTopThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(topDelta: e.VerticalChange);
    }

    private void ResizeBottomThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(bottomDelta: e.VerticalChange);
    }

    private void ResizeLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(leftDelta: e.HorizontalChange);
    }

    private void ResizeRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(rightDelta: e.HorizontalChange);
    }

    private void ResizeTopLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(leftDelta: e.HorizontalChange, topDelta: e.VerticalChange);
    }

    private void ResizeTopRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(rightDelta: e.HorizontalChange, topDelta: e.VerticalChange);
    }

    private void ResizeBottomLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(leftDelta: e.HorizontalChange, bottomDelta: e.VerticalChange);
    }

    private void ResizeWindow(double leftDelta = 0, double topDelta = 0, double rightDelta = 0, double bottomDelta = 0)
    {
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        var newLeft = Left;
        var newTop = Top;
        var newWidth = Width;
        var newHeight = Height;

        if (leftDelta != 0)
        {
            var targetWidth = Math.Max(MinWidth, Width - leftDelta);
            newLeft = Left + (Width - targetWidth);
            newWidth = targetWidth;
        }

        if (topDelta != 0)
        {
            var targetHeight = Math.Max(MinHeight, Height - topDelta);
            newTop = Top + (Height - targetHeight);
            newHeight = targetHeight;
        }

        if (rightDelta != 0)
        {
            newWidth = Math.Max(MinWidth, newWidth + rightDelta);
        }

        if (bottomDelta != 0)
        {
            newHeight = Math.Max(MinHeight, newHeight + bottomDelta);
        }

        Left = newLeft;
        Top = newTop;
        Width = newWidth;
        Height = newHeight;
        PersistWindowBounds();
    }

    private void ApplySavedWindowBounds()
    {
        var settings = _settings;
        if (settings.SettingsWindowWidth is not > 0 || settings.SettingsWindowHeight is not > 0)
        {
            return;
        }

        _suppressWindowBoundsPersistence = true;
        try
        {
            Width = Math.Max(MinWidth, settings.SettingsWindowWidth.Value);
            Height = Math.Max(MinHeight, settings.SettingsWindowHeight.Value);

            if (settings.SettingsWindowLeft.HasValue)
            {
                Left = settings.SettingsWindowLeft.Value;
            }

            if (settings.SettingsWindowTop.HasValue)
            {
                Top = settings.SettingsWindowTop.Value;
            }
        }
        finally
        {
            _suppressWindowBoundsPersistence = false;
        }
    }

    private void SettingsWindow_BoundsChanged(object? sender, EventArgs e)
    {
        PersistWindowBoundsDebounced();
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        
        // 关闭时立即保存，不使用防抖
        _windowBoundsPersistTimer?.Stop();
        PersistWindowBounds();
        FlushYarnSelectSave();
        FlushYanmSave();
        FlushQuickPanelTriggerSave();
        FlushAiSettingsSave();
        FlushWebDavSettingsSave();
        FlushEnvironmentVariablesSave();
        FlushWanPushSave();
    }

    private void PersistWindowBoundsDebounced()
    {
        // 使用防抖机制，避免拖动时频繁保存
        if (_windowBoundsPersistTimer == null)
        {
            _windowBoundsPersistTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // 500ms 延迟
            };
            _windowBoundsPersistTimer.Tick += (s, e) =>
            {
                _windowBoundsPersistTimer?.Stop();
                PersistWindowBounds();
            };
        }

        _windowBoundsPersistTimer.Stop();
        _windowBoundsPersistTimer.Start();
    }

    private void PersistWindowBounds()
    {
        if (_suppressWindowBoundsPersistence || WindowState != WindowState.Normal)
        {
            return;
        }

        var latest = AppSettingsStore.Load();
        latest = latest with
        {
            SettingsWindowLeft = Left,
            SettingsWindowTop = Top,
            SettingsWindowWidth = Width,
            SettingsWindowHeight = Height
        };
        _settings = latest;
        AppSettingsStore.Save(latest);
    }

    private void SettingsSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox && string.IsNullOrEmpty(textBox.Text))
        {
            textBox.CaretIndex = 0;
        }
    }

    private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        if (IsLoaded)
        {
            _mainWindow.NotifyQuickPanelSettingsChanged("theme-mode-changed", refreshYanmOverlay: false);
        }
    }

    private void SaveSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        StartupRegistrationService.Apply(_settings.LaunchAtStartup);
        if (sender is FrameworkElement { DataContext: SettingsWindow })
        {
            _mainWindow.NotifyQuickPanelSettingsChanged("general-setting-changed", refreshYanmOverlay: false);
        }
    }

    private void OpenApiDocsUrlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = _settings.AgentApiPort;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{port}/docs",
                UseShellExecute = true
            });
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"无法打开浏览器: {ex.Message}");
        }
    }

    private void HotkeyRecorderBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border)
        {
            border.Focus();
            SetSnapAssistRecordingState(true);
            e.Handled = true;
        }
    }

    private void HotkeyRecorderBorder_LostFocus(object sender, RoutedEventArgs e)
    {
        SetSnapAssistRecordingState(false);
    }

    private void HotkeyRecorderBorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

        if (key == System.Windows.Input.Key.Escape)
        {
            SetSnapAssistRecordingState(false);
            System.Windows.Input.Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
            key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
            key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
            key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
        {
            e.Handled = true;
            return;
        }

        var modifiers = new List<string>();
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) modifiers.Add("Ctrl");
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) modifiers.Add("Shift");
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) modifiers.Add("Alt");
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) modifiers.Add("Win");

        string keyStr = key.ToString();
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            keyStr = (key - System.Windows.Input.Key.D0).ToString();
        else if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            keyStr = "Num" + (key - System.Windows.Input.Key.NumPad0).ToString();

        modifiers.Add(keyStr);
        string hotkey = string.Join("+", modifiers);

        if (_mainWindow.TryUpdateWindowSnapAssistHotkey(hotkey, out var message))
        {
            _settings = _mainWindow.GetCurrentAppSettings();
            NotifySnapAssistHotkeyDisplayChanged();
        }

        SetSnapAssistRecordingState(false);
        System.Windows.Input.Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void ClearSnapAssistHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow.TryUpdateWindowSnapAssistHotkey(string.Empty, out var message))
        {
            _settings = _mainWindow.GetCurrentAppSettings();
            NotifySnapAssistHotkeyDisplayChanged();
        }
        SetSnapAssistRecordingState(false);
        e.Handled = true;
    }

    private void LauncherHotkeyRecorderBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border)
        {
            border.Focus();
            SetLauncherRecordingState(true);
            e.Handled = true;
        }
    }

    private void LauncherHotkeyRecorderBorder_LostFocus(object sender, RoutedEventArgs e)
    {
        SetLauncherRecordingState(false);
    }

    private void LauncherHotkeyRecorderBorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        e.Handled = HandleLauncherRecorderKeyDown(key, GetCurrentModifiers());
    }

    private void LauncherHotkeyRecorderBorder_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        e.Handled = HandleLauncherRecorderKeyUp(key);
    }

    private void ClearLauncherHotkey_Click(object sender, RoutedEventArgs e)
    {
        _lastLauncherDoubleTapCandidate = null;
        _lastLauncherDoubleTapAtUtc = default;
        if (_mainWindow.TryUpdateLauncherHotkey(string.Empty, out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
            RefreshSyncActivityLog();
        }
        else
        {
            System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SetLauncherRecordingState(false);
        e.Handled = true;
    }

    private void CommitLauncherHotkeyShortcut(string shortcut)
    {
        if (_mainWindow.TryUpdateLauncherHotkey(shortcut, out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
            RefreshSyncActivityLog();
        }
        else
        {
            System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SetLauncherRecordingState(false);
        System.Windows.Input.Keyboard.ClearFocus();
    }

    private bool HandleLauncherRecorderKeyDown(Key key, ModifierKeys modifiers)
    {
        if (key == System.Windows.Input.Key.Escape)
        {
            SetLauncherRecordingState(false);
            System.Windows.Input.Keyboard.ClearFocus();
            return true;
        }

        if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
            key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
            key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
            key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
        {
            return true;
        }

        var shortcut = BuildStandardHotkeyString(key, modifiers);
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return true;
        }

        _lastLauncherDoubleTapCandidate = null;
        _lastLauncherDoubleTapAtUtc = default;
        CommitLauncherHotkeyShortcut(shortcut);
        return true;
    }

    private bool HandleLauncherRecorderKeyUp(Key key)
    {
        var candidateShortcut = key switch
        {
            System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl => "DoubleCtrl",
            System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt => "DoubleAlt",
            _ => null
        };

        if (candidateShortcut == null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (string.Equals(_lastLauncherDoubleTapCandidate, candidateShortcut, StringComparison.Ordinal) &&
            now - _lastLauncherDoubleTapAtUtc <= TimeSpan.FromMilliseconds(450))
        {
            _lastLauncherDoubleTapCandidate = null;
            _lastLauncherDoubleTapAtUtc = default;
            CommitLauncherHotkeyShortcut(candidateShortcut);
            return true;
        }

        _lastLauncherDoubleTapCandidate = candidateShortcut;
        _lastLauncherDoubleTapAtUtc = now;
        return true;
    }

    private void SaveQuickPanelTrigger_Click(object sender, RoutedEventArgs e)
    {
        SaveQuickPanelTriggerSettings();
    }

    private void TriggerCard_Click(object sender, MouseButtonEventArgs e)
    {
        HostAssets.AppendLog($"TriggerCard_Click fired, sender type: {sender?.GetType().Name}");
        
        if (sender is not FrameworkElement { Tag: string triggerName })
        {
            HostAssets.AppendLog($"TriggerCard_Click: sender is not FrameworkElement with Tag, sender={sender}");
            return;
        }

        HostAssets.AppendLog($"TriggerCard_Click: triggerName={triggerName}");

        // Toggle the trigger based on the tag
        switch (triggerName)
        {
            case "RightButtonLongPress":
                TriggerRightButtonLongPress = !TriggerRightButtonLongPress;
                HostAssets.AppendLog($"Toggled RightButtonLongPress to {TriggerRightButtonLongPress}");
                OnPropertyChanged(nameof(TriggerRightButtonLongPress));
                break;
            case "MiddleButtonLongPress":
                TriggerMiddleButtonLongPress = !TriggerMiddleButtonLongPress;
                HostAssets.AppendLog($"Toggled MiddleButtonLongPress to {TriggerMiddleButtonLongPress}");
                OnPropertyChanged(nameof(TriggerMiddleButtonLongPress));
                break;
            case "RightButtonDrag":
                TriggerRightButtonDrag = !TriggerRightButtonDrag;
                HostAssets.AppendLog($"Toggled RightButtonDrag to {TriggerRightButtonDrag}");
                OnPropertyChanged(nameof(TriggerRightButtonDrag));
                break;
            case "MiddleButtonDown":
                TriggerMiddleButtonDown = !TriggerMiddleButtonDown;
                HostAssets.AppendLog($"Toggled MiddleButtonDown to {TriggerMiddleButtonDown}");
                OnPropertyChanged(nameof(TriggerMiddleButtonDown));
                break;
            case "X1ButtonDown":
                TriggerX1ButtonDown = !TriggerX1ButtonDown;
                HostAssets.AppendLog($"Toggled X1ButtonDown to {TriggerX1ButtonDown}");
                OnPropertyChanged(nameof(TriggerX1ButtonDown));
                break;
            case "X2ButtonDown":
                TriggerX2ButtonDown = !TriggerX2ButtonDown;
                HostAssets.AppendLog($"Toggled X2ButtonDown to {TriggerX2ButtonDown}");
                OnPropertyChanged(nameof(TriggerX2ButtonDown));
                break;
            case "HorizontalWheel":
                TriggerHorizontalWheel = !TriggerHorizontalWheel;
                HostAssets.AppendLog($"Toggled HorizontalWheel to {TriggerHorizontalWheel}");
                OnPropertyChanged(nameof(TriggerHorizontalWheel));
                break;
            case "CtrlLeftClick":
                TriggerCtrlLeftClick = !TriggerCtrlLeftClick;
                HostAssets.AppendLog($"Toggled CtrlLeftClick to {TriggerCtrlLeftClick}");
                OnPropertyChanged(nameof(TriggerCtrlLeftClick));
                break;
            case "CtrlRightClick":
                TriggerCtrlRightClick = !TriggerCtrlRightClick;
                HostAssets.AppendLog($"Toggled CtrlRightClick to {TriggerCtrlRightClick}");
                OnPropertyChanged(nameof(TriggerCtrlRightClick));
                break;
            default:
                HostAssets.AppendLog($"Unknown trigger name: {triggerName}");
                break;
        }

        // Auto-save after toggle
        SaveQuickPanelTriggerSettings();
        HostAssets.AppendLog("TriggerCard_Click: Settings saved");
    }

    private void AssignGesture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        // Tag format: "GestureName:Target" (e.g., "RightButtonLongPress:Panel")
        var parts = tag.Split(':');
        if (parts.Length != 2)
        {
            return;
        }

        var gestureName = parts[0];
        var target = parts[1]; // None, Panel, Radial, Yanm

        HostAssets.AppendLog($"AssignGesture_Click: gesture={gestureName}, target={target}");

        // Keep each gesture assigned to only one target, but allow one target
        // to be triggered by multiple different gestures.
        ClearGestureFromAllTargets(gestureName);

        // Assign the gesture to the selected target
        AssignGestureToTarget(gestureName, target);

        // Update UI colors for all affected cards
        UpdateAllGestureCardColors();

        // Save settings
        SaveQuickPanelTriggerSettings();
        
        HostAssets.AppendLog($"Gesture {gestureName} assigned to {target}");
    }

    private void InitializeMouseTriggerTargetDropdowns()
    {
        var gestures = GetMouseTriggerGestureNames();
        foreach (var gesture in gestures)
        {
            if (_mouseTriggerTargetCombos.ContainsKey(gesture))
            {
                continue;
            }

            if (FindName($"{gesture}_None") is not System.Windows.Controls.Button noneButton ||
                noneButton.Parent is not Grid grid)
            {
                continue;
            }

            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();

            var combo = new WpfComboBox
            {
                Tag = gesture,
                Height = 28,
                MinWidth = 120,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                ItemsSource = GetMouseTriggerTargetOptions(gesture),
                DisplayMemberPath = nameof(MouseTriggerOption.Label),
                SelectedValuePath = nameof(MouseTriggerOption.Value),
                Style = TryFindResource("GlobalComboBoxStyle") as Style
            };
            combo.SelectionChanged += MouseTriggerTargetCombo_SelectionChanged;
            grid.Children.Add(combo);
            _mouseTriggerTargetCombos[gesture] = combo;
        }
    }

    private static IReadOnlyList<MouseTriggerOption> GetMouseTriggerTargetOptions(string gestureName) => gestureName switch
    {
        "RightButtonDrag" => GestureMouseTriggerTargetOptions,
        "MiddleButtonDrag" => GestureMouseTriggerTargetOptions,
        _ => StandardMouseTriggerTargetOptions
    };

    private void MouseTriggerTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMouseTriggerTargetCombos)
        {
            return;
        }

        if (sender is not WpfComboBox { Tag: string gestureName } combo)
        {
            return;
        }

        var target = combo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        ClearGestureFromAllTargets(gestureName);
        AssignGestureToTarget(gestureName, target);
        UpdateAllGestureCardColors();
        SaveQuickPanelTriggerSettings();

        HostAssets.AppendLog($"Mouse trigger target changed: gesture={gestureName}, target={target}.");
    }

    private void UpdateMouseGestureTriggerMode(string? value)
    {
        var normalized = MouseGestureTriggerModes.Normalize(value);
        if (string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), normalized, StringComparison.Ordinal))
        {
            return;
        }

        if (normalized == MouseGestureTriggerModes.RightDrag)
        {
            ClearGestureFromAllTargets("RightButtonDrag");
        }
        else if (normalized == MouseGestureTriggerModes.MiddleDrag)
        {
            ClearGestureFromAllTargets("MiddleButtonDrag");
        }

        _settings.MouseGestureTriggerMode = normalized;
        UpdateAllGestureCardColors();
        SaveQuickPanelTriggerSettings();
        OnPropertyChanged(nameof(MouseGestureTriggerMode));
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
    }

    private void RecordMouseTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MouseTriggerCaptureWindow
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var gestureName = MouseTriggerModeToGestureName(dialog.TriggerMode);
        if (string.IsNullOrWhiteSpace(gestureName))
        {
            SyncStatusText = "未识别可分配的鼠标触发方式。";
            return;
        }

        ClearGestureFromAllTargets(gestureName);
        AssignGestureToTarget(gestureName, dialog.Target);
        UpdateAllGestureCardColors();
        SaveQuickPanelTriggerSettings();
        SyncStatusText = $"已录制并分配鼠标触发：{GetMouseTriggerLabel(dialog.TriggerMode)} -> {GetTriggerTargetLabel(dialog.Target)}";
    }

    private static string MouseTriggerModeToGestureName(string mode) => MouseTriggerModes.Normalize(mode) switch
    {
        MouseTriggerModes.RightLongPress => "RightButtonLongPress",
        MouseTriggerModes.MiddleLongPress => "MiddleButtonLongPress",
        MouseTriggerModes.RightDrag => "RightButtonDrag",
        MouseTriggerModes.MiddleDrag => "MiddleButtonDrag",
        MouseTriggerModes.MiddleDown => "MiddleButtonDown",
        MouseTriggerModes.X1Down => "X1ButtonDown",
        MouseTriggerModes.X2Down => "X2ButtonDown",
        MouseTriggerModes.HorizontalWheel => "HorizontalWheel",
        MouseTriggerModes.CtrlLeftClick => "CtrlLeftClick",
        MouseTriggerModes.CtrlRightClick => "CtrlRightClick",
        MouseTriggerModes.CtrlMiddleClick => "CtrlMiddleClick",
        _ => string.Empty
    };

    private static string GetMouseTriggerLabel(string mode) => MouseTriggerModes.Normalize(mode) switch
    {
        MouseTriggerModes.RightLongPress => "长按右键",
        MouseTriggerModes.MiddleLongPress => "长按中键",
        MouseTriggerModes.RightDrag => "按右键移动",
        MouseTriggerModes.MiddleDrag => "按中键移动",
        MouseTriggerModes.MiddleDown => "按下中键",
        MouseTriggerModes.X1Down => "按下 X1 键",
        MouseTriggerModes.X2Down => "按下 X2 键",
        MouseTriggerModes.HorizontalWheel => "滚轮左右",
        MouseTriggerModes.CtrlLeftClick => "Ctrl+左键单击",
        MouseTriggerModes.CtrlRightClick => "Ctrl+右键单击",
        MouseTriggerModes.CtrlMiddleClick => "Ctrl+中键单击",
        _ => "未知触发"
    };

    private static string GetTriggerTargetLabel(string target) => target switch
    {
        "Panel" => "面板",
        "Radial" => "燕环",
        "Yanm" => "燕幕",
        "WindowSnap" => "窗口排列",
        "Gesture" => "鼠标手势",
        _ => "未分配"
    };

    private void ClearGestureFromAllTargets(string gestureName)
    {
        // Clear the gesture from QuickPanel, RadialMenu, and Yanm settings
        var trigger = _settings.QuickPanelMouseTriggers;
        var radial = _settings.RadialMenu;
        var yanm = _settings.Yanm;
        var legacyMode = GestureNameToMouseTriggerMode(gestureName);

        switch (gestureName)
        {
            case "RightButtonLongPress":
                trigger.RightButtonLongPress = false;
                radial.TriggerRightButtonLongPress = false;
                yanm.TriggerRightButtonLongPress = false;
                break;
            case "MiddleButtonLongPress":
                trigger.MiddleButtonLongPress = false;
                radial.TriggerMiddleButtonLongPress = false;
                yanm.TriggerMiddleButtonLongPress = false;
                break;
            case "RightButtonDrag":
                trigger.RightButtonDrag = false;
                radial.TriggerRightButtonDrag = false;
                yanm.TriggerRightButtonDrag = false;
                break;
            case "MiddleButtonDrag":
                trigger.MiddleButtonDrag = false;
                radial.TriggerMiddleButtonDrag = false;
                yanm.TriggerMiddleButtonDrag = false;
                break;
            case "MiddleButtonDown":
                trigger.MiddleButtonDown = false;
                radial.TriggerMiddleButtonDown = false;
                yanm.TriggerMiddleButtonDown = false;
                break;
            case "X1ButtonDown":
                trigger.X1ButtonDown = false;
                radial.TriggerX1ButtonDown = false;
                yanm.TriggerX1ButtonDown = false;
                break;
            case "X2ButtonDown":
                trigger.X2ButtonDown = false;
                radial.TriggerX2ButtonDown = false;
                yanm.TriggerX2ButtonDown = false;
                break;
            case "HorizontalWheel":
                trigger.HorizontalWheel = false;
                radial.TriggerHorizontalWheel = false;
                yanm.TriggerHorizontalWheel = false;
                break;
            case "CtrlLeftClick":
                trigger.CtrlLeftClick = false;
                radial.TriggerCtrlLeftClick = false;
                yanm.TriggerCtrlLeftClick = false;
                break;
            case "CtrlRightClick":
                trigger.CtrlRightClick = false;
                radial.TriggerCtrlRightClick = false;
                yanm.TriggerCtrlRightClick = false;
                break;
            case "CtrlMiddleClick":
                trigger.CtrlMiddleClick = false;
                radial.TriggerCtrlMiddleClick = false;
                yanm.TriggerCtrlMiddleClick = false;
                break;
        }

        if ((gestureName == "RightButtonDrag" &&
             string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.RightDrag, StringComparison.Ordinal)) ||
            (gestureName == "MiddleButtonDrag" &&
             string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.MiddleDrag, StringComparison.Ordinal)))
        {
            _settings.MouseGestureTriggerMode = MouseGestureTriggerModes.None;
        }

        if (string.Equals(MouseTriggerModes.Normalize(_settings.WindowSnapAssistMouseTriggerMode), legacyMode, StringComparison.OrdinalIgnoreCase))
        {
            _settings = _settings with { WindowSnapAssistMouseTriggerMode = MouseTriggerModes.None };
        }

        if (!string.IsNullOrWhiteSpace(legacyMode))
        {
            if (string.Equals(MouseTriggerModes.Normalize(radial.MouseTriggerMode), legacyMode, StringComparison.OrdinalIgnoreCase))
            {
                radial.MouseTriggerMode = MouseTriggerModes.None;
            }

            if (string.Equals(MouseTriggerModes.Normalize(yanm.MouseTriggerMode), legacyMode, StringComparison.OrdinalIgnoreCase))
            {
                yanm.MouseTriggerMode = MouseTriggerModes.None;
            }
        }
    }

    private static string GestureNameToMouseTriggerMode(string gestureName) => gestureName switch
    {
        "RightButtonLongPress" => MouseTriggerModes.RightLongPress,
        "MiddleButtonLongPress" => MouseTriggerModes.MiddleLongPress,
        "RightButtonDrag" => MouseTriggerModes.RightDrag,
        "MiddleButtonDrag" => MouseTriggerModes.MiddleDrag,
        "MiddleButtonDown" => MouseTriggerModes.MiddleDown,
        "X1ButtonDown" => MouseTriggerModes.X1Down,
        "X2ButtonDown" => MouseTriggerModes.X2Down,
        "HorizontalWheel" => MouseTriggerModes.HorizontalWheel,
        "CtrlLeftClick" => MouseTriggerModes.CtrlLeftClick,
        "CtrlRightClick" => MouseTriggerModes.CtrlRightClick,
        "CtrlMiddleClick" => MouseTriggerModes.CtrlMiddleClick,
        _ => MouseTriggerModes.None
    };

    private void AssignGestureToTarget(string gestureName, string target)
    {
        switch (target)
        {
            case "None":
                // Already cleared by ClearGestureFromAllTargets
                break;
            case "Panel":
                SetGestureForPanel(gestureName, true);
                break;
            case "Radial":
                SetGestureForRadial(gestureName, true);
                break;
            case "Yanm":
                SetGestureForYanm(gestureName, true);
                break;
            case "WindowSnap":
                _settings = _settings with
                {
                    WindowSnapAssistMouseTriggerMode = GestureNameToMouseTriggerMode(gestureName)
                };
                break;
            case "Gesture":
                if (gestureName == "RightButtonDrag")
                {
                    _settings.MouseGestureTriggerMode = MouseGestureTriggerModes.RightDrag;
                }
                else if (gestureName == "MiddleButtonDrag")
                {
                    _settings.MouseGestureTriggerMode = MouseGestureTriggerModes.MiddleDrag;
                }
                break;
        }
    }

    private void SetGestureForPanel(string gestureName, bool value)
    {
        var trigger = _settings.QuickPanelMouseTriggers;
        switch (gestureName)
        {
            case "RightButtonLongPress": trigger.RightButtonLongPress = value; break;
            case "MiddleButtonLongPress": trigger.MiddleButtonLongPress = value; break;
            case "RightButtonDrag": trigger.RightButtonDrag = value; break;
            case "MiddleButtonDrag": trigger.MiddleButtonDrag = value; break;
            case "MiddleButtonDown": trigger.MiddleButtonDown = value; break;
            case "X1ButtonDown": trigger.X1ButtonDown = value; break;
            case "X2ButtonDown": trigger.X2ButtonDown = value; break;
            case "HorizontalWheel": trigger.HorizontalWheel = value; break;
            case "CtrlLeftClick": trigger.CtrlLeftClick = value; break;
            case "CtrlRightClick": trigger.CtrlRightClick = value; break;
            case "CtrlMiddleClick": trigger.CtrlMiddleClick = value; break;
        }
    }

    private void SetGestureForRadial(string gestureName, bool value)
    {
        var radial = _settings.RadialMenu;
        switch (gestureName)
        {
            case "RightButtonLongPress": radial.TriggerRightButtonLongPress = value; break;
            case "MiddleButtonLongPress": radial.TriggerMiddleButtonLongPress = value; break;
            case "RightButtonDrag": radial.TriggerRightButtonDrag = value; break;
            case "MiddleButtonDrag": radial.TriggerMiddleButtonDrag = value; break;
            case "MiddleButtonDown": radial.TriggerMiddleButtonDown = value; break;
            case "X1ButtonDown": radial.TriggerX1ButtonDown = value; break;
            case "X2ButtonDown": radial.TriggerX2ButtonDown = value; break;
            case "HorizontalWheel": radial.TriggerHorizontalWheel = value; break;
            case "CtrlLeftClick": radial.TriggerCtrlLeftClick = value; break;
            case "CtrlRightClick": radial.TriggerCtrlRightClick = value; break;
            case "CtrlMiddleClick": radial.TriggerCtrlMiddleClick = value; break;
        }
    }

    private void SetGestureForYanm(string gestureName, bool value)
    {
        var yanm = _settings.Yanm;
        switch (gestureName)
        {
            case "RightButtonLongPress": yanm.TriggerRightButtonLongPress = value; break;
            case "MiddleButtonLongPress": yanm.TriggerMiddleButtonLongPress = value; break;
            case "RightButtonDrag": yanm.TriggerRightButtonDrag = value; break;
            case "MiddleButtonDrag": yanm.TriggerMiddleButtonDrag = value; break;
            case "MiddleButtonDown": yanm.TriggerMiddleButtonDown = value; break;
            case "X1ButtonDown": yanm.TriggerX1ButtonDown = value; break;
            case "X2ButtonDown": yanm.TriggerX2ButtonDown = value; break;
            case "HorizontalWheel": yanm.TriggerHorizontalWheel = value; break;
            case "CtrlLeftClick": yanm.TriggerCtrlLeftClick = value; break;
            case "CtrlRightClick": yanm.TriggerCtrlRightClick = value; break;
            case "CtrlMiddleClick": yanm.TriggerCtrlMiddleClick = value; break;
        }
    }

    private string GetGestureTarget(string gestureName)
    {
        // Check which target this gesture is assigned to
        var trigger = _settings.QuickPanelMouseTriggers;
        var radial = _settings.RadialMenu;
        var yanm = _settings.Yanm;

        bool panelValue = false, radialValue = false, yanmValue = false;

        switch (gestureName)
        {
            case "RightButtonLongPress":
                panelValue = trigger.RightButtonLongPress;
                radialValue = radial.TriggerRightButtonLongPress;
                yanmValue = yanm.TriggerRightButtonLongPress;
                break;
            case "MiddleButtonLongPress":
                panelValue = trigger.MiddleButtonLongPress;
                radialValue = radial.TriggerMiddleButtonLongPress;
                yanmValue = yanm.TriggerMiddleButtonLongPress;
                break;
            case "RightButtonDrag":
                panelValue = trigger.RightButtonDrag;
                radialValue = radial.TriggerRightButtonDrag;
                yanmValue = yanm.TriggerRightButtonDrag;
                break;
            case "MiddleButtonDrag":
                panelValue = trigger.MiddleButtonDrag;
                radialValue = radial.TriggerMiddleButtonDrag;
                yanmValue = yanm.TriggerMiddleButtonDrag;
                break;
            case "MiddleButtonDown":
                panelValue = trigger.MiddleButtonDown;
                radialValue = radial.TriggerMiddleButtonDown;
                yanmValue = yanm.TriggerMiddleButtonDown;
                break;
            case "X1ButtonDown":
                panelValue = trigger.X1ButtonDown;
                radialValue = radial.TriggerX1ButtonDown;
                yanmValue = yanm.TriggerX1ButtonDown;
                break;
            case "X2ButtonDown":
                panelValue = trigger.X2ButtonDown;
                radialValue = radial.TriggerX2ButtonDown;
                yanmValue = yanm.TriggerX2ButtonDown;
                break;
            case "HorizontalWheel":
                panelValue = trigger.HorizontalWheel;
                radialValue = radial.TriggerHorizontalWheel;
                yanmValue = yanm.TriggerHorizontalWheel;
                break;
            case "CtrlLeftClick":
                panelValue = trigger.CtrlLeftClick;
                radialValue = radial.TriggerCtrlLeftClick;
                yanmValue = yanm.TriggerCtrlLeftClick;
                break;
            case "CtrlRightClick":
                panelValue = trigger.CtrlRightClick;
                radialValue = radial.TriggerCtrlRightClick;
                yanmValue = yanm.TriggerCtrlRightClick;
                break;
            case "CtrlMiddleClick":
                panelValue = trigger.CtrlMiddleClick;
                radialValue = radial.TriggerCtrlMiddleClick;
                yanmValue = yanm.TriggerCtrlMiddleClick;
                break;
        }

        if (gestureName == "RightButtonDrag" &&
            string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.RightDrag, StringComparison.Ordinal))
        {
            return "Gesture";
        }

        if (gestureName == "MiddleButtonDrag" &&
            string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.MiddleDrag, StringComparison.Ordinal))
        {
            return "Gesture";
        }

        var legacyMode = GestureNameToMouseTriggerMode(gestureName);
        if (legacyMode != MouseTriggerModes.None &&
            string.Equals(MouseTriggerModes.Normalize(_settings.WindowSnapAssistMouseTriggerMode), legacyMode, StringComparison.OrdinalIgnoreCase))
        {
            return "WindowSnap";
        }

        if (panelValue) return "Panel";
        if (radialValue) return "Radial";
        if (yanmValue) return "Yanm";
        return "None";
    }

    private void UpdateAllGestureCardColors()
    {
        // Update all gesture cards with their current assignments
        foreach (var gesture in GetMouseTriggerGestureNames())
        {
            var target = GetGestureTarget(gesture);
            UpdateGestureCardColors(gesture, target);
        }
    }

    private static string[] GetMouseTriggerGestureNames() =>
    [
        "RightButtonLongPress", "MiddleButtonLongPress", "RightButtonDrag", "MiddleButtonDrag",
        "MiddleButtonDown", "X1ButtonDown", "X2ButtonDown", "HorizontalWheel",
        "CtrlLeftClick", "CtrlRightClick", "CtrlMiddleClick"
    ];

    private void UpdateGestureCardColors(string gestureName, string target)
    {
        // Find the card and highlight elements
        var cardName = $"{gestureName}Card";
        var highlightName = $"{gestureName}Highlight";
        var arrowsName = $"{gestureName}Arrows";

        var card = FindName(cardName) as System.Windows.Controls.Border;
        var highlight = FindName(highlightName) as System.Windows.Shapes.Shape;
        var arrows = FindName(arrowsName) as System.Windows.Shapes.Shape;

        if (card == null)
        {
            HostAssets.AppendLog($"Card not found: {cardName}");
            return;
        }

        // Update card border and background based on target
        // For highlight and arrows, we use consistent Blue color to denote "Active area" if not None
        var blueHighlight = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6));

        var (borderBrush, background, highlightFill) = target switch
        {
            "Panel" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x3B, 0x82, 0xF6)), // Blue border
                        new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0D, 0x3B, 0x82, 0xF6)), // Blue background
                        blueHighlight),
            "Radial" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xA8, 0x55, 0xF7)), // Purple border
                         new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0D, 0xA8, 0x55, 0xF7)), // Purple background
                         blueHighlight), // Unified to Blue
            "Yanm" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x10, 0xB9, 0x81)), // Green border
                       new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0D, 0x10, 0xB9, 0x81)), // Green background
                       blueHighlight), // Unified to Blue
            "Gesture" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0xFB, 0x92, 0x3C)),
                          new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0xFB, 0x92, 0x3C)),
                          blueHighlight), // Unified to Blue
            "WindowSnap" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0x38, 0xBD, 0xF8)),
                             new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x38, 0xBD, 0xF8)),
                             blueHighlight), // Unified to Blue
            _ => (null, null, null)
        };

        card.BorderBrush = borderBrush;
        card.Background = background;

        if (highlight != null)
        {
            highlight.Fill = highlightFill;
        }

        if (arrows != null)
        {
            // If active, arrows are blue; if None, fallback to system text secondary brush (gray)
            arrows.Fill = highlightFill ?? (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BrushTextSec");
        }

        // Update button styles - find all 4 buttons for this gesture
        UpdateGestureButtonStyles(gestureName, target);
    }

    private void UpdateGestureButtonStyles(string gestureName, string activeTarget)
    {
        if (_mouseTriggerTargetCombos.TryGetValue(gestureName, out var combo))
        {
            _isUpdatingMouseTriggerTargetCombos = true;
            try
            {
                combo.SelectedValue = activeTarget;
            }
            finally
            {
                _isUpdatingMouseTriggerTargetCombos = false;
            }
        }

        var targets = new[] { "None", "Panel", "Radial", "Yanm", "WindowSnap", "Gesture" };
        
        foreach (var target in targets)
        {
            var buttonName = $"{gestureName}_{target}";
            var button = FindName(buttonName) as System.Windows.Controls.Button;
            
            if (button == null) continue;

            if (target == activeTarget)
            {
                // Active button - colored background
                var activeBrush = target switch
                {
                    "None" => null,
                    "Panel" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)), // Blue
                    "Radial" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x93, 0x33, 0xEA)), // Purple
                    "Yanm" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x05, 0x96, 0x69)), // Green
                    "WindowSnap" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x02, 0x84, 0xC7)),
                    "Gesture" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xEA, 0x58, 0x0C)),
                    _ => null
                };
                button.Background = activeBrush;
                button.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
            else
            {
                // Inactive button - default style
                button.Background = new SolidColorBrush(System.Windows.Media.Colors.Transparent);
                button.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x71, 0x71, 0x7A));
            }
        }
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
        }
    }

    private async void SignInMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void SignOutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SignOutAsync();
    }

    private async void RefreshAccountMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudAsync();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        await SignOutAsync();
    }

    private async void RefreshSyncStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudAsync();
    }

    private void RefreshSyncLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSyncActivityLog();
    }

    private DispatcherTimer? _webDavSaveTimer;
    private DispatcherTimer? _webDavStatusHideTimer;
    private bool _isWebDavSaveStatusVisible;

    public bool IsWebDavSaveStatusVisible
    {
        get => _isWebDavSaveStatusVisible;
        private set
        {
            if (_isWebDavSaveStatusVisible == value) return;
            _isWebDavSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    private void QueueWebDavSettingsSave(int delayMs = 500)
    {
        if (_webDavSaveTimer == null)
        {
            _webDavSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _webDavSaveTimer.Tick += (s, e) => { _webDavSaveTimer.Stop(); SaveWebDavSettings(); };
        }
        else
        {
            _webDavSaveTimer.Stop();
            _webDavSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _webDavSaveTimer.Start();
    }

    private void FlushWebDavSettingsSave()
    {
        if (_webDavSaveTimer != null && _webDavSaveTimer.IsEnabled)
        {
            _webDavSaveTimer.Stop();
            SaveWebDavSettings();
        }
    }

    private void SaveWebDavSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDavSettings();
    }

    private void SaveWebDavSettings()
    {
        _personalSyncSecrets.GitHubToken = GitHubTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GiteeToken = GiteeTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GitLabToken = GitLabTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GiteaToken = GiteaTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.S3SecretAccessKey = S3SecretAccessKeyBox?.Password ?? string.Empty;
        _personalSyncSecrets.WebDavPassword = WebDavPasswordBox?.Password ?? string.Empty;
        CloudSyncDiagnostics.Log(
            "SettingsWindow.PersonalSync",
            "Save personal sync button clicked",
            ("selectedProvider", SelectedPersonalSyncProvider),
            ("summary", CloudSyncDiagnostics.DescribePersonalSync(_personalSyncSettings, _personalSyncSecrets)));
        _mainWindow.SavePersonalSyncSettings(ClonePersonalSyncSettings(_personalSyncSettings), ClonePersonalSyncSecrets(_personalSyncSecrets));
        _settings = AppSettingsStore.Load();
        RefreshWebDavSummary();
        SyncStatusText = "个人同步配置已保存。";
        _webDavStatusHideTimer = ShowSaveStatusTemporarily(_webDavStatusHideTimer, visible => IsWebDavSaveStatusVisible = visible);
        RefreshSyncActivityLog();
    }

    private DispatcherTimer? _aiSaveTimer;
    private DispatcherTimer? _aiStatusHideTimer;
    private bool _isAiSaveStatusVisible;

    public bool IsAiSaveStatusVisible
    {
        get => _isAiSaveStatusVisible;
        private set
        {
            if (_isAiSaveStatusVisible == value) return;
            _isAiSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    private void QueueAiSettingsSave(int delayMs = 500)
    {
        if (_aiSaveTimer == null)
        {
            _aiSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _aiSaveTimer.Tick += (s, e) => { _aiSaveTimer.Stop(); SaveAiSettings(); };
        }
        else
        {
            _aiSaveTimer.Stop();
            _aiSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _aiSaveTimer.Start();
    }

    private void FlushAiSettingsSave()
    {
        if (_aiSaveTimer != null && _aiSaveTimer.IsEnabled)
        {
            _aiSaveTimer.Stop();
            SaveAiSettings();
        }
    }

    private void SaveAiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAiSettings();
    }

    private void SaveAiSettings()
    {
        // 1. 同步服务商列表到 _settings
        _settings.AiServiceProviders = AiServiceProvidersList.Select(vm => {
            vm.RawSettings.Models = vm.Models.ToList();
            return vm.RawSettings;
        }).ToList();

        // 2. 同步提示词
        _settings.AiSystemPrompt = AiSystemPrompt;

        // 3. 将当前选中的提供商设为 Active 默认提供商并同步参数
        if (SelectedServiceProvider != null)
        {
            _settings.ActiveServiceProviderId = SelectedServiceProvider.Id;
            _settings.AiBaseUrl = SelectedServiceProvider.BaseUrl;
            _settings.AiApiKey = SelectedServiceProvider.ApiKey;
            _settings.AiModel = SelectedServiceProvider.SelectedModel;
        }

        // 4. 保存设置并同步至主窗口
        AppSettingsStore.Save(_settings);
        CloudSyncDiagnostics.Log(
            "SettingsWindow.Ai",
            "AI settings saved",
            ("providerCount", _settings.AiServiceProviders.Count),
            ("activeProviderId", _settings.ActiveServiceProviderId ?? string.Empty),
            ("providerNames", string.Join(", ", _settings.AiServiceProviders.Select(static provider => provider.Name ?? string.Empty))));
        _mainWindow.OnAiSettingsChanged();
        _mainWindow.NotifyQuickPanelSettingsChanged("ai-settings-saved", refreshYanmOverlay: false);

        // 5. 更新原始值
        _originalAiBaseUrl = _settings.AiBaseUrl;
        _originalAiApiKey = _settings.AiApiKey;
        _originalAiModel = _settings.AiModel;
        _originalAiSystemPrompt = _settings.AiSystemPrompt;

        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSystemPrompt = _settings.AiSystemPrompt;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);

        // 6. 重置状态
        HasAiSettingsChanged = false;
        _aiStatusHideTimer = ShowSaveStatusTemporarily(_aiStatusHideTimer, visible => IsAiSaveStatusVisible = visible);
    }

    private void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddProviderWindow();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var newProvider = new AiServiceProviderSettings
            {
                Id = Guid.NewGuid().ToString(),
                Name = dialog.ProviderName,
                ProviderType = dialog.ProviderType,
                BaseUrl = BuildDefaultProviderBaseUrl(dialog.ProviderType),
                ApiKey = "",
                IsEnabled = true,
                Models = [],
                SelectedModel = string.Empty
            };

            var vm = new SettingsAiProviderVM(newProvider);
            _aiServiceProvidersList.Add(vm);
            SelectedServiceProvider = vm;
            HasAiSettingsChanged = true;
            OnPropertyChanged(nameof(FilteredProviders));
        }
    }

    private void DeleteModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string modelName && SelectedServiceProvider != null)
        {
            SelectedServiceProvider.Models.Remove(modelName);
            if (SelectedServiceProvider.SelectedModel == modelName)
            {
                SelectedServiceProvider.SelectedModel = SelectedServiceProvider.Models.FirstOrDefault() ?? string.Empty;
            }
            HasAiSettingsChanged = true;
        }
    }

    private void AddNewModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServiceProvider == null) return;

        var dialog = new SimpleTextInputWindow("添加模型", "请输入模型名称（如 gpt-4o, deepseek-chat）:", "");
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var modelName = dialog.ValueText;
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                if (!SelectedServiceProvider.Models.Contains(modelName))
                {
                    SelectedServiceProvider.Models.Add(modelName);
                    if (string.IsNullOrWhiteSpace(SelectedServiceProvider.SelectedModel))
                    {
                        SelectedServiceProvider.SelectedModel = modelName;
                    }
                    HasAiSettingsChanged = true;
                }
            }
        }
    }

    private async void ManageModelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServiceProvider == null)
        {
            System.Windows.MessageBox.Show(this, "请先选择一个提供商。", "管理模型", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedServiceProvider.BaseUrl) || string.IsNullOrWhiteSpace(SelectedServiceProvider.ApiKey))
        {
            System.Windows.MessageBox.Show(this, "请先填写当前提供商的 API 地址和 API 密钥。", "管理模型", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var availableModels = await FetchAvailableModelsAsync(SelectedServiceProvider);
            Mouse.OverrideCursor = null;
            if (availableModels.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "没有读取到可用模型。请检查提供商接口是否支持读取模型列表。", "管理模型", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new AiModelPickerWindow(SelectedServiceProvider.Name, availableModels, SelectedServiceProvider.Models)
            {
                Owner = this
            };

            if (picker.ShowDialog() == true)
            {
                var addedCount = 0;
                foreach (var modelName in picker.SelectedModels)
                {
                    if (SelectedServiceProvider.Models.Contains(modelName))
                    {
                        continue;
                    }

                    SelectedServiceProvider.Models.Add(modelName);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    if (string.IsNullOrWhiteSpace(SelectedServiceProvider.SelectedModel))
                    {
                        SelectedServiceProvider.SelectedModel = SelectedServiceProvider.Models.FirstOrDefault() ?? string.Empty;
                    }

                    HasAiSettingsChanged = true;
                    ShowToast($"已添加 {addedCount} 个模型");
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"读取模型列表失败：{ex.Message}", "管理模型", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void CheckApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServiceProvider == null) return;
        var baseUrl = SelectedServiceProvider.BaseUrl;
        var apiKey = SelectedServiceProvider.ApiKey;

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            System.Windows.MessageBox.Show("请先填写服务地址和 API 密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CheckApiKeyButtonText = "检测中...";

        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var requestUrl = $"{baseUrl.TrimEnd('/')}/models";
                var response = await client.GetAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    System.Windows.MessageBox.Show("连接检测成功！API 地址和密钥连通正常。", "检测成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var err = await response.Content.ReadAsStringAsync();
                    System.Windows.MessageBox.Show($"检测失败。服务器返回状态码: {(int)response.StatusCode}\n{err}", "检测失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"请求异常: {ex.Message}", "检测失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CheckApiKeyButtonText = "检测";
        }
    }

    private static string BuildDefaultProviderBaseUrl(string providerType)
    {
        return providerType switch
        {
            "Gemini" => "https://generativelanguage.googleapis.com/v1beta",
            "Anthropic" => "https://api.anthropic.com/v1",
            "Ollama" => "http://localhost:11434/v1",
            _ => "https://api.openai.com/v1"
        };
    }

    private static async Task<IReadOnlyList<string>> FetchAvailableModelsAsync(SettingsAiProviderVM provider)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        using var request = BuildModelListRequest(provider);
        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"服务器返回 {(int)response.StatusCode}：{responseBody}");
        }

        return ParseAvailableModelNames(provider.ProviderType, responseBody);
    }

    private static HttpRequestMessage BuildModelListRequest(SettingsAiProviderVM provider)
    {
        var baseUrl = provider.BaseUrl.Trim().TrimEnd('/');
        var apiKey = provider.ApiKey.Trim();
        var providerType = provider.ProviderType?.Trim() ?? string.Empty;

        if (string.Equals(providerType, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var separator = baseUrl.Contains('?') ? "&" : "?";
            return new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models{separator}key={Uri.EscapeDataString(apiKey)}");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        if (string.Equals(providerType, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private static IReadOnlyList<string> ParseAvailableModelNames(string providerType, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var models = new List<string>();

        if (string.Equals(providerType, "Gemini", StringComparison.OrdinalIgnoreCase) &&
            document.RootElement.TryGetProperty("models", out var geminiModels) &&
            geminiModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in geminiModels.EnumerateArray())
            {
                if (!model.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                {
                    name = name["models/".Length..];
                }

                models.Add(name);
            }
        }
        else if (document.RootElement.TryGetProperty("data", out var dataModels) &&
                 dataModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in dataModels.EnumerateArray())
            {
                if (!model.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                var id = idElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    models.Add(id);
                }
            }
        }

        return models
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ResetSystemPromptButton_Click(object sender, RoutedEventArgs e)
    {
        AiSystemPrompt = AppSettingsStore.DefaultAiSystemPrompt;
        AiSystemPromptTextBox.Text = AppSettingsStore.DefaultAiSystemPrompt;
        HasAiSettingsChanged = true;
        ShowToast("提示词已重置（需要点击保存生效）");
    }

    private void SearchProviderTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilteredProviders));
    }

    private void AiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HasAiSettingsChanged = true;
    }


    private void AiSettings_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckAiSettingsChanged();
        QueueAiSettingsSave(500);
    }

    private void EditSystemPromptInNewWindow_Click(object sender, RoutedEventArgs e)
    {
        var editor = new SystemPromptEditorWindow(AiSystemPrompt)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            AiSystemPrompt = editor.PromptText;
            AiSystemPromptTextBox.Text = editor.PromptText;
        }
    }

    private void AddEnvironmentVariableButton_Click(object sender, RoutedEventArgs e)
    {
        EnvironmentVariables.Add(new EnvironmentVariableEditorItem("NOTION_TOKEN", string.Empty, "Notion Integration Token"));
        EnvironmentStatusText = "已添加一行环境变量。";
        QueueEnvironmentVariablesSave(200);
    }

    private void RemoveEnvironmentVariableButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EnvironmentVariableEditorItem item })
        {
            EnvironmentVariables.Remove(item);
            EnvironmentStatusText = "已移除一行环境变量。";
            QueueEnvironmentVariablesSave(200);
        }
    }

    private DispatcherTimer? _envVarsSaveTimer;
    private DispatcherTimer? _envVarsStatusHideTimer;
    private bool _isEnvVarsSaveStatusVisible;

    public bool IsEnvVarsSaveStatusVisible
    {
        get => _isEnvVarsSaveStatusVisible;
        private set
        {
            if (_isEnvVarsSaveStatusVisible == value) return;
            _isEnvVarsSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    private void QueueEnvironmentVariablesSave(int delayMs = 500)
    {
        if (_envVarsSaveTimer == null)
        {
            _envVarsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _envVarsSaveTimer.Tick += (s, e) => { _envVarsSaveTimer.Stop(); SaveEnvironmentVariables(); };
        }
        else
        {
            _envVarsSaveTimer.Stop();
            _envVarsSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _envVarsSaveTimer.Start();
    }

    private void FlushEnvironmentVariablesSave()
    {
        if (_envVarsSaveTimer != null && _envVarsSaveTimer.IsEnabled)
        {
            _envVarsSaveTimer.Stop();
            SaveEnvironmentVariables();
        }
    }

    private void SaveEnvironmentVariablesButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEnvironmentVariables();
    }

    private void SaveEnvironmentVariables()
    {
        var variables = EnvironmentVariables
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(static item => new AppEnvironmentVariableSettings
            {
                Name = item.Name.Trim(),
                Value = item.Value ?? string.Empty,
                Description = item.Description ?? string.Empty
            })
            .Where(static item => AppEnvironmentVariableStore.IsValidEnvironmentName(item.Name))
            .ToArray();

        AppEnvironmentVariableStore.Save(variables);
        _mainWindow.NotifyQuickPanelSettingsChanged("environment-variables-saved", refreshYanmOverlay: false);
        _settings = AppSettingsStore.Load();
        EnvironmentStatusText = BuildEnvironmentSummary();
        _envVarsStatusHideTimer = ShowSaveStatusTemporarily(_envVarsStatusHideTimer, visible => IsEnvVarsSaveStatusVisible = visible);
    }

    private void CheckAiSettingsChanged()
    {
        HasAiSettingsChanged = 
            AiBaseUrl != _originalAiBaseUrl ||
            AiApiKey != _originalAiApiKey ||
            AiModel != _originalAiModel ||
            AiSystemPrompt != _originalAiSystemPrompt;
    }

    private void ShowToast(string message)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            // 停止任何正在进行的动画
            ToastTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ToastNotification.BeginAnimation(OpacityProperty, null);
            
            // 重置状态
            ToastNotification.Opacity = 1;
            ToastTransform.X = 400;
            ToastMessage.Text = message;
            ToastNotification.Visibility = Visibility.Visible;

            // 滑入动画
            var slideIn = new DoubleAnimation
            {
                From = 400,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ToastTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

            // 等待2秒
            await Task.Delay(2000);

            // 淡出动画
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                ToastNotification.Visibility = Visibility.Collapsed;
                ToastNotification.Opacity = 1;
                ToastTransform.X = 400;
            };
            ToastNotification.BeginAnimation(OpacityProperty, fadeOut);
        });
    }

    private void WebDavPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _personalSyncSecrets.WebDavPassword = WebDavPasswordBox.Password;
        RefreshWebDavSummary();
    }

    private void SetWebDavCredentialButton_Click(object sender, RoutedEventArgs e)
    {
        var username = WebDavUsername.Trim();
        var requireUsername = string.IsNullOrWhiteSpace(username);
        if (requireUsername)
        {
            System.Windows.MessageBox.Show(this, "请先在上一层填写 WebDAV 用户名，再设置应用密码。", "缺少用户名", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WebDavCredentialWindow(username, requireUsername: false)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        WebDavUsername = dialog.Username;
        _personalSyncSecrets.WebDavPassword = dialog.Password;
        _mainWindow.SavePersonalSyncSettings(ClonePersonalSyncSettings(_personalSyncSettings), ClonePersonalSyncSecrets(_personalSyncSecrets));
        RefreshWebDavSummary();
        SyncStatusText = "WebDAV 凭据已保存。";
    }

    private async void TestWebDavButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDavSettingsButton_Click(sender, e);
        SetPersonalSyncButtonsEnabled(false);
        WebDavStatusText = "正在后台测试连接...";
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var result = await _mainWindow.ProbeWebDavAsync();
            LoadPersonalSyncStateFromSettings();
            WebDavStatusText = result.message;
            RefreshSyncActivityLog();
            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, $"{SelectedPersonalSyncProviderDisplayName} 测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetPersonalSyncButtonsEnabled(true);
        }
    }

    private async void SyncWebDavButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDavSettingsButton_Click(sender, e);
        SetPersonalSyncButtonsEnabled(false);
        WebDavStatusText = "正在后台同步，请稍候...";
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var result = await _mainWindow.SyncWebDavNowAsync();
            LoadPersonalSyncStateFromSettings();
            WebDavStatusText = result.message;
            await RefreshExtensionsFromDiskAsync();
            RefreshSyncActivityLog();
            RefreshPersonalExtensionSyncStatus();
            await RefreshPersonalSyncCommitsAsync();
            await RefreshPersonalConfigRestorePointsAsync();
            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, $"{SelectedPersonalSyncProviderDisplayName} 同步失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetPersonalSyncButtonsEnabled(true);
        }
    }

    private void SetPersonalSyncButtonsEnabled(bool enabled)
    {
        if (TestPersonalSyncButton != null) TestPersonalSyncButton.IsEnabled = enabled;
        if (ClearPersonalSyncButton != null) ClearPersonalSyncButton.IsEnabled = enabled;
        if (SyncPersonalSyncButton != null) SyncPersonalSyncButton.IsEnabled = enabled;
    }

    private void RefreshPersonalExtensionSyncStatus()
    {
        try
        {
            ExtensionSyncConflictItems.Clear();
            foreach (var conflict in _mainWindow.GetExtensionSyncConflicts())
            {
                ExtensionSyncConflictItems.Add(new ExtensionSyncConflictItem(conflict));
            }
            OnPropertyChanged(nameof(HasExtensionSyncConflicts));
            ExtensionDataConflictItems.Clear();
            var dataStates = _mainWindow.GetExtensionDataSyncStates();
            foreach (var state in dataStates.Where(static item => item.Conflict != null))
            {
                ExtensionDataConflictItems.Add(new ExtensionDataConflictItem(state));
            }
            OnPropertyChanged(nameof(HasExtensionDataConflicts));
            var dataPending = dataStates.Count(static item => item.Pending);
            var dataErrors = dataStates.Count(static item => !string.IsNullOrWhiteSpace(item.LastError));
            var dataTracked = dataStates.Count;
            var latestDataRevision = dataStates.Count == 0 ? 0 : dataStates.Max(static item => item.LastRemoteRevision);
            ExtensionDataSyncStatusText =
                $"数据项统计 · 已跟踪 {dataTracked} 项 · 待同步 {dataPending} · 冲突 {ExtensionDataConflictItems.Count} · 错误 {dataErrors} · 最高版本 {latestDataRevision}";
            var session = SyncSessionStore.Load();
            var accountMode = session != null && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var authority = accountMode
                ? "云端私有库为主配置 · 个人仓库用作备份"
                : "个人仓库双向同步模式";
            if (!File.Exists(HostAssets.WebDavSyncStatePath))
            {
                PersonalExtensionSyncStatusText = $"{authority} · 尚未生成本地扩展索引";
                return;
            }

            var index = JsonSerializer.Deserialize<WebDavSyncIndex>(
                File.ReadAllText(HostAssets.WebDavSyncStatePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new WebDavSyncIndex();
            var active = index.Items.Count(static item => !item.Deleted && !item.Purged);
            var deleted = index.Items.Count(static item => item.Deleted && !item.Purged);
            var purged = index.Items.Count(static item => item.Purged);
            var pending = index.Items.Count(static item => item.LocalDeletionPending);
            var source = !string.IsNullOrWhiteSpace(index.UpdatedByDeviceName)
                ? index.UpdatedByDeviceName
                : !string.IsNullOrWhiteSpace(index.UpdatedByDeviceId) ? index.UpdatedByDeviceId : "未知设备";
            var updated = DateTimeOffset.TryParse(index.UpdatedAtUtc, out var updatedAt)
                ? updatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
                : "尚未同步";
            PersonalExtensionSyncStatusText =
                $"{authority} · 云端版本 {index.Revision} · 有效扩展 {active} 个 · 已删除 {deleted} 个 · 已彻底删除 {purged} 个 · 待同步 {pending} 个 · 来源设备: {source} · 更新时间: {updated}";
        }
        catch (Exception ex)
        {
            PersonalExtensionSyncStatusText = $"扩展同步索引无法读取：{ex.Message}";
        }
    }

    private async void UseLocalExtensionSyncConflictButton_Click(object sender, RoutedEventArgs e)
    {
        await ResolveExtensionSyncConflictFromButtonAsync(sender, useLocalVersion: true);
    }

    private async void AcceptRemoteExtensionSyncConflictButton_Click(object sender, RoutedEventArgs e)
    {
        await ResolveExtensionSyncConflictFromButtonAsync(sender, useLocalVersion: false);
    }

    private async Task ResolveExtensionSyncConflictFromButtonAsync(object sender, bool useLocalVersion)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ExtensionSyncConflictItem item } button)
        {
            return;
        }
        button.IsEnabled = false;
        try
        {
            var result = await _mainWindow.ResolveExtensionSyncConflictAsync(item.ExtensionId, useLocalVersion);
            SyncStatusText = result.message;
            RefreshPersonalExtensionSyncStatus();
            RefreshSyncActivityLog();
            if (result.ok)
            {
                await RefreshExtensionsFromDiskAsync();
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void UseLocalExtensionDataConflictButton_Click(object sender, RoutedEventArgs e)
    {
        await ResolveExtensionDataConflictFromButtonAsync(sender, useLocalVersion: true);
    }

    private async void AcceptRemoteExtensionDataConflictButton_Click(object sender, RoutedEventArgs e)
    {
        await ResolveExtensionDataConflictFromButtonAsync(sender, useLocalVersion: false);
    }

    private async Task ResolveExtensionDataConflictFromButtonAsync(object sender, bool useLocalVersion)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ExtensionDataConflictItem item } button)
        {
            return;
        }
        button.IsEnabled = false;
        try
        {
            var result = await _mainWindow.ResolveExtensionDataSyncConflictAsync(
                item.ExtensionId,
                item.Key,
                useLocalVersion);
            SyncStatusText = result.message;
            RefreshPersonalExtensionSyncStatus();
            RefreshSyncActivityLog();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void RefreshPersonalConfigRestorePointsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPersonalConfigRestorePointsAsync();
    }

    private async Task RefreshPersonalConfigRestorePointsAsync()
    {
        try
        {
            PersonalConfigRestoreStatusText = "正在读取跨后端配置恢复点...";
            var points = await _mainWindow.GetPersonalConfigRestorePointsAsync();
            PersonalConfigRestorePoints.Clear();
            foreach (var point in points)
            {
                PersonalConfigRestorePoints.Add(new PersonalConfigRestorePointItem(point));
            }
            PersonalConfigRestoreStatusText = points.Count == 0
                ? "尚无历史备份；完成一次有配置变化的个人同步后会自动创建。"
                : $"共发现 {points.Count} 个历史备份，可用于恢复当前配置。";
        }
        catch (Exception ex)
        {
            PersonalConfigRestorePoints.Clear();
            PersonalConfigRestoreStatusText = $"读取恢复点失败：{ex.Message}";
        }
    }

    private async void RestorePersonalConfigPointButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: PersonalConfigRestorePointItem item } button)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            $"将主配置恢复到 {item.CreatedAtText} 的状态？\n\n这会恢复设置、快捷面板、燕环和规则，但不会替换本机密钥、扩展包或燕幕。恢复结果会作为一个新版本继续同步，原恢复点不会删除。",
            "确认恢复个人仓库配置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        button.IsEnabled = false;
        try
        {
            PersonalConfigRestoreStatusText = $"正在校验并恢复 {item.CreatedAtText} 的配置...";
            var restoreResult = await _mainWindow.RestorePersonalConfigRestorePointAsync(item.RestorePointId);
            SyncStatusText = restoreResult.message;
            PersonalConfigRestoreStatusText = restoreResult.message;
            if (restoreResult.ok)
            {
                RefreshAccountObjectSyncStatus();
                RefreshSyncActivityLog();
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void RefreshPersonalSyncCommitsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPersonalSyncCommitsAsync(forceMessage: true);
    }

    private async Task RefreshPersonalSyncCommitsAsync(bool forceMessage = false)
    {
        bool isGitProvider = 
            string.Equals(SelectedPersonalSyncProvider, PersonalSyncProviders.GitHub, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SelectedPersonalSyncProvider, PersonalSyncProviders.Gitee, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SelectedPersonalSyncProvider, PersonalSyncProviders.GitLab, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SelectedPersonalSyncProvider, PersonalSyncProviders.Gitea, StringComparison.OrdinalIgnoreCase);

        if (!isGitProvider)
        {
            PersonalSyncCommitItems.Clear();
            PersonalSyncCommitStatusText = "提交记录当前仅支持 Git 同步仓库 (GitHub/Gitee/GitLab/Gitea)。";
            return;
        }

        try
        {
            CloudSyncDiagnostics.Log(
                "SettingsWindow.PersonalSync",
                "Refresh personal sync commits started",
                ("provider", SelectedPersonalSyncProvider),
                ("forceMessage", forceMessage),
                ("summary", CloudSyncDiagnostics.DescribePersonalSync(_personalSyncSettings, _personalSyncSecrets)));
            if (forceMessage)
            {
                PersonalSyncCommitStatusText = $"正在读取 {SelectedPersonalSyncProvider} 提交记录...";
            }

            var commits = await _mainWindow.GetPersonalSyncCommitsAsync(_personalSyncSettings, _personalSyncSecrets);
            PersonalSyncCommitItems.Clear();
            foreach (var commit in commits)
            {
                PersonalSyncCommitItems.Add(new PersonalSyncCommitItem(
                    commit.Sha,
                    commit.Message,
                    commit.Author,
                    commit.CommittedAtUtc,
                    commit.Url));
            }

            PersonalSyncCommitStatusText = PersonalSyncCommitItems.Count == 0
                ? "仓库暂无提交记录。"
                : $"最近 {PersonalSyncCommitItems.Count} 条提交，可点击打开云端详情。";

            CloudSyncDiagnostics.Log(
                "SettingsWindow.PersonalSync",
                "Refresh personal sync commits completed",
                ("provider", SelectedPersonalSyncProvider),
                ("count", PersonalSyncCommitItems.Count));
        }
        catch (Exception ex)
        {
            PersonalSyncCommitItems.Clear();
            PersonalSyncCommitStatusText = $"读取提交记录失败：{ex.Message}";
            CloudSyncDiagnostics.Log(
                "SettingsWindow.PersonalSync",
                "Refresh personal sync commits failed",
                ("provider", SelectedPersonalSyncProvider),
                ("error", ex.Message));
        }
    }

    private async void ClearCloudButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = System.Windows.MessageBox.Show(
            this,
            "此操作将删除云端的所有扩展和配置数据，且无法恢复！\n\n" +
            "清空后，下次点击“立即同步”会重新以上传本地内容为准。",
            "清空云端 - 危险操作",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirmResult != MessageBoxResult.OK)
        {
            return;
        }

        var savedCloudPassword = _mainWindow.CloudSyncClient?.GetSavedPassword();
        if (string.IsNullOrWhiteSpace(savedCloudPassword))
        {
            System.Windows.MessageBox.Show(
                this,
                "未检测到燕子云账号登录密码，请先登录您的当前账户。",
                "清空云端失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var currentAccountLabel = _mainWindow.CloudSyncClient?.CurrentUserLabel ?? "当前账户";
        var passwordDialog = new WebDavCredentialWindow(currentAccountLabel, requireUsername: false)
        {
            Owner = this,
            Title = "验证当前账户密码 - 清空云端"
        };

        if (passwordDialog.ShowDialog() != true)
        {
            return;
        }

        if (passwordDialog.Password != savedCloudPassword)
        {
            System.Windows.MessageBox.Show(
                this,
                "当前账户登录密码验证失败，无法执行清空操作。",
                "清空云端失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            WebDavStatusText = "正在清空云端，请稍候...（可能需要几分钟）";
            var service = new PersonalSyncService(AppSettingsStore.Load(), requireEnabled: false);
            await service.ClearCloudAsync();
            WebDavStatusText = "云端已清空。";
            RefreshSyncActivityLog();
            System.Windows.MessageBox.Show(
                this,
                "云端数据已成功清空。\n\n下次点击\"立即同步\"时将重新上传本地扩展。",
                "清空云端成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            // 提取更友好的错误信息
            if (message.Contains("Too many requests"))
            {
                message = "坚果云频率限制，请稍后再试。";
            }
            else if (message.Contains("503"))
            {
                message = "服务暂时不可用，请稍后再试。";
            }
            
            WebDavStatusText = $"清空云端失败：{message}";
            RefreshSyncActivityLog();
            System.Windows.MessageBox.Show(
                this,
                $"清空云端失败：{message}\n\n请查看同步记录了解详情。",
                "清空云端失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenExtensionsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ExtensionsRootPath,
            UseShellExecute = true
        });
    }

    private async void RefreshExtensionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshExtensionsFromDiskAsync();
    }

    private async void RefreshRecycleBinButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshExtensionsFromDiskAsync();
    }

    private void OpenExtensionDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        if (!Directory.Exists(item.DirectoryPath))
        {
            System.Windows.MessageBox.Show(this, "扩展目录不存在。", "打开目录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = RefreshExtensionsFromDiskAsync();
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = item.DirectoryPath,
            UseShellExecute = true
        });
    }

    private void OpenExtensionLogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        if (!File.Exists(HostAssets.HostLogPath))
        {
            System.Windows.MessageBox.Show(this, "暂无运行日志。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = HostAssets.HostLogPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"打开运行日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExtensionCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            IsInteractiveSource(source))
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        SelectedExtensionItem = item;
    }

    private void CloseExtensionDetailPanelButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelectedExtensionItem();
    }

    private void ExtensionCardsContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleExtensionCardWidthUpdate();
    }

    private async void EditExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        await EditExtensionItemAsync(item);
    }

    private async void ToggleExtensionStartupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        var nextMode = item.HasAppLaunchStartup ? null : "on_app_launch";
        var result = await _mainWindow.UpdateExtensionStartupFromSettingsAsync(item.ExtensionId, nextMode, item.StartupSchedule);
        SyncStatusText = result.message;
        if (!result.ok || result.updated == null)
        {
            System.Windows.MessageBox.Show(this, result.message, "更新开机自启失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        item.StartupMode = result.updated.Startup?.Mode ?? string.Empty;
        item.StartupSchedule = result.updated.Startup?.Schedule ?? string.Empty;
        RefreshExtensionCacheFromMainWindow();
    }

    private async void ConfigureExtensionScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        var dialog = new ScheduleConfigWindow(item.StartupSchedule)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _mainWindow.UpdateExtensionStartupFromSettingsAsync(item.ExtensionId, item.StartupMode, dialog.ResultSchedule);
        SyncStatusText = result.message;
        if (!result.ok || result.updated == null)
        {
            System.Windows.MessageBox.Show(this, result.message, "更新定时运行失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        item.StartupMode = result.updated.Startup?.Mode ?? string.Empty;
        item.StartupSchedule = result.updated.Startup?.Schedule ?? string.Empty;
        RefreshExtensionCacheFromMainWindow();
    }

    private async Task EditExtensionItemAsync(SettingsExtensionItem item)
    {
        var result = await _mainWindow.EditExtensionFromSettingsAsync(item.ExtensionId, this);
        if (!string.IsNullOrWhiteSpace(result.message))
        {
            SyncStatusText = result.message;
        }

        if (!result.ok)
        {
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                System.Windows.MessageBox.Show(this, result.message, "编辑扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        RefreshExtensionItems();
        RefreshShortcutItems();
    }

    private async void DeleteExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        var result = await _mainWindow.DeleteExtensionFromSettingsAsync(item.ExtensionId, this);
        if (!string.IsNullOrWhiteSpace(result.message))
        {
            SyncStatusText = result.message;
        }

        if (!result.ok)
        {
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                System.Windows.MessageBox.Show(this, result.message, "删除扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        ExtensionItems.Remove(item);
        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        OnPropertyChanged(nameof(ExtensionSearchSummary));
        RefreshShortcutItems();
        await RefreshExtensionsFromDiskAsync();
    }

    private async void RestoreRecycleBinExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsRecycleBinItem item } || item.IsOperationBusy)
        {
            return;
        }

        item.IsRestoring = true;
        try
        {
            var result = await _mainWindow.RestoreExtensionFromRecycleBinAsync(item.ItemId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "恢复扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("扩展已恢复", $"{item.Title} 已从回收站恢复。");
            }
        }
        finally
        {
            item.IsRestoring = false;
        }
    }

    private async void DeleteRecycleBinExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsRecycleBinItem item } || item.IsOperationBusy)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认彻底删除“{item.Title}”吗？这会清空回收站中的本地副本，无法恢复。",
            "彻底删除扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        item.IsDeletingPermanently = true;
        try
        {
            var result = await _mainWindow.PurgeExtensionFromRecycleBinAsync(item.ItemId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "彻底删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("回收站扩展已清理", $"{item.Title} 已从回收站彻底删除。");
            }
        }
        finally
        {
            item.IsDeletingPermanently = false;
        }
    }

    private async void PublishExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item } || item.IsOperationBusy)
        {
            return;
        }

        item.IsPublishing = true;
        try
        {
            var result = await _mainWindow.PublishExtensionFromSettingsAsync(item.ExtensionId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "发布扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("扩展已发布到商店", $"{item.Title} 已完成发布，可在扩展商店查看。");
            }
        }
        finally
        {
            item.IsPublishing = false;
        }
    }

    private void CopyExtensionStoreLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        try
        {
            var result = _mainWindow.CopyExtensionStoreLink(item.ExtensionId);
            SyncStatusText = result.message;
        }
        catch (Exception ex)
        {
            SyncStatusText = $"复制商店链接失败：{ex.Message}";
            System.Windows.MessageBox.Show(this, SyncStatusText, "复制商店链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenExtensionStoreLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        try
        {
            var result = _mainWindow.OpenExtensionStoreLink(item.ExtensionId);
            SyncStatusText = result.message;
        }
        catch (Exception ex)
        {
            SyncStatusText = $"打开商店链接失败：{ex.Message}";
            System.Windows.MessageBox.Show(this, SyncStatusText, "打开商店链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void UnpublishExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item } || item.IsOperationBusy)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认下线扩展“{item.Title}”吗？下线后扩展商店将不再展示它。",
            "确认下线扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        item.IsUnpublishing = true;
        try
        {
            var result = await _mainWindow.UnpublishExtensionFromSettingsAsync(item.ExtensionId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "下线扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("扩展已从商店下线", $"{item.Title} 已从扩展商店移除。");
            }
        }
        finally
        {
            item.IsUnpublishing = false;
        }
    }

    public void RefreshExtensionsFromExternal()
    {
        _ = Dispatcher.InvokeAsync(async () => await RefreshExtensionsFromDiskAsync());
    }

    private async Task RefreshExtensionsFromDiskAsync()
    {
        if (IsExtensionsLoading)
        {
            return;
        }

        var refreshVersion = ++_extensionsRefreshVersion;
        var startedAt = Stopwatch.StartNew();
        IsExtensionsLoading = true;
        LocalExtensionSummary = "正在后台刷新扩展数据...";
        HostAssets.AppendLog($"Settings extensions refresh started: version={refreshVersion}");

        try
        {
            // 1. 首先加载并显示本地磁盘扩展，忽略网络以保障秒开体验
            var publishedMap = new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
            var data = await Task.Run(() =>
            {
                var backgroundStartedAt = Stopwatch.StartNew();
                LocalExtensionCatalog.EnsureSampleExtension();
                var entries = LocalExtensionCatalog.LoadEntries().ToList();
                var recycleBinItems = _mainWindow.GetRecycleBinEntriesForSettings()
                    .Select(item => new SettingsRecycleBinItem(
                        item.ItemId,
                        item.ExtensionId,
                        item.Title,
                        item.Category,
                        item.Version,
                        item.DeletedAtUtc))
                    .ToList();
                var shortcutItems = entries
                    .Select(entry => new
                    {
                        entry.Manifest.Id,
                        entry.Manifest.Name,
                        Category = entry.Manifest.Category ?? "扩展",
                        entry.Manifest.GlobalShortcut
                    })
                    .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new SettingsShortcutItem(
                        item.Id,
                        item.Name,
                        item.Category,
                        item.GlobalShortcut))
                    .ToList();
                HostAssets.AppendLog(
                    $"Settings extensions refresh background prepared: version={refreshVersion}, " +
                    $"entries={entries.Count}, recycleBinItems={recycleBinItems.Count}, shortcutItems={shortcutItems.Count}, " +
                    $"elapsedMs={backgroundStartedAt.ElapsedMilliseconds}");
                return (entries, recycleBinItems, shortcutItems);
            });

            if (refreshVersion != _extensionsRefreshVersion)
            {
                HostAssets.AppendLog($"Settings extensions refresh skipped stale result: version={refreshVersion}");
                return;
            }

            var uiApplyStartedAt = Stopwatch.StartNew();
            await Dispatcher.InvokeAsync(() =>
            {
                _mainWindow.ReloadLocalExtensionsFromEntries(data.entries, "已刷新本地扩展。");
                _cachedExtensionItems = BuildSettingsExtensionItems(_mainWindow.GetExtensionsForSettings(), publishedMap);
                _cachedRecycleBinItems = data.recycleBinItems;
                if (IsExtensionsSelected)
                {
                    RefreshExtensionSummary();
                    RefreshRecycleBinSummary();
                    RefreshExtensionItems();
                }

                if (IsRecycleBinSelected)
                {
                    RefreshRecycleBinSummary();
                    RefreshRecycleBinItems();
                }
            }, DispatcherPriority.Background);
            HostAssets.AppendLog(
                $"Settings extensions refresh UI applied: version={refreshVersion}, elapsedMs={uiApplyStartedAt.ElapsedMilliseconds}");

            // 2. 本地数据刷新完毕后，后台默默向云端同步发布状态，随后平滑渲染
            _ = Task.Run(async () =>
            {
                try
                {
                    var cloudMap = await _mainWindow.GetOwnedPublishedExtensionsForSettingsAsync();
                    if (cloudMap != null && cloudMap.Count > 0)
                    {
                        if (refreshVersion != _extensionsRefreshVersion) return;
                        
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (refreshVersion != _extensionsRefreshVersion) return;
                            _cachedExtensionItems = BuildSettingsExtensionItems(_mainWindow.GetExtensionsForSettings(), cloudMap);
                            if (IsExtensionsSelected)
                            {
                                RefreshExtensionItems();
                            }
                        }, DispatcherPriority.Background);
                    }
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Settings extensions cloud status refresh failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Settings extensions refresh failed: version={refreshVersion}, error={ex.Message}");
            LocalExtensionSummary = $"刷新扩展失败：{ex.Message}";
        }
        finally
        {
            if (refreshVersion == _extensionsRefreshVersion)
            {
                IsExtensionsLoading = false;
                _hasLoadedExtensions = true; // 标记已加载过扩展
            }

            HostAssets.AppendLog(
                $"Settings extensions refresh finished: version={refreshVersion}, totalElapsedMs={startedAt.ElapsedMilliseconds}");
        }
    }

    private void LoadPersonalSyncStateFromSettings()
    {
        _settings = AppSettingsStore.Load();
        _personalSyncSettings = ClonePersonalSyncSettings(_settings.PersonalSync);
        _personalSyncSecrets = ClonePersonalSyncSecrets(_mainWindow.GetPersonalSyncSecrets());
        OnPropertyChanged(nameof(EnablePersonalSync));
        OnPropertyChanged(nameof(SelectedPersonalSyncProvider));
        OnPropertyChanged(nameof(PersonalSyncAutoSyncDelaySeconds));
        OnPropertyChanged(nameof(SelectedPersonalSyncProviderDisplayName));
        OnPropertyChanged(nameof(PersonalSyncActionButtonText));
        OnPropertyChanged(nameof(SelectedPersonalSyncProviderQuickLinkText));
        OnPropertyChanged(nameof(SelectedPersonalSyncProviderQuickLinkUrl));
        OnPropertyChanged(nameof(HasSelectedPersonalSyncProviderQuickLink));
        OnPropertyChanged(nameof(IsSyncProviderGitHub));
        OnPropertyChanged(nameof(IsSyncProviderGitee));
        OnPropertyChanged(nameof(IsSyncProviderGitLab));
        OnPropertyChanged(nameof(IsSyncProviderGitea));
        OnPropertyChanged(nameof(IsSyncProviderS3));
        OnPropertyChanged(nameof(IsSyncProviderWebDav));
        OnPropertyChanged(nameof(GitHubSyncOwner));
        OnPropertyChanged(nameof(GitHubSyncRepo));
        OnPropertyChanged(nameof(GitHubSyncBranch));
        OnPropertyChanged(nameof(GitHubSyncPathPrefix));
        OnPropertyChanged(nameof(GiteeSyncUsername));
        OnPropertyChanged(nameof(GiteeSyncRepo));
        OnPropertyChanged(nameof(GiteeSyncBranch));
        OnPropertyChanged(nameof(GiteeSyncPathPrefix));
        OnPropertyChanged(nameof(GitLabSyncBaseUrl));
        OnPropertyChanged(nameof(GitLabSyncProjectPath));
        OnPropertyChanged(nameof(GitLabSyncBranch));
        OnPropertyChanged(nameof(GitLabSyncPathPrefix));
        OnPropertyChanged(nameof(GiteaSyncBaseUrl));
        OnPropertyChanged(nameof(GiteaSyncUsername));
        OnPropertyChanged(nameof(GiteaSyncRepo));
        OnPropertyChanged(nameof(GiteaSyncBranch));
        OnPropertyChanged(nameof(GiteaSyncPathPrefix));
        OnPropertyChanged(nameof(S3SyncAccessKeyId));
        OnPropertyChanged(nameof(S3SyncRegion));
        OnPropertyChanged(nameof(S3SyncBucket));
        OnPropertyChanged(nameof(S3SyncEndpoint));
        OnPropertyChanged(nameof(S3SyncPathPrefix));
        OnPropertyChanged(nameof(WebDavServerUrl));
        OnPropertyChanged(nameof(WebDavRootPath));
        OnPropertyChanged(nameof(WebDavUsername));
        if (GitHubTokenBox != null) GitHubTokenBox.Password = _personalSyncSecrets.GitHubToken ?? string.Empty;
        if (GiteeTokenBox != null) GiteeTokenBox.Password = _personalSyncSecrets.GiteeToken ?? string.Empty;
        if (GitLabTokenBox != null) GitLabTokenBox.Password = _personalSyncSecrets.GitLabToken ?? string.Empty;
        if (GiteaTokenBox != null) GiteaTokenBox.Password = _personalSyncSecrets.GiteaToken ?? string.Empty;
        if (S3SecretAccessKeyBox != null) S3SecretAccessKeyBox.Password = _personalSyncSecrets.S3SecretAccessKey ?? string.Empty;
        if (WebDavPasswordBox != null) WebDavPasswordBox.Password = _personalSyncSecrets.WebDavPassword ?? string.Empty;
        RefreshWebDavSummary();
    }

    private static PersonalSyncSettings ClonePersonalSyncSettings(PersonalSyncSettings? settings)
    {
        settings ??= new PersonalSyncSettings();
        var json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<PersonalSyncSettings>(json) ?? new PersonalSyncSettings();
    }

    private static PersonalSyncSecretBag ClonePersonalSyncSecrets(PersonalSyncSecretBag? secrets)
    {
        secrets ??= new PersonalSyncSecretBag();
        var json = JsonSerializer.Serialize(secrets);
        return JsonSerializer.Deserialize<PersonalSyncSecretBag>(json) ?? new PersonalSyncSecretBag();
    }

    private static int NormalizePersonalSyncAutoSyncDelay(int value)
    {
        return value is 0 or 2 or 3 or 5 or 10 or 20 or 30 or 60 or 120
            ? value
            : 10;
    }

    private void RefreshWebDavSummary()
    {
        if (!EnablePersonalSync)
        {
            WebDavStatusText = "未启用个人同步。";
            return;
        }

        WebDavStatusText = SelectedPersonalSyncProvider switch
        {
            var provider when provider == PersonalSyncProviders.WebDav =>
                string.IsNullOrWhiteSpace(_personalSyncSecrets.WebDavPassword)
                    ? "已启用 WebDAV，但还未设置密码。"
                    : $"WebDAV：{WebDavServerUrl} {WebDavRootPath}",
            var provider when provider == PersonalSyncProviders.GitHub =>
                string.IsNullOrWhiteSpace(_personalSyncSecrets.GitHubToken)
                    ? "已选择 GitHub，但还未填写 Token。"
                    : $"GitHub：{(string.IsNullOrWhiteSpace(GitHubSyncOwner) ? "<自动识别>" : GitHubSyncOwner)}/{GitHubSyncRepo}",
            var provider when provider == PersonalSyncProviders.Gitee =>
                string.IsNullOrWhiteSpace(_personalSyncSecrets.GiteeToken)
                    ? "已选择 Gitee，但还未填写 Token。"
                    : $"Gitee：{(string.IsNullOrWhiteSpace(GiteeSyncUsername) ? "<自动识别>" : GiteeSyncUsername)}/{GiteeSyncRepo}",
            var provider when provider == PersonalSyncProviders.GitLab =>
                string.IsNullOrWhiteSpace(_personalSyncSecrets.GitLabToken)
                    ? "已选择 GitLab，但还未填写 Token。"
                    : $"GitLab：{GitLabSyncProjectPath}",
            var provider when provider == PersonalSyncProviders.Gitea =>
                string.IsNullOrWhiteSpace(_personalSyncSecrets.GiteaToken)
                    ? "已选择 Gitea，但还未填写 Token。"
                    : $"Gitea：{(string.IsNullOrWhiteSpace(GiteaSyncUsername) ? "<自动识别>" : GiteaSyncUsername)}/{GiteaSyncRepo}",
            var provider when provider == PersonalSyncProviders.S3 =>
                string.IsNullOrWhiteSpace(_personalSyncSecrets.S3SecretAccessKey)
                    ? "已选择 S3，但还未填写 Secret Access Key。"
                    : $"S3：{S3SyncBucket} ({S3SyncRegion})",
            _ => "个人同步配置待完成。"
        };
    }

    public void RefreshWebDavConfigFromExternal()
    {
        LoadPersonalSyncStateFromSettings();
        SyncStatusText = "个人同步配置已刷新。";
        RefreshSyncActivityLog();
    }

    public void RefreshAiConfigFromExternal()
    {
        _settings = AppSettingsStore.Load();
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSystemPrompt = _settings.AiSystemPrompt;
        ReloadAiProvidersFromSettings();
        _originalAiBaseUrl = _settings.AiBaseUrl;
        _originalAiApiKey = _settings.AiApiKey;
        _originalAiModel = _settings.AiModel;
        _originalAiSystemPrompt = _settings.AiSystemPrompt;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        HasAiSettingsChanged = false;
        CloudSyncDiagnostics.Log(
            "SettingsWindow.Ai",
            "AI config refreshed from external",
            ("providerCount", _settings.AiServiceProviders.Count),
            ("activeProviderId", _settings.ActiveServiceProviderId ?? string.Empty),
            ("providerNames", string.Join(", ", _settings.AiServiceProviders.Select(static provider => provider.Name ?? string.Empty))));
    }

    public void ShowCloudSyncProgressToast(string message)
    {
        Dispatcher.Invoke(() =>
        {
            CloudSyncToastMessage.Text = string.IsNullOrWhiteSpace(message) ? "正在同步云端配置..." : message;
            CloudSyncToastNotification.Visibility = Visibility.Visible;
        });
    }

    public void HideCloudSyncProgressToast()
    {
        Dispatcher.Invoke(() =>
        {
            CloudSyncToastNotification.Visibility = Visibility.Collapsed;
        });
    }

    private void ReloadAiProvidersFromSettings()
    {
        _settings.AiServiceProviders ??= [];
        _aiServiceProvidersList.Clear();
        foreach (var provider in _settings.AiServiceProviders)
        {
            var vm = new SettingsAiProviderVM(provider);
            if (provider.Models != null)
            {
                foreach (var model in provider.Models)
                {
                    vm.Models.Add(model);
                }
            }

            _aiServiceProvidersList.Add(vm);
        }

        SelectedServiceProvider = _aiServiceProvidersList.FirstOrDefault(p => p.Id == _settings.ActiveServiceProviderId)
                                 ?? _aiServiceProvidersList.FirstOrDefault();
        OnPropertyChanged(nameof(FilteredProviders));
    }

    private void EditLauncherHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "设置主程序快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。也支持全局双击 Ctrl 或双击 Alt 呼出主界面。",
            LauncherHotkey,
            allowDoubleTap: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_mainWindow.TryUpdateLauncherHotkey(dialog.ShortcutText, out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
            RefreshSyncActivityLog();
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ResetLauncherHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow.TryUpdateLauncherHotkey("Alt+Space", out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
            RefreshSyncActivityLog();
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void EditSnapAssistHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var currentHotkey = _settings.WindowSnapAssistHotkey;
        var dialog = new HotkeyCaptureWindow(
            "设置窗口排列快捷键",
            "按下组合键后，将在前台窗口位置弹出布局轮盘。留空表示仅通过鼠标触发。",
            string.IsNullOrWhiteSpace(currentHotkey) ? null : currentHotkey,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_mainWindow.TryUpdateWindowSnapAssistHotkey(dialog.ShortcutText, out var message))
        {
            _settings = AppSettingsStore.Load();
            OnPropertyChanged(nameof(WindowSnapAssistHotkey));
            SyncStatusText = message;
            RefreshSyncActivityLog();
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void EditYanmHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "录制燕幕快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。",
            _settings.Yanm.CustomShortcut,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_mainWindow.TryUpdateYanmHotkey(dialog.ShortcutText, out var message))
        {
            YanmCustomShortcut = dialog.ShortcutText;
            SyncStatusText = message;
            RefreshSyncActivityLog();
            OnPropertyChanged(nameof(YanmSummary));
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void EditRadialHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "录制燕环快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。",
            _settings.RadialMenu.CustomShortcut,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_mainWindow.TryUpdateRadialHotkey(dialog.ShortcutText, out var message))
        {
            RadialCustomShortcut = dialog.ShortcutText;
            RadialActivationKey = RadialActivationKeys.Custom;
            SyncStatusText = message;
            RefreshSyncActivityLog();
            OnPropertyChanged(nameof(RadialMenuSummary));
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void EditShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsShortcutItem item })
        {
            return;
        }

        var dialog = new HotkeyCaptureWindow(
            "设置扩展快捷键",
            $"窗口激活后，直接按一次新的组合键即可为 {item.Title} 完成录制。",
            item.ShortcutValue,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _mainWindow.UpdateExtensionShortcutFromSettingsAsync(item.ExtensionId, dialog.ShortcutText);
        SyncStatusText = result.message;
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshShortcutItems();
        RefreshExtensionSummary();
    }

    private async void EditExtensionShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        var dialog = new HotkeyCaptureWindow(
            "设置扩展快捷键",
            $"窗口激活后，直接按一次新的组合键即可为 {item.Title} 完成录制。",
            item.Shortcut,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _mainWindow.UpdateExtensionShortcutFromSettingsAsync(item.ExtensionId, dialog.ShortcutText);
        SyncStatusText = result.message;
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 只更新当前项的快捷键显示，不需要刷新整个列表
        item.Shortcut = dialog.ShortcutText ?? string.Empty;
    }

    private async void ClearShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsShortcutItem item })
        {
            return;
        }

        var result = await _mainWindow.UpdateExtensionShortcutFromSettingsAsync(item.ExtensionId, null);
        SyncStatusText = result.message;
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "快捷键清除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshShortcutItems();
        RefreshExtensionSummary();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FilterTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string filterMode)
        {
            return;
        }

        // 更新筛选模式
        _extensionFilterMode = filterMode;

        // 更新标签样式
        UpdateFilterTabStyles();

        // 通知可见性变化
        OnPropertyChanged(nameof(ExtensionsListVisibility));
        OnPropertyChanged(nameof(RecycleBinListVisibility));

        // 刷新扩展列表
        RefreshExtensionItems();

        if (_extensionFilterMode == "recycle")
        {
            ClearSelectedExtensionItem();
        }
    }

    private void UpdateFilterTabStyles()
    {
        // 重置所有标签样式
        if (FilterAllTab != null)
        {
            FilterAllTab.Style = _extensionFilterMode == "all" 
                ? (Style)FindResource("FilterTabActiveStyle") 
                : (Style)FindResource("FilterTabStyle");
            var textBlock = FilterAllTab.Child as TextBlock;
            if (textBlock != null) textBlock.Foreground = _extensionFilterMode == "all" 
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 142, 142));
        }

        if (FilterPublishedTab != null)
        {
            FilterPublishedTab.Style = _extensionFilterMode == "published" 
                ? (Style)FindResource("FilterTabActiveStyle") 
                : (Style)FindResource("FilterTabStyle");
            var textBlock = FilterPublishedTab.Child as TextBlock;
            if (textBlock != null) textBlock.Foreground = _extensionFilterMode == "published" 
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 142, 142));
        }

        if (FilterDisabledTab != null)
        {
            FilterDisabledTab.Style = _extensionFilterMode == "disabled" 
                ? (Style)FindResource("FilterTabActiveStyle") 
                : (Style)FindResource("FilterTabStyle");
            var textBlock = FilterDisabledTab.Child as TextBlock;
            if (textBlock != null) textBlock.Foreground = _extensionFilterMode == "disabled" 
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 142, 142));
        }

        if (FilterShortcutTab != null)
        {
            FilterShortcutTab.Style = _extensionFilterMode == "shortcut" 
                ? (Style)FindResource("FilterTabActiveStyle") 
                : (Style)FindResource("FilterTabStyle");
            var textBlock = FilterShortcutTab.Child as TextBlock;
            if (textBlock != null) textBlock.Foreground = _extensionFilterMode == "shortcut" 
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 142, 142));
        }

        if (FilterRecycleTab != null)
        {
            FilterRecycleTab.Style = _extensionFilterMode == "recycle" 
                ? (Style)FindResource("FilterTabActiveStyle") 
                : (Style)FindResource("FilterTabStyle");
            var textBlock = FilterRecycleTab.Child as TextBlock;
            if (textBlock != null) textBlock.Foreground = _extensionFilterMode == "recycle" 
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 142, 142));
        }
    }

    private async Task SignInAsync()
    {
        var ok = await _mainWindow.PromptLoginFromSettingsAsync();
        RefreshAccountSummary();
        if (ok)
        {
            await _mainWindow.RefreshCloudFromSettingsAsync();
            RefreshWebDavConfigFromExternal();
            SyncStatusText = _mainWindow.SyncStatus;
            RefreshSyncActivityLog();
        }
    }

    private void ExtensionEnabledSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: SettingsExtensionItem item } checkbox)
        {
            return;
        }

        _mainWindow.SetExtensionEnabled(item.ExtensionId, checkbox.IsChecked == true);
        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        RefreshExtensionItems();
    }

    private async Task SignOutAsync()
    {
        _mainWindow.SignOutFromSettings();
        ClearWebDavConfiguration();
        RefreshAccountSummary();
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshSyncActivityLog();
        await Task.CompletedTask;
    }

    private void RefreshSyncActivityLog()
    {
        try
        {
            if (!File.Exists(HostAssets.HostLogPath))
            {
                SyncActivityLogText = "暂无同步记录。";
                return;
            }

            var allLines = ReadLogTailLines(HostAssets.HostLogPath, 512 * 1024)
                .Where(static line =>
                    line.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("webdav", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("cloud", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("登录", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("账号", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 过滤掉决策记录，只保留实际操作记录（上传、下载、完成等）
            var filteredLines = allLines
                .Where(line => !line.Contains("WebDAV decision", StringComparison.OrdinalIgnoreCase))
                .TakeLast(40)
                .Select(FormatSyncLogLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            SyncActivityLogText = filteredLines.Length == 0
                ? "暂无同步记录。"
                : string.Join(Environment.NewLine, filteredLines);
        }
        catch (Exception ex)
        {
            SyncActivityLogText = $"读取同步记录失败：{ex.Message}";
        }
    }

    private static string FormatSyncLogLine(string line)
    {
        try
        {
            // 解析时间戳 [2026-05-12 09:47:55]
            var timestampMatch = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
            var timeAgo = "";
            if (timestampMatch.Success && DateTime.TryParse(timestampMatch.Groups[1].Value, out var timestamp))
            {
                var elapsed = DateTime.Now - timestamp;
                timeAgo = elapsed.TotalMinutes < 1 ? "刚刚" :
                         elapsed.TotalMinutes < 60 ? $"{(int)elapsed.TotalMinutes}分钟前" :
                         elapsed.TotalHours < 24 ? $"{(int)elapsed.TotalHours}小时前" :
                         $"{(int)elapsed.TotalDays}天前";
            }

            // 提取扩展ID
            var idMatch = System.Text.RegularExpressions.Regex.Match(line, @"id=([a-zA-Z0-9\-_]+)");
            var extensionName = "";
            if (idMatch.Success)
            {
                var id = idMatch.Groups[1].Value;
                extensionName = GetExtensionName(id);
            }

            // 格式化不同类型的日志
            if (line.Contains("WebDAV uploaded package", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{timeAgo}] ↑ 上传 · {extensionName} · 本机";
            }
            else if (line.Contains("WebDAV downloaded package", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{timeAgo}] ↓ 下载 · {extensionName} · 云端";
            }
            else if (line.Contains("WebDAV decision", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("local-wins", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[{timeAgo}] ✓ 同步决策 · {extensionName} · 本机版本较新";
                }
                else if (line.Contains("remote-wins", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[{timeAgo}] ✓ 同步决策 · {extensionName} · 云端版本较新";
                }
                else if (line.Contains("conflict", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[{timeAgo}] ⚠ 冲突 · {extensionName} · 需要手动处理";
                }
            }
            else if (line.Contains("WebDAV background sync completed", StringComparison.OrdinalIgnoreCase))
            {
                var uploadMatch = System.Text.RegularExpressions.Regex.Match(line, @"uploaded=(\d+)");
                var pullMatch = System.Text.RegularExpressions.Regex.Match(line, @"pulled=(\d+)");
                var uploaded = uploadMatch.Success ? uploadMatch.Groups[1].Value : "0";
                var pulled = pullMatch.Success ? pullMatch.Groups[1].Value : "0";
                return $"[{timeAgo}] ✓ 后台同步完成 · 上传 {uploaded} 个，下载 {pulled} 个";
            }
            else if (line.Contains("WebDAV background sync failed", StringComparison.OrdinalIgnoreCase))
            {
                // 提取错误信息
                var errorMatch = System.Text.RegularExpressions.Regex.Match(line, @"failed:.*?->\s*(.+)$");
                var errorMsg = errorMatch.Success ? errorMatch.Groups[1].Value : "未知错误";
                return $"[{timeAgo}] ✗ 后台同步失败 · {errorMsg}";
            }
            else if (line.Contains("WebDAV launcher config uploaded", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{timeAgo}] ↑ 上传 · 启动器配置 · 本机";
            }
            else if (line.Contains("WebDAV launcher config sync: no changes", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{timeAgo}] ✓ 启动器配置 · 无变化";
            }
            else if (line.Contains("登录", StringComparison.OrdinalIgnoreCase) || line.Contains("账号", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("成功", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[{timeAgo}] ✓ 账号登录成功";
                }
                else if (line.Contains("退出", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[{timeAgo}] ✓ 账号已退出";
                }
            }

            // 如果无法识别，返回空字符串（将被过滤掉）
            return "";
        }
        catch
        {
            return ""; // 解析失败的行不显示
        }
    }

    private static string GetExtensionName(string extensionId)
    {
        // 检查缓存
        if (_extensionNameCache.TryGetValue(extensionId, out var cachedName))
        {
            return cachedName;
        }

        // 尝试从本地目录读取扩展名称
        try
        {
            var manifestPath = Path.Combine(LocalExtensionCatalog.CatalogRootPath, extensionId, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                var nameMatch = System.Text.RegularExpressions.Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
                if (nameMatch.Success)
                {
                    var name = nameMatch.Groups[1].Value;
                    _extensionNameCache[extensionId] = name;
                    return name;
                }
            }
        }
        catch { /* 忽略读取错误 */ }

        // 如果无法读取，缓存ID本身
        _extensionNameCache[extensionId] = extensionId;
        return extensionId;
    }

    private static IEnumerable<string> ReadLogTailLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length <= 0)
        {
            return [];
        }

        var bytesToRead = (int)Math.Min(length, maxBytes);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        if (bytesToRead < length)
        {
            _ = reader.ReadLine();
        }

        var content = reader.ReadToEnd();
        return content.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    private void ClearWebDavConfiguration()
    {
        // Clear UI-bound properties
        EnableWebDavSync = false;
        WebDavServerUrl = string.Empty;
        WebDavRootPath = string.Empty;
        WebDavUsername = string.Empty;
        WebDavPasswordBox.Password = string.Empty;
        
        // Save cleared settings to persistent storage
        _mainWindow.SaveWebDavSettings(false, string.Empty, string.Empty, string.Empty);
        
        // Clear stored credential
        WebDavCredentialStore.Clear();
        
        // Update UI status
        RefreshWebDavSummary();
        SyncStatusText = "已退出登录，WebDAV 配置已清除。";
    }

    private async Task RefreshCloudAsync()
    {
        await _mainWindow.RefreshCloudFromSettingsAsync();
        RefreshAccountSummary();
        RefreshWebDavConfigFromExternal();
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshAccountObjectSyncStatus();
    }

    private async void RefreshAccountObjectSyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            button.IsEnabled = false;
        }

        try
        {
            SyncStatusText = "正在刷新账号配置同步状态...";
            await RefreshCloudAsync();
            RefreshSyncActivityLog();
        }
        catch (Exception ex)
        {
            SyncStatusText = $"账号配置同步刷新失败：{ex.Message}";
            RefreshAccountObjectSyncStatus();
        }
        finally
        {
            if (sender is System.Windows.Controls.Button completedButton)
            {
                completedButton.IsEnabled = true;
            }
        }
    }

    private async void UseLocalAccountSyncConflictButton_Click(object sender, RoutedEventArgs e)
    {
        await ResolveAccountSyncConflictFromButtonAsync(sender, useLocalVersion: true);
    }

    private async void AcceptRemoteAccountSyncConflictButton_Click(object sender, RoutedEventArgs e)
    {
        await ResolveAccountSyncConflictFromButtonAsync(sender, useLocalVersion: false);
    }

    private void ShowAccountSyncHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AccountSyncObjectStatusItem item } ||
            _mainWindow.CloudSyncClient is not { } client)
        {
            return;
        }

        var historyWindow = new CloudSyncHistoryWindow(
            _mainWindow,
            client,
            item.ObjectId,
            item.DisplayName,
            item.Revision)
        {
            Owner = this
        };
        historyWindow.ShowDialog();
        if (historyWindow.Restored)
        {
            SyncStatusText = "已恢复账号同步历史版本，本机配置已按云端新版本刷新。";
            RefreshAccountObjectSyncStatus();
            RefreshSyncActivityLog();
        }
    }

    private async Task ResolveAccountSyncConflictFromButtonAsync(object sender, bool useLocalVersion)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AccountSyncObjectStatusItem item } button)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            var result = await _mainWindow.ResolveCloudObjectConflictAsync(item.ObjectId, useLocalVersion);
            SyncStatusText = result.message;
            if (result.ok)
            {
                await RefreshCloudAsync();
            }
            RefreshAccountObjectSyncStatus();
            RefreshSyncActivityLog();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void RefreshAccountObjectSyncStatus()
    {
        var userId = _mainWindow.CloudSyncClient?.CurrentUserId ?? SyncSessionStore.Load()?.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            AccountSyncStatus = AccountSyncStatusView.Empty;
            return;
        }

        var state = CloudObjectSyncStateStore.Load(userId);
        var pendingIds = state.PendingObjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var objectIds = state.Objects.Keys
            .Union(pendingIds, StringComparer.OrdinalIgnoreCase)
            .Union(state.Conflicts.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetAccountSyncObjectOrder)
            .ThenBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var objectItems = objectIds.Select(objectId =>
        {
            state.Objects.TryGetValue(objectId, out var cached);
            state.PendingOperations.TryGetValue(objectId, out var pending);
            state.Conflicts.TryGetValue(objectId, out var conflict);
            var hasError = !string.IsNullOrWhiteSpace(pending?.LastError);
            var status = conflict != null
                ? "需要处理冲突"
                : pending != null
                ? hasError ? "等待重试" : "待上传"
                : cached?.Deleted == true ? "已删除" : "已同步";
            var source = string.IsNullOrWhiteSpace(cached?.UpdatedByDeviceName)
                ? cached?.UpdatedByDeviceId
                : cached.UpdatedByDeviceName;
            var detail = conflict != null
                ? $"云端版本 {conflict.RemoteRevision} · 本地副本已保留"
                : hasError
                ? pending!.LastError
                : pending != null
                    ? $"已尝试 {pending.AttemptCount} 次"
                    : string.IsNullOrWhiteSpace(source)
                        ? "来源设备未知"
                        : $"来源设备：{source}";
            return new AccountSyncObjectStatusItem(
                objectId,
                GetAccountSyncObjectDisplayName(objectId, cached),
                status,
                cached?.Revision ?? pending?.LastObservedRemoteRevision ?? 0,
                FormatAccountSyncTime(cached?.UpdatedAtUtc),
                detail,
                pending != null,
                hasError,
                conflict != null,
                state.ObjectHistoryAvailable);
        }).ToArray();

        var pendingCount = state.PendingOperations.Count;
        var errorCount = state.PendingOperations.Values.Count(static item => !string.IsNullOrWhiteSpace(item.LastError));
        var conflictCount = state.Conflicts.Count;
        var modeText = !state.ObjectSyncAvailable
            ? state.ServerProtocolVersion == 0 ? "正在建立连接" : "兼容备份模式"
            : state.ObjectsAuthoritative ? "增量同步模式" : "兼容迁移模式";
        var healthText = conflictCount > 0
            ? $"{conflictCount} 个跨设备冲突需要选择版本"
            : errorCount > 0
            ? $"{errorCount} 项数据同步失败，等待重试"
            : pendingCount > 0
                ? $"{pendingCount} 项数据等待同步"
                : state.Objects.Count > 0 ? "所有账号配置均已同步" : "等待首次同步";
        var explanation = state.ObjectsAuthoritative
            ? "云端数据为权威配置；旧版本客户端数据将自动读取兼容。"
            : state.ObjectSyncAvailable
                ? "正在安全迁移：同时写入增量数据 and 整包备份，可安全回退。"
                : "当前正在使用整包配置同步；暂不支持增量同步。";

        AccountSyncStatus = new AccountSyncStatusView(
            modeText,
            healthText,
            $"云端版本 {state.LastSyncedRevision}",
            $"已同步 {state.Objects.Count} 项数据 · 等待中 {pendingCount}",
            FormatAccountSyncTime(state.CapabilitiesCheckedAtUtc, "未建立连接"),
            explanation,
            errorCount > 0 || conflictCount > 0,
            pendingCount > 0,
            objectItems);
    }

    private static int GetAccountSyncObjectOrder(string objectId)
    {
        for (var index = 0; index < LauncherConfigObjectStore.Definitions.Length; index++)
        {
            if (LauncherConfigObjectStore.Definitions[index].ObjectId.Equals(objectId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        if (objectId.Equals(YanmObjectStore.LayoutObjectId, StringComparison.OrdinalIgnoreCase)) return 100;
        if (objectId.Equals(YanmObjectStore.ComponentStateIndexObjectId, StringComparison.OrdinalIgnoreCase)) return 101;
        if (YanmObjectStore.IsDynamicObjectId(objectId)) return 102;
        return int.MaxValue;
    }

    private static string GetAccountSyncObjectDisplayName(string objectId, CloudObjectSyncCacheEntry? cached)
    {
        var fixedName = objectId switch
        {
            "settings.general" => "通用设置",
            "settings.runtime" => "运行与扩展环境",
            "settings.ai" => "AI 服务设置",
            "settings.hotkeys" => "全局快捷键",
            "settings.mouseTriggers" => "鼠标与手势触发",
            "quickPanel.groups" => "快捷面板分组（旧版）",
            "quickPanel.groupIndex" => "快捷面板分组索引",
            "quickPanel.favorites" => "收藏、禁用与搜索范围",
            "radialMenu.pages" => "燕环页面（旧版）",
            "radialMenu.pageIndex" => "燕环页面索引",
            "yanyu.rules" => "燕语规则",
            "window.controls" => "窗口控制与燕选",
            "yanm.layout" => "燕幕布局与组件定义",
            "yanm.componentStateIndex" => "燕幕组件状态索引",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(fixedName)) return fixedName;

        if (cached != null && TryReadNestedPayloadName(cached.Payload, "group", out var groupName))
        {
            return objectId.StartsWith(AccountConfigObjectStore.QuickPanelContextPrefix, StringComparison.OrdinalIgnoreCase)
                ? $"上下文面板 · {groupName}"
                : $"全局面板 · {groupName}";
        }
        if (cached != null && TryReadNestedPayloadName(cached.Payload, "page", out var pageName))
        {
            return $"燕环页面 · {pageName}";
        }
        if (cached != null &&
            cached.Payload.ValueKind == JsonValueKind.Object &&
            cached.Payload.TryGetProperty("stateKey", out var stateKey) &&
            stateKey.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(stateKey.GetString()))
        {
            return $"燕幕状态 · {stateKey.GetString()}";
        }
        return objectId;
    }

    private static bool TryReadNestedPayloadName(JsonElement payload, string propertyName, out string name)
    {
        name = string.Empty;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(propertyName, out var item) &&
               item.ValueKind == JsonValueKind.Object &&
               item.TryGetProperty("name", out var nameProperty) &&
               nameProperty.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(name = nameProperty.GetString() ?? string.Empty);
    }

    private static string FormatAccountSyncTime(string? value, string fallback = "尚未同步")
    {
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : fallback;
    }

    private void RefreshAccountSummary()
    {
        var session = SyncSessionStore.Load();
        HostAssets.AppendLog($"Settings RefreshAccountSummary: sessionExists={session != null}, sessionExpired={session != null && session.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        if (session != null && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            IsAccountLoggedIn = true;
            AccountTitle = session.Username;
            AccountSubtitle = $"已登录 Cloud · 用户 ID {session.UserId}";
            AccountInitial = session.Username[..1].ToUpperInvariant();
            return;
        }

        IsAccountLoggedIn = false;
        AccountTitle = "未登录";
        AccountSubtitle = "点击左上角账户卡片登录或切换账号。";
        AccountInitial = "燕";
    }

    public void RefreshAccountFromExternal()
    {
        RefreshAccountSummary();
        SyncStatusText = _mainWindow.SyncStatus;
    }

    private void RefreshExtensionSummary()
    {
        var count = _cachedExtensionItems.Count > 0
            ? _cachedExtensionItems.Count
            : _mainWindow.GetExtensionsForSettings().Count;
        LocalExtensionSummary = $"当前机器已发现 {count} 个扩展。";
        OnPropertyChanged(nameof(ExtensionSearchSummary));
    }

    private void RefreshRecycleBinSummary()
    {
        var count = _cachedRecycleBinItems.Count;
        RecycleBinSummary = count == 0
            ? "回收站为空。"
            : $"当前回收站中有 {count} 个扩展。";
        OnPropertyChanged(nameof(RecycleBinSearchSummary));
    }

    private void RefreshExtensionItems()
    {
        if (_cachedExtensionItems.Count == 0)
        {
            RefreshExtensionCacheFromMainWindow();
        }

        var selectedExtensionId = SelectedExtensionItem?.ExtensionId;
        ExtensionItems.Clear();
        RecycleBinItems.Clear();

        var keyword = ExtensionSearchText.Trim();
        
        // 根据筛选模式选择数据源
        IEnumerable<SettingsExtensionItem> sourceItems = _extensionFilterMode switch
        {
            "published" => _cachedExtensionItems.Where(item => item.IsPublishedInStore),
            "disabled" => _cachedExtensionItems.Where(item => !item.IsEnabled),
            "shortcut" => _cachedExtensionItems.Where(item => item.HasShortcut),
            "recycle" => Enumerable.Empty<SettingsExtensionItem>(), // 回收站使用单独的数据源
            _ => _cachedExtensionItems // "all"
        };
        
        // 应用搜索关键词筛选
        var items = sourceItems
            .Where(item =>
                string.IsNullOrWhiteSpace(keyword) ||
                item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.DirectoryPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 如果是回收站模式，显示回收站项目
        if (_extensionFilterMode == "recycle")
        {
            var recycleBinKeyword = keyword;
            var recycleBinItems = _cachedRecycleBinItems
                .Where(item =>
                    string.IsNullOrWhiteSpace(recycleBinKeyword) ||
                    item.Title.Contains(recycleBinKeyword, StringComparison.OrdinalIgnoreCase) ||
                    item.ExtensionId.Contains(recycleBinKeyword, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(recycleBinKeyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var item in recycleBinItems)
            {
                RecycleBinItems.Add(item);
            }
        }
        else
        {
            foreach (var item in items)
            {
                ExtensionItems.Add(item);
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedExtensionId))
        {
            SelectedExtensionItem = ExtensionItems.FirstOrDefault(item =>
                item.ExtensionId.Equals(selectedExtensionId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            UpdateExtensionDetailPanelState();
        }

        OnPropertyChanged(nameof(ExtensionSearchSummary));
    }

    private void RefreshMouseGestureManagement()
    {
        MouseGestureItems.Clear();
        MouseGestureExtensionOptions.Clear();
        MouseGestureAppOptions.Clear();
        foreach (var app in ScanAppOptions())
        {
            MouseGestureAppOptions.Add(app);
        }
        MouseGestureQuickBindItems.Clear();

        var commands = _mainWindow.GetExtensionsForSettings();
        var commandMap = commands
            .GroupBy(static command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands.Where(static command => command.Source == CommandSource.LocalExtension && !command.IsProviderResult))
        {
            MouseGestureExtensionOptions.Add(new MouseGestureExtensionOption(command));
        }

        var assignedBySequence = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in LocalExtensionCatalog.LoadEntries())
        {
            var gesture = entry.Manifest.MouseGesture;
            if (gesture == null ||
                string.IsNullOrWhiteSpace(gesture.Sequence) && !MouseGestureTemplateRecognizer.HasTemplateData(gesture.Data))
            {
                continue;
            }

            commandMap.TryGetValue(entry.Manifest.Id, out var command);
            var directory = Path.GetDirectoryName(entry.ManifestPath);
            var sequence = MouseGestureNaming.NormalizeSequence(gesture.Sequence);
            var sign = string.IsNullOrWhiteSpace(gesture.Sign)
                ? MouseGestureNaming.GetDisplayName(sequence)
                : gesture.Sign.Trim();
            if (!string.IsNullOrWhiteSpace(sequence) && !assignedBySequence.ContainsKey(sequence))
            {
                assignedBySequence[sequence] = entry.Manifest.Name;
            }

            MouseGestureItems.Add(new SettingsMouseGestureItem(
                entry.Manifest.Id,
                entry.Manifest.Name,
                entry.Manifest.Category ?? command?.Category ?? "扩展",
                BuildGestureTriggerLabel(),
                sequence,
                sign,
                gesture.Data,
                gesture.MinDistance ?? 30,
                gesture.Tolerance,
                command?.IconSource ?? ExtensionIconLibrary.ResolveImageSource(entry.Manifest.Icon, directory),
                command?.VectorIcon ?? ExtensionIconLibrary.ResolveVectorIcon(entry.Manifest.Icon),
                command?.AccentBrush ?? CreateAccentBrush(entry.Manifest.AccentHex),
                command?.DisplayGlyph ?? BuildFallbackGlyph(entry.Manifest.Name)));
        }

        foreach (var appBind in _settings.MouseGestureAppBindings)
        {
            if (string.IsNullOrWhiteSpace(appBind.Sequence)) continue;
            var seq = MouseGestureNaming.NormalizeSequence(appBind.Sequence);
            var appName = string.IsNullOrWhiteSpace(appBind.AppName) ? "应用程序" : appBind.AppName;
            if (!assignedBySequence.ContainsKey(seq))
            {
                assignedBySequence[seq] = appName;
            }
            MouseGestureItems.Add(new SettingsMouseGestureItem(
                "app:" + appBind.AppPath,
                appName,
                "应用程序",
                BuildGestureTriggerLabel(),
                seq,
                MouseGestureNaming.GetDisplayName(seq),
                null,
                30,
                null,
                null,
                null,
                CreateAccentBrush("#3B82F6"),
                BuildFallbackGlyph(appName)));
        }

        foreach (var template in CommonMouseGestureTemplates)
        {
            assignedBySequence.TryGetValue(template.Sequence, out var assignedTitle);
            MouseGestureQuickBindItems.Add(new MouseGestureQuickBindItem(
                template.Sequence,
                template.Name,
                template.Description,
                assignedTitle));
        }

        OnPropertyChanged(nameof(MouseGestureManagementSummary));
        OnPropertyChanged(nameof(MouseGestureEmptyVisibility));
    }

    private async void ClearMouseGestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsMouseGestureItem item })
        {
            return;
        }

        if (item.ExtensionId != null && item.ExtensionId.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            var appPath = item.ExtensionId.Substring(4);
            _settings.MouseGestureAppBindings.RemoveAll(x => string.Equals(x.AppPath, appPath, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Sequence, item.Sequence, StringComparison.OrdinalIgnoreCase));
            AppSettingsStore.Save(_settings);
            _mainWindow.NotifyQuickPanelSettingsChanged("mouse-gesture-app-unbound", refreshYanmOverlay: false);
            _mainWindow.ReloadMouseGestureRegistrations();
            RefreshMouseGestureManagement();
            SyncStatusText = $"已解绑应用手势 [{item.Title}]。";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ExtensionId))
        {
            SyncStatusText = "当前手势没有可解绑的扩展。";
            return;
        }

        var result = await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(item.ExtensionId, null);
        await HandleMouseGestureUpdateResultAsync(result);
    }

    private async void EditMouseGestureExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsMouseGestureItem item })
        {
            return;
        }

        var extensionItem = _cachedExtensionItems.FirstOrDefault(x =>
            x.ExtensionId.Equals(item.ExtensionId, StringComparison.OrdinalIgnoreCase));
        if (extensionItem == null)
        {
            RefreshExtensionCacheFromMainWindow();
            extensionItem = _cachedExtensionItems.FirstOrDefault(x =>
                x.ExtensionId.Equals(item.ExtensionId, StringComparison.OrdinalIgnoreCase));
        }

        if (extensionItem != null)
        {
            await EditExtensionItemAsync(extensionItem);
            RefreshMouseGestureManagement();
        }
    }

        private List<MouseGestureAppOption> ScanAppOptions()
    {
        var list = new List<MouseGestureAppOption>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var procs = System.Diagnostics.Process.GetProcesses();
            foreach (var p in procs)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    var fileName = p.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(fileName) || !System.IO.File.Exists(fileName)) continue;
                    if (addedPaths.Contains(fileName)) continue;

                    var title = p.MainWindowTitle;
                    var name = string.IsNullOrWhiteSpace(title) ? p.ProcessName : title;
                    if (name.Length > 25) name = name.Substring(0, 25) + "...";

                    addedPaths.Add(fileName);
                    list.Add(new MouseGestureAppOption(name, fileName, "运行中的应用", true));
                }
                catch { }
            }
        }
        catch { }

        var commonApps = new (string Name, string Path)[]
        {
            ("记事本", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe")),
            ("计算器", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "calc.exe")),
            ("任务管理器", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskmgr.exe")),
            ("命令提示符", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")),
            ("资源管理器", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        };

        foreach (var app in commonApps)
        {
            if (System.IO.File.Exists(app.Path) && !addedPaths.Contains(app.Path))
            {
                addedPaths.Add(app.Path);
                list.Add(new MouseGestureAppOption(app.Name, app.Path, "全部应用程序", false));
            }
        }

        return list;
    }

    private void BindCommonMouseGestureAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item } || item.SelectedApp == null)
        {
            SyncStatusText = "请选择一个应用程序后再绑定常用手势。";
            return;
        }

        var appBindings = _settings.MouseGestureAppBindings;
        appBindings.RemoveAll(x => string.Equals(x.Sequence, item.Sequence, StringComparison.OrdinalIgnoreCase));
        appBindings.Add(new MouseGestureAppBinding
        {
            Sequence = item.Sequence,
            AppPath = item.SelectedApp.AppPath,
            AppName = item.SelectedApp.AppName
        });

        AppSettingsStore.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged("mouse-gesture-app-bound", refreshYanmOverlay: false);
        _mainWindow.ReloadMouseGestureRegistrations();
        RefreshMouseGestureManagement();
        SyncStatusText = $"已将手势 [{item.DisplayName}] 成功绑定到应用 [{item.SelectedApp.AppName}]！";
    }

    private async void BindCommonMouseGestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item } ||
            item.SelectedExtension == null ||
            string.IsNullOrWhiteSpace(item.SelectedExtension.ExtensionId))
        {
            SyncStatusText = "请选择一个扩展后再绑定常用手势。";
            return;
        }

        var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(_settings.MouseGestureTriggerMode);
        var gesture = new LocalExtensionMouseGestureManifest
        {
            Trigger = string.IsNullOrWhiteSpace(runtimeTrigger) ? "right-drag" : runtimeTrigger,
            Sequence = item.Sequence,
            Sign = item.DisplayName,
            MinDistance = 30
        };

        var result = await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(item.SelectedExtension.ExtensionId, gesture);
        await HandleMouseGestureUpdateResultAsync(result);
    }

    private void RefreshMouseGestureExtensionCandidates(MouseGestureQuickBindItem item, string? keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var query = MouseGestureExtensionOptions.Where(option =>
            string.IsNullOrWhiteSpace(keyword) ||
            option.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            option.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        item.FilteredExtensionOptions = new ObservableCollection<MouseGestureExtensionOption>(query.Take(24));
        item.IsExtensionPopupOpen = item.FilteredExtensionOptions.Count > 0;
    }

    private void RefreshMouseGestureAppCandidates(MouseGestureQuickBindItem item, string? keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var query = MouseGestureAppOptions.Where(option =>
            string.IsNullOrWhiteSpace(keyword) ||
            option.AppName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            option.AppPath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            option.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        item.FilteredAppOptions = new ObservableCollection<MouseGestureAppOption>(query.Take(24));
        item.IsAppPopupOpen = item.FilteredAppOptions.Count > 0;
    }

    private void MouseGestureExtensionSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MouseGestureQuickBindItem item })
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.SelectAll();
            }

            RefreshMouseGestureExtensionCandidates(item, item.ExtensionSearchText);
            item.IsExtensionPopupOpen = true;
        }
    }

    private void MouseGestureExtensionDropdownToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MouseGestureQuickBindItem item)
        {
            e.Handled = true;
            RefreshMouseGestureExtensionCandidates(item, string.Empty);
            item.IsExtensionPopupOpen = true;
            if (fe.Parent is Grid grid && grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is { } textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }
    }

    private void MouseGestureExtensionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item })
        {
            return;
        }

        var selected = item.SelectedExtension;
        if (selected == null ||
            !string.Equals(selected.Label, item.ExtensionSearchText ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            item.SelectedExtension = null;
        }

        RefreshMouseGestureExtensionCandidates(item, item.ExtensionSearchText);
        item.IsExtensionPopupOpen = true;
    }

    private void MouseGestureExtensionSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not DependencyObject source ||
            source is not FrameworkElement { DataContext: MouseGestureQuickBindItem item } ||
            e.Key != Key.Down ||
            item.FilteredExtensionOptions.Count == 0)
        {
            return;
        }

        var listBox = FindYarnSelectExtensionListBox(source);
        if (listBox != null)
        {
            listBox.SelectedIndex = 0;
            var itemContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            itemContainer?.Focus();
            listBox.Focus();
            e.Handled = true;
        }
    }

    private void MouseGestureExtensionListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitMouseGestureExtensionCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && listBox.DataContext is MouseGestureQuickBindItem item)
        {
            item.FilteredExtensionOptions = [];
            item.IsExtensionPopupOpen = false;
            e.Handled = true;
        }
    }

    private void MouseGestureExtensionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitMouseGestureExtensionCandidate(listBox);
        }
    }

    private void MouseGestureExtensionListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitMouseGestureExtensionCandidate(listBox);
        }
    }

    private static void CommitMouseGestureExtensionCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.DataContext is not MouseGestureQuickBindItem item ||
            listBox.SelectedItem is not MouseGestureExtensionOption option)
        {
            return;
        }

        item.SelectedExtension = option;
        item.ExtensionSearchText = option.Label;
        item.FilteredExtensionOptions = [];
        item.IsExtensionPopupOpen = false;
    }

    private void MouseGestureAppSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MouseGestureQuickBindItem item })
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.SelectAll();
            }

            RefreshMouseGestureAppCandidates(item, item.AppSearchText);
            item.IsAppPopupOpen = true;
        }
    }

    private void MouseGestureAppDropdownToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MouseGestureQuickBindItem item)
        {
            e.Handled = true;
            RefreshMouseGestureAppCandidates(item, string.Empty);
            item.IsAppPopupOpen = true;
            if (fe.Parent is Grid grid && grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is { } textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }
    }

    private void MouseGestureAppSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item })
        {
            return;
        }

        var selected = item.SelectedApp;
        if (selected == null ||
            !string.Equals(selected.AppName, item.AppSearchText ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            item.SelectedApp = null;
        }

        RefreshMouseGestureAppCandidates(item, item.AppSearchText);
        item.IsAppPopupOpen = true;
    }

    private void MouseGestureAppSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not DependencyObject source ||
            source is not FrameworkElement { DataContext: MouseGestureQuickBindItem item } ||
            e.Key != Key.Down ||
            item.FilteredAppOptions.Count == 0)
        {
            return;
        }

        var listBox = FindYarnSelectExtensionListBox(source);
        if (listBox != null)
        {
            listBox.SelectedIndex = 0;
            var itemContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            itemContainer?.Focus();
            listBox.Focus();
            e.Handled = true;
        }
    }

    private void MouseGestureAppListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitMouseGestureAppCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && listBox.DataContext is MouseGestureQuickBindItem item)
        {
            item.FilteredAppOptions = [];
            item.IsAppPopupOpen = false;
            e.Handled = true;
        }
    }

    private void MouseGestureAppListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitMouseGestureAppCandidate(listBox);
        }
    }

    private void MouseGestureAppListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitMouseGestureAppCandidate(listBox);
        }
    }

    private static void CommitMouseGestureAppCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.DataContext is not MouseGestureQuickBindItem item ||
            listBox.SelectedItem is not MouseGestureAppOption option)
        {
            return;
        }

        item.SelectedApp = option;
        item.AppSearchText = option.AppName;
        item.FilteredAppOptions = [];
        item.IsAppPopupOpen = false;
    }

    private void RefreshMouseGestureManagementButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMouseGestureManagement();
        SyncStatusText = "鼠标手势管理列表已刷新。";
    }

    private async Task HandleMouseGestureUpdateResultAsync((bool ok, string message, CommandItem? updated) result)
    {
        if (!string.IsNullOrWhiteSpace(result.message))
        {
            SyncStatusText = result.message;
        }

        if (!result.ok)
        {
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                System.Windows.MessageBox.Show(this, result.message, "更新鼠标手势失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionItems();
        RefreshMouseGestureManagement();
        await Task.CompletedTask;
    }

    private string BuildGestureTriggerLabel()
    {
        return MouseGestureTriggerSummary == "未启用"
            ? "全局触发未启用"
            : MouseGestureTriggerSummary;
    }

    private static SolidColorBrush CreateAccentBrush(string? accentHex)
    {
        try
        {
            var normalized = string.IsNullOrWhiteSpace(accentHex) ? "#FF3B82F6" : accentHex.Trim();
            if (normalized.StartsWith('#') && normalized.Length == 7)
            {
                normalized = "#FF" + normalized[1..];
            }

            return (SolidColorBrush)new BrushConverter().ConvertFromString(normalized)!;
        }
        catch
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        }
    }

    private static string BuildFallbackGlyph(string? title)
    {
        var first = (title ?? string.Empty).Trim().EnumerateRunes().FirstOrDefault();
        return first.Value == 0 ? "扩" : first.ToString().ToUpperInvariant();
    }

    private void ClearSelectedExtensionItem()
    {
        SelectedExtensionItem = null;
    }

    private void UpdateExtensionDetailPanelState()
    {
        if (ExtensionDetailColumn == null || ExtensionDetailPanel == null)
        {
            return;
        }

        // Preserve scroll position to prevent unwanted scroll jump
        var scrollOffset = ExtensionCardsScrollViewer?.VerticalOffset ?? 0;

        var isOpen = SelectedExtensionItem != null;
        ExtensionDetailColumn.Width = isOpen ? new GridLength(380) : new GridLength(0);
        ExtensionDetailPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        ScheduleExtensionCardWidthUpdate();

        // Restore scroll position after layout update
        if (ExtensionCardsScrollViewer != null)
        {
            Dispatcher.BeginInvoke(new Action(() => ExtensionCardsScrollViewer.ScrollToVerticalOffset(scrollOffset)), DispatcherPriority.Loaded);
        }
    }

    private void ExtensionCardsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleExtensionCardWidthUpdate();
    }

    private void ScheduleExtensionCardWidthUpdate()
    {
        if (ExtensionCardsScrollViewer == null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(UpdateExtensionCardWidth), DispatcherPriority.Loaded);
    }

    private void UpdateExtensionCardWidth()
    {
        if (ExtensionCardsScrollViewer == null)
        {
            return;
        }

        const double minCardWidth = 240;
        const double cardGap = 14;
        const double viewportPaddingAllowance = 24;

        var viewportWidth = ExtensionCardsScrollViewer.ViewportWidth > 0
            ? ExtensionCardsScrollViewer.ViewportWidth
            : ExtensionCardsScrollViewer.ActualWidth;

        var availableWidth = Math.Max(0, viewportWidth - viewportPaddingAllowance);
        if (availableWidth <= 0)
        {
            ExtensionCardWidth = 280;
            return;
        }

        var columnCount = Math.Max(1, (int)Math.Floor((availableWidth + cardGap) / (minCardWidth + cardGap)));
        var computedWidth = (availableWidth - ((columnCount - 1) * cardGap)) / columnCount;
        ExtensionCardWidth = Math.Max(minCardWidth, computedWidth);
    }

    private void RefreshRecycleBinItems()
    {
        RecycleBinItems.Clear();

        var keyword = RecycleBinSearchText.Trim();
        var items = _cachedRecycleBinItems
            .Where(item =>
                string.IsNullOrWhiteSpace(keyword) ||
                item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in items)
        {
            RecycleBinItems.Add(item);
        }

        OnPropertyChanged(nameof(RecycleBinSearchSummary));
    }

    private void RefreshExtensionCacheFromMainWindow()
    {
        _cachedExtensionItems = BuildSettingsExtensionItems(
            _mainWindow.GetExtensionsForSettings(),
            publishedMap: null);
    }

    private List<SettingsExtensionItem> BuildSettingsExtensionItems(
        IReadOnlyList<CommandItem> commands,
        IReadOnlyDictionary<string, CloudExtensionRecord>? publishedMap)
    {
        return commands
            .Select(command =>
            {
                CloudExtensionRecord? cloudRecord = null;
                publishedMap?.TryGetValue(command.ExtensionId, out cloudRecord);

                return new SettingsExtensionItem(
                    command.ExtensionId,
                    command.Title,
                    command.Subtitle,
                    command.Category,
                    command.DeclaredVersion,
                    command.ExtensionDirectoryPath ?? string.Empty,
                    command.Category.Contains("网页搜索", StringComparison.OrdinalIgnoreCase) ? "网页搜索扩展" : "本地扩展",
                    command.Source == CommandSource.LocalExtension,
                    _mainWindow.IsExtensionEnabled(command.ExtensionId),
                    cloudRecord?.IsPublished != 0,
                    cloudRecord?.PublisherUsername ?? string.Empty,
                    command.GlobalShortcut ?? string.Empty,
                    command.IconSource,
                    command.VectorIcon,
                    command.AccentBrush,
                    command.DisplayGlyph,
                    command.Startup?.Mode ?? string.Empty,
                    command.Startup?.Schedule ?? string.Empty);
            })
            .ToList();
    }

    private void RefreshShortcutItems()
    {
        ShortcutItems.Clear();
        foreach (var command in _mainWindow.GetLocalExtensionsForSettings())
        {
            ShortcutItems.Add(new SettingsShortcutItem(
                command.ExtensionId,
                command.Title,
                command.Category,
                command.GlobalShortcut));
        }
    }

    private void SearchPopupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem is SearchDisplayItem selectedItem)
        {
            var target = NavigationItems.FirstOrDefault(t => t.Key == selectedItem.TabKey);
            if (target != null)
            {
                SelectedNavigation = target;
            }
        }
    }

    private void SearchPopupListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SearchPopupListBox.SelectedItem is SearchDisplayItem selectedItem)
        {
            ActivateSearchResult(selectedItem, clearSelection: true);
            e.Handled = true;
        }
    }

    private void SearchPopupListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Delete)
        {
            ReturnFocusToSearchBoxForEditing(e.Key);
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Down)
        {
            if (SearchPopupListBox.Items.Count > 0)
            {
                var nextIndex = SearchPopupListBox.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchPopupListBox.SelectedIndex + 1, SearchPopupListBox.Items.Count - 1);
                SearchPopupListBox.SelectedIndex = nextIndex;
                SearchPopupListBox.ScrollIntoView(SearchPopupListBox.SelectedItem);
            }

            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Up)
        {
            if (SearchPopupListBox.Items.Count > 0)
            {
                var previousIndex = SearchPopupListBox.SelectedIndex < 0
                    ? 0
                    : Math.Max(SearchPopupListBox.SelectedIndex - 1, 0);
                SearchPopupListBox.SelectedIndex = previousIndex;
                SearchPopupListBox.ScrollIntoView(SearchPopupListBox.SelectedItem);
            }

            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (SearchPopupListBox.SelectedItem is SearchDisplayItem selectedItem)
            {
                ActivateSearchResult(selectedItem, clearSelection: true);
            }

            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            IsSearchPopupOpen = false;
            SettingsSearchBox.Focus();
            e.Handled = true;
        }
    }

    private void ReturnFocusToSearchBoxForEditing(System.Windows.Input.Key key)
    {
        SettingsSearchBox.Focus();
        SettingsSearchBox.CaretIndex = SettingsSearchBox.Text?.Length ?? 0;

        var text = SettingsSearchText ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        if (key == System.Windows.Input.Key.Back)
        {
            SettingsSearchText = text[..^1];
        }
        else if (key == System.Windows.Input.Key.Delete)
        {
            SettingsSearchText = string.Empty;
        }

        SettingsSearchBox.CaretIndex = SettingsSearchText?.Length ?? 0;
    }

    private static string? BuildStandardHotkeyString(System.Windows.Input.Key key, ModifierKeys? activeModifiers = null)
    {
        if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
            key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
            key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
            key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
        {
            return null;
        }

        var currentModifiers = activeModifiers ?? System.Windows.Input.Keyboard.Modifiers;
        var modifiers = new List<string>();
        if (currentModifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) modifiers.Add("Ctrl");
        if (currentModifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) modifiers.Add("Shift");
        if (currentModifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) modifiers.Add("Alt");
        if (currentModifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) modifiers.Add("Win");

        var keyStr = key.ToString();
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
        {
            keyStr = (key - System.Windows.Input.Key.D0).ToString();
        }
        else if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
        {
            keyStr = "Num" + (key - System.Windows.Input.Key.NumPad0).ToString();
        }

        modifiers.Add(keyStr);
        return string.Join("+", modifiers);
    }

    private void SettingsSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ApplySettingsSearch(SettingsSearchText);
            if (SearchPopupListBox.SelectedItem is SearchDisplayItem selectedItem)
            {
                ActivateSearchResult(selectedItem, clearSelection: true);
            }
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Down && IsSearchPopupOpen)
        {
            if (SearchPopupListBox.Items.Count > 0)
            {
                var nextIndex = SearchPopupListBox.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchPopupListBox.SelectedIndex + 1, SearchPopupListBox.Items.Count - 1);
                SearchPopupListBox.SelectedIndex = nextIndex;
                SearchPopupListBox.ScrollIntoView(SearchPopupListBox.SelectedItem);
                SearchPopupListBox.Focus();
                e.Handled = true;
            }
        }
    }

    private void ApplySettingsSearch(string query)
    {
        query = query.Trim();
        HighlightKeyword = query;
        MatchedSearchItems.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchPopupOpen = false;
            return;
        }

        // 1. 匹配 Tab 标题
        var tabMatches = NavigationItems.Where(item => 
            item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var allDetailMatches = SettingsSearchData.AllSearchItems
            .Concat(_dynamicSettingsSearchItems)
            .GroupBy(item => $"{item.TabKey}\u001f{item.DisplayTitle}\u001f{item.MatchTerm}")
            .Select(group => group.First());

        // 2. 匹配右侧具体设置正文
        var detailMatches = allDetailMatches.Where(item =>
            item.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.MatchTerm.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var searchDisplayItems = new List<SearchDisplayItem>();

        // 优先把 Tab 级别的匹配加在前面
        foreach (var tab in tabMatches)
        {
            searchDisplayItems.Add(new SearchDisplayItem(
                tab.Key,
                tab.Title,
                tab.IconGeometry
            ));
        }

        // 再把具体的设置项正文匹配加在后面
        foreach (var match in detailMatches)
        {
            var tabIcon = NavigationItems.FirstOrDefault(t => t.Key == match.TabKey)?.IconGeometry;
            searchDisplayItems.Add(new SearchDisplayItem(
                match.TabKey,
                match.DisplayTitle,
                tabIcon
            ));
        }

        // 根据 DisplayTitle 去重，保留前 8 个
        var uniqueItems = searchDisplayItems.GroupBy(x => x.DisplayTitle).Select(g => g.First()).Take(8).ToList();

        foreach (var item in uniqueItems)
        {
            MatchedSearchItems.Add(item);
        }

        IsSearchPopupOpen = MatchedSearchItems.Count > 0;

        if (MatchedSearchItems.Count > 0)
        {
            var firstTab = NavigationItems.FirstOrDefault(t => t.Key == MatchedSearchItems[0].TabKey);
            if (firstTab != null)
            {
                SelectedNavigation = firstTab;
            }

            SearchPopupListBox.SelectedIndex = 0;
        }
    }

    private void ActivateSearchResult(SearchDisplayItem selectedItem, bool clearSelection)
    {
        var target = NavigationItems.FirstOrDefault(t => t.Key == selectedItem.TabKey);
        if (target != null)
        {
            SelectedNavigation = target;
        }

        IsSearchPopupOpen = false;
        if (clearSelection)
        {
            SearchPopupListBox.SelectedIndex = -1;
        }
    }

    private void SetSnapAssistRecordingState(bool isRecording)
    {
        if (_isRecordingSnapAssistHotkey == isRecording)
        {
            return;
        }

        _isRecordingSnapAssistHotkey = isRecording;
        OnPropertyChanged(nameof(SnapAssistRecorderText));
        OnPropertyChanged(nameof(SnapAssistRecorderForeground));
    }

    private void NotifySnapAssistHotkeyDisplayChanged()
    {
        OnPropertyChanged(nameof(WindowSnapAssistHotkey));
        OnPropertyChanged(nameof(SnapAssistRecorderText));
        OnPropertyChanged(nameof(SnapAssistRecorderForeground));
    }

    private void SetLauncherRecordingState(bool isRecording)
    {
        if (_isRecordingLauncherHotkey == isRecording)
        {
            return;
        }

        _isRecordingLauncherHotkey = isRecording;
        if (!isRecording)
        {
            _lastLauncherDoubleTapCandidate = null;
            _lastLauncherDoubleTapAtUtc = default;
        }
        OnPropertyChanged(nameof(LauncherRecorderText));
        OnPropertyChanged(nameof(LauncherRecorderForeground));
    }

    private void NotifyLauncherHotkeyDisplayChanged()
    {
        OnPropertyChanged(nameof(LauncherHotkey));
        OnPropertyChanged(nameof(LauncherRecorderText));
        OnPropertyChanged(nameof(LauncherRecorderForeground));
    }

    private string GetLauncherHotkeyDisplayText() =>
        string.IsNullOrWhiteSpace(_launcherHotkey) ? "设置快捷键" : FormatLauncherShortcutForDisplay(_launcherHotkey);

    private static string FormatLauncherShortcutForDisplay(string shortcut) => shortcut switch
    {
        "DoubleCtrl" => "双击Ctrl",
        "DoubleAlt" => "双击Alt",
        _ => shortcut
    };

    private IntPtr SettingsWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 彻底拦截 Windows 默认的背景擦除消息，防止 DirectX 绘制首帧前出现系统白色画刷闪烁
        if (msg == 0x0014 /* WM_ERASEBKGND */)
        {
            handled = true;
            return new IntPtr(1);
        }

        if (!_isRecordingLauncherHotkey)
        {
            return IntPtr.Zero;
        }

        if (msg != WmKeyDown && msg != WmSysKeyDown && msg != WmKeyUp && msg != WmSysKeyUp)
        {
            return IntPtr.Zero;
        }

        var key = KeyInterop.KeyFromVirtualKey(wParam.ToInt32());
        if (key == Key.None)
        {
            return IntPtr.Zero;
        }

        var modifiers = GetCurrentModifiers();
        handled = msg is WmKeyDown or WmSysKeyDown
            ? HandleLauncherRecorderKeyDown(key, modifiers)
            : HandleLauncherRecorderKeyUp(key);
        return IntPtr.Zero;
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        var mods = ModifierKeys.None;
        if ((GetKeyState(0x11) & 0x8000) != 0) mods |= ModifierKeys.Control;
        if ((GetKeyState(0x12) & 0x8000) != 0) mods |= ModifierKeys.Alt;
        if ((GetKeyState(0x10) & 0x8000) != 0) mods |= ModifierKeys.Shift;
        if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) mods |= ModifierKeys.Windows;
        return mods;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private void RebuildDynamicSettingsSearchItems()
    {
        _dynamicSettingsSearchItems.Clear();

        foreach (var navigationItem in NavigationItems)
        {
            if (!TryGetSearchSectionRoot(navigationItem.Key, out var root) || root == null)
            {
                continue;
            }

            foreach (var text in CollectSearchableSectionTexts(root))
            {
                _dynamicSettingsSearchItems.Add(new SettingsSearchItem(
                    navigationItem.Key,
                    $"{navigationItem.Title} - {text}",
                    $"{navigationItem.Title} {text} {navigationItem.Key}"));
            }
        }
    }

    private void RefreshSelectedSectionHighlights()
    {
        ClearSelectedSectionHighlights();

        if (string.IsNullOrWhiteSpace(HighlightKeyword) ||
            SelectedNavigation == null ||
            !TryGetSearchSectionRoot(SelectedNavigation.Key, out var root) ||
            root == null)
        {
            return;
        }

        foreach (var textBlock in EnumerateDescendantTextBlocks(root))
        {
            if (textBlock is HighlightedTextBlock)
            {
                continue;
            }

            if (BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
            {
                continue;
            }

            var text = textBlock.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text) || !text.Contains(HighlightKeyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _searchHighlightSnapshots[textBlock] = textBlock.Text ?? string.Empty;
            ApplyInlineHighlight(textBlock, text, HighlightKeyword);
        }
    }

    private void ClearSelectedSectionHighlights()
    {
        foreach (var (textBlock, originalText) in _searchHighlightSnapshots.ToArray())
        {
            textBlock.Inlines.Clear();
            textBlock.Text = originalText;
        }

        _searchHighlightSnapshots.Clear();
    }

    private static void ApplyInlineHighlight(TextBlock textBlock, string text, string keyword)
    {
        textBlock.Inlines.Clear();

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                textBlock.Inlines.Add(new Run(text[startIndex..]) { Foreground = textBlock.Foreground });
                break;
            }

            if (matchIndex > startIndex)
            {
                textBlock.Inlines.Add(new Run(text[startIndex..matchIndex]) { Foreground = textBlock.Foreground });
            }

            textBlock.Inlines.Add(new Run(text.Substring(matchIndex, keyword.Length))
            {
                Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF111827")),
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E6FDE047")),
                FontWeight = FontWeights.SemiBold
            });

            startIndex = matchIndex + keyword.Length;
        }
    }

    private bool TryGetSearchSectionRoot(string sectionKey, out FrameworkElement? root)
    {
        root = sectionKey switch
        {
            "general" => GeneralSectionRoot,
            "ai" => AiSectionRoot,
            "environment" => EnvironmentSectionRoot,
            "sync" => SyncSectionRoot,
            "extensions" => ExtensionsSectionRoot,
            "quickpanel" => QuickPanelSectionRoot,
            "mousegestures" => MouseGesturesSectionRoot,
            "radial" => RadialSectionRoot,
            "yarnselect" => YarnSelectSectionRoot,
            "yanm" => YanmSectionRoot,
            "about" => AboutSectionRoot,
            _ => null
        };

        return root != null;
    }

    private static IEnumerable<string> CollectSearchableSectionTexts(DependencyObject root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var textBlock in EnumerateDescendantTextBlocks(root))
        {
            var text = NormalizeSearchText(textBlock.Text);
            if (text.Length >= 2 && seen.Add(text))
            {
                yield return text;
            }
        }

        foreach (var dependencyObject in EnumerateDescendants(root))
        {
            if (dependencyObject is not FrameworkElement element)
            {
                continue;
            }

            foreach (var candidate in ExtractSearchableElementText(element))
            {
                if (candidate.Length >= 2 && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<TextBlock> EnumerateDescendantTextBlocks(DependencyObject root) =>
        EnumerateDescendants(root).OfType<TextBlock>();

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
            {
                continue;
            }

            yield return dependencyObject;

            foreach (var descendant in EnumerateDescendants(dependencyObject))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<string> ExtractSearchableElementText(FrameworkElement element)
    {
        if (element is System.Windows.Controls.Button button && button.Content is string buttonText)
        {
            var normalized = NormalizeSearchText(buttonText);
            if (normalized.Length >= 2)
            {
                yield return normalized;
            }
        }

        if (element.ToolTip is string tooltip)
        {
            var normalized = NormalizeSearchText(tooltip);
            if (normalized.Length >= 2)
            {
                yield return normalized;
            }
        }
    }

    private static string NormalizeSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim();
        if (normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tencent://", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized.Replace(Environment.NewLine, " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static bool SettingsSearchMatches(string sectionKey, string query)
    {
        return GetSettingsSearchTerms(sectionKey)
            .Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         query.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetSettingsSearchTerms(string sectionKey) => sectionKey switch
    {
        "general" =>
        [
            "常规", "开机", "启动", "托盘", "关闭", "主程序", "快捷键", "general", "startup", "launch", "tray", "hotkey"
        ],
        "sync" =>
        [
            "同步", "云", "云同步", "账号", "登录", "注册", "坚果云", "webdav", "cloud", "cloudflare", "服务器", "密码", "配置"
        ],
        "environment" =>
        [
            "环境变量", "密钥", "key", "token", "notion", "api", "secret", "env", "environment"
        ],
        "extensions" =>
        [
            "扩展", "插件", "目录", "本地", "删除", "编辑", "搜索", "打开目录", "extension", "plugin", "folder", "delete", "edit"
        ],
        "recycle" =>
        [
            "回收站", "恢复", "彻底删除", "已删除", "扩展回收站", "recycle", "trash", "restore", "deleted"
        ],
        "shortcuts" =>
        [
            "快捷键", "热键", "组合键", "录制", "全局快捷键", "shortcut", "hotkey", "keyboard"
        ],
        "quickpanel" =>
        [
            "鼠标触发", "鼠标面板", "快捷面板", "面板", "鼠标", "右键", "中键", "x1", "x2", "长按", "滚轮", "松开", "quick panel", "mouse", "middle", "right click"
        ],
        "mousegestures" =>
        [
            "鼠标手势", "手势", "轨迹", "录制", "绑定", "常用手势", "gesture", "mouse gesture", "stroke", "draw"
        ],
        "radial" =>
        [
            "燕环", "轮盘", "游戏轮盘", "capslock", "caps", "radial", "ring", "wheel", "gesture"
        ],
        "yarnselect" =>
        [
            "燕选", "左键辅助", "鼠标选中", "选中操作", "复制", "剪切", "粘贴", "搜索选中", "left button", "selection", "copy", "paste"
        ],
        "yanm" =>
        [
            "燕幕", "全局信息层", "信息展示", "组件", "webview", "html", "win", "capslock", "按住", "双击", "overlay", "dashboard"
        ],
        "about" =>
        [
            "关于", "版本", "协议", "logo", "about", "version", "license"
        ],
        _ => [sectionKey]
    };

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.TextBox or
                System.Windows.Controls.Primitives.ButtonBase or
                Selector or
                System.Windows.Controls.Primitives.ScrollBar or
                ResizeGrip)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void UpdateQuickPanelMouseTrigger(bool value, Action<QuickPanelMouseTriggerSettings> update)
    {
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        update(_settings.QuickPanelMouseTriggers);
        OnPropertyChanged();
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
    }

    private void UpdateRadialMenu<T>(T value, Action<RadialMenuSettings> update)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        update(_settings.RadialMenu);
        OnPropertyChanged();
        OnPropertyChanged(nameof(RadialMouseTriggerMode));
        OnPropertyChanged(nameof(RadialUsesCustomShortcut));
        OnPropertyChanged(nameof(RadialCustomShortcut));
        OnPropertyChanged(nameof(RadialMenuSummary));
    }

    private static bool IsRightMouseTriggerMode(string mode)
    {
        return string.Equals(mode, MouseTriggerModes.RightDrag, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, MouseTriggerModes.RightLongPress, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, MouseTriggerModes.CtrlRightClick, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyMouseTriggerModeToRadialFlags(RadialMenuSettings radial)
    {
        var mode = MouseTriggerModes.Normalize(radial.MouseTriggerMode);
        switch (mode)
        {
            case MouseTriggerModes.MiddleDown:
                radial.TriggerMiddleButtonDown = true;
                break;
            case MouseTriggerModes.X1Down:
                radial.TriggerX1ButtonDown = true;
                break;
            case MouseTriggerModes.X2Down:
                radial.TriggerX2ButtonDown = true;
                break;
            case MouseTriggerModes.CtrlLeftClick:
                radial.TriggerCtrlLeftClick = true;
                break;
            case MouseTriggerModes.CtrlRightClick:
                radial.TriggerCtrlRightClick = true;
                break;
            case MouseTriggerModes.CtrlMiddleClick:
                radial.TriggerCtrlMiddleClick = true;
                break;
            case MouseTriggerModes.MiddleLongPress:
                radial.TriggerMiddleButtonLongPress = true;
                break;
            case MouseTriggerModes.RightLongPress:
                radial.TriggerRightButtonLongPress = true;
                break;
            case MouseTriggerModes.RightDrag:
                radial.TriggerRightButtonDrag = true;
                break;
            case MouseTriggerModes.MiddleDrag:
                radial.TriggerMiddleButtonDrag = true;
                break;
            case MouseTriggerModes.HorizontalWheel:
                radial.TriggerHorizontalWheel = true;
                break;
        }
    }

    public string RadialPreviewDebugLog
    {
        get => _radialPreviewDebugLog;
        private set
        {
            if (value == _radialPreviewDebugLog)
            {
                return;
            }

            _radialPreviewDebugLog = value;
            OnPropertyChanged();
        }
    }

    private static void SyncRadialMouseTriggerModeFromFlags(RadialMenuSettings radial)
    {
        radial.MouseTriggerMode =
            radial.TriggerRightButtonDrag ? MouseTriggerModes.RightDrag :
            radial.TriggerMiddleButtonDrag ? MouseTriggerModes.MiddleDrag :
            radial.TriggerRightButtonLongPress ? MouseTriggerModes.RightLongPress :
            radial.TriggerMiddleButtonLongPress ? MouseTriggerModes.MiddleLongPress :
            radial.TriggerMiddleButtonDown ? MouseTriggerModes.MiddleDown :
            radial.TriggerX1ButtonDown ? MouseTriggerModes.X1Down :
            radial.TriggerX2ButtonDown ? MouseTriggerModes.X2Down :
            radial.TriggerHorizontalWheel ? MouseTriggerModes.HorizontalWheel :
            radial.TriggerCtrlLeftClick ? MouseTriggerModes.CtrlLeftClick :
            radial.TriggerCtrlRightClick ? MouseTriggerModes.CtrlRightClick :
            radial.TriggerCtrlMiddleClick ? MouseTriggerModes.CtrlMiddleClick :
            MouseTriggerModes.None;
    }

    private static void ApplyMouseTriggerModeToYanmFlags(YanmSettings yanm)
    {
        var mode = MouseTriggerModes.Normalize(yanm.MouseTriggerMode);
        switch (mode)
        {
            case MouseTriggerModes.MiddleDown:
                yanm.TriggerMiddleButtonDown = true;
                break;
            case MouseTriggerModes.X1Down:
                yanm.TriggerX1ButtonDown = true;
                break;
            case MouseTriggerModes.X2Down:
                yanm.TriggerX2ButtonDown = true;
                break;
            case MouseTriggerModes.CtrlLeftClick:
                yanm.TriggerCtrlLeftClick = true;
                break;
            case MouseTriggerModes.CtrlRightClick:
                yanm.TriggerCtrlRightClick = true;
                break;
            case MouseTriggerModes.CtrlMiddleClick:
                yanm.TriggerCtrlMiddleClick = true;
                break;
            case MouseTriggerModes.MiddleLongPress:
                yanm.TriggerMiddleButtonLongPress = true;
                break;
            case MouseTriggerModes.RightLongPress:
                yanm.TriggerRightButtonLongPress = true;
                break;
            case MouseTriggerModes.RightDrag:
                yanm.TriggerRightButtonDrag = true;
                break;
            case MouseTriggerModes.MiddleDrag:
                yanm.TriggerMiddleButtonDrag = true;
                break;
            case MouseTriggerModes.HorizontalWheel:
                yanm.TriggerHorizontalWheel = true;
                break;
        }
    }

    private static void SyncYanmMouseTriggerModeFromFlags(YanmSettings yanm)
    {
        yanm.MouseTriggerMode =
            yanm.TriggerRightButtonDrag ? MouseTriggerModes.RightDrag :
            yanm.TriggerMiddleButtonDrag ? MouseTriggerModes.MiddleDrag :
            yanm.TriggerRightButtonLongPress ? MouseTriggerModes.RightLongPress :
            yanm.TriggerMiddleButtonLongPress ? MouseTriggerModes.MiddleLongPress :
            yanm.TriggerMiddleButtonDown ? MouseTriggerModes.MiddleDown :
            yanm.TriggerX1ButtonDown ? MouseTriggerModes.X1Down :
            yanm.TriggerX2ButtonDown ? MouseTriggerModes.X2Down :
            yanm.TriggerHorizontalWheel ? MouseTriggerModes.HorizontalWheel :
            yanm.TriggerCtrlLeftClick ? MouseTriggerModes.CtrlLeftClick :
            yanm.TriggerCtrlRightClick ? MouseTriggerModes.CtrlRightClick :
            yanm.TriggerCtrlMiddleClick ? MouseTriggerModes.CtrlMiddleClick :
            MouseTriggerModes.None;
    }

    private void QueueQuickPanelTriggerSave(int delayMs = 500)
    {
        if (_quickPanelSaveTimer == null)
        {
            _quickPanelSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _quickPanelSaveTimer.Tick += (s, e) => { _quickPanelSaveTimer.Stop(); SaveQuickPanelTriggerSettings(); };
        }
        else
        {
            _quickPanelSaveTimer.Stop();
            _quickPanelSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _quickPanelSaveTimer.Start();
    }

    private void FlushQuickPanelTriggerSave()
    {
        if (_quickPanelSaveTimer != null && _quickPanelSaveTimer.IsEnabled)
        {
            _quickPanelSaveTimer.Stop();
            SaveQuickPanelTriggerSettings();
        }
    }

    private void SaveQuickPanelTriggerSettings()
    {
        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.Yanm ??= new YanmSettings();
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        _settings.RadialMenu.ActivationKey = RadialActivationKeys.Normalize(_settings.RadialMenu.ActivationKey);
        _settings.RadialMenu.CustomShortcut = (_settings.RadialMenu.CustomShortcut ?? string.Empty).Trim();
        _settings.RadialMenu.WhitelistedProcesses = ParseProcessList(string.Join(", ", _settings.RadialMenu.WhitelistedProcesses ?? []));
        _settings.RadialMenu.BlacklistedProcesses = ParseProcessList(string.Join(", ", _settings.RadialMenu.BlacklistedProcesses ?? []));
        SyncRadialMouseTriggerModeFromFlags(_settings.RadialMenu);
        SyncYanmMouseTriggerModeFromFlags(_settings.Yanm);
        ApplyMouseTriggerModeToRadialFlags(_settings.RadialMenu);
        ApplyMouseTriggerModeToYanmFlags(_settings.Yanm);
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("quickpanel-trigger-settings-saved");
        SyncStatusText = $"鼠标面板触发已保存：{QuickPanelTriggerSummary}";
        _quickPanelStatusHideTimer = ShowSaveStatusTemporarily(_quickPanelStatusHideTimer, visible => IsQuickPanelSaveStatusVisible = visible);
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerMode));
        OnPropertyChanged(nameof(MouseGestureManagementSummary));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(RadialAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(RadialMenuSummary));
        if (IsMouseGesturesSelected)
        {
            RefreshMouseGestureManagement();
        }
    }

    private void SaveYarnSelectSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveYarnSelectSettings();
    }

    private void PickYanmWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕幕白名单", _settings.Yanm.WhitelistedProcesses ?? new(), list =>
        {
            _settings.Yanm.WhitelistedProcesses = list;
            OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
            SaveYanmSettings(requireCustomShortcut: false);
        });
    }

    private void PickYanmBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕幕黑名单", _settings.Yanm.BlacklistedProcesses ?? new(), list =>
        {
            _settings.Yanm.BlacklistedProcesses = list;
            OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
            SaveYanmSettings(requireCustomShortcut: false);
        });
    }

    private void PickRadialWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕环白名单", _settings.RadialMenu.WhitelistedProcesses ?? new(), list =>
        {
            _settings.RadialMenu.WhitelistedProcesses = list;
            OnPropertyChanged(nameof(RadialWhitelistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private void PickGlobalServiceBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("全局服务黑名单", _settings.GlobalServiceBlacklistedProcesses ?? new(), list =>
        {
            _settings.GlobalServiceBlacklistedProcesses = list;
            OnPropertyChanged(nameof(GlobalServiceBlacklistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private void PickRadialBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕环黑名单", _settings.RadialMenu.BlacklistedProcesses ?? new(), list =>
        {
            _settings.RadialMenu.BlacklistedProcesses = list;
            OnPropertyChanged(nameof(RadialBlacklistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private void PickYarnSelectWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕选白名单", _settings.YarnSelect.WhitelistedProcesses ?? new(), list =>
        {
            _settings.YarnSelect.WhitelistedProcesses = list;
            OnPropertyChanged(nameof(YarnSelectWhitelistedProcessesText));
            SaveYarnSelectSettings();
        });
    }

    private void PickYarnSelectBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕选黑名单", _settings.YarnSelect.BlacklistedProcesses ?? new(), list =>
        {
            _settings.YarnSelect.BlacklistedProcesses = list;
            OnPropertyChanged(nameof(YarnSelectBlacklistedProcessesText));
            SaveYarnSelectSettings();
        });
    }

    private void OpenProcessPickerForList(string targetName, List<string> currentList, Action<List<string>> updateAction)
    {
        var picker = new ProcessPickerWindow(targetName, $"请选择要加入 {targetName} 的进程：", string.Empty, currentList);
        if (picker.ShowDialog() == true)
        {
            foreach (var b in picker.Blacklist)
            {
                if (!string.IsNullOrWhiteSpace(b.ExecutablePath))
                {
                    _settings.ProcessExecutablePaths[b.ProcessName] = b.ExecutablePath;
                }
            }
            updateAction(picker.Blacklist.Select(b => b.ProcessName).ToList());
        }
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var className = new System.Text.StringBuilder(256);
            if (GetClassName(hwnd, className, className.Capacity) > 0)
            {
                var classStr = className.ToString();
                if (string.Equals(classStr, "Progman", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return "desktop";
                }
            }

            _ = GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? string.Empty : Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Settings: failed to get foreground process, error={ex.Message}");
            return string.Empty;
        }
    }

    private void UpdateYarnSelect(bool value, Action<YarnSelectSettings> update)
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        update(_settings.YarnSelect);
        OnPropertyChanged();
        OnPropertyChanged(nameof(YarnSelectSummary));
        QueueYarnSelectSave(200);
    }

    private void QueueYarnSelectSave(int delayMs = 500)
    {
        if (_yarnSelectSaveTimer == null)
        {
            _yarnSelectSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            _yarnSelectSaveTimer.Tick += (s, e) =>
            {
                _yarnSelectSaveTimer.Stop();
                SaveYarnSelectSettings();
            };
        }
        else
        {
            _yarnSelectSaveTimer.Stop();
            _yarnSelectSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }

        _yarnSelectSaveTimer.Start();
    }

    private void FlushYarnSelectSave()
    {
        if (_yarnSelectSaveTimer != null && _yarnSelectSaveTimer.IsEnabled)
        {
            _yarnSelectSaveTimer.Stop();
            SaveYarnSelectSettings();
        }
    }

    private void UpdateYanm<T>(T value, Action<YanmSettings> update)
    {
        _settings.Yanm ??= new YanmSettings();
        update(_settings.Yanm);
        OnPropertyChanged();
        OnPropertyChanged(nameof(YanmUsesCustomShortcut));
        OnPropertyChanged(nameof(YanmCustomShortcut));
        OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
        OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
        OnPropertyChanged(nameof(YanmMouseTriggerMode));
        OnPropertyChanged(nameof(YanmMouseTriggerRightDrag));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        QueueYanmSave(200);
    }

    private void QueueYanmSave(int delayMs = 500)
    {
        if (_yanmSaveTimer == null)
        {
            _yanmSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _yanmSaveTimer.Tick += (s, e) => { _yanmSaveTimer.Stop(); SaveYanmSettings(); };
        }
        else
        {
            _yanmSaveTimer.Stop();
            _yanmSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _yanmSaveTimer.Start();
    }

    private void FlushYanmSave()
    {
        if (_yanmSaveTimer != null && _yanmSaveTimer.IsEnabled)
        {
            _yanmSaveTimer.Stop();
            SaveYanmSettings();
        }
    }

    private void SaveYanmSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveYanmSettings();
    }

    private void YanmActivationKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveYanmSettings(requireCustomShortcut: false);
        if (string.Equals(_settings.Yanm?.ActivationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(_settings.Yanm?.CustomShortcut))
        {
            SyncStatusText = "已切换为自定义快捷键，请点击“录制快捷键”完成设置。";
            OnPropertyChanged(nameof(YanmSummary));
        }
    }

    private void SaveYanmSettings(bool requireCustomShortcut = true)
    {
        _settings.Yanm ??= new YanmSettings();
        _settings.Yanm.ActivationKey = YanmActivationKeys.Normalize(_settings.Yanm.ActivationKey);
        _settings.Yanm.MouseTriggerMode = MouseTriggerModes.Normalize(_settings.Yanm.MouseTriggerMode);
        _settings.Yanm.WhitelistedProcesses = ParseProcessList(string.Join(", ", _settings.Yanm.WhitelistedProcesses ?? []));
        _settings.Yanm.BlacklistedProcesses = ParseProcessList(string.Join(", ", _settings.Yanm.BlacklistedProcesses ?? []));
        ApplyMouseTriggerModeToYanmFlags(_settings.Yanm);
        SyncYanmMouseTriggerModeFromFlags(_settings.Yanm);
        if (requireCustomShortcut &&
            string.Equals(_settings.Yanm.ActivationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(_settings.Yanm.CustomShortcut))
        {
            System.Windows.MessageBox.Show(this, "已选择自定义快捷键，请先录制一个快捷键再保存。", "缺少快捷键", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AppSettingsStore.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged("yanm-settings-saved");
        SyncStatusText = $"燕幕设置已保存：{YanmSummary}";
        _yanmStatusHideTimer = ShowSaveStatusTemporarily(_yanmStatusHideTimer, visible => IsYanmSaveStatusVisible = visible);
        OnPropertyChanged(nameof(EnableYanm));
        OnPropertyChanged(nameof(YanmActivationKey));
        OnPropertyChanged(nameof(YanmTriggerHold));
        OnPropertyChanged(nameof(YanmTriggerDoubleTap));
        OnPropertyChanged(nameof(YanmUsesCustomShortcut));
        OnPropertyChanged(nameof(YanmCustomShortcut));
        OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
        OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
        OnPropertyChanged(nameof(YanmMouseTriggerMode));
        OnPropertyChanged(nameof(YanmMouseTriggerRightDrag));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
    }

    private void SaveYarnSelectSettings()
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.YarnSelect.WhitelistedProcesses ??= [];
        _settings.YarnSelect.BlacklistedProcesses ??= [];
        _settings.YarnSelect.Rules = YarnSelectRules
            .Select(item => YarnSelectSettings.NormalizeRule(new YarnSelectRuleSettings
            {
                Enabled = item.Enabled,
                TriggerKey = item.TriggerKey,
                ActionType = item.ActionType,
                ExtensionId = ResolveYarnSelectExtensionId(item),
                Description = item.Description
            }))
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerKey))
            .DistinctBy(static rule => rule.TriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AppSettingsStore.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged("yarnselect-settings-saved");
        SyncStatusText = $"燕选设置已保存：{YarnSelectSummary}";
        _yarnSelectStatusHideTimer = ShowSaveStatusTemporarily(_yarnSelectStatusHideTimer, visible => IsYarnSelectSaveStatusVisible = visible);
        RefreshYarnSelectBindings();
    }

    private string ResolveYarnSelectExtensionId(YarnSelectRuleItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExtensionId) &&
            YarnSelectExtensionOptions.Any(option => option.ExtensionId.Equals(item.ExtensionId, StringComparison.OrdinalIgnoreCase)))
        {
            return item.ExtensionId;
        }

        var searchText = (item.ExtensionSearchText ?? string.Empty).Trim();
        return YarnSelectExtensionOptions.FirstOrDefault(option =>
            option.Title.Equals(searchText, StringComparison.OrdinalIgnoreCase) ||
            option.ExtensionId.Equals(searchText, StringComparison.OrdinalIgnoreCase))
            ?.ExtensionId ?? string.Empty;
    }

    private void RefreshYarnSelectBindings()
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.YarnSelect.Rules ??= [];
        if (_settings.YarnSelect.Rules.Count == 0)
        {
            _settings.YarnSelect.Rules = YarnSelectSettings.CreateDefaultRulesFromLegacy(_settings.YarnSelect);
        }

        RefreshYarnSelectExtensionOptions();
        YarnSelectRules.Clear();
        foreach (var rule in _settings.YarnSelect.Rules.Select(YarnSelectSettings.NormalizeRule))
        {
            var item = new YarnSelectRuleItem(rule);
            item.OnChangedAction = () => { RefreshYarnSelectPreviewMap(); QueueYarnSelectSave(500); };
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }
        RefreshYarnSelectPreviewMap();

        OnPropertyChanged(nameof(EnableYarnSelect));
        OnPropertyChanged(nameof(YarnSelectCopy));
        OnPropertyChanged(nameof(YarnSelectCut));
        OnPropertyChanged(nameof(YarnSelectPaste));
        OnPropertyChanged(nameof(YarnSelectSearch));
        OnPropertyChanged(nameof(YarnSelectRun));
        OnPropertyChanged(nameof(YarnSelectSmartCopyPaste));
        OnPropertyChanged(nameof(YarnSelectSidePaste));
        OnPropertyChanged(nameof(YarnSelectWhitelistedProcessesText));
        OnPropertyChanged(nameof(YarnSelectBlacklistedProcessesText));
        OnPropertyChanged(nameof(YarnSelectSummary));
    }

    private void RefreshYarnSelectExtensionOptions()
    {
        YarnSelectExtensionOptions.Clear();
        RadialMenuExtensionOptions.Clear();
        YarnSelectExtensionOptions.Add(new YarnSelectExtensionOption(string.Empty, "不绑定扩展"));
        foreach (var command in _mainWindow.GetLocalExtensionsForSettings())
        {
            var option = new YarnSelectExtensionOption(command);
            YarnSelectExtensionOptions.Add(option);
        }

        foreach (var command in _mainWindow.GetAllCommands()
                     .Where(IsRadialMenuCommandCandidate)
                     .DistinctBy(static command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static command => command.ItemKindLabel, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static command => command.Title, StringComparer.OrdinalIgnoreCase))
        {
            RadialMenuExtensionOptions.Add(new YarnSelectExtensionOption(command));
        }

        RefreshRadialMenuCommandCandidates(RadialMenuSearchText);
    }

    private void RefreshRadialMenuSlots()
    {
        if (_isRefreshingRadialMenu)
        {
            return;
        }

        try
        {
            _isRefreshingRadialMenu = true;
            RefreshYarnSelectExtensionOptions();
            _settings.RadialMenu ??= new RadialMenuSettings();
            _settings.RadialMenu.Pages ??= [];
            if (_settings.RadialMenu.Pages.Count == 0)
            {
                _settings.RadialMenu.Pages.Add(new RadialMenuPageSettings { Id = "default", Name = "全局" });
            }

            if (_settings.RadialMenu.Pages.All(page => !page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
            }

            RadialMenuPages.Clear();
            var allPages = _settings.RadialMenu.Pages;
            var pageMap = allPages.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var childIdsSet = _settings.RadialMenu.GetChildPageIdsSet();
            var rootPages = allPages.Where(p => !childIdsSet.Contains(p.Id)).ToList();
            if (rootPages.Count == 0 && allPages.Count > 0)
            {
                rootPages = [allPages[0]];
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddPageHierarchy(RadialMenuPageSettings page, int level)
            {
                if (visited.Contains(page.Id)) return;
                visited.Add(page.Id);

                var isAppPage = !string.IsNullOrEmpty(page.ContextProcessName);
                var icon = isAppPage ? GetProcessIcon(page.ContextProcessName!) : null;
                string prefix = level switch
                {
                    0 => "",
                    1 => "└─ ",
                    2 => "   └─ ",
                    3 => "      └─ ",
                    _ => new string(' ', (level - 1) * 3) + "└─ "
                };
                string dispName = prefix + page.Name;

                RadialMenuPages.Add(new RadialMenuPageEditorItem(page.Id, page.Name, icon, isAppPage, level, dispName));

                if (page.ChildPageIds != null)
                {
                    foreach (var childId in page.ChildPageIds)
                    {
                        if (!string.IsNullOrWhiteSpace(childId) && pageMap.TryGetValue(childId, out var childPage))
                        {
                            AddPageHierarchy(childPage, level + 1);
                        }
                    }
                }
            }

            foreach (var root in rootPages)
            {
                AddPageHierarchy(root, 0);
            }

            foreach (var page in allPages)
            {
                if (!visited.Contains(page.Id))
                {
                    AddPageHierarchy(page, 0);
                }
            }

            RadialMenuChildPageOptions.Clear();
            RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(string.Empty, "不进入子环", null, false, 0, "不进入子环"));
            foreach (var item in RadialMenuPages)
            {
                RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(item.Id, item.Name, null, false, item.Level, item.DisplayName));
            }

            var selectedPage = _settings.RadialMenu.Pages.First(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase));
            selectedPage.Slots ??= [];
            selectedPage.SlotTitles ??= [];
            selectedPage.ChildPageIds ??= [];
            while (selectedPage.Slots.Count < RadialMenuSettings.TotalSlotCount) selectedPage.Slots.Add(null);
            while (selectedPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) selectedPage.SlotTitles.Add(null);
            while (selectedPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) selectedPage.ChildPageIds.Add(null);

            RadialMenuSlots.Clear();
            BuildRadialPreviewSeparators();
            var center = 180.0;
            var innerRadius = 78.0;
            var outerRadius = 142.0;
            var runtimeItems = _mainWindow.GetRadialMenuItems(selectedPage.Id);
            for (var index = 0; index < RadialMenuSettings.TotalSlotCount; index++)
            {
                var isOuter = index >= RadialMenuSettings.InnerSlotCount;
                var offset = isOuter ? index - RadialMenuSettings.InnerSlotCount : index;
                var step = isOuter ? 22.5 : 45.0;
                var angle = (-90 + offset * step) * Math.PI / 180.0;
                var radius = isOuter ? outerRadius : innerRadius;
                var startAngleDegrees = isOuter ? -101.25 + offset * step : -112.5 + offset * step;
                var runtimeItem = runtimeItems.ElementAtOrDefault(index);
                var runtimeCommand = runtimeItem?.Command;
                var childPageId = selectedPage.ChildPageIds.ElementAtOrDefault(index) ?? string.Empty;
                var hasChildPage = !string.IsNullOrWhiteSpace(childPageId);
                var extTitle = !string.IsNullOrWhiteSpace(childPageId) && runtimeCommand == null
                    ? string.Empty
                    : ResolveRadialExtensionTitle(
                        selectedPage.Slots.ElementAtOrDefault(index),
                        selectedPage.SlotTitles.ElementAtOrDefault(index));
                RadialMenuSlots.Add(new RadialMenuSlotEditorItem(
                    index,
                    selectedPage.Slots.ElementAtOrDefault(index) ?? string.Empty,
                    selectedPage.SlotTitles.ElementAtOrDefault(index) ?? string.Empty,
                    childPageId,
                    extTitle,
                    ResolveRadialChildPageTitle(childPageId),
                    center + Math.Cos(angle) * radius - (isOuter ? 31 : 38),
                    center + Math.Sin(angle) * radius - (isOuter ? 25 : 30),
                    isOuter,
                    BuildRadialSectorGeometry(center, center, isOuter ? 113.0 : 35.0, isOuter ? 180.0 : 113.0, startAngleDegrees, step),
                    runtimeCommand?.IconSource,
                    runtimeCommand?.VectorIcon,
                    runtimeCommand?.AccentBrush ?? (hasChildPage ? ResolveRadialChildPageAccentBrush() : System.Windows.Media.Brushes.Transparent),
                    runtimeCommand?.DisplayGlyph ?? (hasChildPage ? "›" : string.Empty)));
            }

            OnPropertyChanged(nameof(SelectedRadialMenuPageName));
        }
        finally
        {
            _isRefreshingRadialMenu = false;
            // 直接通过SelectedIndex设置ComboBox选中项，
            // 避免SelectedValue/SelectedValuePath在ItemTemplate场景下不渲染选择框的WPF问题。
            SyncRadialMenuPageComboBoxSelection();
        }
    }

    /// <summary>
    /// 将ComboBox的选中项同步为当前设置中保存的SelectedPageId。
    /// 通过SelectedIndex而非SelectedValue来设置，确保ItemTemplate正确渲染。
    /// </summary>
    private void SyncRadialMenuPageComboBoxSelection()
    {
        if (RadialMenuPageComboBox == null)
        {
            return;
        }

        var targetId = _settings.RadialMenu?.SelectedPageId ?? string.Empty;
        var targetIndex = -1;
        for (var i = 0; i < RadialMenuPages.Count; i++)
        {
            if (string.Equals(RadialMenuPages[i].Id, targetId, StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0 && RadialMenuPages.Count > 0)
        {
            targetIndex = 0;
        }

        _isRefreshingRadialMenu = true;
        try
        {
            RadialMenuPageComboBox.SelectedIndex = targetIndex;
        }
        finally
        {
            _isRefreshingRadialMenu = false;
        }
    }

    /// <summary>
    /// ComboBox选择变化事件处理，替代SelectedValue的TwoWay绑定。
    /// </summary>
    private void RadialMenuPageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isRefreshingRadialMenu)
        {
            return;
        }

        if (RadialMenuPageComboBox.SelectedItem is RadialMenuPageEditorItem selected &&
            !string.IsNullOrWhiteSpace(selected.Id))
        {
            SelectedRadialMenuPageId = selected.Id;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static string? FindExecutablePath(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        var exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
            ? processName 
            : processName + ".exe";

        try
        {
            var nameOnly = Path.GetFileNameWithoutExtension(exeName);
            var processes = System.Diagnostics.Process.GetProcessesByName(nameOnly);
            if (processes.Length > 0)
            {
                var path = processes[0].MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch { }

        string[] registryKeys = [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\App Paths"
        ];
        foreach (var keyPath in registryKeys)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Path.Combine(keyPath, exeName)) 
                             ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Path.Combine(keyPath, exeName));
                if (key != null)
                {
                    var path = key.GetValue("")?.ToString();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
        }

        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var p in pathEnv.Split(';'))
                {
                    var fullPath = Path.Combine(p.Trim(), exeName);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
        }
        catch { }

        string[] systemDirs = [
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        ];
        foreach (var dir in systemDirs)
        {
            try
            {
                var fullPath = Path.Combine(dir, exeName);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch { }
        }

        return null;
    }

    private static ImageSource? GetProcessIcon(string processName)
    {
        try
        {
            var path = FindExecutablePath(processName);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
                    if (bitmapSource.CanFreeze)
                    {
                        bitmapSource.Freeze();
                    }
                    DestroyIcon(icon.Handle);
                    return bitmapSource;
                }
            }
        }
        catch { }
        return null;
    }

    private void BuildRadialPreviewSeparators()
    {
        RadialMenuPreviewSeparators.Clear();
        const double center = 180.0;
        for (var index = 0; index < RadialMenuSettings.InnerSlotCount; index++)
        {
            var angle = (-112.5 + index * 45) * Math.PI / 180.0;
            RadialMenuPreviewSeparators.Add(new RadialSeparatorViewModel(
                center + Math.Cos(angle) * 35.0,
                center + Math.Sin(angle) * 35.0,
                center + Math.Cos(angle) * 113,
                center + Math.Sin(angle) * 113));
        }

        for (var index = 0; index < RadialMenuSettings.OuterSlotCount; index++)
        {
            var angle = (-101.25 + index * 22.5) * Math.PI / 180.0;
            RadialMenuPreviewSeparators.Add(new RadialSeparatorViewModel(
                center + Math.Cos(angle) * 113,
                center + Math.Sin(angle) * 113,
                center + Math.Cos(angle) * 180.0,
                center + Math.Sin(angle) * 180.0));
        }
    }

    private static Geometry BuildRadialSectorGeometry(double centerX, double centerY, double innerRadius, double outerRadius, double startAngleDegrees, double sweepDegrees)
    {
        static System.Windows.Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
        {
            var radians = angleDegrees * Math.PI / 180.0;
            return new System.Windows.Point(
                cx + Math.Cos(radians) * radius,
                cy + Math.Sin(radians) * radius);
        }

        var endAngleDegrees = startAngleDegrees + sweepDegrees;
        var outerStart = PointOnCircle(centerX, centerY, outerRadius, startAngleDegrees);
        var outerEnd = PointOnCircle(centerX, centerY, outerRadius, endAngleDegrees);
        var innerEnd = PointOnCircle(centerX, centerY, innerRadius, endAngleDegrees);
        var innerStart = PointOnCircle(centerX, centerY, innerRadius, startAngleDegrees);
        var isLargeArc = sweepDegrees >= 180.0;

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment(outerEnd, new System.Windows.Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new System.Windows.Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private void SaveRadialMenuSlots()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        if (RadialMenuSlots.Count == 0)
        {
            HostAssets.AppendLog("Settings: skipped saving radial slots because the radial editor has not loaded any slot items.");
            return;
        }

        var selectedPage = _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase));
        if (selectedPage == null)
        {
            return;
        }

        selectedPage.Slots = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item.ExtensionId.Trim())
            .Cast<string?>()
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
        selectedPage.SlotTitles = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.DisplayTitle) ? null : item.DisplayTitle.Trim())
            .Cast<string?>()
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
        selectedPage.ChildPageIds = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.ChildPageId) ? null : item.ChildPageId.Trim())
            .Cast<string?>()
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
        while (selectedPage.Slots.Count < RadialMenuSettings.TotalSlotCount)
        {
            selectedPage.Slots.Add(null);
        }

        while (selectedPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount)
        {
            selectedPage.SlotTitles.Add(null);
        }

        while (selectedPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount)
        {
            selectedPage.ChildPageIds.Add(null);
        }
        var firstPageSlots = _settings.RadialMenu.Pages[0].Slots ?? [];
        _settings.RadialMenu.Slots = firstPageSlots
            .Concat(Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount))
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
    }

    private static bool IsRadialMenuCommandCandidate(CommandItem command)
    {
        return command.Source is CommandSource.LocalExtension or CommandSource.WebSearch or CommandSource.Application or CommandSource.Local;
    }

    private void RefreshRadialMenuCommandCandidates(string? keyword)
    {
        FilteredRadialMenuCommandOptions.Clear();
        keyword = (keyword ?? string.Empty).Trim();
        var candidates = RadialMenuExtensionOptions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            candidates = candidates.Where(option =>
                option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var option in candidates.Take(40))
        {
            FilteredRadialMenuCommandOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var fileResults = EverythingSearchService.Search(keyword, 20);
            if (fileResults.Success)
            {
                foreach (var result in fileResults.Results)
                {
                    var command = BuildRadialFileCommand(result);
                    if (FilteredRadialMenuCommandOptions.Any(option =>
                            option.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    FilteredRadialMenuCommandOptions.Add(new YarnSelectExtensionOption(command));
                }
            }
        }
    }

    private void SelectRadialMenuSlot(RadialMenuSlotEditorItem slot)
    {
        _selectedRadialMenuSlot = slot;
        OnPropertyChanged(nameof(RadialMenuSelectedSlotSummary));
    }

    private static string FormatRadialTraceValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    }

    private static string GetRadialTraceListValue(IReadOnlyList<string?>? values, int index)
    {
        if (values == null || index < 0 || index >= values.Count)
        {
            return string.Empty;
        }

        return values[index] ?? string.Empty;
    }

    private static string FirstRadialTraceValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string DescribeRadialTraceSlot(RadialMenuSlotEditorItem? slot)
    {
        if (slot == null)
        {
            return "(slot missing)";
        }

        return $"index={slot.Index + 1}, ext={FormatRadialTraceValue(slot.ExtensionId)}, title={FormatRadialTraceValue(slot.DisplayTitle)}, child={FormatRadialTraceValue(slot.ChildPageId)}, childTitle={FormatRadialTraceValue(slot.ChildPageTitle)}";
    }

    private static string DescribeRadialTracePageSlot(RadialMenuPageSettings? page, int index)
    {
        if (page == null)
        {
            return "(page missing)";
        }

        return $"pageId={FormatRadialTraceValue(page.Id)}, pageName={FormatRadialTraceValue(page.Name)}, slot={index + 1}, ext={FormatRadialTraceValue(GetRadialTraceListValue(page.Slots, index))}, title={FormatRadialTraceValue(GetRadialTraceListValue(page.SlotTitles, index))}, child={FormatRadialTraceValue(GetRadialTraceListValue(page.ChildPageIds, index))}";
    }

    private static HashSet<string> CollectRadialChildPageTreeIds(RadialMenuSettings radial, string rootPageId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootPageId) || radial.Pages == null)
        {
            return result;
        }

        var stack = new Stack<string>();
        stack.Push(rootPageId.Trim());
        while (stack.Count > 0)
        {
            var pageId = stack.Pop();
            if (!result.Add(pageId))
            {
                continue;
            }

            var page = radial.Pages.FirstOrDefault(item => item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
            if (page?.ChildPageIds == null)
            {
                continue;
            }

            foreach (var childPageId in page.ChildPageIds)
            {
                if (!string.IsNullOrWhiteSpace(childPageId))
                {
                    stack.Push(childPageId.Trim());
                }
            }
        }

        return result;
    }

    private RadialMenuSlotEditorItem? ResolveRadialSlotFromMenuSender(object sender)
    {
        DependencyObject? current = sender as DependencyObject;
        while (current != null)
        {
            if (current is ContextMenu { PlacementTarget: FrameworkElement { DataContext: RadialMenuSlotEditorItem slot } })
            {
                return slot;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        if (sender is FrameworkElement { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: RadialMenuSlotEditorItem fallbackSlot } } })
        {
            return fallbackSlot;
        }

        HostAssets.AppendLog($"Settings radial slot resolve fallback: sender={sender?.GetType().Name ?? "(null)"}, fallback={DescribeRadialTraceSlot(_selectedRadialMenuSlot)}.");
        return _selectedRadialMenuSlot;
    }

    private void ApplyRadialMenuCommandToSlot(RadialMenuSlotEditorItem slot, YarnSelectExtensionOption option)
    {
        if (string.IsNullOrWhiteSpace(option.ExtensionId))
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        slot.ExtensionId = option.ExtensionId;
        slot.DisplayTitle = string.Empty;
        slot.ExtensionTitle = option.Title;
        UpdateRadialSlotPresentation(slot);
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private static CommandItem BuildRadialFileCommand(EverythingSearchResult result)
    {
        var subtitle = string.IsNullOrWhiteSpace(result.SizeText)
            ? result.DirectoryPath
            : $"{result.DirectoryPath}   ·   {result.SizeText}";
        return new CommandItem(
            glyph: result.IsFolder ? "夹" : "文",
            title: result.Name,
            subtitle: subtitle,
            category: result.IsFolder ? "文件夹" : "文件",
            accentHex: result.IsFolder ? "#FF3B82F6" : "#FF4B5563",
            openTarget: result.FullPath,
            keywords: [result.FullPath, result.DirectoryPath, result.Name],
            source: CommandSource.File,
            extensionId: $"result::{result.FullPath}",
            resultKind: result.IsFolder ? ResultItemKind.Folder : ResultItemKind.File,
            resultProviderTitle: "Everything 文件",
            iconSourceOverride: NativeFileIconService.GetIcon(result.FullPath, result.IsFolder));
    }

    private void AddRadialMenuPageButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"页面 {_settings.RadialMenu.Pages.Count + 1}"
        };
        _settings.RadialMenu.Pages.Add(page);
        _settings.RadialMenu.SelectedPageId = page.Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
    }

    private void DeleteRadialMenuPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.RadialMenu.Pages.Count <= 1)
        {
            return;
        }

        var removePageId = ResolveSelectedRadialMenuPageIdFromEditor();
        var currentPage = _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(removePageId, StringComparison.OrdinalIgnoreCase));
        if (currentPage == null)
        {
            return;
        }

        var pageName = currentPage?.Name ?? "当前轮盘";
        var result = System.Windows.MessageBox.Show(
            $"确定要删除轮盘“{pageName}”吗？\n删除后该轮盘配置将无法恢复。",
            "确认删除轮盘",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var removedId = removePageId;
        var parentPageId = _settings.RadialMenu.Pages.FirstOrDefault(page =>
            page.ChildPageIds?.Any(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase)) == true)?.Id;
        _settings.RadialMenu.Pages.RemoveAll(page => page.Id.Equals(removedId, StringComparison.OrdinalIgnoreCase));
        foreach (var page in _settings.RadialMenu.Pages)
        {
            page.ChildPageIds = (page.ChildPageIds ?? [])
                .Select(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase) ? null : id)
                .ToList();
        }

        var remainingChildIds = _settings.RadialMenu.GetChildPageIdsSet();
        var fallbackPage =
            (!string.IsNullOrWhiteSpace(parentPageId)
                ? _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(parentPageId, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? _settings.RadialMenu.Pages.FirstOrDefault(page =>
                string.IsNullOrWhiteSpace(page.ContextProcessName) && !remainingChildIds.Contains(page.Id))
            ?? _settings.RadialMenu.Pages.FirstOrDefault(page => !remainingChildIds.Contains(page.Id))
            ?? _settings.RadialMenu.Pages.FirstOrDefault();
        if (fallbackPage == null)
        {
            return;
        }

        _settings.RadialMenu.SelectedPageId = fallbackPage.Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
    }

    private string ResolveSelectedRadialMenuPageIdFromEditor()
    {
        if (RadialMenuPageComboBox?.SelectedItem is RadialMenuPageEditorItem selected &&
            !string.IsNullOrWhiteSpace(selected.Id))
        {
            return selected.Id;
        }

        return SelectedRadialMenuPageId;
    }

    private void RadialMenuCenter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        RenameCurrentRadialMenuPage();
        e.Handled = true;
    }

    private void RadialMenuCenter_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void RenameRadialMenuPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RenameCurrentRadialMenuPage();
    }

    private void RenameCurrentRadialMenuPage()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var pageId = SelectedRadialMenuPageId;
        var page = _settings.RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            HostAssets.AppendLog($"Radial rename skipped: selected page not found, pageId={pageId}.");
            return;
        }

        var oldName = page.Name;
        var dialog = new SimpleTextInputWindow("重命名轮盘", "输入新的轮盘名称。", page.Name)
        {
            Owner = this
        };
        bool accepted;
        try
        {
            _isRenamingRadialMenuPage = true;
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            _isRenamingRadialMenuPage = false;
        }

        if (!accepted)
        {
            HostAssets.AppendLog($"Radial rename cancelled: pageId={pageId}, oldName={oldName}.");
            return;
        }

        var trimmedName = dialog.ValueText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            HostAssets.AppendLog($"Radial rename ignored empty name: pageId={pageId}, oldName={oldName}.");
            return;
        }

        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        page = _settings.RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            HostAssets.AppendLog($"Radial rename failed after dialog: page missing, pageId={pageId}, newName={trimmedName}.");
            return;
        }

        page.Name = trimmedName;
        HostAssets.AppendLog($"Radial rename saving: pageId={pageId}, oldName={oldName}, newName={trimmedName}.");
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
        OnPropertyChanged(nameof(SelectedRadialMenuPageName));
        var saved = AppSettingsStore.Load().RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty;
        HostAssets.AppendLog($"Radial rename saved: pageId={pageId}, savedName={saved}, currentName={SelectedRadialMenuPageName}.");
    }

    private void RadialExtensionDragStart_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: YarnSelectExtensionOption option } ||
            string.IsNullOrWhiteSpace(option.ExtensionId))
        {
            return;
        }

        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, option.ExtensionId, System.Windows.DragDropEffects.Copy);
    }

    private void RadialSlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            SelectRadialMenuSlot(slot);
            if (slot.HasChildPageTitle && !string.IsNullOrWhiteSpace(slot.ChildPageId))
            {
                SelectedRadialMenuPageId = slot.ChildPageId;
                e.Handled = true;
                return;
            }

            if (slot.IsEmpty)
            {
                OpenRadialSlotPicker(slot);
                e.Handled = true;
                return;
            }

            var command = ResolveRadialSlotCommand(slot);
            if (command != null)
            {
                _mainWindow.ExecuteCommandExternally(command, launchSource: "settings-radial-preview");
                e.Handled = true;
            }
        }
    }

    private void RadialSlot_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            SelectRadialMenuSlot(slot);
        }
    }

    private void RadialSlot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void RadialSlot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RadialMenuSlotEditorItem slot } ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat))
        {
            return;
        }

        slot.ExtensionId = e.Data.GetData(System.Windows.DataFormats.StringFormat) as string ?? string.Empty;
        slot.DisplayTitle = string.Empty;
        slot.ExtensionTitle = ResolveRadialExtensionTitle(slot.ExtensionId);
        UpdateRadialSlotPresentation(slot);
        e.Handled = true;
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void RadialSlot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            slot.IsHovered = true;
            SelectRadialMenuSlot(slot);
        }
    }

    private void RadialSlot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            slot.IsHovered = false;
        }
    }

    private void RadialSlotAddCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        OpenRadialSlotPicker(slot);
    }

    private void RadialSlotSetSimulatedKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        var initialShortcut = slot.ExtensionId.StartsWith(RadialSimulatedKeyPrefix, StringComparison.OrdinalIgnoreCase)
            ? slot.ExtensionId[RadialSimulatedKeyPrefix.Length..]
            : string.Empty;
        var dialog = new HotkeyCaptureWindow(
            "模拟按键",
            "录制要在此槽位执行的组合键，并设置轮盘里显示的名称。",
            initialShortcut,
            slot.DisplayTitle,
            allowEmpty: false,
            allowDoubleTap: false,
            allowModifierless: true)
        {
            Owner = this
        };
        bool? dialogResult;
        _suspendActivationRefresh = true;
        try
        {
            dialogResult = dialog.ShowDialog() == true;
        }
        finally
        {
            _suspendActivationRefresh = false;
        }

        if (dialogResult != true)
        {
            return;
        }

        var shortcut = dialog.ShortcutText.Trim();
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return;
        }

        var currentSlot = RadialMenuSlots.FirstOrDefault(item => item.Index == slot.Index) ?? slot;
        SelectRadialMenuSlot(currentSlot);
        currentSlot.ExtensionId = $"{RadialSimulatedKeyPrefix}{shortcut}";
        currentSlot.DisplayTitle = string.IsNullOrWhiteSpace(dialog.DisplayNameText)
            ? shortcut
            : dialog.DisplayNameText.Trim();
        currentSlot.ExtensionTitle = ResolveRadialExtensionTitle(currentSlot.ExtensionId, currentSlot.DisplayTitle);
        HostAssets.AppendLog($"Radial simulated key assigned: slot={slot.Index + 1}, shortcut={shortcut}, displayTitle={currentSlot.DisplayTitle}, page={SelectedRadialMenuPageId}.");
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void RadialSlotClearCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        DeleteRadialSlotContent(slot);
    }

    private void RadialSlotEnterChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot != null && slot.HasChildPageTitle && !string.IsNullOrWhiteSpace(slot.ChildPageId))
        {
            SelectedRadialMenuPageId = slot.ChildPageId;
        }
    }

    private void RadialSlotAddChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        CreateRadialChildPageForSlot(slot, GetNextRadialChildPageName(), SelectedRadialMenuPageId);
    }

    private void CreateRadialChildPageForSlot(RadialMenuSlotEditorItem slot, string pageName, string? parentPageId = null)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var effectiveParentPageId = string.IsNullOrWhiteSpace(parentPageId)
            ? SelectedRadialMenuPageId
            : parentPageId.Trim();
        var parentPage = _settings.RadialMenu.Pages.FirstOrDefault(page =>
            page.Id.Equals(effectiveParentPageId, StringComparison.OrdinalIgnoreCase));
        if (parentPage == null)
        {
            HostAssets.AppendLog($"Settings radial child page add skipped: parent page missing, parent={effectiveParentPageId}, slot={slot.Index + 1}.");
            return;
        }

        parentPage.Slots ??= [];
        parentPage.SlotTitles ??= [];
        parentPage.ChildPageIds ??= [];
        while (parentPage.Slots.Count < RadialMenuSettings.TotalSlotCount) parentPage.Slots.Add(null);
        while (parentPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) parentPage.SlotTitles.Add(null);
        while (parentPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) parentPage.ChildPageIds.Add(null);
        if (!string.IsNullOrWhiteSpace(parentPage.ChildPageIds[slot.Index]))
        {
            HostAssets.AppendLog($"Settings radial child page add skipped: slot already has child, parent={effectiveParentPageId}, slot={slot.Index + 1}.");
            return;
        }

        var childPageName = pageName.Trim();
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = childPageName
        };
        _settings.RadialMenu.Pages.Add(page);
        parentPage.ChildPageIds[slot.Index] = page.Id;
        _settings.RadialMenu.SelectedPageId = effectiveParentPageId;

        var currentSlot = RadialMenuSlots.FirstOrDefault(item => item.Index == slot.Index) ?? slot;
        currentSlot.ChildPageId = page.Id;
        currentSlot.ChildPageTitle = childPageName;
        UpdateRadialSlotPresentation(currentSlot);
        
        SaveRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private async void OpenRadialSlotPicker(RadialMenuSlotEditorItem slot)
    {
        var parentPageId = SelectedRadialMenuPageId;
        _suspendActivationRefresh = true;
        try
        {
            var result = await _mainWindow.ShowForRadialPickerAsync(!slot.HasChildPageTitle);
            if (result == null)
            {
                return;
            }

            if (result.Action == RadialSlotPickerWindow.PickerAction.AddChildPage)
            {
                CreateRadialChildPageForSlot(slot, GetNextRadialChildPageName(), parentPageId);
                return;
            }

            if (result.Command == null)
            {
                return;
            }

            ApplyRadialMenuCommandToSlot(slot, new YarnSelectExtensionOption(result.Command));
        }
        finally
        {
            _suspendActivationRefresh = false;
        }
    }

    private CommandItem? ResolveRadialSlotCommand(RadialMenuSlotEditorItem slot)
    {
        return _mainWindow
            .GetRadialMenuItems(SelectedRadialMenuPageId)
            .ElementAtOrDefault(slot.Index)?
            .Command;
    }

    private string GetNextRadialChildPageName()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var usedNumbers = _settings.RadialMenu.Pages
            .Select(page => page.Name ?? string.Empty)
            .Select(name =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(name, @"^子环\s*(\d+)$");
                return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : (int?)null;
            })
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToHashSet();
        var next = 1;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        return $"子环 {next}";
    }

    private void UpdateRadialSlotPresentation(RadialMenuSlotEditorItem slot)
    {
        var command = ResolveRadialSlotCommand(slot);
        if (command != null)
        {
            slot.IconSource = command.IconSource;
            slot.VectorIcon = command.VectorIcon;
            slot.AccentBrush = command.AccentBrush;
            slot.DisplayGlyph = command.DisplayGlyph;
            slot.ExtensionTitle = command.Title;
            return;
        }

        if (slot.HasChildPageTitle)
        {
            slot.IconSource = null;
            slot.VectorIcon = null;
            slot.AccentBrush = ResolveRadialChildPageAccentBrush();
            slot.DisplayGlyph = "›";
            slot.ExtensionTitle = string.Empty;
            return;
        }

        slot.IconSource = null;
        slot.VectorIcon = null;
        slot.AccentBrush = System.Windows.Media.Brushes.Transparent;
        slot.DisplayGlyph = string.Empty;
    }

    private void RadialSlotClearChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        DeleteRadialSlotContent(slot);
    }

    private void RadialSlotDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        var comboPage = RadialMenuPageComboBox?.SelectedItem as RadialMenuPageEditorItem;
        HostAssets.AppendLog($"Settings radial delete menu clicked: comboPage={FormatRadialTraceValue(comboPage?.Id)}, selectedPage={FormatRadialTraceValue(_settings.RadialMenu?.SelectedPageId)}, resolvedSlot={DescribeRadialTraceSlot(slot)}.");
        if (slot == null)
        {
            return;
        }

        DeleteRadialSlotContent(slot);
    }

    private void DeleteRadialSlotContent(RadialMenuSlotEditorItem slot)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];

        var editorPageId = ResolveSelectedRadialMenuPageIdFromEditor();
        var currentSlot = RadialMenuSlots.FirstOrDefault(item => item.Index == slot.Index) ?? slot;
        var slotIndex = currentSlot.Index;
        var currentPage = string.IsNullOrWhiteSpace(editorPageId)
            ? null
            : _settings.RadialMenu.Pages.FirstOrDefault(page =>
                page.Id.Equals(editorPageId, StringComparison.OrdinalIgnoreCase));

        HostAssets.AppendLog($"Settings radial delete requested: editorPage={FormatRadialTraceValue(editorPageId)}, settingsSelectedPage={FormatRadialTraceValue(_settings.RadialMenu.SelectedPageId)}, slotCount={RadialMenuSlots.Count}, pageCount={_settings.RadialMenu.Pages.Count}, sourceSlot={DescribeRadialTraceSlot(slot)}, currentSlot={DescribeRadialTraceSlot(currentSlot)}, pageSlotBefore={DescribeRadialTracePageSlot(currentPage, slotIndex)}.");

        if (string.IsNullOrWhiteSpace(editorPageId))
        {
            HostAssets.AppendLog("Settings radial delete skipped: editor page id is empty.");
            return;
        }

        if (slotIndex < 0 || slotIndex >= RadialMenuSettings.TotalSlotCount)
        {
            HostAssets.AppendLog($"Settings radial delete skipped: slot index out of range, slot={slotIndex}.");
            return;
        }

        if (currentPage == null)
        {
            HostAssets.AppendLog($"Settings radial delete skipped: page not found, editorPage={editorPageId}.");
            return;
        }

        _settings.RadialMenu.SelectedPageId = editorPageId;
        currentPage.Slots ??= [];
        currentPage.SlotTitles ??= [];
        currentPage.ChildPageIds ??= [];
        while (currentPage.Slots.Count < RadialMenuSettings.TotalSlotCount) currentPage.Slots.Add(null);
        while (currentPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) currentPage.SlotTitles.Add(null);
        while (currentPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) currentPage.ChildPageIds.Add(null);

        var pageExtensionId = GetRadialTraceListValue(currentPage.Slots, slotIndex);
        var pageSlotTitle = GetRadialTraceListValue(currentPage.SlotTitles, slotIndex);
        var pageChildPageId = GetRadialTraceListValue(currentPage.ChildPageIds, slotIndex);
        var removedExtensionId = FirstRadialTraceValue(currentSlot.ExtensionId, pageExtensionId);
        var removedChildPageId = FirstRadialTraceValue(currentSlot.ChildPageId, pageChildPageId);
        var removedDisplayTitle = FirstRadialTraceValue(currentSlot.DisplayTitle, pageSlotTitle);
        var hasCommand = !string.IsNullOrWhiteSpace(removedExtensionId) || !string.IsNullOrWhiteSpace(removedDisplayTitle);
        var hasChildPage = !string.IsNullOrWhiteSpace(removedChildPageId);
        if (!hasCommand && !hasChildPage)
        {
            HostAssets.AppendLog($"Settings radial delete skipped: slot is empty after checking editor and settings, pageSlot={DescribeRadialTracePageSlot(currentPage, slotIndex)}.");
            return;
        }

        var message = hasChildPage
            ? "确定要删除该槽位吗？\n这会同时删除当前槽位里的子环。"
            : "确定要删除该槽位吗？";
        var result = System.Windows.MessageBox.Show(
            message,
            "确认删除",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            HostAssets.AppendLog($"Settings radial delete cancelled: editorPage={editorPageId}, slot={slotIndex + 1}, child={FormatRadialTraceValue(removedChildPageId)}, ext={FormatRadialTraceValue(removedExtensionId)}.");
            return;
        }

        var removedChildPageIds = hasChildPage
            ? CollectRadialChildPageTreeIds(_settings.RadialMenu, removedChildPageId)
            : [];
        var pagesBefore = _settings.RadialMenu.Pages.Count;
        HostAssets.AppendLog($"Settings radial delete applying: editorPage={editorPageId}, slot={slotIndex + 1}, ext={FormatRadialTraceValue(removedExtensionId)}, child={FormatRadialTraceValue(removedChildPageId)}, childTreeCount={removedChildPageIds.Count}, pageSlotBefore={DescribeRadialTracePageSlot(currentPage, slotIndex)}.");

        SelectRadialMenuSlot(currentSlot);
        var slotsToClear = RadialMenuSlots.Where(item => item.Index == slotIndex).ToList();
        if (!slotsToClear.Any(item => ReferenceEquals(item, slot)))
        {
            slotsToClear.Add(slot);
        }

        foreach (var editorSlot in slotsToClear.Distinct())
        {
            editorSlot.ExtensionId = string.Empty;
            editorSlot.DisplayTitle = string.Empty;
            editorSlot.ChildPageId = string.Empty;
            editorSlot.ChildPageTitle = string.Empty;
            editorSlot.ExtensionTitle = string.Empty;
            UpdateRadialSlotPresentation(editorSlot);
        }

        currentPage.Slots[slotIndex] = null;
        currentPage.SlotTitles[slotIndex] = null;
        currentPage.ChildPageIds[slotIndex] = null;

        var removedPageCount = 0;
        if (removedChildPageIds.Count > 0)
        {
            removedPageCount = _settings.RadialMenu.Pages.RemoveAll(page => removedChildPageIds.Contains(page.Id));
            foreach (var page in _settings.RadialMenu.Pages)
            {
                if (page.ChildPageIds == null)
                {
                    continue;
                }

                for (int i = 0; i < page.ChildPageIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(page.ChildPageIds[i]) && removedChildPageIds.Contains(page.ChildPageIds[i]!))
                    {
                        page.ChildPageIds[i] = null;
                    }
                }
            }
        }

        _settings.RadialMenu.SelectedPageId = editorPageId;
        SaveQuickPanelTriggerSettings();
        var savedSettings = AppSettingsStore.Load();
        var savedRadial = savedSettings.RadialMenu ?? new RadialMenuSettings();
        var savedPage = savedRadial.Pages.FirstOrDefault(page =>
            page.Id.Equals(editorPageId, StringComparison.OrdinalIgnoreCase));
        var savedChildPageId = GetRadialTraceListValue(savedPage?.ChildPageIds, slotIndex);
        var deletedChildStillExists = removedChildPageIds.Count > 0 &&
            savedRadial.Pages.Any(page => removedChildPageIds.Contains(page.Id));
        HostAssets.AppendLog($"Settings radial delete saved: editorPage={editorPageId}, slot={slotIndex + 1}, pagesBefore={pagesBefore}, pagesAfter={savedRadial.Pages.Count}, removedPageCount={removedPageCount}, savedChild={FormatRadialTraceValue(savedChildPageId)}, deletedChildStillExists={deletedChildStillExists}, savedPageSlot={DescribeRadialTracePageSlot(savedPage, slotIndex)}.");
        if (!string.IsNullOrWhiteSpace(savedChildPageId) || deletedChildStillExists)
        {
            HostAssets.AppendLog($"Settings radial delete failed verification: editorPage={editorPageId}, slot={slotIndex + 1}, expectedChildCleared={FormatRadialTraceValue(removedChildPageId)}, savedChild={FormatRadialTraceValue(savedChildPageId)}, deletedChildStillExists={deletedChildStillExists}.");
        }

        RefreshRadialMenuSlots();
    }

    private void RadialMenuSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Down || FilteredRadialMenuCommandOptions.Count == 0)
        {
            return;
        }

        if (FindSiblingListBox(sender as DependencyObject) is { } listBox)
        {
            listBox.SelectedIndex = 0;
            listBox.Focus();
            e.Handled = true;
        }
    }

    private void RadialMenuCommandListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitRadialMenuCommandCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RadialMenuSearchText = string.Empty;
            e.Handled = true;
        }
    }

    private void RadialMenuCommandListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject dep)
        {
            var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(dep);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta / 3.0));
                e.Handled = true;
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject dep) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void RadialMenuCommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitRadialMenuCommandCandidate(listBox);
        }
    }

    private void CommitRadialMenuCommandCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (_selectedRadialMenuSlot == null ||
            listBox.SelectedItem is not YarnSelectExtensionOption option)
        {
            return;
        }

        ApplyRadialMenuCommandToSlot(_selectedRadialMenuSlot, option);
    }

    private string ResolveRadialExtensionTitle(string? extensionId, string? displayTitleOverride = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return "拖入扩展";
        }

        if (extensionId.StartsWith(RadialSimulatedKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(displayTitleOverride)
                ? extensionId[RadialSimulatedKeyPrefix.Length..]
                : displayTitleOverride.Trim();
        }

        if (extensionId.StartsWith("result::", StringComparison.OrdinalIgnoreCase))
        {
            var path = extensionId["result::".Length..];
            var title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(title) ? path : title;
        }

        return RadialMenuExtensionOptions.FirstOrDefault(option =>
            option.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase))?.Title ?? "未知扩展";
    }


    private string ResolveRadialChildPageTitle(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return string.Empty;
        }

        var name = _settings.RadialMenu?.Pages?.FirstOrDefault(page =>
            page.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name;
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }

    private static System.Windows.Media.Brush ResolveRadialChildPageAccentBrush()
    {
        return System.Windows.Application.Current.TryFindResource("BrushRadialChildAccentSector") as System.Windows.Media.Brush
               ?? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!;
    }

    public Dictionary<string, YarnSelectPreviewKeyItem> YarnSelectPreviewKeyMap { get; } = InitYarnSelectPreviewKeyMap();

    private static Dictionary<string, YarnSelectPreviewKeyItem> InitYarnSelectPreviewKeyMap()
    {
        var keys = new (string key, string name)[]
        {
            ("Right", "右键"), ("X1", "侧键1"), ("X2", "侧键2"),
            ("1", "1"), ("2", "2"), ("3", "3"), ("4", "4"), ("5", "5"),
            ("6", "6"), ("7", "7"), ("8", "8"), ("9", "9"), ("0", "0"),
            ("Q", "Q"), ("W", "W"), ("E", "E"), ("R", "R"), ("T", "T"),
            ("Y", "Y"), ("U", "U"), ("I", "I"), ("O", "O"), ("P", "P"),
            ("A", "A"), ("S", "S"), ("D", "D"), ("F", "F"), ("G", "G"),
            ("H", "H"), ("J", "J"), ("K", "K"), ("L", "L"),
            ("Z", "Z"), ("X", "X"), ("C", "C"), ("V", "V"), ("B", "B"),
            ("N", "N"), ("M", "M")
        };

        var dict = new Dictionary<string, YarnSelectPreviewKeyItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in keys)
        {
            dict[key] = new YarnSelectPreviewKeyItem
            {
                KeyCode = key,
                DisplayName = name,
                RuleSummary = $"【触发键: {name}】\n状态: 未配置\n💡 点击可在右侧/下方快速创建以此键触发的新规则"
            };
        }

        return dict;
    }

    public void RefreshYarnSelectPreviewMap()
    {
        var ruleDict = YarnSelectRules.ToDictionary(
            rule => YarnSelectSettings.NormalizeTriggerKey(rule.TriggerKey),
            rule => rule,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (key, previewItem) in YarnSelectPreviewKeyMap)
        {
            if (ruleDict.TryGetValue(key, out var rule))
            {
                previewItem.IsConfigured = true;
                previewItem.RuleEnabled = rule.Enabled;
                var actionLabel = GetYarnSelectActionLabel(rule.ActionType);
                var descStr = string.IsNullOrWhiteSpace(rule.Description) ? "" : $" ({rule.Description})";
                previewItem.RuleSummary = $"【触发键: {previewItem.DisplayName}】\n状态: {(rule.Enabled ? "🟢 已启用" : "⚪ 已禁用")}\n动作: {actionLabel}{descStr}\n💡 点击可自动高亮定位此规则";
            }
            else
            {
                previewItem.IsConfigured = false;
                previewItem.RuleEnabled = false;
                previewItem.RuleSummary = $"【触发键: {previewItem.DisplayName}】\n状态: 未配置\n💡 点击可在右侧/下方快速创建以此键触发的新规则";
            }
        }
    }

    private void YarnSelectPreviewKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rawKey })
        {
            var normalized = YarnSelectSettings.NormalizeTriggerKey(rawKey);
            var existing = YarnSelectRules.FirstOrDefault(rule =>
                YarnSelectSettings.NormalizeTriggerKey(rule.TriggerKey).Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var newRule = new YarnSelectRuleItem(new YarnSelectRuleSettings
                {
                    Enabled = true,
                    TriggerKey = normalized,
                    ActionType = YarnSelectActionTypes.Copy,
                    Description = $"{normalized} 触发动作"
                });
                newRule.OnChangedAction = () => { RefreshYarnSelectPreviewMap(); QueueYarnSelectSave(500); };
                ApplyYarnSelectExtensionSelection(newRule);
                YarnSelectRules.Add(newRule);
                SaveYarnSelectSettings();
                existing = newRule;
            }

            foreach (var rule in YarnSelectRules)
            {
                rule.IsHighlighted = (rule == existing);
            }

            RefreshYarnSelectPreviewMap();
        }
    }

    private void AddYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var item = new YarnSelectRuleItem(new YarnSelectRuleSettings
        {
            TriggerKey = "A",
            ActionType = YarnSelectActionTypes.RunExtension,
            Description = "新燕选规则"
        });
        item.OnChangedAction = () => { RefreshYarnSelectPreviewMap(); QueueYarnSelectSave(500); };
        ApplyYarnSelectExtensionSelection(item);
        YarnSelectRules.Add(item);
        OnPropertyChanged(nameof(YarnSelectSummary));
        RefreshYarnSelectPreviewMap();
        QueueYarnSelectSave(200);
    }

    private void DeleteYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            return;
        }

        YarnSelectRules.Remove(item);
        SaveYarnSelectSettings();
        RefreshYarnSelectPreviewMap();
    }

    private void YarnSelectKeyPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            foreach (var rule in YarnSelectRules)
            {
                if (rule != item)
                {
                    rule.IsKeyPickerOpen = false;
                }
            }

            item.IsKeyPickerOpen = !item.IsKeyPickerOpen;
        }
    }

    private void YarnSelectKeyOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item, Tag: string key })
        {
            item.SelectTriggerKey(key);
            SaveYarnSelectSettings();
        }
    }

    private void CloseYarnSelectKeyPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            item.IsKeyPickerOpen = false;
        }
    }

    private void KeyPickerPopup_Opened(object sender, EventArgs e)
    {
        if (sender is Popup { Child: FrameworkElement element })
        {
            element.Focus();
        }
    }

    private void YarnSelectKeyPickerPopup_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            string? key = null;
            if (e.Key >= System.Windows.Input.Key.A && e.Key <= System.Windows.Input.Key.Z)
            {
                key = e.Key.ToString();
            }
            else if (e.Key >= System.Windows.Input.Key.D0 && e.Key <= System.Windows.Input.Key.D9)
            {
                key = ((char)('0' + (e.Key - System.Windows.Input.Key.D0))).ToString();
            }
            else if (e.Key >= System.Windows.Input.Key.NumPad0 && e.Key <= System.Windows.Input.Key.NumPad9)
            {
                key = ((char)('0' + (e.Key - System.Windows.Input.Key.NumPad0))).ToString();
            }

            if (!string.IsNullOrEmpty(key))
            {
                item.SelectTriggerKey(key);
                SaveYarnSelectSettings();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                item.IsKeyPickerOpen = false;
                e.Handled = true;
            }
        }
    }

    private void ResetYarnSelectRulesButton_Click(object sender, RoutedEventArgs e)
    {
        YarnSelectRules.Clear();
        foreach (var rule in YarnSelectSettings.CreateDefaultRulesFromLegacy(new YarnSelectSettings()))
        {
            var item = new YarnSelectRuleItem(rule);
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }

        SaveYarnSelectSettings();
    }

    private static string GetYarnSelectActionLabel(string actionType)
    {
        return YarnSelectActionTypes.Normalize(actionType) switch
        {
            YarnSelectActionTypes.Cut => "剪切",
            YarnSelectActionTypes.Paste => "粘贴",
            YarnSelectActionTypes.Search => "搜索",
            YarnSelectActionTypes.Run => "运行",
            YarnSelectActionTypes.SmartCopyPaste => "智能复制/粘贴",
            YarnSelectActionTypes.RunExtension => "运行扩展",
            _ => "复制"
        };
    }

    private void ApplyYarnSelectExtensionSelection(YarnSelectRuleItem item)
    {
        var selected = YarnSelectExtensionOptions.FirstOrDefault(option =>
            option.ExtensionId.Equals(item.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        item.ExtensionSearchText = selected?.Title ?? string.Empty;
        item.FilteredExtensionOptions = [];
    }

    private void RefreshYarnSelectExtensionCandidates(YarnSelectRuleItem item, string keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var candidates = string.IsNullOrEmpty(keyword)
            ? YarnSelectExtensionOptions.Take(12)
            : YarnSelectExtensionOptions
                .Where(option =>
                    option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(12);

        item.FilteredExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>(candidates);
    }

    private void YarnSelectExtensionDropdownToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is YarnSelectRuleItem item)
        {
            e.Handled = true;
            RefreshYarnSelectExtensionCandidates(item, string.Empty);
            item.IsExtensionPickerOpen = true;

            if (fe.Parent is Grid grid && grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is { } textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }
    }

    private void YarnSelectExtensionSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.SelectAll();
            }

            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
            item.IsExtensionPickerOpen = true;
        }
    }

    private void YarnSelectExtensionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            var selected = YarnSelectExtensionOptions.FirstOrDefault(option =>
                option.ExtensionId.Equals(item.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (selected == null ||
                !selected.Title.Equals(item.ExtensionSearchText ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                item.ExtensionId = string.Empty;
            }

            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
            item.IsExtensionPickerOpen = true;
        }
    }

    private void YarnSelectExtensionSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not DependencyObject source ||
            source is not FrameworkElement { DataContext: YarnSelectRuleItem item } ||
            e.Key != Key.Down ||
            item.FilteredExtensionOptions.Count == 0)
        {
            return;
        }

        var listBox = FindYarnSelectExtensionListBox(source);
        if (listBox != null)
        {
            listBox.SelectedIndex = 0;
            var itemContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            itemContainer?.Focus();
            listBox.Focus();
            e.Handled = true;
        }
    }

    private static System.Windows.Controls.ListBox? FindYarnSelectExtensionListBox(DependencyObject source)
    {
        var parent = VisualTreeHelper.GetParent(source);
        while (parent != null)
        {
            if (parent is Grid grid)
            {
                var popup = grid.Children.OfType<System.Windows.Controls.Primitives.Popup>().FirstOrDefault();
                if (popup?.Child is Border border && border.Child is System.Windows.Controls.ListBox listBox)
                {
                    return listBox;
                }
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void YarnSelectExtensionListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitYarnSelectExtensionCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && listBox.DataContext is YarnSelectRuleItem item)
        {
            item.IsExtensionPickerOpen = false;
            e.Handled = true;
        }
    }

    private void YarnSelectExtensionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitYarnSelectExtensionCandidate(listBox);
        }
    }

    private void CommitYarnSelectExtensionCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.DataContext is not YarnSelectRuleItem item ||
            listBox.SelectedItem is not YarnSelectExtensionOption option)
        {
            return;
        }

        item.ExtensionId = option.ExtensionId;
        item.ExtensionSearchText = option.Title;
        item.IsExtensionPickerOpen = false;
    }

    private static System.Windows.Controls.ListBox? FindSiblingListBox(DependencyObject? source)
    {
        var parent = source == null ? null : VisualTreeHelper.GetParent(source);
        while (parent != null)
        {
            if (parent is StackPanel panel)
            {
                return panel.Children.OfType<System.Windows.Controls.ListBox>().FirstOrDefault();
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static System.Windows.Controls.ListBox? FindDescendantListBox(DependencyObject source, object itemsSource)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is System.Windows.Controls.ListBox listBox &&
                ReferenceEquals(listBox.ItemsSource, itemsSource))
            {
                return listBox;
            }

            var nested = FindDescendantListBox(child, itemsSource);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RefreshQuickPanelTriggerBindings()
    {
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        OnPropertyChanged(nameof(TriggerMiddleButtonDown));
        OnPropertyChanged(nameof(TriggerX1ButtonDown));
        OnPropertyChanged(nameof(TriggerX2ButtonDown));
        OnPropertyChanged(nameof(TriggerCtrlLeftClick));
        OnPropertyChanged(nameof(TriggerCtrlRightClick));
        OnPropertyChanged(nameof(TriggerMiddleButtonLongPress));
        OnPropertyChanged(nameof(TriggerRightButtonLongPress));
        OnPropertyChanged(nameof(TriggerRightButtonDrag));
        OnPropertyChanged(nameof(TriggerHorizontalWheel));

        OnPropertyChanged(nameof(ExecuteOnButtonRelease));
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerMode));
        OnPropertyChanged(nameof(MouseGestureManagementSummary));
        OnPropertyChanged(nameof(EnableRadialMenu));
        OnPropertyChanged(nameof(EnableRadialCapsLockHold));
        OnPropertyChanged(nameof(RadialActivationKey));
        OnPropertyChanged(nameof(RadialUsesCustomShortcut));
        OnPropertyChanged(nameof(RadialCustomShortcut));
        OnPropertyChanged(nameof(RadialWhitelistedProcessesText));
        OnPropertyChanged(nameof(RadialBlacklistedProcessesText));
        OnPropertyChanged(nameof(RadialAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(RadialMenuSummary));
        RefreshRadialMenuSlots();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private void ExternalLink_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"无法打开链接: {ex.Message}", "出错啦", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenSyncProviderLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"无法打开链接: {ex.Message}", "出错啦", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenPersonalSyncCommitButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"无法打开提交链接: {ex.Message}", "出错啦", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void TogglePersonalSyncCommitDiff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is PersonalSyncCommitItem item)
        {
            if (item.IsExpanded)
            {
                item.IsExpanded = false;
                return;
            }

            item.IsExpanded = true;
            if (string.IsNullOrWhiteSpace(item.DiffText))
            {
                item.DiffText = "正在从云端拉取具体变更差异 (Diff)...";
                try
                {
                    var diff = await _mainWindow.GetPersonalSyncCommitDiffAsync(item.Sha);
                    item.DiffText = string.IsNullOrWhiteSpace(diff) ? "未检测到文件变动或无法读取具体变更差异。" : diff;
                }
                catch (Exception ex)
                {
                    item.DiffText = $"拉取差异失败：{ex.Message}";
                }
            }
        }
    }

    private async void VisitPersonalSyncRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        // 弹出等待提示或者直接拉取，由于是通过 API 获取用户名拼 URL，我们传递 _personalSyncSettings 和 _personalSyncSecrets
        var url = await _mainWindow.GetPersonalSyncRepositoryWebUrlAsync(_personalSyncSettings, _personalSyncSecrets);
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"无法打开链接: {ex.Message}", "出错啦", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            System.Windows.MessageBox.Show(this, "当前同步方式无法获取有效的云端链接。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TogglePersonalSyncAdvancedOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPersonalSyncAdvancedOptions = !ShowPersonalSyncAdvancedOptions;
    }

    private void SyncSubTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string tabKey } && !string.IsNullOrWhiteSpace(tabKey))
        {
            SyncActiveSubTab = tabKey;
            if (tabKey == "history")
            {
                RefreshSyncActivityLog();
                _ = RefreshPersonalSyncCommitsAsync();
                _ = RefreshPersonalConfigRestorePointsAsync();
            }
        }
    }

    private void ToggleAccountSyncObjectsButton_Click(object sender, RoutedEventArgs e)
    {
        IsAccountSyncObjectsExpanded = !IsAccountSyncObjectsExpanded;
    }

    private void CopySyncLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SyncActivityLogText))
            {
                System.Windows.Clipboard.SetText(SyncActivityLogText);
                HostAssets.AppendLog("SettingsWindow: SyncActivityLog copied to clipboard.");
                System.Windows.MessageBox.Show(this, "同步日志已成功复制到剪贴板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(this, "暂无日志可复制。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SettingsWindow: CopySyncLog failed: {ex.Message}");
        }
    }
}

public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true 
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)) 
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 42, 42));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed record SettingsNavigationItem(string Key, string IconReference, string Title, string Accent)
{
    public Geometry? IconGeometry => ExtensionIconLibrary.ResolveVectorIcon(IconReference);
}

public sealed record SyncProviderOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record AutoSyncDelayOption(int Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record MouseGestureTemplateDefinition(string Sequence, string Name, string Description);

public sealed class SettingsMouseGestureItem
{
    public SettingsMouseGestureItem(
        string extensionId,
        string title,
        string category,
        string triggerLabel,
        string sequence,
        string displayName,
        int[]? data,
        int minDistance,
        int? tolerance,
        ImageSource? iconSource,
        Geometry? vectorIcon,
        System.Windows.Media.Brush accentBrush,
        string displayGlyph)
    {
        ExtensionId = extensionId;
        Title = title;
        Category = category;
        TriggerLabel = triggerLabel;
        Sequence = sequence;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? MouseGestureNaming.GetDisplayName(sequence) : displayName;
        MinDistance = minDistance;
        Tolerance = tolerance;
        IconSource = iconSource;
        VectorIcon = vectorIcon;
        AccentBrush = accentBrush;
        DisplayGlyph = displayGlyph;
        PreviewGeometry = MouseGesturePreviewGeometryFactory.Create(sequence, data);
    }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Category { get; }

    public string TriggerLabel { get; }

    public string Sequence { get; }

    public string DisplayName { get; }

    public int MinDistance { get; }

    public int? Tolerance { get; }

    public string DetailText
    {
        get
        {
            var sequenceText = string.IsNullOrWhiteSpace(Sequence) ? "已录制图形" : $"序列 {Sequence}";
            return $"{sequenceText} · 最小距离 {MinDistance}px" + (Tolerance is > 0 ? $" · 容差 {Tolerance}" : string.Empty);
        }
    }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public Geometry PreviewGeometry { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);
}

public sealed class MouseGestureQuickBindItem : INotifyPropertyChanged
{
    private MouseGestureExtensionOption? _selectedExtension;
    private MouseGestureAppOption? _selectedApp;
    private string _extensionSearchText = string.Empty;
    private string _appSearchText = string.Empty;
    private ObservableCollection<MouseGestureExtensionOption> _filteredExtensionOptions = [];
    private ObservableCollection<MouseGestureAppOption> _filteredAppOptions = [];
    private ICollectionView? _filteredAppOptionsView;
    private bool _isExtensionPopupOpen;
    private bool _isAppPopupOpen;

    public MouseGestureQuickBindItem(string sequence, string displayName, string description, string? assignedTitle)
    {
        Sequence = sequence;
        DisplayName = displayName;
        Description = description;
        AssignedTitle = assignedTitle ?? string.Empty;
        PreviewGeometry = MouseGesturePreviewGeometryFactory.Create(sequence, data: null);
        RebuildFilteredAppOptionsView();
    }

    public string Sequence { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string AssignedTitle { get; }

    public Geometry PreviewGeometry { get; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssignedTitle);

    public string StatusText => IsAssigned ? $"已被 {AssignedTitle} 使用" : "未绑定";

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExtensionSearchText)));
        }
    }

    public string AppSearchText
    {
        get => _appSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _appSearchText)
            {
                return;
            }

            _appSearchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppSearchText)));
        }
    }

    public ObservableCollection<MouseGestureExtensionOption> FilteredExtensionOptions
    {
        get => _filteredExtensionOptions;
        set
        {
            if (ReferenceEquals(value, _filteredExtensionOptions))
            {
                return;
            }

            _filteredExtensionOptions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredExtensionOptions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredExtensionListVisibility)));
        }
    }

    public ObservableCollection<MouseGestureAppOption> FilteredAppOptions
    {
        get => _filteredAppOptions;
        set
        {
            if (ReferenceEquals(value, _filteredAppOptions))
            {
                return;
            }

            _filteredAppOptions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredAppOptions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredAppListVisibility)));
            RebuildFilteredAppOptionsView();
        }
    }

    public ICollectionView? FilteredAppOptionsView
    {
        get => _filteredAppOptionsView;
        private set
        {
            if (ReferenceEquals(value, _filteredAppOptionsView))
            {
                return;
            }

            _filteredAppOptionsView = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredAppOptionsView)));
        }
    }

    public Visibility FilteredExtensionListVisibility => FilteredExtensionOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FilteredAppListVisibility => FilteredAppOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool IsExtensionPopupOpen
    {
        get => _isExtensionPopupOpen;
        set
        {
            if (value == _isExtensionPopupOpen)
            {
                return;
            }

            _isExtensionPopupOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExtensionPopupOpen)));
        }
    }

    public bool IsAppPopupOpen
    {
        get => _isAppPopupOpen;
        set
        {
            if (value == _isAppPopupOpen)
            {
                return;
            }

            _isAppPopupOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAppPopupOpen)));
        }
    }

    public MouseGestureAppOption? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (Equals(value, _selectedApp)) return;
            _selectedApp = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedApp)));
        }
    }

public MouseGestureExtensionOption? SelectedExtension
    {
        get => _selectedExtension;
        set
        {
            if (Equals(value, _selectedExtension))
            {
                return;
            }

            _selectedExtension = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedExtension)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RebuildFilteredAppOptionsView()
    {
        var view = CollectionViewSource.GetDefaultView(_filteredAppOptions);
        if (view is ListCollectionView listView)
        {
            listView.GroupDescriptions.Clear();
            listView.SortDescriptions.Clear();
            listView.SortDescriptions.Add(new SortDescription(nameof(MouseGestureAppOption.IsRunning), ListSortDirection.Descending));
            listView.SortDescriptions.Add(new SortDescription(nameof(MouseGestureAppOption.AppName), ListSortDirection.Ascending));
            listView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MouseGestureAppOption.GroupTitle)));
        }

        FilteredAppOptionsView = view;
    }
}

public sealed class MouseGestureAppOption
{
    public MouseGestureAppOption(string appName, string appPath, string category, bool isRunning)
    {
        AppName = appName;
        AppPath = appPath;
        Category = category;
        IsRunning = isRunning;
        DisplayLabel = isRunning ? $"🟢 [运行中] {appName}" : $"💻 {appName}";
    }

    public string AppName { get; }
    public string AppPath { get; }
    public string Category { get; }
    public bool IsRunning { get; }
    public string DisplayLabel { get; }
    public string GroupTitle => IsRunning ? "运行中的应用" : "全部应用";

    public override string ToString() => DisplayLabel;
}

public sealed class MouseGestureExtensionOption
{
    public MouseGestureExtensionOption(CommandItem command)
    {
        ExtensionId = command.ExtensionId;
        Label = command.Title;
        Category = command.Category;
        IconSource = command.IconSource;
        VectorIcon = command.VectorIcon;
        AccentBrush = command.AccentBrush;
        DisplayGlyph = command.DisplayGlyph;
    }

    public string ExtensionId { get; }

    public string Label { get; }

    public string Category { get; }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public override string ToString() => Label;
}

internal static class MouseGesturePreviewGeometryFactory
{
    public static Geometry Create(string? sequence, int[]? data)
    {
        var points = MouseGestureTemplateRecognizer.HasTemplateData(data)
            ? DecodeTemplateData(data!)
            : BuildSequencePoints(MouseGestureNaming.NormalizeSequence(sequence));
        return BuildGeometry(ScalePoints(points, 52, 8));
    }

    private static List<WpfPoint> DecodeTemplateData(int[] data)
    {
        var points = new List<WpfPoint>(data.Length / 2);
        for (var index = 0; index + 1 < data.Length; index += 2)
        {
            points.Add(new WpfPoint(data[index], data[index + 1]));
        }

        return points;
    }

    private static List<WpfPoint> BuildSequencePoints(string sequence)
    {
        var points = new List<WpfPoint> { new(0, 0) };
        var current = new WpfPoint(0, 0);
        foreach (var ch in sequence)
        {
            var delta = ch switch
            {
                '↑' => new WpfVector(0, -1),
                '↗' => new WpfVector(1, -1),
                '→' => new WpfVector(1, 0),
                '↘' => new WpfVector(1, 1),
                '↓' => new WpfVector(0, 1),
                '↙' => new WpfVector(-1, 1),
                '←' => new WpfVector(-1, 0),
                '↖' => new WpfVector(-1, -1),
                _ => new WpfVector(0, 0)
            };
            current += delta;
            points.Add(current);
        }

        return points.Count > 1 ? points : [new WpfPoint(0, 0), new WpfPoint(1, 0)];
    }

    private static List<WpfPoint> ScalePoints(IReadOnlyList<WpfPoint> points, double size, double padding)
    {
        var minX = points.Min(static point => point.X);
        var maxX = points.Max(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxY = points.Max(static point => point.Y);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var scale = Math.Min((size - (padding * 2)) / width, (size - (padding * 2)) / height);
        var actualWidth = width * scale;
        var actualHeight = height * scale;
        var offsetX = padding + ((size - (padding * 2) - actualWidth) / 2);
        var offsetY = padding + ((size - (padding * 2) - actualHeight) / 2);
        return points
            .Select(point => new WpfPoint(
                offsetX + ((point.X - minX) * scale),
                offsetY + ((point.Y - minY) * scale)))
            .ToList();
    }

    private static Geometry BuildGeometry(IReadOnlyList<WpfPoint> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }
}

public class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(
            nameof(SourceText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightTextProperty =
        DependencyProperty.Register(
            nameof(HighlightText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBrush),
            typeof(System.Windows.Media.Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(System.Windows.Media.Brushes.DeepSkyBlue, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightBackgroundBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBackgroundBrush),
            typeof(System.Windows.Media.Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(System.Windows.Media.Brushes.Transparent, OnHighlightPropertyChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string HighlightText
    {
        get => (string)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    public System.Windows.Media.Brush HighlightBrush
    {
        get => (System.Windows.Media.Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public System.Windows.Media.Brush HighlightBackgroundBrush
    {
        get => (System.Windows.Media.Brush)GetValue(HighlightBackgroundBrushProperty);
        set => SetValue(HighlightBackgroundBrushProperty, value);
    }

    private static void OnHighlightPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightedTextBlock textBlock)
        {
            textBlock.RebuildInlines();
        }
    }

    private void RebuildInlines()
    {
        Inlines.Clear();

        var text = SourceText ?? string.Empty;
        var keyword = (HighlightText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (keyword.Length == 0)
        {
            Inlines.Add(new Run(text) { Foreground = Foreground });
            return;
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                Inlines.Add(new Run(text[startIndex..]) { Foreground = Foreground });
                break;
            }

            if (matchIndex > startIndex)
            {
                Inlines.Add(new Run(text[startIndex..matchIndex]) { Foreground = Foreground });
            }

            Inlines.Add(new Run(text.Substring(matchIndex, keyword.Length))
            {
                Foreground = HighlightBrush,
                Background = HighlightBackgroundBrush,
                FontWeight = FontWeights.SemiBold
            });

            startIndex = matchIndex + keyword.Length;
        }
    }
}

public sealed record SettingsShortcutItem(string ExtensionId, string Title, string Category, string? Shortcut)
{
    public string ShortcutValue => Shortcut ?? string.Empty;

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? "未设置" : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);
}

public sealed record YarnSelectActionTypeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record YanmActivationKeyOption(string Value, string Label);

public sealed record MouseTriggerOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record YarnSelectExtensionOption(
    string ExtensionId,
    string Title,
    string Detail,
    ImageSource? IconSource,
    Geometry? VectorIcon,
    System.Windows.Media.Brush AccentBrush,
    string DisplayGlyph)
{
    public YarnSelectExtensionOption(CommandItem command)
        : this(
            command.ExtensionId,
            command.Title,
            string.IsNullOrWhiteSpace(command.OpenTarget)
                ? command.ItemKindLabel
                : $"{command.ItemKindLabel} · {command.OpenTarget}",
            command.IconSource,
            command.VectorIcon,
            command.AccentBrush,
            command.DisplayGlyph)
    {
    }

    public YarnSelectExtensionOption(string extensionId, string title)
        : this(extensionId, title, string.Empty, null, null, System.Windows.Media.Brushes.Transparent, string.Empty)
    {
    }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public override string ToString() => Title;
}

public sealed class RadialMenuSlotEditorItem : INotifyPropertyChanged
{
    private string _extensionId;
    private string _displayTitle;
    private string _childPageId;
    private string _extensionTitle;
    private string _childPageTitle;
    private bool _isHovered;
    private ImageSource? _iconSource;
    private Geometry? _vectorIcon;
    private System.Windows.Media.Brush _accentBrush;
    private string _displayGlyph;

    public RadialMenuSlotEditorItem(int index, string extensionId, string displayTitle, string childPageId, string extensionTitle, string childPageTitle, double x, double y, bool isOuter, Geometry sectorGeometry, ImageSource? iconSource, Geometry? vectorIcon, System.Windows.Media.Brush accentBrush, string displayGlyph)
    {
        Index = index;
        _extensionId = extensionId;
        _displayTitle = displayTitle;
        _childPageId = childPageId;
        _extensionTitle = extensionTitle;
        _childPageTitle = childPageTitle;
        X = x;
        Y = y;
        IsOuter = isOuter;
        SectorGeometry = sectorGeometry;
        _iconSource = iconSource;
        _vectorIcon = vectorIcon;
        _accentBrush = accentBrush;
        _displayGlyph = displayGlyph;
    }

    public int Index { get; }

    public string Label => (Index + 1).ToString(CultureInfo.InvariantCulture);

    public double X { get; }

    public double Y { get; }

    public bool IsOuter { get; }

    public Geometry SectorGeometry { get; }

    public double SlotWidth => IsOuter ? 62 : 76;

    public double SlotHeight => IsOuter ? 50 : 60;

    public double TitleWidth => IsOuter ? 50 : 60;

    public double IconSize => IsOuter ? 23 : 32;

    public double IconContainerSize => IsOuter ? 23 : 32;

    public CornerRadius IconCornerRadius => IsOuter ? new CornerRadius(6) : new CornerRadius(8);

    public double VectorIconSize => IsOuter ? 14 : 19;

    public double GlyphFontSize => IsOuter ? 11 : 13;

    public double PlusFontSize => IsOuter ? 20 : 24;

    public Thickness SlotPadding => IsOuter ? new Thickness(4, 2, 4, 0) : new Thickness(6, 4, 6, 0);

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (Equals(value, _iconSource))
            {
                return;
            }

            _iconSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImageIcon));
            OnPropertyChanged(nameof(HasPresentationIcon));
        }
    }

    public Geometry? VectorIcon
    {
        get => _vectorIcon;
        set
        {
            if (Equals(value, _vectorIcon))
            {
                return;
            }

            _vectorIcon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVectorIcon));
            OnPropertyChanged(nameof(HasPresentationIcon));
        }
    }

    public System.Windows.Media.Brush AccentBrush
    {
        get => _accentBrush;
        set
        {
            if (Equals(value, _accentBrush))
            {
                return;
            }

            _accentBrush = value;
            OnPropertyChanged();
        }
    }

    public string DisplayGlyph
    {
        get => _displayGlyph;
        set
        {
            value ??= string.Empty;
            if (value == _displayGlyph)
            {
                return;
            }

            _displayGlyph = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseGlyphIcon));
            OnPropertyChanged(nameof(HasPresentationIcon));
        }
    }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public bool HasPresentationIcon => HasImageIcon || HasVectorIcon || UseGlyphIcon;

    public bool IsEmpty => string.IsNullOrWhiteSpace(_extensionId) && string.IsNullOrWhiteSpace(_childPageId);

    public bool IsNotEmpty => !IsEmpty;

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (value == _isHovered)
            {
                return;
            }

            _isHovered = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionId
    {
        get => _extensionId;
        set
        {
            value ??= string.Empty;
            if (value == _extensionId)
            {
                return;
            }

            _extensionId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsNotEmpty));
        }
    }

    public string DisplayTitle
    {
        get => _displayTitle;
        set
        {
            value ??= string.Empty;
            if (value == _displayTitle)
            {
                return;
            }

            _displayTitle = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionTitle
    {
        get => _extensionTitle;
        set
        {
            value ??= string.Empty;
            if (value == _extensionTitle)
            {
                return;
            }

            _extensionTitle = value;
            OnPropertyChanged();
        }
    }

    public string ChildPageId
    {
        get => _childPageId;
        set
        {
            value ??= string.Empty;
            if (value == _childPageId)
            {
                return;
            }

            _childPageId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsNotEmpty));
        }
    }

    public string ChildPageTitle
    {
        get => _childPageTitle;
        set
        {
            value ??= string.Empty;
            if (value == _childPageTitle)
            {
                return;
            }

            _childPageTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChildPageTitle));
        }
    }

    public bool HasChildPageTitle => !string.IsNullOrWhiteSpace(_childPageTitle);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}

public sealed record RadialMenuPageEditorItem(string Id, string Name, ImageSource? Icon = null, bool IsAppPage = false, int Level = 0, string DisplayName = "")
{
    public System.Windows.Thickness IndentMargin => new(Level * 14, 0, 0, 0);
}

public sealed class YarnSelectPreviewKeyItem : INotifyPropertyChanged
{
    private bool _isConfigured;
    private bool _ruleEnabled;
    private string _ruleSummary = string.Empty;

    public string KeyCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public bool IsConfigured
    {
        get => _isConfigured;
        set
        {
            if (_isConfigured == value) return;
            _isConfigured = value;
            NotifyStyleProperties();
        }
    }

    public bool RuleEnabled
    {
        get => _ruleEnabled;
        set
        {
            if (_ruleEnabled == value) return;
            _ruleEnabled = value;
            NotifyStyleProperties();
        }
    }

    public string RuleSummary
    {
        get => _ruleSummary;
        set
        {
            if (_ruleSummary == value) return;
            _ruleSummary = value;
            OnPropertyChanged();
        }
    }

    public System.Windows.Media.Brush BackgroundBrush
    {
        get
        {
            if (!IsConfigured)
            {
                return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushSecondaryBtnBG"];
            }
            return RuleEnabled
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15803D"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151"));
        }
    }

    public System.Windows.Media.Brush BorderBrush
    {
        get
        {
            if (!IsConfigured)
            {
                return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushBorder"];
            }
            return RuleEnabled
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B7280"));
        }
    }

    public System.Windows.Media.Brush TextBrush
    {
        get
        {
            if (IsConfigured)
            {
                return System.Windows.Media.Brushes.White;
            }
            return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushTextMain"];
        }
    }

    public bool HasGreenDot => IsConfigured && RuleEnabled;

    private void NotifyStyleProperties()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(RuleEnabled));
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(HasGreenDot));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class YarnSelectRuleItem : INotifyPropertyChanged
{
    private bool _enabled;
    private string _triggerKey;
    private string _actionType;
    private string _extensionId;
    private string _extensionSearchText;
    private string _description;
    private bool _isKeyPickerOpen;
    private bool _isHighlighted;
    private ObservableCollection<YarnSelectExtensionOption> _filteredExtensionOptions = [];

    public Action? OnChangedAction { get; set; }

    public YarnSelectRuleItem(YarnSelectRuleSettings rule)
    {
        _enabled = rule.Enabled;
        _triggerKey = rule.TriggerKey;
        _actionType = rule.ActionType;
        _extensionId = rule.ExtensionId;
        _extensionSearchText = string.Empty;
        _description = rule.Description;
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (value == _isHighlighted)
            {
                return;
            }

            _isHighlighted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ItemBorderBrush));
            OnPropertyChanged(nameof(ItemBackgroundBrush));
        }
    }

    public System.Windows.Media.Brush ItemBorderBrush => IsHighlighted
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"))
        : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushBorder"];

    public System.Windows.Media.Brush ItemBackgroundBrush => IsHighlighted
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B"))
        : System.Windows.Media.Brushes.Transparent;

    public bool IsKeyPickerOpen
    {
        get => _isKeyPickerOpen;
        set
        {
            if (value == _isKeyPickerOpen)
            {
                return;
            }

            _isKeyPickerOpen = value;
            OnPropertyChanged();
        }
    }

    public void SelectTriggerKey(string key)
    {
        TriggerKey = key;
        IsKeyPickerOpen = false;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public string TriggerKey
    {
        get => _triggerKey;
        set
        {
            value = YarnSelectSettings.NormalizeTriggerKey(value);
            if (value == _triggerKey)
            {
                return;
            }

            _triggerKey = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public bool IsRunExtension => YarnSelectActionTypes.Normalize(ActionType) == YarnSelectActionTypes.RunExtension;

    public string ActionType
    {
        get => _actionType;
        set
        {
            value = YarnSelectActionTypes.Normalize(value);
            if (value == _actionType)
            {
                return;
            }

            _actionType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRunExtension));
            OnChangedAction?.Invoke();
        }
    }

    public string ExtensionId
    {
        get => _extensionId;
        set
        {
            value ??= string.Empty;
            if (value == _extensionId)
            {
                return;
            }

            _extensionId = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            OnPropertyChanged();
        }
    }

    private bool _isExtensionPickerOpen;

    public bool IsExtensionPickerOpen
    {
        get => _isExtensionPickerOpen;
        set
        {
            if (value == _isExtensionPickerOpen)
            {
                return;
            }

            _isExtensionPickerOpen = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<YarnSelectExtensionOption> FilteredExtensionOptions
    {
        get => _filteredExtensionOptions;
        set
        {
            if (ReferenceEquals(value, _filteredExtensionOptions))
            {
                return;
            }

            _filteredExtensionOptions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredExtensionListVisibility));
            IsExtensionPickerOpen = value.Count > 0;
        }
    }

    public Visibility FilteredExtensionListVisibility => FilteredExtensionOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string Description
    {
        get => _description;
        set
        {
            value ??= string.Empty;
            if (value == _description)
            {
                return;
            }

            _description = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsExtensionItem : INotifyPropertyChanged
{
    private bool _isPublished;
    private string _publisherName;
    private bool _isPublishing;
    private bool _isUnpublishing;
    private string _shortcut;
    private string _startupMode;
    private string _startupSchedule;
    private bool _isSelected;

    public SettingsExtensionItem(
        string extensionId,
        string title,
        string description,
        string category,
        string version,
        string directoryPath,
        string sourceLabel,
        bool canOpenDirectory,
        bool isEnabled,
        bool isPublished,
        string publisherName,
        string shortcut,
        ImageSource? iconSource,
        Geometry? vectorIcon,
        System.Windows.Media.Brush accentBrush,
        string displayGlyph,
        string startupMode,
        string startupSchedule)
    {
        ExtensionId = extensionId;
        Title = title;
        Description = description;
        Category = category;
        Version = version;
        DirectoryPath = directoryPath;
        SourceLabel = sourceLabel;
        CanOpenDirectory = canOpenDirectory;
        IsEnabled = isEnabled;
        _isPublished = isPublished;
        _publisherName = publisherName;
        _shortcut = shortcut ?? string.Empty;
        IconSource = iconSource;
        VectorIcon = vectorIcon;
        AccentBrush = accentBrush;
        DisplayGlyph = displayGlyph;
        _startupMode = startupMode ?? string.Empty;
        _startupSchedule = startupSchedule ?? string.Empty;
    }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Description { get; }

    public string Category { get; }

    public string Version { get; }

    public string DirectoryPath { get; }

    public string SourceLabel { get; }

    public bool CanOpenDirectory { get; }

    public bool IsEnabled { get; }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public string Shortcut
    {
        get => _shortcut;
        set
        {
            if (_shortcut == value)
            {
                return;
            }

            _shortcut = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutLabel));
            OnPropertyChanged(nameof(HasShortcut));
            OnPropertyChanged(nameof(ShortcutBadgeVisibility));
            OnPropertyChanged(nameof(ShortcutDetailLabel));
        }
    }

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? string.Empty : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);

    public Visibility ShortcutBadgeVisibility => HasShortcut ? Visibility.Visible : Visibility.Collapsed;

    public string ShortcutDetailLabel => HasShortcut ? Shortcut : "未设置";

    public string StartupMode
    {
        get => _startupMode;
        set
        {
            value ??= string.Empty;
            if (_startupMode == value)
            {
                return;
            }

            _startupMode = value;
            NotifyStartupStateChanged();
        }
    }

    public string StartupSchedule
    {
        get => _startupSchedule;
        set
        {
            value ??= string.Empty;
            if (_startupSchedule == value)
            {
                return;
            }

            _startupSchedule = value;
            NotifyStartupStateChanged();
        }
    }

    public bool HasAppLaunchStartup => StartupMode.Equals("on_app_launch", StringComparison.OrdinalIgnoreCase);

    public bool HasScheduleStartup => !string.IsNullOrWhiteSpace(StartupSchedule);

    public string StartupActionLabel => HasAppLaunchStartup ? "关闭自启" : "开机自启";

    public string StartupDetailLabel => HasAppLaunchStartup ? "已启用" : "未启用";

    public string ScheduleDetailLabel => HasScheduleStartup ? ScheduleConfigWindow.CronToFriendly(StartupSchedule) : "未设置";

    public Visibility AutoStartBadgeVisibility => HasAppLaunchStartup ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ScheduleBadgeVisibility => HasScheduleStartup ? Visibility.Visible : Visibility.Collapsed;

    public string EnabledStateLabel => IsEnabled ? "已启用" : "已禁用";

    public string PublishedStateLabel => IsPublishedInStore ? "已发布到商店" : "仅本地";

    public string DescriptionOrFallback => string.IsNullOrWhiteSpace(Description) ? "这个扩展没有提供额外说明。" : Description;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsPublished
    {
        get => _isPublished;
        set
        {
            if (_isPublished == value)
            {
                return;
            }

            _isPublished = value;
            NotifyPublishStateChanged();
        }
    }

    public string PublisherName
    {
        get => _publisherName;
        set
        {
            if (string.Equals(_publisherName, value, StringComparison.Ordinal))
            {
                return;
            }

            _publisherName = value;
            NotifyPublishStateChanged();
        }
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        set
        {
            if (_isPublishing == value)
            {
                return;
            }

            _isPublishing = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsUnpublishing
    {
        get => _isUnpublishing;
        set
        {
            if (_isUnpublishing == value)
            {
                return;
            }

            _isUnpublishing = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsPublishedInStore => IsPublished && !string.IsNullOrWhiteSpace(PublisherName);

    public bool IsOperationBusy => IsPublishing || IsUnpublishing;

    public string PublishActionLabel => IsPublishedInStore ? "更新商店版本" : "发布到商店";

    public string PublishButtonText => IsPublishing
        ? (IsPublishedInStore ? "更新中..." : "发布中...")
        : PublishActionLabel;

    public string UnpublishButtonText => IsUnpublishing ? "下线中..." : "下线";

    public Visibility PublishSpinnerVisibility => IsPublishing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PublishIconVisibility => IsPublishing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility UnpublishSpinnerVisibility => IsUnpublishing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnpublishIconVisibility => IsUnpublishing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PublishNewButtonVisibility => IsPublishedInStore ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PublishUpdateButtonVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StoreLinkButtonVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnpublishButtonVisibility => CanUnpublish ? Visibility.Visible : Visibility.Collapsed;

    public string PublisherLabel => string.IsNullOrWhiteSpace(PublisherName) ? "未发布" : $"发布者：{PublisherName}";

    public bool CanUnpublish => IsPublishedInStore;

    public bool PublishButtonEnabled => !IsOperationBusy;

    public bool UnpublishButtonEnabled => CanUnpublish && !IsOperationBusy;

    public bool EditButtonEnabled => !IsOperationBusy;

    public bool DeleteButtonEnabled => !IsOperationBusy;

    public bool OpenDirectoryButtonEnabled => CanOpenDirectory && !IsOperationBusy;

    public Visibility PublishedBadgeVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DisabledBadgeVisibility => !IsEnabled ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyPublishStateChanged()
    {
        OnPropertyChanged(nameof(IsPublished));
        OnPropertyChanged(nameof(PublisherName));
        OnPropertyChanged(nameof(IsPublishedInStore));
        OnPropertyChanged(nameof(PublishActionLabel));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(PublisherLabel));
        OnPropertyChanged(nameof(CanUnpublish));
        OnPropertyChanged(nameof(PublishNewButtonVisibility));
        OnPropertyChanged(nameof(PublishUpdateButtonVisibility));
        OnPropertyChanged(nameof(StoreLinkButtonVisibility));
        OnPropertyChanged(nameof(UnpublishButtonVisibility));
        OnPropertyChanged(nameof(UnpublishButtonEnabled));
        OnPropertyChanged(nameof(PublishedBadgeVisibility));
        OnPropertyChanged(nameof(PublishedStateLabel));
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsPublishing));
        OnPropertyChanged(nameof(IsUnpublishing));
        OnPropertyChanged(nameof(IsOperationBusy));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(UnpublishButtonText));
        OnPropertyChanged(nameof(PublishSpinnerVisibility));
        OnPropertyChanged(nameof(PublishIconVisibility));
        OnPropertyChanged(nameof(UnpublishSpinnerVisibility));
        OnPropertyChanged(nameof(UnpublishIconVisibility));
        OnPropertyChanged(nameof(PublishButtonEnabled));
        OnPropertyChanged(nameof(UnpublishButtonEnabled));
        OnPropertyChanged(nameof(EditButtonEnabled));
        OnPropertyChanged(nameof(DeleteButtonEnabled));
        OnPropertyChanged(nameof(OpenDirectoryButtonEnabled));
    }

    private void NotifyStartupStateChanged()
    {
        OnPropertyChanged(nameof(StartupMode));
        OnPropertyChanged(nameof(StartupSchedule));
        OnPropertyChanged(nameof(HasAppLaunchStartup));
        OnPropertyChanged(nameof(HasScheduleStartup));
        OnPropertyChanged(nameof(StartupActionLabel));
        OnPropertyChanged(nameof(StartupDetailLabel));
        OnPropertyChanged(nameof(ScheduleDetailLabel));
        OnPropertyChanged(nameof(AutoStartBadgeVisibility));
        OnPropertyChanged(nameof(ScheduleBadgeVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsRecycleBinItem : INotifyPropertyChanged
{
    private bool _isRestoring;
    private bool _isDeletingPermanently;

    public SettingsRecycleBinItem(
        string itemId,
        string extensionId,
        string title,
        string category,
        string version,
        string deletedAtUtc)
    {
        ItemId = itemId;
        ExtensionId = extensionId;
        Title = title;
        Category = category;
        Version = version;
        DeletedAtUtc = deletedAtUtc;
    }

    public string ItemId { get; }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Category { get; }

    public string Version { get; }

    public string DeletedAtUtc { get; }

    public string DeletedAtLabel => DateTimeOffset.TryParse(DeletedAtUtc, out var timestamp)
        ? $"删除时间：{timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
        : "删除时间：未知";

    public bool IsRestoring
    {
        get => _isRestoring;
        set
        {
            if (_isRestoring == value)
            {
                return;
            }

            _isRestoring = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsDeletingPermanently
    {
        get => _isDeletingPermanently;
        set
        {
            if (_isDeletingPermanently == value)
            {
                return;
            }

            _isDeletingPermanently = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsOperationBusy => IsRestoring || IsDeletingPermanently;

    public string RestoreButtonText => IsRestoring ? "恢复中..." : "恢复";

    public string DeleteButtonText => IsDeletingPermanently ? "删除中..." : "彻底删除";

    public Visibility RestoreSpinnerVisibility => IsRestoring ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeleteSpinnerVisibility => IsDeletingPermanently ? Visibility.Visible : Visibility.Collapsed;

    public bool RestoreButtonEnabled => !IsOperationBusy;

    public bool DeleteButtonEnabled => !IsOperationBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsRestoring));
        OnPropertyChanged(nameof(IsDeletingPermanently));
        OnPropertyChanged(nameof(IsOperationBusy));
        OnPropertyChanged(nameof(RestoreButtonText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(RestoreSpinnerVisibility));
        OnPropertyChanged(nameof(DeleteSpinnerVisibility));
        OnPropertyChanged(nameof(RestoreButtonEnabled));
        OnPropertyChanged(nameof(DeleteButtonEnabled));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record AccountSyncStatusView(
    string ModeText,
    string HealthText,
    string RevisionText,
    string ObjectCountText,
    string LastCheckedText,
    string ExplanationText,
    bool HasErrors,
    bool HasPending,
    IReadOnlyList<AccountSyncObjectStatusItem> Objects)
{
    public static AccountSyncStatusView Empty { get; } = new(
        "未登录账号",
        "登录燕子云后可查看账号配置同步状态",
        "云端版本 0",
        "待同步项目 0 个 · 等待中 0",
        "未连接",
        "账号配置同步与个人仓库备份相互独立。",
        false,
        false,
        []);
}

public sealed record AccountSyncObjectStatusItem(
    string ObjectId,
    string DisplayName,
    string StatusText,
    long Revision,
    string UpdatedAtText,
    string DetailText,
    bool IsPending,
    bool HasError,
    bool HasConflict,
    bool HistoryAvailable);

public sealed class PersonalConfigRestorePointItem
{
    public PersonalConfigRestorePointItem(LauncherConfigRestorePointInfo info)
    {
        RestorePointId = info.RestorePointId;
        CreatedAtText = DateTimeOffset.TryParse(info.CreatedAtUtc, out var createdAt)
            ? createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : info.CreatedAtUtc;
        DeviceText = !string.IsNullOrWhiteSpace(info.SourceDeviceName)
            ? info.SourceDeviceName!
            : !string.IsNullOrWhiteSpace(info.SourceDeviceId) ? info.SourceDeviceId! : "未知设备";
        SummaryText = $"包含项目 {info.ObjectCount} 个 · 本次变更 {info.ChangedObjectIds.Count} 个";
        SizeText = info.SizeBytes < 1024
            ? $"{info.SizeBytes} B"
            : $"{info.SizeBytes / 1024.0:0.#} KB";
        RevisionText = $"备份版本 {info.Revision}";
    }

    public string RestorePointId { get; }
    public string CreatedAtText { get; }
    public string DeviceText { get; }
    public string SummaryText { get; }
    public string SizeText { get; }
    public string RevisionText { get; }
}

public sealed class ExtensionSyncConflictItem
{
    public ExtensionSyncConflictItem(ExtensionSyncConflictRecord record)
    {
        ExtensionId = record.ExtensionId;
        LocalText = record.LocalPurged
            ? $"本地：彻底删除 · rev {record.LocalRevision}"
            : record.LocalDeleted
                ? $"本地：删除 · rev {record.LocalRevision}"
                : $"本地：v{record.LocalVersion} · rev {record.LocalRevision}";
        RemoteText = record.RemotePurged
            ? $"远端：彻底删除 · rev {record.RemoteRevision}"
            : record.RemoteDeleted
                ? $"远端：删除 · rev {record.RemoteRevision}"
                : $"远端：v{record.RemoteVersion} · rev {record.RemoteRevision}";
        RemoteDeviceText = !string.IsNullOrWhiteSpace(record.RemoteDeviceName)
            ? record.RemoteDeviceName!
            : !string.IsNullOrWhiteSpace(record.RemoteDeviceId) ? record.RemoteDeviceId! : "未知设备";
        DetectedAtText = DateTimeOffset.TryParse(record.DetectedAtUtc, out var detectedAt)
            ? detectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : record.DetectedAtUtc;
    }

    public string ExtensionId { get; }
    public string LocalText { get; }
    public string RemoteText { get; }
    public string RemoteDeviceText { get; }
    public string DetectedAtText { get; }
}

public sealed class ExtensionDataConflictItem
{
    public ExtensionDataConflictItem(ExtensionDataSyncState state)
    {
        ExtensionId = state.ExtensionId;
        Key = state.Key;
        var conflict = state.Conflict ?? new ExtensionDataConflict();
        LocalText = conflict.LocalDeleted
            ? $"本地：删除 · base rev {conflict.LocalBaseRevision}"
            : $"本地：base rev {conflict.LocalBaseRevision} · {ShortHash(conflict.LocalContentHash)}";
        RemoteText = conflict.Remote.Deleted
            ? $"远端：删除 · rev {conflict.Remote.Revision}"
            : $"远端：rev {conflict.Remote.Revision} · {ShortHash(conflict.Remote.ContentHash)}";
        RemoteDeviceText = !string.IsNullOrWhiteSpace(conflict.Remote.UpdatedByDeviceName)
            ? conflict.Remote.UpdatedByDeviceName
            : !string.IsNullOrWhiteSpace(conflict.Remote.UpdatedByDeviceId)
                ? conflict.Remote.UpdatedByDeviceId
                : "未知设备";
        DetectedAtText = DateTimeOffset.TryParse(conflict.DetectedAtUtc, out var detectedAt)
            ? detectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : conflict.DetectedAtUtc;
    }

    public string ExtensionId { get; }
    public string Key { get; }
    public string DisplayId => $"{ExtensionId} / {Key}";
    public string LocalText { get; }
    public string RemoteText { get; }
    public string RemoteDeviceText { get; }
    public string DetectedAtText { get; }

    private static string ShortHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "无 hash" : value.Length <= 10 ? value : value[..10];
}

public sealed class PersonalSyncCommitItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private string? _diffText;

    public PersonalSyncCommitItem(string sha, string message, string author, DateTimeOffset committedAtUtc, string url)
    {
        Sha = sha;
        Message = string.IsNullOrWhiteSpace(message) ? "(无提交说明)" : message;
        FriendlyMessage = GetFriendlyCommitMessage(Message);
        Author = string.IsNullOrWhiteSpace(author) ? "未知作者" : author;
        CommittedAtUtc = committedAtUtc;
        Url = url;
    }

    public string Sha { get; }
    public string ShortSha => Sha.Length <= 8 ? Sha : Sha[..8];
    public string Message { get; }
    public string FriendlyMessage { get; }
    public string Author { get; }
    public DateTimeOffset CommittedAtUtc { get; }
    public string LocalTimeLabel => CommittedAtUtc.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.CurrentCulture);
    public string Url { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (value != _isExpanded)
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(DiffVisibility));
                OnPropertyChanged(nameof(DiffBtnText));
            }
        }
    }

    public string DiffBtnText => IsExpanded ? "收起差异" : "查看差异";

    public Visibility DiffVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string? DiffText
    {
        get => _diffText;
        set
        {
            if (value != _diffText)
            {
                _diffText = value;
                OnPropertyChanged(nameof(DiffText));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string GetFriendlyCommitMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return "(无提交说明)";
        }

        var msg = rawMessage.Trim();

        if (msg.Equals("Update yanm-state.json from Web", StringComparison.OrdinalIgnoreCase))
        {
            return "同步状态：更新燕幕组件状态 (云漫游)";
        }

        if (msg.StartsWith("上传数据：更新 ", StringComparison.OrdinalIgnoreCase))
        {
            var path = msg.Substring("上传数据：更新 ".Length).Trim();
            var detail = GetPathFriendlyName(path);
            return $"上传数据：更新 {detail}";
        }
        
        if (msg.StartsWith("上传数据：删除 ", StringComparison.OrdinalIgnoreCase))
        {
            var path = msg.Substring("上传数据：删除 ".Length).Trim();
            var detail = GetPathFriendlyName(path);
            return $"上传数据：删除 {detail}";
        }

        return msg;
    }

    private static string GetPathFriendlyName(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.EndsWith("state/launcher-config.json", StringComparison.OrdinalIgnoreCase))
            return "系统主设置与快捷菜单";
        if (normalized.EndsWith("state/yanm-state.json", StringComparison.OrdinalIgnoreCase))
            return "燕幕组件状态";
        if (normalized.EndsWith("state/config-manifest.json", StringComparison.OrdinalIgnoreCase))
            return "配置清单 (config-manifest.json)";
        if (normalized.Contains("state/config-changes/", StringComparison.OrdinalIgnoreCase))
            return "配置历史变更记录 (config-changes)";
        if (normalized.EndsWith("settings-general.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】通用系统设置";
        if (normalized.EndsWith("settings-ai.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】AI 助手模型与密钥设置";
        if (normalized.EndsWith("settings-hotkeys.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】系统主快捷键";
        if (normalized.EndsWith("settings-mouse-triggers.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】鼠标动作与手势触发规则";
        if (normalized.EndsWith("quick-panel-groups.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】快捷面板菜单分组";
        if (normalized.EndsWith("quick-panel-favorites.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】常用扩展收藏与禁用项";
        if (normalized.EndsWith("radial-menu-pages.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】轮盘菜单页面布局";
        if (normalized.EndsWith("yanm-layout.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】燕幕布局与样式";
        if (normalized.EndsWith("yanyu-rules.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】窗口别名 (燕语) 规则";
        if (normalized.EndsWith("window-controls.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】窗口绑定、吸附与切换配置";
        if (normalized.Contains("packages/") && normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return "个人备份扩展包";
        if (normalized.Contains("appdata/"))
            return "扩展专属应用数据备份";
            
        return path;
    }
}

public sealed class EnvironmentVariableEditorItem : INotifyPropertyChanged
{
    private string _name;
    private string _value;
    private string _description;

    public EnvironmentVariableEditorItem(string name, string value, string description)
    {
        _name = name;
        _value = value;
        _description = description;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (value == _name)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (value == _value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (value == _description)
            {
                return;
            }

            _description = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ModelNameFirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
        {
            return s.Substring(0, 1).ToUpper();
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class SettingsAiProviderVM : INotifyPropertyChanged
{
    private readonly AiServiceProviderSettings _settings;
    public AiServiceProviderSettings RawSettings => _settings;

    public SettingsAiProviderVM(AiServiceProviderSettings settings)
    {
        _settings = settings;
    }

    public string Id => _settings.Id;

    public string Name
    {
        get => _settings.Name;
        set
        {
            if (_settings.Name != value)
            {
                _settings.Name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(AvatarChar));
            }
        }
    }

    public string ProviderType
    {
        get => _settings.ProviderType;
        set
        {
            if (_settings.ProviderType != value)
            {
                _settings.ProviderType = value;
                OnPropertyChanged(nameof(ProviderType));
            }
        }
    }

    public string BaseUrl
    {
        get => _settings.BaseUrl;
        set
        {
            if (_settings.BaseUrl != value)
            {
                _settings.BaseUrl = value;
                OnPropertyChanged(nameof(BaseUrl));
                OnPropertyChanged(nameof(PreviewUrl));
            }
        }
    }

    public string ApiKey
    {
        get => _settings.ApiKey;
        set
        {
            if (_settings.ApiKey != value)
            {
                _settings.ApiKey = value;
                OnPropertyChanged(nameof(ApiKey));
            }
        }
    }

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set
        {
            if (_settings.IsEnabled != value)
            {
                _settings.IsEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public string SelectedModel
    {
        get => _settings.SelectedModel;
        set
        {
            if (_settings.SelectedModel != value)
            {
                _settings.SelectedModel = value;
                OnPropertyChanged(nameof(SelectedModel));
            }
        }
    }

    public ObservableCollection<string> Models { get; } = new();

    public string AvatarChar => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";

    public string PreviewUrl => string.IsNullOrWhiteSpace(BaseUrl) ? "无预览" : $"{BaseUrl.TrimEnd('/')}/chat/completions";

    public Visibility DetailsVisibility => Visibility.Visible; // 仅在自身 DataContext 下总是 Visible，而在外层做 Visibility 绑定

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record SearchDisplayItem(string TabKey, string DisplayTitle, System.Windows.Media.Geometry? IconGeometry);
public sealed record SettingsSearchItem(string TabKey, string DisplayTitle, string MatchTerm);

public static class SettingsSearchData
{
    public static readonly List<SettingsSearchItem> AllSearchItems = new()
    {
        // 常规
        new("general", "常规 - 主题模式", "主题模式 暗黑模式 深色模式 浅色模式 theme mode dark light"),
        new("general", "常规 - 开机启动", "开机启动 随系统启动 自启动 startup launch"),
        new("general", "常规 - 自动检测更新", "自动检测更新 自动下载更新 升级 update version upgrade"),
        new("general", "常规 - 最小化到系统托盘", "最小化到系统托盘 关闭时最小化 托盘 任务栏 tray close"),
        new("general", "常规 - 自动刷新云状态", "启动后自动刷新云状态 同步云状态 refresh cloud"),
        new("general", "常规 - 窗口快速排列", "窗口快速排列 窗口分屏 布局轮盘 Snap layout"),
        new("general", "常规 - 窗口排列快捷键", "窗口排列快捷键 快捷键 组合键 视窗快捷键 hotkey shortcut"),
        
        // 模型服务
        new("ai", "模型服务 - API Key", "API Key 密钥 接口密钥 Token 密码 鉴权 apikey key secret"),
        new("ai", "模型服务 - 自定义 Base URL", "Base URL 接口地址 代理地址 域名 接口链接 自定义服务 url"),
        new("ai", "模型服务 - 模型名称", "模型名称 默认模型 模型切换 AI模型 Gemini Claude GPT model"),
        new("ai", "模型服务 - 系统提示词", "系统提示词 System Prompt 预设 角色扮演 prompt system"),

        // 环境变量
        new("environment", "环境变量 - 添加/编辑环境变量", "环境变量 变量配置 Notion Key OpenAI Key 密钥 环境变量列表 env var key token"),

        // 同步与备份
        new("sync", "同步与备份 - WebDAV 同步", "WebDAV 同步 云同步 坚果云 同步服务器 账号 密码 备份 sync backup account password webdav"),
        new("sync", "同步与备份 - 自动备份频率", "自动备份频率 备份频率 自动备份 备份时间 frequency"),
        new("sync", "同步与备份 - 备份与恢复操作", "立即备份 立即恢复 上传备份 下载备份 同步数据 restore"),

        // 扩展
        new("extensions", "扩展 - 扩展管理", "扩展 插件 本地扩展 启用扩展 禁用扩展 编辑 搜索 打开目录 扩展根目录 extension plugin folder"),

        // 回收站
        new("recycle", "回收站 - 扩展回收站", "回收站 扩展回收站 恢复 彻底删除 已删除插件 recycle bin trash restore"),

        // 快捷键
        new("shortcuts", "快捷键 - 快捷键绑定", "快捷键绑定 热键 录制快捷键 全局快捷键 组合键 shortcut hotkey binding"),

        // 鼠标触发
        new("quickpanel", "鼠标触发 - 面板触发方式", "面板触发 鼠标触发 鼠标面板 快捷面板 右键 中键 X1键 X2键 长按 滚轮 trigger mouse right click"),

        // 鼠标手势
        new("mousegestures", "鼠标手势 - 手势绑定", "鼠标手势 手势绑定 绘制手势 轨迹 常用手势 gesture mouse draw"),

        // 燕环
        new("radial", "燕环 - 轮盘设置", "燕环 轮盘 游戏轮盘 Caps Lock 唤醒 槽位 子环 唤醒键 radial ring wheel"),

        // 燕选
        new("yarnselect", "燕选 - 选中操作", "燕选 选中操作 复制 剪切 粘贴 快捷操作 划词搜索 select copy paste selection"),

        // 燕幕
        new("yanm", "燕幕 - 仪表盘", "燕幕 仪表盘 WebView HTML 组件 全局信息层 Caps Lock 唤醒 双击 overlay webview html"),

        // 关于
        new("about", "关于 - 版本与协议", "关于 软件版本 官方网站 用户协议 开源许可 开源协议 about version update website")
    };
}

public class KeywordMatchToBrushConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string keyword && values[1] is string tag)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && !string.IsNullOrWhiteSpace(tag))
            {
                if (tag.Contains(keyword, StringComparison.OrdinalIgnoreCase) || 
                    keyword.Contains(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4DFFD600"));
                }
            }
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

