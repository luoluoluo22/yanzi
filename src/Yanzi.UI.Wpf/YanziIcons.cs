using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>Pixel-consistent vector icons for native WPF controls, independent of font metrics.</summary>
public static class YanziIcons
{
    /// <summary>Render any simple Lucide-style 24-unit stroked path using theme tokens.</summary>
    public static FrameworkElement StrokeIcon(string pathGeometry, double size = 16)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var path = new Path
        {
            Data = Geometry.Parse(pathGeometry),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        path.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Foreground");
        canvas.Children.Add(path);
        return IconFrame(canvas, size);
    }

    /// <summary>Font-independent, theme-aware check from the same 24-unit Lucide grid.</summary>
    public static FrameworkElement Check(double size = 14)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var path = new Path
        {
            Data = Geometry.Parse("M20,6 L9,17 L4,12"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        path.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Foreground");
        canvas.Children.Add(path);
        return IconFrame(canvas, size);
    }

    /// <summary>A crisp filled selection marker; never depends on Unicode glyph fallback.</summary>
    public static FrameworkElement RadioDot(double size = 12)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var circle = new Ellipse { Width = 12, Height = 12 };
        circle.SetResourceReference(Shape.FillProperty, "Yanzi.Color.Foreground");
        Canvas.SetLeft(circle, 6);
        Canvas.SetTop(circle, 6);
        canvas.Children.Add(circle);
        return IconFrame(canvas, size);
    }

    public static FrameworkElement ChevronRight(double size = 14)
    {
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var path = new Path
        {
            Data = Geometry.Parse("M9,18 L15,12 L9,6"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        path.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Foreground");
        canvas.Children.Add(path);
        return IconFrame(canvas, size);
    }

    private static FrameworkElement IconFrame(Canvas canvas, double size) => new Viewbox
    {
        Width = size, Height = size, Child = canvas,
        IsHitTestVisible = false, Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true
    };

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
