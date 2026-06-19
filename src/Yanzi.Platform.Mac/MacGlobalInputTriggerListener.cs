using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using Yanzi.Shared;

namespace Yanzi.Platform.Mac;

internal static class MacLogger
{
    private static readonly object _fileLock = new();

    public static void WriteLog(string tag, string message)
    {
        lock (_fileLock)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".yanzi_boot.log"
                );
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {message}\n");
            }
            catch {}
        }
    }
}

public sealed class MacGlobalInputTriggerListenerFactory : IGlobalInputTriggerListenerFactory
{
    public IGlobalInputTriggerListener Create(GlobalInputTriggerSettings settings) => new MacGlobalInputTriggerListener(settings);
}

public sealed class MacGlobalInputTriggerListener : IGlobalInputTriggerListener
{
    private static readonly TimeSpan ActivationHoldWindow = TimeSpan.FromMilliseconds(900);
    private readonly IGlobalInputTriggerListener[] _listeners;
    private readonly object _activationLock = new();
    private IGlobalInputTriggerListener? _activeListener;
    private DateTime _activeListenerExpiresAtUtc = DateTime.MinValue;

    public MacGlobalInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _listeners = [new MacSecondaryButtonInputTriggerListener(settings), new MacFnKeyInputTriggerListener(settings)];

        foreach (var listener in _listeners)
        {
            listener.ActivationRequested += Listener_ActivationRequested;
            listener.ActivationUpdated += Listener_ActivationUpdated;
            listener.ActivationReleased += Listener_ActivationReleased;
            listener.LauncherRequested += Listener_LauncherRequested;
            listener.HotkeyTriggered += Listener_HotkeyTriggered;
        }
    }

    public bool IsRunning => _listeners.Any(listener => listener.IsRunning);

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;
    public event EventHandler? LauncherRequested;
    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    public void UpdateAbbreviations(Dictionary<string, string> abbreviations)
    {
        foreach (var listener in _listeners)
        {
            listener.UpdateAbbreviations(abbreviations);
        }
    }

    public void Start()
    {
        foreach (var listener in _listeners)
            listener.Start();
    }

    public void Stop()
    {
        foreach (var listener in _listeners)
            listener.Stop();
    }

    public void Dispose()
    {
        foreach (var listener in _listeners)
        {
            listener.ActivationRequested -= Listener_ActivationRequested;
            listener.ActivationUpdated -= Listener_ActivationUpdated;
            listener.ActivationReleased -= Listener_ActivationReleased;
            listener.LauncherRequested -= Listener_LauncherRequested;
            listener.HotkeyTriggered -= Listener_HotkeyTriggered;
            listener.Dispose();
        }
    }

    private void Listener_LauncherRequested(object? sender, EventArgs e)
    {
        LauncherRequested?.Invoke(this, e);
    }

    private void Listener_HotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        HotkeyTriggered?.Invoke(this, e);
    }

    private void Listener_ActivationRequested(object? sender, RadialMenuActivationEventArgs e)
    {
        if (!TryClaimActivation(sender))
            return;

        ActivationRequested?.Invoke(this, e);
    }

    private void Listener_ActivationUpdated(object? sender, RadialMenuActivationEventArgs e)
    {
        if (!TryAcceptActiveEvent(sender, release: false))
            return;

        ActivationUpdated?.Invoke(this, e);
    }

    private void Listener_ActivationReleased(object? sender, RadialMenuActivationEventArgs e)
    {
        if (!TryAcceptActiveEvent(sender, release: true))
            return;

        ActivationReleased?.Invoke(this, e);
    }

    private bool TryClaimActivation(object? sender)
    {
        if (sender is not IGlobalInputTriggerListener listener)
            return true;

        lock (_activationLock)
        {
            var now = DateTime.UtcNow;
            if (_activeListener != null &&
                !ReferenceEquals(_activeListener, listener) &&
                now < _activeListenerExpiresAtUtc)
            {
                if (listener is MacSecondaryButtonInputTriggerListener)
                {
                    MacLogger.WriteLog("NativeMac", $"Preempting active listener {_activeListener.GetType().Name} for MacSecondaryButtonInputTriggerListener in TryClaimActivation");
                }
                else
                {
                    MacLogger.WriteLog("NativeMac", $"Rejecting claim by {listener.GetType().Name} because {_activeListener.GetType().Name} is active until {_activeListenerExpiresAtUtc}");
                    return false;
                }
            }

            _activeListener = listener;
            _activeListenerExpiresAtUtc = now + ActivationHoldWindow;
            return true;
        }
    }

    private bool TryAcceptActiveEvent(object? sender, bool release)
    {
        if (sender is not IGlobalInputTriggerListener listener)
            return true;

        lock (_activationLock)
        {
            var now = DateTime.UtcNow;
            if (_activeListener != null && !ReferenceEquals(_activeListener, listener))
            {
                if (now < _activeListenerExpiresAtUtc)
                {
                    if (listener is MacSecondaryButtonInputTriggerListener)
                    {
                        MacLogger.WriteLog("NativeMac", $"Preempting active listener {_activeListener.GetType().Name} for MacSecondaryButtonInputTriggerListener in TryAcceptActiveEvent");
                    }
                    else
                    {
                        return false;
                    }
                }

                _activeListener = listener;
            }

            if (_activeListener == null)
                _activeListener = listener;

            if (release)
            {
                if (!ReferenceEquals(_activeListener, listener))
                {
                    if (listener is MacFnKeyInputTriggerListener)
                    {
                        MacLogger.WriteLog("NativeMac", "Forcing release because MacFnKeyInputTriggerListener fired release (Fn released)");
                    }
                    else
                    {
                        return false;
                    }
                }

                _activeListener = null;
                _activeListenerExpiresAtUtc = DateTime.MinValue;
            }
            else
            {
                _activeListenerExpiresAtUtc = now + ActivationHoldWindow;
            }

            return true;
        }
    }
}

internal sealed class MacSecondaryButtonInputTriggerListener : IGlobalInputTriggerListener
{
    private readonly GlobalInputTriggerSettings _settings;
    private IntPtr _eventTap;
    private bool _isEnabled;
    private bool _rightButtonPressed;
    private bool _dragTriggered;
    private Timer? _longPressTimer;
    private Point _pressPoint = new(0, 0);
    private readonly CGEventTapCallBack _eventTapCallback;

    public bool IsRunning => _isEnabled;

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;
    public event EventHandler? LauncherRequested
    {
        add { }
        remove { }
    }

    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered
    {
        add { }
        remove { }
    }

    public MacSecondaryButtonInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _settings = settings;
        _eventTapCallback = EventTapCallback;
        _longPressTimer = new Timer();
        _longPressTimer.Elapsed += LongPressTimer_Elapsed;
    }

    public void UpdateAbbreviations(Dictionary<string, string> abbreviations)
    {
        // Stub, no action needed for secondary button
    }

    public void Start()
    {
        if (_isEnabled)
            return;

        try
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] Starting MacSecondaryButtonInputTriggerListener...\n");
        }
        catch {}

        _eventTap = CreateEventTap();
        
        try
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] MacSecondaryButton CreateEventTap returned: {_eventTap}\n");
        }
        catch {}

        if (_eventTap == IntPtr.Zero)
        {
            Console.WriteLine("Failed to create event tap");
            return;
        }

        try
        {
            var runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
            CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, CFRunLoopModeCommonModes);
            CFRelease(runLoopSource);
            CFRunLoopWakeUp(CFRunLoopGetMain());

            CGEventTapEnable(_eventTap, true);

            _isEnabled = true;
            Console.WriteLine("Mac secondary-button trigger listener started");
            LogInput($"settings secondaryLongPress={_settings.EnableSecondaryButtonLongPress}, secondaryDrag={_settings.EnableSecondaryButtonDrag}");
            
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] MacSecondaryButton listener started and registered successfully!\n");
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac ERROR] MacSecondaryButton Start Failed: {ex.GetType().Name} - {ex.Message}\nStack: {ex.StackTrace}\n");
            }
            catch {}
        }
    }

    public void Stop()
    {
        if (!_isEnabled)
            return;

        CGEventTapEnable(_eventTap, false);
        CFRelease(_eventTap);
        _eventTap = IntPtr.Zero;

        _longPressTimer?.Stop();
        _rightButtonPressed = false;
        _dragTriggered = false;
        
        _isEnabled = false;
        Console.WriteLine("Mac secondary-button trigger listener stopped");
    }

    private IntPtr CreateEventTap()
    {
        var mask = CGEventMaskBit(CGEventType.RightMouseDown) |
                   CGEventMaskBit(CGEventType.RightMouseUp) |
                   CGEventMaskBit(CGEventType.MouseMoved) |
                   CGEventMaskBit(CGEventType.RightMouseDragged);

        // Try Session location first with active interception
        var tap = CGEventTapCreate(
            CGEventTapLocation.Session,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.Default,
            mask,
            _eventTapCallback,
            IntPtr.Zero);

        if (tap == IntPtr.Zero)
        {
            // Fallback to HID with active interception
            tap = CGEventTapCreate(
                CGEventTapLocation.HID,
                CGEventTapPlacement.HeadInsertEventTap,
                CGEventTapOptions.Default,
                mask,
                _eventTapCallback,
                IntPtr.Zero);
        }

        if (tap == IntPtr.Zero)
        {
            // Fallback to Session with ListenOnly (succeeds without Accessibility permission)
            tap = CGEventTapCreate(
                CGEventTapLocation.Session,
                CGEventTapPlacement.HeadInsertEventTap,
                CGEventTapOptions.ListenOnly,
                mask,
                _eventTapCallback,
                IntPtr.Zero);
        }

        if (tap == IntPtr.Zero)
        {
            // Fallback to HID with ListenOnly
            tap = CGEventTapCreate(
                CGEventTapLocation.HID,
                CGEventTapPlacement.HeadInsertEventTap,
                CGEventTapOptions.ListenOnly,
                mask,
                _eventTapCallback,
                IntPtr.Zero);
        }

        return tap;
    }

    private IntPtr EventTapCallback(IntPtr proxy, CGEventType type, IntPtr eventRef, IntPtr refcon)
    {
        try
        {
            if (type == (CGEventType)0xFFFFFFFE || type == (CGEventType)0xFFFFFFFF)
            {
                Console.WriteLine($"[input] Secondary Event Tap disabled: {type}. Re-enabling...");
                CGEventTapEnable(_eventTap, true);
                return eventRef;
            }

            switch (type)
            {
                case CGEventType.RightMouseDown:
                    HandleRightButtonDown(eventRef);
                    break;
                case CGEventType.RightMouseUp:
                    HandleRightButtonUp();
                    break;
                case CGEventType.MouseMoved:
                case CGEventType.RightMouseDragged:
                    HandleMouseMove(eventRef);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Global mouse callback failed: {ex}");
        }

        return eventRef;
    }

    private void HandleRightButtonDown(IntPtr eventPtr)
    {
        var flags = CGEventGetFlags(eventPtr);
        var fnPressed = (flags & (1UL << 23)) != 0;

        MacLogger.WriteLog("NativeMac", $"Secondary MouseDown, fnPressed={fnPressed}");

        if (!fnPressed)
        {
            _rightButtonPressed = false;
            return;
        }

        _rightButtonPressed = true;
        _dragTriggered = false;
        _pressPoint = GetEventLocation(eventPtr);
        LogInput($"secondary down at {_pressPoint.X:0},{_pressPoint.Y:0}");

        if (_settings.EnableSecondaryButtonLongPress)
        {
            _longPressTimer?.Stop();
            _longPressTimer!.Interval = _settings.LongPressThresholdMs;
            _longPressTimer.Start();
        }
    }

    private void HandleRightButtonUp()
    {
        _longPressTimer?.Stop();
        LogInput("secondary up");

        if (_rightButtonPressed && _dragTriggered)
        {
            ReleaseActivation(RadialMenuActivationSource.SecondaryButton);
        }

        _rightButtonPressed = false;
        _dragTriggered = false;
    }

    private void HandleMouseMove(IntPtr eventPtr)
    {
        if (!_rightButtonPressed)
            return;

        var currentPoint = GetEventLocation(eventPtr);

        if (_dragTriggered)
        {
            ActivationUpdated?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.SecondaryButton, null, currentPoint.X, currentPoint.Y));
            return;
        }

        var dx = currentPoint.X - _pressPoint.X;
        var dy = currentPoint.Y - _pressPoint.Y;
        var distanceSquared = dx * dx + dy * dy;

        if (distanceSquared >= _settings.DragThresholdPixels * _settings.DragThresholdPixels)
        {
            _dragTriggered = true;
            _longPressTimer?.Stop();

            if (_settings.EnableSecondaryButtonDrag)
            {
                LogInput($"secondary drag activation at {currentPoint.X:0},{currentPoint.Y:0}");
                RequestActivation(RadialMenuActivationSource.SecondaryButton, currentPoint, isLongPress: false);
            }
        }
    }

    private void LongPressTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            _longPressTimer?.Stop();

            if (_rightButtonPressed && !_dragTriggered)
            {
                _dragTriggered = true;
                LogInput($"secondary long press activation at {_pressPoint.X:0},{_pressPoint.Y:0}");
                RequestActivation(RadialMenuActivationSource.SecondaryButton, _pressPoint, isLongPress: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Global mouse long press failed: {ex}");
        }
    }

    private Point GetEventLocation(IntPtr eventPtr)
    {
        var point = CGEventGetLocation(eventPtr);
        return new Point(point.X, point.Y);
    }

    private void RequestActivation(RadialMenuActivationSource source, Point point, int? fingerCount = null, bool isLongPress = false)
    {
        ActivationRequested?.Invoke(this, new RadialMenuActivationEventArgs(source, fingerCount, point.X, point.Y, isLongPress));
    }

    private void ReleaseActivation(RadialMenuActivationSource source)
    {
        ActivationReleased?.Invoke(this, new RadialMenuActivationEventArgs(source));
    }

    private void LogInput(string message)
    {
        if (_settings.EnableInputDiagnostics)
            Console.WriteLine($"[input] {message}");
    }

    public void Dispose()
    {
        Stop();
        _longPressTimer?.Dispose();
    }

    private record Point(double X, double Y);

    #region P/Invoke

    private enum CGEventTapLocation : uint
    {
        HID = 0,
        Session = 1,
        AnnotatedSession = 2
    }

    private enum CGEventTapPlacement : uint
    {
        HeadInsertEventTap = 0,
        TailAppendEventTap = 1
    }

    private enum CGEventTapOptions : uint
    {
        Default = 0,
        ListenOnly = 1
    }

    private enum CGEventType : uint
    {
        LeftMouseDown = 1,
        LeftMouseUp = 2,
        RightMouseDown = 3,
        RightMouseUp = 4,
        MouseMoved = 5,
        LeftMouseDragged = 6,
        RightMouseDragged = 7
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventTapCreate(
        CGEventTapLocation tap,
        CGEventTapPlacement place,
        CGEventTapOptions options,
        ulong eventsOfInterest,
        CGEventTapCallBack callback,
        IntPtr userInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGEventTapCallBack(IntPtr proxy, CGEventType type, IntPtr @event, IntPtr refcon);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, uint order);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRunLoopGetMain();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopWakeUp(IntPtr runLoop);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern ulong CGEventGetFlags(IntPtr @event);

    private static ulong CGEventMaskBit(CGEventType type) => 1UL << (int)type;

    private static readonly IntPtr CFRunLoopModeCommonModes =
        CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopCommonModes", CFStringEncodingUtf8);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, uint encoding);

    private const uint CFStringEncodingUtf8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;
    }

    #endregion
}

internal sealed class MacFnKeyInputTriggerListener : IGlobalInputTriggerListener
{
    private readonly GlobalInputTriggerSettings _settings;
    private readonly object _lifecycleLock = new();
    private IntPtr _eventTap;
    private IntPtr _runLoop;
    private bool _isEnabled;
    private bool _fnKeyPressed;
    private bool _gestureTriggered;
    private bool _trackpadTouchDetected;
    private Timer? _fnLongPressTimer;
    private Point _pressPoint = new(0, 0);
    private readonly CGEventTapCallBack _eventTapCallback;
    private Thread? _eventTapThread;
    private ManualResetEventSlim? _startedEvent;

    // Character buffering and abbreviation structures
    private readonly StringBuilder _charBuffer = new();
    private Dictionary<string, string> _abbreviations = [];
    private readonly object _bufferLock = new();
    private bool _isTapActiveInterception;

    public bool IsRunning => _isEnabled;

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;
    public event EventHandler? LauncherRequested;
    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    public MacFnKeyInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _settings = settings;
        _eventTapCallback = EventTapCallback;
        _fnLongPressTimer = new Timer();
        _fnLongPressTimer.Elapsed += FnLongPressTimer_Elapsed;
    }

    public void UpdateAbbreviations(Dictionary<string, string> abbreviations)
    {
        lock (_bufferLock)
        {
            _abbreviations = abbreviations != null 
                ? new Dictionary<string, string>(abbreviations, StringComparer.OrdinalIgnoreCase) 
                : [];
        }
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_isEnabled || _eventTapThread != null)
                return;

            _startedEvent?.Dispose();
            _startedEvent = new ManualResetEventSlim(false);
        }

        try
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] Starting MacFnKeyInputTriggerListener...\n");
        }
        catch {}

        if (System.OperatingSystem.IsMacOS())
        {
            try
            {
                bool trusted = AXIsProcessTrusted();
                bool listenTrusted = CGPreflightListenEventAccess();
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] AXIsProcessTrusted check: {trusted}\n");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] CGPreflightListenEventAccess check: {listenTrusted}\n");
                if (!trusted)
                {
                    trusted = RequestAccessibilityTrustPrompt();
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] AXIsProcessTrustedWithOptions prompt result: {trusted}\n");
                    Console.WriteLine("⚠️ [WARNING] 燕子启动器未获得 macOS 辅助功能 (Accessibility) 权限！全局快捷键和简写指令膨胀功能将无法工作。请在：系统设置 -> 隐私与安全 -> 辅助功能 中启用 燕子启动器 (Yanzi)！");
                }
                if (!listenTrusted)
                {
                    bool requestResult = CGRequestListenEventAccess();
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] CGRequestListenEventAccess result: {requestResult}\n");
                    Console.WriteLine("⚠️ [WARNING] 燕子启动器未获得 macOS 输入监控 (Input Monitoring) 权限！Fn+触摸板和全局按键监听可能无法工作。请在：系统设置 -> 隐私与安全 -> 输入监控 中启用 燕子启动器 (Yanzi)！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to check AXIsProcessTrusted: {ex.Message}");
                try
                {
                    var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac ERROR] AXIsProcessTrusted failed: {ex.Message}\n");
                }
                catch {}
            }
        }

        var thread = new Thread(EventTapThreadMain)
        {
            IsBackground = true,
            Name = "YanziMacFnEventTap"
        };

        lock (_lifecycleLock)
        {
            _eventTapThread = thread;
        }

        thread.Start();
        _startedEvent?.Wait(TimeSpan.FromSeconds(3));

        if (!_isEnabled)
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac ERROR] MacFnKey listener did not become enabled within startup timeout.\n");
            }
            catch {}
        }
    }

    private void EventTapThreadMain()
    {
        try
        {
            _eventTap = CreateEventTap();
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] MacFnKey CreateEventTap returned: {_eventTap}, isTapActiveInterception={_isTapActiveInterception}\n");
            }
            catch {}

            if (_eventTap == IntPtr.Zero)
            {
                Console.WriteLine("Failed to create Fn key event tap");
                return;
            }

            var runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
            _runLoop = CFRunLoopGetCurrent();
            CFRunLoopAddSource(_runLoop, runLoopSource, CFRunLoopModeDefaultMode);
            CFRunLoopAddSource(_runLoop, runLoopSource, CFRunLoopModeCommonModes);
            CFRelease(runLoopSource);

            CGEventTapEnable(_eventTap, true);

            _isEnabled = true;
            Console.WriteLine("Mac Fn key trigger listener started");
            
            var successLogPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
            System.IO.File.AppendAllText(successLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac] MacFnKey listener started on dedicated run loop and registered successfully!\n");
            _startedEvent?.Set();

            CFRunLoopRun();
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi_boot.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [NativeMac ERROR] MacFnKey event tap thread failed: {ex.GetType().Name} - {ex.Message}\nStack: {ex.StackTrace}\n");
            }
            catch {}
        }
        finally
        {
            if (_eventTap != IntPtr.Zero)
            {
                CGEventTapEnable(_eventTap, false);
                CFRelease(_eventTap);
                _eventTap = IntPtr.Zero;
            }

            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_eventTapThread, Thread.CurrentThread))
                    _eventTapThread = null;

                _runLoop = IntPtr.Zero;
                _isEnabled = false;
                _startedEvent?.Set();
            }
        }
    }

    public void Stop()
    {
        Thread? eventTapThread;
        IntPtr runLoop;
        IntPtr eventTap;

        lock (_lifecycleLock)
        {
            if (!_isEnabled && _eventTapThread == null)
                return;

            eventTapThread = _eventTapThread;
            runLoop = _runLoop;
            eventTap = _eventTap;
        }

        if (eventTap != IntPtr.Zero)
            CGEventTapEnable(eventTap, false);

        if (runLoop != IntPtr.Zero)
        {
            CFRunLoopStop(runLoop);
            CFRunLoopWakeUp(runLoop);
        }

        if (eventTapThread != null && eventTapThread != Thread.CurrentThread)
            eventTapThread.Join(TimeSpan.FromSeconds(1));

        lock (_lifecycleLock)
        {
            _eventTapThread = null;
            _startedEvent?.Dispose();
            _startedEvent = null;
        }

        _fnLongPressTimer?.Stop();
        _fnKeyPressed = false;
        _gestureTriggered = false;
        _trackpadTouchDetected = false;
        _isEnabled = false;
        Console.WriteLine("Mac Fn key trigger listener stopped");
    }

    private IntPtr CreateEventTap()
    {
        var mask = CGEventMaskBit(CGEventType.FlagsChanged) |
                   CGEventMaskBit(CGEventType.MouseMoved) |
                   CGEventMaskBit(CGEventType.LeftMouseDragged) |
                   CGEventMaskBit(CGEventType.RightMouseDragged) |
                   CGEventMaskBit(CGEventType.ScrollWheel) |
                   CGEventMaskBit(CGEventType.KeyDown) |
                   CGEventMaskBit(CGEventType.LeftMouseDown);

        // Try Session location with active interception first (required to swallow space bar)
        var tap = CGEventTapCreate(
            CGEventTapLocation.Session,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.Default,
            mask,
            _eventTapCallback,
            IntPtr.Zero);

        if (tap != IntPtr.Zero)
        {
            _isTapActiveInterception = true;
            return tap;
        }

        tap = CGEventTapCreate(
            CGEventTapLocation.HID,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.Default,
            mask,
            _eventTapCallback,
            IntPtr.Zero);

        if (tap != IntPtr.Zero)
        {
            _isTapActiveInterception = true;
            return tap;
        }

        // Fall back to ListenOnly if active interception fails
        tap = CGEventTapCreate(
            CGEventTapLocation.HID,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.ListenOnly,
            mask,
            _eventTapCallback,
            IntPtr.Zero);

        _isTapActiveInterception = false;
        return tap;
    }

    private void HandleFnStateChange(bool fnPressed, IntPtr eventRef)
    {
        if (fnPressed && !_fnKeyPressed)
        {
            _fnKeyPressed = true;
            var loc = CGEventGetLocation(eventRef);
            _pressPoint = new Point(loc.X, loc.Y);
            _trackpadTouchDetected = false;
            _gestureTriggered = false;
            LogBoot($"HandleFnStateChange: Fn Pressed! Registered press point at {loc.X:0},{loc.Y:0}");
            if (_settings.EnableInputDiagnostics)
                Console.WriteLine($"[input] Fn key down registered at {loc.X:0},{loc.Y:0}");
        }
        else if (!fnPressed && _fnKeyPressed)
        {
            _fnKeyPressed = false;
            _fnLongPressTimer?.Stop();
            LogBoot($"HandleFnStateChange: Fn Released! _gestureTriggered={_gestureTriggered}");
            
            if (_settings.EnableInputDiagnostics)
                Console.WriteLine("[input] Fn key up registered, releasing activation");
                
            var loc = CGEventGetLocation(eventRef);
            ActivationReleased?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture, null, loc.X, loc.Y));
            
            _gestureTriggered = false;
            _trackpadTouchDetected = false;
        }
    }

    private void FnLongPressTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            _fnLongPressTimer?.Stop();

            if (_fnKeyPressed && !_gestureTriggered)
            {
                _gestureTriggered = true;
                if (_settings.EnableInputDiagnostics)
                    Console.WriteLine($"[input] Fn key long press triggered panel at {_pressPoint.X:0},{_pressPoint.Y:0}");
                ActivationRequested?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture, null, _pressPoint.X, _pressPoint.Y, isLongPress: true));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fn key long press timer failed: {ex}");
        }
    }

    private void RequestFnDragActivation(Point currentPoint, string reason)
    {
        _fnLongPressTimer?.Stop();
        _gestureTriggered = true;
        LogBoot($"HandleFnMovement: Fn drag menu triggered at {currentPoint.X:0},{currentPoint.Y:0}: {reason}");
        if (_settings.EnableInputDiagnostics)
            Console.WriteLine($"[input] Fn drag menu triggered at {currentPoint.X:0},{currentPoint.Y:0}: {reason}");

        ActivationRequested?.Invoke(
            this,
            new RadialMenuActivationEventArgs(
                RadialMenuActivationSource.TrackpadGesture,
                null,
                currentPoint.X,
                currentPoint.Y,
                isLongPress: false));
    }

    private IntPtr EventTapCallback(IntPtr proxy, CGEventType type, IntPtr eventRef, IntPtr refcon)
    {
        try
        {
            if (type == (CGEventType)0xFFFFFFFE || type == (CGEventType)0xFFFFFFFF)
            {
                LogBoot($"EventTapCallback: Event tap disabled by OS! type={type}. Re-enabling...");
                CGEventTapEnable(_eventTap, true);
                return eventRef;
            }

            var flags = CGEventGetFlags(eventRef);
            var fnPressed = (flags & (1UL << 23)) != 0;

            if (type == CGEventType.FlagsChanged)
            {
                if (_settings.EnableInputDiagnostics)
                    Console.WriteLine($"[input] Callback Event: type={type}, flags={flags:X}, fnPressed={fnPressed}, _fnKeyPressed={_fnKeyPressed}");
            }
            else if (type == CGEventType.KeyDown)
            {
                var keyCode = (ushort)CGEventGetIntegerValueField(eventRef, 9);
                if (_settings.EnableInputDiagnostics)
                    Console.WriteLine($"[input] Callback Event: type={type}, keyCode={keyCode}, keyName='{GetKeyName(keyCode)}', fnPressed={fnPressed}, _fnKeyPressed={_fnKeyPressed}");
            }
            else if (_fnKeyPressed)
            {
                if (_settings.EnableInputDiagnostics)
                    Console.WriteLine($"[input] Callback Event while Fn held: type={type}, fnPressed={fnPressed}");
            }

            if (type == CGEventType.KeyDown)
            {
                var keyCode = (ushort)CGEventGetIntegerValueField(eventRef, 9); // kCGKeyboardEventKeycode = 9
                var optionPressed = (flags & (1UL << 19)) != 0;
                var commandPressed = (flags & (1UL << 20)) != 0;
                var shiftPressed = (flags & (1UL << 17)) != 0;
                var ctrlPressed = (flags & (1UL << 18)) != 0;

                var keyName = GetKeyName(keyCode);
                if (_settings.EnableInputDiagnostics)
                {
                    Console.WriteLine($"[input] KeyDown: keyCode={keyCode}, keyName='{keyName}', cmd={commandPressed}, ctrl={ctrlPressed}, alt={optionPressed}, shift={shiftPressed}");
                }

                // Check custom launcher trigger hotkey dynamically
                if (!string.IsNullOrEmpty(keyName))
                {
                    var hotkeyParts = new List<string>();
                    if (commandPressed) hotkeyParts.Add("cmd");
                    if (ctrlPressed) hotkeyParts.Add("ctrl");
                    if (optionPressed) hotkeyParts.Add("alt");
                    if (shiftPressed) hotkeyParts.Add("shift");
                    hotkeyParts.Add(keyName);

                    var hotkeyString = string.Join("+", hotkeyParts);

                    if (IsLauncherHotkeyMatch(hotkeyString))
                    {
                        lock (_bufferLock)
                        {
                            _charBuffer.Clear();
                        }
                        LauncherRequested?.Invoke(this, EventArgs.Empty);
                        return IntPtr.Zero; // Consume the event
                    }

                    if (IsMousePanelHotkeyMatch(hotkeyString))
                    {
                        lock (_bufferLock)
                        {
                            _charBuffer.Clear();
                        }
                        var location = CGEventGetLocation(eventRef);
                        var args = new HotkeyTriggeredEventArgs(hotkeyString) { ScreenX = location.X, ScreenY = location.Y };
                        HotkeyTriggered?.Invoke(this, args);
                        return IntPtr.Zero; // Consume the event
                    }
                }

                // Check other global hotkeys
                if (!string.IsNullOrEmpty(keyName))
                {
                    var hotkeyParts = new List<string>();
                    if (commandPressed) hotkeyParts.Add("cmd");
                    if (ctrlPressed) hotkeyParts.Add("ctrl");
                    if (optionPressed) hotkeyParts.Add("alt"); // standard: command is cmd, option is alt, control is ctrl, shift is shift
                    if (shiftPressed) hotkeyParts.Add("shift");
                    hotkeyParts.Add(keyName);

                    var hotkeyString = string.Join("+", hotkeyParts);

                    var args = new HotkeyTriggeredEventArgs(hotkeyString);
                    HotkeyTriggered?.Invoke(this, args);

                    if (args.Handled)
                    {
                        lock (_bufferLock)
                        {
                            _charBuffer.Clear();
                        }
                        return IntPtr.Zero; // Consume global event
                    }
                }

                // Global Text Abbreviation Buffering & Expansion Logic
                if (commandPressed || ctrlPressed || optionPressed)
                {
                    lock (_bufferLock)
                    {
                        _charBuffer.Clear();
                    }
                }
                else if (IsNavigationKey(keyCode))
                {
                    lock (_bufferLock)
                    {
                        _charBuffer.Clear();
                    }
                }
                else if (keyCode == 0x33) // Backspace/Delete
                {
                    lock (_bufferLock)
                    {
                        if (_charBuffer.Length > 0)
                        {
                            _charBuffer.Length--;
                        }
                    }
                }
                else if (keyCode == 0x31) // Space Key Down (Trigger key)
                {
                    string currentBufferText;
                    lock (_bufferLock)
                    {
                        currentBufferText = _charBuffer.ToString();
                    }

                    if (!string.IsNullOrEmpty(currentBufferText))
                    {
                        string? expandedText = null;
                        lock (_bufferLock)
                        {
                            if (_abbreviations.TryGetValue(currentBufferText, out var val))
                            {
                                expandedText = val;
                            }
                        }

                        if (expandedText != null)
                        {
                            lock (_bufferLock)
                            {
                                _charBuffer.Clear();
                            }

                            int backspaceCount = _isTapActiveInterception ? currentBufferText.Length : currentBufferText.Length + 1;
                            SimulateTextExpansion(backspaceCount, expandedText);

                            if (_isTapActiveInterception)
                            {
                                return IntPtr.Zero; // Swallowed the Space key cleanly!
                            }
                        }
                        else
                        {
                            lock (_bufferLock)
                            {
                                _charBuffer.Clear();
                            }
                        }
                    }
                }
                else
                {
                    var typedChar = GetKeyChar(keyCode, shiftPressed);
                    if (typedChar.HasValue)
                    {
                        lock (_bufferLock)
                        {
                            _charBuffer.Append(typedChar.Value);
                            if (_charBuffer.Length > 50)
                            {
                                _charBuffer.Remove(0, _charBuffer.Length - 50);
                            }
                        }
                    }
                    else
                    {
                        lock (_bufferLock)
                        {
                            _charBuffer.Clear();
                        }
                    }
                }
            }

            if (type == CGEventType.FlagsChanged)
            {
                HandleFnStateChange(fnPressed, eventRef);
            }
            else if (type == CGEventType.LeftMouseDown || type == CGEventType.RightMouseDown || type == CGEventType.ScrollWheel)
            {
                lock (_bufferLock)
                {
                    _charBuffer.Clear();
                }
                if (type == CGEventType.ScrollWheel)
                {
                    HandleFnStateChange(fnPressed, eventRef);
                }
            }
            else if (type == CGEventType.MouseMoved || type == CGEventType.LeftMouseDragged || type == CGEventType.RightMouseDragged)
            {
                // Sync Fn key state on mouse movements/drags/scrolls as fallback
                HandleFnStateChange(fnPressed, eventRef);

                if (_fnKeyPressed)
                {
                    var currentPoint = GetEventLocation(eventRef);
                    if (_gestureTriggered)
                    {
                        if (_settings.EnableInputDiagnostics)
                            Console.WriteLine($"[input] HandleFnMovement: Updating active gesture at {currentPoint.X:0},{currentPoint.Y:0}");
                        ActivationUpdated?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture, null, currentPoint.X, currentPoint.Y));
                    }
                    else
                    {
                        var dx = currentPoint.X - _pressPoint.X;
                        var dy = currentPoint.Y - _pressPoint.Y;
                        var distanceSquared = dx * dx + dy * dy;
                        var threshold = Math.Max(1, _settings.DragThresholdPixels);

                        if (!_trackpadTouchDetected)
                        {
                            _trackpadTouchDetected = true;
                            if (_settings.EnableInputDiagnostics)
                                Console.WriteLine($"[input] HandleFnMovement: First pointer movement while Fn held at {currentPoint.X:0},{currentPoint.Y:0}; distanceSquared={distanceSquared:0.0}, thresholdSquared={threshold * threshold}");
                            if (distanceSquared >= threshold * threshold)
                            {
                                RequestFnDragActivation(currentPoint, "first movement threshold reached");
                                return eventRef;
                            }
                        }
                        else
                        {
                            if (distanceSquared >= threshold * threshold)
                            {
                                RequestFnDragActivation(currentPoint, "drag threshold reached");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fn key trigger callback failed: {ex}");
        }

        return eventRef;
    }

    private bool IsLauncherHotkeyMatch(string currentPressed)
    {
        if (string.IsNullOrEmpty(_settings.LauncherHotkey))
            return false;

        return NormalizeHotkey(_settings.LauncherHotkey) == NormalizeHotkey(currentPressed);
    }

    private bool IsMousePanelHotkeyMatch(string currentPressed)
    {
        if (string.IsNullOrEmpty(_settings.MousePanelHotkey))
            return false;

        return NormalizeHotkey(_settings.MousePanelHotkey) == NormalizeHotkey(currentPressed);
    }

    private static string NormalizeHotkey(string hotkey)
    {
        var parts = hotkey.Trim().ToLowerInvariant().Split('+');
        var modifiers = new HashSet<string>();
        string key = string.Empty;

        foreach (var p in parts)
        {
            if (p == "cmd" || p == "command" || p == "⌘" || p == "meta") modifiers.Add("cmd");
            else if (p == "alt" || p == "opt" || p == "option" || p == "⌥") modifiers.Add("alt");
            else if (p == "ctrl" || p == "control" || p == "⌃") modifiers.Add("ctrl");
            else if (p == "shift" || p == "⇧") modifiers.Add("shift");
            else key = p;
        }

        var sorted = new List<string>();
        if (modifiers.Contains("cmd")) sorted.Add("cmd");
        if (modifiers.Contains("ctrl")) sorted.Add("ctrl");
        if (modifiers.Contains("alt")) sorted.Add("alt");
        if (modifiers.Contains("shift")) sorted.Add("shift");
        if (!string.IsNullOrEmpty(key)) sorted.Add(key);

        return string.Join("+", sorted);
    }

    private static bool IsNavigationKey(ushort keyCode)
    {
        return keyCode switch
        {
            0x7B => true, // Left Arrow
            0x7C => true, // Right Arrow
            0x7D => true, // Down Arrow
            0x7E => true, // Up Arrow
            0x73 => true, // Home
            0x77 => true, // End
            0x74 => true, // Page Up
            0x79 => true, // Page Down
            0x24 => true, // Return/Enter
            0x35 => true, // Escape
            0x30 => true, // Tab
            _ => false
        };
    }

    private static char? GetKeyChar(ushort keyCode, bool shiftPressed)
    {
        return keyCode switch
        {
            0x00 => shiftPressed ? 'A' : 'a',
            0x01 => shiftPressed ? 'S' : 's',
            0x02 => shiftPressed ? 'D' : 'd',
            0x03 => shiftPressed ? 'F' : 'f',
            0x04 => shiftPressed ? 'H' : 'h',
            0x05 => shiftPressed ? 'G' : 'g',
            0x06 => shiftPressed ? 'Z' : 'z',
            0x07 => shiftPressed ? 'X' : 'x',
            0x08 => shiftPressed ? 'C' : 'c',
            0x09 => shiftPressed ? 'V' : 'v',
            0x0B => shiftPressed ? 'B' : 'b',
            0x0C => shiftPressed ? 'Q' : 'q',
            0x0D => shiftPressed ? 'W' : 'w',
            0x0E => shiftPressed ? 'E' : 'e',
            0x0F => shiftPressed ? 'R' : 'r',
            0x10 => shiftPressed ? 'Y' : 'y',
            0x11 => shiftPressed ? 'T' : 't',
            0x12 => shiftPressed ? '!' : '1',
            0x13 => shiftPressed ? '@' : '2',
            0x14 => shiftPressed ? '#' : '3',
            0x15 => shiftPressed ? '$' : '4',
            0x16 => shiftPressed ? '^' : '6',
            0x17 => shiftPressed ? '%' : '5',
            0x18 => shiftPressed ? '+' : '=',
            0x19 => shiftPressed ? '(' : '9',
            0x1A => shiftPressed ? '&' : '7',
            0x1B => shiftPressed ? '_' : '-',
            0x1C => shiftPressed ? '*' : '8',
            0x1D => shiftPressed ? ')' : '0',
            0x1E => shiftPressed ? '}' : ']',
            0x1F => shiftPressed ? 'O' : 'o',
            0x20 => shiftPressed ? 'U' : 'u',
            0x21 => shiftPressed ? '{' : '[',
            0x22 => shiftPressed ? 'I' : 'i',
            0x23 => shiftPressed ? 'P' : 'p',
            0x25 => shiftPressed ? 'L' : 'l',
            0x26 => shiftPressed ? 'J' : 'j',
            0x27 => shiftPressed ? '"' : '\'',
            0x28 => shiftPressed ? 'K' : 'k',
            0x29 => shiftPressed ? ':' : ';',
            0x2A => shiftPressed ? '|' : '\\',
            0x2B => shiftPressed ? '<' : ',',
            0x2C => shiftPressed ? '?' : '/',
            0x2D => shiftPressed ? 'N' : 'n',
            0x2E => shiftPressed ? 'M' : 'm',
            0x2F => shiftPressed ? '>' : '.',
            _ => null
        };
    }

    private static void SimulateTextExpansion(int backspaceCount, string expandedText)
    {
        Task.Run(() =>
        {
            try
            {
                // 1. Write the expanded text to clipboard using pbcopy
                SetClipboardText(expandedText);
                Thread.Sleep(50); // wait briefly for clipboard system to register it

                // 2. Send backspaces to delete the abbreviation
                for (int i = 0; i < backspaceCount; i++)
                {
                    PostKeyboardEvent(0x33, true, CGEventFlags.None);
                    Thread.Sleep(10);
                    PostKeyboardEvent(0x33, false, CGEventFlags.None);
                    Thread.Sleep(10);
                }

                // Wait a tiny bit before pasting
                Thread.Sleep(50);

                // 3. Send Command + V to paste the expanded text
                PostKeyboardEvent(0x09, true, CGEventFlags.MaskCommand);
                Thread.Sleep(15);
                PostKeyboardEvent(0x09, false, CGEventFlags.MaskCommand);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SimulateTextExpansion failed: {ex}");
            }
        });
    }

    private static void SetClipboardText(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "pbcopy",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            process.Start();
            using (var writer = process.StandardInput)
            {
                writer.Write(text);
            }
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set clipboard via pbcopy: {ex}");
        }
    }

    private static void PostKeyboardEvent(ushort keyCode, bool keyDown, CGEventFlags flags)
    {
        var eventRef = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown);
        if (eventRef == IntPtr.Zero)
            return;

        try
        {
            CGEventSetFlags(eventRef, flags);
            CGEventPost(CGEventTapLocation.HID, eventRef);
        }
        finally
        {
            CFRelease(eventRef);
        }
    }

    private static string GetKeyName(ushort keyCode)
    {
        return keyCode switch
        {
            0x00 => "a", 0x01 => "s", 0x02 => "d", 0x03 => "f", 0x04 => "h", 0x05 => "g",
            0x06 => "z", 0x07 => "x", 0x08 => "c", 0x09 => "v", 0x0B => "b", 0x0C => "q",
            0x0D => "w", 0x0E => "e", 0x0F => "r", 0x10 => "y", 0x11 => "t",
            0x12 => "1", 0x13 => "2", 0x14 => "3", 0x15 => "4", 0x16 => "6", 0x17 => "5",
            0x18 => "=", 0x19 => "9", 0x1A => "7", 0x1B => "-", 0x1C => "8", 0x1D => "0",
            0x1E => "]", 0x1F => "o", 0x20 => "u", 0x21 => "[", 0x22 => "i", 0x23 => "p",
            0x25 => "l", 0x26 => "j", 0x27 => "'", 0x28 => "k", 0x29 => ";", 0x2A => "\\",
            0x2B => ",", 0x2C => "/", 0x2D => "n", 0x2E => "m", 0x2F => ".",
            0x30 => "tab", 0x31 => "space", 0x33 => "delete", 0x35 => "escape",
            _ => string.Empty
        };
    }

    private Point GetEventLocation(IntPtr eventPtr)
    {
        var point = CGEventGetLocation(eventPtr);
        return new Point(point.X, point.Y);
    }

    private static void LogBoot(string message)
    {
        MacLogger.WriteLog("FnListener", message);
    }

    private static bool RequestAccessibilityTrustPrompt()
    {
        try
        {
            var key = CreateNSString("AXTrustedCheckOptionPrompt");
            var value = objc_msgSend_bool(objc_getClass("NSNumber"), sel_registerName("numberWithBool:"), 1);
            var options = objc_msgSend_objectKey(
                objc_getClass("NSDictionary"),
                sel_registerName("dictionaryWithObject:forKey:"),
                value,
                key);

            return options != IntPtr.Zero
                ? AXIsProcessTrustedWithOptions(options)
                : AXIsProcessTrusted();
        }
        catch (Exception ex)
        {
            LogBoot($"RequestAccessibilityTrustPrompt failed: {ex.GetType().Name} - {ex.Message}");
            return AXIsProcessTrusted();
        }
    }

    private static IntPtr CreateNSString(string value)
    {
        return objc_msgSend_string(
            objc_getClass("NSString"),
            sel_registerName("stringWithUTF8String:"),
            value);
    }

    public void Dispose()
    {
        Stop();
        _fnLongPressTimer?.Dispose();
    }

    private record Point(double X, double Y);

    #region P/Invoke

    private enum CGEventTapLocation : uint
    {
        HID = 0,
        Session = 1,
        AnnotatedSession = 2
    }

    private enum CGEventTapPlacement : uint
    {
        HeadInsertEventTap = 0,
        TailAppendEventTap = 1
    }

    private enum CGEventTapOptions : uint
    {
        Default = 0,
        ListenOnly = 1
    }

    private enum CGEventType : uint
    {
        LeftMouseDown = 1,
        LeftMouseUp = 2,
        RightMouseDown = 3,
        RightMouseUp = 4,
        MouseMoved = 5,
        LeftMouseDragged = 6,
        RightMouseDragged = 7,
        KeyDown = 10,
        FlagsChanged = 12,
        ScrollWheel = 22
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern long CGEventGetIntegerValueField(IntPtr @event, int field);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventTapCreate(
        CGEventTapLocation tap,
        CGEventTapPlacement place,
        CGEventTapOptions options,
        ulong eventsOfInterest,
        CGEventTapCallBack callback,
        IntPtr userInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGEventTapCallBack(IntPtr proxy, CGEventType type, IntPtr @event, IntPtr refcon);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, uint order);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRunLoopGetMain();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopWakeUp(IntPtr runLoop);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopRun();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopStop(IntPtr runLoop);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern ulong CGEventGetFlags(IntPtr @event);

    private static ulong CGEventMaskBit(CGEventType type) => 1UL << (int)type;

    private static readonly IntPtr CFRunLoopModeDefaultMode =
        CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", CFStringEncodingUtf8);

    private static readonly IntPtr CFRunLoopModeCommonModes =
        CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopCommonModes", CFStringEncodingUtf8);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, uint encoding);

    private const uint CFStringEncodingUtf8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;
    }

    [Flags]
    private enum CGEventFlags : ulong
    {
        None = 0,
        MaskShift = 1UL << 17,
        MaskControl = 1UL << 18,
        MaskAlternate = 1UL << 19,
        MaskCommand = 1UL << 20
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr @event, CGEventFlags flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(CGEventTapLocation tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern bool CGPreflightListenEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern bool CGRequestListenEventAccess();

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_string(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_bool(IntPtr receiver, IntPtr selector, byte value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_objectKey(IntPtr receiver, IntPtr selector, IntPtr obj, IntPtr key);

    #endregion
}
