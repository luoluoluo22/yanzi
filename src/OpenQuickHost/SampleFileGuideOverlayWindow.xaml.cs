using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace OpenQuickHost;

public partial class SampleFileGuideOverlayWindow : Window
{
    private Storyboard? _storyboard;

    public SampleFileGuideOverlayWindow()
    {
        InitializeComponent();

        // 默认定位在屏幕左侧偏下 (贴近常见桌面图标/资源管理器区域)
        Left = 60;
        Top = Math.Max(120, SystemParameters.PrimaryScreenHeight - 300);

        Loaded += SampleFileGuideOverlayWindow_Loaded;
        Closed += SampleFileGuideOverlayWindow_Closed;
    }

    private void SampleFileGuideOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _storyboard = TryFindResource("FileGuideBreatheAnimation") as Storyboard;
        _storyboard?.Begin(this, isControllable: true);
    }

    private void SampleFileGuideOverlayWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            _storyboard?.Stop(this);
        }
        catch
        {
            // Ignore
        }
    }
}
