using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yanzi.Shared;

namespace Yanzi.Platform.Mac;

internal sealed class MacPrivateMultitouchInputTriggerListener : IGlobalInputTriggerListener
{
    private const string FrameworkPath = "/System/Library/PrivateFrameworks/MultitouchSupport.framework/MultitouchSupport";

    private readonly GlobalInputTriggerSettings _settings;
    private readonly object _stateLock = new();
    private readonly System.Timers.Timer _releaseTimer;
    private readonly List<IntPtr> _devices = [];
    private MTContactFrameCallback? _callback;
    private IntPtr _frameworkHandle;
    private bool _isEnabled;
    private bool _gestureTriggered;
    private int _lastFingerCount;
    private ContactPoint? _centroidStart;
    private ContactPoint? _centroidAtActivation;
    private ScreenPoint? _cursorAtActivation;
    private DateTime _lastFrameAtUtc;

    private MTDeviceCreateListDelegate? _createList;
    private MTRegisterContactFrameCallbackDelegate? _registerCallback;
    private MTDeviceStartDelegate? _deviceStart;
    private MTDeviceStopDelegate? _deviceStop;

    public MacPrivateMultitouchInputTriggerListener(GlobalInputTriggerSettings settings)
    {
        _settings = settings;
        _releaseTimer = new System.Timers.Timer { AutoReset = false };
        _releaseTimer.Elapsed += (_, _) => CompleteDebouncedRelease();
    }

    public bool IsRunning => _isEnabled;

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    public event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;

    public bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(FrameworkPath);

    public void Start()
    {
        if (_isEnabled)
            return;

        if (!IsAvailable)
        {
            LogMultitouch($"private MultitouchSupport dylib unavailable at {FrameworkPath}");
            return;
        }

        if (!TryLoadFramework())
            return;

        var deviceList = _createList!();
        var count = CFArrayGetCount(deviceList);
        if (count <= 0)
        {
            LogMultitouch("no multitouch devices found");
            return;
        }

        _callback = ContactFrameCallback;
        for (var index = 0; index < count; index++)
        {
            var device = CFArrayGetValueAtIndex(deviceList, index);
            if (device == IntPtr.Zero)
                continue;

            _devices.Add(device);
            _registerCallback!(device, _callback);
            _deviceStart!(device, 0);
        }

        _isEnabled = _devices.Count > 0;
        Console.WriteLine(_isEnabled
            ? $"Mac private multitouch trigger listener started ({_devices.Count} device(s))"
            : "Mac private multitouch trigger listener did not find usable devices");
    }

    public void Stop()
    {
        if (!_isEnabled)
            return;

        foreach (var device in _devices)
            _deviceStop?.Invoke(device);

        _devices.Clear();
        _isEnabled = false;
        _releaseTimer.Stop();
        ResetGestureState();
        Console.WriteLine("Mac private multitouch trigger listener stopped");
    }

    public void Dispose()
    {
        Stop();
        _releaseTimer.Dispose();
        if (_frameworkHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_frameworkHandle);
            _frameworkHandle = IntPtr.Zero;
        }
    }

    private bool TryLoadFramework()
    {
        try
        {
            _frameworkHandle = NativeLibrary.Load(FrameworkPath);
            _createList = GetExport<MTDeviceCreateListDelegate>("MTDeviceCreateList");
            _registerCallback = GetExport<MTRegisterContactFrameCallbackDelegate>("MTRegisterContactFrameCallback");
            _deviceStart = GetExport<MTDeviceStartDelegate>("MTDeviceStart");
            _deviceStop = GetExport<MTDeviceStopDelegate>("MTDeviceStop");
            return true;
        }
        catch (Exception ex)
        {
            LogMultitouch($"failed to load private MultitouchSupport: {ex.Message}");
            return false;
        }
    }

    private T GetExport<T>(string name)
        where T : Delegate
    {
        var symbol = NativeLibrary.GetExport(_frameworkHandle, name);
        return Marshal.GetDelegateForFunctionPointer<T>(symbol);
    }

    private void ContactFrameCallback(IntPtr device, IntPtr data, int fingerCount, double timestamp, int frame)
    {
        try
        {
            HandleContactFrame(data, fingerCount);
        }
        catch (Exception ex)
        {
            LogMultitouch($"contact frame failed: {ex}");
        }
    }

    private void HandleContactFrame(IntPtr data, int fingerCount)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastFrameAtUtc).TotalMilliseconds > _settings.TrackpadGestureResetMs)
            ResetGestureState();

        _lastFrameAtUtc = now;
        _lastFingerCount = fingerCount;
        LogMultitouch($"frame fingers={fingerCount}, triggered={_gestureTriggered}");

        if (!_settings.EnableTrackpadGesture ||
            _settings.TrackpadGestureFingerCount != fingerCount ||
            TrackpadGestureModes.Normalize(_settings.TrackpadGestureMode) != TrackpadGestureModes.FingerMove)
        {
            if (_gestureTriggered)
                ScheduleDebouncedRelease(fingerCount);
            else
                ResetGestureState();

            return;
        }

        CancelDebouncedRelease();
        var centroid = ReadCentroid(data, fingerCount);
        if (_centroidStart == null)
        {
            _centroidStart = centroid;
            return;
        }

        if (_gestureTriggered)
        {
            PublishGestureUpdate(centroid, fingerCount);
            return;
        }

        var threshold = Math.Max(0.002, _settings.TrackpadGestureNormalizedThreshold);
        var dx = centroid.X - _centroidStart.Value.X;
        var dy = centroid.Y - _centroidStart.Value.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        LogMultitouch($"centroid dx={dx:0.0000}, dy={dy:0.0000}, distance={distance:0.0000}, threshold={threshold:0.0000}");

        if (!IsDiagonalUpRightGesture(dx, dy, threshold))
            return;

        _gestureTriggered = true;
        _centroidAtActivation = centroid;
        _cursorAtActivation = GetCurrentCursorLocation();
        ActivationRequested?.Invoke(
            this,
            new RadialMenuActivationEventArgs(
                RadialMenuActivationSource.TrackpadGesture,
                fingerCount,
                _cursorAtActivation.Value.X,
                _cursorAtActivation.Value.Y));
    }

    private void ScheduleDebouncedRelease(int fingerCount)
    {
        if (_releaseTimer.Enabled)
            return;

        _releaseTimer.Interval = Math.Max(80, _settings.TrackpadGestureReleaseDelayMs);
        _releaseTimer.Start();
        LogMultitouch($"release pending after finger mismatch ({fingerCount})");
    }

    private void CancelDebouncedRelease()
    {
        if (!_releaseTimer.Enabled)
            return;

        _releaseTimer.Stop();
        LogMultitouch("release canceled; target fingers restored");
    }

    private void CompleteDebouncedRelease()
    {
        var shouldRelease = false;
        lock (_stateLock)
        {
            if (_gestureTriggered && _lastFingerCount != _settings.TrackpadGestureFingerCount)
            {
                shouldRelease = true;
                ResetGestureState();
            }
        }

        if (!shouldRelease)
            return;

        LogMultitouch("released after debounce");
        ActivationReleased?.Invoke(
            this,
            new RadialMenuActivationEventArgs(RadialMenuActivationSource.TrackpadGesture, _settings.TrackpadGestureFingerCount));
    }

    private void ResetGestureState()
    {
        _gestureTriggered = false;
        _centroidStart = null;
        _centroidAtActivation = null;
        _cursorAtActivation = null;
    }

    private void PublishGestureUpdate(ContactPoint centroid, int fingerCount)
    {
        if (_centroidAtActivation == null || _cursorAtActivation == null)
            return;

        var dx = centroid.X - _centroidAtActivation.Value.X;
        var dy = centroid.Y - _centroidAtActivation.Value.Y;
        var screenX = _cursorAtActivation.Value.X + dx * _settings.TrackpadGestureScreenScalePixels;
        var screenY = _cursorAtActivation.Value.Y - dy * _settings.TrackpadGestureScreenScalePixels;
        LogMultitouch($"update dx={dx:0.0000}, dy={dy:0.0000}, screen={screenX:0},{screenY:0}");
        WarpCursor(screenX, screenY);

        ActivationUpdated?.Invoke(
            this,
            new RadialMenuActivationEventArgs(
                RadialMenuActivationSource.TrackpadGesture,
                fingerCount,
                screenX,
                screenY));
    }

    private static ContactPoint ReadCentroid(IntPtr data, int fingerCount)
    {
        var stride = Marshal.SizeOf<MTContact>();
        double x = 0;
        double y = 0;

        for (var index = 0; index < fingerCount; index++)
        {
            var contact = Marshal.PtrToStructure<MTContact>(IntPtr.Add(data, index * stride));
            x += contact.NormalizedPosition.X;
            y += contact.NormalizedPosition.Y;
        }

        return new ContactPoint(x / fingerCount, y / fingerCount);
    }

    private static bool IsDiagonalUpRightGesture(double dx, double dy, double threshold)
    {
        var minimumComponent = Math.Max(0.0025, threshold * 0.7);
        if (dx < minimumComponent || dy < minimumComponent)
            return false;

        var maxComponent = Math.Max(dx, dy);
        var minComponent = Math.Min(dx, dy);
        return minComponent / maxComponent >= 0.45;
    }

    private void LogMultitouch(string message)
    {
        if (_settings.EnableInputDiagnostics)
            Console.WriteLine($"[multitouch] {message}");
    }

    private readonly record struct ContactPoint(double X, double Y);
    private readonly record struct ScreenPoint(double X, double Y);

    private static ScreenPoint GetCurrentCursorLocation()
    {
        var eventRef = CGEventCreate(IntPtr.Zero);
        try
        {
            var point = CGEventGetLocation(eventRef);
            return new ScreenPoint(point.X, point.Y);
        }
        finally
        {
            if (eventRef != IntPtr.Zero)
                CFRelease(eventRef);
        }
    }

    private static void WarpCursor(double x, double y)
    {
        CGWarpMouseCursorPosition(new CGPoint(x, y));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MTContactFrameCallback(IntPtr device, IntPtr data, int fingerCount, double timestamp, int frame);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MTDeviceCreateListDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MTRegisterContactFrameCallbackDelegate(IntPtr device, MTContactFrameCallback callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MTDeviceStartDelegate(IntPtr device, int unknown);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MTDeviceStopDelegate(IntPtr device);

    [StructLayout(LayoutKind.Sequential)]
    private struct MTPoint
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MTVector
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MTContact
    {
        public int Frame;
        public double Timestamp;
        public int Identifier;
        public int State;
        public int Unknown1;
        public int Unknown2;
        public MTPoint NormalizedPosition;
        public float Size;
        public int Unknown3;
        public float Angle;
        public float MajorAxis;
        public float MinorAxis;
        public MTVector Velocity;
        public int Unknown4;
        public int Unknown5;
        public float Unknown6;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public readonly double X;
        public readonly double Y;
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGWarpMouseCursorPosition(CGPoint newCursorPosition);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}
