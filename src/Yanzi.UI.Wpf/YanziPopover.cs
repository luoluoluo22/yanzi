using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Yanzi.UI.Wpf;

/// <summary>Owner-anchored popup with light/dark semantic surface. Caller owns the returned popup lifecycle.</summary>
public static class YanziPopover
{
    public static Popup Attach(Button anchor, UIElement content)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        var frame = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), MinWidth = 190 };
        frame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        frame.Child = content;

        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            AllowsTransparency = true, StaysOpen = false, Child = frame,
            HorizontalOffset = 0, VerticalOffset = 5 };
        anchor.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        anchor.Unloaded += (_, _) => popup.IsOpen = false;
        return popup;
    }
}
