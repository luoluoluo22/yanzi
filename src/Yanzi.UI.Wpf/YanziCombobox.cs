using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Searchable shadcn-style single-select combobox with custom borderless input,
/// filtered WPF list popup, empty state and keyboard selection.
/// Does NOT instantiate a Windows ComboBox nor install global keyboard hooks.
/// </summary>
public sealed class YanziCombobox : UserControl
{
    private readonly List<string> _options = [];
    private readonly TextBox _input;
    private readonly Popup _popup;
    private readonly ListBox _results;
    private readonly TextBlock _empty;
    private readonly Border _surface;
    private string? _selectedValue;
    private bool _committing;

    public string? SelectedValue => _selectedValue;
    public int FilteredCount => _results.Items.Count;
    public bool IsOpen { get => _popup.IsOpen; set => _popup.IsOpen = value; }
    public IReadOnlyList<string> Items => _options;
    public event EventHandler<string?>? SelectionChanged;

    public string SearchText
    {
        get => _input.Text;
        set => _input.Text = value ?? "";
    }

    public YanziCombobox(string placeholder = "Select a framework")
    {
        Width = 288;
        Height = 38;
        HorizontalAlignment = HorizontalAlignment.Left;
        _input = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(11, 7, 4, 7),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14,
            MinWidth = 0
        };
        _input.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.Foreground");
        _input.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Yanzi.Color.Foreground");
        _input.SetResourceReference(Control.FontFamilyProperty, "Yanzi.Font.Geist");
        AutomationProperties.SetName(_input, "Combobox search");
        _input.ToolTip = placeholder;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.Children.Add(_input);
        var hint = new TextBlock
        {
            Text = placeholder,
            Margin = new Thickness(13, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            FontSize = 13
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        hint.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        hint.Visibility = string.IsNullOrEmpty(_input.Text) ? Visibility.Visible : Visibility.Collapsed;
        _input.TextChanged += (_, _) =>
            hint.Visibility = string.IsNullOrEmpty(_input.Text) ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(hint);
        var arrow = YanziIcons.ChevronUp(14);
        arrow.RenderTransformOrigin = new Point(.5, .5);
        arrow.RenderTransform = new RotateTransform(180);
        var openButton = new Button
        {
            Content = arrow, Width = 28, Height = 30, Padding = new Thickness(0),
            Focusable = false, ToolTip = "Show options"
        };
        YanziUi.WithStyle(openButton, YanziUi.Styles.GhostButton);
        AutomationProperties.SetName(openButton, "Open combobox options");
        openButton.Click += (_, _) => { _input.Focus(); Open(); };
        Grid.SetColumn(openButton, 1);
        grid.Children.Add(openButton);

        _surface = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = grid
        };
        _surface.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Input");
        _surface.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        Content = _surface;

        _results = new ListBox
        {
            MaxHeight = 220,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4),
            FontSize = 13
        };
        _results.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Popover");
        _results.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.PopoverForeground");
        _results.SetResourceReference(Control.FontFamilyProperty, "Yanzi.Font.Geist");
        AutomationProperties.SetName(_results, "Combobox results");
        _empty = new TextBlock
        {
            Text = "No items found.",
            Padding = new Thickness(11, 9, 11, 9),
            TextAlignment = TextAlignment.Center,
            Visibility = Visibility.Collapsed,
            FontSize = 12
        };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        var panel = new StackPanel();
        panel.Children.Add(_results);
        panel.Children.Add(_empty);
        var popupFrame = new Border
        {
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        // Keep the option surface aligned with the rendered trigger (no fixed 285px width).
        popupFrame.SetBinding(FrameworkElement.WidthProperty,
            new Binding(nameof(ActualWidth)) { Source = this });
        popupFrame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Popover");
        popupFrame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            AllowsTransparency = true,
            StaysOpen = false,
            Child = popupFrame
        };
        _input.GotKeyboardFocus += (_, _) => { if (IsEnabled) Open(); };
        _input.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (IsEnabled && _input.IsKeyboardFocused && !_popup.IsOpen) Open();
        };
        _input.TextChanged += (_, _) =>
        {
            if (_committing) return;
            if (_selectedValue is not null && _input.Text != _selectedValue)
            {
                _selectedValue = null;
                SelectionChanged?.Invoke(this, null);
            }
            Refresh();
            if (IsEnabled && _input.IsKeyboardFocused) Open();
        };
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down)
            {
                Open();
                if (_results.Items.Count > 0)
                {
                    _results.SelectedIndex = 0;
                    _results.Focus();
                    var row = _results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    row?.Focus();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _popup.IsOpen && _results.Items.Count > 0)
            {
                Select(_results.SelectedItem?.ToString() ?? _results.Items[0]!.ToString()!);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                IsOpen = false;
                e.Handled = true;
            }
        };
        _results.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _results.SelectedItem is string selected)
            {
                Select(selected);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                IsOpen = false;
                _input.Focus();
                e.Handled = true;
            }
        };
        _results.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_results.SelectedItem is string selected)
            {
                Select(selected);
                e.Handled = true;
            }
        };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) IsOpen = false; };
        Unloaded += (_, _) => IsOpen = false;
    }

    public void Add(string option)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(option);
        if (_options.Contains(option, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Combobox option already exists.", nameof(option));
        _options.Add(option);
        Refresh();
    }

    public bool Select(string value)
    {
        if (!IsEnabled || !_options.Contains(value, StringComparer.Ordinal))
            return false;
        _selectedValue = value;
        _committing = true;
        try
        {
            _input.Text = value;
            _input.CaretIndex = _input.Text.Length;
        }
        finally { _committing = false; }
        Refresh();
        IsOpen = false;
        _input.Focus();
        SelectionChanged?.Invoke(this, value);
        return true;
    }

    public void Clear()
    {
        _selectedValue = null;
        _input.Clear();
        Refresh();
        SelectionChanged?.Invoke(this, null);
    }

    private void Open()
    {
        if (!IsEnabled) return;
        Refresh();
        IsOpen = true;
    }

    private void Refresh()
    {
        var query = _input.Text.Trim();
        var matches = _options.Where(x =>
            x.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _results.ItemsSource = matches;
        _empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _results.Visibility = matches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
