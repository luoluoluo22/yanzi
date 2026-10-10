using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>Reusable circular avatar with optional image and initials fallback.</summary>
public sealed class YanziAvatar : Border
{
    private readonly TextBlock _fallback;
    private readonly Image _portrait;

    public static readonly DependencyProperty InitialsProperty =
        DependencyProperty.Register(nameof(Initials), typeof(string), typeof(YanziAvatar),
            new PropertyMetadata("", (obj, _) => ((YanziAvatar)obj).Update()));

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(YanziAvatar),
            new PropertyMetadata(null, (obj, _) => ((YanziAvatar)obj).Update()));

    public static readonly DependencyProperty DiameterProperty =
        DependencyProperty.Register(nameof(Diameter), typeof(double), typeof(YanziAvatar),
            new PropertyMetadata(42d, (obj, _) => ((YanziAvatar)obj).Update()));

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public bool IsShowingFallback => _portrait.Visibility != Visibility.Visible;

    public YanziAvatar()
    {
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Secondary");
        _fallback = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        _fallback.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.SecondaryForeground");
        _portrait = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
        _portrait.ImageFailed += (_, _) =>
        {
            _portrait.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
        };
        var grid = new Grid();
        grid.Children.Add(_fallback);
        grid.Children.Add(_portrait);
        Child = grid;
        Update();
    }

    private void Update()
    {
        if (_fallback is null) return;
        var diameter = double.IsFinite(Diameter) && Diameter >= 16 ? Diameter : 42;
        Width = diameter;
        Height = diameter;
        CornerRadius = new CornerRadius(diameter / 2);
        _fallback.Text = Initials;
        _fallback.FontSize = Math.Max(10, diameter * 0.31);
        AutomationProperties.SetName(this, Initials);
        _portrait.Clip = new EllipseGeometry(new Point(diameter / 2, diameter / 2), diameter / 2, diameter / 2);
        _portrait.Source = ImageSource;
        _portrait.Visibility = ImageSource is null ? Visibility.Collapsed : Visibility.Visible;
        _fallback.Visibility = ImageSource is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
