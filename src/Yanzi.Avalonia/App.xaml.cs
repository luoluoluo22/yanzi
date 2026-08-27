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

    private static readonly object _logLock = new();

    public static void WriteLog(string message)
    {
        lock (_logLock)
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
    }

    public override void OnFrameworkInitializationCompleted()
    {
        WriteLog("App OnFrameworkInitializationCompleted: Starting...");
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                WriteLog("App OnFrameworkInitializationCompleted: Instantiating MainWindow...");
                _mainWindow = new MainWindow(
                    CreateGlobalInputTriggerListenerFactory(),
                    CreateCommandActionExecutor());
                WriteLog("App OnFrameworkInitializationCompleted: MainWindow instantiated successfully");

                WriteLog("App OnFrameworkInitializationCompleted: Creating TrayIcon...");
                CreateTrayIcon();
                WriteLog("App OnFrameworkInitializationCompleted: CreateTrayIcon completed successfully.");
            }
            else
            {
                WriteLog("App OnFrameworkInitializationCompleted: ApplicationLifetime is NOT IClassicDesktopStyleApplicationLifetime!");
            }
        }
        catch (Exception ex)
        {
            WriteLog($"App OnFrameworkInitializationCompleted ERROR: {ex.GetType().Name} - {ex.Message}\nStack:{ex.StackTrace}");
        }

        WriteLog("App OnFrameworkInitializationCompleted: Invoking base...");
        base.OnFrameworkInitializationCompleted();
        WriteLog("App OnFrameworkInitializationCompleted: base invocation completed.");
    }

    private void CreateTrayIcon()
    {
        WriteLog("CreateTrayIcon: Starting...");
        try
        {
            string iconName = "logo.png";
            WriteLog($"CreateTrayIcon: Using static white iconName={iconName}");
            
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

            var permissionGuideMenuItem = new NativeMenuItem("获取系统授权 / 授权指引...");
            permissionGuideMenuItem.Click += (sender, e) => OpenPermissionGuide();

            var resetInputMenuItem = new NativeMenuItem("重置键鼠状态 (清除卡键)");
            resetInputMenuItem.Click += (sender, e) => ResetKeyboardAndMouse();

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
                        permissionGuideMenuItem,
                        resetInputMenuItem,
                        new NativeMenuItemSeparator(),
                        _toggleServiceMenuItem,
                        new NativeMenuItemSeparator(),
                        exitMenuItem
                    }
                }
            };
            _trayIcon.Clicked += (sender, e) =>
            {
                WriteLog("TrayIcon Clicked: opening launcher window");
                OpenLauncher();
            };
            WriteLog("CreateTrayIcon: TrayIcon instance created successfully");

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
            WriteLog("CreateTrayIcon: TrayIcon bound to Application SetIcons successfully!");
            
            UpdateTrayMenuState();
            WriteLog("CreateTrayIcon: UpdateTrayMenuState completed");
        }
        catch (Exception ex)
        {
            WriteLog($"CreateTrayIcon ERROR: {ex.GetType().Name} - {ex.Message}\nStack Trace:\n{ex.StackTrace}");
            Console.WriteLine($"Failed to create TrayIcon: {ex.Message}");
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

    private void OpenPermissionGuide()
    {
        if (_mainWindow == null)
            return;

        var guideWindow = new PermissionGuideWindow(_mainWindow);
        guideWindow.Show();
        guideWindow.Activate();
    }

    private void ResetKeyboardAndMouse()
    {
        WriteLog("ResetKeyboardAndMouse requested from tray menu.");
        if (OperatingSystem.IsMacOS())
        {
            MacInputResetHelper.ResetKeyboardAndMouseState();
        }

        if (_mainWindow != null)
        {
            _mainWindow.RestartInputTriggerListener(true);
        }
        WriteLog("ResetKeyboardAndMouse completed.");
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

