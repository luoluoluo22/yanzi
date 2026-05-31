using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

namespace Yanzi.Avalonia;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.WriteLog("Program Main: Entering Entry Point...");

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            App.WriteLog($"CRITICAL UNHANDLED EXCEPTION (AppDomain): {ex?.GetType().Name} - {ex?.Message}\nStack Trace:\n{ex?.StackTrace}");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            App.WriteLog($"CRITICAL UNOBSERVED TASK EXCEPTION: {e.Exception.GetType().Name} - {e.Exception.Message}\nStack Trace:\n{e.Exception.StackTrace}");
            e.SetObserved();
        };

        try
        {
            App.WriteLog("Program Main: Invoking StartWithClassicDesktopLifetime...");
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            App.WriteLog("Program Main: StartWithClassicDesktopLifetime completed successfully.");
        }
        catch (Exception ex)
        {
            App.WriteLog($"CRITICAL LAUNCH ERROR (Main Catch): {ex.GetType().Name} - {ex.Message}\nStack Trace:\n{ex.StackTrace}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
