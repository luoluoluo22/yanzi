using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yanzi.Platform.Mac;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(
                CreateGlobalInputTriggerListenerFactory(),
                CreateCommandActionExecutor());
        }

        base.OnFrameworkInitializationCompleted();
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
