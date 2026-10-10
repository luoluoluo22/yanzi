using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yanzi.UI.Wpf;

public enum YanziToastKind { Info, Success, Error }

/// <summary>Non-modal toast: silent by default; callers choose when to notify.</summary>
public static class YanziToast
{
    public static void Show(Window owner, string message, YanziToastKind kind = YanziToastKind.Info, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.VerifyAccess();
        if (!owner.IsLoaded || !owner.IsVisible || string.IsNullOrWhiteSpace(message)) return;

        var border = new Border
        {
            MaxWidth = 320,
            MinWidth = 180,
            Padding = new Thickness(14, 10, 14, 10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.25 }
        };
        border.SetResourceReference(Border.BackgroundProperty, "Yanzi.Brush.Surface");
        border.SetResourceReference(Border.BorderBrushProperty, kind switch
        {
            YanziToastKind.Error => "Yanzi.Brush.Danger",
            YanziToastKind.Success => "Yanzi.Brush.Success",
            _ => "Yanzi.Brush.Border"
        });
        var label = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Brush.Text");
        border.Child = label;

        var popup = new Popup
        {
            PlacementTarget = owner,
            Placement = PlacementMode.Relative,
            HorizontalOffset = Math.Max(12, owner.ActualWidth - 340),
            VerticalOffset = Math.Max(12, owner.ActualHeight - 76),
            AllowsTransparency = true,
            StaysOpen = true,
            Child = border,
            IsOpen = true
        };
        var timer = new DispatcherTimer(DispatcherPriority.Normal, owner.Dispatcher)
        {
            Interval = duration ?? TimeSpan.FromSeconds(3)
        };
        void Close(object? sender, EventArgs args)
        {
            timer.Stop();
            popup.IsOpen = false;
            owner.Closed -= Close;
        }
        owner.Closed += Close;
        timer.Tick += Close;
        timer.Start();
    }
}
