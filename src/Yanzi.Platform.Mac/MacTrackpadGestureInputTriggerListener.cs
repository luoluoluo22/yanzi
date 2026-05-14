using System.Runtime.InteropServices;
using System.Timers;
using Timer = System.Timers.Timer;
using Yanzi.Shared;

namespace Yanzi.Platform.Mac;

internal sealed class MacTrackpadGestureInputTriggerListener : IGlobalInputTriggerListener
{
    private readonly GlobalInputTriggerSettings _settings;
    private readonly CGEventTapCallBack _eventTapCallback;
    private IntPtr _eventTap;
    private bool _isEnabled;
    private bool _primaryButtonPressed;
    private bool _gestureTriggered;
    private Point _primaryPressPoint = new(0, 0);
    private readonly Timer _gestureResetTimer;

    public MacTrackpadGestureInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _settings = settings;
        _eventTapCallback = EventTapCallback;
        _gestureResetTimer = new Timer();
        _gestureResetTimer.AutoReset = false;
        _gestureResetTimer.Elapsed += GestureResetTimer_Elapsed;
    }

    public bool IsRunning => _isEnabled;

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated
    {
        add { }
        remove { }
    }
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased
    {
        add { }
        remove { }
    }

    public void Start()
    {
        if (_isEnabled)
            return;

        _eventTap = CreateEventTap();
        if (_eventTap == IntPtr.Zero)
        {
            Console.WriteLine("Failed to create trackpad gesture event tap");
            return;
        }

        var runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, CFRunLoopModeDefaultMode);
        CFRelease(runLoopSource);

        CGEventTapEnable(_eventTap, true);
        _isEnabled = true;
        Console.WriteLine("Mac trackpad gesture trigger listener started");
        LogTrackpad($"settings enabled={_settings.EnableTrackpadGesture}, fingers={_settings.TrackpadGestureFingerCount}, mode={_settings.TrackpadGestureMode}, moveThreshold={_settings.TrackpadGestureMoveThresholdPixels}, scrollThreshold={_settings.TrackpadGestureScrollThreshold}, resetMs={_settings.TrackpadGestureResetMs}");
    }

    public void Stop()
    {
        if (!_isEnabled)
            return;

        CGEventTapEnable(_eventTap, false);
        CFRelease(_eventTap);
        _eventTap = IntPtr.Zero;
        _primaryButtonPressed = false;
        _gestureTriggered = false;
        _gestureResetTimer.Stop();
        _isEnabled = false;
        Console.WriteLine("Mac trackpad gesture trigger listener stopped");
    }

    public void Dispose()
    {
        Stop();
        _gestureResetTimer.Dispose();
    }

    private IntPtr CreateEventTap()
    {
        var mask = CGEventMaskBit(CGEventType.LeftMouseDown) |
                   CGEventMaskBit(CGEventType.LeftMouseUp) |
                   CGEventMaskBit(CGEventType.LeftMouseDragged) |
                   CGEventMaskBit(CGEventType.ScrollWheel);

        return CGEventTapCreate(
            CGEventTapLocation.HID,
            CGEventTapPlacement.HeadInsertEventTap,
            CGEventTapOptions.ListenOnly,
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
                case CGEventType.LeftMouseDown:
                    HandlePrimaryButtonDown(eventRef);
                    break;
                case CGEventType.LeftMouseUp:
                    HandlePrimaryButtonUp();
                    break;
                case CGEventType.LeftMouseDragged:
                    HandlePrimaryButtonDrag(eventRef);
                    break;
                case CGEventType.ScrollWheel:
                    HandleScrollWheel(eventRef);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Trackpad gesture callback failed: {ex}");
        }

        return eventRef;
    }

    private void HandlePrimaryButtonDown(IntPtr eventPtr)
    {
        _primaryButtonPressed = true;
        _gestureTriggered = false;
        _primaryPressPoint = GetEventLocation(eventPtr);
        LogTrackpad($"primary down at {_primaryPressPoint.X:0},{_primaryPressPoint.Y:0}");
    }

    private void HandlePrimaryButtonUp()
    {
        LogTrackpad("primary up");
        _primaryButtonPressed = false;
        _gestureTriggered = false;
    }

    private void HandlePrimaryButtonDrag(IntPtr eventPtr)
    {
        if (!IsFingerMoveGestureArmed())
        {
            LogTrackpad($"primary drag ignored: enabled={_settings.EnableTrackpadGesture}, fingers={_settings.TrackpadGestureFingerCount}, mode={_settings.TrackpadGestureMode}, primaryDown={_primaryButtonPressed}, alreadyTriggered={_gestureTriggered}");
            return;
        }

        var currentPoint = GetEventLocation(eventPtr);
        var dx = currentPoint.X - _primaryPressPoint.X;
        var dy = currentPoint.Y - _primaryPressPoint.Y;
        var distanceSquared = dx * dx + dy * dy;
        var threshold = Math.Max(1, _settings.TrackpadGestureMoveThresholdPixels);
        LogTrackpad($"primary drag dx={dx:0.0}, dy={dy:0.0}, distanceSquared={distanceSquared:0.0}, thresholdSquared={threshold * threshold}");

        if (distanceSquared < threshold * threshold)
            return;

        RequestTrackpadActivation(currentPoint, $"primary drag threshold reached");
    }

    private void HandleScrollWheel(IntPtr eventPtr)
    {
        var point = GetEventLocation(eventPtr);
        var deltaY = CGEventGetIntegerValueField(eventPtr, CGEventField.ScrollWheelEventDeltaAxis1);
        var deltaX = CGEventGetIntegerValueField(eventPtr, CGEventField.ScrollWheelEventDeltaAxis2);
        var fixedDeltaY = CGEventGetIntegerValueField(eventPtr, CGEventField.ScrollWheelEventFixedPtDeltaAxis1);
        var fixedDeltaX = CGEventGetIntegerValueField(eventPtr, CGEventField.ScrollWheelEventFixedPtDeltaAxis2);
        var totalDelta = Math.Abs(deltaX) + Math.Abs(deltaY) + Math.Abs(fixedDeltaX) + Math.Abs(fixedDeltaY);
        LogTrackpad($"scroll at {point.X:0},{point.Y:0}, deltaX={deltaX}, deltaY={deltaY}, fixedDeltaX={fixedDeltaX}, fixedDeltaY={fixedDeltaY}, primaryDown={_primaryButtonPressed}, triggered={_gestureTriggered}, total={totalDelta}");

        if (!IsThreeFingerMoveGestureArmed() || totalDelta < Math.Max(1, _settings.TrackpadGestureScrollThreshold))
            return;

        RequestTrackpadActivation(point, "scroll threshold reached");
    }

    private bool IsFingerMoveGestureArmed()
    {
        return _settings.EnableTrackpadGesture &&
               _settings.TrackpadGestureFingerCount == 2 &&
               TrackpadGestureModes.Normalize(_settings.TrackpadGestureMode) == TrackpadGestureModes.FingerMove &&
               _primaryButtonPressed &&
               !_gestureTriggered;
    }

    private bool IsThreeFingerMoveGestureArmed()
    {
        return _settings.EnableTrackpadGesture &&
               _settings.TrackpadGestureFingerCount == 3 &&
               TrackpadGestureModes.Normalize(_settings.TrackpadGestureMode) == TrackpadGestureModes.FingerMove &&
               !_gestureTriggered;
    }

    private void RequestTrackpadActivation(Point point, string reason)
    {
        _gestureTriggered = true;
        RestartGestureResetTimer();
        LogTrackpad($"trackpad gesture activation at {point.X:0},{point.Y:0}: {reason}");
        ActivationRequested?.Invoke(
            this,
            new RadialMenuActivationEventArgs(
                RadialMenuActivationSource.TrackpadGesture,
                _settings.TrackpadGestureFingerCount,
                point.X,
                point.Y));
    }

    private void RestartGestureResetTimer()
    {
        _gestureResetTimer.Stop();
        _gestureResetTimer.Interval = Math.Max(200, _settings.TrackpadGestureResetMs);
        _gestureResetTimer.Start();
    }

    private void GestureResetTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        _gestureTriggered = false;
        LogTrackpad("gesture reset");
    }

    private Point GetEventLocation(IntPtr eventPtr)
    {
        var point = CGEventGetLocation(eventPtr);
        return new Point(point.X, point.Y);
    }

    private void LogTrackpad(string message)
    {
        if (_settings.EnableInputDiagnostics)
            Console.WriteLine($"[trackpad] {message}");
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
        LeftMouseDragged = 6,
        ScrollWheel = 22
    }

    private enum CGEventField : uint
    {
        ScrollWheelEventDeltaAxis1 = 11,
        ScrollWheelEventDeltaAxis2 = 12,
        ScrollWheelEventFixedPtDeltaAxis1 = 93,
        ScrollWheelEventFixedPtDeltaAxis2 = 94
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
    private static extern long CGEventGetIntegerValueField(IntPtr @event, CGEventField field);

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
