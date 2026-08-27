using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Yanzi.Shared;

public partial class RadialMenuItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isHovered;
    private double _scale = 1;
    private string _childPageId;
    private string _childPageTitle;

    public string OwnerPageId { get; }
    public int Index { get; }
    public CommandItem? Command { get; private set; }
    public string ChildPageId
    {
        get => _childPageId;
        set
        {
            if (SetProperty(ref _childPageId, value))
            {
                OnPropertyChanged(nameof(HasChildPage));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsNotEmpty));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(SectorBrush));
                OnPropertyChanged(nameof(SectorOpacity));
                OnPropertyChanged(nameof(IsSectorVisible));
                OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
            }
        }
    }

    public string ChildPageTitle
    {
        get => _childPageTitle;
        set
        {
            if (SetProperty(ref _childPageTitle, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(SectorBrush));
            }
        }
    }
    public double X { get; }
    public double Y { get; }
    public double AngleDegrees { get; }
    public RadialMenuRing Ring { get; }
    public Geometry? SectorGeometry { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(Scale));
                OnPropertyChanged(nameof(SectorOpacity));
                OnPropertyChanged(nameof(IsSectorVisible));
                OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
            }
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (SetProperty(ref _isHovered, value))
            {
                OnPropertyChanged(nameof(IsEmptyAndHovered));
                OnPropertyChanged(nameof(SectorOpacity));
                OnPropertyChanged(nameof(IsSectorVisible));
                OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
            }
        }
    }

    public double Scale
    {
        get => IsSelected ? 1.08 : _scale;
        set => SetProperty(ref _scale, value);
    }

    public bool IsEmpty => Command == null && !HasChildPage;
    public bool IsNotEmpty => !IsEmpty;
    public bool IsEmptyAndHovered => IsEmpty && IsHovered;
    public bool ShouldShowEmptyPlaceholder => IsEmpty && (IsHovered || IsSelected);
    public bool HasChildPage => !string.IsNullOrWhiteSpace(ChildPageId);
    public IBrush SectorBrush => IsEmpty
        ? new SolidColorBrush(Color.Parse("#FF64748B"))
        : HasChildPage
            ? new SolidColorBrush(Color.Parse("#FF3B82F6"))
            : new SolidColorBrush(Color.Parse("#FF334155"));
    public double SectorOpacity => IsSelected ? 0.58 : IsHovered ? 0.44 : IsEmpty ? 0.0 : 0.32;
    public bool IsSectorVisible => SectorGeometry != null && (!IsEmpty || IsHovered || IsSelected);

    public string? IconSource => Command?.IconPath;
    public bool HasImageIcon => !string.IsNullOrWhiteSpace(IconSource);
    
    private global::Avalonia.Media.Imaging.Bitmap? _realIcon;
    public global::Avalonia.Media.Imaging.Bitmap? RealIcon
    {
        get => _realIcon;
        set
        {
            if (SetProperty(ref _realIcon, value))
            {
                OnPropertyChanged(nameof(HasRealIcon));
                OnPropertyChanged(nameof(UseGlyphIcon));
            }
        }
    }
    public bool HasRealIcon => RealIcon != null;
    
    public string? VectorIcon => Command?.VectorIconData;
    public bool HasVectorIcon => !string.IsNullOrWhiteSpace(VectorIcon);
    public string? DisplayGlyph => Command?.Glyph;
    public bool UseGlyphIcon => !string.IsNullOrWhiteSpace(DisplayGlyph) && !HasRealIcon;
    public string Title => Command?.Title ?? ChildPageTitle;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "添加" : Title;

    public IBrush AccentBrush
    {
        get
        {
            if (IsEmpty)
            {
                if (global::Avalonia.Application.Current != null &&
                    global::Avalonia.Application.Current.TryGetResource("BrushSecondaryBtnBG", global::Avalonia.Application.Current.ActualThemeVariant, out var emptyBg) &&
                    emptyBg is IBrush emptyBrush)
                {
                    return emptyBrush;
                }
                return new SolidColorBrush(Color.Parse("#14FFFFFF"));
            }
            if (HasRealIcon || HasImageIcon) return new SolidColorBrush(Color.Parse("#00000000"));
            if (!string.IsNullOrWhiteSpace(Command?.AccentColor))
            {
                if (Color.TryParse(Command.AccentColor, out var col))
                    return new SolidColorBrush(col);
            }
            if (global::Avalonia.Application.Current != null &&
                global::Avalonia.Application.Current.TryGetResource("BrushSecondaryBtnBG", global::Avalonia.Application.Current.ActualThemeVariant, out var cardBg) &&
                cardBg is IBrush cardBrush)
            {
                return cardBrush;
            }
            return new SolidColorBrush(Color.Parse("#FF1E293B"));
        }
    }

    public RadialMenuItemViewModel(string ownerPageId, int index, CommandItem? command, string childPageId,
                                   string childPageTitle, double x, double y, double angleDegrees, RadialMenuRing ring,
                                   Geometry? sectorGeometry = null)
    {
        OwnerPageId = ownerPageId;
        Index = index;
        Command = command;
        _childPageId = childPageId;
        _childPageTitle = childPageTitle;
        X = x;
        Y = y;
        AngleDegrees = angleDegrees;
        Ring = ring;
        SectorGeometry = sectorGeometry;
    }

    public void UpdateCommand(CommandItem? command)
    {
        if (ReferenceEquals(Command, command)) return;
        Command = command;
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNotEmpty));
        OnPropertyChanged(nameof(IsEmptyAndHovered));
        OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
        OnPropertyChanged(nameof(SectorBrush));
        OnPropertyChanged(nameof(SectorOpacity));
        OnPropertyChanged(nameof(IsSectorVisible));
        OnPropertyChanged(nameof(IconSource));
        OnPropertyChanged(nameof(HasImageIcon));
        OnPropertyChanged(nameof(VectorIcon));
        OnPropertyChanged(nameof(HasVectorIcon));
        OnPropertyChanged(nameof(DisplayGlyph));
        OnPropertyChanged(nameof(UseGlyphIcon));
        OnPropertyChanged(nameof(Title));
    }
}
