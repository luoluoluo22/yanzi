using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>Reusable WPF indeterminate circular loading indicator. Animation runs only while loaded.</summary>
public sealed class YanziLoadingRing : UserControl
{
    private readonly RotateTransform _rotation = new();
    private readonly DoubleAnimation _animation = new(0, 360, TimeSpan.FromSeconds(0.85))
    {
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = null
    };

    public YanziLoadingRing()
    {
        Width = 24;
        Height = 24;
        IsHitTestVisible = false;
        var root = new Viewbox { Stretch = Stretch.Uniform };
        var canvas = new Canvas { Width = 24, Height = 24, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _rotation };
        var path = new Path
        {
            Data = Geometry.Parse("M 12,3 A 9,9 0 1 1 3,12"),
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true
        };
        path.SetResourceReference(Shape.StrokeProperty, "Yanzi.Brush.Accent");
        canvas.Children.Add(path);
        root.Child = canvas;
        Content = root;
        Loaded += (_, _) => _rotation.BeginAnimation(RotateTransform.AngleProperty, _animation);
        Unloaded += (_, _) => _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}
