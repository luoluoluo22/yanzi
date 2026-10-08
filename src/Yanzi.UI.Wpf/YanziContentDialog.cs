using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Yanzi.UI.Wpf;

/// <summary>
/// General purpose owner-modal dialog with arbitrary content; intentionally separate from
/// the destructive / confirm-only Alert Dialog. Never modifies global application styles.
/// </summary>
public sealed class YanziContentDialog : Window
{
    public Grid Overlay { get; }
    public Border Card { get; }
    public ContentPresenter BodyPresenter { get; }
    public Button CloseButton { get; }
    public Button CancelButton { get; }
    public Button SaveButton { get; }
    private readonly IInputElement? _previousFocus;

    public YanziContentDialog(Window owner, string title, string description,
        UIElement body, string saveLabel = "Save changes")
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        owner.VerifyAccess();
        var focusBeforeDialog = Keyboard.FocusedElement;
        // Restore focus only to a descendant of the owner, never another app/window.
        _previousFocus = focusBeforeDialog is DependencyObject node &&
            ReferenceEquals(Window.GetWindow(node), owner)
            ? focusBeforeDialog : null;

        Owner = owner;
        Title = title;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Max(510, owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width);
        Height = Math.Max(410, owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
        YanziUi.ApplyTo(this, YanziUi.GetTheme(owner));

        Overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(165, 0, 0, 0))
        };
        var main = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        header.Children.Add(heading);

        CloseButton = YanziUi.WithStyle(new Button
        {
            Content = "×",
            FontSize = 19,
            FontWeight = FontWeights.Normal,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            ToolTip = "Close dialog",
            Focusable = true
        }, YanziUi.Styles.GhostButton);
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, "Close dialog");
        Grid.SetColumn(CloseButton, 1);
        header.Children.Add(CloseButton);
        CloseButton.Click += (_, _) => DialogResult = false;
        main.Children.Add(header);

        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 16)
        };
        descriptionText.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        descriptionText.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        main.Children.Add(descriptionText);

        BodyPresenter = new ContentPresenter { Content = body };
        var scroller = new ScrollViewer
        {
            Content = BodyPresenter,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 16)
        };
        main.Children.Add(scroller);

        var footer = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        CancelButton = YanziUi.WithStyle(new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 78,
            Height = 35,
            Margin = new Thickness(0, 0, 8, 0)
        }, YanziUi.Styles.OutlineButton);
        SaveButton = YanziUi.WithStyle(new Button
        {
            Content = saveLabel,
            MinWidth = 112,
            Height = 35,
            IsDefault = true
        }, YanziUi.Styles.DefaultButton);
        CancelButton.Click += (_, _) => DialogResult = false;
        SaveButton.Click += (_, _) => DialogResult = true;
        footer.Children.Add(CancelButton);
        footer.Children.Add(SaveButton);
        var footerBackground = new Border
        {
            Padding = new Thickness(16),
            Margin = new Thickness(-16, 0, -16, -16),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer
        };
        footerBackground.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Muted");
        footerBackground.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        main.Children.Add(footerBackground);

        Card = new Border
        {
            Width = 384,
            MaxWidth = 384,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black,
                BlurRadius = 26, ShadowDepth = 7, Opacity = .32 },
            Child = main
        };
        Card.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        Card.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        Overlay.Children.Add(Card);
        Content = Overlay;
        // WPF equivalent of a modal dialog focus trap: Tab and Shift+Tab wrap
        // inside the card, never reaching the disabled owner underneath.
        KeyboardNavigation.SetTabNavigation(Card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(Card, KeyboardNavigationMode.Cycle);
        Closed += (_, _) => owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_previousFocus is UIElement element && element.IsEnabled && element.IsVisible)
                element.Focus();
        });
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!TryFocusFirstInput(body))
                CloseButton.Focus();
        });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    private static bool TryFocusFirstInput(UIElement element)
    {
        if (element is TextBox tb && tb.IsEnabled) return tb.Focus();
        if (element is PasswordBox pwd && pwd.IsEnabled) return pwd.Focus();
        if (element is DatePicker date && date.IsEnabled) return date.Focus();
        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                if (TryFocusFirstInput(child)) return true;
        }
        if (element is ContentControl cc && cc.Content is UIElement nested)
            return TryFocusFirstInput(nested);
        if (element is Border border && border.Child is UIElement content)
            return TryFocusFirstInput(content);
        return false;
    }
}
