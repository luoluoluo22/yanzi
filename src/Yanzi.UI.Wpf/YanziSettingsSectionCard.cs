using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Unified, reusable settings-section surface. Dividers are inserted only
/// BETWEEN rows; the final row ends at the rounded outer card boundary.
/// Rows may be existing live WPF controls with bindings and event handlers.
/// </summary>
public sealed class YanziSettingsSectionCard : ContentControl
{
    private readonly YanziCard _card = new() { BodyPadding = new Thickness(16, 12, 16, 4) };
    private readonly StackPanel _rows = new();

    public int RowCount { get; private set; }
    public int DividerCount { get; private set; }
    public YanziCard Card => _card;
    public StackPanel Rows => _rows;

    public YanziSettingsSectionCard(string title, string description)
    {
        var header = new StackPanel();
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        var hint = new TextBlock
        {
            Text = description, FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        header.Children.Add(heading);
        header.Children.Add(hint);
        _card.SetHeader(header);
        _card.SetBody(_rows);
        Content = _card;
    }

    public void AddRow(UIElement row, UIElement? dividerBefore = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (RowCount > 0 && dividerBefore is not null)
        {
            _rows.Children.Add(dividerBefore);
            DividerCount++;
        }
        _rows.Children.Add(row);
        RowCount++;
    }
}
