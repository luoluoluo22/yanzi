using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>Border-separated, keyboard-focusable native Accordion with single-open behavior.</summary>
public sealed class YanziAccordion : UserControl
{
    private readonly StackPanel _stack = new();
    private readonly List<(Button Trigger, Border Body, FrameworkElement Arrow)> _panels = [];
    public bool SingleOpen { get; set; } = true;
    public int SectionCount => _panels.Count;
    public int ExpandedIndex => _panels.FindIndex(x => x.Body.Visibility == Visibility.Visible);

    public YanziAccordion() { Content = _stack; }

    public void Add(string title, UIElement content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);
        int index = _panels.Count;
        var container = new StackPanel();
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new TextBlock { Text = title, FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        columns.Children.Add(heading);
        var arrow = YanziIcons.ChevronUp(15);
        arrow.Margin = new Thickness(8, 0, 3, 0);
        arrow.RenderTransformOrigin = new Point(.5, .5);
        arrow.RenderTransform = new RotateTransform(180);
        Grid.SetColumn(arrow, 1);
        columns.Children.Add(arrow);
        var trigger = YanziUi.WithStyle(new Button
        {
            Content = columns,
            Height = 46,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        }, YanziUi.Styles.GhostButton);
        AutomationProperties.SetName(trigger, title);
        var body = new Border
        {
            Child = content,
            Padding = new Thickness(0, 2, 0, 18),
            Visibility = Visibility.Collapsed
        };
        trigger.Click += (_, _) => SetExpanded(index, body.Visibility != Visibility.Visible);
        var separator = new Border { Height = 1 };
        separator.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
        container.Children.Add(trigger);
        container.Children.Add(body);
        container.Children.Add(separator);
        _panels.Add((trigger, body, arrow));
        _stack.Children.Add(container);
    }

    public void Expand(int index) => SetExpanded(index, true);

    private void SetExpanded(int index, bool expanded)
    {
        if (index < 0 || index >= _panels.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        for (int i = 0; i < _panels.Count; i++)
        {
            if (i != index && (!expanded || SingleOpen)) Close(i);
        }
        _panels[index].Body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _panels[index].Arrow.RenderTransform = new RotateTransform(expanded ? 0 : 180);
    }

    private void Close(int index)
    {
        _panels[index].Body.Visibility = Visibility.Collapsed;
        _panels[index].Arrow.RenderTransform = new RotateTransform(180);
    }
}
