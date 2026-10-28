using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

public enum YanziBadgeVariant
{
    Default,
    Secondary,
    Destructive,
    Outline,
    Ghost,
    Link
}

/// <summary>
/// Compact shadcn-like status badge. A badge describes a status; it does not
/// perform navigation by itself. Use a real Button/Hyperlink for actions.
/// </summary>
public sealed class YanziBadge : ContentControl
{
    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(YanziBadgeVariant), typeof(YanziBadge),
            new FrameworkPropertyMetadata(YanziBadgeVariant.Default));
    public static readonly DependencyProperty LeadingIconProperty =
        DependencyProperty.Register(nameof(LeadingIcon), typeof(string), typeof(YanziBadge),
            new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TrailingIconProperty =
        DependencyProperty.Register(nameof(TrailingIcon), typeof(string), typeof(YanziBadge),
            new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(YanziBadge),
            new PropertyMetadata(false));

    public YanziBadgeVariant Variant
    {
        get => (YanziBadgeVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string LeadingIcon
    {
        get => (string)GetValue(LeadingIconProperty);
        set => SetValue(LeadingIconProperty, value);
    }

    public string TrailingIcon
    {
        get => (string)GetValue(TrailingIconProperty);
        set => SetValue(TrailingIconProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public YanziBadge()
    {
        Focusable = false;
        SetResourceReference(StyleProperty, YanziUi.Styles.Badge);
    }
}
