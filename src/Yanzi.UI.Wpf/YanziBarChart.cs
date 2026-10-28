using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Yanzi.UI.Wpf;

public sealed record YanziBarPoint(string Label, double Value);

/// <summary>
/// Small neutral bar-chart visualization for component previews and compact summaries.
/// Bars are scaled from data and use semantic theme colors; no business values are fetched.
/// </summary>
public sealed class YanziBarChart : UserControl
{
    private readonly UniformGrid _grid = new() { Rows = 1 };
    private YanziBarPoint[] _series = [];
    private int _selectedIndex;
    private double _maxBarHeight = 128;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = value; Render(); }
    }

    public double MaxBarHeight
    {
        get => _maxBarHeight;
        set
        {
            if (!double.IsFinite(value) || value < 32 || value > 500)
                throw new ArgumentOutOfRangeException(nameof(value));
            _maxBarHeight = value;
            Render();
        }
    }

    public int PointCount => _series.Length;

    public YanziBarChart()
    {
        Height = 176;
        Content = _grid;
    }

    public void SetData(IEnumerable<YanziBarPoint> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var series = data.ToArray();
        if (series.Any(p => !double.IsFinite(p.Value) || p.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(data), "Values must be finite and non-negative.");
        if (series.Any(p => string.IsNullOrWhiteSpace(p.Label)))
            throw new ArgumentException("Each bar needs a non-empty label.", nameof(data));
        _series = series;
        Render();
    }

    private void Render()
    {
        _grid.Children.Clear();
        _grid.Columns = Math.Max(1, _series.Length);
        var max = Math.Max(1, _series.Length == 0 ? 1 : _series.Max(p => p.Value));
        for (var index = 0; index < _series.Length; index++)
        {
            var point = _series[index];
            var column = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 2, 0)
            };
            var bar = new Border
            {
                Height = Math.Max(3, point.Value / max * _maxBarHeight),
                Width = 34,
                MaxWidth = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(7, 7, 3, 3),
                ToolTip = $"{point.Label}: {point.Value:0.##}"
            };
            bar.SetResourceReference(Border.BackgroundProperty,
                index == _selectedIndex ? "Yanzi.Color.Primary" : "Yanzi.Color.ChartBar");
            column.Children.Add(bar);

            var label = new TextBlock
            {
                Text = point.Label,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 9, 0, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            column.Children.Add(label);
            _grid.Children.Add(column);
        }
    }
}
