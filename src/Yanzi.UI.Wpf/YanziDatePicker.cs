using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Shared shadcn-style Date Picker: outline trigger + themed Popup + YanziCalendarMonth.
/// Unlike WPF's stock DatePicker the dropdown is never rendered by the Windows calendar theme.
/// </summary>
public sealed class YanziDatePicker : UserControl
{
    private readonly Button _trigger;
    private readonly TextBlock _text;
    private readonly Popup _popup;
    private readonly YanziCalendarMonth _calendar;
    private bool _updating;

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(YanziDatePicker),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((YanziDatePicker)d).RefreshSelection()));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(YanziDatePicker),
            new PropertyMetadata("选择日期", (d, _) => ((YanziDatePicker)d).RefreshCaption()));

    public static readonly DependencyProperty MinimumDateProperty =
        DependencyProperty.Register(nameof(MinimumDate), typeof(DateTime?), typeof(YanziDatePicker),
            new PropertyMetadata(null, (d, _) => ((YanziDatePicker)d).SyncDateLimits()));

    public static readonly DependencyProperty MaximumDateProperty =
        DependencyProperty.Register(nameof(MaximumDate), typeof(DateTime?), typeof(YanziDatePicker),
            new PropertyMetadata(null, (d, _) => ((YanziDatePicker)d).SyncDateLimits()));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value?.Date);
    }

    public DateTime? MinimumDate
    {
        get => (DateTime?)GetValue(MinimumDateProperty);
        set => SetValue(MinimumDateProperty, value?.Date);
    }

    public DateTime? MaximumDate
    {
        get => (DateTime?)GetValue(MaximumDateProperty);
        set => SetValue(MaximumDateProperty, value?.Date);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => _popup.IsOpen;
        set => _popup.IsOpen = value && IsEnabled;
    }

    public event EventHandler<DateTime?>? SelectedDateChanged;

    public YanziDatePicker()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        MinWidth = 190;

        var face = new Grid();
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Native vector equivalent of the reference Calendar icon (no glyph/font dependency).
        var icon = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(
                "M7,2 L7,6 M17,2 L17,6 M3,10 L21,10 M5,4 L19,4 C20.1,4 21,4.9 21,6 L21,19 C21,20.1 20.1,21 19,21 L5,21 C3.9,21 3,20.1 3,19 L3,6 C3,4.9 3.9,4 5,4 Z"),
            Stretch = System.Windows.Media.Stretch.Uniform,
            StrokeThickness = 1.7,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "Yanzi.Color.MutedForeground");
        Grid.SetColumn(icon, 0);
        face.Children.Add(icon);

        _text = new TextBlock
        {
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0)
        };
        Grid.SetColumn(_text, 1);
        face.Children.Add(_text);

        var chevron = new TextBlock
        {
            Text = "⌄", FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        Grid.SetColumn(chevron, 2);
        face.Children.Add(chevron);

        _trigger = YanziUi.WithStyle(new Button
        {
            Content = face, MinHeight = 36, Padding = new Thickness(10, 5, 10, 5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        }, YanziUi.Styles.OutlineButton);
        AutomationProperties.SetName(_trigger, "选择日期");
        _trigger.Click += (_, _) => IsDropDownOpen = !IsDropDownOpen;

        _calendar = new YanziCalendarMonth();
        _calendar.DateSelected += (_, date) =>
        {
            SelectedDate = date;
            IsDropDownOpen = false;
            _trigger.Focus();
        };

        _popup = new Popup
        {
            PlacementTarget = _trigger,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 5,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = _calendar
        };
        _popup.Closed += (_, _) => _trigger.SetCurrentValue(Button.IsDefaultProperty, false);

        var root = new Grid();
        root.Children.Add(_trigger);
        root.Children.Add(_popup);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && IsDropDownOpen)
            {
                IsDropDownOpen = false;
                _trigger.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                IsDropDownOpen = true;
                e.Handled = true;
            }
        };
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled) IsDropDownOpen = false;
        };
        RefreshCaption();
    }

    private void RefreshCaption()
    {
        if (_text is null) return;
        _text.Text = SelectedDate?.ToString("yyyy年M月d日", CultureInfo.GetCultureInfo("zh-CN")) ?? Placeholder;
        _text.SetResourceReference(TextBlock.ForegroundProperty,
            SelectedDate.HasValue ? "Yanzi.Color.Foreground" : "Yanzi.Color.MutedForeground");
    }

    private void RefreshSelection()
    {
        RefreshCaption();
        if (_calendar is null || _updating) return;
        _updating = true;
        try
        {
            _calendar.SetSelectedDate(SelectedDate);
            SelectedDateChanged?.Invoke(this, SelectedDate);
        }
        finally { _updating = false; }
    }

    private void SyncDateLimits()
    {
        if (_calendar is null) return;
        _calendar.MinimumDate = MinimumDate;
        _calendar.MaximumDate = MaximumDate;
    }
}
