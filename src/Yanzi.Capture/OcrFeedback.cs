using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Yanzi.Capture;

/// <summary>
/// Independent, non-activating OCR progress/completion notification.
/// Recognition, clipboard write and feedback rendering are separate outcomes.
/// </summary>
public static class OcrFeedback
{
    private static OcrToastWindow? _currentToast;

    public static void BeginRecognizing()
    {
        TryShow(() =>
        {
            _currentToast?.Close();
            var toast = new OcrToastWindow();
            _currentToast = toast;
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_currentToast, toast))
                    _currentToast = null;
            };
            toast.Show();
        }, "ocr.toast.loading.failed");
    }

    public static async Task PublishAsync(CaptureOcrResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            Complete("未识别到文字", "请尝试选择更清晰的文字区域",
                null, result.Engine, false);
            return;
        }

        // Native SetClipboardData success is authoritative; no OleFlushClipboard
        // post-write exception and no potentially stale clipboard readback.
        var write = await CaptureTextClipboard.WriteAsync(result.Text);
        if (write.Success)
        {
            CaptureDiagnostics.Mark("ocr.clipboard.copied",
                ("chars", result.Text.Length), ("backend", "win32"),
                ("attempts", write.Attempts));
            Complete("文字已复制", "点击查看识别结果 · 3 秒后关闭",
                result.Text, result.Engine, true);
        }
        else
        {
            CaptureDiagnostics.Mark("ocr.clipboard.failed",
                ("chars", result.Text.Length), ("backend", "win32"),
                ("attempts", write.Attempts), ("reason", write.Detail));
            Complete("已识别，未能复制", "点击查看文字并手动复制",
                result.Text, result.Engine, false);
        }
    }

    public static void ShowFailure(Exception error)
    {
        CaptureDiagnostics.Mark("ocr.recognition.failed",
            ("error", error.GetType().Name), ("message", error.Message));
        Complete("OCR 识别失败", "点击查看失败原因",
            "OCR 失败：" + error.Message, "燕子截图", false);
    }

    private static void Complete(string title, string subtitle,
        string? details, string engine, bool successful)
    {
        TryShow(() =>
        {
            if (_currentToast is { IsVisible: true } toast)
            {
                toast.Complete(title, subtitle, details, engine, successful);
                return;
            }

            var next = new OcrToastWindow();
            _currentToast = next;
            next.Closed += (_, _) =>
            {
                if (ReferenceEquals(_currentToast, next))
                    _currentToast = null;
            };
            next.Complete(title, subtitle, details, engine, successful);
            next.Show();
        }, "ocr.toast.completion.failed");
    }

    private static void TryShow(Action action, string diagnostic)
    {
        try { action(); }
        catch (Exception ex)
        {
            // A display problem must never turn successful OCR/copy into OCR failure.
            CaptureDiagnostics.Mark(diagnostic,
                ("error", ex.GetType().Name), ("message", ex.Message));
        }
    }
}

internal sealed class OcrToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _title = new();
    private readonly TextBlock _description = new();
    private readonly TextBlock _symbol = new();
    private readonly System.Windows.Shapes.Path _spinner;
    private string? _details;
    private string _engine = string.Empty;
    private bool _completed;

    public OcrToastWindow()
    {
        Title = "OCR 正在识别";
        Width = 312;
        Height = 82;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 20);
        Top = Math.Max(area.Top, area.Bottom - Height - 20);

        _spinner = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 9,1 A 8,8 0 1 1 1,9"),
            Stroke = Brush("#6AA8FF"),
            StrokeThickness = 2.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 18,
            Height = 18,
            Stretch = Stretch.None,
            RenderTransform = new RotateTransform(0, 9, 9),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _symbol.FontSize = 19;
        _symbol.Width = 30;
        _symbol.Foreground = Brush("#78D69B");
        _symbol.VerticalAlignment = VerticalAlignment.Center;
        _symbol.Visibility = Visibility.Collapsed;

        _title.Text = "正在识别文字…";
        _title.FontSize = 14;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = Brushes.White;
        _description.Text = "燕子 PaddleOCR · 请稍候";
        _description.FontSize = 11;
        _description.Foreground = Brush("#BAC4D0");
        _description.Margin = new Thickness(0, 5, 0, 0);

        var text = new StackPanel();
        text.Children.Add(_title);
        text.Children.Add(_description);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_spinner);
        content.Children.Add(_symbol);
        content.Children.Add(text);

        var panel = new Border
        {
            Background = Brush("#1D2128"),
            BorderBrush = Brush("#4C5562"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(16, 12, 10, 10),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 2,
                Opacity = 0.38
            },
            Child = content
        };
        AutomationProperties.SetAutomationId(panel, "capture.ocr.toast");
        Content = panel;
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        panel.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                !_completed || _details is null)
                return;
            _timer.Stop();
            Close();
            var details = new OcrResultWindow(_details, _engine)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            details.Show();
            details.Activate();
            e.Handled = true;
        };

        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
        Loaded += (_, _) =>
        {
            if (_completed) _timer.Start();
            else StartSpinner();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _spinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }

    public void Complete(string title, string subtitle,
        string? details, string engine, bool successful)
    {
        _details = details;
        _engine = engine;
        _completed = true;
        Title = title;
        _title.Text = title;
        _description.Text = subtitle;
        _symbol.Text = successful ? "✓" : "!";
        _symbol.Foreground = successful ? Brush("#78D69B") : Brush("#F3C36B");
        _spinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        _spinner.Visibility = Visibility.Collapsed;
        _symbol.Visibility = Visibility.Visible;
        if (Content is Border panel)
            panel.Cursor = details is null ? Cursors.Arrow : Cursors.Hand;

        _timer.Stop();
        if (IsLoaded)
            _timer.Start();
    }

    private void StartSpinner()
    {
        var animation = new DoubleAnimation(0, 360,
            new Duration(TimeSpan.FromMilliseconds(850)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _spinner.RenderTransform.BeginAnimation(
            RotateTransform.AngleProperty, animation);
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
