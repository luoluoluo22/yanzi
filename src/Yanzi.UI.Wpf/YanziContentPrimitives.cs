using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>Chat and file UI primitives. These controls never read or upload files.</summary>
public static class YanziContentPrimitives
{
    public static Border MessageBubble(string content, bool outgoing)
    {
        var bubble = new Border { MaxWidth = 300, Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        bubble.SetResourceReference(Border.BackgroundProperty, outgoing ? "Yanzi.Color.Primary" : "Yanzi.Color.Secondary");
        var text = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        text.SetResourceReference(TextBlock.ForegroundProperty, outgoing ? "Yanzi.Color.PrimaryForeground" : "Yanzi.Color.SecondaryForeground");
        bubble.Child = text;
        return bubble;
    }

    public static StackPanel Marker(string label, bool positive = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0) };
        dot.SetResourceReference(Shape.FillProperty, positive ? "Yanzi.Color.Success" : "Yanzi.Color.Destructive");
        row.Children.Add(dot);
        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        row.Children.Add(text);
        return row;
    }

    /// <summary>Displays a file name and optional removal action; storage remains the caller's responsibility.</summary>
    public static Border Attachment(string fileName, Action? remove = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 7, 9, 7), MinWidth = 130 };
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        frame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        var row = new DockPanel();
        var title = new TextBlock { Text = "📄  " + fileName, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.SecondaryForeground");
        if (remove is not null)
        {
            var button = YanziUi.WithStyle(new Button { Content = "×", MinWidth = 28, MinHeight = 26,
                ToolTip = "移除此附件" }, YanziUi.Styles.GhostButton);
            button.Click += (_, _) => remove();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        row.Children.Add(title);
        frame.Child = row;
        return frame;
    }

    public static T Direction<T>(T element, FlowDirection direction) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.FlowDirection = direction;
        return element;
    }
}
