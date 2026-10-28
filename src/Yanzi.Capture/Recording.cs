using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yanzi.Capture;

public sealed class RecordingSession : IAsyncDisposable
{
    private readonly Process _process;
    private bool _stopping;

    public string OutputPath { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public bool IsRunning => !_process.HasExited;

    private RecordingSession(Process process, string outputPath)
    {
        _process = process;
        OutputPath = outputPath;
    }

    public static RecordingSession Start(Int32Rect region)
    {
        if (region.Width < 16 || region.Height < 16)
            throw new InvalidOperationException("录制区域太小。");

        var videos = Environment.GetFolderPath(
            Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
            videos = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

        var outputDirectory = Path.Combine(videos, "Yanzi");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(
            outputDirectory,
            "Yanzi-Record-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4");

        // H.264/YUV420P require even frame dimensions.
        var width = region.Width - region.Width % 2;
        var height = region.Height - region.Height % 2;

        var args = string.Join(" ",
            "-hide_banner",
            "-loglevel error",
            "-y",
            "-f gdigrab",
            "-framerate 30",
            $"-offset_x {region.X}",
            $"-offset_y {region.Y}",
            $"-video_size {width}x{height}",
            "-draw_mouse 1",
            "-i desktop",
            "-an",
            "-c:v libx264",
            "-preset ultrafast",
            "-crf 23",
            "-pix_fmt yuv420p",
            $"\"{outputPath}\"");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "无法启动 FFmpeg。请确认已安装并加入 PATH。");

        return new RecordingSession(process, outputPath);
    }

    public async Task StopAsync()
    {
        if (_stopping) return;
        _stopping = true;

        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("q");
                await _process.StandardInput.FlushAsync();

                var completed = await Task.WhenAny(
                    _process.WaitForExitAsync(),
                    Task.Delay(5000));

                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            if (!_process.HasExited)
                await _process.WaitForExitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process.Dispose();
    }
}

public sealed class RecordingBarWindow : Window
{
    private readonly RecordingSession _session;
    private readonly TextBlock _timerText = new();
    private readonly Button _stopButton;
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private bool _finished;

    public event Action<string>? RecordingFinished;

    public RecordingBarWindow(RecordingSession session)
    {
        _session = session;
        Title = "燕子录屏";
        Width = 360;
        Height = 64;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;

        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Top + 24;

        var border = new Border
        {
            Background = Brush("#EC171A20"),
            BorderBrush = Brush("#3A414C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        var dot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = Brush("#FF4D4F"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(dot);

        var textStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _timerText.Text = "00:00";
        _timerText.FontSize = 15;
        _timerText.FontWeight = FontWeights.SemiBold;
        _timerText.Foreground = Brushes.White;
        textStack.Children.Add(_timerText);
        textStack.Children.Add(new TextBlock
        {
            Text = "   正在录制 · 无音频",
            Foreground = Brush("#AAB3C0"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        _stopButton = new Button
        {
            Content = "停止",
            Width = 62,
            Height = 34,
            Background = Brush("#FF4D4F"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        AutomationProperties.SetAutomationId(
            _stopButton,
            "capture.record.stop");
        _stopButton.Click += async (_, _) => await FinishAsync();
        Grid.SetColumn(_stopButton, 2);
        grid.Children.Add(_stopButton);

        border.Child = grid;
        Content = border;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button) return;
            try { DragMove(); } catch { }
        };

        _timer.Tick += (_, _) => RefreshTimer();
        Loaded += (_, _) =>
        {
            _timer.Start();
            RefreshTimer();
        };
        Closing += async (_, e) =>
        {
            if (_finished) return;
            e.Cancel = true;
            await FinishAsync();
        };
    }

    private void RefreshTimer()
    {
        var elapsed = DateTimeOffset.Now - _session.StartedAt;
        _timerText.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    private async Task FinishAsync()
    {
        if (_finished) return;
        _finished = true;
        _stopButton.IsEnabled = false;
        _stopButton.Content = "处理中";
        _timer.Stop();

        await _session.StopAsync();

        RecordingFinished?.Invoke(_session.OutputPath);
        Close();
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

public sealed class RecordingFinishedWindow : Window
{
    private readonly string _path;

    public RecordingFinishedWindow(string path)
    {
        _path = path;
        Title = "录制完成";
        Width = 520;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#171A20");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "录制完成",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });

        var pathText = new TextBlock
        {
            Text = _path,
            Foreground = Brush("#AAB3C0"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(pathText, 2);
        root.Children.Add(pathText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var folder = MakeButton("打开文件夹", false);
        folder.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_path}\"",
                UseShellExecute = true
            });
        };

        var done = MakeButton("完成", true);
        done.Margin = new Thickness(8, 0, 0, 0);
        done.Click += (_, _) => Close();

        actions.Children.Add(folder);
        actions.Children.Add(done);
        Grid.SetRow(actions, 4);
        root.Children.Add(actions);

        Content = root;
    }

    private static Button MakeButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 88,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Background = primary ? Brush("#3A8DFF") : Brush("#292E36"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
