using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Fully drawn radio item: no System.Windows.Controls.RadioButton is instantiated.
/// Visual state comes from the Yanzi.Radio.Custom control template.
/// </summary>
public sealed class YanziRadio : Control
{
    internal YanziRadioGroup? Group { get; set; }

    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked), typeof(bool), typeof(YanziRadio),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, CheckedChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(YanziRadio), new PropertyMetadata(string.Empty));

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetCurrentValue(IsCheckedProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public event RoutedEventHandler? Checked;

    public YanziRadio()
    {
        Focusable = true;
        IsTabStop = true;
        Width = 24;
        Height = 24;
        Cursor = Cursors.Hand;
        SetResourceReference(StyleProperty, YanziUi.Styles.RadioCustom);
    }

    public bool Select()
    {
        if (!IsEnabled) return false;
        if (Group is not null) return Group.Select(Value);
        IsChecked = true;
        return true;
    }

    private static void CheckedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var radio = (YanziRadio)sender;
        if ((bool)args.NewValue)
        {
            radio.Group?.RadioChecked(radio);
            radio.Checked?.Invoke(radio, new RoutedEventArgs());
        }
        if (UIElementAutomationPeer.FromElement(radio) is YanziRadioAutomationPeer peer)
        {
            peer.NotifySelectionChanged((bool)args.OldValue, (bool)args.NewValue);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        Focus();
        Select();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled || e.Handled) return;
        if (e.Key is Key.Space or Key.Enter)
        {
            Select();
            e.Handled = true;
        }
        else if (Group is not null)
        {
            int direction = e.Key switch
            {
                Key.Right or Key.Down => 1,
                Key.Left or Key.Up => -1,
                Key.Home => -2,
                Key.End => 2,
                _ => 0
            };
            if (direction != 0)
                e.Handled = Group.Navigate(this, direction);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new YanziRadioAutomationPeer(this);
}

internal sealed class YanziRadioAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider
{
    internal YanziRadioAutomationPeer(YanziRadio owner) : base(owner) { }
    private YanziRadio Radio => (YanziRadio)Owner;
    protected override string GetClassNameCore() => nameof(YanziRadio);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;
    protected override string GetNameCore() =>
        AutomationProperties.GetName(Radio) is { Length: > 0 } name ? name : Radio.Value;
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);
    public bool IsSelected => Radio.IsChecked;
    public IRawElementProviderSimple? SelectionContainer =>
        Radio.Group is null ? null :
            ProviderFromPeer(UIElementAutomationPeer.CreatePeerForElement(Radio.Group));
    public void AddToSelection() => Select();
    public void RemoveFromSelection() => throw new InvalidOperationException("Radio selection cannot be cleared individually.");
    public void Select()
    {
        if (!Radio.IsEnabled) throw new ElementNotEnabledException();
        Radio.Select();
    }

    internal void NotifySelectionChanged(bool wasChecked, bool isChecked)
    {
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, wasChecked, isChecked);
        if (isChecked)
            RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
    }
}
