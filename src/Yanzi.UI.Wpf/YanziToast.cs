using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yanzi.UI.Wpf;

public enum YanziToastKind { Info, Success, Error, Warning, Loading }

/// <summary>Shared non-modal notification with status, description, optional action, and dismissal.</summary>
public static class YanziToast
{
    public static void Show(Window owner, string message,
        YanziToastKind kind = YanziToastKind.Info, TimeSpan? duration = null,
        string? description = null, string? actionLabel = null, Action? onAction = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.VerifyAccess();
        if (!owner.IsLoaded || !owner.IsVisible || string.IsNullOrWhiteSpace(message)) return;

        var border = new Border
        {
            MaxWidth = 365, MinWidth = 190, Padding = new Thickness(12),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.22 }
        };
        border.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        border.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");

        var container = new DockPanel { LastChildFill = true };
        var dismiss = YanziUi.WithStyle(new Button
            { Content = "×", Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0) },
            YanziUi.Styles.GhostButton);
        DockPanel.SetDock(dismiss, Dock.Right);
        container.Children.Add(dismiss);

        var icon = new TextBlock
        {
            Text = kind switch
            {
                YanziToastKind.Success => "✓", YanziToastKind.Error => "!",
                YanziToastKind.Warning => "!", YanziToastKind.Loading => "◌",
                _ => "i"
            },
            FontSize = 15, FontWeight = FontWeights.Bold, Width = 21
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty,
            kind == YanziToastKind.Error ? "Yanzi.Color.Destructive" : "Yanzi.Color.Foreground");
        DockPanel.SetDock(icon, Dock.Left);
        container.Children.Add(icon);

        var body = new StackPanel();
        var title = new TextBlock
        {
            Text = message, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        body.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var detail = new TextBlock
            {
                Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            body.Children.Add(detail);
        }
        container.Children.Add(body);
        border.Child = container;

        var popup = new Popup
        {
            PlacementTarget = owner, Placement = PlacementMode.Relative,
            HorizontalOffset = Math.Max(12, owner.ActualWidth - 390),
            VerticalOffset = Math.Max(12, owner.ActualHeight - 105),
            AllowsTransparency = true, StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = border, IsOpen = true
        };
        var timer = new DispatcherTimer(DispatcherPriority.Normal, owner.Dispatcher)
            { Interval = duration ?? TimeSpan.FromSeconds(kind == YanziToastKind.Loading ? 10 : 4) };
        void Close(object? sender, EventArgs args)
        {
            timer.Stop();
            popup.IsOpen = false;
            owner.Closed -= Close;
        }
        dismiss.Click += Close;
        owner.Closed += Close;
        timer.Tick += Close;
        if (!string.IsNullOrWhiteSpace(actionLabel) && onAction is not null)
        {
            var action = YanziUi.WithStyle(new Button
            {
                Content = actionLabel, Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            }, YanziUi.Styles.OutlineButton);
            action.Click += (_, _) => { Close(null, EventArgs.Empty); onAction(); };
            body.Children.Add(action);
        }
        timer.Start();
    }
}
