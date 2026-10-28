using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Reusable icon/title/subtitle row with optional action.</summary>
public sealed class YanziItem : UserControl
{
    public YanziItem(string title, string description, string icon = "•", Action? action = null)
    {
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(4, 5, 4, 5) };
        if (action is not null)
        {
            var run = YanziUi.WithStyle(new Button { Content = "›", Width = 32, MinWidth = 32 },
                YanziUi.Styles.GhostButton);
            run.Click += (_, _) => action();
            DockPanel.SetDock(run, Dock.Right);
            root.Children.Add(run);
        }
        var symbol = new Border { Height = 38, Width = 38, CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 12, 0) };
        symbol.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        var glyph = new TextBlock { Text = icon, FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        symbol.Child = glyph;
        DockPanel.SetDock(symbol, Dock.Left);
        root.Children.Add(symbol);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var headline = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        headline.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        var secondary = new TextBlock { Text = description, FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        secondary.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        content.Children.Add(headline);
        content.Children.Add(secondary);
        root.Children.Add(content);
        Content = root;
    }
}
