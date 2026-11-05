using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenQuickHost;

/// <summary>
/// Keeps the existing schedule-chip visuals while giving mouse-only choices
/// standard keyboard activation and UI Automation Invoke semantics.
/// </summary>
public sealed class ScheduleChoiceBorder : Border
{
    public ScheduleChoiceBorder()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ScheduleChoiceAutomationPeer(this);

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || (e.Key != Key.Enter && e.Key != Key.Space))
            return;
        Activate();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!IsKeyboardFocused || ActualWidth < 4 || ActualHeight < 4)
            return;

        var ring = TryFindResource("Yanzi.Color.Ring") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue;
        drawingContext.DrawRoundedRectangle(null, new System.Windows.Media.Pen(ring, 2),
            new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 5, 5);
    }

    internal void Activate()
    {
        if (!IsEnabled || !IsVisible)
            return;

        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = this
        };
        RaiseEvent(click);
    }
}

internal sealed class ScheduleChoiceAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    private ScheduleChoiceBorder Choice => (ScheduleChoiceBorder)Owner;

    public ScheduleChoiceAutomationPeer(ScheduleChoiceBorder owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetClassNameCore() => nameof(ScheduleChoiceBorder);
    protected override bool IsControlElementCore() => true;
    protected override bool IsContentElementCore() => true;

    protected override string GetNameCore()
    {
        var explicitName = AutomationProperties.GetName(Choice);
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName;
        if (Choice.Child is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
            return text.Text;
        return Choice.Tag?.ToString() ?? "定时运行选项";
    }

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

    public void Invoke()
    {
        if (!Choice.IsEnabled)
            throw new ElementNotEnabledException();
        Choice.Dispatcher.BeginInvoke(() => Choice.Activate());
    }
}
