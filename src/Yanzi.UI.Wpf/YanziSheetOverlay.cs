using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yanzi.UI.Wpf;

public enum YanziSheetSide { Top, Right, Bottom, Left }

/// <summary>
/// Borderless overlay attached to a WPF owner with a four-edge sheet.
/// No native title bar; independent overlay window avoids modifying the owner visual tree.
/// </summary>
public sealed class YanziSheetOverlay : Window
{
    public YanziSheetSide Side { get; }
    public Grid Overlay { get; }
    public Border Panel { get; }
    public Button CloseButton { get; }
    public Border? DrawerHandle { get; }
    public FrameworkElement? DrawerGrip { get; private set; }
    public IReadOnlyList<double> DrawerSnapPoints { get; } = new[] { .40, .65, .90 };
    public double CurrentDrawerFraction { get; private set; } = .40;
    private bool _dragging;
    private double _dragStartY;
    private double _dragStartHeight;

    public YanziSheetOverlay(Window owner, string title, UIElement content,
        YanziSheetSide side = YanziSheetSide.Right)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        owner.VerifyAccess();
        Side = side;

        Owner = owner;
        Title = title;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Max(440, owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width);
        Height = Math.Max(400, owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
        YanziUi.ApplyTo(this, YanziUi.GetTheme(owner));
        Overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0))
        };
        // Click on the *overlay background*, not on sheet child controls, to dismiss.
        Overlay.MouseLeftButtonDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, Overlay))
            {
                Close();
                e.Handled = true;
            }
        };
        var layout = new DockPanel { Margin = new Thickness(24) };
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        heading.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        titleText.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        heading.Children.Add(titleText);
        CloseButton = YanziUi.WithStyle(new Button
        {
            Content = "×", Width = 29, Height = 29, Padding = new Thickness(0),
            ToolTip = "Close sheet", FontSize = 19
        }, YanziUi.Styles.GhostButton);
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, "Close sheet");
        CloseButton.Click += (_, _) => Close();
        Grid.SetColumn(CloseButton, 1);
        heading.Children.Add(CloseButton);
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);

        var scroller = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        layout.Children.Add(scroller);

        var panelContent = new Grid();
        if (side == YanziSheetSide.Bottom)
        {
            var handle = new Border
            {
                Width = 52, Height = 5, CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 11, 0, 0)
            };
            handle.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var grip = new Border
            {
                Height = 27,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNS,
                IsManipulationEnabled = true,
                Child = handle
            };
            System.Windows.Automation.AutomationProperties.SetName(grip, "Drawer drag handle");
            DrawerGrip = grip;
            grip.MouseLeftButtonDown += GripMouseDown;
            grip.MouseMove += GripMouseMove;
            grip.MouseLeftButtonUp += GripMouseUp;
            grip.LostMouseCapture += (_, _) => { _dragging = false; };
            grip.ManipulationStarting += (_, e) =>
            {
                e.ManipulationContainer = Overlay;
                e.Mode = ManipulationModes.Translate;
                e.Handled = true;
            };
            grip.ManipulationDelta += (_, e) =>
            {
                if (!_dragging)
                {
                    _dragging = true;
                    _dragStartHeight = Panel?.Height ?? 335;
                }
                ApplyDragHeight((Panel?.Height ?? 335) - e.DeltaManipulation.Translation.Y);
                e.Handled = true;
            };
            grip.ManipulationCompleted += (_, e) =>
            {
                _dragging = false;
                CompleteDrawerDrag(Panel?.Height ?? 335);
                e.Handled = true;
            };
            shell.Children.Add(grip);
            Grid.SetRow(layout, 1);
            shell.Children.Add(layout);
            panelContent.Children.Add(shell);
            DrawerHandle = handle;
        }
        else panelContent.Children.Add(layout);

        Panel = new Border
        {
            // Bottom drawers have rounded leading corners; sheets retain flat edges.
            CornerRadius = side == YanziSheetSide.Bottom
                ? new CornerRadius(16, 16, 0, 0) : new CornerRadius(0),
            BorderThickness = side switch
            {
                YanziSheetSide.Right => new Thickness(1, 0, 0, 0),
                YanziSheetSide.Left => new Thickness(0, 0, 1, 0),
                YanziSheetSide.Top => new Thickness(0, 0, 0, 1),
                _ => new Thickness(0, 1, 0, 0)
            },
            Child = panelContent
        };
        Panel.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Background");
        Panel.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        if (side is YanziSheetSide.Left or YanziSheetSide.Right)
        {
            Panel.Width = 390;
            Panel.HorizontalAlignment =
                side == YanziSheetSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Panel.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            Panel.Height = 335;
            Panel.HorizontalAlignment = HorizontalAlignment.Stretch;
            Panel.VerticalAlignment =
                side == YanziSheetSide.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }
        Overlay.Children.Add(Panel);
        Content = Overlay;

        Loaded += (_, _) =>
        {
            AnimateIn();
            CloseButton.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>Choose one of the configured snap fractions for a bottom drawer.</summary>
    public bool SnapDrawerTo(double fraction)
    {
        if (Side != YanziSheetSide.Bottom ||
            !DrawerSnapPoints.Any(x => Math.Abs(x - fraction) < .0001))
            return false;
        var availableHeight = Math.Max(400, Overlay.ActualHeight > 0 ? Overlay.ActualHeight : Height);
        CurrentDrawerFraction = fraction;
        Panel.Height = Math.Round(availableHeight * fraction);
        return true;
    }

    /// <summary>Complete mouse/touch drag: dismiss below threshold, otherwise settle to nearest snap point.</summary>
    public bool CompleteDrawerDrag(double proposedHeight)
    {
        if (Side != YanziSheetSide.Bottom || !double.IsFinite(proposedHeight))
            return false;
        var availableHeight = Math.Max(400, Overlay.ActualHeight > 0 ? Overlay.ActualHeight : Height);
        if (proposedHeight < availableHeight * .22)
        {
            Close();
            return true;
        }
        return SnapDrawerTo(DrawerSnapPoints.MinBy(x => Math.Abs(x * availableHeight - proposedHeight)));
    }

    private void GripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Side != YanziSheetSide.Bottom) return;
        _dragging = true;
        _dragStartY = e.GetPosition(Overlay).Y;
        _dragStartHeight = Panel?.Height ?? 335;
        if (Panel is not null) Panel.RenderTransform = Transform.Identity;
        DrawerGrip?.CaptureMouse();
        e.Handled = true;
    }

    private void GripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        ApplyDragHeight(_dragStartHeight + _dragStartY - e.GetPosition(Overlay).Y);
        e.Handled = true;
    }

    private void GripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        DrawerGrip?.ReleaseMouseCapture();
        CompleteDrawerDrag(Panel?.Height ?? 335);
        e.Handled = true;
    }

    private void ApplyDragHeight(double height)
    {
        var max = Math.Max(400, Overlay.ActualHeight > 0 ? Overlay.ActualHeight : Height) * .94;
        Panel.Height = Math.Clamp(height, 60, max);
    }

    private void AnimateIn()
    {
        var shift = Side switch
        {
            YanziSheetSide.Left => -390,
            YanziSheetSide.Right => 390,
            YanziSheetSide.Top => -335,
            _ => 335
        };
        var transform = new TranslateTransform();
        Panel.RenderTransform = transform;
        var axis = Side is YanziSheetSide.Left or YanziSheetSide.Right
            ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        transform.BeginAnimation(axis, new DoubleAnimation
        {
            From = shift,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }
}
