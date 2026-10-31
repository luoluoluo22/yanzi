using OpenQuickHost;

static void Verify(bool passed, string message)
{
    if (!passed) throw new Exception("FAIL " + message);
    Console.WriteLine("PASS " + message);
}
static bool Near(double a, double b) => Math.Abs(a - b) < 0.00001;

static void RoundTrip(double initialLeft, double initialWidth, double expandedWidth, double restoredWidth)
{
    var center = initialLeft + initialWidth / 2;
    var originalScopeX = initialLeft + 20;
    var expanded = AiWindowResizeGeometry.KeepCenterAndScopeTabs(initialLeft, initialWidth,
        expandedWidth, 0);
    Verify(Near(expanded.Left + expandedWidth / 2, center),
        "expansion retains window center");
    Verify(Near(expanded.Left + expanded.PopupOffset + 20, originalScopeX),
        "expansion retains top tabs position");
    Verify(Near(expanded.Left, initialLeft - (expandedWidth - initialWidth) / 2),
        "conversation panel expands equally to left and right");

    var restored = AiWindowResizeGeometry.KeepCenterAndScopeTabs(expanded.Left,
        expandedWidth, restoredWidth, expanded.PopupOffset);
    Verify(Near(restored.Left + restoredWidth / 2, center), "restore retains center");
    Verify(Near(restored.Left + restored.PopupOffset + 20, originalScopeX),
        "restore retains top tabs position");

    var again = AiWindowResizeGeometry.KeepCenterAndScopeTabs(restored.Left,
        restoredWidth, expandedWidth, restored.PopupOffset);
    Verify(Near(again.Left + again.PopupOffset + 20, originalScopeX),
        "repeated scope switching does not drift");
}

RoundTrip(600, 700, 1100, 700);
RoundTrip(100, 850, 1100, 700);
RoundTrip(-20, 700, 1100, 700);
Console.WriteLine("All AI window centered-resize checks passed.");
