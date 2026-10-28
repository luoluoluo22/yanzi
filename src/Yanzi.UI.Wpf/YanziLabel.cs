using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>Accessible form label bound to the target control's focus and disabled state.</summary>
public sealed class YanziLabel : TextBlock
{
    private FrameworkElement? _target;
    private string _caption = "";

    public FrameworkElement? Target
    {
        get => _target;
        set
        {
            _target = value;
            BindingOperations.ClearBinding(this, IsEnabledProperty);
            if (value is not null)
            {
                SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = value });
                AutomationProperties.SetLabeledBy(value, this);
            }
            Cursor = value is null ? Cursors.Arrow : Cursors.Hand;
        }
    }

    public string Caption
    {
        get => _caption;
        set { _caption = value ?? ""; Refresh(); }
    }

    public bool Required
    {
        get => _required;
        set { _required = value; Refresh(); }
    }

    private bool _required;

    public YanziLabel()
    {
        FontSize = 13;
        FontWeight = FontWeights.SemiBold;
        Margin = new Thickness(0, 0, 0, 7);
        SetResourceReference(ForegroundProperty, "Yanzi.Color.Foreground");
        MouseLeftButtonUp += (_, e) =>
        {
            if (IsEnabled && Target?.Focus() == true) e.Handled = true;
        };
    }

    private void Refresh()
    {
        Text = Caption + (Required ? " *" : "");
        AutomationProperties.SetName(this, Caption + (Required ? "，必填" : ""));
    }
}
