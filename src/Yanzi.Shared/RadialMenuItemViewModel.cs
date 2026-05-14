using CommunityToolkit.Mvvm.ComponentModel;

namespace Yanzi.Shared;

public partial class RadialMenuItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isHovered;
    private double _scale = 1;

    public string OwnerPageId { get; }
    public int Index { get; }
    public CommandItem? Command { get; }
    public string ChildPageId { get; }
    public string ChildPageTitle { get; }
    public double X { get; }
    public double Y { get; }
    public double AngleDegrees { get; }
    public RadialMenuRing Ring { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (SetProperty(ref _isHovered, value))
            {
                OnPropertyChanged(nameof(IsEmptyAndHovered));
            }
        }
    }

    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public bool IsEmpty => Command == null && !HasChildPage;
    public bool IsNotEmpty => !IsEmpty;
    public bool IsEmptyAndHovered => IsEmpty && IsHovered;
    public bool HasChildPage => !string.IsNullOrWhiteSpace(ChildPageId);

    public string? IconSource => Command?.IconPath;
    public bool HasImageIcon => !string.IsNullOrWhiteSpace(IconSource);
    public string? VectorIcon => Command?.VectorIconData;
    public bool HasVectorIcon => !string.IsNullOrWhiteSpace(VectorIcon);
    public string? DisplayGlyph => Command?.Glyph;
    public bool UseGlyphIcon => !string.IsNullOrWhiteSpace(DisplayGlyph);
    public string Title => Command?.Title ?? ChildPageTitle;

    public RadialMenuItemViewModel(string ownerPageId, int index, CommandItem? command, string childPageId, 
                                   string childPageTitle, double x, double y, double angleDegrees, RadialMenuRing ring)
    {
        OwnerPageId = ownerPageId;
        Index = index;
        Command = command;
        ChildPageId = childPageId;
        ChildPageTitle = childPageTitle;
        X = x;
        Y = y;
        AngleDegrees = angleDegrees;
        Ring = ring;
    }
}
