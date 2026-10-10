using System.IO;
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
    private CaptureOcrHotkeyReceiver? _ocrHotkey;
    private CaptureOcrClient? _ocrClient;
    private CaptureShortcutPreferences _preferences = new();
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

            if (string.Equals(input?.Trim(), "settings", StringComparison.OrdinalIgnoreCase))
            {
                OpenSettings();
                return;
            }

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
                StartCaptureFlow(string.Equals(input?.Trim(), "ocr", StringComparison.OrdinalIgnoreCase));
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
            _ocrClient = new CaptureOcrClient(
                _context?.AgentApiBaseUrl ?? string.Empty,
                _context?.AgentApiToken ?? string.Empty,
                CaptureDataDirectory, message => _context?.Log(message));
            Prewarm();
            _preferences = CapturePreferencesStore.Load(CaptureDataDirectory);
            _ocrHotkey = new CaptureOcrHotkeyReceiver(() => Run("ocr"));
            if (!_ocrHotkey.TryApply(_preferences.OcrShortcut))
            {
                if (_ocrHotkey.TryApply("Ctrl+Alt+F3"))
                    _context?.Log("OCR 预设快捷键被占用：" + _preferences.OcrShortcut + "；已启用备用键 Ctrl+Alt+F3");
                else
                    _context?.Log("截图 OCR 快捷键注册失败：" + _preferences.OcrShortcut + "；备用键也不可用");
            }
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
            _ocrHotkey?.Dispose();
            _ocrHotkey = null;

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
            _ = new CaptureWorkspaceWindow(
                dummy,
                _context?.ExtensionDataDirectory
                    ?? Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "OpenQuickHost",
                        "ExtensionData",
                        "yanzi-capture"));

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

    private string CaptureDataDirectory => _context?.ExtensionDataDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "ExtensionData", "yanzi-capture");

    private bool ApplyCaptureSettings(CaptureShortcutPreferences next)
    {
        if (_ocrHotkey is null || !_ocrHotkey.TryApply(next.OcrShortcut))
            return false;
        _preferences = next;
        return true;
    }

    private void OpenSettings()
    {
        var settings = new CaptureSettingsWindow(CaptureDataDirectory,
            ApplyCaptureSettings, _ocrHotkey?.RegisteredShortcut);
        if (_activeWindow?.IsVisible == true)
            settings.Owner = _activeWindow;
        settings.ShowDialog();
    }

    private void StartCaptureFlow(bool ocrOnly = false)
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

            var workspace = new CaptureWorkspaceWindow(
                screen,
                _context?.ExtensionDataDirectory
                    ?? Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "OpenQuickHost",
                        "ExtensionData",
                        "yanzi-capture"),
                ocrOnly: ocrOnly,
                openSettings: () => OpenSettings(),
                ocrClient: _ocrClient);

            _activeWindow = workspace;

            workspace.Closed += (_, _) =>
            {
                CaptureDiagnostics.Mark("workspace.closed");
                FinishFlow();
            };

            CaptureDiagnostics.Mark("workspace.show.call");
            workspace.Show();
            workspace.Activate();
            CaptureDiagnostics.Mark(
                "workspace.show.return",
                ("actualWidth", workspace.ActualWidth),
                ("actualHeight", workspace.ActualHeight));
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
        var editor = new EditorWindow(document, _ocrClient);
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
