using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>Pixel-consistent vector icons for native WPF controls, independent of font metrics.</summary>
public static class YanziIcons
{
    public static FrameworkElement ChevronUp(double size = 16)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var drawing = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var chevron = new Path
        {
            Data = Geometry.Parse("M6,15 L12,9 L18,15"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        chevron.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Foreground");
        drawing.Children.Add(chevron);
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = drawing,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
    }
}
