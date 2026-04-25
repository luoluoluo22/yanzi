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

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);
        SyncConfigLoader.EnsureExampleFile();
        var settings = AppSettingsStore.Load();
        StartupRegistrationService.Apply(settings.LaunchAtStartup);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.HideToTray();

        _notifyIcon = BuildNotifyIcon(window);
        StartLocalAgentApi(window, settings);
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

    private static Forms.NotifyIcon BuildNotifyIcon(MainWindow window)
    {
        var notifyIcon = new Forms.NotifyIcon
        {
            Text = "燕子",
            Visible = true
        };

        notifyIcon.Icon = TryCreateNotifyIcon() ?? SystemIcons.Application;
        
        // 双击显示/隐藏
        notifyIcon.DoubleClick += (_, _) => window.TogglePanelVisibility();
        
        // 右键弹出 WPF ContextMenu
        notifyIcon.MouseUp += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                if (WpfApplication.Current.TryFindResource("TrayContextMenu") is System.Windows.Controls.ContextMenu menu)
                {
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
