namespace Yanzi.Shared;

public enum RadialMenuActivationSource
{
    SecondaryButton,
    TrackpadSecondaryClick,
    TrackpadGesture,
    Unknown
}

public sealed class RadialMenuActivationEventArgs : EventArgs
{
    public RadialMenuActivationEventArgs(
        RadialMenuActivationSource source,
        int? fingerCount = null,
        double? screenX = null,
        double? screenY = null,
        bool isLongPress = false)
    {
        Source = source;
        FingerCount = fingerCount;
        ScreenX = screenX;
        ScreenY = screenY;
        IsLongPress = isLongPress;
    }

    public RadialMenuActivationSource Source { get; }
    public int? FingerCount { get; }
    public double? ScreenX { get; }
    public double? ScreenY { get; }
    public bool IsLongPress { get; }
    public bool HasScreenPosition => ScreenX.HasValue && ScreenY.HasValue;
}

public class HotkeyTriggeredEventArgs : EventArgs
{
    public string Hotkey { get; }
    public bool Handled { get; set; }
    public double? ScreenX { get; set; }
    public double? ScreenY { get; set; }

    public HotkeyTriggeredEventArgs(string hotkey)
    {
        Hotkey = hotkey;
    }
}

public interface IGlobalInputTriggerListener : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();

    event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested;
    event EventHandler<RadialMenuActivationEventArgs>? ActivationUpdated;
    event EventHandler<RadialMenuActivationEventArgs>? ActivationReleased;
    event EventHandler? LauncherRequested;
    event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    void UpdateAbbreviations(Dictionary<string, string> abbreviations);
}

public interface IGlobalInputTriggerListenerFactory
{
    IGlobalInputTriggerListener Create(GlobalInputTriggerSettings settings);
}

public sealed class DisabledGlobalInputTriggerListenerFactory : IGlobalInputTriggerListenerFactory
{
    public IGlobalInputTriggerListener Create(GlobalInputTriggerSettings settings) => new DisabledGlobalInputTriggerListener();
}

public sealed class DisabledGlobalInputTriggerListener : IGlobalInputTriggerListener
{
    public bool IsRunning => false;

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

    public event EventHandler<RadialMenuActivationEventArgs>? ActivationRequested
    {
        add { }
        remove { }
    }

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

    public void UpdateAbbreviations(Dictionary<string, string> abbreviations)
    {
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

public class GlobalInputTriggerSettings
{
    public int LongPressThresholdMs { get; set; } = 500;
    public int DragThresholdPixels { get; set; } = 30;
    public bool EnableSecondaryButtonLongPress { get; set; } = true;
    public bool EnableSecondaryButtonDrag { get; set; } = true;
    public bool EnableTrackpadGesture { get; set; }
    public int TrackpadGestureFingerCount { get; set; } = 3;
    public string TrackpadGestureMode { get; set; } = TrackpadGestureModes.FingerMove;
    public int TrackpadGestureMoveThresholdPixels { get; set; } = 18;
    public int TrackpadGestureScrollThreshold { get; set; } = 3;
    public int TrackpadGestureResetMs { get; set; } = 700;
    public int TrackpadGestureReleaseDelayMs { get; set; } = 220;
    public double TrackpadGestureNormalizedThreshold { get; set; } = 0.025;
    public double TrackpadGestureScreenScalePixels { get; set; } = 2200;
    public bool EnableInputDiagnostics { get; set; }
    public string LauncherHotkey { get; set; } = "alt+space";
    public string MousePanelHotkey { get; set; } = "alt+m";
}

public static class TrackpadGestureModes
{
    public const string None = "none";
    public const string FingerMove = "fingermove";

    public static string Normalize(string mode)
    {
        return mode?.Trim().ToLowerInvariant() ?? None;
    }
}
