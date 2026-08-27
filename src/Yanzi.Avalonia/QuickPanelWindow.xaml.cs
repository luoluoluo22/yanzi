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

    public static readonly StyledProperty<bool> IsPinnedProperty =
        AvaloniaProperty.Register<QuickPanelWindow, bool>(nameof(IsPinned));

    public bool IsPinned
    {
        get => GetValue(IsPinnedProperty);
        set => SetValue(IsPinnedProperty, value);
    }

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
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        var formats = string.Join(", ", e.Data.GetDataFormats());
        Console.WriteLine($"[ui-panel] DragOver: formats={formats}");
        
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        var formats = string.Join(", ", e.Data.GetDataFormats());
        Console.WriteLine($"[ui-panel] Window_Drop fired. Formats: {formats}");
        
        if (!e.Data.Contains(DataFormats.Files)) 
        {
            Console.WriteLine("[ui-panel] e.Data.Contains(DataFormats.Files) returned false.");
            return;
        }

        var files = e.Data.GetFiles()?.ToArray();
        Console.WriteLine($"[ui-panel] Files count: {files?.Length}");
        if (files == null || files.Length == 0) return;

        var position = e.GetPosition(this);
        var targetSlot = FindMainRingItem(position) ?? GlobalSlots.Concat(ContextSlots).FirstOrDefault(s => s.IsEmpty);
        Console.WriteLine($"[ui-panel] Found target slot: {targetSlot?.OwnerPageId}, IsEmpty: {targetSlot?.IsEmpty}");
        
        if (targetSlot == null) return;

        if (!targetSlot.IsEmpty)
        {
            var result = await Task.Run(() =>
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e 'display dialog \"槽位已有内容（{targetSlot.Title}），是否替换？\" buttons {{\"取消\", \"替换\"}} default button \"替换\" with title \"燕子启动器\"'",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            });
            
            if (!result.Contains("button returned:替换"))
            {
                return;
            }
        }

        var path = files[0].Path.LocalPath;
        Console.WriteLine($"[ui-panel] LocalPath is: {path}");
        
        var command = new CommandItem
        {
            ExtensionId = "custom_" + Guid.NewGuid().ToString("N"),
            Title = System.IO.Path.GetFileNameWithoutExtension(path),
            ActionKind = CommandActionKind.LaunchApplication,
            ApplicationName = path
        };

        Dispatcher.UIThread.Post(() => {
            try 
            {
                Console.WriteLine($"[ui-panel] Dispatching AddCustomExtension...");
                _mainWindow.AddCustomExtension(command);
                Console.WriteLine($"[ui-panel] Dispatching AssignCommandToSlot...");
                AssignCommandToSlot(targetSlot, command);
                Console.WriteLine($"[ui-panel] Successfully assigned command.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ui-panel] Error in Drop Dispatcher: {ex}");
            }
        });
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

        PrepareOverlayForActivation(activation);
        LoadSlots();
        ActiveTitle = "取消";

        if (!IsVisible)
            Show();

        _isPanelActive = true;
        IsContentVisible = true;
        Opacity = 1;
        Topmost = true;
    }

    private void RevealPanel(int visibilityVersion)
    {
        if (!_isPanelActive || visibilityVersion != _visibilityVersion)
            return;

        Opacity = 1;
        Topmost = true;
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
                if (!IsPinned)
                    HidePanel();
                ActiveTitle = "取消";
                _isExecuting = false;
                _activeActivationSource = RadialMenuActivationSource.Unknown;
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
            _activeActivationSource = RadialMenuActivationSource.Unknown;
            return;
        }

        _isExecuting = false;
        _activeActivationSource = RadialMenuActivationSource.Unknown;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!IsPinned)
            HidePanel();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            if (IsOutsidePanel(pointer.Position))
            {
                if (!IsPinned)
                    HidePanel();
                e.Handled = true;
            }
            else
            {
                BeginMoveDrag(e);
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
        var defaultCommands = _mainWindow.GetDefaultCommands(string.Empty).ToList();

        // Load 12 Global Slots
        for (int i = 0; i < 12; i++)
        {
            var key = $"quickpanel_global_{i}";
            CommandItem? command = null;
            if (_radialSettings.Slots.TryGetValue(key, out var slot) && !string.IsNullOrEmpty(slot.ExtensionId))
            {
                command = allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, slot.ExtensionId, StringComparison.OrdinalIgnoreCase));
            }
            else if (i < defaultCommands.Count)
            {
                command = defaultCommands[i];
            }

            var vm = new RadialMenuItemViewModel(key, i, command, string.Empty, string.Empty, 0, 0, 0, RadialMenuRing.Inner);
            GlobalSlots.Add(vm);
            
            if (command != null && command.ActionKind == CommandActionKind.LaunchApplication && !string.IsNullOrEmpty(command.ApplicationName))
            {
                LoadNativeIconForViewModelAsync(vm, command.ApplicationName);
            }
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
            else if (i + 6 < defaultCommands.Count)
            {
                command = defaultCommands[i + 6];
            }

            var vm = new RadialMenuItemViewModel(key, i, command, string.Empty, string.Empty, 0, 0, 0, RadialMenuRing.Outer);
            ContextSlots.Add(vm);
            
            if (command != null && command.ActionKind == CommandActionKind.LaunchApplication && !string.IsNullOrEmpty(command.ApplicationName))
            {
                LoadNativeIconForViewModelAsync(vm, command.ApplicationName);
            }
        }
    }
    
    private async void LoadNativeIconForViewModelAsync(RadialMenuItemViewModel vm, string path)
    {
        try
        {
            var pngBytes = await Task.Run(() => MacIconExtractor.GetFileIconPngBytes(path));
            if (pngBytes != null && pngBytes.Length > 0)
            {
                using var ms = new System.IO.MemoryStream(pngBytes);
                var bitmap = new global::Avalonia.Media.Imaging.Bitmap(ms);
                Dispatcher.UIThread.Post(() => vm.RealIcon = bitmap);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading icon for {path}: {ex}");
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
            return;

        var bounds = screen.Bounds;
        
        // Window bounds including shadow padding
        int windowWidth = 466;
        int windowHeight = 680;
        PanelLeft = 50;
        PanelTop = 50;

        // Calculate top-left of the PANEL on screen
        double panelScreenLeft = screenPoint.X - 183;
        double panelScreenTop = screenPoint.Y - 290;
        
        // Clamp PANEL inside screen bounds with 10px margin
        panelScreenLeft = Math.Clamp(panelScreenLeft, bounds.X + 10, bounds.X + bounds.Width - 366 - 10);
        panelScreenTop = Math.Clamp(panelScreenTop, bounds.Y + 10, bounds.Y + bounds.Height - 580 - 10);

        // Window position is panel position minus the shadow padding
        Position = new PixelPoint((int)panelScreenLeft - 50, (int)panelScreenTop - 50);
        
        Width = windowWidth;
        Height = windowHeight;
        OverlayWidth = windowWidth;
        OverlayHeight = windowHeight;
        
        SetRadialCenter(new Point(233, 340));
    }

    private void SetRadialCenter(Point center)
    {
        _radialCenter = center;
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
