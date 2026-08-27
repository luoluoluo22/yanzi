using System;
using Avalonia;
using Avalonia.Styling;

namespace Yanzi.Avalonia;

public static class ThemeManager
{
    private static string _currentMode = "System";

    public static string CurrentMode => _currentMode;

    public static void Initialize(string? savedMode = null)
    {
        ApplyTheme(savedMode ?? "System");
    }

    public static void ApplyTheme(string themeMode)
    {
        _currentMode = themeMode;
        if (Application.Current == null) return;

        switch (themeMode.ToLowerInvariant())
        {
            case "dark":
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                break;
            case "light":
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                break;
            case "system":
            default:
                Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                break;
        }
    }
}
