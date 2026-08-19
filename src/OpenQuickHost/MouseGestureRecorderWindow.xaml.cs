using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenQuickHost.Sync;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Key = System.Windows.Input.Key;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Canvas = System.Windows.Controls.Canvas;

namespace OpenQuickHost;

/// <summary>
/// 全屏透明蒙版的鼠标手势录制器。
/// 按住右键 / 中键拖动 → 实时画橘色轨迹 → 松开后量化成 8 方向序列。
/// </summary>
public partial class MouseGestureRecorderWindow : Window
{
    private readonly string _trigger; // right-drag / middle-drag
    private readonly List<Point> _path = new();
    private readonly List<UIElement> _strokeElements = new();
    private bool _drawing;
    private PathFigure? _strokeFigure;

    private static readonly string[] Arrows = ["→","↘","↓","↙","←","↖","↑","↗"];
    private const int MinSegmentDistance = 30; // 单段最小像素距离
    private const double TwoPi = Math.PI * 2;
    private const double EightthPi = Math.PI / 4;

    public string ResultSequence { get; private set; } = string.Empty;
    public string ResultTrigger { get; private set; } = "right-drag";
    public string ResultSign { get; private set; } = string.Empty;
    public int[]? ResultTemplateData { get; private set; }
    public bool WasAccepted { get; private set; }

    /// <summary>
    /// 已注册手势集合，用于显示冲突提示。Key=trigger+"|"+sequence，Value=扩展显示名称列表。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? KnownGestures { get; init; }

    public MouseGestureRecorderWindow(string trigger, string? initialSequence)
    {
        InitializeComponent();
        _trigger = NormalizeTrigger(trigger);
        ResultTrigger = _trigger;
        TriggerLabelRun.Text = _trigger == "middle-drag" ? "鼠标中键" : "鼠标右键";

        // 占满主屏（用 SystemParameters，避免多屏问题）
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        if (!string.IsNullOrWhiteSpace(initialSequence))
        {
            ResultSequence = initialSequence!;
            ResultSign = MouseGestureNaming.GetDisplayName(ResultSequence);
            GestureNameText.Text = ResultSign;
        }

        PreviewMouseDown += OnMouseDownAny;
        PreviewMouseMove += OnMouseMoveAny;
        PreviewMouseUp += OnMouseUpAny;
        PreviewMouseRightButtonDown += OnRightButtonDown;
        PreviewMouseRightButtonUp += OnRightButtonUp;
        KeyDown += OnKeyDownAny;

        // ContextMenu 默认会在右键弹出，这里抑制
        StrokeCanvas.MouseRightButtonUp += (_, e) => e.Handled = true;
    }

    private void OnKeyDownAny(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            WasAccepted = false;
            Close();
        }
    }

    private void OnRightButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger != "right-drag") return;
        StartStroke(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnRightButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger != "right-drag") return;
        FinishStroke();
        e.Handled = true;
    }

    private void OnMouseDownAny(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger == "middle-drag" && e.ChangedButton == MouseButton.Middle)
        {
            StartStroke(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMouseUpAny(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger == "middle-drag" && e.ChangedButton == MouseButton.Middle)
        {
            FinishStroke();
            e.Handled = true;
        }
    }

    private void OnMouseMoveAny(object? sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var pt = e.GetPosition(this);
        if (_path.Count > 0)
        {
            var last = _path[^1];
            if ((pt - last).Length < 3.0) return; // 抖动过滤
            AppendStrokePoint(pt);
        }
        _path.Add(pt);
    }

    private void StartStroke(Point start)
    {
        ClearStroke();
        _drawing = true;
        _path.Add(start);
        ResultPanel.Visibility = Visibility.Collapsed;
        HintRing.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));
        HintText.Text = "正在录制… 松开即可识别";

        // 起点发光标记
        var dot = new Ellipse
        {
            Width = 14, Height = 14,
            Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)),
            Stroke = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(dot, start.X - 7);
        Canvas.SetTop(dot, start.Y - 7);
        StrokeCanvas.Children.Add(dot);
        _strokeElements.Add(dot);

        _strokeFigure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(_strokeFigure);

        // 外层柔光
        var glowPath = new Path
        {
            Data = pathGeometry,
            Stroke = new SolidColorBrush(Color.FromArgb(90, 0xFB, 0x92, 0x3C)),
            StrokeThickness = 12,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        // 内层高亮核心
        var corePath = new Path
        {
            Data = pathGeometry,
            Stroke = new SolidColorBrush(Color.FromArgb(240, 0xFB, 0x92, 0x3C)),
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        StrokeCanvas.Children.Add(glowPath);
        StrokeCanvas.Children.Add(corePath);
        _strokeElements.Add(glowPath);
        _strokeElements.Add(corePath);
    }

    private void AppendStrokePoint(Point point)
    {
        if (_strokeFigure == null) return;
        if (_path.Count == 1)
        {
            _strokeFigure.Segments.Add(new LineSegment(point, isStroked: true));
        }
        else
        {
            var p1 = _path[^1];
            var mid = new Point((p1.X + point.X) / 2.0, (p1.Y + point.Y) / 2.0);
            _strokeFigure.Segments.Add(new QuadraticBezierSegment(p1, mid, isStroked: true));
        }
    }

    private void FinishStroke()
    {
        if (!_drawing) return;
        _drawing = false;
        HintRing.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        HintText.Text = "可以重新录制 · 也可保存";
        // 终点标记
        if (_path.Count > 0)
        {
            var last = _path[^1];
            var dot = new Ellipse
            {
                Width = 16, Height = 16,
                Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                Stroke = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(dot, last.X - 8);
            Canvas.SetTop(dot, last.Y - 8);
            StrokeCanvas.Children.Add(dot);
            _strokeElements.Add(dot);
        }

        var sequence = SimplifyPath(_path);
        if (sequence.Length == 0)
        {
            ResultSequence = string.Empty;
            ResultSign = string.Empty;
            ResultTemplateData = null;
            GestureNameText.Text = "未识别";
            ArrowList.ItemsSource = new[] { "?" };
            RawSeqText.Text = "轨迹太短或未识别，请再试一次";
            ConflictText.Text = string.Empty;
        }
        else
        {
            ResultSequence = sequence;
            ResultSign = MouseGestureNaming.GetDisplayName(sequence, _path);
            ResultTemplateData = MouseGestureTemplateRecognizer.CreateTemplateData(_path);
            var builtIn = MouseGestureTemplateRecognizer.RecognizeBuiltInSign(_path);
            GestureNameText.Text = string.IsNullOrWhiteSpace(builtIn) ? ResultSign : $"{ResultSign} (特征识别)";
            ArrowList.ItemsSource = sequence.ToCharArray();
            RawSeqText.Text = sequence;
            UpdateConflictHint();
        }
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void ClearStroke()
    {
        foreach (var el in _strokeElements)
        {
            StrokeCanvas.Children.Remove(el);
        }
        _strokeElements.Clear();
        _path.Clear();
        _strokeFigure = null;
    }

    private void UpdateConflictHint()
    {
        if (KnownGestures == null || string.IsNullOrEmpty(ResultSequence))
        {
            ConflictText.Text = string.Empty;
            return;
        }
        var key = $"{_trigger}|{ResultSequence}";
        if (KnownGestures.TryGetValue(key, out var owners) && owners.Count > 0)
        {
            ConflictText.Text = $"⚠ 与已有扩展冲突：{string.Join("、", owners)}。保存后多扩展共用同序列时将以选择气泡确认。";
            ConflictText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));
        }
        else
        {
            ConflictText.Text = "✓ 没有冲突，这是一个新手势";
            ConflictText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        }
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        ClearStroke();
        ResultSequence = string.Empty;
        ResultSign = string.Empty;
        ResultTemplateData = null;
        GestureNameText.Text = string.Empty;
        ResultPanel.Visibility = Visibility.Collapsed;
        HintRing.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        HintText.Text = "准备就绪 · 按住右键开始";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasAccepted = false;
        DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResultSequence))
        {
            CancelButton_Click(sender, e);
            return;
        }
        WasAccepted = true;
        DialogResult = true;
    }

    private static string SimplifyPath(IReadOnlyList<Point> pts)
    {
        return MouseGestureTemplateRecognizer.ExtractSequence(pts, minStepDistance: MinSegmentDistance);
    }

    private static string NormalizeTrigger(string? raw)
    {
        return raw switch
        {
            "middle-drag" => "middle-drag",
            _ => "right-drag"
        };
    }
}
