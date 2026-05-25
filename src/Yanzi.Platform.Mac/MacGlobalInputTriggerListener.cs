using System.Runtime.InteropServices;
using System.Timers;
using Timer = System.Timers.Timer;
using Yanzi.Shared;

namespace Yanzi.Platform.Mac;

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
        }
    }

    public bool IsRunning => _listeners.Any(listener => listener.IsRunning);

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;

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
            listener.Dispose();
        }
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
                return false;
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
                    return false;

                _activeListener = listener;
            }

            if (_activeListener == null)
                _activeListener = listener;

            if (release)
            {
                if (!ReferenceEquals(_activeListener, listener))
                    return false;

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

    public MacSecondaryButtonInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _settings = settings;
        _eventTapCallback = EventTapCallback;
        _longPressTimer = new Timer();
        _longPressTimer.Elapsed += LongPressTimer_Elapsed;
    }

    public void Start()
    {
        if (_isEnabled)
            return;

        _eventTap = CreateEventTap();
        if (_eventTap == IntPtr.Zero)
        {
            Console.WriteLine("Failed to create event tap");
            return;
        }

        var runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, CFRunLoopModeDefaultMode);
        CFRelease(runLoopSource);

        CGEventTapEnable(_eventTap, true);

        _isEnabled = true;
        Console.WriteLine("Mac secondary-button trigger listener started");
        LogInput($"settings secondaryLongPress={_settings.EnableSecondaryButtonLongPress}, secondaryDrag={_settings.EnableSecondaryButtonDrag}");
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

        return CGEventTapCreate(
            CGEventTapLocation.HID,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.Default,
            mask,
            _eventTapCallback,
            IntPtr.Zero);
    }

    private IntPtr EventTapCallback(IntPtr proxy, CGEventType type, IntPtr eventRef, IntPtr refcon)
    {
        try
        {
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
                RequestActivation(RadialMenuActivationSource.SecondaryButton, currentPoint);
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
                RequestActivation(RadialMenuActivationSource.SecondaryButton, _pressPoint);
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

    private void RequestActivation(RadialMenuActivationSource source, Point point, int? fingerCount = null)
    {
        ActivationRequested?.Invoke(this, new RadialMenuActivationEventArgs(source, fingerCount, point.X, point.Y));
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
    private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    private static ulong CGEventMaskBit(CGEventType type) => 1UL << (int)type;

    private static readonly IntPtr CFRunLoopModeDefaultMode =
        CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", CFStringEncodingUtf8);

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
    private IntPtr _eventTap;
    private bool _isEnabled;
    private bool _fnKeyPressed;
    private bool _gestureTriggered;
    private Point _pressPoint = new(0, 0);
    private readonly CGEventTapCallBack _eventTapCallback;

    public bool IsRunning => _isEnabled;

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;

    public MacFnKeyInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _settings = settings;
        _eventTapCallback = EventTapCallback;
    }

    public void Start()
    {
        if (_isEnabled)
            return;

        _eventTap = CreateEventTap();
        if (_eventTap == IntPtr.Zero)
        {
            Console.WriteLine("Failed to create Fn key event tap");
            return;
        }

        var runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, CFRunLoopModeDefaultMode);
        CFRelease(runLoopSource);

        CGEventTapEnable(_eventTap, true);

        _isEnabled = true;
        Console.WriteLine("Mac Fn key trigger listener started");
    }

    public void Stop()
    {
        if (!_isEnabled)
            return;

        CGEventTapEnable(_eventTap, false);
        CFRelease(_eventTap);
        _eventTap = IntPtr.Zero;

        _fnKeyPressed = false;
        _gestureTriggered = false;
        _isEnabled = false;
        Console.WriteLine("Mac Fn key trigger listener stopped");
    }

    private IntPtr CreateEventTap()
    {
        var mask = CGEventMaskBit(CGEventType.FlagsChanged) |
                   CGEventMaskBit(CGEventType.MouseMoved) |
                    CGEventMaskBit(CGEventType.LeftMouseDragged) |
                    CGEventMaskBit(CGEventType.RightMouseDragged) |
                    CGEventMaskBit(CGEventType.ScrollWheel);

        return CGEventTapCreate(
            CGEventTapLocation.HID,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.ListenOnly,
            mask,
            _eventTapCallback,
            IntPtr.Zero);
    }

    private void HandleFnStateChange(bool fnPressed, IntPtr eventRef)
    {
        if (fnPressed && !_fnKeyPressed)
        {
            _fnKeyPressed = true;
            var loc = CGEventGetLocation(eventRef);
            _pressPoint = new Point(loc.X, loc.Y);
            if (_settings.EnableInputDiagnostics)
                Console.WriteLine($"[input] Fn key down registered at {loc.X:0},{loc.Y:0}");
        }
        else if (!fnPressed && _fnKeyPressed)
        {
            _fnKeyPressed = false;
            if (_gestureTriggered)
            {
                if (_settings.EnableInputDiagnostics)
                    Console.WriteLine("[input] Fn key up registered, releasing radial menu");
                ActivationReleased?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture));
            }
            _gestureTriggered = false;
        }
    }

    private IntPtr EventTapCallback(IntPtr proxy, CGEventType type, IntPtr eventRef, IntPtr refcon)
    {
        try
        {
            var flags = CGEventGetFlags(eventRef);
            var fnPressed = (flags & (1UL << 23)) != 0;

            if (type == CGEventType.FlagsChanged)
            {
                HandleFnStateChange(fnPressed, eventRef);
            }
            else if (type == CGEventType.MouseMoved || type == CGEventType.LeftMouseDragged || type == CGEventType.RightMouseDragged || type == CGEventType.ScrollWheel)
            {
                // Sync Fn key state on mouse movements/drags/scrolls as fallback
                HandleFnStateChange(fnPressed, eventRef);

                if (_fnKeyPressed)
                {
                    var currentPoint = GetEventLocation(eventRef);
                    if (_gestureTriggered)
                    {
                        ActivationUpdated?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture, null, currentPoint.X, currentPoint.Y));
                    }
                    else
                    {
                        var dx = currentPoint.X - _pressPoint.X;
                        var dy = currentPoint.Y - _pressPoint.Y;
                        var distanceSquared = dx * dx + dy * dy;
                        var threshold = _settings.DragThresholdPixels;

                        if (distanceSquared >= threshold * threshold)
                        {
                            _gestureTriggered = true;
                            if (_settings.EnableInputDiagnostics)
                                Console.WriteLine($"[input] Fn drag menu triggered at {currentPoint.X:0},{currentPoint.Y:0}");
                            ActivationRequested?.Invoke(this, new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture, null, currentPoint.X, currentPoint.Y));
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

    private Point GetEventLocation(IntPtr eventPtr)
    {
        var point = CGEventGetLocation(eventPtr);
        return new Point(point.X, point.Y);
    }

    public void Dispose()
    {
        Stop();
    }

    private record Point(double X, double Y);

    #region P/Invoke

    private enum CGEventTapLocation : uint
    {
        HID = 0
    }

    private enum CGEventTapPlacement : uint
    {
        HeadInsertEventTap = 0
    }

    private enum CGEventTapOptions : uint
    {
        ListenOnly = 1
    }

    private enum CGEventType : uint
    {
        MouseMoved = 5,
        LeftMouseDragged = 6,
        RightMouseDragged = 7,
        FlagsChanged = 12,
        ScrollWheel = 22
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
    private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern ulong CGEventGetFlags(IntPtr @event);

    private static ulong CGEventMaskBit(CGEventType type) => 1UL << (int)type;

    private static readonly IntPtr CFRunLoopModeDefaultMode =
        CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", CFStringEncodingUtf8);

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
