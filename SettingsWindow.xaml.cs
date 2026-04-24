using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindow _mainWindow;
    private AppSettings _settings;
    private SettingsNavigationItem? _selectedNavigation;
    private string _accountTitle = "未登录";
    private string _accountSubtitle = "点击左上角账户卡片登录或切换账号。";
    private string _accountInitial = "燕";
    private string _localExtensionSummary = "正在统计...";
    private string _launcherHotkey = "Ctrl+Shift+Space";
    private string _syncStatusText = "同步服务状态未知。";

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        NavigationItems =
        [
            new SettingsNavigationItem("general", "M12,15.5A3.5,3.5 0 1,1 12,8.5A3.5,3.5 0 1,1 12,15.5M19.4,15L21.7,13.5L19.9,8.9L17.3,10L15.7,8.6L16.1,5.8L11.2,5.8L10.8,8.6L9.2,10L6.6,8.9L4.7,13.5L7,15L7,17L4.7,18.5L6.6,23.1L9.2,22L10.8,23.4L11.2,26.2L16.1,26.2L16.5,23.4L18.1,22L20.7,23.1L22.6,18.5L20.3,17Z", "常规", "#FF3B82F6"),
            new SettingsNavigationItem("sync", "M12,6V9L16,5L12,1V4A8,8 0 0,0 4,12C4,13.43 4.37,14.77 5.03,15.94L6.47,14.5C6.17,13.73 6,12.89 6,12A6,6 0 0,1 12,6M18.97,8.06L17.53,9.5C17.83,10.27 18,11.11 18,12A6,6 0 0,1 12,18V15L8,19L12,23V20A8,8 0 0,0 20,12C20,10.57 19.63,9.23 18.97,8.06Z", "同步", "#FF22C55E"),
            new SettingsNavigationItem("extensions", "M20.5,11H19V7C19,5.89 18.11,5 17,5H13V3.5A1.5,1.5 0 0,0 11.5,2A1.5,1.5 0 0,0 10,3.5V5H6C4.89,5 4,5.89 4,7V11H2.5A1.5,1.5 0 0,0 1,12.5A1.5,1.5 0 0,0 2.5,14H4V18C4,19.11 4.89,20 6,20H10V21.5A1.5,1.5 0 0,0 11.5,23A1.5,1.5 0 0,0 13,21.5V20H17C18.11,20 19,19.11 19,18V14H20.5A1.5,1.5 0 0,0 22,12.5A1.5,1.5 0 0,0 20.5,11Z", "扩展", "#FFF97316"),
            new SettingsNavigationItem("shortcuts", "M7,7H17V9H7V7M7,11H13V13H7V11M15,11H17V13H15V11M7,15H11V17H7V15M13,15H17V17H13V15M5,3H19A2,2 0 0,1 21,5V19A2,2 0 0,1 19,21H5A2,2 0 0,1 3,19V5A2,2 0 0,1 5,3Z", "快捷键", "#FFEAB308"),
            new SettingsNavigationItem("quickpanel", "M4,4H20V20H4V4M6,6V18H18V6H6M8,8H10V10H8V8M14,8H16V10H14V8M8,14H10V16H8V14M14,14H16V16H14V14Z", "快捷面板", "#FFEC4899"),
            new SettingsNavigationItem("about", "M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z", "关于", "#FF8B5CF6")
        ];
        _selectedNavigation = NavigationItems.First();
        LaunchAtStartup = _settings.LaunchAtStartup;
        RefreshCloudOnStartup = _settings.RefreshCloudOnStartup;
        CloseToTray = _settings.CloseToTray;
        LauncherHotkey = _settings.LauncherHotkey;
        BaseUrl = _mainWindow.SyncBaseUrl;
        ExtensionsRootPath = LocalExtensionCatalog.CatalogRootPath;
        AppVersionText = $"燕子 · {GetType().Assembly.GetName().Version}";
        ShortcutItems = new ObservableCollection<SettingsShortcutItem>();
        DataContext = this;
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        LoadLogoImage();
    }

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; }

    public ObservableCollection<SettingsShortcutItem> ShortcutItems { get; }

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
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSectionTitle));
            OnPropertyChanged(nameof(SelectedSectionDescription));
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsSyncSelected));
            OnPropertyChanged(nameof(IsExtensionsSelected));
            OnPropertyChanged(nameof(IsShortcutsSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
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
            AboutLogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/logo.png", UriKind.Absolute));
        }
        catch
        {
            // Ignore logo load failures so settings can still open in published builds.
        }
    }

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

    public string SelectedSectionTitle => SelectedNavigation?.Title ?? "Settings";

    public string SelectedSectionDescription => SelectedNavigation?.Key switch
    {
        "general" => "控制燕子(Swallow)的基础行为，包括启动同步和托盘停驻策略。",
        "sync" => "管理云账号状态、同步入口和当前服务端连接信息。",
        "extensions" => "查看本地扩展目录和当前机器已发现的扩展数量。",
        "shortcuts" => "查看和管理主程序与扩展的全局快捷键。",
        "quickpanel" => "控制悬浮网格的操作面板，包括触发逻辑和槽位预设。",
        "about" => "查看当前版本与这套设置窗口的结构定位。",
        _ => "燕子设置"
    };

    public bool IsGeneralSelected => SelectedNavigation?.Key == "general";

    public bool IsSyncSelected => SelectedNavigation?.Key == "sync";

    public bool IsExtensionsSelected => SelectedNavigation?.Key == "extensions";

    public bool IsShortcutsSelected => SelectedNavigation?.Key == "shortcuts";

    public bool IsQuickPanelSelected => SelectedNavigation?.Key == "quickpanel";

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
        RefreshAccountSummary();
        RefreshExtensionSummary();
        RefreshShortcutItems();
        SyncStatusText = $"当前同步服务：{BaseUrl}";
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        _settings = AppSettingsStore.Load();
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(RefreshCloudOnStartup));
        OnPropertyChanged(nameof(CloseToTray));
        LauncherHotkey = _settings.LauncherHotkey;
        RefreshAccountSummary();
        RefreshExtensionSummary();
        RefreshShortcutItems();
    }

    private void SaveSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        StartupRegistrationService.Apply(_settings.LaunchAtStartup);
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

    private void OpenExtensionsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ExtensionsRootPath,
            UseShellExecute = true
        });
    }

    private void RefreshExtensionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshExtensionSummary();
        RefreshShortcutItems();
    }

    private void EditLauncherHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "设置主程序快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。",
            LauncherHotkey)
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
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ResetLauncherHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow.TryUpdateLauncherHotkey("Ctrl+Shift+Space", out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
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

    private async Task SignInAsync()
    {
        var ok = await _mainWindow.PromptLoginFromSettingsAsync();
        RefreshAccountSummary();
        if (ok)
        {
            await _mainWindow.RefreshCloudFromSettingsAsync();
        }
    }

    private async Task SignOutAsync()
    {
        _mainWindow.SignOutFromSettings();
        RefreshAccountSummary();
        await Task.CompletedTask;
    }

    private async Task RefreshCloudAsync()
    {
        await _mainWindow.RefreshCloudFromSettingsAsync();
        RefreshAccountSummary();
        SyncStatusText = "已刷新云状态。";
    }

    private void RefreshAccountSummary()
    {
        var session = SyncSessionStore.Load();
        var credential = SecureCredentialStore.Load();
        if (session != null && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            AccountTitle = session.Username;
            AccountSubtitle = $"已登录 Cloud · 用户 ID {session.UserId}";
            AccountInitial = session.Username[..1].ToUpperInvariant();
            return;
        }

        if (!string.IsNullOrWhiteSpace(credential?.Username))
        {
            AccountTitle = credential.Username;
            AccountSubtitle = "本机已保存登录信息，下一次同步时会自动登录。";
            AccountInitial = credential.Username[..1].ToUpperInvariant();
            return;
        }

        AccountTitle = "未登录";
        AccountSubtitle = "点击左上角账户卡片登录或切换账号。";
        AccountInitial = "燕";
    }

    private void RefreshExtensionSummary()
    {
        var count = LocalExtensionCatalog.LoadCommands().Count;
        LocalExtensionSummary = $"当前机器已发现 {count} 个本地扩展。";
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SettingsNavigationItem(string Key, string Glyph, string Title, string Accent);

public sealed record SettingsShortcutItem(string ExtensionId, string Title, string Category, string? Shortcut)
{
    public string ShortcutValue => Shortcut ?? string.Empty;

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? "未设置" : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);
}
