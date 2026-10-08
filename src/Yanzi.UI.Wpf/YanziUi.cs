using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

public enum YanziTheme { Dark, Light }

/// <summary>
/// Opt-in per-window design system. Never edits Application.Current.Resources:
/// legacy extensions retain their own styles and theme behavior.
/// </summary>
public static class YanziUi
{
    private sealed class InstalledResources
    {
        public required ResourceDictionary Tokens { get; init; }
        public required ResourceDictionary Controls { get; init; }
        public YanziTheme Theme { get; set; }
    }

    private static readonly ConditionalWeakTable<Window, InstalledResources> Installed = new();

    public static class Styles
    {
        public const string PrimaryButton = "Yanzi.Button.Primary";
        public const string SecondaryButton = "Yanzi.Button.Secondary";
        public const string DangerButton = "Yanzi.Button.Danger";
        public const string Input = "Yanzi.Input";
        public const string Toggle = "Yanzi.Toggle";
        public const string CheckBox = "Yanzi.CheckBox";
        public const string Menu = "Yanzi.Menu";
        public const string MenuItem = "Yanzi.MenuItem";
        public const string List = "Yanzi.List";
        public const string ListItem = "Yanzi.ListItem";
        public const string Loading = "Yanzi.Loading";
        public const string Card = "Yanzi.Card";
        public const string TextSecondary = "Yanzi.Text.Secondary";
    }

    public static YanziTheme GetTheme(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.VerifyAccess();
        return Installed.TryGetValue(window, out var resources) ? resources.Theme : YanziTheme.Dark;
    }

    public static void ApplyTo(Window window, YanziTheme theme = YanziTheme.Dark)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.VerifyAccess();

        if (Installed.TryGetValue(window, out var existing))
        {
            if (existing.Theme != theme)
            {
                existing.Tokens.Source = ThemeUri(theme);
                existing.Theme = theme;
            }
            return;
        }

        var tokens = new ResourceDictionary { Source = ThemeUri(theme) };
        var controls = new ResourceDictionary
        {
            Source = new Uri("/Yanzi.UI.Wpf;component/Themes/Controls.xaml", UriKind.Relative)
        };

        window.Resources.MergedDictionaries.Add(tokens);
        window.Resources.MergedDictionaries.Add(controls);
        Installed.Add(window, new InstalledResources { Tokens = tokens, Controls = controls, Theme = theme });
    }

    /// <summary>Use after ApplyTo to keep a local control bound to an updatable resource key.</summary>
    public static T WithStyle<T>(T control, string styleKey) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(styleKey);
        control.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return control;
    }

    private static Uri ThemeUri(YanziTheme theme) =>
        new($"/Yanzi.UI.Wpf;component/Themes/Tokens.{theme}.xaml", UriKind.Relative);
}
