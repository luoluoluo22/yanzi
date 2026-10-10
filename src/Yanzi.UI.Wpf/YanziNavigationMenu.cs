using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Shared shadcn-style navigation menu, independent of native WPF Menu/MenuItem:
/// a compact trigger row, a wide two-column navigation viewport and a moving indicator.
/// </summary>
public sealed class YanziNavigationMenu : Border
{
    public sealed record Link(string Title, string Description, Action Navigate);
    private sealed record Group(string Label, IReadOnlyList<Link> Links, Button Trigger);

    private readonly StackPanel _triggers = new()
    {
        Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly List<Group> _groups = [];
    private readonly StackPanel _links = new();
    private readonly Border _contentBorder;
    private readonly Popup _popup;
    private readonly Polygon _indicator;
    private readonly Canvas _indicatorSpace = new() { Height = 7 };
    private int _activeGroup = -1;

    public int GroupCount => _groups.Count;
    public double ViewportMinWidth => _contentBorder.MinWidth;
    public IReadOnlyList<Link> GetLinks(int index)
    {
        if (index < 0 || index >= _groups.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _groups[index].Links;
    }
    public int ActiveGroupIndex => _popup.IsOpen ? _activeGroup : -1;
    public bool IsOpen => _popup.IsOpen;
    public int VisibleLinkCount =>
        ActiveGroupIndex < 0 ? 0 : _groups[_activeGroup].Links.Count;

    public YanziNavigationMenu()
    {
        BorderThickness = new Thickness(1);
        Padding = new Thickness(4, 4, 4, 0);
        CornerRadius = new CornerRadius(8);
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Card");
        SetResourceReference(BorderBrushProperty, "Yanzi.Color.Border");

        _indicator = new Polygon
        {
            Points = new PointCollection { new(0, 7), new(6, 0), new(12, 7) },
            Width = 12, Height = 7, Visibility = Visibility.Collapsed
        };
        _indicator.SetResourceReference(Shape.FillProperty, "Yanzi.Color.Border");
        _indicatorSpace.Children.Add(_indicator);

        _contentBorder = new Border
        {
            MinWidth = 455, MaxWidth = 565,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14)
        };
        _contentBorder.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        _contentBorder.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        _contentBorder.Child = _links;

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 0,
            VerticalOffset = 3,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = _contentBorder
        };
        _popup.Closed += (_, _) =>
        {
            _activeGroup = -1;
            _indicator.Visibility = Visibility.Collapsed;
            UpdateTriggers();
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_triggers, 0);
        body.Children.Add(_triggers);
        Grid.SetRow(_indicatorSpace, 1);
        body.Children.Add(_indicatorSpace);
        body.Children.Add(_popup);
        Child = body;

        Unloaded += (_, _) => Close();
        PreviewKeyDown += HandleNavigation;
    }

    public Button AddGroup(string title, IEnumerable<Link> links)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(links);
        var items = links.ToArray();
        if (items.Length == 0) throw new ArgumentException("Each navigation group must contain a link.", nameof(links));
        if (items.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Navigate is null))
            throw new ArgumentException("Navigation links need titles and callbacks.", nameof(links));
        var index = _groups.Count;

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var chevron = YanziIcons.ChevronUp(12);
        chevron.RenderTransform = new RotateTransform(180);
        chevron.Margin = new Thickness(6, 0, 0, 0);
        label.Children.Add(chevron);
        var trigger = YanziUi.WithStyle(new Button
        {
            Content = label, MinHeight = 32, Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(1, 0, 1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        }, YanziUi.Styles.GhostButton);
        AutomationProperties.SetName(trigger, title);
        trigger.Click += (_, _) =>
        {
            if (_popup.IsOpen && _activeGroup == index) Close();
            else ShowGroup(index);
        };
        trigger.MouseEnter += (_, _) =>
        {
            if (_popup.IsOpen && _activeGroup != index) ShowGroup(index);
        };
        _groups.Add(new Group(title, items, trigger));
        _triggers.Children.Add(trigger);
        return trigger;
    }

    public void ShowGroup(int index)
    {
        if (index < 0 || index >= _groups.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _activeGroup = index;
        var group = _groups[index];
        _links.Children.Clear();
        var heading = new TextBlock
        {
            Text = group.Label, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 2, 6, 12)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        _links.Children.Add(heading);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < group.Links.Count; i++)
        {
            var item = group.Links[i];
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var card = YanziUi.WithStyle(new Button
            {
                Padding = new Thickness(12, 12, 10, 12),
                Margin = new Thickness(3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinHeight = 66
            }, YanziUi.Styles.GhostButton);
            var content = new StackPanel();
            var name = new TextBlock
            {
                Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
            content.Children.Add(name);
            var description = new TextBlock
            {
                Text = item.Description, TextWrapping = TextWrapping.Wrap,
                FontSize = 11, MaxHeight = 37, Margin = new Thickness(0, 5, 0, 0)
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            content.Children.Add(description);
            card.Content = content;
            AutomationProperties.SetName(card, item.Title);
            card.Click += (_, _) =>
            {
                Close();
                item.Navigate();
                group.Trigger.Focus();
            };
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            grid.Children.Add(card);
        }
        _links.Children.Add(grid);
        UpdateTriggers();
        _popup.IsOpen = true;
        _indicator.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(UpdateIndicator),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void Close()
    {
        _popup.IsOpen = false;
        _indicator.Visibility = Visibility.Collapsed;
    }

    private void UpdateTriggers()
    {
        for (var i = 0; i < _groups.Count; i++)
        {
            _groups[i].Trigger.SetResourceReference(Control.BackgroundProperty,
                _popup.IsOpen && _activeGroup == i ? "Yanzi.Color.Accent" : "Yanzi.Color.Card");
        }
    }

    private void UpdateIndicator()
    {
        if (_activeGroup < 0 || !_popup.IsOpen || _activeGroup >= _groups.Count) return;
        var trigger = _groups[_activeGroup].Trigger;
        var local = trigger.TranslatePoint(new Point(trigger.ActualWidth / 2, 0), _indicatorSpace);
        Canvas.SetLeft(_indicator, Math.Max(0, local.X - 6));
        Canvas.SetTop(_indicator, 0);
    }

    private void HandleNavigation(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _popup.IsOpen)
        {
            var index = _activeGroup;
            Close();
            if (index >= 0) _groups[index].Trigger.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Left or Key.Right) || _groups.Count < 2) return;
        var current = _groups.FindIndex(g => g.Trigger.IsKeyboardFocusWithin);
        if (current < 0) return;
        var next = (current + (e.Key == Key.Right ? 1 : _groups.Count - 1)) % _groups.Count;
        _groups[next].Trigger.Focus();
        if (_popup.IsOpen) ShowGroup(next);
        e.Handled = true;
    }
}
