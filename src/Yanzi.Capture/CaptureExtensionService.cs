using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenQuickHost.CSharpRuntime;

namespace Yanzi.Capture;

public sealed class CaptureExtensionService
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private YanziActionContext? _context;
    private Thread? _uiThread;
    private Dispatcher? _dispatcher;
    private Window? _activeWindow;
    private bool _flowActive;
    private bool _initialized;
    private bool _stopping;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _initialized && !_stopping;
        }
    }

    public Task WaitForStopAsync() => _stopped.Task;

    public async Task InitializeAsync(YanziActionContext context)
    {
        lock (_sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            _context = context;
        }

        CaptureDiagnostics.Initialize(["in-process", context.LaunchSource]);

        _uiThread = new Thread(UiThreadMain)
        {
            IsBackground = true,
            Name = "YanziCaptureUI"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        await _ready.Task.ConfigureAwait(false);
    }

    public void Toggle() => Run(string.Empty);

    public void Activate()
    {
        Post(() =>
        {
            if (_activeWindow is null)
                return;

            _activeWindow.Topmost = true;
            _activeWindow.Activate();
        });
    }

    public void Show() => Toggle();

    public void Run(string input)
    {
        Post(() =>
        {
            if (_stopping)
                return;

            if (_flowActive)
            {
                Activate();
                return;
            }

            if (string.Equals(
                    input?.Trim(),
                    "demo",
                    StringComparison.OrdinalIgnoreCase))
            {
                _flowActive = true;
                OpenEditor(CaptureFactory.CreateDemoDocument());
            }
            else if (IsRecordInput(input))
            {
                StartRecordingFlow();
            }
            else
            {
                StartCaptureFlow();
            }
        });
    }

    public void Quit() => Stop();

    public void Stop()
    {
        lock (_sync)
        {
            if (!_initialized || _stopping)
                return;

            _stopping = true;
        }

        Post(() =>
        {
            try
            {
                _activeWindow?.Close();
            }
            catch
            {
            }

            _activeWindow = null;
            _flowActive = false;

            try
            {
                _dispatcher?.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => _dispatcher?.InvokeShutdown()));
            }
            catch
            {
                try
                {
                    _dispatcher?.InvokeShutdown();
                }
                catch
                {
                }
            }
        });
    }

    private void UiThreadMain()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Prewarm();
            _ready.TrySetResult(true);

            if (!_stopping)
                Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _context?.Log("截图 UI 线程异常: " + ex);
            _ready.TrySetException(ex);
        }
        finally
        {
            try
            {
                _activeWindow?.Close();
            }
            catch
            {
            }

            _activeWindow = null;

            lock (_sync)
            {
                _initialized = false;
                _flowActive = false;
            }

            CaptureDiagnostics.Mark("extension.service.stopped");
            CaptureDiagnostics.Shutdown();
            _stopped.TrySetResult(true);
        }
    }

    private void Prewarm()
    {
        try
        {
            CaptureDiagnostics.Mark("extension.prewarm.begin");

            var warm = new Window
            {
                Width = 8,
                Height = 8,
                Left = -32000,
                Top = -32000,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Content = new Grid
                {
                    Background = Brushes.Black
                }
            };

            warm.Show();
            warm.Hide();
            warm.Close();

            var pixels = new byte[4 * 4 * 4];
            var dummy = BitmapSource.Create(
                4,
                4,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                4 * 4);
            dummy.Freeze();

            _ = new CaptureOverlayWindow(dummy);

            CaptureDiagnostics.Mark("extension.prewarm.end");
        }
        catch (Exception ex)
        {
            _context?.Log("截图预热失败: " + ex.Message);
            CaptureDiagnostics.Mark(
                "extension.prewarm.error",
                ("error", ex.GetType().Name));
        }
    }

    private void StartCaptureFlow()
    {
        if (_flowActive || _stopping)
            return;

        _flowActive = true;

        try
        {
            CaptureDiagnostics.Mark("capture.flow.begin");

            var screen = ScreenCaptureService.CapturePrimary();
            CaptureDiagnostics.Mark(
                "capture.primary.ready",
                ("width", screen.PixelWidth),
                ("height", screen.PixelHeight));

            var overlay = new CaptureOverlayWindow(screen);
            _activeWindow = overlay;

            overlay.Captured += bitmap =>
            {
                _activeWindow = null;
                OpenEditor(new CaptureDocument(bitmap));
            };

            overlay.Cancelled += FinishFlow;

            CaptureDiagnostics.Mark("overlay.show.call");
            overlay.Show();
            overlay.Activate();
            CaptureDiagnostics.Mark(
                "overlay.show.return",
                ("actualWidth", overlay.ActualWidth),
                ("actualHeight", overlay.ActualHeight));
        }
        catch (Exception ex)
        {
            _context?.Log("截图失败: " + ex);
            FinishFlow();
        }
    }

    private void StartRecordingFlow()
    {
        if (_flowActive || _stopping)
            return;

        _flowActive = true;

        try
        {
            var screen = ScreenCaptureService.CapturePrimary();
            var overlay = new CaptureOverlayWindow(
                screen,
                selectionOnly: true);
            _activeWindow = overlay;

            overlay.RegionSelected += region =>
            {
                _activeWindow = null;

                try
                {
                    var session = RecordingSession.Start(region);
                    var bar = new RecordingBarWindow(session);
                    _activeWindow = bar;

                    bar.RecordingFinished += path =>
                    {
                        _activeWindow = null;

                        var done = new RecordingFinishedWindow(path);
                        _activeWindow = done;
                        done.Closed += (_, _) => FinishFlow();
                        done.Show();
                        done.Activate();
                    };

                    bar.Show();
                    bar.Activate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "燕子录屏",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    FinishFlow();
                }
            };

            overlay.Cancelled += FinishFlow;
            overlay.Show();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            _context?.Log("录屏启动失败: " + ex);
            FinishFlow();
        }
    }

    private void OpenEditor(CaptureDocument document)
    {
        CaptureDiagnostics.Mark(
            "editor.open.begin",
            ("width", document.BaseImage.PixelWidth),
            ("height", document.BaseImage.PixelHeight));

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var editor = new EditorWindow(document);
        _activeWindow = editor;
        _flowActive = true;

        CaptureDiagnostics.Mark(
            "editor.constructed",
            ("durationMs",
                System.Diagnostics.Stopwatch
                    .GetElapsedTime(start)
                    .TotalMilliseconds
                    .ToString("F2")));

        editor.ContentRendered += (_, _) =>
        {
            CaptureDiagnostics.Mark(
                "editor.contentRendered",
                ("actualWidth", editor.ActualWidth),
                ("actualHeight", editor.ActualHeight));
        };

        editor.Closed += (_, _) =>
        {
            CaptureDiagnostics.Mark("editor.closed");
            FinishFlow();
        };

        editor.Show();
        editor.Activate();
    }

    private void FinishFlow()
    {
        _activeWindow = null;
        _flowActive = false;

        CaptureDiagnostics.Mark(
            "capture.flow.end",
            ("inProcess", true));
    }

    private void Post(Action action)
    {
        var dispatcher = _dispatcher;

        if (dispatcher is null ||
            dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsRecordInput(string? input)
    {
        var value = input?.Trim() ?? string.Empty;

        return
            string.Equals(
                value,
                "record",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "--record",
                StringComparison.OrdinalIgnoreCase) ||
            value.Contains(
                "录屏",
                StringComparison.OrdinalIgnoreCase);
    }
}
