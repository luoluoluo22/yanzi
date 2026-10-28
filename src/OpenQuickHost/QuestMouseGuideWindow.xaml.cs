using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace OpenQuickHost;

public partial class QuestMouseGuideWindow : Window
{
    private Storyboard? _rightButtonStoryboard;

    public QuestMouseGuideWindow()
    {
        InitializeComponent();

        // 准确定位在屏幕正中央
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2);

        Loaded += QuestMouseGuideWindow_Loaded;
        Closed += QuestMouseGuideWindow_Closed;
    }

    private void QuestMouseGuideWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _rightButtonStoryboard = TryFindResource("RightButtonPressSimulation") as Storyboard;
        _rightButtonStoryboard?.Begin(this, isControllable: true);
    }

    private void QuestMouseGuideWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            _rightButtonStoryboard?.Stop(this);
        }
        catch
        {
            // Ignore
        }
    }
}
