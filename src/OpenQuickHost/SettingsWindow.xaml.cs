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
    private const string RadialSimulatedKeyPrefix = ExtensionIdPrefixes.SimulatedKey;
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
    private bool _isVipActive;
    private string _vipBadgeText = "激活VIP";
    private string _vipDaysText = string.Empty;
    private string _localExtensionSummary = "正在统计...";
    private string _settingsSearchText = string.Empty;
    private string _extensionSearchText = string.Empty;
    private string _radialMenuSearchText = string.Empty;
    private string _launcherHotkey = "Alt+Space";
    private string _syncStatusText = "同步服务状态未知。";
    private string _webDavStatusText = "未启用个人小程序同步。";
    private string _syncActivityLogText = "暂无同步记录。";
    private string _personalSyncCommitStatusText = "启用个人云同步后，可查看最近的备份记录。";
    private string _personalConfigRestoreStatusText = "完成一次个人仓库配置备份后会生成恢复点。";
    private string _personalExtensionSyncStatusText = "尚未生成小程序同步索引。";
    private string _extensionDataSyncStatusText = "尚无小程序私有数据同步记录。";
    private string _extensionDataConflictActionStatusText = string.Empty;
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
    private bool _isLoadingSettings = true;
    private bool _isRefreshingSettingsFromDisk;
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

    private readonly SettingsPersistenceController _settingsPersistence;

    public SettingsWindow(MainWindow mainWindow)
    {
        HostAssets.AppendLog($"SettingsWindow ctor start. thread={Environment.CurrentManagedThreadId}, will InitializeComponent().");
        InitializeComponent();
        HostAssets.AppendLog($"SettingsWindow InitializeComponent completed. Content={Content?.GetType().Name ?? "null"}, width={Width}, height={Height}.");
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 17, 17));
        Opacity = 1;
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _settingsPersistence = new SettingsPersistenceController(_settings);
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
            new SettingsNavigationItem("extensions", "mdi:dashboard", BrandTerms.TermMiniApp, "#FFF97316"),
            new SettingsNavigationItem("quickpanel", "mdi:mouse-panel", "鼠标触发", "#FFEC4899"),
            new SettingsNavigationItem("mousegestures", "mdi:gesture-tap", BrandTerms.TermMouseGesture, "#FFFB923C"),
            new SettingsNavigationItem("radial", "mdi:circle-outline", BrandTerms.TermYanRing, "#FF3B82F6"),
            new SettingsNavigationItem("yarnselect", "mdi:shortcut", BrandTerms.TermYanSelect, "#FF14B8A6"),
            new SettingsNavigationItem("yanm", "mdi:monitor-dashboard", BrandTerms.TermYanScreen, "#FF60A5FA"),
            new SettingsNavigationItem("yanwo", "mdi:home-group", BrandTerms.TermYanNest, "#FFA855F7"),
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
        RunningExtensionRegistry.Changed += SettingsRunningExtensionRegistry_Changed;
        Closed += (s, e) =>
        {
            RunningExtensionRegistry.Changed -= SettingsRunningExtensionRegistry_Changed;
        };
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        EnvironmentVariables = new ObservableCollection<EnvironmentVariableEditorItem>(
            AppEnvironmentVariableStore.Load().Select(static item => new EnvironmentVariableEditorItem(item.Name, item.Value, item.Description)));
        EnvironmentStatusText = BuildEnvironmentSummary();
        BaseUrl = _mainWindow.SyncBaseUrl;
        ExtensionsRootPath = LocalExtensionCatalog.CatalogRootPath;
        AppVersionText = AppVersionInfo.DisplayText;
        ReleaseNotes = ReleaseHistoryProvider.GetHistory(AppVersionInfo.Version);
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
        RefreshAgentApiTokenDisplay();
        RefreshAccountObjectSyncStatus();
        // 延迟到Loaded事件中执行，避免构造函数卡顿
        // RefreshRadialMenuSlots();
        ApplySavedWindowBounds();
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        Deactivated += (_, _) => HideAgentApiToken();
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

    private bool _isExtensionBatchMode;
    public bool IsExtensionBatchMode
    {
        get => _isExtensionBatchMode;
        set
        {
            if (_isExtensionBatchMode == value) return;
            _isExtensionBatchMode = value;
            OnPropertyChanged();
            NotifyBatchSelectionChanged();
        }
    }

    public bool HasExtensionBatchSelected => ExtensionItems.Any(x => x.IsBatchChecked);

    public string ExtensionBatchDeleteButtonText => $"批量删除 ({ExtensionItems.Count(x => x.IsBatchChecked)})";

    public bool? IsAllExtensionBatchSelected
    {
        get
        {
            if (ExtensionItems.Count == 0) return false;
            var checkedCount = ExtensionItems.Count(x => x.IsBatchChecked);
            if (checkedCount == 0) return false;
            if (checkedCount == ExtensionItems.Count) return true;
            return null;
        }
        set
        {
            var selectAll = value == true;
            foreach (var item in ExtensionItems)
            {
                item.IsBatchChecked = selectAll;
            }
            NotifyBatchSelectionChanged();
        }
    }

    public void NotifyBatchSelectionChanged()
    {
        OnPropertyChanged(nameof(IsAllExtensionBatchSelected));
        OnPropertyChanged(nameof(HasExtensionBatchSelected));
        OnPropertyChanged(nameof(ExtensionBatchDeleteButtonText));
    }

    public ObservableCollection<SettingsRecycleBinItem> RecycleBinItems { get; }

    public ObservableCollection<PersonalSyncCommitItem> PersonalSyncCommitItems { get; }

    public ObservableCollection<PersonalConfigRestorePointItem> PersonalConfigRestorePoints { get; }

    public ObservableCollection<ExtensionSyncConflictItem> ExtensionSyncConflictItems { get; }

    public bool HasExtensionSyncConflicts => ExtensionSyncConflictItems.Count > 0;

    public ObservableCollection<ExtensionDataConflictItem> ExtensionDataConflictItems { get; }

    public bool HasExtensionDataConflicts => ExtensionDataConflictItems.Count > 0;

    public bool HasAccountSyncConflicts => AccountSyncStatus.Objects.Any(item => item.HasConflict);
    public IEnumerable<AccountSyncObjectStatusItem> AccountSyncConflictItems => AccountSyncStatus.Objects.Where(item => item.HasConflict);

    private void ShowAccountConflictsButton_Click(object sender, RoutedEventArgs e)
    {
        SyncActiveSubTab = "cloud";
        AccountConflictPanel.BringIntoView();
        AccountConflictPanel.Focus();
    }

    public AccountSyncStatusView AccountSyncStatus
    {
        get => _accountSyncStatus;
        private set
        {
            _accountSyncStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAccountSyncConflicts));
            OnPropertyChanged(nameof(AccountSyncConflictItems));
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
        new(YarnSelectActionTypes.RunExtension, "运行小程序")
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
        new(MouseTriggerModes.CtrlLeftDrag, "Ctrl+左键移动"),
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
        new(MouseGestureTriggerModes.MiddleDrag, "按住中键移动"),
        new(MouseGestureTriggerModes.CtrlLeftDrag, "Ctrl+左键移动")
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
        new("Panel", "背包"),
        new("Radial", "燕环"),
        new("Yanm", "燕幕")
    ];

    private static readonly IReadOnlyList<MouseTriggerOption> GestureMouseTriggerTargetOptions =
    [
        new("None", "禁用"),
        new("Panel", "背包"),
        new("Radial", "燕环"),
        new("Yanm", "燕幕"),
        new("WindowSnap", "窗口排列"),
        new("Gesture", "鼠标手势")
    ];

    private static readonly IReadOnlyList<MouseGestureTemplateDefinition> CommonMouseGestureTemplates =
    [
        new("↑", "上划", "适合返回顶部、上一页或向上滚动"),
        new("↓", "下划", "适合关闭、隐藏或向下滚动"),
        new("←", "左划", "适合后退、切换上一标签"),
        new("→", "右划", "适合前进、切换下一标签"),
        new("CHECKMARK", "✔ 打勾", "打勾手势：短线右下+长线右上，适合确认/保存/执行"),
        new("CIRCLE", "⭕ 画圆", "闭合圆圈：适合刷新、旋转、重新加载或清空"),
        new("HEART", "♥ 心形", "心形手势：双弧圆顶+尖底，适合收藏或关注"),
        new("ALPHA", "α 鱼形", "Alpha 鱼形：适合特定工具或扩展脚本"),
        new("↓→", "L 型", "适合打开目录、窗口最大化/还原"),
        new("→↓", "倒 L", "适合关闭当前标签页 (Ctrl+W)"),
        new("↑→", "右上折角", "适合新建标签页 (Ctrl+T)"),
        new("↓↑", "下上往返", "适合刷新页面 (F5)"),
        new("↑↓", "上下往返", "适合回到顶部/底部"),
        new("U", "U 型", "适合恢复已关闭标签 (Ctrl+Shift+T)"),
        new("C", "C 型", "适合复制、剪贴板动作"),
        new("P", "P 型", "适合打开快捷面板或固定小程序"),
        new("S", "S 型", "适合全局搜索、选择类动作"),
        new("Z", "Z 型", "适合窗口置顶/取消置顶"),
        new("W", "W 型", "适合关闭当前窗口 (Alt+F4)")
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
        set
        {
            _hasNewVersion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateMainButtonText));
        }
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        set
        {
            _isCheckingUpdate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateMainButtonText));
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set
        {
            _isDownloadingUpdate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateMainButtonText));
        }
    }

    public int UpdateProgressValue
    {
        get => _updateProgressValue;
        set
        {
            _updateProgressValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateMainButtonText));
        }
    }

    public bool UpdateDownloaded
    {
        get => _updateDownloaded;
        set
        {
            _updateDownloaded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateMainButtonText));
            OnPropertyChanged(nameof(IsUpdateReadyToRestart));
        }
    }

    public string UpdateMainButtonText
    {
        get
        {
            if (IsCheckingUpdate) return "检测中...";
            if (IsDownloadingUpdate) return $"下载中 {UpdateProgressValue}%";
            if (HasNewVersion) return "下载增量更新";
            return "更新";
        }
    }

    public string NewVersionInfo
    {
        get => _newVersionInfo;
        set { _newVersionInfo = value; OnPropertyChanged(); }
    }

    private ObservableCollection<ReleaseNoteEntry> _releaseNotes = new();
    public ObservableCollection<ReleaseNoteEntry> ReleaseNotes
    {
        get => _releaseNotes;
        set { _releaseNotes = value; OnPropertyChanged(); }
    }

    private void SettingsRunningExtensionRegistry_Changed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_cachedExtensionItems != null)
            {
                foreach (var item in _cachedExtensionItems)
                {
                    item.RefreshRunningState();
                }
            }
        }));
    }

    private void SubscribeUpdateEvents()
    {
        if (_updateEventsSubscribed) return;
        _updateEventsSubscribed = true;

        if (VelopackUpdateService.Instance.IsDownloading)
        {
            IsDownloadingUpdate = true;
            UpdateProgressValue = VelopackUpdateService.Instance.CurrentProgress;
        }

        if (VelopackUpdateService.Instance.IsUpdateReady)
        {
            UpdateDownloaded = true;
            _newVersionUpdateInfo = VelopackUpdateService.Instance.ReadyUpdateInfo;
            if (_newVersionUpdateInfo != null)
            {
                NewVersionInfo = _newVersionUpdateInfo.TargetFullRelease.Version.ToString();
            }
        }

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
                IsDownloadingUpdate = progress < 100;
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

        // 1. 异步拉取线上最新 Release 历史更新说明
        _ = Task.Run(async () =>
        {
            try
            {
                var onlineNotes = await ReleaseHistoryProvider.FetchOnlineHistoryAsync(AppVersionInfo.Version);
                if (onlineNotes != null && onlineNotes.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ReleaseNotes = onlineNotes;
                    });
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"SettingsWindow: FetchOnlineHistoryAsync error: {ex.Message}");
            }
        });

        if (UpdateDownloaded) return;
        if (IsCheckingUpdate || IsDownloadingUpdate) return;

        IsCheckingUpdate = true;
        try
        {
            var updateInfo = await VelopackUpdateService.Instance.CheckForUpdatesAsync(OpenQuickHost.Sync.UpdateChannelMode.Mirror);
            if (updateInfo != null)
            {
                _newVersionUpdateInfo = updateInfo;
                NewVersionInfo = updateInfo.TargetFullRelease.Version.ToString();
                HasNewVersion = true;

                // 2. 发现新版本时，自动激活后台增量下载与组装！
                _ = DownloadUpdatesCoreAsync(updateInfo);
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

    private async void UpdateMainButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 默认使用镜像更新并自动下载
        await PerformCheckForUpdatesAsync(OpenQuickHost.Sync.UpdateChannelMode.Mirror, autoDownload: true);
    }

    private void UpdateDropDownButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (UpdateDropDownButton.ContextMenu != null)
        {
            UpdateDropDownButton.ContextMenu.PlacementTarget = UpdateDropDownButton;
            UpdateDropDownButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            UpdateDropDownButton.ContextMenu.IsOpen = true;
        }
    }

    private async void UpdateMirrorMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await PerformCheckForUpdatesAsync(OpenQuickHost.Sync.UpdateChannelMode.Mirror, autoDownload: true);
    }

    private async void UpdateOfficialMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await PerformCheckForUpdatesAsync(OpenQuickHost.Sync.UpdateChannelMode.Official, autoDownload: true);
    }

    private async Task PerformCheckForUpdatesAsync(OpenQuickHost.Sync.UpdateChannelMode mode, bool autoDownload = true)
    {
        SubscribeUpdateEvents();

        if (IsCheckingUpdate || IsDownloadingUpdate) return;

        UpdateDownloaded = false;
        HasNewVersion = false;
        IsCheckingUpdate = true;
        var channelName = mode == OpenQuickHost.Sync.UpdateChannelMode.Mirror ? "镜像加速源 (ghfast.top)" : "官方直连源 (GitHub)";

        try
        {
            var updateInfo = await VelopackUpdateService.Instance.CheckForUpdatesAsync(mode);
            if (updateInfo != null)
            {
                _newVersionUpdateInfo = updateInfo;
                NewVersionInfo = updateInfo.TargetFullRelease.Version.ToString();
                HasNewVersion = true;

                // 维护期校验：检查当前用户是否有权升级到该版本
                var session = SyncSessionStore.Load();
                var newVerReleaseDate = await ReleaseHistoryProvider.GetReleaseDateAsync(NewVersionInfo);
                bool isEntitled = AppVersionInfo.IsVersionEntitled(
                    session?.IsVip ?? false,
                    session?.VipType,
                    session?.VipExpireAt,
                    newVerReleaseDate
                );

                if (!isEntitled)
                {
                    HostAssets.AppendLog($"SettingsWindow: New version {NewVersionInfo} requires VIP renewal (expireAt={session?.VipExpireAt}, releaseDate={newVerReleaseDate}).");
                    UpdateDownloaded = false;
                    IsDownloadingUpdate = false;
                    UpdateCheckStatusText = $"发现新版本 v{NewVersionInfo}，发布于您的 VIP 维护期之后。续费 VIP 即可立即一键升级！";
                    return;
                }

                if (autoDownload)
                {
                    await DownloadUpdatesCoreAsync(updateInfo);
                }
            }
            else
            {
                HasNewVersion = false;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SettingsWindow: PerformCheckForUpdates failed ({channelName}): {ex}");
            System.Windows.MessageBox.Show(
                $"通过 [{channelName}] 检测更新发生异常: {ex.Message}\n\n详细诊断日志已记录至:\n{HostAssets.HostLogPath}\n\n如需反馈，请将该日志文件一并发送给开发者。",
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
        var session = SyncSessionStore.Load();
        var newVerReleaseDate = await ReleaseHistoryProvider.GetReleaseDateAsync(NewVersionInfo);
        bool isEntitled = AppVersionInfo.IsVersionEntitled(
            session?.IsVip ?? false,
            session?.VipType,
            session?.VipExpireAt,
            newVerReleaseDate
        );
        if (!isEntitled)
        {
            ActivateVipButton_Click(sender, e);
            return;
        }
        await DownloadUpdatesCoreAsync(_newVersionUpdateInfo);
    }

    private async Task DownloadUpdatesCoreAsync(Velopack.UpdateInfo updateInfo)
    {
        IsDownloadingUpdate = true;
        UpdateProgressValue = 0;
        UpdateDownloaded = false;

        try
        {
            var success = await VelopackUpdateService.Instance.DownloadUpdatesAsync(updateInfo);
            if (success)
            {
                UpdateDownloaded = true;
                IsDownloadingUpdate = false;
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
            _settings = _settingsPersistence.Save(_settings);
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
                "导入备份会覆盖您当前所有的配置和小程序！此操作无法撤销。\n\n在导入前，我们会自动关闭 Everything 引擎。导入成功后应用将自动重启。\n\n是否确定要导入该备份？",
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
        _settings = _settingsPersistence.Save(_settings);
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

    public bool IsVipActive
    {
        get => _isVipActive;
        private set
        {
            if (value == _isVipActive)
            {
                return;
            }

            _isVipActive = value;
            OnPropertyChanged();
        }
    }

    public string VipBadgeText
    {
        get => _vipBadgeText;
        private set
        {
            if (value == _vipBadgeText)
            {
                return;
            }

            _vipBadgeText = value;
            OnPropertyChanged();
        }
    }

    public string VipDaysText
    {
        get => _vipDaysText;
        private set
        {
            if (value == _vipDaysText)
            {
                return;
            }

            _vipDaysText = value;
            OnPropertyChanged();
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
            _settings = _settingsPersistence.Save(_settings);
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
            _settings = _settingsPersistence.Save(_settings);
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
            _settings = _settingsPersistence.Save(_settings);
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
        _settings = _settingsPersistence.Save(_settings);
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
        : $"当前槽位：{_selectedRadialMenuSlot.Label} · 可添加小程序、程序、系统设置项，或右键添加子环。";

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

    public bool TriggerCtrlLeftDrag
    {
        get => _settings.QuickPanelMouseTriggers.CtrlLeftDrag;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.CtrlLeftDrag = value);
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
            var processes = ParseProcessList(value);
            if (ProcessListsEqual(_settings.GlobalServiceBlacklistedProcesses, processes)) return;
            _settings.GlobalServiceBlacklistedProcesses = processes;
            OnPropertyChanged();
            QueueQuickPanelTriggerSave(500);
        }
    }

    public string RadialBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.RadialMenu.BlacklistedProcesses ?? []);
        set
        {
            var processes = ParseProcessList(value);
            if (ProcessListsEqual(_settings.RadialMenu.BlacklistedProcesses, processes)) return;
            _settings.RadialMenu.BlacklistedProcesses = processes;
            OnPropertyChanged();
            QueueQuickPanelTriggerSave(500);
        }
    }

    public string RadialWhitelistedProcessesText
    {
        get => string.Join(", ", _settings.RadialMenu.WhitelistedProcesses ?? []);
        set
        {
            var processes = ParseProcessList(value);
            if (ProcessListsEqual(_settings.RadialMenu.WhitelistedProcesses, processes)) return;
            _settings.RadialMenu.WhitelistedProcesses = processes;
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
        set { if (value != _settings.Yanm.Enabled) UpdateYanm(value, settings => settings.Enabled = value); }
    }

    public string YanmActivationKey
    {
        get => YanmActivationKeys.Normalize(_settings.Yanm.ActivationKey);
        set
        {
            var normalized = YanmActivationKeys.Normalize(value);
            if (normalized != YanmActivationKey) UpdateYanm(normalized, settings => settings.ActivationKey = normalized);
        }
    }

    public bool YanmUsesCustomShortcut => string.Equals(YanmActivationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase);

    public string YanmCustomShortcut
    {
        get => _settings.Yanm.CustomShortcut;
        set { if (value != _settings.Yanm.CustomShortcut) UpdateYanm(value, settings => settings.CustomShortcut = value); }
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
            var processes = ParseProcessList(value);
            if (ProcessListsEqual(_settings.Yanm.BlacklistedProcesses, processes)) return;
            _settings.Yanm.BlacklistedProcesses = processes;
            OnPropertyChanged();
            QueueYanmSave(500);
        }
    }

    private static List<string> ParseProcessList(string? value) =>
        (value ?? string.Empty)
        .Split([',', ';', '，', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool ProcessListsEqual(List<string>? current, List<string> updated) =>
        (current ?? []).SequenceEqual(updated, StringComparer.OrdinalIgnoreCase);

    public string YanmWhitelistedProcessesText
    {
        get => string.Join(", ", _settings.Yanm.WhitelistedProcesses ?? []);
        set
        {
            var processes = ParseProcessList(value);
            if (ProcessListsEqual(_settings.Yanm.WhitelistedProcesses, processes)) return;
            _settings.Yanm.WhitelistedProcesses = processes;
            OnPropertyChanged();
            QueueYanmSave(500);
        }
    }

    public bool YanmTriggerHold
    {
        get => _settings.Yanm.TriggerWinHold;
        set { if (value != _settings.Yanm.TriggerWinHold) UpdateYanm(value, settings => settings.TriggerWinHold = value); }
    }

    public bool YanmTriggerDoubleTap
    {
        get => _settings.Yanm.TriggerWinDoubleTap;
        set { if (value != _settings.Yanm.TriggerWinDoubleTap) UpdateYanm(value, settings => settings.TriggerWinDoubleTap = value); }
    }

    public bool YanmMouseTriggerRightDrag
    {
        get => _settings.Yanm.TriggerRightButtonDrag;
        set { if (value != _settings.Yanm.TriggerRightButtonDrag) UpdateYanm(value, settings => settings.TriggerRightButtonDrag = value); }
    }

    public string YanmMouseTriggerMode
    {
        get => MouseTriggerModes.Normalize(_settings.Yanm.MouseTriggerMode);
        set
        {
            var normalized = MouseTriggerModes.Normalize(value);
            if (normalized != YanmMouseTriggerMode) UpdateYanm(normalized, settings => settings.MouseTriggerMode = normalized);
        }
    }

    public string RadialMouseTriggerMode
    {
        get => MouseTriggerModes.Normalize(_settings.RadialMenu.MouseTriggerMode);
        set
        {
            var normalized = MouseTriggerModes.Normalize(value);
            if (normalized != RadialMouseTriggerMode) UpdateRadialMenu(normalized, settings => settings.MouseTriggerMode = normalized);
        }
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
            if (trigger.CtrlLeftDrag) labels.Add("Ctrl+左键移动");
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
        MouseGestureTriggerModes.CtrlLeftDrag => "Ctrl+左键移动",
        _ => "未启用"
    };

    public string MouseGestureManagementSummary
    {
        get
        {
            var count = MouseGestureItems?.Count ?? 0;
            var trigger = MouseGestureTriggerSummary;
            return count == 0
                ? $"当前没有小程序绑定鼠标手势。全局触发方式：{trigger}。"
                : $"当前有 {count} 个小程序绑定鼠标手势。全局触发方式：{trigger}。";
        }
    }

    public Visibility MouseGestureEmptyVisibility =>
        MouseGestureItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string MouseGestureTriggerMode
    {
        get => MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode);
        set => UpdateMouseGestureTriggerMode(value);
    }

    public bool MouseGestureEnableWheelActions
    {
        get => _settings.MouseGestureEnableWheelActions;
        set
        {
            if (_settings.MouseGestureEnableWheelActions == value) return;
            _settings.MouseGestureEnableWheelActions = value;
            _settings = _settingsPersistence.Save(_settings);
            OnPropertyChanged(nameof(MouseGestureEnableWheelActions));
        }
    }

    public bool MouseGestureEnableRockerActions
    {
        get => _settings.MouseGestureEnableRockerActions;
        set
        {
            if (_settings.MouseGestureEnableRockerActions == value) return;
            _settings.MouseGestureEnableRockerActions = value;
            _settings = _settingsPersistence.Save(_settings);
            OnPropertyChanged(nameof(MouseGestureEnableRockerActions));
        }
    }

    public string MouseGestureBlacklistText
    {
        get => string.Join(", ", _settings.MouseGestureBlacklistedProcesses ?? []);
        set
        {
            var list = (value ?? string.Empty)
                .Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _settings.MouseGestureBlacklistedProcesses = list;
            _settings = _settingsPersistence.Save(_settings);
            OnPropertyChanged(nameof(MouseGestureBlacklistText));
        }
    }

    public string RadialAssignedMouseTriggerSummary => BuildAssignedMouseTriggerSummary(
    [
        (_settings.RadialMenu.TriggerMiddleButtonDown, "按下中键"),
        (_settings.RadialMenu.TriggerX1ButtonDown, "按下 X1 键"),
        (_settings.RadialMenu.TriggerX2ButtonDown, "按下 X2 键"),
        (_settings.RadialMenu.TriggerCtrlLeftClick, "Ctrl+左键单击"),
        (_settings.RadialMenu.TriggerCtrlLeftDrag, "Ctrl+左键移动"),
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
        (_settings.Yanm.TriggerCtrlLeftDrag, "Ctrl+左键移动"),
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
        "environment" => "配置 Notion、第三方 API 和应用型小程序可读取的用户环境变量。",
        "sync" => "管理云账号状态、同步入口和当前服务端连接信息。",
        "extensions" => "查看本地小程序目录和当前机器已发现的小程序数量。",
        "recycle" => "查看已删除小程序，支持恢复和彻底删除。",
        "quickpanel" => "统一分配鼠标动作给面板、燕环、燕幕、窗口排列和鼠标手势，避免触发方式重叠。",
        "mousegestures" => "管理小程序使用的鼠标轨迹，快速查看冲突并把常用手势绑定到小程序。",
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

        _isLoadingSettings = false;
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
        _isRefreshingSettingsFromDisk = true;
        try
        {
        _settings = AppSettingsStore.Load();
        _settingsPersistence.Reset(_settings);
        RefreshAgentApiTokenDisplay();
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
        _originalAiBaseUrl = _settings.AiBaseUrl;
        _originalAiApiKey = _settings.AiApiKey;
        _originalAiModel = _settings.AiModel;
        _originalAiSystemPrompt = _settings.AiSystemPrompt;
        HasAiSettingsChanged = false;
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
        finally
        {
            _isRefreshingSettingsFromDisk = false;
        }
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
            ? "尚未配置环境变量。应用小程序和脚本将只能读取系统环境变量。"
            : $"已配置 {count} 个用户环境变量，脚本运行和应用小程序桥接均可读取。";
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
        var pos = e.GetPosition(this);
        var isInteractive = IsInteractiveSource(e.OriginalSource as DependencyObject);
        if (pos.Y <= 64)
        {
            HostAssets.AppendLog($"[SettingsWindow.Input] TopArea PreviewMouseDown: pos=({pos.X:F0},{pos.Y:F0}), isInteractive={isInteractive}, source={e.OriginalSource?.GetType().Name}");
        }

        if (e.ButtonState != MouseButtonState.Pressed || isInteractive)
        {
            return;
        }

        if (pos.Y > 64)
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
        // 隐形预渲染期间窗口位置虽不变，但保守起见跳过持久化
        if (_isHiddenPreRender)
        {
            return;
        }

        PersistWindowBoundsDebounced();
    }

    private bool _isHiddenPreRender;

    /// <summary>
    /// 以“隐形预渲染”方式显示：窗口停在真实位置，但用 1x1 的窗口区域把自己裁剪到不可见，
    /// 等 WPF 呈现首帧后移除区域立即完整显示。
    /// 两种会闪白的做法都要避开：
    /// 1. 直接 Show —— DWM 会在 WPF 呈现首帧前合成未初始化的白色表面（内容区闪白）；
    /// 2. 停靠 -32000 等屏幕外坐标 —— 那是系统停放最小化窗口的位置，DWM 对完全离屏的
    ///    窗口暂停合成，移回屏幕时同样会闪一帧白。
    /// 窗口区域只是合成裁剪，表面内容始终在正常合成路径上，移除区域展示的就是已呈现的帧。
    /// </summary>
    public void ShowOffscreen(Action onScreenReady)
    {
        if (IsVisible)
        {
            onScreenReady();
            return;
        }

        var previousShowActivated = ShowActivated;
        ShowActivated = false; // 预渲染阶段不抢焦点，显示时再激活
        _isHiddenPreRender = true;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        }

        // 1x1 区域位于窗口左上角（系统拥有区域对象的生命周期，无需删除）
        var region = Win32Native.CreateRectRgn(0, 0, 1, 1);
        if (region != IntPtr.Zero)
        {
            Win32Native.SetWindowRgn(hwnd, region, false);
        }

        HostAssets.AppendLog("SettingsWindow: hidden pre-render show (1x1 window region).");
        Show();

        EventHandler? onRendering = null;
        var settled = false;
        void SettleOnScreen()
        {
            // 渲染回调与兜底定时器可能都会触发，只结算一次
            if (settled)
            {
                return;
            }

            settled = true;
            CompositionTarget.Rendering -= onRendering;
            _isHiddenPreRender = false;

            if (IsVisible)
            {
                // 移除裁剪区域，窗口立即以已呈现的完整帧出现
                Win32Native.SetWindowRgn(hwnd, IntPtr.Zero, true);
                ShowActivated = previousShowActivated;
                HostAssets.AppendLog("SettingsWindow: hidden pre-render done, region cleared.");
                onScreenReady();
            }
        }

        var renderingTicks = 0;
        onRendering = (_, _) =>
        {
            // 第一个 Rendering 回调发生在首帧渲染之前，跳过它，
            // 在第二个合成回调里显示（此时表面已呈现过至少一帧）
            renderingTicks++;
            if (renderingTicks < 2)
            {
                return;
            }

            CompositionTarget.Rendering -= onRendering;
            Dispatcher.BeginInvoke(SettleOnScreen, DispatcherPriority.Render);
        };
        CompositionTarget.Rendering += onRendering;

        // 兜底：渲染回调异常/被阻塞时最迟 300ms 显示
        _ = Task.Delay(300).ContinueWith(
            _ => Dispatcher.BeginInvoke(SettleOnScreen),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();

        // 先写入尚未到期的编辑，再保存窗口位置；位置保存会读取最新设置。
        _windowBoundsPersistTimer?.Stop();
        FlushYarnSelectSave();
        FlushYanmSave();
        FlushQuickPanelTriggerSave();
        FlushAiSettingsSave();
        FlushWebDavSettingsSave();
        FlushEnvironmentVariablesSave();
        FlushWanPushSave();
        PersistWindowBounds();
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
        if (latest.SettingsWindowLeft == Left && latest.SettingsWindowTop == Top &&
            latest.SettingsWindowWidth == Width && latest.SettingsWindowHeight == Height)
        {
            return;
        }

        latest = latest with
        {
            SettingsWindowLeft = Left,
            SettingsWindowTop = Top,
            SettingsWindowWidth = Width,
            SettingsWindowHeight = Height
        };
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
        if (_isLoadingSettings || _isRefreshingSettingsFromDisk || !IsLoaded ||
            string.Equals(AppSettingsStore.Load().ThemeMode, _settings.ThemeMode, StringComparison.Ordinal))
        {
            return;
        }

        _settings = _settingsPersistence.Save(_settings);
        _mainWindow.RefreshAppSettings();
        if (IsLoaded)
        {
            _mainWindow.NotifyQuickPanelSettingsChanged("theme-mode-changed", refreshYanmOverlay: false);
        }
    }

    private void SaveSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settingsPersistence.Save(_settings);
        _mainWindow.RefreshAppSettings();
        StartupRegistrationService.Apply(_settings.LaunchAtStartup);
        if (sender is FrameworkElement { DataContext: SettingsWindow })
        {
            _mainWindow.NotifyQuickPanelSettingsChanged("general-setting-changed", refreshYanmOverlay: false);
        }
    }

    // The console deliberately no longer embeds credentials in public HTML.
    // Display the *running server's* token here, falling back to the locally saved setting.
    private string GetAgentApiTokenForDisplay()
    {
        var activeToken = (System.Windows.Application.Current as App)?.AgentApiServer?.TokenForSettings;
        return !string.IsNullOrWhiteSpace(activeToken)
            ? activeToken
            : (AppSettingsStore.Load().AgentApiToken ?? string.Empty);
    }

    private void HideAgentApiToken()
    {
        if (AgentApiTokenVisibleTextBox == null) return;
        AgentApiTokenVisibleTextBox.Text = string.Empty;
        AgentApiTokenVisibleTextBox.Visibility = Visibility.Collapsed;
        AgentApiTokenMaskedText.Visibility = Visibility.Visible;
        AgentApiTokenVisibilityButton.Content = "显示";
    }

    private void RefreshAgentApiTokenDisplay()
    {
        if (AgentApiTokenMaskedText == null) return;
        HideAgentApiToken();
        AgentApiTokenMaskedText.Text = string.IsNullOrWhiteSpace(GetAgentApiTokenForDisplay())
            ? "未配置"
            : "••••••••••••••••";
        AgentApiTokenHintText.Text =
            "默认隐藏。点击复制并粘贴到 API 控制台的 X-Yanzi-Token；请勿公开分享。";
    }

    private void ToggleAgentApiTokenVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (AgentApiTokenVisibleTextBox.Visibility == Visibility.Visible)
        {
            HideAgentApiToken();
            AgentApiTokenHintText.Text = "Token 已隐藏。";
            return;
        }

        var token = GetAgentApiTokenForDisplay();
        if (string.IsNullOrWhiteSpace(token))
        {
            AgentApiTokenHintText.Text = "尚未配置 API Token。";
            return;
        }

        AgentApiTokenVisibleTextBox.Text = token;
        AgentApiTokenVisibleTextBox.Visibility = Visibility.Visible;
        AgentApiTokenMaskedText.Visibility = Visibility.Collapsed;
        AgentApiTokenVisibilityButton.Content = "隐藏";
        AgentApiTokenHintText.Text = "Token 仅在本地设置窗口显示，离开窗口后自动隐藏。";
    }

    private void CopyAgentApiToken_Click(object sender, RoutedEventArgs e)
    {
        var token = GetAgentApiTokenForDisplay();
        if (string.IsNullOrWhiteSpace(token))
        {
            AgentApiTokenHintText.Text = "尚未配置 API Token。";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(token);
            AgentApiTokenHintText.Text = "已复制当前 API Token，请粘贴到控制台；剪贴板中包含敏感信息。";
        }
        catch (System.Exception ex)
        {
            AgentApiTokenHintText.Text = "复制失败：" + ex.Message;
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

    private void OpenAiCapabilitiesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = _settings.AgentApiPort;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{port}/docs#capability-console",
                UseShellExecute = true
            });
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"无法打开能力管理: {ex.Message}");
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
        _settingsPersistence.Reset(_settings);
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
        _settingsPersistence.Reset(_settings);
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
        if (_isLoadingSettings || _isRefreshingSettingsFromDisk)
        {
            return;
        }

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
        _isUpdatingMouseTriggerTargetCombos = true;
        try
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

                var currentTarget = GetGestureTarget(gesture);

                var combo = new WpfComboBox
                {
                    Tag = gesture,
                    Height = 28,
                    MinWidth = 120,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    ItemsSource = GetMouseTriggerTargetOptions(gesture),
                    DisplayMemberPath = nameof(MouseTriggerOption.Label),
                    SelectedValuePath = nameof(MouseTriggerOption.Value),
                    SelectedValue = currentTarget,
                    Style = TryFindResource("GlobalComboBoxStyle") as Style
                };
                combo.SelectionChanged += MouseTriggerTargetCombo_SelectionChanged;
                grid.Children.Add(combo);
                _mouseTriggerTargetCombos[gesture] = combo;
            }
        }
        finally
        {
            _isUpdatingMouseTriggerTargetCombos = false;
        }
    }

    private static IReadOnlyList<MouseTriggerOption> GetMouseTriggerTargetOptions(string gestureName) => gestureName switch
    {
        "RightButtonDrag" => GestureMouseTriggerTargetOptions,
        "MiddleButtonDrag" => GestureMouseTriggerTargetOptions,
        "CtrlLeftDrag" => GestureMouseTriggerTargetOptions,
        _ => StandardMouseTriggerTargetOptions
    };

    private void MouseTriggerTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingMouseTriggerTargetCombos || _isLoadingSettings || _isRefreshingSettingsFromDisk)
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

        if (string.Equals(target, GetGestureTarget(gestureName), StringComparison.Ordinal))
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
        else if (normalized == MouseGestureTriggerModes.CtrlLeftDrag)
        {
            ClearGestureFromAllTargets("CtrlLeftDrag");
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
        MouseTriggerModes.CtrlLeftDrag => "CtrlLeftDrag",
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
        MouseTriggerModes.CtrlLeftDrag => "Ctrl+左键移动",
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
            case "CtrlLeftDrag":
                trigger.CtrlLeftDrag = false;
                radial.TriggerCtrlLeftDrag = false;
                yanm.TriggerCtrlLeftDrag = false;
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
             string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.MiddleDrag, StringComparison.Ordinal)) ||
            (gestureName == "CtrlLeftDrag" &&
             string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.CtrlLeftDrag, StringComparison.Ordinal)))
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
        "CtrlLeftDrag" => MouseTriggerModes.CtrlLeftDrag,
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
                _settings.RadialMenu ??= new RadialMenuSettings();
                _settings.RadialMenu.Enabled = true;
                SetGestureForRadial(gestureName, true);
                break;
            case "Yanm":
                _settings.Yanm ??= new YanmSettings();
                _settings.Yanm.Enabled = true;
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
                else if (gestureName == "CtrlLeftDrag")
                {
                    _settings.MouseGestureTriggerMode = MouseGestureTriggerModes.CtrlLeftDrag;
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
            case "CtrlLeftDrag": trigger.CtrlLeftDrag = value; break;
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
            case "CtrlLeftDrag": radial.TriggerCtrlLeftDrag = value; break;
            case "CtrlRightClick": radial.TriggerCtrlRightClick = value; break;
            case "CtrlMiddleClick": radial.TriggerCtrlMiddleClick = value; break;
        }

        if (value)
        {
            radial.MouseTriggerMode = GestureNameToMouseTriggerMode(gestureName);
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
            case "CtrlLeftDrag": yanm.TriggerCtrlLeftDrag = value; break;
            case "CtrlRightClick": yanm.TriggerCtrlRightClick = value; break;
            case "CtrlMiddleClick": yanm.TriggerCtrlMiddleClick = value; break;
        }

        if (value)
        {
            yanm.MouseTriggerMode = GestureNameToMouseTriggerMode(gestureName);
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
            case "CtrlLeftDrag":
                panelValue = trigger.CtrlLeftDrag;
                radialValue = radial.TriggerCtrlLeftDrag;
                yanmValue = yanm.TriggerCtrlLeftDrag;
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

        // 优先判断各个功能显式启用的触发开关，避免被全局默认的 MouseGestureTriggerMode 掩盖而发生误判或被重置
        if (radialValue) return "Radial";
        if (panelValue) return "Panel";
        if (yanmValue) return "Yanm";

        var legacyMode = GestureNameToMouseTriggerMode(gestureName);
        if (legacyMode != MouseTriggerModes.None &&
            string.Equals(MouseTriggerModes.Normalize(_settings.WindowSnapAssistMouseTriggerMode), legacyMode, StringComparison.OrdinalIgnoreCase))
        {
            return "WindowSnap";
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

        if (gestureName == "CtrlLeftDrag" &&
            string.Equals(MouseGestureTriggerModes.Normalize(_settings.MouseGestureTriggerMode), MouseGestureTriggerModes.CtrlLeftDrag, StringComparison.Ordinal))
        {
            return "Gesture";
        }

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
        "CtrlLeftClick", "CtrlLeftDrag", "CtrlRightClick", "CtrlMiddleClick"
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
        _settingsPersistence.Reset(_settings);
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
                PersonalExtensionSyncStatusText = $"{authority} · 尚未生成本地小程序索引";
                return;
            }

            var index = JsonSerializer.Deserialize<WebDavSyncIndex>(
                File.ReadAllText(HostAssets.WebDavSyncStatePath),
                JsonDefaults.CaseInsensitive) ?? new WebDavSyncIndex();
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
                $"{authority} · 云端版本 {index.Revision} · 有效小程序 {active} 个 · 已删除 {deleted} 个 · 已彻底删除 {purged} 个 · 待同步 {pending} 个 · 来源设备: {source} · 更新时间: {updated}";
        }
        catch (Exception ex)
        {
            PersonalExtensionSyncStatusText = $"小程序同步索引无法读取：{ex.Message}";
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
        var originalContent = button.Content;
        button.Content = "处理中…";
        ExtensionDataConflictActionStatusText = $"正在核对 {item.DisplayId} 的本机和云端版本…";
        try
        {
            var result = await _mainWindow.ResolveExtensionDataSyncConflictAsync(
                item.ExtensionId,
                item.Key,
                useLocalVersion);
            SyncStatusText = result.message;
            ExtensionDataConflictActionStatusText = result.message;
            RefreshPersonalExtensionSyncStatus();
            RefreshSyncActivityLog();
        }
        catch (Exception ex)
        {
            SyncStatusText = $"处理小程序数据冲突失败：{ex.Message}";
            ExtensionDataConflictActionStatusText = SyncStatusText;
        }
        finally
        {
            button.Content = originalContent;
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
            PersonalConfigRestoreStatusText = "正在读取历史备份...";
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
            $"将主配置恢复到 {item.CreatedAtText} 的状态？\n\n这会恢复设置、快捷面板、燕环和规则，但不会替换本机密钥、小程序包或燕幕。恢复结果会作为一个新版本继续同步，原恢复点不会删除。",
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
            PersonalSyncCommitStatusText = "此处可查看 GitHub、Gitee、GitLab 或 Gitea 中的备份记录。";
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
                PersonalSyncCommitStatusText = $"正在读取 {SelectedPersonalSyncProvider} 备份记录...";
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
                ? "还没有云端备份记录。"
                : $"最近 {PersonalSyncCommitItems.Count} 条备份记录，可查看详情。";

            CloudSyncDiagnostics.Log(
                "SettingsWindow.PersonalSync",
                "Refresh personal sync commits completed",
                ("provider", SelectedPersonalSyncProvider),
                ("count", PersonalSyncCommitItems.Count));
        }
        catch (Exception ex)
        {
            PersonalSyncCommitItems.Clear();
            PersonalSyncCommitStatusText = $"暂时无法读取备份记录：{ex.Message}";
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
            "此操作将删除云端的所有小程序和配置数据，且无法恢复！\n\n" +
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
                "云端数据已成功清空。\n\n下次点击\"立即同步\"时将重新上传本地小程序。",
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
            System.Windows.MessageBox.Show(this, "小程序目录不存在。", "打开目录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void OpenHostLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = HostAssets.HostLogPath;
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(logPath))
            {
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [System] Yanzi host log created.{Environment.NewLine}", System.Text.Encoding.UTF8);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"打开运行日志文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenHostLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = HostAssets.HostLogPath;
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(logPath))
            {
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [System] Yanzi host log created.{Environment.NewLine}", System.Text.Encoding.UTF8);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{logPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"打开日志目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleExtensionBatchModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsExtensionBatchMode = true;
        foreach (var item in ExtensionItems)
        {
            item.IsBatchChecked = false;
        }
        NotifyBatchSelectionChanged();
    }

    private void ExitExtensionBatchModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsExtensionBatchMode = false;
        foreach (var item in ExtensionItems)
        {
            item.IsBatchChecked = false;
        }
        NotifyBatchSelectionChanged();
    }

    private void ExtensionBatchSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb)
        {
            var selectAll = cb.IsChecked == true;
            foreach (var item in ExtensionItems)
            {
                item.IsBatchChecked = selectAll;
            }
            NotifyBatchSelectionChanged();
        }
    }

    private void ExtensionBatchItemCheck_Click(object sender, RoutedEventArgs e)
    {
        NotifyBatchSelectionChanged();
    }

    private async void BatchDeleteExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = ExtensionItems.Where(x => x.IsBatchChecked).ToList();
        if (selectedItems.Count == 0) return;

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认将选中的 {selectedItems.Count} 个小程序移入回收站吗？",
            "批量删除小程序",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        int successCount = 0;
        foreach (var item in selectedItems)
        {
            var result = await _mainWindow.DeleteExtensionFromSettingsSilentlyAsync(item.ExtensionId);
            if (result.ok)
            {
                successCount++;
                ExtensionItems.Remove(item);
            }
        }

        _mainWindow.NotifyExtensionsBatchDeleted();
        _settings = _mainWindow.GetCurrentAppSettings();
        _settingsPersistence.Reset(_settings);
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        OnPropertyChanged(nameof(ExtensionSearchSummary));
        RefreshShortcutItems();
        await RefreshExtensionsFromDiskAsync();

        NotifyBatchSelectionChanged();
        if (ExtensionItems.Count(x => x.IsBatchChecked) == 0)
        {
            IsExtensionBatchMode = false;
        }

        SyncStatusText = $"已批量移入回收站 {successCount} 个小程序。";
        if (System.Windows.Application.Current is App app)
        {
            app.ShowDesktopNotification("批量删除完成", $"已成功将 {successCount} 个小程序移入回收站。");
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

        if (IsExtensionBatchMode)
        {
            item.IsBatchChecked = !item.IsBatchChecked;
            NotifyBatchSelectionChanged();
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
                System.Windows.MessageBox.Show(this, result.message, "编辑小程序失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _settings = _mainWindow.GetCurrentAppSettings();
        _settingsPersistence.Reset(_settings);
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
                System.Windows.MessageBox.Show(this, result.message, "删除小程序失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        ExtensionItems.Remove(item);
        _settings = _mainWindow.GetCurrentAppSettings();
        _settingsPersistence.Reset(_settings);
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
                System.Windows.MessageBox.Show(this, result.message, "恢复小程序失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("小程序已恢复", $"{item.Title} 已从回收站恢复。");
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
            "彻底删除小程序",
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
                app.ShowDesktopNotification("回收站小程序已清理", $"{item.Title} 已从回收站彻底删除。");
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
                System.Windows.MessageBox.Show(this, result.message, "发布小程序失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("小程序已发布到商店", $"{item.Title} 已完成发布，可在小程序商店查看。");
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
            $"确认下线小程序“{item.Title}”吗？下线后小程序商店将不再展示它。",
            "确认下线小程序",
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
                System.Windows.MessageBox.Show(this, result.message, "下线小程序失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("小程序已从商店下线", $"{item.Title} 已从小程序商店移除。");
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
        LocalExtensionSummary = "正在后台刷新小程序数据...";
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
                        Category = entry.Manifest.Category ?? "小程序",
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
                _mainWindow.ReloadLocalExtensionsFromEntries(data.entries, "已刷新本地小程序。");
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
            LocalExtensionSummary = $"刷新小程序失败：{ex.Message}";
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
        _settingsPersistence.Reset(_settings);
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
        _settingsPersistence.Reset(_settings);
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
            BrandTerms.Format("设置{Warehouse}快捷键"),
            BrandTerms.Format("窗口激活后，直接按一次新的组合键即可完成录制。也支持全局双击 Ctrl 或双击 Alt 呼出{Warehouse}。"),
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
        _settingsPersistence.Reset(_settings);
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
            "设置小程序快捷键",
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
            "设置小程序快捷键",
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
        _settingsPersistence.Reset(_settings);
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

    private bool _accountSyncBusy;
    public bool IsAccountSyncIdle => !_accountSyncBusy;

    private async void UploadAccountSettingsButton_Click(object sender, RoutedEventArgs e) => await TransferAccountSettingsFromButtonAsync(true);
    private async void DownloadAccountSettingsButton_Click(object sender, RoutedEventArgs e) => await TransferAccountSettingsFromButtonAsync(false);

    private async Task TransferAccountSettingsFromButtonAsync(bool upload)
    {
        if (_accountSyncBusy) return;
        _accountSyncBusy = true;
        OnPropertyChanged(nameof(IsAccountSyncIdle));
        try
        {
            SyncStatusText = upload ? "正在上传账号设置…" : "正在下载账号设置，本机未上传的修改会保留…";
            var result = await _mainWindow.TransferAccountSettingsAsync(upload);
            SyncStatusText = result.message;
            RefreshAccountObjectSyncStatus();
            RefreshSyncActivityLog();
        }
        catch (Exception ex) { SyncStatusText = $"本次同步未完成：{ex.Message}"; }
        finally
        {
            _accountSyncBusy = false;
            OnPropertyChanged(nameof(IsAccountSyncIdle));
        }
    }

    private async void RefreshAccountObjectSyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_accountSyncBusy) return;
        _accountSyncBusy = true;
        OnPropertyChanged(nameof(IsAccountSyncIdle));

        try
        {
            SyncStatusText = "正在刷新账号配置同步状态...";
            var upload = await _mainWindow.TransferAccountSettingsAsync(upload: true);
            await RefreshCloudAsync();
            if (!upload.ok) SyncStatusText = upload.message;
            RefreshSyncActivityLog();
        }
        catch (Exception ex)
        {
            SyncStatusText = $"账号配置同步刷新失败：{ex.Message}";
            RefreshAccountObjectSyncStatus();
        }
        finally
        {
            _accountSyncBusy = false;
            OnPropertyChanged(nameof(IsAccountSyncIdle));
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

    private void ShowTaskRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow.CloudSyncClient is not { CurrentUserId: not null } client) { SyncStatusText = "请先登录燕子账号。"; return; }
        new TaskRecoveryWindow(_mainWindow, client) { Owner = this }.Show();
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
            var result = await _mainWindow.ResolveCloudObjectConflictAsync(item.ObjectId, useLocalVersion, item.Revision);
            await RefreshCloudAsync();
            SyncStatusText = result.message;
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
        if (!string.IsNullOrWhiteSpace(state.PersistenceError))
        {
            AccountSyncStatus = new AccountSyncStatusView("同步已暂停", "本机同步记录需要修复", "", "", "",
                state.PersistenceError, true, false, []);
            return;
        }
        var currentDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
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
            var source = SyncUserText.Device(cached?.UpdatedByDeviceId, cached?.UpdatedByDeviceName, currentDeviceId);
            var detail = conflict != null
                ? "本机修改已单独保留 · 云端来源：" + source
                : hasError ? "暂时未能同步，将自动重试"
                : pending != null
                    ? state.ObjectSyncAvailable ? "等待上传，可在云端同步页点击 ↑ 重试" : "服务端暂不支持此项上传"
                    : $"来源：{source}";
            return new AccountSyncObjectStatusItem(
                objectId,
                GetAccountSyncObjectDisplayName(objectId, cached),
                status,
                conflict?.RemoteRevision ?? cached?.Revision ?? pending?.LastObservedRemoteRevision ?? 0,
                FormatAccountSyncTime(cached?.UpdatedAtUtc),
                detail,
                pending != null,
                hasError,
                conflict != null,
                state.ObjectHistoryAvailable)
            {
                ConflictSummary = conflict == null ? "" : SyncUserText.Differences(conflict, cached)
            };
        }).ToArray();

        var pendingCount = state.PendingOperations.Count;
        var errorCount = state.PendingOperations.Values.Count(static item => !string.IsNullOrWhiteSpace(item.LastError));
        var conflictCount = state.Conflicts.Count;
        var modeText = !state.ObjectSyncAvailable
            ? state.ServerProtocolVersion == 0 ? "正在建立连接" : "兼容备份模式"
            : state.ObjectsAuthoritative ? "自动同步" : "正在升级同步";
        var healthText = conflictCount > 0
            ? $"{conflictCount} 项设置需要你选择 · 点击查看"
            : errorCount > 0
            ? $"{errorCount} 项数据同步失败，等待重试"
            : pendingCount > 0
                ? $"{pendingCount} 项数据等待同步"
                : state.Objects.Count > 0 ? "所有账号配置均已同步" : "等待首次同步";
        var explanation = conflictCount > 0
            ? "这台电脑保留的修改与云端不同，请查看后选择要保留的一份。"
            : errorCount > 0 ? "暂时未能完成同步，联网后会自动重试。"
            : "设置会通过账号同步；个人存储空间中的备份单独管理。";

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
            "settings.runtime" => "运行与小程序环境",
            "settings.ai" => "AI 服务设置",
            "settings.hotkeys" => "全局快捷键",
            "settings.mouseTriggers" => "鼠标与手势触发",
            "quickPanel.groups" => "快捷面板分组（旧版）",
            "quickPanel.groupIndex" => "快捷面板分组与顺序",
            "quickPanel.favorites" => "收藏、禁用与搜索范围",
            "radialMenu.pages" => "燕环页面（旧版）",
            "radialMenu.pageIndex" => "燕环页面与呼出方式",
            "yanyu.rules" => "燕语规则",
            "window.controls" => "窗口控制与燕选",
            "yanm.layout" => "燕幕布局与组件定义",
            "yanm.componentStateIndex" => "燕幕组件数据",
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
            AccountSubtitle = $"用户 ID {session.UserId}";
            AccountInitial = session.Username[..1].ToUpperInvariant();

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_mainWindow.CloudSyncClient != null)
                    {
                        var status = await _mainWindow.CloudSyncClient.GetVipStatusAsync();
                        Dispatcher.Invoke(() =>
                        {
                            if (status != null && status.IsVip)
                            {
                                IsVipActive = true;
                                if (status.VipType == "lifetime")
                                {
                                    VipBadgeText = "终身VIP";
                                    VipDaysText = "永久";
                                }
                                else
                                {
                                    VipBadgeText = $"VIP {status.DaysRemaining}天";
                                    VipDaysText = $"{status.DaysRemaining}天";
                                }
                                AccountSubtitle = $"{VipBadgeText} · ID {session.UserId}";
                            }
                            else
                            {
                                IsVipActive = false;
                                VipBadgeText = "激活VIP";
                                VipDaysText = string.Empty;
                                AccountSubtitle = $"普通用户 · ID {session.UserId}";
                            }
                        });
                    }
                }
                catch
                {
                    // 忽略后台静默拉取失败
                }
            });
            return;
        }

        IsAccountLoggedIn = false;
        IsVipActive = false;
        VipBadgeText = "激活VIP";
        VipDaysText = string.Empty;
        AccountTitle = "未登录";
        AccountSubtitle = "点击登录或切换账号。";
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
        LocalExtensionSummary = $"当前机器已发现 {count} 个小程序。";
        OnPropertyChanged(nameof(ExtensionSearchSummary));
    }

    private void RefreshRecycleBinSummary()
    {
        var count = _cachedRecycleBinItems.Count;
        RecycleBinSummary = count == 0
            ? "回收站为空。"
            : $"当前回收站中有 {count} 个小程序。";
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
        NotifyBatchSelectionChanged();
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

        var whitelistAppsBySeq = _settings.MouseGestureAppBindings
            .Where(b => !b.IsBlacklist && !string.IsNullOrWhiteSpace(b.Sequence) && !string.IsNullOrWhiteSpace(b.AppPath))
            .GroupBy(b => MouseGestureNaming.NormalizeSequence(b.Sequence), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var blacklistAppsBySeq = _settings.MouseGestureAppBindings
            .Where(b => b.IsBlacklist && !string.IsNullOrWhiteSpace(b.Sequence) && !string.IsNullOrWhiteSpace(b.AppPath))
            .GroupBy(b => MouseGestureNaming.NormalizeSequence(b.Sequence), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var assignedBySequence = new Dictionary<string, string>(StringComparer.Ordinal);
        var handledAppSequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in LocalExtensionCatalog.LoadEntries())
        {
            var gesture = entry.Manifest.MouseGesture;
            if (gesture == null || (string.IsNullOrWhiteSpace(gesture.Sequence) && !MouseGestureTemplateRecognizer.HasTemplateData(gesture.Data)))
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

            var boundW = whitelistAppsBySeq.TryGetValue(sequence, out var wl) ? wl.Select(x => x.AppPath).ToList() : null;
            var boundB = blacklistAppsBySeq.TryGetValue(sequence, out var bl) ? bl.Select(x => x.AppPath).ToList() : null;
            handledAppSequences.Add(sequence);

            MouseGestureItems.Add(new SettingsMouseGestureItem(
                entry.Manifest.Id,
                entry.Manifest.Name,
                entry.Manifest.Category ?? command?.Category ?? "小程序",
                BuildGestureTriggerLabel(),
                sequence,
                sign,
                gesture.Data,
                gesture.MinDistance ?? 30,
                gesture.Tolerance,
                command?.IconSource ?? ExtensionIconLibrary.ResolveImageSource(entry.Manifest.Icon, directory),
                command?.VectorIcon ?? ExtensionIconLibrary.ResolveVectorIcon(entry.Manifest.Icon),
                command?.AccentBrush ?? CreateAccentBrush(entry.Manifest.AccentHex),
                command?.DisplayGlyph ?? BuildFallbackGlyph(entry.Manifest.Name),
                boundW,
                boundB));
        }

        foreach (var appGroup in whitelistAppsBySeq)
        {
            if (handledAppSequences.Contains(appGroup.Key)) continue;

            var first = appGroup.Value[0];
            var seq = appGroup.Key;
            var appName = string.IsNullOrWhiteSpace(first.AppName) ? "应用程序" : first.AppName;
            if (!assignedBySequence.ContainsKey(seq))
            {
                assignedBySequence[seq] = appName;
            }

            var boundW = appGroup.Value.Select(x => x.AppPath).ToList();
            var boundB = blacklistAppsBySeq.TryGetValue(seq, out var bl) ? bl.Select(x => x.AppPath).ToList() : null;

            MouseGestureItems.Add(new SettingsMouseGestureItem(
                "app:" + first.AppPath,
                appName,
                "应用程序",
                BuildGestureTriggerLabel(),
                seq,
                MouseGestureNaming.GetDisplayName(seq),
                null,
                30,
                null,
                ExtensionIconLibrary.TryExtractAssociatedIcon(first.AppPath),
                null,
                CreateAccentBrush("#3B82F6"),
                BuildFallbackGlyph(appName),
                boundW,
                boundB));
        }

        var extBySeq = new Dictionary<string, (string ExtId, string Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LocalExtensionCatalog.LoadEntries())
        {
            var g = entry.Manifest.MouseGesture;
            if (g == null || (string.IsNullOrWhiteSpace(g.Sequence) && !MouseGestureTemplateRecognizer.HasTemplateData(g.Data))) continue;
            var norm = MouseGestureNaming.NormalizeSequence(g.Sequence);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                extBySeq[norm] = (entry.Manifest.Id, entry.Manifest.Name);
            }
        }

        foreach (var template in CommonMouseGestureTemplates)
        {
            var normSeq = MouseGestureNaming.NormalizeSequence(template.Sequence);
            string? extId = null;
            string? assignedTitle = null;
            var boundWhitelist = new List<string>();
            var boundBlacklist = new List<string>();

            if (extBySeq.TryGetValue(normSeq, out var extInfo))
            {
                extId = extInfo.ExtId;
                assignedTitle = extInfo.Name;
            }

            if (whitelistAppsBySeq.TryGetValue(normSeq, out var wApps) && wApps.Count > 0)
            {
                boundWhitelist = wApps.Select(a => a.AppPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (blacklistAppsBySeq.TryGetValue(normSeq, out var bApps) && bApps.Count > 0)
            {
                boundBlacklist = bApps.Select(a => a.AppPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (boundWhitelist.Count > 0 || boundBlacklist.Count > 0)
            {
                var tagList = new List<string>();
                if (boundWhitelist.Count > 0) tagList.Add($"限定 {boundWhitelist.Count} 个应用");
                if (boundBlacklist.Count > 0) tagList.Add($"禁用 {boundBlacklist.Count} 个应用");
                var appTag = string.Join(", ", tagList);

                if (string.IsNullOrWhiteSpace(assignedTitle))
                {
                    assignedTitle = appTag;
                }
                else
                {
                    assignedTitle = $"{assignedTitle} ({appTag})";
                }
            }

            MouseGestureQuickBindItems.Add(new MouseGestureQuickBindItem(
                template.Sequence,
                template.Name,
                template.Description,
                assignedTitle,
                null,
                extId,
                boundWhitelist,
                boundBlacklist));
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
            _settings = _settingsPersistence.Save(_settings);
            _mainWindow.NotifyQuickPanelSettingsChanged("mouse-gesture-app-unbound", refreshYanmOverlay: false);
            _mainWindow.ReloadMouseGestureRegistrations();
            RefreshMouseGestureManagement();
            SyncStatusText = $"已解绑应用手势 [{item.Title}]。";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ExtensionId))
        {
            SyncStatusText = "当前手势没有可解绑的小程序。";
            return;
        }

        var result = await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(item.ExtensionId, null);
        await HandleMouseGestureUpdateResultAsync(result);
    }

    private async void ChangeBoundGestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsMouseGestureItem item })
        {
            return;
        }

        var triggerLabel = BuildGestureTriggerLabel();
        var dlg = new MouseGestureBindingDialog(
            item.Sequence,
            item.DisplayName,
            item.DetailText,
            triggerLabel,
            item.Data,
            item.ExtensionId,
            item.BoundWhitelistAppPaths,
            item.BoundBlacklistAppPaths,
            MouseGestureExtensionOptions,
            MouseGestureAppOptions)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            if (dlg.WasUnbound)
            {
                await UnbindGestureAsync(item.Sequence);
                RefreshMouseGestureManagement();
                SyncStatusText = $"已解绑手势 [{item.DisplayName}]。";
            }
            else if (dlg.WasSaved)
            {
                // 1. 如果选择了新小程序，绑定小程序手势
                if (dlg.SelectedExtension != null)
                {
                    // 若原先绑定的是不同的小程序，先解绑旧的小程序
                    if (!string.IsNullOrWhiteSpace(item.ExtensionId) &&
                        !item.ExtensionId.StartsWith("app:", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(item.ExtensionId, dlg.SelectedExtension.ExtensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(item.ExtensionId, null);
                    }

                    var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(_settings.MouseGestureTriggerMode);
                    var gesture = new LocalExtensionMouseGestureManifest
                    {
                        Trigger = string.IsNullOrWhiteSpace(runtimeTrigger) ? "right-drag" : runtimeTrigger,
                        Sequence = item.Sequence,
                        Sign = item.DisplayName,
                        Data = item.Data,
                        MinDistance = item.MinDistance > 0 ? item.MinDistance : 30
                    };
                    var result = await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(dlg.SelectedExtension.ExtensionId, gesture);
                    await HandleMouseGestureUpdateResultAsync(result);
                }

                // 2. 更新应用白名单与黑名单绑定列表
                _settings.MouseGestureAppBindings.RemoveAll(x =>
                    string.Equals(MouseGestureNaming.NormalizeSequence(x.Sequence), MouseGestureNaming.NormalizeSequence(item.Sequence), StringComparison.OrdinalIgnoreCase));

                foreach (var app in dlg.SelectedWhitelistApps)
                {
                    _settings.MouseGestureAppBindings.Add(new MouseGestureAppBinding
                    {
                        AppPath = app.AppPath,
                        AppName = app.AppName,
                        Sequence = item.Sequence,
                        ExtensionId = dlg.SelectedExtension?.ExtensionId ?? item.ExtensionId,
                        IsBlacklist = false
                    });
                }

                foreach (var app in dlg.SelectedBlacklistApps)
                {
                    _settings.MouseGestureAppBindings.Add(new MouseGestureAppBinding
                    {
                        AppPath = app.AppPath,
                        AppName = app.AppName,
                        Sequence = item.Sequence,
                        ExtensionId = dlg.SelectedExtension?.ExtensionId ?? item.ExtensionId,
                        IsBlacklist = true
                    });
                }

                _settings = _settingsPersistence.Save(_settings);
                _mainWindow.ReloadMouseGestureRegistrations();
                RefreshMouseGestureManagement();

                var tagList = new List<string>();
                if (dlg.SelectedWhitelistApps.Count > 0) tagList.Add($"限定 {dlg.SelectedWhitelistApps.Count} 个应用生效");
                else tagList.Add("全局所有应用生效");
                if (dlg.SelectedBlacklistApps.Count > 0) tagList.Add($"禁用 {dlg.SelectedBlacklistApps.Count} 个应用");

                var appSummary = "（" + string.Join("，", tagList) + "）";
                var actionName = dlg.SelectedExtension?.Label ?? (dlg.SelectedWhitelistApps.Count > 0 ? dlg.SelectedWhitelistApps[0].AppName : item.DisplayName);
                SyncStatusText = $"手势 [{item.DisplayName}] 已更新至 [{actionName}] {appSummary}";
            }
        }
    }

    private void BoundGestureCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ChangeBoundGestureButton_Click(sender, e);
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

        _settings = _settingsPersistence.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged("mouse-gesture-app-bound", refreshYanmOverlay: false);
        _mainWindow.ReloadMouseGestureRegistrations();
        RefreshMouseGestureManagement();
        SyncStatusText = $"已将手势 [{item.DisplayName}] 成功绑定到应用 [{item.SelectedApp.AppName}]！";
    }

    private async void QuickBindOpenDialogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item }) return;

        var triggerLabel = BuildGestureTriggerLabel();
        var dlg = new MouseGestureBindingDialog(
            item.Sequence,
            item.DisplayName,
            item.Description,
            triggerLabel,
            item.Data,
            item.AssignedExtensionId,
            item.BoundWhitelistAppPaths,
            item.BoundBlacklistAppPaths,
            MouseGestureExtensionOptions,
            MouseGestureAppOptions)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            if (dlg.WasUnbound)
            {
                await UnbindGestureAsync(item.Sequence);
                RefreshMouseGestureManagement();
                SyncStatusText = $"已解绑手势 [{item.DisplayName}]。";
            }
            else if (dlg.WasSaved)
            {
                // 1. 如果选择了小程序，绑定小程序手势
                if (dlg.SelectedExtension != null)
                {
                    var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(_settings.MouseGestureTriggerMode);
                    var gesture = new LocalExtensionMouseGestureManifest
                    {
                        Trigger = string.IsNullOrWhiteSpace(runtimeTrigger) ? "right-drag" : runtimeTrigger,
                        Sequence = item.Sequence,
                        Sign = item.DisplayName,
                        Data = item.Data,
                        MinDistance = 30
                    };
                    var result = await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(dlg.SelectedExtension.ExtensionId, gesture);
                    await HandleMouseGestureUpdateResultAsync(result);
                }

                // 2. 更新应用白名单与黑名单绑定列表
                _settings.MouseGestureAppBindings.RemoveAll(x =>
                    string.Equals(MouseGestureNaming.NormalizeSequence(x.Sequence), MouseGestureNaming.NormalizeSequence(item.Sequence), StringComparison.OrdinalIgnoreCase));

                foreach (var app in dlg.SelectedWhitelistApps)
                {
                    _settings.MouseGestureAppBindings.Add(new MouseGestureAppBinding
                    {
                        AppPath = app.AppPath,
                        AppName = app.AppName,
                        Sequence = item.Sequence,
                        ExtensionId = dlg.SelectedExtension?.ExtensionId,
                        IsBlacklist = false
                    });
                }

                foreach (var app in dlg.SelectedBlacklistApps)
                {
                    _settings.MouseGestureAppBindings.Add(new MouseGestureAppBinding
                    {
                        AppPath = app.AppPath,
                        AppName = app.AppName,
                        Sequence = item.Sequence,
                        ExtensionId = dlg.SelectedExtension?.ExtensionId,
                        IsBlacklist = true
                    });
                }

                _settings = _settingsPersistence.Save(_settings);
                _mainWindow.ReloadMouseGestureRegistrations();
                RefreshMouseGestureManagement();

                var tagList = new List<string>();
                if (dlg.SelectedWhitelistApps.Count > 0) tagList.Add($"限定 {dlg.SelectedWhitelistApps.Count} 个应用生效");
                else tagList.Add("全局所有应用生效");
                if (dlg.SelectedBlacklistApps.Count > 0) tagList.Add($"禁用 {dlg.SelectedBlacklistApps.Count} 个应用");

                var appSummary = "（" + string.Join("，", tagList) + "）";
                var actionName = dlg.SelectedExtension?.Label ?? (dlg.SelectedWhitelistApps.Count > 0 ? dlg.SelectedWhitelistApps[0].AppName : item.DisplayName);
                SyncStatusText = $"手势 [{item.DisplayName}] 已配置至 [{actionName}] {appSummary}";
            }
        }
    }

    private async void QuickBindUnbindButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item }) return;
        await UnbindGestureAsync(item.Sequence);
        RefreshMouseGestureManagement();
        SyncStatusText = $"已解绑手势 [{item.DisplayName}]。";
    }

    private async Task UnbindGestureAsync(string sequence)
    {
        // 1. 解除小程序手势绑定
        foreach (var entry in LocalExtensionCatalog.LoadEntries())
        {
            if (entry.Manifest.MouseGesture != null &&
                string.Equals(entry.Manifest.MouseGesture.Sequence, sequence, StringComparison.OrdinalIgnoreCase))
            {
                await _mainWindow.UpdateExtensionMouseGestureFromSettingsAsync(entry.Manifest.Id, null);
            }
        }

        // 2. 解除应用手势绑定
        _settings.MouseGestureAppBindings.RemoveAll(x =>
            string.Equals(x.Sequence, sequence, StringComparison.OrdinalIgnoreCase));
        _settings = _settingsPersistence.Save(_settings);
    }

    private async void BindCommonMouseGestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item } ||
            item.SelectedExtension == null ||
            string.IsNullOrWhiteSpace(item.SelectedExtension.ExtensionId))
        {
            SyncStatusText = "请选择一个想要绑定的小程序。";
            return;
        }

        var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(_settings.MouseGestureTriggerMode);
        var gesture = new LocalExtensionMouseGestureManifest
        {
            Trigger = string.IsNullOrWhiteSpace(runtimeTrigger) ? "right-drag" : runtimeTrigger,
            Sequence = item.Sequence,
            Sign = item.DisplayName,
            Data = item.Data,
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

    private void AddNewMouseGestureButton_Click(object sender, RoutedEventArgs e)
    {
        var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(MouseGestureTriggerMode);
        var trigger = string.IsNullOrWhiteSpace(runtimeTrigger) ? "right-drag" : runtimeTrigger;

        var recorder = new MouseGestureRecorderWindow(trigger, initialSequence: null)
        {
            Owner = this
        };

        if (recorder.ShowDialog() == true && recorder.WasAccepted && (!string.IsNullOrWhiteSpace(recorder.ResultSequence) || recorder.ResultTemplateData != null))
        {
            var seq = recorder.ResultSequence;
            var sign = recorder.ResultSign;
            var data = recorder.ResultTemplateData;

            // 检查常用手势列表中是否已有该手势
            var existing = MouseGestureQuickBindItems.FirstOrDefault(x => string.Equals(x.Sequence, seq, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.IsExtensionPopupOpen = true;
                SyncStatusText = $"已录制手势 [{existing.DisplayName}]，请在列表中选择小程序完成绑定。";
            }
            else
            {
                var displayName = string.IsNullOrWhiteSpace(sign) ? MouseGestureNaming.GetDisplayName(seq) : sign;
                var newItem = new MouseGestureQuickBindItem(seq, displayName, "自定义手势", assignedTitle: null, data: data);
                MouseGestureQuickBindItems.Insert(0, newItem);
                newItem.IsExtensionPopupOpen = true;
                SyncStatusText = $"已录制新手势 [{displayName}]，请选择小程序完成绑定。";
            }
        }
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
        _settingsPersistence.Reset(_settings);
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
                    command.Category.Contains("网页搜索", StringComparison.OrdinalIgnoreCase) ? "网页搜索小程序" : "本地小程序",
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
                System.Windows.MessageBox.Show(this, $"无法打开备份详情： {ex.Message}", "出错啦", MessageBoxButton.OK, MessageBoxImage.Error);
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
