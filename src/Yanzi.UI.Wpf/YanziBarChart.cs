using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Yanzi.UI.Wpf;

public sealed record YanziBarPoint(string Label, double Value);

/// <summary>Simple native bar chart; useful for small data summaries, not a full plotting engine.</summary>
public sealed class YanziBarChart : UserControl
{
    private readonly UniformGrid _grid = new() { Rows = 1 };
    public YanziBarChart() { Height = 155; Content = _grid; }

    public void SetData(IEnumerable<YanziBarPoint> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var series = data.ToArray();
        if (series.Any(p => !double.IsFinite(p.Value) || p.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(data), "Values must be finite and non-negative.");
        _grid.Children.Clear();
        _grid.Columns = Math.Max(1, series.Length);
        var max = Math.Max(1, series.Length == 0 ? 1 : series.Max(p => p.Value));
        foreach (var point in series)
        {
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom };
            var bar = new Border
            {
                Width = 26, Height = Math.Max(3, point.Value / max * 116),
                CornerRadius = new CornerRadius(5, 5, 0, 0)
            };
            bar.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Primary");
            column.Children.Add(bar);
            var text = new TextBlock { Text = point.Label, Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            column.Children.Add(text);
            _grid.Children.Add(column);
        }
    }
}
