namespace OpenQuickHost;

/// <summary>
/// Resizes the conversation panel symmetrically while compensating the independent
/// top scope-tabs popup so its screen X does not change during the transition.
/// </summary>
internal static class AiWindowResizeGeometry
{
    internal static (double Left, double PopupOffset) KeepCenterAndScopeTabs(
        double left, double oldWidth, double newWidth, double existingPopupOffset)
    {
        var halfWidthChange = (newWidth - oldWidth) / 2d;
        return (left - halfWidthChange, existingPopupOffset + halfWidthChange);
    }
}
