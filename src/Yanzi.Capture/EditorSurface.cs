using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Yanzi.Capture;

public enum EditorTool
{
    Select,
    Rectangle,
    Arrow,
    Pen,
    Text,
    Mosaic
}

public sealed class EditorSurface : FrameworkElement
{
    private readonly CaptureDocument _document;
    private Annotation? _preview;
    private Annotation? _selected;
    private Point _start;
    private Point _last;
    private Vector _moved;
    private bool _drawing;
    private bool _moving;
    private double _scale = 1;
    private Vector _offset;
    private readonly List<Point> _penPoints = [];

    public EditorTool Tool { get; set; } = EditorTool.Select;
    public Brush StrokeBrush { get; set; } = Brushes.OrangeRed;
    public double StrokeThickness { get; set; } = 5;
    public double TextSize { get; set; } = 28;
    public Action<string>? StatusChanged { get; set; }
    public Func<Point, TextAnnotation?>? TextFactory { get; set; }
    public Annotation? Selected => _selected;

    public EditorSurface(CaptureDocument document)
    {
        _document = document;
        Focusable = true;
        Cursor = Cursors.Arrow;
        _document.History.Changed += () => InvalidateVisual();
        _document.Annotations.CollectionChanged += (_, _) => InvalidateVisual();

        MouseLeftButtonDown += OnPointerDown;
        MouseMove += OnPointerMove;
        MouseLeftButtonUp += OnPointerUp;
        KeyDown += OnKeyDown;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(17, 19, 24)),
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        var imageWidth = _document.BaseImage.PixelWidth;
        var imageHeight = _document.BaseImage.PixelHeight;
        _scale = Math.Min(
            Math.Max(0.05, (ActualWidth - 56) / imageWidth),
            Math.Max(0.05, (ActualHeight - 56) / imageHeight));
        _scale = Math.Min(_scale, 1.5);

        var shownWidth = imageWidth * _scale;
        var shownHeight = imageHeight * _scale;
        _offset = new Vector(
            Math.Max(28, (ActualWidth - shownWidth) / 2),
            Math.Max(28, (ActualHeight - shownHeight) / 2));

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(8, 9, 12)),
            null,
            new Rect(
                _offset.X - 8,
                _offset.Y - 8,
                shownWidth + 16,
                shownHeight + 16),
            12,
            12);

        dc.PushTransform(new TranslateTransform(_offset.X, _offset.Y));
        dc.PushTransform(new ScaleTransform(_scale, _scale));
        dc.DrawImage(
            _document.BaseImage,
            new Rect(0, 0, imageWidth, imageHeight));

        foreach (var annotation in _document.Annotations)
            annotation.Draw(dc);
        _preview?.Draw(dc);

        if (_selected != null && Tool == EditorTool.Select)
        {
            var bounds = _selected.Bounds;
            var pen = new Pen(
                new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                Math.Max(1.2, 1.6 / _scale))
            {
                DashStyle = DashStyles.Dash
            };
            dc.DrawRectangle(
                null,
                pen,
                new Rect(
                    bounds.X - 6 / _scale,
                    bounds.Y - 6 / _scale,
                    bounds.Width + 12 / _scale,
                    bounds.Height + 12 / _scale));
        }

        dc.Pop();
        dc.Pop();
    }

    private Point ToDocument(Point view) => new(
        (view.X - _offset.X) / _scale,
        (view.Y - _offset.Y) / _scale);

    private bool IsInsideImage(Point point) =>
        point.X >= 0 &&
        point.Y >= 0 &&
        point.X <= _document.BaseImage.PixelWidth &&
        point.Y <= _document.BaseImage.PixelHeight;

    private Point ClampToImage(Point point) => new(
        Math.Clamp(point.X, 0, _document.BaseImage.PixelWidth),
        Math.Clamp(point.Y, 0, _document.BaseImage.PixelHeight));

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var point = ToDocument(e.GetPosition(this));
        if (!IsInsideImage(point)) return;

        _start = _last = point;
        _moved = default;

        if (Tool == EditorTool.Select)
        {
            _selected = _document.Annotations
                .Reverse()
                .FirstOrDefault(x => x.HitTest(point));

            if (_selected != null)
            {
                _moving = true;
                Mouse.Capture(this);
                Cursor = Cursors.SizeAll;
                StatusChanged?.Invoke("拖动标注以调整位置，Delete 删除");
            }
            else
            {
                StatusChanged?.Invoke("选择一个标注后可以移动或删除");
            }

            InvalidateVisual();
            return;
        }

        if (Tool == EditorTool.Text)
        {
            var annotation = TextFactory?.Invoke(point);
            if (annotation != null)
            {
                _document.History.Execute(
                    new AddAnnotationCommand(_document, annotation));
                _selected = annotation;
                StatusChanged?.Invoke("已添加文字");
                InvalidateVisual();
            }
            return;
        }

        _drawing = true;
        Mouse.Capture(this);

        if (Tool == EditorTool.Pen)
        {
            _penPoints.Clear();
            _penPoints.Add(point);
            _preview = CreatePenPreview();
        }
        else
        {
            _preview = CreateShapePreview(_start, point);
        }

        InvalidateVisual();
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        var point = ClampToImage(ToDocument(e.GetPosition(this)));

        if (_drawing)
        {
            if (Tool == EditorTool.Pen)
            {
                if (_penPoints.Count == 0 ||
                    (point - _penPoints[^1]).Length >= 1.5)
                {
                    _penPoints.Add(point);
                    _preview = CreatePenPreview();
                }
            }
            else
            {
                _preview = CreateShapePreview(_start, point);
            }

            InvalidateVisual();
            return;
        }

        if (_moving && _selected != null)
        {
            var delta = point - _last;
            _selected.Move(delta);
            _moved += delta;
            _last = point;
            InvalidateVisual();
        }
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawing)
        {
            _drawing = false;
            Mouse.Capture(null);

            var end = ClampToImage(ToDocument(e.GetPosition(this)));
            Annotation? annotation;

            if (Tool == EditorTool.Pen)
            {
                if (_penPoints.Count == 0)
                    _penPoints.Add(end);
                annotation = CreatePenPreview();
                _penPoints.Clear();
            }
            else if (Tool == EditorTool.Mosaic)
            {
                var rect = Normalize(_start, end);
                annotation = rect.Width >= 8 && rect.Height >= 8
                    ? MosaicAnnotation.Create(_document.BaseImage, rect)
                    : null;
            }
            else
            {
                annotation = _preview;
            }

            _preview = null;

            if (annotation != null &&
                (annotation.Bounds.Width > 6 ||
                 annotation.Bounds.Height > 6))
            {
                _document.History.Execute(
                    new AddAnnotationCommand(_document, annotation));
                _selected = annotation;
                StatusChanged?.Invoke(Tool switch
                {
                    EditorTool.Arrow => "已添加箭头",
                    EditorTool.Rectangle => "已添加矩形",
                    EditorTool.Pen => "已添加画笔",
                    EditorTool.Mosaic => "已添加马赛克",
                    _ => "已添加标注"
                });
            }

            InvalidateVisual();
            return;
        }

        if (_moving && _selected != null)
        {
            _moving = false;
            Mouse.Capture(null);
            Cursor = Cursors.Arrow;

            if (_moved.Length > 0.2)
                _document.History.RecordExecuted(
                    new MoveAnnotationCommand(_selected, _moved));

            _moved = default;
            InvalidateVisual();
        }
    }

    private Annotation CreateShapePreview(Point a, Point b)
    {
        if (Tool == EditorTool.Arrow)
        {
            return new ArrowAnnotation(a, b)
            {
                Stroke = StrokeBrush,
                Thickness = StrokeThickness
            };
        }

        if (Tool == EditorTool.Mosaic)
        {
            return new RectangleAnnotation(new Rect(a, b))
            {
                Stroke = new SolidColorBrush(Color.FromRgb(155, 166, 182)),
                Thickness = 2
            };
        }

        return new RectangleAnnotation(new Rect(a, b))
        {
            Stroke = StrokeBrush,
            Thickness = StrokeThickness
        };
    }

    private PenAnnotation CreatePenPreview() => new(_penPoints)
    {
        Stroke = StrokeBrush,
        Thickness = StrokeThickness
    };

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selected != null)
        {
            var old = _selected;
            _selected = null;
            _document.History.Execute(
                new RemoveAnnotationCommand(_document, old));
            StatusChanged?.Invoke("已删除标注");
            e.Handled = true;
        }
        else if (e.Key == Key.Z &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _document.History.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _document.History.Redo();
            e.Handled = true;
        }
    }

    public void SelectAnnotation(Annotation? annotation)
    {
        _selected = annotation;
        InvalidateVisual();
    }

    private static Rect Normalize(Point a, Point b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X),
        Math.Abs(a.Y - b.Y));
}
