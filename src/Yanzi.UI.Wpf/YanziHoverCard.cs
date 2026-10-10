using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Yanzi.UI.Wpf;

/// <summary>Keyboard- and pointer-triggered hover information popup. No background web dependencies.</summary>
public static class YanziHoverCard
{
    public static Popup Attach(FrameworkElement anchor, UIElement content)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        var container = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14), MinWidth = 180, MaxWidth = 340 };
        container.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        container.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        container.Child = content;
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            AllowsTransparency = true, StaysOpen = true, Child = container, VerticalOffset = 1
        };
        var closeDelay = new DispatcherTimer(DispatcherPriority.Normal, anchor.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(175) };
        closeDelay.Tick += (_, _) => { closeDelay.Stop(); popup.IsOpen = false; };
        void Open(object? sender, RoutedEventArgs e) { closeDelay.Stop(); popup.IsOpen = true; }
        void Close(object? sender, RoutedEventArgs e) { closeDelay.Stop(); closeDelay.Start(); }

        anchor.MouseEnter += Open;
        anchor.MouseLeave += Close;
        container.MouseEnter += Open;
        container.MouseLeave += Close;
        anchor.GotKeyboardFocus += Open;
        anchor.LostKeyboardFocus += Close;
        anchor.Unloaded += (_, _) => { closeDelay.Stop(); popup.IsOpen = false; };
        return popup;
    }
}
