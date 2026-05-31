using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Input;
using Yanzi.Platform.Mac;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _toggleServiceMenuItem;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static void WriteLog(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".yanzi_boot.log"
            );
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch {}
    }

    public override void OnFrameworkInitializationCompleted()
    {
        WriteLog("App OnFrameworkInitializationCompleted: Starting...");
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _mainWindow = new MainWindow(
                    CreateGlobalInputTriggerListenerFactory(),
                    CreateCommandActionExecutor());
                WriteLog("App: MainWindow instantiated successfully");

                // Post assigning of MainWindow to after the desktop lifetime has finished starting,
                // which prevents ClassicDesktopStyleApplicationLifetime from automatically calling Show() at startup.
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    WriteLog("App: Deferred Assigning MainWindow and Creating TrayIcon starting...");
                    desktop.MainWindow = _mainWindow;
                    CreateTrayIcon();
                });
            }
        }
        catch (Exception ex)
        {
            WriteLog($"App OnFrameworkInitializationCompleted ERROR: {ex.GetType().Name} - {ex.Message}\nStack:{ex.StackTrace}");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        WriteLog("CreateTrayIcon: Starting...");
        try
        {
            bool isDarkMode = !System.OperatingSystem.IsMacOS() || IsMacSystemDarkMode();
            string iconName = isDarkMode ? "logo.png" : "logo_dark.png";
            WriteLog($"CreateTrayIcon: isDarkMode={isDarkMode}, iconName={iconName}");
            
            var logoUri = new Uri($"avares://Yanzi.Avalonia/Assets/{iconName}");
            WriteLog($"CreateTrayIcon: loading Uri={logoUri}");
            
            using var stream = AssetLoader.Open(logoUri);
            WriteLog("CreateTrayIcon: Asset stream opened successfully");
            
            var bitmap = new global::Avalonia.Media.Imaging.Bitmap(stream);
            WriteLog($"CreateTrayIcon: Bitmap loaded. Size={bitmap.Size}");

            var openLauncherMenuItem = new NativeMenuItem("打开主启动器")
            {
                Gesture = KeyGesture.Parse("alt+space")
            };
            openLauncherMenuItem.Click += (sender, e) => OpenLauncher();

            var openMousePanelMenuItem = new NativeMenuItem("打开鼠标面板")
            {
                Gesture = KeyGesture.Parse("alt+m")
            };
            openMousePanelMenuItem.Click += (sender, e) => OpenMousePanel();

            _toggleServiceMenuItem = new NativeMenuItem("暂停服务")
            {
                Gesture = KeyGesture.Parse("alt+p")
            };
            _toggleServiceMenuItem.Click += (sender, e) => ToggleService();

            var settingsMenuItem = new NativeMenuItem("打开设置...")
            {
                Gesture = KeyGesture.Parse("cmd+,")
            };
            settingsMenuItem.Click += (sender, e) => OpenSettings();

            var exitMenuItem = new NativeMenuItem("退出")
            {
                Gesture = KeyGesture.Parse("cmd+q")
            };
            exitMenuItem.Click += (sender, e) => ExitApp();

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(bitmap),
                ToolTipText = "燕子启动器 (Yanzi)",
                Menu = new NativeMenu
                {
                    Items =
                    {
                        openLauncherMenuItem,
                        new NativeMenuItemSeparator(),
                        openMousePanelMenuItem,
                        new NativeMenuItemSeparator(),
                        settingsMenuItem,
                        new NativeMenuItemSeparator(),
                        _toggleServiceMenuItem,
                        new NativeMenuItemSeparator(),
                        exitMenuItem
                    }
                }
            };
            WriteLog("CreateTrayIcon: TrayIcon instance created successfully");

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
            WriteLog("CreateTrayIcon: TrayIcon bound to Application SetIcons successfully!");
            
            UpdateTrayMenuState();
            WriteLog("CreateTrayIcon: UpdateTrayMenuState completed");

            // Start auto theme detection timer on macOS
            if (System.OperatingSystem.IsMacOS())
            {
                WriteLog("CreateTrayIcon: Starting macOS Themevariant auto detection timer...");
                var timer = new global::Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2.5)
                };
                bool lastMode = isDarkMode;
                timer.Tick += (s, e) =>
                {
                    bool currentMode = IsMacSystemDarkMode();
                    if (currentMode != lastMode)
                    {
                        WriteLog($"CreateTrayIcon Timer: Theme variant changed from {lastMode} to {currentMode}");
                        lastMode = currentMode;
                        UpdateTrayIcon(currentMode);
                    }
                };
                timer.Start();
                WriteLog("CreateTrayIcon: Themevariant auto detection timer started successfully");
            }
        }
        catch (Exception ex)
        {
            WriteLog($"CreateTrayIcon ERROR: {ex.GetType().Name} - {ex.Message}\nStack Trace:\n{ex.StackTrace}");
            Console.WriteLine($"Failed to create TrayIcon: {ex.Message}");
        }
    }

    private void UpdateTrayIcon(bool isDarkMode)
    {
        if (_trayIcon == null) return;
        try
        {
            WriteLog($"UpdateTrayIcon: Updating tray icon to isDarkMode={isDarkMode}...");
            string iconName = isDarkMode ? "logo.png" : "logo_dark.png";
            var logoUri = new Uri($"avares://Yanzi.Avalonia/Assets/{iconName}");
            using var stream = AssetLoader.Open(logoUri);
            var bitmap = new global::Avalonia.Media.Imaging.Bitmap(stream);
            _trayIcon.Icon = new WindowIcon(bitmap);
            WriteLog("UpdateTrayIcon: TrayIcon updated successfully");
        }
        catch (Exception ex)
        {
            WriteLog($"UpdateTrayIcon ERROR: {ex.GetType().Name} - {ex.Message}\nStack:{ex.StackTrace}");
            Console.WriteLine($"Failed to update tray icon: {ex.Message}");
        }
    }

    private static bool IsMacSystemDarkMode()
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "defaults";
            process.StartInfo.Arguments = "read -g AppleInterfaceStyle";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return output.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void UpdateTrayMenuState()
    {
        if (_toggleServiceMenuItem == null || _mainWindow == null)
            return;

        _toggleServiceMenuItem.Header = _mainWindow.IsServiceRunning ? "暂停服务" : "启用服务";
    }

    private void ToggleService()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.ToggleService();
        UpdateTrayMenuState();
    }

    private void OpenSettings()
    {
        if (_mainWindow == null)
            return;

        var settingsWindow = new SettingsWindow(_mainWindow);
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void OpenLauncher()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.ShowLauncherFromTray();
    }

    private void OpenMousePanel()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.ShowQuickPanelFromTray();
    }

    private void ExitApp()
    {
        _trayIcon?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static IGlobalInputTriggerListenerFactory CreateGlobalInputTriggerListenerFactory()
    {
        return OperatingSystem.IsMacOS()
            ? new MacGlobalInputTriggerListenerFactory()
            : new DisabledGlobalInputTriggerListenerFactory();
    }

    private static ICommandActionExecutor CreateCommandActionExecutor()
    {
        return OperatingSystem.IsMacOS()
            ? new MacCommandActionExecutor()
            : new DisabledCommandActionExecutor();
    }
}

