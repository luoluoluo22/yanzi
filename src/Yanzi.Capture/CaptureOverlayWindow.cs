using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Yanzi.Capture;

public sealed class CaptureOverlayWindow : Window
{
    private BitmapSource? _screen;
    private readonly bool _selectionOnly;
    private readonly Canvas _canvas = new();
    private readonly Image _screenImage = new();
    private readonly Rectangle _selection = new();
    private ImageBrush? _selectionBrush;
    private readonly TextBlock _hint = new();
    private readonly TextBlock _sizeBadge = new();
    private readonly Canvas _crosshair = new()
    {
        Width = 32,
        Height = 32,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private Point _start;
    private bool _dragging;
    private double _scaleX = 1;
    private double _scaleY = 1;

    public event Action<BitmapSource>? Captured;
    public event Action<Int32Rect>? RegionSelected;
    public event Action? Cancelled;

    public CaptureOverlayWindow(BitmapSource screen, bool selectionOnly = false)
    {
        _screen = screen;
        _selectionOnly = selectionOnly;
        CaptureDiagnostics.Mark("overlay.ctor.begin",
            ("screenWidth", screen.PixelWidth),
            ("screenHeight", screen.PixelHeight));
        Title = "燕子截图";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        // WPF's default dark crosshair disappears against the dimmed screenshot.
        // Render a high-contrast blue reticle above the overlay instead.
        Cursor = Cursors.None;
        Left = 0;
        Top = 0;
        SizeToContent = SizeToContent.Manual;

        // Set the final logical size before HWND creation. Previously the
        // window first appeared at WPF's auto/default size (1920x1029 on a
        // 2560x1440 desktop) and was resized in Loaded, causing a visible
        // full-screen jump and an extra compositor surface allocation.
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        var root = new Grid();
        _screenImage.Source = _screen;
        _screenImage.Stretch = Stretch.Fill;
        root.Children.Add(_screenImage);
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(122, 0, 0, 0))
        });

        _selectionBrush = new ImageBrush(_screen)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute
        };
        _selection.Fill = _selectionBrush;
        _selection.Stroke = Brushes.White;
        _selection.StrokeThickness = 1.5;
        _selection.Visibility = Visibility.Collapsed;
        _selection.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 14,
            Opacity = 0.5,
            ShadowDepth = 0
        };
        _canvas.Children.Add(_selection);

        _hint.Text = _selectionOnly
            ? "拖动选择录制区域  ·  Esc 取消"
            : "拖动选择截图区域  ·  Esc 取消";
        _hint.Foreground = Brushes.White;
        _hint.Background = new SolidColorBrush(Color.FromArgb(205, 20, 22, 27));
        _hint.Padding = new Thickness(14, 8, 14, 8);
        _hint.FontSize = 13;
        Canvas.SetLeft(_hint, 24);
        Canvas.SetTop(_hint, 24);
        _canvas.Children.Add(_hint);

        _sizeBadge.Foreground = Brushes.White;
        _sizeBadge.Background = new SolidColorBrush(Color.FromArgb(220, 18, 21, 27));
        _sizeBadge.Padding = new Thickness(9, 5, 9, 5);
        _sizeBadge.FontSize = 11;
        _sizeBadge.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_sizeBadge);

        var crosshairGeometry = Geometry.Parse(
            "M 16,1 L 16,11 M 16,21 L 16,31 M 1,16 L 11,16 M 21,16 L 31,16");
        _crosshair.Children.Add(new Path
        {
            Data = crosshairGeometry,
            Stroke = Brushes.White,
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        _crosshair.Children.Add(new Path
        {
            Data = crosshairGeometry,
            Stroke = new SolidColorBrush(Color.FromRgb(0, 163, 255)),
            StrokeThickness = 2.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        var center = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(0, 163, 255)),
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(center, 13);
        Canvas.SetTop(center, 13);
        _crosshair.Children.Add(center);
        _canvas.Children.Add(_crosshair);

        root.Children.Add(_canvas);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            CaptureDiagnostics.Mark("overlay.sourceInitialized",
                ("width", Width),
                ("height", Height),
                ("actualWidth", ActualWidth),
                ("actualHeight", ActualHeight));
        };

        ContentRendered += (_, _) =>
        {
            CaptureDiagnostics.Mark("overlay.contentRendered",
                ("actualWidth", ActualWidth),
                ("actualHeight", ActualHeight));
        };

        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _scaleX = _screen.PixelWidth / Math.Max(1, ActualWidth);
            _scaleY = _screen.PixelHeight / Math.Max(1, ActualHeight);

            CaptureDiagnostics.Mark("overlay.loaded",
                ("dpiX", dpi.DpiScaleX),
                ("dpiY", dpi.DpiScaleY),
                ("width", Width),
                ("height", Height),
                ("actualWidth", ActualWidth),
                ("actualHeight", ActualHeight),
                ("scaleX", _scaleX),
                ("scaleY", _scaleY));

            Activate();
            UpdateCrosshair(Mouse.GetPosition(_canvas));
            CaptureDiagnostics.Mark("overlay.activated");
        };
        SizeChanged += (_, _) =>
        {
            _scaleX = _screen.PixelWidth / Math.Max(1, ActualWidth);
            _scaleY = _screen.PixelHeight / Math.Max(1, ActualHeight);
        };
        MouseLeftButtonDown += BeginSelection;
        MouseMove += UpdateSelection;
        MouseLeave += (_, _) => _crosshair.Visibility = Visibility.Collapsed;
        MouseLeftButtonUp += CompleteSelection;
        CaptureDiagnostics.Mark("overlay.ctor.end",
            ("width", Width),
            ("height", Height));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                Cancelled?.Invoke();
            }
        };
    }

    private void BeginSelection(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        _dragging = true;
        Mouse.Capture(this);
        _selection.Visibility = Visibility.Visible;
        UpdateSelectionVisual(_start, _start);
    }

    private void UpdateSelection(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        UpdateCrosshair(position);
        if (!_dragging) return;
        UpdateSelectionVisual(_start, position);
    }

    private void UpdateCrosshair(Point position)
    {
        if (position.X < 0 || position.Y < 0 ||
            position.X > _canvas.ActualWidth || position.Y > _canvas.ActualHeight)
        {
            _crosshair.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(_crosshair, position.X - 16);
        Canvas.SetTop(_crosshair, position.Y - 16);
        _crosshair.Visibility = Visibility.Visible;
    }

    private void CompleteSelection(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Mouse.Capture(null);
        var end = e.GetPosition(_canvas);
        var dipRect = Normalize(_start, end);
        if (dipRect.Width < 8 || dipRect.Height < 8)
        {
            _selection.Visibility = Visibility.Collapsed;
            return;
        }

        var pixels = new Int32Rect(
            (int)Math.Round(dipRect.X * _scaleX),
            (int)Math.Round(dipRect.Y * _scaleY),
            Math.Max(1, (int)Math.Round(dipRect.Width * _scaleX)),
            Math.Max(1, (int)Math.Round(dipRect.Height * _scaleY)));

        var screen = _screen;
        if (screen is null)
            return;

        if (_selectionOnly)
        {
            ReleaseVisualResources();
            Close();
            RegionSelected?.Invoke(pixels);
            return;
        }

        var capture = ScreenCaptureService.Crop(screen, pixels);
        ReleaseVisualResources();
        Close();
        Captured?.Invoke(capture);
    }

    private void UpdateSelectionVisual(Point a, Point b)
    {
        var rect = Normalize(a, b);
        Canvas.SetLeft(_selection, rect.X);
        Canvas.SetTop(_selection, rect.Y);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;
        if (_selectionBrush is not null)
        {
            _selectionBrush.Viewbox = new Rect(
                rect.X * _scaleX,
                rect.Y * _scaleY,
                Math.Max(1, rect.Width * _scaleX),
                Math.Max(1, rect.Height * _scaleY));
        }
    }

    private void ReleaseVisualResources()
    {
        CaptureDiagnostics.Mark("overlay.release.begin");
        _screenImage.Source = null;
        _selection.Fill = null;
        _selectionBrush = null;
        _screen = null;
        Content = null;
        CaptureDiagnostics.Mark("overlay.release.end");
    }

    private static Rect Normalize(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
