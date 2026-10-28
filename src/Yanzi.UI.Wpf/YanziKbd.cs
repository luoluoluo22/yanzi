using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>
/// A semantic, theme-aware keycap for documenting a single keyboard key.
/// Like shadcn Kbd, it is display-only; text comes from the consumer.
/// </summary>
public sealed class YanziKbd : Border
{
    private readonly TextBlock _label;

    public static readonly DependencyProperty KeyTextProperty =
        DependencyProperty.Register(nameof(KeyText), typeof(string), typeof(YanziKbd),
            new PropertyMetadata(string.Empty, (target, args) =>
            {
                ((YanziKbd)target)._label.Text = args.NewValue as string ?? string.Empty;
            }));

    public string KeyText
    {
        get => (string)GetValue(KeyTextProperty);
        set => SetValue(KeyTextProperty, value);
    }

    public YanziKbd()
    {
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        _label.SetResourceReference(TextBlock.FontFamilyProperty, "Yanzi.Font.Geist");
        Child = _label;
        SetResourceReference(StyleProperty, "Yanzi.Kbd");
    }
}

/// <summary>Inline composition container for two or more YanziKbd keycaps.</summary>
public sealed class YanziKbdGroup : StackPanel
{
    public YanziKbdGroup()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(StyleProperty, "Yanzi.KbdGroup");
    }
}
