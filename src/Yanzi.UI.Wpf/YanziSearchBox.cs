using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>
/// A native WPF search input with an accessible TextBox, unobtrusive placeholder,
/// and the Lucide Search icon geometry (24x24 circle + diagonal handle).
/// No installed icon font, emoji, web view, or external runtime is required.
/// </summary>
public sealed class YanziSearchBox : UserControl
{
    private readonly TextBlock _placeholder;

    public TextBox Input { get; }

    public string Text
    {
        get => Input.Text;
        set => Input.Text = value ?? string.Empty;
    }

    public string Placeholder
    {
        get => _placeholder.Text;
        set => _placeholder.Text = value ?? string.Empty;
    }

    public event TextChangedEventHandler? TextChanged;

    public YanziSearchBox(string placeholder = "Search")
    {
        MinHeight = 38;
        var grid = new Grid { Height = 38 };
        Input = YanziUi.WithStyle(new TextBox
        {
            Height = 38,
            Padding = new Thickness(12, 7, 36, 7),
            FontSize = 13,
            Text = "",
            ToolTip = "Search"
        }, YanziUi.Styles.InputSoft);
        System.Windows.Automation.AutomationProperties.SetName(Input, "搜索");
        grid.Children.Add(Input);

        _placeholder = new TextBlock
        {
            Text = placeholder,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            // Align to the native TextBox caret start (17 DIP), not the border inset (14 DIP).
            // Measured with GetRectFromCharacterIndex(0, trailingEdge: false).
            Margin = new Thickness(17, 0, 37, 0),
            IsHitTestVisible = false
        };
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        grid.Children.Add(_placeholder);

        // Lucide "Search": circle cx=11 cy=11 r=8, path m21 21-4.35-4.35.
        // Viewbox scales the actual vector geometry down to 18 DIP without
        // altering the stroke or introducing font-substitution differences.
        var icon = new Viewbox
        {
            Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 13, 0),
            IsHitTestVisible = false
        };
        var artwork = new Canvas { Width = 24, Height = 24 };
        var lens = new Ellipse
        {
            Width = 16, Height = 16,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };
        lens.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.MutedForeground");
        Canvas.SetLeft(lens, 3);
        Canvas.SetTop(lens, 3);
        artwork.Children.Add(lens);
        var handle = new Line
        {
            X1 = 16.65, Y1 = 16.65,
            X2 = 21, Y2 = 21,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        handle.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.MutedForeground");
        artwork.Children.Add(handle);
        icon.Child = artwork;
        grid.Children.Add(icon);

        Input.TextChanged += (_, e) =>
        {
            UpdatePlaceholder();
            TextChanged?.Invoke(this, e);
        };
        Content = grid;
        UpdatePlaceholder();
    }

    public bool FocusInput() => Input.Focus();

    private void UpdatePlaceholder() =>
        _placeholder.Visibility = string.IsNullOrEmpty(Input.Text) ? Visibility.Visible : Visibility.Collapsed;
}
