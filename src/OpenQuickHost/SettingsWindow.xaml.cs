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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenQuickHost.Sync;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private const string RadialSimulatedKeyPrefix = "keysim::";
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
        InitializeComponent();
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
            new SettingsNavigationItem("about", "mdi:about", "关于", "#FF3B82F6")
        ];
        _selectedNavigation = NavigationItems.First();
        LaunchAtStartup = _settings.LaunchAtStartup;
        RefreshCloudOnStartup = _settings.RefreshCloudOnStartup;
        CloseToTray = _settings.CloseToTray;
        EnableAutoUpdate = _settings.EnableAutoUpdate;
        EnableWindowSnapAssist = _settings.EnableWindowSnapAssist;
        LauncherHotkey = _settings.LauncherHotkey;
        LoadPersonalSyncStateFromSettings();
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSystemPrompt = _settings.AiSystemPrompt;

        _settings.AiServiceProviders ??= [];
        foreach (var provider in _settings.AiServiceProviders)
        {
            var vm = new SettingsAiProviderVM(provider);
            if (provider.Models != null)
            {
                foreach (var m in provider.Models)
                {
                    vm.Models.Add(m);
                }
            }
            _aiServiceProvidersList.Add(vm);
        }
        SelectedServiceProvider = _aiServiceProvidersList.FirstOrDefault(p => p.Id == _settings.ActiveServiceProviderId)
                                 ?? _aiServiceProvidersList.FirstOrDefault();


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
        UpdateBackupStatusText();
        DataContext = this;
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
        HostAssets.AppendLog("SettingsWindow: OnSourceInitialized called. Updating DWM Theme.");
        App.UpdateWindowDwmTheme(this);
    }

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; }

    public ObservableCollection<SettingsShortcutItem> ShortcutItems { get; }

    public ObservableCollection<SettingsExtensionItem> ExtensionItems { get; }

    public ObservableCollection<SettingsRecycleBinItem> RecycleBinItems { get; }

    public ObservableCollection<PersonalSyncCommitItem> PersonalSyncCommitItems { get; }

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
            OnPropertyChanged(nameof(IsAboutSelected));
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
                _ = RefreshPersonalSyncCommitsAsync();
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
        get => string.IsNullOrWhiteSpace(_settings.WindowSnapAssistHotkey) ? "未设置" : _settings.WindowSnapAssistHotkey;
        private set
        {
            _settings = _settings with { WindowSnapAssistHotkey = value };
            OnPropertyChanged();
        }
    }

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
        }
    }

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
        }
    }

    private void SaveWanPushUuidButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        ShowToast("广域网推送配置已保存");
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

    public string RadialBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.RadialMenu.BlacklistedProcesses ?? []);
        set
        {
            _settings.RadialMenu.BlacklistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
        }
    }

    public string RadialWhitelistedProcessesText
    {
        get => string.Join(", ", _settings.RadialMenu.WhitelistedProcesses ?? []);
        set
        {
            _settings.RadialMenu.WhitelistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
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

    public string YanmBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.Yanm.BlacklistedProcesses ?? []);
        set
        {
            _settings.Yanm.BlacklistedProcesses = ParseProcessList(value);
            OnPropertyChanged();
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

    public bool IsNormalSettingsVisible => !IsAiSelected;

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

        _settings = AppSettingsStore.Load();
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.Yanm ??= new YanmSettings();
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(RefreshCloudOnStartup));
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

        _settings = _settings with
        {
            SettingsWindowLeft = Left,
            SettingsWindowTop = Top,
            SettingsWindowWidth = Width,
            SettingsWindowHeight = Height
        };
        AppSettingsStore.Save(_settings);
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
    }

    private void SaveSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        StartupRegistrationService.Apply(_settings.LaunchAtStartup);
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

        var card = FindName(cardName) as System.Windows.Controls.Border;
        var highlight = FindName(highlightName) as System.Windows.Shapes.Shape;

        if (card == null)
        {
            HostAssets.AppendLog($"Card not found: {cardName}");
            return;
        }

        // Update card border and background based on target
        var (borderBrush, background, highlightFill) = target switch
        {
            "Panel" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x3B, 0x82, 0xF6)), // Blue border
                        new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0D, 0x3B, 0x82, 0xF6)), // Blue background
                        new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6))), // Blue highlight
            "Radial" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xA8, 0x55, 0xF7)), // Purple border
                         new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0D, 0xA8, 0x55, 0xF7)), // Purple background
                         new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7))), // Purple highlight
            "Yanm" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x10, 0xB9, 0x81)), // Green border
                       new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0D, 0x10, 0xB9, 0x81)), // Green background
                       new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x10, 0xB9, 0x81))), // Green highlight
            "Gesture" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0xFB, 0x92, 0x3C)),
                          new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0xFB, 0x92, 0x3C)),
                          new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFB, 0x92, 0x3C))),
            "WindowSnap" => (new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xA0, 0x38, 0xBD, 0xF8)),
                             new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x38, 0xBD, 0xF8)),
                             new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8))),
            _ => (null, null, null)
        };

        card.BorderBrush = borderBrush;
        card.Background = background;

        if (highlight != null)
        {
            highlight.Fill = highlightFill;
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

    private void SaveWebDavSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _personalSyncSecrets.GitHubToken = GitHubTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GiteeToken = GiteeTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GitLabToken = GitLabTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GiteaToken = GiteaTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.S3SecretAccessKey = S3SecretAccessKeyBox?.Password ?? string.Empty;
        _personalSyncSecrets.WebDavPassword = WebDavPasswordBox?.Password ?? string.Empty;
        _mainWindow.SavePersonalSyncSettings(ClonePersonalSyncSettings(_personalSyncSettings), ClonePersonalSyncSecrets(_personalSyncSecrets));
        _settings = AppSettingsStore.Load();
        RefreshWebDavSummary();
        SyncStatusText = "个人同步配置已保存。";
        RefreshSyncActivityLog();
    }

    private void SaveAiSettingsButton_Click(object sender, RoutedEventArgs e)
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
        _mainWindow.OnAiSettingsChanged();

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

        // 7. Toast
        ShowToast("AI 配置已保存");
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
        EnvironmentStatusText = "已添加一行环境变量，填写后点击保存。";
    }

    private void RemoveEnvironmentVariableButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EnvironmentVariableEditorItem item })
        {
            EnvironmentVariables.Remove(item);
            EnvironmentStatusText = "已移除一行环境变量，点击保存后生效。";
        }
    }

    private void SaveEnvironmentVariablesButton_Click(object sender, RoutedEventArgs e)
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
        EnvironmentVariables.Clear();
        foreach (var variable in AppEnvironmentVariableStore.Load())
        {
            EnvironmentVariables.Add(new EnvironmentVariableEditorItem(variable.Name, variable.Value, variable.Description));
        }

        _settings = AppSettingsStore.Load();
        EnvironmentStatusText = BuildEnvironmentSummary();
        ShowToast("环境变量已保存");
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
            WebDavStatusText = result.message;
            await RefreshExtensionsFromDiskAsync();
            RefreshSyncActivityLog();
            await RefreshPersonalSyncCommitsAsync();
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
        if (SavePersonalSyncButton != null) SavePersonalSyncButton.IsEnabled = enabled;
        if (TestPersonalSyncButton != null) TestPersonalSyncButton.IsEnabled = enabled;
        if (ClearPersonalSyncButton != null) ClearPersonalSyncButton.IsEnabled = enabled;
        if (SyncPersonalSyncButton != null) SyncPersonalSyncButton.IsEnabled = enabled;
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
            if (LastSyncStatusTextBlock != null)
            {
                LastSyncStatusTextBlock.Text = "上次同步: 成功";
                LastSyncStatusTextBlock.ToolTip = "同步已成功完成 (WebDav/S3 等其他模式)";
            }
            return;
        }

        try
        {
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

            if (LastSyncStatusTextBlock != null)
            {
                if (PersonalSyncCommitItems.Count > 0)
                {
                    var latest = PersonalSyncCommitItems[0];
                    LastSyncStatusTextBlock.Text = $"上次同步: {latest.Message}";
                    LastSyncStatusTextBlock.ToolTip = $"最新提交: {latest.Message}\n作者: {latest.Author}\n时间: {latest.LocalTimeLabel}\nSHA: {latest.ShortSha}";
                }
                else
                {
                    LastSyncStatusTextBlock.Text = "上次同步: 成功";
                    LastSyncStatusTextBlock.ToolTip = "暂无提交记录";
                }
            }
        }
        catch (Exception ex)
        {
            PersonalSyncCommitItems.Clear();
            PersonalSyncCommitStatusText = $"读取提交记录失败：{ex.Message}";
            if (LastSyncStatusTextBlock != null)
            {
                LastSyncStatusTextBlock.Text = "上次同步: 失败";
                LastSyncStatusTextBlock.ToolTip = $"读取记录失败：{ex.Message}";
            }
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
            var service = new PersonalSyncService(AppSettingsStore.Load());
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

    private void ExtensionCardsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
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
            var publishedMap = await _mainWindow.GetOwnedPublishedExtensionsForSettingsAsync();
            HostAssets.AppendLog($"Settings extensions refresh cloud publish map count={publishedMap.Count}");
            var data = await Task.Run(() =>
            {
                var backgroundStartedAt = Stopwatch.StartNew();
                LocalExtensionCatalog.EnsureSampleExtension();
                var entries = LocalExtensionCatalog.LoadEntries()
                    .ToList();
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
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
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

    private async void BindCommonMouseGestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MouseGestureQuickBindItem item } ||
            item.SelectedExtension == null)
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
        var scrollOffset = MainContentScrollViewer?.VerticalOffset ?? 0;

        var isOpen = SelectedExtensionItem != null;
        ExtensionDetailColumn.Width = isOpen ? new GridLength(380) : new GridLength(0);
        ExtensionDetailPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        ScheduleExtensionCardWidthUpdate();

        // Restore scroll position after layout update
        if (MainContentScrollViewer != null)
        {
            Dispatcher.BeginInvoke(new Action(() => MainContentScrollViewer.ScrollToVerticalOffset(scrollOffset)), DispatcherPriority.Loaded);
        }
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

        var viewportWidth = ExtensionCardsScrollViewer.ViewportWidth;
        if (double.IsNaN(viewportWidth) || viewportWidth <= 0 || double.IsInfinity(viewportWidth))
        {
            viewportWidth = ExtensionCardsScrollViewer.ActualWidth;
        }

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

    private void ApplySettingsSearch(string query)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var target = NavigationItems.FirstOrDefault(item => SettingsSearchMatches(item.Key, query));
        if (target != null)
        {
            SelectedNavigation = target;
        }
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

    private async void PickYanmWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await PickProcessAndAddAsync("燕幕白名单", process =>
        {
            _settings.Yanm.WhitelistedProcesses = AddProcessToList(_settings.Yanm.WhitelistedProcesses, process);
            OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
            SaveYanmSettings(requireCustomShortcut: false);
        });
    }

    private async void PickYanmBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await PickProcessAndAddAsync("燕幕黑名单", process =>
        {
            _settings.Yanm.BlacklistedProcesses = AddProcessToList(_settings.Yanm.BlacklistedProcesses, process);
            OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
            SaveYanmSettings(requireCustomShortcut: false);
        });
    }

    private async void PickRadialWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await PickProcessAndAddAsync("燕环白名单", process =>
        {
            _settings.RadialMenu.WhitelistedProcesses = AddProcessToList(_settings.RadialMenu.WhitelistedProcesses, process);
            OnPropertyChanged(nameof(RadialWhitelistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private async void PickRadialBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await PickProcessAndAddAsync("燕环黑名单", process =>
        {
            _settings.RadialMenu.BlacklistedProcesses = AddProcessToList(_settings.RadialMenu.BlacklistedProcesses, process);
            OnPropertyChanged(nameof(RadialBlacklistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private async void PickYarnSelectWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await PickProcessAndAddAsync("燕选白名单", process =>
        {
            _settings.YarnSelect.WhitelistedProcesses = AddProcessToList(_settings.YarnSelect.WhitelistedProcesses, process);
            OnPropertyChanged(nameof(YarnSelectWhitelistedProcessesText));
            SaveYarnSelectSettings();
        });
    }

    private async void PickYarnSelectBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        await PickProcessAndAddAsync("燕选黑名单", process =>
        {
            _settings.YarnSelect.BlacklistedProcesses = AddProcessToList(_settings.YarnSelect.BlacklistedProcesses, process);
            OnPropertyChanged(nameof(YarnSelectBlacklistedProcessesText));
            SaveYarnSelectSettings();
        });
    }

    private async Task PickProcessAndAddAsync(string targetName, Action<string> addProcess)
    {
        SyncStatusText = $"{targetName}：设置窗口将隐藏，请在 2.5 秒内切到目标窗口。";
        HostAssets.AppendLog($"Settings: process picker started for {targetName}.");
        Hide();
        await Task.Delay(2500);
        var processName = GetForegroundProcessName();
        Show();
        Activate();

        if (string.IsNullOrWhiteSpace(processName))
        {
            SyncStatusText = $"{targetName}：没有获取到目标进程。";
            return;
        }

        addProcess(processName);
        SyncStatusText = $"{targetName} 已添加进程：{processName}";
        HostAssets.AppendLog($"Settings: process picker added process={processName}, target={targetName}.");
    }

    private static List<string> AddProcessToList(IEnumerable<string>? source, string processName) =>
        (source ?? [])
        .Append(processName)
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Select(static item => item.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
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
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }

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
            foreach (var page in _settings.RadialMenu.Pages)
            {
                var isAppPage = !string.IsNullOrEmpty(page.ContextProcessName);
                var icon = isAppPage ? GetProcessIcon(page.ContextProcessName!) : null;
                RadialMenuPages.Add(new RadialMenuPageEditorItem(page.Id, page.Name, icon, isAppPage));
            }

            RadialMenuChildPageOptions.Clear();
            RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(string.Empty, "不进入子环", null, false));
            foreach (var page in _settings.RadialMenu.Pages)
            {
                RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(page.Id, page.Name, null, false));
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
                RadialMenuSlots.Add(new RadialMenuSlotEditorItem(
                    index,
                    selectedPage.Slots.ElementAtOrDefault(index) ?? string.Empty,
                    selectedPage.SlotTitles.ElementAtOrDefault(index) ?? string.Empty,
                    childPageId,
                    ResolveRadialExtensionTitle(
                        selectedPage.Slots.ElementAtOrDefault(index),
                        selectedPage.SlotTitles.ElementAtOrDefault(index)),
                    ResolveRadialChildPageTitle(childPageId),
                    center + Math.Cos(angle) * radius - (isOuter ? 31 : 38),
                    center + Math.Sin(angle) * radius - (isOuter ? 25 : 30),
                    isOuter,
                    BuildRadialSectorGeometry(center, center, isOuter ? 113.0 : 35.0, isOuter ? 180.0 : 113.0, startAngleDegrees, step),
                    runtimeCommand?.IconSource,
                    runtimeCommand?.VectorIcon,
                    runtimeCommand?.AccentBrush ?? System.Windows.Media.Brushes.Transparent,
                    runtimeCommand?.DisplayGlyph ?? (string.IsNullOrWhiteSpace(childPageId) ? string.Empty : "环")));
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

        var removedId = _settings.RadialMenu.SelectedPageId;
        _settings.RadialMenu.Pages.RemoveAll(page => page.Id.Equals(removedId, StringComparison.OrdinalIgnoreCase));
        foreach (var page in _settings.RadialMenu.Pages)
        {
            page.ChildPageIds = (page.ChildPageIds ?? [])
                .Select(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase) ? null : id)
                .ToList();
        }
        _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
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
            dialogResult = dialog.ShowDialog();
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

        SelectRadialMenuSlot(slot);
        slot.ExtensionId = string.Empty;
        slot.DisplayTitle = string.Empty;
        slot.ExtensionTitle = ResolveRadialExtensionTitle(string.Empty);
        UpdateRadialSlotPresentation(slot);
        SaveQuickPanelTriggerSettings();
    }

    private void RadialSlotAddChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        CreateRadialChildPageForSlot(slot, GetNextRadialChildPageName());
    }

    private void CreateRadialChildPageForSlot(RadialMenuSlotEditorItem slot, string pageName)
    {
        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = pageName.Trim()
        };
        _settings.RadialMenu.Pages.Add(page);
        slot.ChildPageId = page.Id;
        slot.ChildPageTitle = ResolveRadialChildPageTitle(page.Id);
        UpdateRadialSlotPresentation(slot);
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void OpenRadialSlotPicker(RadialMenuSlotEditorItem slot)
    {
        var picker = new RadialSlotPickerWindow(
            keyword => _mainWindow.GetRadialMenuCommandCandidates(keyword),
            allowAddChildPage: !slot.HasChildPageTitle,
            createExtension: owner => _mainWindow.OpenAddExtensionForSlot(owner))
        {
            Owner = this
        };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        if (picker.SelectedAction == RadialSlotPickerWindow.PickerAction.AddChildPage)
        {
            CreateRadialChildPageForSlot(slot, GetNextRadialChildPageName());
            return;
        }

        if (picker.SelectedCommand == null)
        {
            return;
        }

        ApplyRadialMenuCommandToSlot(slot, new YarnSelectExtensionOption(picker.SelectedCommand));
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
            slot.AccentBrush = System.Windows.Media.Brushes.Transparent;
            slot.DisplayGlyph = "环";
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

        SelectRadialMenuSlot(slot);
        var removedId = slot.ChildPageId;
        slot.ChildPageId = string.Empty;
        slot.ChildPageTitle = ResolveRadialChildPageTitle(string.Empty);
        if (!string.IsNullOrWhiteSpace(removedId) &&
            _settings.RadialMenu?.Pages?.Count > 1)
        {
            _settings.RadialMenu.Pages.RemoveAll(page => page.Id.Equals(removedId, StringComparison.OrdinalIgnoreCase));
            foreach (var page in _settings.RadialMenu.Pages)
            {
                page.ChildPageIds = (page.ChildPageIds ?? [])
                    .Select(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase) ? null : id)
                    .ToList();
            }
        }

        SaveQuickPanelTriggerSettings();
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

    private void AddYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var item = new YarnSelectRuleItem(new YarnSelectRuleSettings
        {
            TriggerKey = "A",
            ActionType = YarnSelectActionTypes.RunExtension,
            Description = "新燕选规则"
        });
        ApplyYarnSelectExtensionSelection(item);
        YarnSelectRules.Add(item);
        OnPropertyChanged(nameof(YarnSelectSummary));
    }

    private void DeleteYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            return;
        }

        YarnSelectRules.Remove(item);
        SaveYarnSelectSettings();
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
        if (keyword.Length == 0)
        {
            item.FilteredExtensionOptions = [];
            return;
        }

        item.FilteredExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>(
            YarnSelectExtensionOptions
                .Where(option =>
                    option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(8));
    }

    private void YarnSelectExtensionSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
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
        }
    }

    private void YarnSelectExtensionSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: YarnSelectRuleItem item } ||
            e.Key != Key.Down ||
            item.FilteredExtensionOptions.Count == 0)
        {
            return;
        }

        if (FindDescendantListBox(this, FilteredRadialMenuCommandOptions) is { } listBox)
        {
            listBox.SelectedIndex = 0;
            listBox.Focus();
            e.Handled = true;
        }
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
            item.FilteredExtensionOptions = [];
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
        item.FilteredExtensionOptions = [];
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

    public bool HasVectorIcon => VectorIcon != null;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);
}

public sealed class MouseGestureQuickBindItem : INotifyPropertyChanged
{
    private MouseGestureExtensionOption? _selectedExtension;

    public MouseGestureQuickBindItem(string sequence, string displayName, string description, string? assignedTitle)
    {
        Sequence = sequence;
        DisplayName = displayName;
        Description = description;
        AssignedTitle = assignedTitle ?? string.Empty;
        PreviewGeometry = MouseGesturePreviewGeometryFactory.Create(sequence, data: null);
    }

    public string Sequence { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string AssignedTitle { get; }

    public Geometry PreviewGeometry { get; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssignedTitle);

    public string StatusText => IsAssigned ? $"已被 {AssignedTitle} 使用" : "未绑定";

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

    public bool HasVectorIcon => VectorIcon != null;

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

    public bool HasVectorIcon => VectorIcon != null;

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

    public bool HasVectorIcon => VectorIcon != null;

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

public sealed record RadialMenuPageEditorItem(string Id, string Name, ImageSource? Icon = null, bool IsAppPage = false);

public sealed class YarnSelectRuleItem : INotifyPropertyChanged
{
    private bool _enabled;
    private string _triggerKey;
    private string _actionType;
    private string _extensionId;
    private string _extensionSearchText;
    private string _description;
    private ObservableCollection<YarnSelectExtensionOption> _filteredExtensionOptions = [];

    public YarnSelectRuleItem(YarnSelectRuleSettings rule)
    {
        _enabled = rule.Enabled;
        _triggerKey = rule.TriggerKey;
        _actionType = rule.ActionType;
        _extensionId = rule.ExtensionId;
        _extensionSearchText = string.Empty;
        _description = rule.Description;
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
        }
    }

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

    public bool HasVectorIcon => VectorIcon != null;

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

public sealed class PersonalSyncCommitItem
{
    public PersonalSyncCommitItem(string sha, string message, string author, DateTimeOffset committedAtUtc, string url)
    {
        Sha = sha;
        Message = string.IsNullOrWhiteSpace(message) ? "(无提交说明)" : message;
        Author = string.IsNullOrWhiteSpace(author) ? "未知作者" : author;
        CommittedAtUtc = committedAtUtc;
        Url = url;
    }

    public string Sha { get; }

    public string ShortSha => Sha.Length <= 8 ? Sha : Sha[..8];

    public string Message { get; }

    public string Author { get; }

    public DateTimeOffset CommittedAtUtc { get; }

    public string LocalTimeLabel => CommittedAtUtc.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.CurrentCulture);

    public string Url { get; }
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

