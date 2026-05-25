using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public partial class InputStateWindow : Window
{
    private readonly DispatcherTimer _timer;

    public InputStateWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (_, _) => RefreshState();
        Loaded += InputStateWindow_Loaded;
        Closed += InputStateWindow_Closed;
    }

    public void RefreshState()
    {
        SummaryTextBlock.Text = $"最后刷新：{DateTime.Now:HH:mm:ss.fff}";
        MouseStateTextBlock.Text = $"{InputHookService.GetMouseStateSummary()}{Environment.NewLine}{YarnSelectService.GetMouseStateSummary()}";
        KeyboardStateTextBlock.Text = KeyboardDoubleTapService.GetKeyboardStateSummary();
    }

    private void InputStateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshState();
        _timer.Start();
    }

    private void InputStateWindow_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshState();
    }

    private void ResetKeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        KeyboardDoubleTapService.ResetStuckKeyboardState();
        HostAssets.AppendLog("Input state window: keyboard state reset requested.");
        RefreshState();
    }

    private void ResetMouseButton_Click(object sender, RoutedEventArgs e)
    {
        InputHookService.ResetMouseState();
        YarnSelectService.ResetMouseState();
        HostAssets.AppendLog("Input state window: mouse state reset requested.");
        RefreshState();
    }
}
