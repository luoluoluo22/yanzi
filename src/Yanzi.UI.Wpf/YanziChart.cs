using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Local, theme-aware chart with bar, line, area and pie variants.
/// Uses WPF geometry for data display (not native chart-kit chrome).
/// All modes support point selection, localized tooltips and accessible labels.
/// </summary>
public enum YanziChartKind { Bar, Line, Area, Pie }

public sealed class YanziChart : UserControl
{
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly TextBlock _details = new()
    {
        FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };
    private readonly WrapPanel _legend = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 4, 0, 3)
    };
    private YanziBarPoint[] _points = [];
    private YanziChartKind _kind = YanziChartKind.Bar;
    private int _selectedIndex = -1;
    private readonly List<FrameworkElement> _hitTargets = [];
    private readonly Dictionary<int, FrameworkElement> _keyboardTargets = [];

    public YanziChartKind Kind
    {
        get => _kind;
        set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _kind = value; Render(); }
    }
    public int PointCount => _points.Length;
    public int SelectedIndex => _selectedIndex;
    public IReadOnlyList<YanziBarPoint> Points => _points;
    public int RenderedPointCount => _hitTargets.Count;
    public event EventHandler<int>? SelectedPointChanged;

    public YanziChart()
    {
        Width = 330;
        Height = 200;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(_canvas);
        _details.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        Grid.SetRow(_details, 1);
        layout.Children.Add(_details);
        Grid.SetRow(_legend, 2);
        layout.Children.Add(_legend);
        Content = layout;
        _canvas.SizeChanged += (_, _) => Render();
        Render();
    }

    public void SetData(IEnumerable<YanziBarPoint> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var data = values.ToArray();
        if (data.Any(p => string.IsNullOrWhiteSpace(p.Label)))
            throw new ArgumentException("Every point needs a label.", nameof(values));
        if (data.Any(p => !double.IsFinite(p.Value) || p.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(values), "Values must be non-negative finite numbers.");
        _points = data;
        _selectedIndex = -1;
        Render();
    }

    public void SelectPoint(int index)
    {
        if (index < 0 || index >= _points.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var retainFocus = _keyboardTargets.Values.Any(target => target.IsKeyboardFocusWithin);
        _selectedIndex = index;
        UpdateDetails();
        Render();
        if (retainFocus && _keyboardTargets.TryGetValue(index, out var current))
            current.Focus();
        SelectedPointChanged?.Invoke(this, index);
    }

    private void UpdateDetails()
    {
        _details.Text = _selectedIndex >= 0 && _selectedIndex < _points.Length
            ? $"{_points[_selectedIndex].Label}：{_points[_selectedIndex].Value:0.##}"
            : "点击图形查看数据";
    }

    private static T Theme<T>(T element, DependencyProperty property, string name) where T : FrameworkElement
    {
        element.SetResourceReference(property, name);
        return element;
    }

    private static void Put(Canvas parent, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        parent.Children.Add(element);
    }

    private void BindKeyboardPoint(Button button, int index)
    {
        button.Focusable = true;
        KeyboardNavigation.SetIsTabStop(button, true);
        AutomationProperties.SetName(button,
            $"{_points[index].Label}：{_points[index].Value:0.##}");
        button.Click += (_, _) => SelectPoint(index);
        button.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Right or Key.Down)
            {
                SelectPoint((index + 1) % _points.Length);
                e.Handled = true;
            }
            else if (e.Key is Key.Left or Key.Up)
            {
                SelectPoint((index + _points.Length - 1) % _points.Length);
                e.Handled = true;
            }
        };
        _keyboardTargets[index] = button;
    }

    private ToolTip PointTooltip(int index) => YanziUi.WithStyle(new ToolTip
    {
        Content = $"{_points[index].Label}：{_points[index].Value:0.##}"
    }, YanziUi.Styles.Tooltip);

    private void AddClickable(FrameworkElement shape, int index, double x, double y)
    {
        shape.ToolTip = PointTooltip(index);
        shape.Cursor = Cursors.Hand;
        shape.MouseLeftButtonUp += (_, e) => { SelectPoint(index); e.Handled = true; };
        _hitTargets.Add(shape);
        Put(_canvas, shape, x, y);

        // Draw sectors remain real vector geometry; their keyboard equivalent is
        // the interactive legend, as a full-circle overlay would steal pointer input.
        if (Kind == YanziChartKind.Pie) return;

        var width = Math.Max(24, shape.Width);
        var height = Math.Max(24, shape.Height);
        var target = YanziUi.WithStyle(new Button
        {
            Width = width, Height = height,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = PointTooltip(index)
        }, "Yanzi.Chart.HitTarget");
        BindKeyboardPoint(target, index);
        Put(_canvas, target,
            x - (width - shape.Width) / 2,
            y - (height - shape.Height) / 2);
    }

    private void Render()
    {
        if (_canvas is null) return;
        _canvas.Children.Clear();
        _hitTargets.Clear();
        _keyboardTargets.Clear();
        _legend.Children.Clear();
        _legend.Visibility = Kind == YanziChartKind.Pie ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
        if (_points.Length == 0)
        {
            var empty = new TextBlock { Text = "暂无数据", FontSize = 13 };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Put(_canvas, empty, 14, 14);
            return;
        }
        var width = Math.Max(140, double.IsFinite(_canvas.ActualWidth) && _canvas.ActualWidth > 0
            ? _canvas.ActualWidth : Width > 0 ? Width : 330);
        var height = Math.Max(105, double.IsFinite(_canvas.ActualHeight) && _canvas.ActualHeight > 0
            ? _canvas.ActualHeight : Height > 0 ? Height - 25 : 170);
        if (Kind == YanziChartKind.Pie)
            DrawPie(width, height);
        else
            DrawCartesian(width, height);
    }

    private void DrawCartesian(double width, double height)
    {
        var left = 32d;
        var right = width - 12;
        var top = 14d;
        var bottom = height - 25;
        var graphWidth = Math.Max(30, right - left);
        var graphHeight = Math.Max(30, bottom - top);
        var peak = Math.Max(1, _points.Max(x => x.Value)) * 1.12;
        for (var i = 0; i <= 3; i++)
        {
            var level = peak * (3 - i) / 3;
            var y = top + graphHeight * i / 3;
            var guide = new Line { X1 = left, X2 = right, Y1 = y, Y2 = y,
                StrokeThickness = .75, Opacity = .65 };
            guide.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Border");
            _canvas.Children.Add(guide);
            var tick = new TextBlock { Text = level.ToString("0.#"), FontSize = 9 };
            tick.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Put(_canvas, tick, 0, y - 7);
        }
        var step = graphWidth / _points.Length;
        var positions = new List<Point>();
        for (var i = 0; i < _points.Length; i++)
        {
            var point = _points[i];
            var x = left + step * (i + .5);
            var y = bottom - point.Value / peak * graphHeight;
            positions.Add(new Point(x, y));
            var label = new TextBlock { Text = point.Label, FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = Math.Max(25, step - 2) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Put(_canvas, label, x - Math.Min(step / 2, 22), bottom + 7);
        }

        if (Kind == YanziChartKind.Area && positions.Count > 0)
        {
            var area = new Polygon { Opacity = .22, StrokeThickness = 0 };
            area.SetResourceReference(Shape.FillProperty, "Yanzi.Color.Primary");
            area.Points.Add(new Point(positions[0].X, bottom));
            foreach (var pt in positions) area.Points.Add(pt);
            area.Points.Add(new Point(positions[^1].X, bottom));
            _canvas.Children.Add(area);
        }
        if (Kind is YanziChartKind.Line or YanziChartKind.Area)
        {
            for (var i = 1; i < positions.Count; i++)
            {
                var line = new Line
                {
                    X1 = positions[i-1].X, Y1 = positions[i-1].Y,
                    X2 = positions[i].X, Y2 = positions[i].Y,
                    StrokeThickness = 2.2
                };
                line.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Primary");
                _canvas.Children.Add(line);
            }
        }
        for (var i = 0; i < _points.Length; i++)
        {
            var pt = positions[i];
            if (Kind == YanziChartKind.Bar)
            {
                var barWidth = Math.Min(34, Math.Max(5, step * .65));
                var barHeight = Math.Max(2, bottom - pt.Y);
                var bar = new Border
                {
                    Width = barWidth, Height = barHeight,
                    CornerRadius = new CornerRadius(4,4,0,0)
                };
                bar.SetResourceReference(Border.BackgroundProperty,
                    i == _selectedIndex ? "Yanzi.Color.Primary" : "Yanzi.Color.ChartBar");
                AddClickable(bar, i, pt.X - barWidth / 2, bottom - barHeight);
            }
            else
            {
                var dot = new Ellipse
                {
                    Width = i == _selectedIndex ? 13 : 10,
                    Height = i == _selectedIndex ? 13 : 10,
                    StrokeThickness = 2.1
                };
                dot.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Primary");
                dot.SetResourceReference(Shape.FillProperty,
                    i == _selectedIndex ? "Yanzi.Color.Primary" : "Yanzi.Color.Card");
                AddClickable(dot, i, pt.X - dot.Width/2, pt.Y - dot.Height/2);
            }
        }
    }

    private void DrawPie(double width, double height)
    {
        var sum = _points.Sum(x => x.Value);
        if (sum <= 0)
        {
            var empty = new TextBlock { Text = "总量为 0", FontSize = 13 };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            Put(_canvas, empty, 14, 14);
            return;
        }
        var radius = Math.Max(25, Math.Min(height - 22, width * .55) / 2);
        var center = new Point(width / 2, height / 2);
        var angle = -90d;
        for (var i = 0; i < _points.Length; i++)
        {
            var point = _points[i];
            if (point.Value <= 0) continue;
            var sweep = point.Value / sum * 360;
            var shape = new Path { StrokeThickness = 1.4 };
            var colorKey = $"Yanzi.Color.Chart{1 + i % 5}";
            shape.SetResourceReference(Shape.FillProperty, colorKey);
            shape.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Background");
            if (sweep >= 359.999)
            {
                shape.Data = new EllipseGeometry(center, radius, radius);
            }
            else
            {
                Point at(double degrees) => new(center.X + radius * Math.Cos(degrees * Math.PI / 180),
                    center.Y + radius * Math.Sin(degrees * Math.PI / 180));
                var figure = new PathFigure { StartPoint = center, IsClosed = true, IsFilled = true };
                figure.Segments.Add(new LineSegment(at(angle), true));
                figure.Segments.Add(new ArcSegment(at(angle + sweep), new Size(radius, radius),
                    0, sweep > 180, SweepDirection.Clockwise, true));
                figure.Segments.Add(new LineSegment(center, true));
                shape.Data = new PathGeometry(new[] { figure });
            }
            AddClickable(shape, i, 0, 0);
            var legendItem = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(6, 1, 6, 1)
            };
            var marker = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            marker.SetResourceReference(Border.BackgroundProperty, colorKey);
            legendItem.Children.Add(marker);
            var legendLabel = new TextBlock
            {
                Text = $"{point.Label} {point.Value / sum:P0}",
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = i == _selectedIndex ? FontWeights.SemiBold : FontWeights.Normal
            };
            legendLabel.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            legendItem.Children.Add(legendLabel);
            var legendButton = YanziUi.WithStyle(new Button
            {
                Content = legendItem,
                Padding = new Thickness(5, 3, 5, 3),
                MinHeight = 25,
                Margin = new Thickness(0, 0, 2, 0)
            }, YanziUi.Styles.GhostButton);
            BindKeyboardPoint(legendButton, i);
            _legend.Children.Add(legendButton);
            angle += sweep;
        }
    }
}
