using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace OpenQuickHost;

/// <summary>A separate STA visual indicator. Borders and cursor halo never receive pointer input;
/// only the small always-visible toolbar accepts pause, resume and stop.</summary>
internal sealed class UiSessionOverlay : IDisposable
{
    private readonly IntPtr _target;
    private readonly string _extensionId;
    private readonly string _mode;
    private readonly Func<string> _state;
    private readonly Func<string> _stage;
    private readonly Func<DateTimeOffset> _lastActivity;
    private readonly Action<string> _onInterference;
    private readonly Action _onPause;
    private readonly Action _onResume;
    private readonly Action _onStop;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Window> _borders = [];
    private Dispatcher? _dispatcher;
    private Window? _toolbar;
    private Window? _halo;
    private DispatcherTimer? _timer;
    private TextBlock? _statusText;
    private Button? _pauseButton;
    private HwndSource? _toolbarSource;
    private readonly LowLevelHook _mouseCallback;
    private readonly LowLevelHook _keyboardCallback;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private int _pauseQueued;
    private long _ignorePhysicalUntilMs;
    private bool _started;
    private int _disposed;
    private int _frame;

    private const int ExStyle = -20;
    private const int Transparent = 0x20;
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x80;
    private const int HotkeyId = 0x5941;
    private const int WmHotkey = 0x0312;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    // Low-level hooks provide the event source and injected flag, unlike
    // GetLastInputInfo (which falsely attributes browser clicks and synthetic
    // UI Automation input to the currently focused calendar).
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelHook(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseEvent
    {
        public Point Location;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardEvent
    {
        public uint VirtualKey, ScanCode, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, LowLevelHook callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect value);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public UiSessionOverlay(IntPtr target, string extensionId, string mode,
        Func<string> state, Func<string> stage, Func<DateTimeOffset> lastActivity,
        Action<string> onInterference, Action onPause, Action onResume, Action onStop)
    {
        _target = target;
        _extensionId = extensionId;
        _mode = mode;
        _state = state;
        _stage = stage;
        _lastActivity = lastActivity;
        _onInterference = onInterference;
        _onPause = onPause;
        _onResume = onResume;
        _onStop = onStop;
        _mouseCallback = MouseHookCallback;
        _keyboardCallback = KeyboardHookCallback;
    }

    public async Task StartAsync(CancellationToken token)
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "YanziUiTestIndicator" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(4), token);
    }

    private static Window NewOverlayWindow()
    {
        return new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowActivated = false,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true
        };
    }

    private static void MakePassThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, ExStyle, GetWindowLong(hwnd, ExStyle) |
            Transparent | NoActivate | ToolWindow);
    }

    private static Border NewBlueBorder()
    {
        var gradient = new LinearGradientBrush();
        gradient.StartPoint = new System.Windows.Point(0, 0);
        gradient.EndPoint = new System.Windows.Point(1, 1);
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(40, 101, 245), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(101, 203, 255), .5));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(58, 117, 243), 1));
        var border = new Border
        {
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(11),
            BorderBrush = gradient,
            Background = Brushes.Transparent,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(41, 116, 255), BlurRadius = 13,
                ShadowDepth = 0, Opacity = .48
            }
        };
        var animation = new DoubleAnimation(.72, 1, new Duration(TimeSpan.FromSeconds(2.4)))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        border.BeginAnimation(UIElement.OpacityProperty, animation);
        return border;
    }

    private static Button ToolbarButton(string text, Color background)
    {
        return new Button
        {
            Content = text,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(background),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 47
        };
    }

    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            if (!IsWindow(_target) || !GetWindowRect(_target, out _))
                throw new InvalidOperationException("The extension window has closed.");
            var screens = _mode == "foreground"
                ? Forms.Screen.AllScreens.Select(screen => screen.Bounds).ToArray()
                : [Forms.Screen.FromHandle(_target).Bounds];
            GetWindowRect(_target, out var targetRect);
            var scale = 96.0 / Math.Max(96u, GetDpiForWindow(_target));
            foreach (var bounds in screens)
            {
                var rect = _mode == "foreground" ? bounds :
                    new System.Drawing.Rectangle(targetRect.Left, targetRect.Top,
                        targetRect.Right - targetRect.Left, targetRect.Bottom - targetRect.Top);
                var win = NewOverlayWindow();
                win.Title = "燕子 AI 测试边框";
                win.Left = rect.Left * scale;
                win.Top = rect.Top * scale;
                win.Width = Math.Max(2, rect.Width * scale);
                win.Height = Math.Max(2, rect.Height * scale);
                win.IsHitTestVisible = false;
                win.Content = NewBlueBorder();
                _borders.Add(win);
                win.Show();
                MakePassThrough(win);
            }

            var bar = NewOverlayWindow();
            bar.Title = "燕子 AI 正在进行 UI 测试";
            bar.Width = 400;
            bar.Height = 44;
            bar.Left = Math.Max(0, (targetRect.Right - 405) * scale);
            bar.Top = Math.Max(0, (targetRect.Top - 48) * scale);
            var surface = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(244, 27, 42, 72)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(97, 160, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 6, 9, 6),
                Effect = new DropShadowEffect
                { Color = Colors.Black, ShadowDepth = 3, BlurRadius = 10, Opacity = .4 }
            };
            var row = new DockPanel { LastChildFill = true };
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            _pauseButton = ToolbarButton("暂停", Color.FromRgb(47, 100, 184));
            _pauseButton.Click += (_, _) =>
            {
                if (_state() == "paused") _onResume();
                else _onPause();
            };
            var stop = ToolbarButton("停止", Color.FromRgb(161, 53, 65));
            stop.Click += (_, _) => _onStop();
            controls.Children.Add(_pauseButton);
            controls.Children.Add(stop);
            DockPanel.SetDock(controls, Dock.Right);
            row.Children.Add(controls);
            _statusText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            row.Children.Add(_statusText);
            surface.Child = row;
            bar.Content = surface;
            _toolbar = bar;
            UpdatePosition(screens);
            bar.Show();

            _toolbarSource = HwndSource.FromHwnd(new WindowInteropHelper(bar).Handle);
            _toolbarSource?.AddHook(HotkeyHook);
            // Ctrl+Alt+Shift+F12 is a hard stop even if the target has keyboard focus.
            RegisterHotKey(new WindowInteropHelper(bar).Handle, HotkeyId, 0x0001 | 0x0002 | 0x0004, 0x7B);

            if (_mode == "foreground")
            {
                _halo = NewOverlayWindow();
                _halo.Title = "燕子 AI 光标提示";
                _halo.Width = 38;
                _halo.Height = 38;
                _halo.Content = new Ellipse
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(85, 196, 255)),
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(35, 67, 152, 247))
                };
                _halo.Show();
                MakePassThrough(_halo);
            }

            // Observe only real, non-injected events. Polling GetLastInputInfo
            // cannot attribute the event to the browser versus our target.
            var module = GetModuleHandle(null);
            _mouseHook = SetWindowsHookEx(14, _mouseCallback, module, 0); // WH_MOUSE_LL
            _keyboardHook = SetWindowsHookEx(13, _keyboardCallback, module, 0); // WH_KEYBOARD_LL
            if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
                throw new InvalidOperationException("Could not install physical-input detection hooks.");
            _ignorePhysicalUntilMs = Environment.TickCount64 + 220;
            _started = true;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(130) };
            _timer.Tick += Tick;
            _timer.Start();
            UpdateStatus();
            _ready.TrySetResult(true);
            Dispatcher.Run();
        }
        catch (Exception error) { _ready.TrySetException(error); }
        finally
        {
            _started = false;
            _timer?.Stop();
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
            _mouseHook = _keyboardHook = IntPtr.Zero;
            foreach (var border in _borders.ToArray())
                try { border.Close(); } catch { }
            if (_toolbar != null)
            {
                var hwnd = new WindowInteropHelper(_toolbar).Handle;
                if (hwnd != IntPtr.Zero) UnregisterHotKey(hwnd, HotkeyId);
                if (_toolbarSource != null) _toolbarSource.RemoveHook(HotkeyHook);
                try { _toolbar.Close(); } catch { }
            }
            try { _halo?.Close(); } catch { }
        }
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onStop();
        }
        return IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _started && Volatile.Read(ref _disposed) == 0 &&
            _state() == "active" && Environment.TickCount64 >=
                Interlocked.Read(ref _ignorePhysicalUntilMs))
        {
            var input = Marshal.PtrToStructure<MouseEvent>(lParam);
            // LLMHF_INJECTED: don't interpret AI-generated input as human input.
            if ((input.Flags & 0x01) == 0 &&
                (_mode == "foreground" || MouseOnTarget(input.Location)))
                QueuePhysicalInputPause();
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _started && Volatile.Read(ref _disposed) == 0 &&
            _state() == "active" && Environment.TickCount64 >=
                Interlocked.Read(ref _ignorePhysicalUntilMs))
        {
            var input = Marshal.PtrToStructure<KeyboardEvent>(lParam);
            // LLKHF_INJECTED: ignore synthetic keyboard events.
            if ((input.Flags & 0x10) == 0 &&
                (_mode == "foreground" || GetForegroundWindow() == _target))
                QueuePhysicalInputPause();
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private bool MouseOnTarget(Point point)
    {
        return GetWindowRect(_target, out var rect) &&
               point.X >= rect.Left && point.X <= rect.Right &&
               point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private void QueuePhysicalInputPause()
    {
        if (Interlocked.Exchange(ref _pauseQueued, 1) != 0) return;
        _dispatcher?.BeginInvoke((Action)(() =>
        {
            if (_state() == "active") _onInterference("user_input");
        }), DispatcherPriority.Send);
    }

    private void UpdatePosition(System.Drawing.Rectangle[] screens)
    {
        var dpi = Math.Max(96u, GetDpiForWindow(_target));
        var scale = 96.0 / dpi;
        if (!GetWindowRect(_target, out var target)) return;
        for (int i = 0; i < _borders.Count; i++)
        {
            var rect = _mode == "foreground"
                ? screens[Math.Min(i, screens.Length - 1)]
                : new System.Drawing.Rectangle(target.Left, target.Top,
                    target.Right - target.Left, target.Bottom - target.Top);
            var win = _borders[i];
            win.Left = rect.Left * scale;
            win.Top = rect.Top * scale;
            win.Width = Math.Max(2, rect.Width * scale);
            win.Height = Math.Max(2, rect.Height * scale);
        }
        if (_toolbar != null)
        {
            _toolbar.Left = Math.Max(0, (target.Right - 405) * scale);
            _toolbar.Top = Math.Max(0, (target.Top - 48) * scale);
        }
        if (_halo != null && GetCursorPos(out var cursor))
        {
            _halo.Left = (cursor.X - 19) * scale;
            _halo.Top = (cursor.Y - 19) * scale;
        }
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_started) return;
        if (!IsWindow(_target))
        {
            _onStop();
            return;
        }
        if (DateTimeOffset.UtcNow - _lastActivity() > TimeSpan.FromSeconds(120))
        {
            _onStop();
            return;
        }
        if (++_frame % 3 == 0)
        {
            var screens = _mode == "foreground"
                ? Forms.Screen.AllScreens.Select(x => x.Bounds).ToArray()
                : [Forms.Screen.FromHandle(_target).Bounds];
            UpdatePosition(screens);
            UpdateStatus();
        }
        else if (_halo != null)
        {
            var screens = Forms.Screen.AllScreens.Select(x => x.Bounds).ToArray();
            UpdatePosition(screens);
        }
    }

    private void UpdateStatus()
    {
        if (_statusText == null || _pauseButton == null) return;
        var state = _state();
        _statusText.Text = state switch
        {
            "paused" => "已暂停 · 用户控制优先",
            "active" => $"燕子 AI 测试 · {_stage()}",
            _ => "测试已结束"
        };
        _pauseButton.Content = state == "paused" ? "继续" : "暂停";
    }

    public void Refresh() =>
        _dispatcher?.BeginInvoke((Action)UpdateStatus, DispatcherPriority.Background);

    public void ResetInputBaseline()
    {
        Interlocked.Exchange(ref _pauseQueued, 0);
        // Ignore the mouse-up/keypress that physically clicked Resume in the
        // toolbar; subsequent genuine user activity will pause again.
        Interlocked.Exchange(ref _ignorePhysicalUntilMs, Environment.TickCount64 + 550);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _dispatcher?.BeginInvoke((Action)(() => _dispatcher.InvokeShutdown()),
            DispatcherPriority.Send);
    }
}

