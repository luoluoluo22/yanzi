using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace OpenQuickHost;

public partial class App : WpfApplication
{
    private const string SingleInstanceAppId = "Yanzi.OpenQuickHost";
    private Forms.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;
    private RunningExtensionsWindow? _runningExtensionsWindow;
    private InputStateWindow? _inputStateWindow;
    private LocalAgentApiServer? _agentApiServer;
    private SingleInstanceService? _singleInstanceService;
    private bool _listenerServicesPaused;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        // 必须最先执行，拦截 Velopack 的命令行钩子（如快捷方式生成、升级更新等）
        Velopack.VelopackApp.Build().Run();

        TrySetProcessDpiAwareness();
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        SyncConfigLoader.EnsureExampleFile();
        var settings = AppSettingsStore.Load();
        ApplyTheme(settings.ThemeMode);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        StartupRegistrationService.Apply(settings.LaunchAtStartup);
        EverythingRuntimeService.EnsureStartedInBackground();
        _singleInstanceService = new SingleInstanceService(SingleInstanceAppId);
        if (!_singleInstanceService.TryAcquirePrimaryInstance())
        {
            try
            {
                var protocolArgument = e.Args.FirstOrDefault(static arg =>
                    arg.StartsWith("yanzi://", StringComparison.OrdinalIgnoreCase));
                var message = string.IsNullOrWhiteSpace(protocolArgument)
                    ? "__show__"
                    : protocolArgument;
                
                // 同步等待 Named Pipe 通信（至多 300 毫秒），超时自动断开
                using var cts = new CancellationTokenSource(300);
                _ = _singleInstanceService.SendToPrimaryInstanceAsync(message, cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略任何发送侧异常，以闪退为第一核心纪律
            }
            finally
            {
                // 极其彻底、闪瞬地秒杀当前冗余进程，在内存中绝不容许留下半个字节
                System.Environment.Exit(0);
            }
            return;
        }

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            TryRegisterUriProtocol();
            _notifyIcon = BuildNotifyIcon(window);
            HostAssets.AppendLog($"App startup: version={AppVersionInfo.Version}, build={AppVersionInfo.BuildStamp}, baseDir={AppDomain.CurrentDomain.BaseDirectory}");
            window.Show();
            if (ShouldStartHidden(e.Args))
            {
                window.HideToTray();
            }
            else
            {
                window.ShowPanel();
            }

            StartLocalAgentApi(window, settings);
            _singleInstanceService.StartServer(message => HandleSecondaryLaunchMessageAsync(window, message));
            _ = HandleLaunchArgumentsAsync(window, e.Args);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"MainWindow startup crash: {ex.ToString()}");
            throw;
        }
    }

    private static bool ShouldStartHidden(string[] args)
    {
        return args.Any(arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-tray", StringComparison.OrdinalIgnoreCase));
    }

    private static void TrySetProcessDpiAwareness()
    {
        try
        {
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
            SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch
        {
            // The manifest is the primary DPI declaration; this is a startup-time fallback.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private static void TryRegisterUriProtocol()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                UriProtocolRegistrationService.EnsureRegistered(executablePath);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Protocol registration skipped: {ex.Message}");
        }
    }

    private static async Task HandleLaunchArgumentsAsync(MainWindow window, string[] args)
    {
        var protocolArgument = args.FirstOrDefault(static arg =>
            arg.StartsWith("yanzi://", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(protocolArgument))
        {
            return;
        }

        try
        {
            window.ShowPanel();
            await window.HandleProtocolLaunchAsync(protocolArgument);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Protocol launch failed: {ex}");
        }
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

        try
        {
            var terminatedCount = RunningExtensionRegistry.TerminateAll();
            if (terminatedCount > 0)
            {
                HostAssets.AppendLog($"App shutdown terminated running extensions: count={terminatedCount}");
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"App shutdown terminate running extensions failed: {ex.Message}");
        }

        try
        {
            EverythingRuntimeService.StopOwnedRuntime();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"App shutdown stop Everything failed: {ex.Message}");
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_agentApiServer != null)
        {
            _agentApiServer.Dispose();
            _agentApiServer = null;
        }

        if (_singleInstanceService != null)
        {
            _singleInstanceService.Dispose();
            _singleInstanceService = null;
        }

        base.OnExit(e);
    }



    private static async Task HandleSecondaryLaunchMessageAsync(MainWindow window, string message)
    {
        await window.Dispatcher.InvokeAsync(async () =>
        {
            window.ShowPanel();
            if (!string.Equals(message, "__show__", StringComparison.Ordinal))
            {
                await window.HandleProtocolLaunchAsync(message);
            }
        }).Task.Unwrap();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        HostAssets.AppendLog($"DispatcherUnhandledException: {e.Exception}");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        HostAssets.AppendLog($"AppDomainUnhandledException: {e.ExceptionObject}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HostAssets.AppendLog($"UnobservedTaskException: {e.Exception}");
    }

    private void StartLocalAgentApi(MainWindow window, AppSettings settings)
    {
        if (!settings.EnableAgentApi)
        {
            return;
        }

        try
        {
            var prefix = $"http://127.0.0.1:{settings.AgentApiPort}/";
            _agentApiServer = new LocalAgentApiServer(
                prefix,
                settings.AgentApiToken,
                () =>
                {
                    window.Dispatcher.Invoke(() => window.ReloadLocalExtensionsFromExternal());
                });
            _agentApiServer.Start();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Local Agent API failed to start: {ex.Message}");
        }
    }

    private Forms.NotifyIcon BuildNotifyIcon(MainWindow window)
    {
        var notifyIcon = new Forms.NotifyIcon
        {
            Text = "燕子",
            Visible = true
        };

        notifyIcon.Icon = TryCreateNotifyIcon() ?? SystemIcons.Application;
        
        notifyIcon.DoubleClick += (_, _) => ToggleListenerServices();
        
        // 右键弹出 WPF ContextMenu
        notifyIcon.MouseUp += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                if (WpfApplication.Current.TryFindResource("TrayContextMenu") is System.Windows.Controls.ContextMenu menu)
                {
                    UpdateTrayMenuState(menu);
                    menu.IsOpen = true;
                    // 激活窗口以确保菜单失去焦点时能自动关闭
                    window.Activate();
                }
            }
        };

        return notifyIcon;
    }

    public void ShowDesktopNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (_notifyIcon == null)
        {
            return;
        }

        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ShowDesktopNotification failed: {ex.Message}");
        }
    }

    // 托盘菜单事件处理器
    private void TrayShow_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.ShowPanel();
    }

    private void TrayMousePanel_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.ShowMousePanel();
    }

    private void TrayToggleMousePanelService_Click(object sender, RoutedEventArgs e)
    {
        ToggleListenerServices();
    }

    private void TrayHide_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.HideToTray();
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        CurrentApp?.OpenSettingsWindow();
    }

    private void TrayRunningExtensions_Click(object sender, RoutedEventArgs e)
    {
        CurrentApp?.OpenRunningExtensionsWindow();
    }

    private void TrayInputState_Click(object sender, RoutedEventArgs e)
    {
        if (_inputStateWindow is { IsVisible: true })
        {
            _inputStateWindow.Activate();
            _inputStateWindow.RefreshState();
            return;
        }

        _inputStateWindow = new InputStateWindow();
        _inputStateWindow.Closed += (_, _) => _inputStateWindow = null;
        _inputStateWindow.Show();
    }

    private void TrayResetInputState_Click(object sender, RoutedEventArgs e)
    {
        KeyboardDoubleTapService.ResetStuckKeyboardState();
        InputHookService.ResetMouseState();
        YarnSelectService.ResetMouseState();
        _inputStateWindow?.RefreshState();
        ShowDesktopNotification(
            "输入状态已重置",
            "已清理可能卡住的键盘修饰键，以及鼠标面板、燕环、燕幕和燕选的临时鼠标状态。",
            Forms.ToolTipIcon.Info);
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow is MainWindow mw)
        {
            mw.AllowClose = true;
            Current.Shutdown();
        }
    }

    private void ToggleListenerServices()
    {
        if (MainWindow is not MainWindow mainWindow || _notifyIcon == null)
        {
            return;
        }

        _listenerServicesPaused = !_listenerServicesPaused;
        if (_listenerServicesPaused)
        {
            mainWindow.PauseListenerServices();
            _notifyIcon.Icon = TryCreateDisabledNotifyIcon() ?? SystemIcons.Application;
            _notifyIcon.Text = "燕子 - 服务已暂停";
            HostAssets.AppendLog("Tray: listener services paused.");
        }
        else
        {
            mainWindow.ResumeListenerServices();
            _notifyIcon.Icon = TryCreateNotifyIcon() ?? SystemIcons.Application;
            _notifyIcon.Text = "燕子";
            HostAssets.AppendLog("Tray: listener services resumed.");
        }
    }

    private void UpdateTrayMenuState(System.Windows.Controls.ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            if (Equals(item.Tag, "service-toggle"))
            {
                item.Header = _listenerServicesPaused ? "恢复全部服务" : "暂停全部服务";
            }
            else if (Equals(item.Tag, "mouse-panel"))
            {
                item.IsEnabled = !_listenerServicesPaused;
            }
            else if (Equals(item.Tag, "running-extensions"))
            {
                item.Header = $"正在运行的扩展 ({RunningExtensionRegistry.GetRunningCount()})";
            }
        }
    }

    private static Icon? TryCreateNotifyIcon()
    {
        try
        {
            var resource = WpfApplication.GetResourceStream(new Uri("yanzi.ico", UriKind.Relative));
            if (resource == null)
            {
                return null;
            }

            using var icon = new Icon(resource.Stream);
            return (Icon)icon.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static Icon? TryCreateDisabledNotifyIcon()
    {
        try
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                using var fill = new SolidBrush(Color.FromArgb(255, 96, 96, 96));
                using var border = new Pen(Color.FromArgb(255, 150, 150, 150), 2);
                g.FillEllipse(fill, 4, 4, 24, 24);
                g.DrawEllipse(border, 4, 4, 24, 24);
                using var slash = new Pen(Color.FromArgb(255, 220, 220, 220), 3);
                g.DrawLine(slash, 10, 22, 22, 10);
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
            return null;
        }
    }

    private static App? CurrentApp => Current as App;

    public void OpenSettingsWindow(string? sectionKey = null)
    {
        if (MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        try
        {
            HostAssets.AppendLog($"Settings window open requested: section={sectionKey ?? "default"}, existing={_settingsWindow != null && _settingsWindow.IsLoaded}.");
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow(mainWindow);
                _settingsWindow.ShowInTaskbar = true;
                HostAssets.AppendLog("Settings window opened as independent top-level window.");

                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                HostAssets.AppendLog("Settings window created.");
            }
            else if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.ShowInTaskbar = true;
            }

            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
                HostAssets.AppendLog("Settings window shown.");
            }

            if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
            {
                _settingsWindow.WindowState = System.Windows.WindowState.Normal;
            }

            _settingsWindow.NavigateTo(sectionKey);
            _settingsWindow.Activate();
            _settingsWindow.Focus();
            HostAssets.AppendLog("Settings window activated.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Settings window open failed: {ex}");
        }
    }

    public void OpenRunningExtensionsWindow()
    {
        if (_runningExtensionsWindow == null || !_runningExtensionsWindow.IsLoaded)
        {
            _runningExtensionsWindow = new RunningExtensionsWindow();
            _runningExtensionsWindow.Closed += (_, _) => _runningExtensionsWindow = null;
        }

        if (!_runningExtensionsWindow.IsVisible)
        {
            _runningExtensionsWindow.Show();
        }

        if (_runningExtensionsWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            _runningExtensionsWindow.WindowState = System.Windows.WindowState.Normal;
        }

        _runningExtensionsWindow.Activate();
        _runningExtensionsWindow.Focus();
    }

    private static string _currentThemeMode = "Dark";

    public static void ApplyTheme(string themeMode)
    {
        _currentThemeMode = themeMode;
        
        bool useLightTheme = false;
        if (string.Equals(themeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            useLightTheme = IsSystemLightTheme();
        }
        else if (string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            useLightTheme = true;
        }

        string themeUri = useLightTheme 
            ? "/Themes/LightTheme.xaml"
            : "/Themes/DarkTheme.xaml";

        var mergedDicts = Current.Resources.MergedDictionaries;
        
        var existingThemeDict = mergedDicts.FirstOrDefault(static d => 
            d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));

        if (existingThemeDict != null)
        {
            if (existingThemeDict.Source.OriginalString == themeUri)
                return;

            mergedDicts.Remove(existingThemeDict);
        }

        mergedDicts.Insert(0, new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) });
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            if (string.Equals(_currentThemeMode, "System", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ApplyTheme("System"));
            }
        }
    }
}
