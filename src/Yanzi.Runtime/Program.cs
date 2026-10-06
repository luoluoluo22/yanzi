namespace Yanzi.Runtime;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Reuse the existing service engine and extension contracts in a separate lifetime.
        // No shell owns this process or adds it to its child-process job.
        var application = new OpenQuickHost.App();
        application.InitializeComponent();
        application.Run();
    }
}
