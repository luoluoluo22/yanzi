using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class QuickPanelWindow : Window, INotifyPropertyChanged
{
    private readonly RadialMenuSettings _radialSettings;
    private readonly ICommandActionExecutor _commandActionExecutor;
    private readonly MainWindow _mainWindow;

    private RadialMenuActivationSource _activeActivationSource = RadialMenuActivationSource.Unknown;
    private RadialMenuItemViewModel? _activeItem;
    private Point _radialCenter = new(700, 700);
    private double _overlayWidth = 1400;
    private double _overlayHeight = 1400;
    
    private string _activeTitle = "取消";
    private bool _isExecuting;
    private bool _isContentVisible;
    private bool _isPanelActive;
    private int _visibilityVersion;

    private double _panelLeft;
    private double _panelTop;

    public bool IsContentVisible
    {
        get => _isContentVisible;
        private set => SetField(ref _isContentVisible, value);
    }

    public double PanelLeft
    {
        get => _panelLeft;
        private set => SetField(ref _panelLeft, value);
    }

    public double PanelTop
    {
        get => _panelTop;
        private set => SetField(ref _panelTop, value);
    }

    public double OverlayWidth
    {
        get => _overlayWidth;
        private set => SetField(ref _overlayWidth, value);
    }

    public double OverlayHeight
    {
        get => _overlayHeight;
        private set => SetField(ref _overlayHeight, value);
    }

    public string ActiveTitle
    {
        get => _activeTitle;
        private set => SetField(ref _activeTitle, value);
    }

    public string PageTitle => "控制面板";

    public ObservableCollection<RadialMenuItemViewModel> GlobalSlots { get; } = [];
    public ObservableCollection<RadialMenuItemViewModel> ContextSlots { get; } = [];

    public QuickPanelWindow()
        : this(new RadialMenuSettings(), new DisabledCommandActionExecutor(), null!)
    {
    }

    public QuickPanelWindow(
        RadialMenuSettings radialSettings,
        ICommandActionExecutor commandActionExecutor,
        MainWindow mainWindow)
    {
        InitializeComponent();
        _radialSettings = radialSettings;
        _commandActionExecutor = commandActionExecutor;
        _mainWindow = mainWindow;

        DataContext = this;

        IsContentVisible = false;
        Opacity = 0;
        _isPanelActive = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void ShowPanel(RadialMenuActivationEventArgs? activation = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowPanel(activation));
            return;
        }

        Console.WriteLine($"[ui-panel] ShowPanel requested. Source={activation?.Source}, ScreenX={activation?.ScreenX}, ScreenY={activation?.ScreenY}");

        _isExecuting = false;
        _activeActivationSource = activation?.Source ?? RadialMenuActivationSource.Unknown;
        ClearActiveItem();
        var visibilityVersion = ++_visibilityVersion;

        Opacity = 0;
        IsContentVisible = false;

        PrepareOverlayForActivation(activation);
        LoadSlots();
        ActiveTitle = "取消";

        if (!IsVisible)
            Show();

        _isPanelActive = true;
        IsContentVisible = true;
        Dispatcher.UIThread.Post(() => RevealPanel(visibilityVersion), DispatcherPriority.Loaded);
    }

    private void RevealPanel(int visibilityVersion)
    {
        if (!_isPanelActive || visibilityVersion != _visibilityVersion)
            return;

        Opacity = 1;
        Topmost = true;
        Console.WriteLine($"[ui-panel] Panel revealed at Left={PanelLeft:0}, Top={PanelTop:0}");
    }

    public void ExecuteSelectedFromHoldRelease(RadialMenuActivationEventArgs? activation = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ExecuteSelectedFromHoldRelease(activation));
            return;
        }

        if (_isExecuting)
            return;

        _isExecuting = true;

        if (activation != null && activation.HasScreenPosition)
        {
            var position = new Point(activation.ScreenX!.Value - Position.X, activation.ScreenY!.Value - Position.Y);
            if (IsOutsidePanel(position))
            {
                global::Yanzi.Avalonia.App.WriteLog($"[ui-panel] ExecuteSelectedFromHoldRelease: Cancelled because cursor was outside panel at {position.X},{position.Y}");
                HidePanel();
                ActiveTitle = "取消";
                return;
            }
        }

        if (_activeItem?.Command != null)
        {
            ExecuteCommand(_activeItem.Command);
            return;
        }

        if (_activeItem != null && _activeItem.IsEmpty)
        {
            OpenSlotActionWindow(_activeItem);
            _isExecuting = false;
            return;
        }

        HidePanel();
        ActiveTitle = "取消";
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        HidePanel();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            if (IsOutsidePanel(pointer.Position))
            {
                HidePanel();
                e.Handled = true;
            }
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanelActive)
            return;

        var position = e.GetPosition(this);
        UpdateSelection(position);
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
    }

    private void Slot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: RadialMenuItemViewModel item })
            return;

        e.Handled = true;
        SetActiveItem(item);

        var pressedPoint = e.GetCurrentPoint(this);
        if (pressedPoint.Properties.IsRightButtonPressed)
        {
            OpenSlotActionWindow(item);
            return;
        }

        if (item.Command != null)
        {
            ExecuteCommand(item.Command);
        }
    }

    private void Slot_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (_activeActivationSource != RadialMenuActivationSource.Unknown)
            return;

        if (sender is Border { DataContext: RadialMenuItemViewModel item })
        {
            SetActiveItem(item);
        }
    }

    private void Slot_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_activeActivationSource != RadialMenuActivationSource.Unknown)
            return;

        if (sender is Border { DataContext: RadialMenuItemViewModel item })
        {
            if (ReferenceEquals(_activeItem, item))
                ClearActiveItem();
        }
    }

    private void OpenSlotActionWindow(RadialMenuItemViewModel item)
    {
        var window = new RadialSlotActionWindow(
            item.IsEmpty ? "搜索扩展" : "修改槽位",
            _mainWindow.GetRadialMenuCommandCandidates,
            command => AssignCommandToSlot(item, command),
            () => { }, // Sub-pages are not used in Grid panel
            () => RemoveCommandFromSlot(item),
            () => { },
            allowDeleteCommand: item.Command != null,
            allowDeleteChildPage: false);

        window.Show(this);
        window.Activate();
    }

    private void AssignCommandToSlot(RadialMenuItemViewModel item, CommandItem command)
    {
        var slotKey = item.OwnerPageId;
        if (!_radialSettings.Slots.TryGetValue(slotKey, out var slot))
        {
            slot = new RadialMenuSlotSettings();
            _radialSettings.Slots[slotKey] = slot;
        }
        slot.ExtensionId = command.ExtensionId;
        LoadSlots();
        ActiveTitle = $"已设置：{command.Title}";
    }

    private void RemoveCommandFromSlot(RadialMenuItemViewModel item)
    {
        var slotKey = item.OwnerPageId;
        if (_radialSettings.Slots.TryGetValue(slotKey, out var slot))
        {
            slot.ExtensionId = null;
        }
        LoadSlots();
        ActiveTitle = "已删除扩展";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        HidePanel();
    }

    private void LoadSlots()
    {
        GlobalSlots.Clear();
        ContextSlots.Clear();

        var allCommands = _mainWindow.GetRadialMenuCommandCandidates(string.Empty);

        // Load 12 Global Slots
        for (int i = 0; i < 12; i++)
        {
            var key = $"quickpanel_global_{i}";
            CommandItem? command = null;
            if (_radialSettings.Slots.TryGetValue(key, out var slot) && !string.IsNullOrEmpty(slot.ExtensionId))
            {
                command = allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, slot.ExtensionId, StringComparison.OrdinalIgnoreCase));
            }
            else if (i < allCommands.Count)
            {
                command = allCommands[i];
            }

            var vm = new RadialMenuItemViewModel(key, i, command, string.Empty, string.Empty, 0, 0, 0, RadialMenuRing.Inner);
            GlobalSlots.Add(vm);
        }

        // Load 12 Context Slots
        for (int i = 0; i < 12; i++)
        {
            var key = $"quickpanel_context_{i}";
            CommandItem? command = null;
            if (_radialSettings.Slots.TryGetValue(key, out var slot) && !string.IsNullOrEmpty(slot.ExtensionId))
            {
                command = allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, slot.ExtensionId, StringComparison.OrdinalIgnoreCase));
            }
            else if (i + 6 < allCommands.Count)
            {
                command = allCommands[i + 6];
            }

            var vm = new RadialMenuItemViewModel(key, i, command, string.Empty, string.Empty, 0, 0, 0, RadialMenuRing.Outer);
            ContextSlots.Add(vm);
        }
    }

    private void ExecuteCommand(CommandItem command)
    {
        _isExecuting = true;
        HidePanel();
        ActiveTitle = "取消";
        _ = ExecuteCommandAsync(command);
    }

    private async Task ExecuteCommandAsync(CommandItem command)
    {
        try
        {
            await Task.Delay(90);
            await Task.Run(() => _commandActionExecutor.Execute(command));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command execution failed: {ex}");
        }
    }

    private void UpdateSelection(Point position)
    {
        var item = FindMainRingItem(position);
        SetActiveItem(item);
    }

    private void SetActiveItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_activeItem, item))
            return;

        ClearActiveItem();
        _activeItem = item;
        if (_activeItem == null)
        {
            ActiveTitle = "取消";
            return;
        }

        _activeItem.IsHovered = true;
        _activeItem.IsSelected = true;
        ActiveTitle = string.IsNullOrWhiteSpace(_activeItem.Title) ? "松开或右键可配置扩展" : _activeItem.Title;
    }

    private void ClearActiveItem()
    {
        if (_activeItem != null)
        {
            _activeItem.IsHovered = false;
            _activeItem.IsSelected = false;
        }

        _activeItem = null;
        ActiveTitle = "取消";
    }

    private RadialMenuItemViewModel? FindMainRingItem(Point position)
    {
        double localX = position.X - PanelLeft;
        double localY = position.Y - PanelTop;

        if (localX >= 56 && localX < 356)
        {
            if (localY >= 64 && localY < 292)
            {
                int col = (int)((localX - 56) / 75);
                int row = (int)((localY - 64) / 76);
                col = Math.Clamp(col, 0, 3);
                row = Math.Clamp(row, 0, 2);
                int index = row * 4 + col;
                if (index >= 0 && index < GlobalSlots.Count)
                    return GlobalSlots[index];
            }
            else if (localY >= 332 && localY < 560)
            {
                int col = (int)((localX - 56) / 75);
                int row = (int)((localY - 332) / 76);
                col = Math.Clamp(col, 0, 3);
                row = Math.Clamp(row, 0, 2);
                int index = row * 4 + col;
                if (index >= 0 && index < ContextSlots.Count)
                    return ContextSlots[index];
            }
        }
        return null;
    }

    public void UpdateSelectionFromActivation(RadialMenuActivationEventArgs activation)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateSelectionFromActivation(activation));
            return;
        }

        if (!_isPanelActive || !activation.HasScreenPosition)
            return;

        var position = new Point(activation.ScreenX!.Value - Position.X, activation.ScreenY!.Value - Position.Y);
        UpdateSelection(position);
    }

    private void HidePanel()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HidePanel);
            return;
        }

        var visibilityVersion = ++_visibilityVersion;
        _isPanelActive = false;
        IsContentVisible = false;
        Opacity = 0;
        
        _activeActivationSource = RadialMenuActivationSource.Unknown;
        ClearActiveItem();

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);
            if (!_isPanelActive && visibilityVersion == _visibilityVersion)
            {
                Hide();
            }
        }, DispatcherPriority.Background);
    }

    private void PrepareOverlayForActivation(RadialMenuActivationEventArgs? activation)
    {
        var screenPoint = activation?.HasScreenPosition == true
            ? new Point(activation.ScreenX!.Value, activation.ScreenY!.Value)
            : new Point(Position.X + Width / 2, Position.Y + Height / 2);

        var screen = Screens.ScreenFromPoint(new PixelPoint((int)screenPoint.X, (int)screenPoint.Y)) ??
                     Screens.Primary ??
                     Screens.All.FirstOrDefault();

        if (screen == null)
        {
            SetRadialCenter(new Point(Width / 2, Height / 2));
            return;
        }

        var bounds = screen.Bounds;
        Position = bounds.Position;
        Width = bounds.Width;
        Height = bounds.Height;
        OverlayWidth = bounds.Width;
        OverlayHeight = bounds.Height;

        SetRadialCenter(new Point(screenPoint.X - bounds.X, screenPoint.Y - bounds.Y));
    }

    private void SetRadialCenter(Point center)
    {
        _radialCenter = center;
        
        double left = center.X - 183;
        double top = center.Y - 290;

        PanelLeft = Math.Clamp(left, 10, Width - 376);
        PanelTop = Math.Clamp(top, 10, Height - 590);
    }

    private bool IsOutsidePanel(Point position)
    {
        double localX = position.X - PanelLeft;
        double localY = position.Y - PanelTop;
        return localX < -40 || localX > 406 || localY < -40 || localY > 620;
    }

    private bool HasMovedAwayFromCenter(Point position)
    {
        var dx = position.X - _radialCenter.X;
        var dy = position.Y - _radialCenter.Y;
        return dx * dx + dy * dy > 400;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
