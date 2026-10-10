using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Exclusive selection group for custom YanziRadio controls. Supports programmatic
/// SelectedValue binding, clickable labels, roving Tab stop and arrow navigation.
/// </summary>
public sealed class YanziRadioGroup : StackPanel
{
    private readonly List<YanziRadio> _items = [];
    private bool _synchronizing;
    public IReadOnlyList<YanziRadio> Items => _items;

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(string),
            typeof(YanziRadioGroup), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedValueChanged));

    public string? SelectedValue
    {
        get => (string?)GetValue(SelectedValueProperty);
        set => SetCurrentValue(SelectedValueProperty, value);
    }

    public event EventHandler<string?>? SelectionChanged;

    public YanziRadioGroup()
    {
        Orientation = Orientation.Vertical;
        Focusable = false;
    }

    public YanziRadio Add(string label, string value, bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (_items.Any(x => x.Value == value))
            throw new ArgumentException("Radio group values must be unique.", nameof(value));

        var entry = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 13, 8),
            IsEnabled = isEnabled,
            Cursor = Cursors.Hand
        };
        var radio = new YanziRadio
        {
            Value = value,
            Group = this,
            IsTabStop = _items.Count == 0,
            Margin = new Thickness(0, 0, 6, 0)
        };
        AutomationProperties.SetName(radio, label);
        entry.Children.Add(radio);
        var description = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            IsHitTestVisible = false
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        entry.Children.Add(description);
        entry.MouseLeftButtonDown += (_, e) =>
        {
            if (!entry.IsEnabled || e.Handled) return;
            radio.Focus();
            radio.Select();
            e.Handled = true;
        };
        _items.Add(radio);
        Children.Add(entry);

        if (string.Equals(SelectedValue, value, StringComparison.Ordinal))
            ApplySelection();
        else
            UpdateTabStops();
        return radio;
    }

    public bool Select(string value)
    {
        var requested = _items.FirstOrDefault(x => x.Value == value);
        if (requested is null || !requested.IsEnabled || !((FrameworkElement)requested.Parent).IsEnabled)
            return false;
        SelectedValue = value;
        ApplySelection();
        return true;
    }

    internal void RadioChecked(YanziRadio radio)
    {
        if (_synchronizing) return;
        // A disabled item cannot force the group into an inconsistent state.
        if (!Select(radio.Value))
            ApplySelection();
    }

    private static void OnSelectedValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var group = (YanziRadioGroup)sender;
        group.ApplySelection();
        group.SelectionChanged?.Invoke(group, (string?)args.NewValue);
    }

    private void ApplySelection()
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            foreach (var item in _items)
                item.IsChecked = item.Value == SelectedValue && item.IsEnabled;
            UpdateTabStops();
        }
        finally { _synchronizing = false; }
    }

    private void UpdateTabStops()
    {
        var selected = _items.FirstOrDefault(x => x.IsChecked && x.IsEnabled);
        var focusable = selected ?? _items.FirstOrDefault(x => x.IsEnabled);
        foreach (var item in _items)
            item.IsTabStop = ReferenceEquals(item, focusable);
    }

    internal bool Navigate(YanziRadio current, int direction)
    {
        if (!IsEnabled || _items.Count == 0) return false;
        var enabled = _items.Where(x => x.IsEnabled && ((FrameworkElement)x.Parent).IsEnabled).ToArray();
        if (enabled.Length == 0) return false;
        var idx = Array.IndexOf(enabled, current);
        var next = direction switch
        {
            -2 => 0,
            2 => enabled.Length - 1,
            _ => (idx + direction + enabled.Length) % enabled.Length
        };
        var radio = enabled[next];
        if (!Select(radio.Value)) return false;
        radio.Focus();
        return true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new YanziRadioGroupAutomationPeer(this);
}

internal sealed class YanziRadioGroupAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    public YanziRadioGroupAutomationPeer(YanziRadioGroup owner) : base(owner) { }
    private YanziRadioGroup Group => (YanziRadioGroup)Owner;
    protected override string GetClassNameCore() => nameof(YanziRadioGroup);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);
    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => false;
    public IRawElementProviderSimple[] GetSelection() =>
        Group.Items.Where(x => x.IsChecked)
            .Select(x => ProviderFromPeer(UIElementAutomationPeer.CreatePeerForElement(x))!)
            .ToArray();
}
