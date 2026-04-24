using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OpenQuickHost;

public partial class QuickPanelWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindow _mainWindow;
    private AppSettings _settings;
    private readonly List<SlotViewModel> _allSlots = new();
    private bool _isPinned;

    public QuickPanelWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        
        LoadSlots();
        DataContext = this;

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    public ObservableCollection<SlotViewModel> Slots { get; } = new();

    public System.Windows.Media.Brush PinButtonBrush => _isPinned
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFF59E0B")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF888888")!;

    public string PinButtonTooltip => _isPinned ? "已固定，失去焦点时不自动关闭" : "点击固定，失去焦点时不自动关闭";

    private bool _isShowingFavorites = false;
    public bool IsShowingFavorites
    {
        get => _isShowingFavorites;
        private set
        {
            if (_isShowingFavorites == value) return;
            _isShowingFavorites = value;
            OnPropertyChanged();
        }
    }

    private void LoadSlots()
    {
        _settings = AppSettingsStore.Load();
        Slots.Clear();
        var allCommands = _mainWindow.GetAllCommands();

        if (_isShowingFavorites)
        {
            // Show only favorited slots
            var favIds = _settings.FavoriteExtensionIds;
            foreach (var favId in favIds)
            {
                var command = allCommands.FirstOrDefault(c => c.ExtensionId == favId);
                if (command != null)
                    Slots.Add(new SlotViewModel(Slots.Count, command, true));
            }
            // Pad remaining with empty
            while (Slots.Count < 28)
                Slots.Add(new SlotViewModel(Slots.Count, null, false));
        }
        else
        {
            for (int i = 0; i < 28; i++)
            {
                var extensionId = _settings.QuickPanelSlots.ElementAtOrDefault(i);
                var command = string.IsNullOrEmpty(extensionId)
                    ? null
                    : allCommands.FirstOrDefault(c => c.ExtensionId == extensionId);
                var isFav = command != null && _settings.FavoriteExtensionIds.Contains(command.ExtensionId);
                Slots.Add(new SlotViewModel(i, command, isFav));
            }
        }

        // Cache all slots for searching
        _allSlots.Clear();
        _allSlots.AddRange(Slots);
    }

    private void HubSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = HubSearchBox.Text.Trim().ToLower();
        if (string.IsNullOrEmpty(query))
        {
            // Restore all slots
            if (Slots.Count != _allSlots.Count)
            {
                Slots.Clear();
                foreach (var s in _allSlots) Slots.Add(s);
            }
            return;
        }

        // Filter and update
        var filtered = _allSlots
            .Where(s => s.IsOccupied && s.Title.ToLower().Contains(query))
            .ToList();

        Slots.Clear();
        foreach (var s in filtered) Slots.Add(s);
    }

    private void SaveSlots()
    {
        var slotsList = new List<string?>();
        for (int i = 0; i < 28; i++)
        {
            var vm = Slots.ElementAtOrDefault(i);
            slotsList.Add(vm?.Command?.ExtensionId);
        }
        _settings.QuickPanelSlots = slotsList;
        AppSettingsStore.Save(_settings);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _mainWindow.OpenSettingsWindow("quickpanel");
    }

    private void PinAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
    }

    private void SlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is SlotViewModel vm)
        {
            if (vm.Command != null)
            {
                _mainWindow.ExecuteCommandExternally(vm.Command);
                Hide();
            }
            else
            {
                // Open add extension dialog, then fill this slot
                var newCommand = _mainWindow.OpenAddExtensionForSlot();
                if (newCommand != null)
                {
                    vm.SetCommand(newCommand, false);
                    SaveSlots();
                }
            }
        }
    }

    private void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm)
        {
            vm.SetCommand(null, false);
            SaveSlots();
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm && vm.Command != null)
        {
            var id = vm.Command.ExtensionId;
            if (_settings.FavoriteExtensionIds.Contains(id))
                _settings.FavoriteExtensionIds.Remove(id);
            else
                _settings.FavoriteExtensionIds.Add(id);

            AppSettingsStore.Save(_settings);
            vm.SetFavorite(_settings.FavoriteExtensionIds.Contains(id));
        }
    }

    private void SidebarAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isShowingFavorites)
        {
            IsShowingFavorites = false;
            LoadSlots();
        }
    }

    private void SidebarFavButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isShowingFavorites)
        {
            IsShowingFavorites = true;
            LoadSlots();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_isPinned)
        {
            return;
        }

        Hide();
    }

    public void ShowAtMouse()
    {
        var point = NativeMethods.GetCursorPosition();
        Left = point.X - Width / 2;
        Top = point.Y - Height / 2;
        
        // Ensure within screen bounds
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
        if (Left < screen.Bounds.Left) Left = screen.Bounds.Left;
        if (Top < screen.Bounds.Top) Top = screen.Bounds.Top;
        if (Left + Width > screen.Bounds.Right) Left = screen.Bounds.Right - Width;
        if (Top + Height > screen.Bounds.Bottom) Top = screen.Bounds.Bottom - Height;

        HubSearchBox.Text = string.Empty; // Reset search on show
        LoadSlots(); // Refresh
        Show();
        Activate();
        Focus();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Focus();
            HubSearchBox.Focus();
            Keyboard.Focus(HubSearchBox);
            HubSearchBox.SelectAll();
        }, DispatcherPriority.ApplicationIdle);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => 
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class SlotViewModel : INotifyPropertyChanged
{
    public int Index { get; }
    private CommandItem? _command;
    private bool _isFavorite;

    public CommandItem? Command => _command;

    public SlotViewModel(int index, CommandItem? command, bool isFavorite = false)
    {
        Index = index;
        _command = command;
        _isFavorite = isFavorite;
    }

    public void SetCommand(CommandItem? command, bool isFavorite = false)
    {
        _command = command;
        _isFavorite = isFavorite;
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOccupied));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
    }

    public void SetFavorite(bool isFavorite)
    {
        _isFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
    }

    public bool IsEmpty => _command == null;
    public bool IsOccupied => _command != null;
    public bool IsFavorite => _isFavorite;
    public string FavoriteLabel => _isFavorite ? "取消收藏" : "收藏";
    public string Title => _command?.Title ?? string.Empty;
    public ImageSource? Icon => _command?.IconSource;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static System.Windows.Point GetCursorPosition()
    {
        GetCursorPos(out var lpPoint);
        return new System.Windows.Point(lpPoint.X, lpPoint.Y);
    }
}

public class NullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value == null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class NotNullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToColorConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not bool val) return System.Windows.Media.Brushes.Transparent;
        string[] colors = (parameter as string ?? "#FF555555|White").Split('|');
        var colorStr = val ? colors[0] : colors[1];
        return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colorStr)!;
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

