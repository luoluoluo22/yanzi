namespace Yanzi.Shared;

public class RadialMenuSettings
{
    public const int InnerSlotCount = 8;
    public const int OuterSlotCount = 16;
    
    public bool Enabled { get; set; } = true;
    public string MouseTriggerMode { get; set; } = string.Empty;
    public int RadiusPixels { get; set; } = 90;
    public int DeadZonePixels { get; set; } = 24;
    public bool TriggerRightButtonDrag { get; set; }
    public int DragThresholdPixels { get; set; } = 30;
    public bool TriggerRightButtonLongPress { get; set; } = true;
    public bool TriggerMiddleButtonLongPress { get; set; }
    public bool TriggerCapsLockHold { get; set; }
    public bool TriggerMiddleButtonDown { get; set; }
    public bool TriggerX1ButtonDown { get; set; }
    public bool TriggerX2ButtonDown { get; set; }
    public bool TriggerHorizontalWheel { get; set; }
    public bool TriggerCtrlLeftClick { get; set; }
    public bool TriggerCtrlRightClick { get; set; }
    public bool TriggerTrackpadSecondaryClickLongPress { get; set; } = true;
    public bool TriggerTrackpadGesture { get; set; }
    public int TrackpadGestureFingerCount { get; set; } = 3;
    public string TrackpadTriggerMode { get; set; } = TrackpadTriggerModes.SecondaryClickLongPress;
    public string SelectedPageId { get; set; } = string.Empty;
    public List<RadialMenuPageSettings> Pages { get; set; } = [];
    public Dictionary<string, RadialMenuSlotSettings> Slots { get; set; } = new();
}

public static class TrackpadTriggerModes
{
    public const string None = "none";
    public const string SecondaryClickLongPress = "secondaryclicklongpress";
    public const string Gesture = "gesture";

    public static string Normalize(string mode)
    {
        return mode?.Trim().ToLowerInvariant() ?? None;
    }
}

public class RadialMenuPageSettings
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class RadialMenuSlotSettings
{
    public string? ExtensionId { get; set; }
    public string? ChildPageId { get; set; }
}

public enum RadialMenuRing
{
    Inner,
    Outer,
    Child,
    GrandChild
}
