using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
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
    public ScrollViewer BodyScroller { get; }
    private readonly ScaleTransform _scale = new(.96, .96);
    private readonly SolidColorBrush _scrim = new(Color.FromArgb(0, 0, 0, 0));
    private readonly IInputElement? _previousFocus;
    private bool _closing;
    private bool _finishedClosing;
    private DispatcherTimer? _dismissTimer;
    public bool IsClosing => _closing;

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
        // A modal overlay must fit its owner and the current working area.
        Width = Math.Clamp(owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width,
            300, Math.Max(300, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height,
            320, Math.Max(320, SystemParameters.WorkArea.Height));
        YanziUi.ApplyTo(this, YanziUi.GetTheme(owner));

        Overlay = new Grid { Background = _scrim };
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
        CloseButton.Click += (_, _) => RequestClose(false);
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
        BodyScroller = new ScrollViewer
        {
            Content = BodyPresenter,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 16)
        };
        main.Children.Add(BodyScroller);

        var footer = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        CancelButton = YanziUi.WithStyle(new Button
        {
            Content = "Cancel",
            IsCancel = false, // Escape goes through animated RequestClose instead of auto-dismiss.
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
        CancelButton.Click += (_, _) => RequestClose(false);
        SaveButton.Click += (_, _) => RequestClose(true);
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
            Child = main,
            Opacity = 0,
            RenderTransform = _scale,
            RenderTransformOrigin = new Point(.5, .5)
        };
        Card.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        Card.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        Overlay.Children.Add(Card);
        Content = Overlay;
        Overlay.SizeChanged += (_, _) =>
        {
            Card.Width = Math.Min(384, Math.Max(230, Overlay.ActualWidth - 32));
            BodyScroller.MaxHeight = Math.Max(64, Math.Min(280, Overlay.ActualHeight - 230));
        };
        // WPF equivalent of a modal dialog focus trap: Tab and Shift+Tab wrap
        // inside the card, never reaching the disabled owner underneath.
        KeyboardNavigation.SetTabNavigation(Card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(Card, KeyboardNavigationMode.Cycle);
        Closed += (_, _) =>
        {
            _dismissTimer?.Stop();
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_previousFocus is UIElement element && element.IsEnabled && element.IsVisible)
                element.Focus();
            });
        };
        Loaded += (_, _) =>
        {
            AnimateEntrance();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!TryFocusFirstInput(body))
                    CloseButton.Focus();
            });
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                RequestClose(false);
                e.Handled = true;
            }
        };
    }

    private void AnimateEntrance()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(170));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd
        });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96, 1, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd
        });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.96, 1, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd
        });
        _scrim.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(165, 0, 0, 0), duration) { EasingFunction = easing });
    }

    /// <summary>Unified close route for Escape, X, Cancel and Save.</summary>
    public void RequestClose(bool accepted)
    {
        if (_closing) return;
        _closing = true;
        CloseButton.IsEnabled = CancelButton.IsEnabled = SaveButton.IsEnabled = false;
        if (!IsVisible || !IsLoaded)
        {
            Close();
            return;
        }
        // Animation completion callbacks are not guaranteed under all WPF render
        // schedules. Close on a dispatcher deadline as a safety net.
        _dismissTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(240)
        };
        _dismissTimer.Tick += (_, _) => FinishClose(accepted);
        _dismissTimer.Start();
        var fade = new DoubleAnimation
        {
            To = 0, Duration = TimeSpan.FromMilliseconds(115),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => FinishClose(accepted);
        Card.BeginAnimation(OpacityProperty, fade);
        _scrim.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = Color.FromArgb(0, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(115),
            FillBehavior = FillBehavior.Stop
        });
    }

    private void FinishClose(bool accepted)
    {
        if (_finishedClosing) return;
        _finishedClosing = true;
        _dismissTimer?.Stop();
        Card.Opacity = 0;
        if (IsVisible) DialogResult = accepted;
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
