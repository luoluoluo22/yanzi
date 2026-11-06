using System.Windows;

namespace OpenQuickHost;

public partial class AiToolExecutionCard : System.Windows.Controls.UserControl
{
    private bool followOutput = true;
    public AiToolExecutionCard()
    {
        InitializeComponent();
        OutputBox.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent,
            new System.Windows.Controls.ScrollChangedEventHandler((_, e) =>
            {
                if (e.ExtentHeightChange == 0 && e.VerticalChange != 0)
                    followOutput = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1;
            }));
        OutputBox.TextChanged += (_, _) =>
        {
            if (followOutput)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { if (followOutput) OutputBox.ScrollToEnd(); }));
        };
    }
    private void StopTurn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiToolExecution { IsRunning: true } execution) execution.CancelTurn?.Invoke();
        e.Handled = true;
    }
}
