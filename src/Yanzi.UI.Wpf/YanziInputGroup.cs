using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Editable input with optional leading and trailing descriptive content.</summary>
public sealed class YanziInputGroup : UserControl
{
    public TextBox Input { get; }

    public YanziInputGroup(string prefix = "", string suffix = "")
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var start = new TextBlock { Text = prefix, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 7, 0) };
        start.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        root.Children.Add(start);
        Input = YanziUi.WithStyle(new TextBox { MinWidth = 85 }, YanziUi.Styles.Input);
        Grid.SetColumn(Input, 1);
        root.Children.Add(Input);
        var end = new TextBlock { Text = suffix, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 9, 0) };
        end.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        Grid.SetColumn(end, 2);
        root.Children.Add(end);
        Content = root;
    }
}
