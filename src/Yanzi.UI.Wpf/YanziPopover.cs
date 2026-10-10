using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

public enum YanziPopoverAlign { Start, Center, End }

/// <summary>
/// Shared focusable rich-content popover. Supports horizontal alignment,
/// escape dismissal, owner unload and native screen-edge fallback.
/// </summary>
public static class YanziPopover
{
    public static Popup Attach(Button anchor, UIElement content,
        YanziPopoverAlign align = YanziPopoverAlign.Start)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        var frame = new Border
        {
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), MinWidth = 190
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        frame.Child = content;

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Custom,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = frame,
            CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            {
                var x = align switch
                {
                    YanziPopoverAlign.Center => (targetSize.Width - popupSize.Width) / 2,
                    YanziPopoverAlign.End => targetSize.Width - popupSize.Width,
                    _ => 0d
                };
                // WPF selects the first candidate that fits the monitor and
                // falls back above the trigger near the bottom edge.
                return [
                    new CustomPopupPlacement(new Point(x, targetSize.Height + 5), PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(new Point(x, -popupSize.Height - 5), PopupPrimaryAxis.Horizontal)
                ];
            }
        };
        anchor.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        anchor.Unloaded += (_, _) => popup.IsOpen = false;
        frame.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        return popup;
    }
}
