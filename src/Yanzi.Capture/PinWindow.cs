using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yanzi.Capture;

public sealed class PinWindow : Window
{
    private const double MinScale = 0.10;
    private const double MaxScale = 4.00;
    private const double WheelScaleStep = 1.10;

    private readonly double _imageWidth;
    private readonly double _imageHeight;
    private double _scale;

    public PinWindow(BitmapSource bitmap)
    {
        Title = "燕子贴图";

        _imageWidth = Math.Max(1, bitmap.Width);
        _imageHeight = Math.Max(1, bitmap.Height);
        _scale = Math.Min(1.0, Math.Min(720 / _imageWidth, 520 / _imageHeight));

        Width = _imageWidth * _scale;
        Height = _imageHeight * _scale;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        Content = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true
        };

        PreviewMouseWheel += (_, e) =>
        {
            var factor = e.Delta > 0 ? WheelScaleStep : 1.0 / WheelScaleStep;
            var nextScale = Math.Clamp(_scale * factor, MinScale, MaxScale);
            if (Math.Abs(nextScale - _scale) < 0.0001)
            {
                e.Handled = true;
                return;
            }

            var pointer = e.GetPosition(this);
            var anchorX = ActualWidth > 0 ? pointer.X / ActualWidth : 0.5;
            var anchorY = ActualHeight > 0 ? pointer.Y / ActualHeight : 0.5;

            var nextWidth = _imageWidth * nextScale;
            var nextHeight = _imageHeight * nextScale;

            Left += pointer.X - (anchorX * nextWidth);
            Top += pointer.Y - (anchorY * nextHeight);
            Width = nextWidth;
            Height = nextHeight;
            _scale = nextScale;

            e.Handled = true;
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                Close();
                return;
            }

            try { DragMove(); } catch { }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }
}
