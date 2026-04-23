using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
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

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        NavigationItems =
        [
            new SettingsNavigationItem("general", "G", "General", "#FF3B82F6"),
            new SettingsNavigationItem("sync", "S", "Sync", "#FF22C55E"),
            new SettingsNavigationItem("extensions", "E", "Extensions", "#FFF97316"),
            new SettingsNavigationItem("about", "A", "About", "#FF8B5CF6")
        ];
        _selectedNavigation = NavigationItems.First();
        RefreshCloudOnStartup = _settings.RefreshCloudOnStartup;
        CloseToTray = _settings.CloseToTray;
        BaseUrl = _mainWindow.SyncBaseUrl;
        ExtensionsRootPath = LocalExtensionCatalog.CatalogRootPath;
        AppVersionText = $"燕子 · {GetType().Assembly.GetName().Version}";
        DataContext = this;
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        LoadLogoImage();
    }

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; }

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

    private void LoadLogoImage()
    {
        string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        if (!File.Exists(logoPath))
        {
            return;
        }

        try
        {
            AboutLogoImage.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
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
        "about" => "查看当前版本与这套设置窗口的结构定位。",
        _ => "燕子设置"
    };

    public bool IsGeneralSelected => SelectedNavigation?.Key == "general";

    public bool IsSyncSelected => SelectedNavigation?.Key == "sync";

    public bool IsExtensionsSelected => SelectedNavigation?.Key == "extensions";

    public bool IsAboutSelected => SelectedNavigation?.Key == "about";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAccountSummary();
        RefreshExtensionSummary();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        _settings = AppSettingsStore.Load();
        OnPropertyChanged(nameof(RefreshCloudOnStartup));
        OnPropertyChanged(nameof(CloseToTray));
        RefreshAccountSummary();
        RefreshExtensionSummary();
    }

    private void SaveSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SettingsNavigationItem(string Key, string Glyph, string Title, string Accent);
