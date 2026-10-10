using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yanzi.UI.Wpf;

/// <summary>Theme-aware, composable native WPF primitives; no host or runtime dependency.</summary>
public static class YanziPrimitives
{
    private static void Resource(FrameworkElement element, DependencyProperty property, string token) =>
        element.SetResourceReference(property, $"Yanzi.Color.{token}");

    public static Border Separator(bool vertical = false)
    {
        var line = new Border { Width = vertical ? 1 : double.NaN, Height = vertical ? double.NaN : 1,
            Margin = vertical ? new Thickness(9, 0, 9, 0) : new Thickness(0, 9, 0, 9) };
        Resource(line, Border.BackgroundProperty, "Border");
        return line;
    }

    // Compatibility entry point: all keycaps now use the shared Kbd style/control.
    public static Border Kbd(string shortcut) => new YanziKbd { KeyText = shortcut };

    public static YanziKbdGroup KbdGroup(params string[] keys)
    {
        var group = new YanziKbdGroup();
        foreach (var key in keys)
            group.Children.Add(new YanziKbd { KeyText = key, Margin = new Thickness(0, 0, 4, 0) });
        return group;
    }

    public static Border Avatar(string initials, double diameter = 42) =>
        new YanziAvatar { Initials = initials, Diameter = diameter };

    public static Border Avatar(string initials, double diameter, ImageSource? image) =>
        new YanziAvatar { Initials = initials, Diameter = diameter, ImageSource = image };

    public static Border Skeleton(double width, double height, bool animate = true)
    {
        var placeholder = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left };
        Resource(placeholder, Border.BackgroundProperty, "Muted");
        if (animate)
        {
            var animation = new DoubleAnimation(0.45, 0.85, TimeSpan.FromMilliseconds(920))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            placeholder.Loaded += (_, _) => placeholder.BeginAnimation(UIElement.OpacityProperty, animation);
            placeholder.Unloaded += (_, _) => placeholder.BeginAnimation(UIElement.OpacityProperty, null);
        }
        return placeholder;
    }

    public static Border Alert(string title, string description, bool destructive = false,
        string icon = "i")
    {
        var box = new Border { BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8) };
        box.SetResourceReference(Border.CornerRadiusProperty, "Yanzi.Radius.Md");
        Resource(box, Border.BackgroundProperty, "Card");
        Resource(box, Border.BorderBrushProperty, destructive ? "Destructive" : "Border");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var symbol = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.3), VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
        Resource(symbol, Border.BorderBrushProperty, destructive ? "Destructive" : "Foreground");
        var glyph = new TextBlock { Text = icon, FontWeight = FontWeights.Bold, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Resource(glyph, TextBlock.ForegroundProperty, destructive ? "Destructive" : "Foreground");
        symbol.Child = glyph;
        grid.Children.Add(symbol);
        var stack = new StackPanel();
        var header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold,
            FontSize = 14, TextWrapping = TextWrapping.Wrap };
        Resource(header, TextBlock.ForegroundProperty, destructive ? "Destructive" : "Foreground");
        var detail = new TextBlock { Text = description, FontSize = 14, Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap };
        Resource(detail, TextBlock.ForegroundProperty, "MutedForeground");
        stack.Children.Add(header);
        stack.Children.Add(detail);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        box.Child = grid;
        return box;
    }

    public static StackPanel EmptyState(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(10, 22, 10, 22),
            HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new TextBlock { Text = "□", FontSize = 33,
            HorizontalAlignment = HorizontalAlignment.Center };
        Resource(icon, TextBlock.ForegroundProperty, "MutedForeground");
        panel.Children.Add(icon);
        var header = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 9, 0, 5), TextAlignment = TextAlignment.Center };
        Resource(header, TextBlock.ForegroundProperty, "Foreground");
        panel.Children.Add(header);
        var body = new TextBlock { Text = subtitle, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 350, TextAlignment = TextAlignment.Center };
        Resource(body, TextBlock.ForegroundProperty, "MutedForeground");
        panel.Children.Add(body);
        return panel;
    }

    public static YanziLabel Label(string caption, FrameworkElement target, bool required = false) =>
        new() { Caption = caption, Target = target, Required = required };

    public static StackPanel Field(string label, FrameworkElement control, string? description = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var text = new YanziLabel { Caption = label, Target = control };
        panel.Children.Add(text);
        panel.Children.Add(control);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var help = new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0) };
            Resource(help, TextBlock.ForegroundProperty, "MutedForeground");
            panel.Children.Add(help);
        }
        return panel;
    }
}
