using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Native Select using a custom WPF Popup rather than the operating system's
/// ComboBox popup. Separates choice selection from searchable Combobox semantics.
/// </summary>
public sealed class YanziSelect : UserControl
{
    private readonly List<string> _options = [];
    private readonly Button _trigger;
    private readonly TextBlock _value;
    private readonly StackPanel _choices;
    private readonly Popup _popup;
    private readonly List<Button> _optionButtons = [];
    private string? _selected;

    public string? SelectedValue => _selected;
    public IReadOnlyList<string> Options => _options;
    public bool IsOpen => _popup.IsOpen;
    public event EventHandler<string?>? SelectionChanged;

    public YanziSelect(string placeholder = "Select an option")
    {
        Width = 216;
        Height = 34;
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        _value = new TextBlock
        {
            Text = placeholder, FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _value.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        content.Children.Add(_value);
        var chevron = YanziIcons.ChevronUp(14);
        chevron.RenderTransformOrigin = new Point(.5, .5);
        chevron.RenderTransform = new RotateTransform(180);
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        _trigger = YanziUi.WithStyle(new Button
        {
            Content = content,
            MinWidth = 0,
            Padding = new Thickness(10, 4, 8, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        }, YanziUi.Styles.OutlineButton);
        AutomationProperties.SetName(_trigger, "Select option");
        Content = _trigger;

        _choices = new StackPanel { Margin = new Thickness(4) };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), Padding = new Thickness(2),
            Child = new ScrollViewer
            {
                Content = _choices,
                MaxHeight = 250,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        frame.SetBinding(FrameworkElement.WidthProperty,
            new Binding(nameof(ActualWidth)) { Source = this });

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = frame
        };
        _popup.Opened += (_, _) =>
        {
            if (_optionButtons.Count > 0)
                _optionButtons[Math.Max(0, _options.IndexOf(_selected ?? ""))].Focus();
        };
        _trigger.Click += (_, _) => { if (IsEnabled) Toggle(); };
        _trigger.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter or Key.Down)
            {
                Open();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && IsOpen)
            {
                Close();
                e.Handled = true;
            }
        };
        _popup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                _trigger.Focus();
                e.Handled = true;
            }
            else if (e.Key is Key.Down or Key.Up or Key.Home or Key.End)
            {
                if (_optionButtons.Count == 0) return;
                int current = _optionButtons.FindIndex(x => x.IsKeyboardFocused);
                int next = e.Key switch
                {
                    Key.Home => 0,
                    Key.End => _optionButtons.Count - 1,
                    Key.Down => (current + 1) % _optionButtons.Count,
                    _ => (current < 0 ? 0 : current + _optionButtons.Count - 1) % _optionButtons.Count
                };
                _optionButtons[next].Focus();
                e.Handled = true;
            }
        };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) Close(); };
        Unloaded += (_, _) => Close();
    }

    public void Add(string option)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(option);
        if (_options.Contains(option, StringComparer.Ordinal))
            throw new ArgumentException("Duplicate Select option", nameof(option));
        _options.Add(option);
        var text = option;
        var row = YanziUi.WithStyle(new Button
        {
            Content = option, Height = 30, Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1)
        }, YanziUi.Styles.DropdownAction);
        AutomationProperties.SetName(row, "Select " + option);
        row.Click += (_, _) => Select(text);
        _choices.Children.Add(row);
        _optionButtons.Add(row);
    }

    public bool Select(string value)
    {
        if (!_options.Contains(value, StringComparer.Ordinal) || !IsEnabled) return false;
        _selected = value;
        _value.Text = value;
        _value.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        Close();
        _trigger.Focus();
        SelectionChanged?.Invoke(this, value);
        return true;
    }

    public void Open()
    {
        if (!IsEnabled) return;
        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;
    private void Toggle() { if (IsOpen) Close(); else Open(); }
}
