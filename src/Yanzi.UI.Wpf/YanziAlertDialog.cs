using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Shadcn-style owner-modal alert, with a dimming overlay and borderless rounded card.
/// Separate WPF Window provides modal focus and Escape/Enter, without OS toolwindow chrome.
/// </summary>
public sealed class YanziAlertDialog : Window
{
    public Border Card { get; }
    public Grid Overlay { get; }
    public Button CancelButton { get; }
    public Button ConfirmButton { get; }

    public YanziAlertDialog(Window owner, string title, string description,
        string confirmLabel = "确定", bool dangerous = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        owner.VerifyAccess();

        Owner = owner;
        Title = title;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Max(420, owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width);
        Height = Math.Max(320, owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
        YanziUi.ApplyTo(this, YanziUi.GetTheme(owner));

        Overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) };
        var panel = new StackPanel();
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 9)
        };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        panel.Children.Add(heading);

        var message = new TextBlock
        {
            Text = description,
            FontSize = 14,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 23)
        };
        message.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        message.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        panel.Children.Add(message);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        CancelButton = YanziUi.WithStyle(new Button
        {
            Content = "取消",
            Height = 34, MinWidth = 70, IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0)
        }, YanziUi.Styles.OutlineButton);
        ConfirmButton = YanziUi.WithStyle(new Button
        {
            Content = confirmLabel,
            Height = 34, MinWidth = 70, IsDefault = true
        }, dangerous ? YanziUi.Styles.DestructiveButton : YanziUi.Styles.DefaultButton);
        CancelButton.Click += (_, _) => DialogResult = false;
        ConfirmButton.Click += (_, _) => DialogResult = true;
        footer.Children.Add(CancelButton);
        footer.Children.Add(ConfirmButton);
        panel.Children.Add(footer);

        Card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24),
            Width = 440,
            MaxWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, ShadowDepth = 8, Opacity = 0.30
            },
            Child = panel
        };
        Card.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        Card.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        Overlay.Children.Add(Card);
        Content = Overlay;

        Loaded += (_, _) => CancelButton.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }
}
