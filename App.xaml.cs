using System.Drawing;
using System.Runtime.InteropServices;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;
using System.IO;

namespace OpenQuickHost;

public partial class App : WpfApplication
{
    private Forms.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);
        SyncConfigLoader.EnsureExampleFile();

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.HideToTray();

        _notifyIcon = BuildNotifyIcon(window);
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        base.OnExit(e);
    }

    private static Forms.NotifyIcon BuildNotifyIcon(MainWindow window)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示面板", null, (_, _) => window.ShowPanel());
        menu.Items.Add("隐藏面板", null, (_, _) => window.HideToTray());
        menu.Items.Add("设置", null, (_, _) => CurrentApp?.OpenSettingsWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            window.AllowClose = true;
            Current.Shutdown();
        });

        var notifyIcon = new Forms.NotifyIcon
        {
            Text = "燕子",
            ContextMenuStrip = menu
        };

        string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
        notifyIcon.Icon = TryCreateNotifyIcon(logoPath) ?? SystemIcons.Application;
        notifyIcon.Visible = true;

        notifyIcon.DoubleClick += (_, _) => window.TogglePanelVisibility();
        return notifyIcon;
    }

    private static Icon? TryCreateNotifyIcon(string logoPath)
    {
        if (!File.Exists(logoPath))
        {
            return null;
        }

        try
        {
            using var bitmap = new Bitmap(logoPath);
            IntPtr handle = bitmap.GetHicon();

            try
            {
                using var rawIcon = Icon.FromHandle(handle);
                return (Icon)rawIcon.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static App? CurrentApp => Current as App;

    public void OpenSettingsWindow()
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

        _settingsWindow.Activate();
        _settingsWindow.Focus();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
