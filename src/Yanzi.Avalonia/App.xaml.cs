using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow(
                CreateGlobalInputTriggerListenerFactory(),
                CreateCommandActionExecutor());

            // Post assigning of MainWindow to after the desktop lifetime has finished starting,
            // which prevents ClassicDesktopStyleApplicationLifetime from automatically calling Show() at startup.
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                desktop.MainWindow = _mainWindow;
                CreateTrayIcon();
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        try
        {
            var logoUri = new Uri("avares://Yanzi.Avalonia/Assets/logo.png");
            using var stream = AssetLoader.Open(logoUri);
            var bitmap = new global::Avalonia.Media.Imaging.Bitmap(stream);

            _toggleServiceMenuItem = new NativeMenuItem("暂停服务");
            _toggleServiceMenuItem.Click += (sender, e) => ToggleService();

            var settingsMenuItem = new NativeMenuItem("打开设置...");
            settingsMenuItem.Click += (sender, e) => OpenSettings();

            var exitMenuItem = new NativeMenuItem("退出");
            exitMenuItem.Click += (sender, e) => ExitApp();

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(bitmap),
                ToolTipText = "燕子启动器 (Yanzi)",
                Menu = new NativeMenu
                {
                    Items =
                    {
                        settingsMenuItem,
                        new NativeMenuItemSeparator(),
                        _toggleServiceMenuItem,
                        new NativeMenuItemSeparator(),
                        exitMenuItem
                    }
                }
            };

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
            
            UpdateTrayMenuState();
        }
        catch (Exception ex)
        {
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

