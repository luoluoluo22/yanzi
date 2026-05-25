using System.Windows;

namespace Yanzi;

public static class HostedViewBridge
{
    public static void SetAction(DependencyObject element, string value) =>
        OpenQuickHost.HostedViewBridge.SetAction(element, value);

    public static string GetAction(DependencyObject element) =>
        OpenQuickHost.HostedViewBridge.GetAction(element);

    public static void SetPreferredFocus(DependencyObject element, string value) =>
        OpenQuickHost.HostedViewBridge.SetPreferredFocus(element, value);

    public static string GetPreferredFocus(DependencyObject element) =>
        OpenQuickHost.HostedViewBridge.GetPreferredFocus(element);

    public static void SetLoadedAction(DependencyObject element, string value) =>
        OpenQuickHost.HostedViewBridge.SetLoadedAction(element, value);

    public static string GetLoadedAction(DependencyObject element) =>
        OpenQuickHost.HostedViewBridge.GetLoadedAction(element);
}
