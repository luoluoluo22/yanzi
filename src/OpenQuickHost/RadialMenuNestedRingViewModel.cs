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
    private RadialMenuItemViewModel? _selectedItem;

    public string PageId { get; set; } = string.Empty;
    public int Level { get; set; } // 1 for child, 2 for grandchild, etc.

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

    public double EllipseX => CenterX - 100;
    public double EllipseY => CenterY - 100;
    public double CenterEllipseX => CenterX - 36;
    public double CenterEllipseY => CenterY - 36;
    public double TitleX => CenterX - 75;
    public double TitleY => CenterY - 10;

    public Visibility CenterContentVisibility => (IsCenterHovered || IsLocked) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CenterPinVisibility => (IsCenterHovered || IsLocked) ? Visibility.Visible : Visibility.Collapsed;
    public System.Windows.Media.Brush PinBrush => IsLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFA0A0A0")!;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
