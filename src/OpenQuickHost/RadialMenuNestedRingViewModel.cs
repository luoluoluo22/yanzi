using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace OpenQuickHost;

public class RadialMenuNestedRingViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private double _centerX;
    private double _centerY;
    private bool _isLocked;
    private bool _isCenterHovered;
    private bool _isActive;
    private int _zIndex = 10;
    private RadialMenuItemViewModel? _selectedItem;

    public string PageId { get; set; } = string.Empty;
    public int Level { get; set; } // 1 for child, 2 for grandchild, etc.
    public double ParentX { get; set; }
    public double ParentY { get; set; }
    public bool HasParentConnection => ParentX > 0 || ParentY > 0;
    public bool IsStandaloneRadial { get; set; }

    public bool IsActive
    {
        get => _isActive;
        set 
        { 
            _isActive = value; 
            _zIndex = value ? 100 : 10;
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(ZIndex));
            OnPropertyChanged(nameof(ActiveBorderBrush));
            OnPropertyChanged(nameof(ActiveBorderThickness));
            OnPropertyChanged(nameof(ActiveGlowVisibility));
            OnPropertyChanged(nameof(StandaloneActiveGlowVisibility));
            OnPropertyChanged(nameof(SubRingActiveGlowVisibility));
        }
    }

    public Visibility StandaloneActiveGlowVisibility => (IsStandaloneRadial && IsActive) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubRingActiveGlowVisibility => (!IsStandaloneRadial && IsActive) ? Visibility.Visible : Visibility.Collapsed;

    public int ZIndex
    {
        get => _zIndex;
        set { _zIndex = value; OnPropertyChanged(); }
    }

    public System.Windows.Media.Brush ActiveBorderBrush => IsActive
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#2AFFFFFF")!;

    public double ActiveBorderThickness => IsActive ? 2.8 : 1.0;
    public Visibility ActiveGlowVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public double CenterX
    {
        get => _centerX;
        set 
        { 
            _centerX = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(EllipseX));
            OnPropertyChanged(nameof(OuterEllipseX));
            OnPropertyChanged(nameof(CenterEllipseX));
            OnPropertyChanged(nameof(TitleX));
        }
    }

    public double CenterY
    {
        get => _centerY;
        set 
        { 
            _centerY = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(EllipseY));
            OnPropertyChanged(nameof(OuterEllipseY));
            OnPropertyChanged(nameof(CenterEllipseY));
            OnPropertyChanged(nameof(TitleY));
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set 
        { 
            _isLocked = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(CenterContentVisibility));
            OnPropertyChanged(nameof(CenterPinVisibility));
            OnPropertyChanged(nameof(PinBrush));
        }
    }

    public bool IsCenterHovered
    {
        get => _isCenterHovered;
        set 
        { 
            _isCenterHovered = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(CenterContentVisibility));
            OnPropertyChanged(nameof(CenterPinVisibility));
            OnPropertyChanged(nameof(PinBrush));
        }
    }

    public RadialMenuItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); }
    }

    public ObservableCollection<RadialMenuItemViewModel> Items { get; } = new();
    public ObservableCollection<RadialSeparatorViewModel> Separators { get; } = new();
    public ObservableCollection<RadialMenuItemViewModel> OuterItems { get; } = new();
    public ObservableCollection<RadialSeparatorViewModel> OuterSeparators { get; } = new();
    public ObservableCollection<RadialMenuItemViewModel> MostOuterItems { get; } = new();
    public ObservableCollection<RadialSeparatorViewModel> MostOuterSeparators { get; } = new();

    public double OuterEllipseX => CenterX - 165;
    public double OuterEllipseY => CenterY - 165;
    public double EllipseX => CenterX - 100;
    public double EllipseY => CenterY - 100;
    public double CenterEllipseX => CenterX - 36;
    public double CenterEllipseY => CenterY - 36;
    public double TitleX => CenterX - 75;
    public double TitleY => CenterY - 10;

    public Visibility CenterContentVisibility => Visibility.Visible;
    public Visibility CenterPinVisibility => Visibility.Collapsed;
    public System.Windows.Media.Brush PinBrush => IsLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFA0A0A0")!;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
