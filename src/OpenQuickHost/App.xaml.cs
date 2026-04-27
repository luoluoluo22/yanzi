using System.Drawing;
using System.Windows;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace OpenQuickHost;

public partial class App : WpfApplication
{
    private Forms.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;
    private LocalAgentApiServer? _agentApiServer;
    private bool _listenerServicesPaused;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);
        SyncConfigLoader.EnsureExampleFile();
        var settings = AppSettingsStore.Load();
        StartupRegistrationService.Apply(settings.LaunchAtStartup);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        _notifyIcon = BuildNotifyIcon(window);
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
    }

    private static bool ShouldStartHidden(string[] args)
    {
        return args.Any(arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-tray", StringComparison.OrdinalIgnoreCase));
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
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

        base.OnExit(e);
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

        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(mainWindow);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            _settingsWindow.WindowState = System.Windows.WindowState.Normal;
        }

        _settingsWindow.NavigateTo(sectionKey);
        _settingsWindow.Activate();
        _settingsWindow.Focus();
    }
}
