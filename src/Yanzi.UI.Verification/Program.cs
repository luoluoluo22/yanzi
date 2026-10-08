using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yanzi.UI.Wpf;

internal static class Program
{
    private static int _checked;

    [STAThread]
    private static int Main()
    {
        try
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new Window { Title = "Yanzi.UI.Verification", Width = 300, Height = 200 };

            Check(window.Resources.MergedDictionaries.Count == 0, "legacy windows are untouched before opt-in");
            YanziUi.ApplyTo(window);
            Check(window.Resources.MergedDictionaries.Count == 2, "tokens and controls installed per window");

            var dark = RequireBrush(window, "Yanzi.Brush.Window").Color;
            Check(dark == Color.FromRgb(0x0A, 0x0A, 0x0A), "dark token");
            foreach (var key in new[]
            {
                YanziUi.Styles.DefaultButton,
                YanziUi.Styles.OutlineButton,
                YanziUi.Styles.GhostButton,
                YanziUi.Styles.LinkButton,
                YanziUi.Styles.DestructiveButton,
                YanziUi.Styles.Badge,
                YanziUi.Styles.PrimaryButton,
                YanziUi.Styles.SecondaryButton,
                YanziUi.Styles.DangerButton,
                YanziUi.Styles.Input,
                YanziUi.Styles.Toggle,
                YanziUi.Styles.CheckBox,
                YanziUi.Styles.Menu,
                YanziUi.Styles.MenuItem,
                YanziUi.Styles.List,
                YanziUi.Styles.ListItem,
                YanziUi.Styles.Loading,
                YanziUi.Styles.Card,
                YanziUi.Styles.TextSecondary
            })
            {
                Check(window.TryFindResource(key) is Style, "style " + key);
            }

            var button = YanziUi.WithStyle(new Button { Content = "保存" }, YanziUi.Styles.PrimaryButton);
            window.Content = button;
            var buttonStyle = button.Style;
            Check(buttonStyle != null, "runtime-resolved primary button style");
            Check(buttonStyle?.TargetType == typeof(Button), "style has correct WPF type");

            YanziUi.ApplyTo(window);
            Check(window.Resources.MergedDictionaries.Count == 2, "idempotent installation");
            YanziUi.ApplyTo(window, YanziTheme.Light);
            Check(window.Resources.MergedDictionaries.Count == 2, "theme switch does not leak dictionaries");
            Check(RequireBrush(window, "Yanzi.Brush.Window").Color == Color.FromRgb(0xFF, 0xFF, 0xFF), "light token");
            Check(button.Style != null, "controls survive theme switching");
            YanziUi.ApplyTo(window, YanziTheme.Dark);
            Check(RequireBrush(window, "Yanzi.Brush.Window").Color == dark, "restore dark theme");

            foreach (var variant in Enum.GetValues<YanziBadgeVariant>())
            {
                var item = new YanziBadge { Content = "Example", Variant = variant };
                window.Content = item;
                Check(item.Style != null, "badge resolves style: " + variant);
                Check(item.Style?.TargetType == typeof(YanziBadge), "badge style type: " + variant);
            }
            var spinnerBadge = new YanziBadge { Content = "Generating", IsLoading = true, LeadingIcon = "✓" };
            window.Content = spinnerBadge;
            Check(spinnerBadge.IsLoading && spinnerBadge.LeadingIcon == "✓", "badge spinner/icon properties");
            var expectedKeys = new[] { "Yanzi.Color.Background", "Yanzi.Color.Foreground",
                "Yanzi.Color.Primary", "Yanzi.Color.PrimaryForeground", "Yanzi.Color.Secondary",
                "Yanzi.Color.Destructive", "Yanzi.Color.Accent", "Yanzi.Color.Input",
                "Yanzi.Color.Ring", "Yanzi.Color.Sidebar" };
            foreach (var key in expectedKeys)
                Check(window.TryFindResource(key) is SolidColorBrush, "semantic token " + key);

            var secondWindow = new Window();
            Check(secondWindow.Resources.MergedDictionaries.Count == 0, "separate old window remains unchanged");
            Check(YanziUi.WithStyle(new ProgressBar(), YanziUi.Styles.Loading) is ProgressBar, "loading control helper");

            secondWindow.Close();
            window.Close();
            application.Shutdown();
            Console.WriteLine($"PASS: {_checked} design system checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static SolidColorBrush RequireBrush(Window window, string key)
    {
        if (window.TryFindResource(key) is not SolidColorBrush brush)
            throw new InvalidOperationException("Missing brush: " + key);
        return brush;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checked++;
        Console.WriteLine("OK: " + label);
    }
}
