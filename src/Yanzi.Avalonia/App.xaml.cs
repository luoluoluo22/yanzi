using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yanzi.Platform.Mac;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class App : Application
{
    private MainWindow? _mainWindow;

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
            });
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
