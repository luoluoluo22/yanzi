using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Yanzi.Platform.Mac;

namespace Yanzi.Avalonia;

public partial class PermissionGuideWindow : Window
{
    private readonly MainWindow? _mainWindow;

    public PermissionGuideWindow()
        : this(null!)
    {
    }

    public PermissionGuideWindow(MainWindow? mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        RefreshStatus();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void RefreshStatus()
    {
        bool axGranted = MacPermissionHelper.IsAccessibilityGranted();
        bool inputGranted = MacPermissionHelper.IsInputMonitoringGranted();

        var axBadge = this.FindControl<Border>("AccessibilityBadge");
        var axText = this.FindControl<TextBlock>("AccessibilityStatusText");
        if (axBadge != null && axText != null)
        {
            if (axGranted)
            {
                axBadge.Background = new SolidColorBrush(Color.Parse("#3300E676"));
                axText.Text = "🟢 已授权";
                axText.Foreground = new SolidColorBrush(Color.Parse("#00E676"));
            }
            else
            {
                axBadge.Background = new SolidColorBrush(Color.Parse("#33FF5555"));
                axText.Text = "🔴 未授权";
                axText.Foreground = new SolidColorBrush(Color.Parse("#FF7777"));
            }
        }

        var inputBadge = this.FindControl<Border>("InputMonitoringBadge");
        var inputText = this.FindControl<TextBlock>("InputMonitoringStatusText");
        if (inputBadge != null && inputText != null)
        {
            if (inputGranted)
            {
                inputBadge.Background = new SolidColorBrush(Color.Parse("#3300E676"));
                inputText.Text = "🟢 已授权";
                inputText.Foreground = new SolidColorBrush(Color.Parse("#00E676"));
            }
            else
            {
                inputBadge.Background = new SolidColorBrush(Color.Parse("#33FF5555"));
                inputText.Text = "🔴 未授权";
                inputText.Foreground = new SolidColorBrush(Color.Parse("#FF7777"));
            }
        }

        var statusMsg = this.FindControl<TextBlock>("StatusMessageText");
        if (statusMsg != null)
        {
            if (axGranted && inputGranted)
            {
                statusMsg.Text = "🎉 所有必要权限已全部就绪！全局手势与长按均可正常工作。";
                statusMsg.Foreground = new SolidColorBrush(Color.Parse("#00E676"));
            }
            else
            {
                statusMsg.Text = "⚠️ 尚有权限未开启，请按指引在设置中开启后点击重新连接。";
                statusMsg.Foreground = new SolidColorBrush(Color.Parse("#FFCC00"));
            }
        }
    }

    private void OpenAccessibilitySettings_Click(object? sender, RoutedEventArgs e)
    {
        MacPermissionHelper.OpenAccessibilitySettings();
    }

    private void RequestAccessibility_Click(object? sender, RoutedEventArgs e)
    {
        MacPermissionHelper.RequestAccessibilityPermission();
        RefreshStatus();
    }

    private void OpenInputMonitoringSettings_Click(object? sender, RoutedEventArgs e)
    {
        MacPermissionHelper.OpenInputMonitoringSettings();
    }

    private void RequestInputMonitoring_Click(object? sender, RoutedEventArgs e)
    {
        MacPermissionHelper.RequestInputMonitoringPermission();
        RefreshStatus();
    }

    private void RevealInFinder_Click(object? sender, RoutedEventArgs e)
    {
        MacPermissionHelper.RevealAppInFinder();
    }

    private void CheckAndReconnect_Click(object? sender, RoutedEventArgs e)
    {
        RefreshStatus();

        if (_mainWindow != null)
        {
            _mainWindow.RestartInputTriggerListener(true);
            global::Yanzi.Avalonia.App.WriteLog("PermissionGuideWindow: RestartInputTriggerListener requested after permission check.");
        }

        bool axGranted = MacPermissionHelper.IsAccessibilityGranted();
        bool inputGranted = MacPermissionHelper.IsInputMonitoringGranted();

        var statusMsg = this.FindControl<TextBlock>("StatusMessageText");
        if (statusMsg != null)
        {
            if (axGranted && inputGranted)
            {
                statusMsg.Text = "✅ 重新连接成功！所有权限已正常启用。";
                statusMsg.Foreground = new SolidColorBrush(Color.Parse("#00E676"));
            }
            else
            {
                statusMsg.Text = "⚠️ 检测到部分权限仍未在系统设置中勾选，请勾选后再次重试。";
                statusMsg.Foreground = new SolidColorBrush(Color.Parse("#FF7777"));
            }
        }
    }

    private void InitializeComponent()
    {
        global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
