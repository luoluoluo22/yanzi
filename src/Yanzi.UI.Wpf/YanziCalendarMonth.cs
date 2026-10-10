using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Self-drawn calendar month corresponding to the compact shadcn single-date example.
/// Month navigation, adjacent-month display, selection and high contrast checked date.
/// The legacy WPF Calendar style is kept for backward compatibility.
/// </summary>
public sealed class YanziCalendarMonth : UserControl
{
    private readonly TextBlock _caption;
    private readonly UniformGrid _dates;
    private DateTime _shown;
    private DateTime? _selected;

    public DateTime DisplayedMonth => _shown;
    public DateTime? SelectedDate => _selected;
    public int VisibleWeeks => _dates.Rows;
    public int VisibleDayCount => _dates.Children.Count;
    public event EventHandler<DateTime>? DateSelected;

    public YanziCalendarMonth(DateTime? month = null)
    {
        _shown = new DateTime((month ?? DateTime.Today).Year, (month ?? DateTime.Today).Month, 1);
        Width = 240;
        var outer = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1) };
        outer.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        outer.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");

        var layout = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        var prev = NavButton("‹", () => Navigate(-1));
        var next = NavButton("›", () => Navigate(1));
        Grid.SetColumn(next, 2);
        _caption = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _caption.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        Grid.SetColumn(_caption, 1);
        header.Children.Add(prev);
        header.Children.Add(_caption);
        header.Children.Add(next);
        layout.Children.Add(header);
        var weekday = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var name in new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" })
        {
            var label = new TextBlock { Text = name, FontSize = 12,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 7) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            weekday.Children.Add(label);
        }
        layout.Children.Add(weekday);
        _dates = new UniformGrid { Columns = 7, Rows = 5 };
        layout.Children.Add(_dates);
        outer.Child = layout;
        Content = outer;
        DrawMonth();
    }

    private Button NavButton(string symbol, Action action)
    {
        var button = YanziUi.WithStyle(new Button { Content = symbol,
            Width = 27, Height = 27, FontSize = 20, Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center }, YanziUi.Styles.GhostButton);
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, symbol == "‹" ? "Previous month" : "Next month");
        return button;
    }

    public void Navigate(int monthDelta)
    {
        _shown = _shown.AddMonths(monthDelta);
        DrawMonth();
    }

    public void SelectDate(DateTime selected)
    {
        _selected = selected.Date;
        _shown = new DateTime(selected.Year, selected.Month, 1);
        DrawMonth();
        DateSelected?.Invoke(this, selected.Date);
    }

    private void DrawMonth()
    {
        _caption.Text = _shown.ToString("M月  yyyy", CultureInfo.GetCultureInfo("zh-CN"));
        _dates.Children.Clear();
        int shift = (int)_shown.DayOfWeek;
        DateTime first = _shown.AddDays(-shift);
        int weeks = (int)Math.Ceiling((shift + DateTime.DaysInMonth(_shown.Year, _shown.Month)) / 7d);
        _dates.Rows = weeks;
        for (int i = 0; i < weeks * 7; i++)
        {
            var day = first.AddDays(i).Date;
            bool inMonth = day.Month == _shown.Month;
            bool selected = _selected?.Date == day;
            bool today = DateTime.Today == day;
            var button = new Button
            {
                Content = day.Day.ToString(CultureInfo.InvariantCulture),
                Height = 28, MinWidth = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 2, 1, 2),
                FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            YanziUi.WithStyle(button, selected
                ? YanziUi.Styles.DefaultButton
                : YanziUi.Styles.GhostButton);
            if (!inMonth)
                button.Opacity = .38;
            if (today && !selected)
            {
                button.BorderThickness = new Thickness(1);
                button.SetResourceReference(Control.BorderBrushProperty, "Yanzi.Color.Border");
            }
            AutomationProperties.SetName(button, day.ToString("yyyy-MM-dd"));
            button.Click += (_, _) => SelectDate(day);
            _dates.Children.Add(button);
        }
    }
}
