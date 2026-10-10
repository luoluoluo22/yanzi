using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>
/// A single outline for input and surrounding addons (shadcn InputGroup).
/// Addons may be text, buttons, icons, or keyboard hints; all inherit one
/// focus ring and disabled state. The input keeps its native editing semantics.
/// </summary>
public sealed class YanziInputGroup : UserControl
{
    private readonly DockPanel _inline = new() { LastChildFill = true };
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _above = new();
    private readonly StackPanel _below = new();
    private readonly Border _outline;

    public TextBox Input { get; }
    public Panel Leading => _leading;
    public Panel Trailing => _trailing;
    public Panel Above => _above;
    public Panel Below => _below;

    public YanziInputGroup(string prefix = "", string suffix = "")
    {
        _outline = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            MinHeight = 36
        };
        _outline.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        _outline.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Input");

        Input = new TextBox
        {
            MinWidth = 72,
            MinHeight = 34,
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 14,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        // Without a local no-chrome template, default WPF draws a second
        // rectangular outline when hovering/focusing the nested TextBox.
        var template = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(FocusableProperty, false);
        template.VisualTree = host;
        Input.Template = template;
        Input.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.Foreground");
        Input.SetResourceReference(TextBoxBase.CaretBrushProperty, "Yanzi.Color.Foreground");
        AutomationProperties.SetName(Input, "Input group field");

        if (!string.IsNullOrEmpty(prefix)) AddText(prefix, leading: true);
        if (!string.IsNullOrEmpty(suffix)) AddText(suffix, leading: false);

        DockPanel.SetDock(_leading, Dock.Left);
        DockPanel.SetDock(_trailing, Dock.Right);
        _inline.Children.Add(_leading);
        _inline.Children.Add(_trailing);
        _inline.Children.Add(Input);
        _outline.Child = _inline;

        var root = new StackPanel();
        root.Children.Add(_above);
        root.Children.Add(_outline);
        root.Children.Add(_below);
        Content = root;

        Input.GotKeyboardFocus += (_, _) =>
            _outline.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Ring");
        Input.LostKeyboardFocus += (_, _) =>
            _outline.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Input");
        IsEnabledChanged += (_, _) => _outline.Opacity = IsEnabled ? 1 : .55;
    }

    public void AddText(string text, bool leading = false)
    {
        var label = new TextBlock
        {
            Text = text,
            Margin = leading ? new Thickness(10, 0, 4, 0) : new Thickness(4, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        AddAddon(label, leading);
    }

    public void AddAddon(UIElement content, bool leading = false) =>
        (leading ? _leading : _trailing).Children.Add(content);

    public void AddBlockAddon(UIElement content, bool above = false) =>
        (above ? _above : _below).Children.Add(content);
}
