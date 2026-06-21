using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class RadialMenuWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _selectionTimer;
    private System.Drawing.Point _centerPixels;
    private RadialMenuItemViewModel? _selectedItem;
    private RadialMenuItemViewModel? _selectedChildItem;
    private RadialMenuItemViewModel? _selectedGrandChildItem;
    private List<RadialMenuPageSettings> _pages = [];
    private string _currentPageId = string.Empty;
    private string? _activeProcessName;
    private bool _isEditHoverActive;
    private bool _isAddHoverActive;
    private readonly Stack<string> _pageStack = new();
    private bool _isExecuting;
    private string _activeTitle = "取消";
    private string _pageTitle = "燕环";
    private bool _hasChildRing;
    private string _childRingTitle = string.Empty;
    private string _grandChildRingTitle = string.Empty;
    private double _childRingCenterX;
    private double _childRingCenterY;
    private double _grandChildRingCenterX;
    private double _grandChildRingCenterY;
    private bool _hasGrandChildRing;
    private IntPtr _previousForegroundWindow;
    private bool _editModeLocked;
    private bool _editInteractionActive;
    private bool _isPinned;
    private bool _isPinHoverActive;
    private string _centerPrimaryText = "燕环";
    private bool _isCenterCloseMode;
    private bool _isCenterHovered;
    private RadialSlotPayload? _cutSlotPayload;
    private RadialMenuItemViewModel? _dragSourceItem;
    private int _lastRadiusPixels = 96;

    public RadialMenuWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _selectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _selectionTimer.Tick += (_, _) => UpdateSelectionFromCursor();
        DataContext = this;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _editModeLocked = false;
                Hide();
            }
        };
        MouseWheel += RadialMenuWindow_MouseWheel;
        MouseLeftButtonDown += RadialMenuWindow_MouseLeftButtonDown;
        MouseRightButtonDown += RadialMenuWindow_MouseRightButtonDown;
        Loaded += (_, _) => RebuildItemsForCurrentLayout("loaded");
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RebuildItemsForCurrentLayout("size-changed");
            }
        };
    }

    public ObservableCollection<RadialMenuItemViewModel> Items { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> OuterItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> ChildItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> GrandChildItems { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> MainSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> OuterSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> ChildSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> GrandChildSeparators { get; } = [];

    public bool HasChildRing
    {
        get => _hasChildRing;
        private set
        {
            if (value == _hasChildRing)
            {
                return;
            }

            _hasChildRing = value;
            OnPropertyChanged();
        }
    }

    public string ChildRingTitle
    {
        get => _childRingTitle;
        private set
        {
            if (value == _childRingTitle)
            {
                return;
            }

            _childRingTitle = value;
            OnPropertyChanged();
        }
    }

    public bool HasGrandChildRing
    {
        get => _hasGrandChildRing;
        private set
        {
            if (value == _hasGrandChildRing)
            {
                return;
            }

            _hasGrandChildRing = value;
            OnPropertyChanged();
        }
    }

    public string GrandChildRingTitle
    {
        get => _grandChildRingTitle;
        private set
        {
            if (value == _grandChildRingTitle)
            {
                return;
            }

            _grandChildRingTitle = value;
            OnPropertyChanged();
        }
    }

    public double ChildRingEllipseX => _childRingCenterX - 120;

    public double ChildRingEllipseY => _childRingCenterY - 120;

    public double ChildRingCenterEllipseX => _childRingCenterX - 32;

    public double ChildRingCenterEllipseY => _childRingCenterY - 32;

    public double ChildRingTitleX => _childRingCenterX - 75;

    public double ChildRingTitleY => _childRingCenterY - 10;

    public double GrandChildRingEllipseX => _grandChildRingCenterX - 98;

    public double GrandChildRingEllipseY => _grandChildRingCenterY - 98;

    public double GrandChildRingCenterEllipseX => _grandChildRingCenterX - 27;

    public double GrandChildRingCenterEllipseY => _grandChildRingCenterY - 27;

    public double GrandChildRingTitleX => _grandChildRingCenterX - 64;

    public double GrandChildRingTitleY => _grandChildRingCenterY - 10;

    private ImageSource? _centerIcon;
    public ImageSource? CenterIcon
    {
        get => _centerIcon;
        private set
        {
            _centerIcon = value;
            OnPropertyChanged();
        }
    }

    private string _pageDisplaySummary = string.Empty;
    public string PageDisplaySummary
    {
        get => _pageDisplaySummary;
        private set
        {
            if (value == _pageDisplaySummary) return;
            _pageDisplaySummary = value;
            OnPropertyChanged();
        }
    }

    public string ActiveTitle
    {
        get => _activeTitle;
        private set
        {
            if (value == _activeTitle)
            {
                return;
            }

            _activeTitle = value;
            OnPropertyChanged();
        }
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set
        {
            if (value == _pageTitle)
            {
                return;
            }

            _pageTitle = value;
            OnPropertyChanged();
        }
    }

    public string CenterPrimaryText
    {
        get => _centerPrimaryText;
        private set
        {
            if (value == _centerPrimaryText)
            {
                return;
            }

            _centerPrimaryText = value;
            OnPropertyChanged();
        }
    }

    public bool IsCenterCloseMode
    {
        get => _isCenterCloseMode;
        private set
        {
            if (value == _isCenterCloseMode)
            {
                return;
            }

            _isCenterCloseMode = value;
            OnPropertyChanged();
        }
    }

    public bool IsCenterHovered
    {
        get => _isCenterHovered;
        private set
        {
            if (value == _isCenterHovered)
            {
                return;
            }

            _isCenterHovered = value;
            OnPropertyChanged();
        }
    }

    public System.Windows.Media.Brush PinButtonBrush => _isPinned
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFF59E0B")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF888888")!;

    public string PinButtonTooltip => _isPinned ? "已常驻，失去焦点和执行命令时不自动关闭" : "点击后常驻，失去焦点和执行命令时不自动关闭";

    public bool IsPinHoverActive
    {
        get => _isPinHoverActive;
        private set
        {
            if (value == _isPinHoverActive)
            {
                return;
            }

            _isPinHoverActive = value;
            OnPropertyChanged();
        }
    }

    public bool IsAddHoverActive
    {
        get => _isAddHoverActive;
        private set
        {
            if (value == _isAddHoverActive)
            {
                return;
            }

            _isAddHoverActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AddButtonBrush));
        }
    }

    public System.Windows.Media.Brush AddButtonBrush => _isAddHoverActive
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF10B981")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF888888")!;

    public bool IsEditHoverActive
    {
        get => _isEditHoverActive;
        private set
        {
            if (value == _isEditHoverActive)
            {
                return;
            }

            _isEditHoverActive = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<PaginationDotViewModel> PaginationDots { get; } = new();

    public System.Windows.Media.Brush EditButtonBrush => _editModeLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF888888")!;

    private void LoadRadialMenuPages()
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        if (settings.RadialMenu.Pages.Count == 0)
        {
            settings.RadialMenu.Pages.Add(new RadialMenuPageSettings 
            { 
                Id = "default", 
                Name = "全局",
                Slots = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
                SlotTitles = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
                ChildPageIds = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
            });
            AppSettingsStore.Save(settings);
            _mainWindow.RefreshAppSettings();
        }

        if ((_editModeLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) && !string.IsNullOrWhiteSpace(_activeProcessName))
        {
            var normalizedProcess = _activeProcessName.Trim().ToLowerInvariant().Replace(".exe", "");
            var appPage = settings.RadialMenu.Pages.FirstOrDefault(item => 
                !string.IsNullOrEmpty(item.ContextProcessName) && 
                item.ContextProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase));

            if (appPage == null)
            {
                appPage = new RadialMenuPageSettings
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"{_activeProcessName}",
                    ContextProcessName = normalizedProcess,
                    ContextDisplayName = _activeProcessName,
                    Slots = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
                    SlotTitles = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
                    ChildPageIds = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
                };
                settings.RadialMenu.Pages.Add(appPage);
                AppSettingsStore.Save(settings);
                _mainWindow.RefreshAppSettings();
                _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");
            }
        }

        var normalizedProcessForFilter = string.IsNullOrWhiteSpace(_activeProcessName)
            ? ""
            : _activeProcessName.Trim().ToLowerInvariant().Replace(".exe", "");

        var filteredPages = settings.RadialMenu.Pages.Where(page =>
            string.IsNullOrEmpty(page.ContextProcessName) ||
            (!string.IsNullOrEmpty(normalizedProcessForFilter) && page.ContextProcessName.Equals(normalizedProcessForFilter, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        var appPages = filteredPages.Where(page => !string.IsNullOrEmpty(page.ContextProcessName)).ToList();
        var globalPages = filteredPages.Where(page => string.IsNullOrEmpty(page.ContextProcessName)).ToList();

        var sortedPages = new List<RadialMenuPageSettings>();
        sortedPages.AddRange(appPages);
        sortedPages.AddRange(globalPages);

        _pages = sortedPages;

        while (PaginationDots.Count < _pages.Count)
        {
            PaginationDots.Add(new PaginationDotViewModel { IsSelected = false });
        }
        while (PaginationDots.Count > _pages.Count)
        {
            PaginationDots.RemoveAt(PaginationDots.Count - 1);
        }
    }

    public void ShowAtMouse()
    {
        _isExecuting = false;
        _editModeLocked = false;
        _editInteractionActive = false;
        _selectionTimer.Stop();
        var settings = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        _lastRadiusPixels = settings.RadiusPixels;
        _previousForegroundWindow = RadialMenuNativeMethods.GetForegroundWindow();
        _activeProcessName = null;
        if (_previousForegroundWindow != IntPtr.Zero)
        {
            try
            {
                RadialMenuNativeMethods.GetWindowThreadProcessId(_previousForegroundWindow, out var processId);
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                _activeProcessName = process.ProcessName;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"RadialMenu: Failed to get process name: {ex.Message}");
            }
        }
        LoadRadialMenuPages();
        var firstAppPage = _pages.FirstOrDefault(page => !string.IsNullOrEmpty(page.ContextProcessName));
        if (firstAppPage != null)
        {
            _currentPageId = firstAppPage.Id;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(settings.SelectedPageId) && 
                _pages.Any(p => p.Id.Equals(settings.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                _currentPageId = settings.SelectedPageId;
            }
            else
            {
                _currentPageId = _pages.FirstOrDefault()?.Id ?? string.Empty;
            }
        }
        _pageStack.Clear();
        _centerPixels = Forms.Cursor.Position;

        // First process launch can show before WPF finishes measuring this transparent
        // window. Keep it hidden until the HWND/DPI transform and ActualWidth are stable.
        Opacity = 0;
        BuildItems(_lastRadiusPixels);
        PositionAroundCursor();
        UpdateCenterText();
        ActiveTitle = "取消";
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionAroundCursor();
        BuildItems(_lastRadiusPixels);
        _ = Dispatcher.InvokeAsync(() =>
        {
            PositionAroundCursor();
            BuildItems(_lastRadiusPixels);
            Opacity = 1;
            Activate();
            _selectionTimer.Start();
            UpdateSelectionFromCursor();
        }, DispatcherPriority.Render);
        HostAssets.AppendLog($"Radial menu shown: page={_currentPageId}, items={Items.Count}, center=({_centerPixels.X},{_centerPixels.Y}).");
    }

    private void RebuildItemsForCurrentLayout(string reason)
    {
        if (string.IsNullOrWhiteSpace(_currentPageId))
        {
            return;
        }

        BuildItems(_lastRadiusPixels);
        HostAssets.AppendLog($"Radial menu layout rebuilt: reason={reason}, size=({ActualWidth:0.##},{ActualHeight:0.##}), page={_currentPageId}.");
    }

    private void PositionAroundCursor()
    {
        var source = PresentationSource.FromVisual(this) ?? PresentationSource.FromVisual(_mainWindow);
        var centerDips = source?.CompositionTarget?.TransformFromDevice.Transform(
            new System.Windows.Point(_centerPixels.X, _centerPixels.Y)) ?? new System.Windows.Point(_centerPixels.X, _centerPixels.Y);
        var size = GetMenuSize();
        Left = centerDips.X - size.Width / 2;
        Top = centerDips.Y - size.Height / 2;
    }

    private System.Windows.Size GetMenuSize()
    {
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;
        return new System.Windows.Size(width, height);
    }

    private System.Windows.Point GetMenuCenter()
    {
        var size = GetMenuSize();
        return new System.Windows.Point(size.Width / 2, size.Height / 2);
    }

    public void ExecuteSelectedFromHoldRelease()
    {
        if (_isExecuting)
        {
            return;
        }

        if (_editModeLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _editModeLocked = true;
            LoadRadialMenuPages();
            _selectionTimer.Stop();
            UpdateEditModeState();
            OpenEditMenuForCurrentSelection();
            return;
        }

        _selectionTimer.Stop();
        if (IsEditHoverActive)
        {
            ToggleEditModeState();
            _selectionTimer.Start();
            UpdateSelectionFromCursor();
            return;
        }

        if (IsPinHoverActive)
        {
            TogglePinnedState();
            _selectionTimer.Start();
            UpdateSelectionFromCursor();
            return;
        }

        if (IsAddHoverActive)
        {
            ShowAddRadialMenuContextMenu();
            return;
        }

        var selected = _selectedItem;
        var selectedChild = _selectedChildItem;
        var selectedGrandChild = _selectedGrandChildItem;

        if (TryHandleEmptySlotRelease(selectedGrandChild) ||
            TryHandleEmptySlotRelease(selectedChild) ||
            TryHandleEmptySlotRelease(selected))
        {
            return;
        }

        HideIfAllowed();
        if (selectedGrandChild?.Command != null)
        {
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing grandchild: index={selectedGrandChild.Index}, command={selectedGrandChild.Command.Title}.");
            _ = ExecuteCommandAfterForegroundRestoreAsync(selectedGrandChild.Command, "radial-menu-grandchild");
            return;
        }

        if (selectedChild?.Command != null)
        {
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing child: index={selectedChild.Index}, command={selectedChild.Command.Title}.");
            _ = ExecuteCommandAfterForegroundRestoreAsync(selectedChild.Command, "radial-menu-child");
            return;
        }

        if (selected == null)
        {
            HostAssets.AppendLog("Radial menu release: no selected command.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(selected.ChildPageId))
        {
            HostAssets.AppendLog($"Radial menu release: parent child slot selected without child command, childPage={selected.ChildPageId}.");
            return;
        }

        if (selected.Command == null)
        {
            HostAssets.AppendLog("Radial menu release: selected empty slot.");
            return;
        }

        _isExecuting = true;
        HostAssets.AppendLog($"Radial menu executing: index={selected.Index}, command={selected.Command.Title}.");
        _ = ExecuteCommandAfterForegroundRestoreAsync(selected.Command, "radial-menu");
    }

    private async Task ExecuteCommandAfterForegroundRestoreAsync(CommandItem command, string launchSource)
    {
        InputHookService.MarkCapsLockAsUsed();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            if (_previousForegroundWindow != IntPtr.Zero)
            {
                var restored = RadialMenuNativeMethods.SetForegroundWindow(_previousForegroundWindow);
                HostAssets.AppendLog($"Radial menu restore foreground: restored={restored}, {DescribeWindow(_previousForegroundWindow)}.");
            }

            var currentForeground = RadialMenuNativeMethods.GetForegroundWindow();
            HostAssets.AppendLog($"Radial menu execute ready: foreground={DescribeWindow(currentForeground)}, source={launchSource}, command={command.Title}.");
            var input = string.Empty;
            if (command.ShouldCaptureSelectedInput)
            {
                await Task.Delay(120);
                input = await SelectionCaptureService.CaptureSelectedInputAsync();
                HostAssets.AppendLog($"Radial menu execute captured input length={input.Length}.");
            }
            else
            {
                HostAssets.AppendLog("Radial menu execute: selection capture skipped for command without context input.");
            }

            _mainWindow.ExecuteCommandExternally(command, input, launchSource);
        }
        finally
        {
            _isExecuting = false;
            if (_isPinned && IsVisible)
            {
                _selectionTimer.Start();
                UpdateSelectionFromCursor();
            }
        }
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "hwnd=0x0";
        }

        var titleBuilder = new StringBuilder(256);
        _ = RadialMenuNativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = RadialMenuNativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return $"hwnd=0x{hwnd.ToInt64():X}, pid={processId}, title=\"{titleBuilder}\"";
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_editModeLocked || _editInteractionActive)
        {
            return;
        }

        _selectionTimer.Stop();
        HideIfAllowed();
    }

    private void BuildItems(int radius)
    {
        var effectiveRadius = Math.Clamp(radius - 10, 82, 96);
        Items.Clear();
        OuterItems.Clear();
        ChildItems.Clear();
        GrandChildItems.Clear();
        ClearChildSelection();
        ClearGrandChildSelection();
        HasChildRing = false;
        HasGrandChildRing = false;
        IsPinHoverActive = false;
        SetSelectedItem(null);
        var items = _mainWindow.GetRadialMenuItems(_currentPageId, _activeProcessName);
        var page = _pages.FirstOrDefault(item => item.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        PageTitle = page?.Name ?? "燕环";

        if (page != null && !string.IsNullOrEmpty(page.ContextProcessName))
        {
            CenterIcon = GetProcessIcon(page.ContextProcessName);
        }
        else
        {
            CenterIcon = null;
        }

        var pageName = page?.Name ?? "默认";
        var appName = string.IsNullOrWhiteSpace(_activeProcessName) ? "全局" : _activeProcessName;
        PageDisplaySummary = $"轮盘: {pageName}  |  当前应用: {appName}";

        UpdateCenterText();
        var center = GetMenuCenter();
        BuildSeparators(MainSeparators, center.X, center.Y, 30, 122, RadialMenuSettings.InnerSlotCount);
        BuildSeparators(OuterSeparators, center.X, center.Y, 122, 196, RadialMenuSettings.OuterSlotCount);
        for (var index = 0; index < RadialMenuSettings.InnerSlotCount; index++)
        {
            var angleDegrees = -90 + index * 45.0;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * Math.Clamp(effectiveRadius - 6, 74, 88) - 38;
            var y = center.Y + Math.Sin(angle) * Math.Clamp(effectiveRadius - 6, 74, 88) - 30;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            Items.Add(new RadialMenuItemViewModel(
                _currentPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                angleDegrees,
                RadialMenuRing.Inner,
                CreateSectorGeometry(center.X, center.Y, 29, 122, angleDegrees - 22.5, angleDegrees + 22.5)));
        }

        for (var offset = 0; offset < RadialMenuSettings.OuterSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + offset;
            var angleDegrees = -90 + offset * 22.5;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 162 - 31;
            var y = center.Y + Math.Sin(angle) * 162 - 25;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            OuterItems.Add(new RadialMenuItemViewModel(
                _currentPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                angleDegrees,
                RadialMenuRing.Outer,
                CreateSectorGeometry(center.X, center.Y, 122, 196, angleDegrees - 11.25, angleDegrees + 11.25)));
        }

        var currentIndex = _pages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < PaginationDots.Count; i++)
        {
            PaginationDots[i].IsSelected = (i == currentIndex);
        }
    }

    private void UpdateSelectionFromCursor()
    {
        var cursorPoint = GetCursorWindowPoint();
        if (UpdateEditHoverState(cursorPoint))
        {
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdatePinHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsAddHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdateAddHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsPinHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (HasGrandChildRing && TryUpdateGrandChildSelection(cursorPoint))
        {
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (HasChildRing && TryUpdateChildSelection(cursorPoint))
        {
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        var center = GetMenuCenter();
        var dx = cursorPoint.X - center.X;
        var dy = cursorPoint.Y - center.Y;
        var settings = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        UpdateEditModeState();
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < settings.DeadZonePixels)
        {
            SetSelectedItem(null);
            ClearChildRing();
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "取消";
            IsCenterHovered = true;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        IsCenterHovered = false;

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (distance > 196)
        {
            SetSelectedItem(null);
            ClearChildRing();
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "取消";
            Cursor = System.Windows.Input.Cursors.Arrow;
            return;
        }

        Cursor = System.Windows.Input.Cursors.Hand;

        if (distance > 135)
        {
            var outerIndex = ((int)Math.Round((angle + 90) / 22.5) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
            var outerItem = OuterItems.ElementAtOrDefault(outerIndex);
            SetSelectedItem(outerItem);
            ActiveTitle = ResolveActiveTitle(outerItem?.Command?.Title, outerItem?.Command == null);
            if (!string.IsNullOrWhiteSpace(outerItem?.ChildPageId))
            {
                ActiveTitle = _editModeLocked ? $"松开可编辑：{outerItem.ChildPageTitle}" : $"展开：{outerItem.ChildPageTitle}";
                BuildChildRing(outerItem);
            }
            else
            {
                ClearChildRing();
            }

            return;
        }

        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = Items.ElementAtOrDefault(index);
        SetSelectedItem(item);
        ActiveTitle = ResolveActiveTitle(item?.Command?.Title, item?.Command == null);
        if (!string.IsNullOrWhiteSpace(item?.ChildPageId))
        {
            ActiveTitle = _editModeLocked ? $"松开可编辑：{item.ChildPageTitle}" : $"展开：{item.ChildPageTitle}";
            BuildChildRing(item);
        }
        else
        {
            ClearChildRing();
        }
    }

    private System.Windows.Point GetCursorWindowPoint()
    {
        var cursor = Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        var screenDips = source?.CompositionTarget?.TransformFromDevice.Transform(
            new System.Windows.Point(cursor.X, cursor.Y)) ?? new System.Windows.Point(cursor.X, cursor.Y);
        return new System.Windows.Point(screenDips.X - Left, screenDips.Y - Top);
    }

    private bool TryUpdateChildSelection(System.Windows.Point cursorPoint)
    {
        var dx = cursorPoint.X - _childRingCenterX;
        var dy = cursorPoint.Y - _childRingCenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 150)
        {
            ClearChildSelection();
            ClearGrandChildRing();
            return false;
        }

        if (distance < 26)
        {
            ClearChildSelection();
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "返回上一级";
            return true;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = ChildItems.ElementAtOrDefault(index);
        SetSelectedChildItem(item);
        ActiveTitle = ResolveActiveTitle(item?.Command?.Title, item?.Command == null, isChildRing: true);
        if (!string.IsNullOrWhiteSpace(item?.ChildPageId))
        {
            ActiveTitle = _editModeLocked ? $"松开可编辑：{item.ChildPageTitle}" : $"展开：{item.ChildPageTitle}";
            BuildGrandChildRing(item);
        }
        else
        {
            ClearGrandChildRing();
        }
        return true;
    }

    private bool TryUpdateGrandChildSelection(System.Windows.Point cursorPoint)
    {
        var dx = cursorPoint.X - _grandChildRingCenterX;
        var dy = cursorPoint.Y - _grandChildRingCenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 138)
        {
            ClearGrandChildSelection();
            return false;
        }

        if (distance < 24)
        {
            ClearGrandChildSelection();
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "返回上一级";
            return true;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = GrandChildItems.ElementAtOrDefault(index);
        SetSelectedGrandChildItem(item);
        ActiveTitle = ResolveActiveTitle(item?.Command?.Title, item?.Command == null, isGrandChildRing: true);
        return true;
    }

    private void RadialMenuWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pages.Count <= 1)
        {
            return;
        }

        var currentIndex = Math.Max(0, _pages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase)));
        var delta = e.Delta < 0 ? 1 : -1;
        var nextIndex = (currentIndex + delta + _pages.Count) % _pages.Count;
        _currentPageId = _pages[nextIndex].Id;
        _pageStack.Clear();
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        e.Handled = true;
    }

    private void EnterChildPage(string childPageId)
    {
        if (_pages.All(page => !page.Id.Equals(childPageId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _pageStack.Push(_currentPageId);
        _currentPageId = childPageId;
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        HostAssets.AppendLog($"Radial menu entered child page: {childPageId}.");
    }

    private void ReturnToParentPage()
    {
        if (_pageStack.Count == 0)
        {
            return;
        }

        _currentPageId = _pageStack.Pop();
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
    }

    private string ResolvePageName(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return string.Empty;
        }

        return _pages.FirstOrDefault(page => page.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? pageId;
    }

    private void SetSelectedItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            return;
        }

        if (_selectedItem != null)
        {
            _selectedItem.IsSelected = false;
        }

        _selectedItem = item;
        if (_selectedItem != null)
        {
            _selectedItem.IsSelected = true;
        }
    }

    private void SetSelectedForItem(RadialMenuItemViewModel item)
    {
        if (item.Ring is RadialMenuRing.Child)
        {
            SetSelectedChildItem(item);
            return;
        }

        if (item.Ring is RadialMenuRing.GrandChild)
        {
            SetSelectedGrandChildItem(item);
            return;
        }

        SetSelectedItem(item);
    }

    private void BuildChildRing(RadialMenuItemViewModel parent)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearChildRing();
            return;
        }

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);
        var angle = parent.AngleDegrees * Math.PI / 180.0;
        var center = GetMenuCenter();
        _childRingCenterX = center.X + Math.Cos(angle) * 250;
        _childRingCenterY = center.Y + Math.Sin(angle) * 250;
        ClampRingCenter(ref _childRingCenterX, ref _childRingCenterY, 134);
        ChildRingTitle = parent.ChildPageTitle;
        OnPropertyChanged(nameof(ChildRingEllipseX));
        OnPropertyChanged(nameof(ChildRingEllipseY));
        OnPropertyChanged(nameof(ChildRingCenterEllipseX));
        OnPropertyChanged(nameof(ChildRingCenterEllipseY));
        OnPropertyChanged(nameof(ChildRingTitleX));
        OnPropertyChanged(nameof(ChildRingTitleY));
        BuildSeparators(ChildSeparators, _childRingCenterX, _childRingCenterY, 32, 120, RadialMenuSettings.InnerSlotCount);

        ChildItems.Clear();
        const double radius = 78;
        for (var index = 0; index < 8; index++)
        {
            var childAngleDegrees = -90 + index * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = _childRingCenterX + Math.Cos(childAngle) * radius - 34;
            var y = _childRingCenterY + Math.Sin(childAngle) * radius - 27;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            ChildItems.Add(new RadialMenuItemViewModel(
                parent.ChildPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.Child,
                CreateSectorGeometry(_childRingCenterX, _childRingCenterY, 32, 120, childAngleDegrees - 22.5, childAngleDegrees + 22.5)));
        }

        HasChildRing = true;
    }

    private void BuildGrandChildRing(RadialMenuItemViewModel parent)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearGrandChildRing();
            return;
        }

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);
        var angle = parent.AngleDegrees * Math.PI / 180.0;
        _grandChildRingCenterX = _childRingCenterX + Math.Cos(angle) * 216;
        _grandChildRingCenterY = _childRingCenterY + Math.Sin(angle) * 216;
        ClampRingCenter(ref _grandChildRingCenterX, ref _grandChildRingCenterY, 112);
        GrandChildRingTitle = parent.ChildPageTitle;
        OnPropertyChanged(nameof(GrandChildRingEllipseX));
        OnPropertyChanged(nameof(GrandChildRingEllipseY));
        OnPropertyChanged(nameof(GrandChildRingCenterEllipseX));
        OnPropertyChanged(nameof(GrandChildRingCenterEllipseY));
        OnPropertyChanged(nameof(GrandChildRingTitleX));
        OnPropertyChanged(nameof(GrandChildRingTitleY));
        BuildSeparators(GrandChildSeparators, _grandChildRingCenterX, _grandChildRingCenterY, 27, 98, RadialMenuSettings.InnerSlotCount);

        GrandChildItems.Clear();
        const double radius = 64;
        for (var index = 0; index < 8; index++)
        {
            var childAngleDegrees = -90 + index * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = _grandChildRingCenterX + Math.Cos(childAngle) * radius - 31;
            var y = _grandChildRingCenterY + Math.Sin(childAngle) * radius - 25;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            GrandChildItems.Add(new RadialMenuItemViewModel(
                parent.ChildPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.GrandChild,
                CreateSectorGeometry(_grandChildRingCenterX, _grandChildRingCenterY, 27, 98, childAngleDegrees - 22.5, childAngleDegrees + 22.5)));
        }

        HasGrandChildRing = true;
    }

    private void ClearChildRing()
    {
        ClearChildSelection();
        ChildItems.Clear();
        ChildSeparators.Clear();
        ClearGrandChildRing();
        HasChildRing = false;
        ChildRingTitle = string.Empty;
    }

    private void ClearGrandChildRing()
    {
        ClearGrandChildSelection();
        GrandChildItems.Clear();
        GrandChildSeparators.Clear();
        HasGrandChildRing = false;
        GrandChildRingTitle = string.Empty;
    }

    private void SetSelectedChildItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_selectedChildItem, item))
        {
            return;
        }

        ClearChildSelection();
        _selectedChildItem = item;
        if (_selectedChildItem != null)
        {
            _selectedChildItem.IsSelected = true;
        }
    }

    private void ClearChildSelection()
    {
        if (_selectedChildItem != null)
        {
            _selectedChildItem.IsSelected = false;
            _selectedChildItem = null;
        }
    }

    private void SetSelectedGrandChildItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_selectedGrandChildItem, item))
        {
            return;
        }

        ClearGrandChildSelection();
        _selectedGrandChildItem = item;
        if (_selectedGrandChildItem != null)
        {
            _selectedGrandChildItem.IsSelected = true;
        }
    }

    private void ClearGrandChildSelection()
    {
        if (_selectedGrandChildItem != null)
        {
            _selectedGrandChildItem.IsSelected = false;
            _selectedGrandChildItem = null;
        }
    }

    private static void BuildSeparators(ObservableCollection<RadialSeparatorViewModel> target, double centerX, double centerY, double innerRadius, double outerRadius, int count)
    {
        target.Clear();
        var step = 360.0 / count;
        for (var index = 0; index < count; index++)
        {
            var angle = (-90 - step / 2 + index * step) * Math.PI / 180.0;
            target.Add(new RadialSeparatorViewModel(
                centerX + Math.Cos(angle) * innerRadius,
                centerY + Math.Sin(angle) * innerRadius,
                centerX + Math.Cos(angle) * outerRadius,
                centerY + Math.Sin(angle) * outerRadius));
        }
    }

    private static Geometry CreateSectorGeometry(double centerX, double centerY, double innerRadius, double outerRadius, double startAngleDegrees, double endAngleDegrees)
    {
        static System.Windows.Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
        {
            var radians = angleDegrees * Math.PI / 180.0;
            return new System.Windows.Point(
                cx + Math.Cos(radians) * radius,
                cy + Math.Sin(radians) * radius);
        }

        var outerStart = PointOnCircle(centerX, centerY, outerRadius, startAngleDegrees);
        var outerEnd = PointOnCircle(centerX, centerY, outerRadius, endAngleDegrees);
        var innerEnd = PointOnCircle(centerX, centerY, innerRadius, endAngleDegrees);
        var innerStart = PointOnCircle(centerX, centerY, innerRadius, startAngleDegrees);
        var isLargeArc = Math.Abs(endAngleDegrees - startAngleDegrees) > 180.0;

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment(outerEnd, new System.Windows.Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new System.Windows.Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private void ClampRingCenter(ref double x, ref double y, double radius)
    {
        var size = GetMenuSize();
        x = Math.Clamp(x, radius + 8, size.Width - radius - 8);
        y = Math.Clamp(y, radius + 8, size.Height - radius - 8);
    }

    private void RadialMenuWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editModeLocked && IsPointInCenter(e.GetPosition(this)))
        {
            _editModeLocked = false;
            _selectionTimer.Stop();
            Hide();
            e.Handled = true;
            return;
        }

        ReturnToParentPage();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePinnedState();
        e.Handled = true;
    }

    private void RadialMenuWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editModeLocked)
        {
            return;
        }

        if (TryRenameChildPageCenter(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        OpenEditMenuForCurrentSelection();
        e.Handled = true;
    }

    private void RadialSlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            return;
        }

        SetSelectedForItem(item);

        if (_editModeLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _editModeLocked = true;
            LoadRadialMenuPages();
            _selectionTimer.Stop();
            UpdateEditModeState();
            var target = new RadialEditTarget(item.OwnerPageId, item.Index, item);
            if (item.Command == null && !item.HasChildPage)
            {
                AddCommandToTarget(target);
            }
            else
            {
                OpenEditMenuForTarget(target);
            }

            e.Handled = true;
            return;
        }

        if (item.Command != null)
        {
            _selectionTimer.Stop();
            HideIfAllowed();
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu clicked execute: index={item.Index}, command={item.Command.Title}, ring={item.Ring}.");
            _ = ExecuteCommandAfterForegroundRestoreAsync(item.Command, "radial-menu-click");
            e.Handled = true;
            return;
        }

        if (item.Command == null && !item.HasChildPage)
        {
            TryHandleEmptySlotRelease(item);
            e.Handled = true;
        }
    }

    private void RadialSlot_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editModeLocked || sender is not FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            return;
        }

        SetSelectedForItem(item);
        OpenEditMenuForTarget(new RadialEditTarget(item.OwnerPageId, item.Index, item));
        e.Handled = true;
    }

    private void RadialSlot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_editModeLocked ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            return;
        }

        _dragSourceItem = item;
        DragDrop.DoDragDrop((DependencyObject)sender, item, System.Windows.DragDropEffects.Move);
    }

    private void RadialSlot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!_editModeLocked)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(RadialMenuItemViewModel)))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = TryGetDroppedFilePaths(e, out _)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void RadialSlot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!_editModeLocked ||
            sender is not FrameworkElement { DataContext: RadialMenuItemViewModel target })
        {
            return;
        }

        if (e.Data.GetData(typeof(RadialMenuItemViewModel)) is RadialMenuItemViewModel source)
        {
            MoveRadialSlot(new RadialEditTarget(source.OwnerPageId, source.Index, source), new RadialEditTarget(target.OwnerPageId, target.Index, target));
            _dragSourceItem = null;
            e.Handled = true;
            return;
        }

        if (TryGetDroppedFilePaths(e, out var filePaths))
        {
            AddDroppedPathToRadialSlot(new RadialEditTarget(target.OwnerPageId, target.Index, target), filePaths);
        }

        e.Handled = true;
    }

    private void RadialSlot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            item.IsHovered = true;
        }
    }

    private void RadialSlot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            item.IsHovered = false;
        }
    }

    private static bool TryGetDroppedFilePaths(System.Windows.DragEventArgs e, out string[] filePaths)
    {
        filePaths = [];
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0)
        {
            return false;
        }

        filePaths = droppedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return filePaths.Length > 0;
    }

    private bool IsPointInCenter(System.Windows.Point point)
    {
        var center = GetMenuCenter();
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= 40;
    }

    private bool TryRenameChildPageCenter(System.Windows.Point point)
    {
        if (HasGrandChildRing && IsPointNear(point, _grandChildRingCenterX, _grandChildRingCenterY, 34) && _selectedChildItem?.HasChildPage == true)
        {
            RenameRadialPage(_selectedChildItem.ChildPageId);
            return true;
        }

        if (HasChildRing && IsPointNear(point, _childRingCenterX, _childRingCenterY, 40) && _selectedItem?.HasChildPage == true)
        {
            RenameRadialPage(_selectedItem.ChildPageId);
            return true;
        }

        return false;
    }

    private static bool IsPointNear(System.Windows.Point point, double centerX, double centerY, double radius)
    {
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        return Math.Sqrt(dx * dx + dy * dy) <= radius;
    }

    private void RenameRadialPage(string pageId)
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var page = settings.RadialMenu.Pages.FirstOrDefault(item => item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名子环", "输入新的子环名称。", page.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = dialog.ValueText.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        page.Name = name;
        PersistRadialSettings(settings);
        BuildItems((settings.RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        ActiveTitle = $"已重命名：{name}";
    }

    private void UpdateEditModeState()
    {
        CenterPrimaryText = PageTitle;
        IsCenterCloseMode = false;
    }

    private void UpdateCenterText()
    {
        CenterPrimaryText = PageTitle;
        IsCenterCloseMode = false;
    }

    private string ResolveActiveTitle(string? commandTitle, bool isEmptySlot, bool isChildRing = false, bool isGrandChildRing = false)
    {
        if (!_editModeLocked && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return commandTitle ?? (isGrandChildRing ? "松开可新建" : isChildRing ? "松开可新建" : "松开可新建");
        }

        if (isEmptySlot)
        {
            return "松开可新建";
        }

        return $"松开可修改：{commandTitle}";
    }

    private void OpenEditMenuForCurrentSelection()
    {
        var target = ResolveCurrentEditTarget();
        if (target == null)
        {
            ActiveTitle = "点击中心 X 关闭";
            _selectionTimer.Start();
            return;
        }

        OpenEditMenuForTarget(target);
    }

    private void OpenEditMenuForTarget(RadialEditTarget target)
    {
        _editInteractionActive = true;
        var menu = BuildEditContextMenu(target);
        menu.PlacementTarget = this;
        menu.Closed += (_, _) =>
        {
            _editInteractionActive = false;
            if (IsVisible)
            {
                Activate();
                _selectionTimer.Start();
            }
        };

        menu.Placement = PlacementMode.AbsolutePoint;
        var cursor = Forms.Cursor.Position;
        menu.HorizontalOffset = cursor.X;
        menu.VerticalOffset = cursor.Y;
        menu.IsOpen = true;
        ActiveTitle = "编辑当前槽位";
        HostAssets.AppendLog($"Radial edit menu opened: page={target.PageId}, index={target.Index + 1}, hasCommand={target.Item.Command != null}, hasChild={target.Item.HasChildPage}.");
    }

    private ContextMenu BuildEditContextMenu(RadialEditTarget target)
    {
        var menu = new ContextMenu();

        var addCommandItem = new MenuItem { Header = "添加扩展/应用/系统项" };
        addCommandItem.Click += (_, _) => AddCommandToTarget(target);
        menu.Items.Add(addCommandItem);

        var setSimulatedKeyItem = new MenuItem { Header = "设置模拟按键" };
        setSimulatedKeyItem.Click += (_, _) => SetSimulatedKeyForTarget(target);
        menu.Items.Add(setSimulatedKeyItem);

        var clearCommandItem = new MenuItem
        {
            Header = "删除扩展/应用/系统项",
            IsEnabled = target.Item.Command != null
        };
        clearCommandItem.Click += (_, _) => ClearCommandFromTarget(target);
        menu.Items.Add(clearCommandItem);

        var cutItem = new MenuItem
        {
            Header = "剪切槽位",
            IsEnabled = target.Item.Command != null || target.Item.HasChildPage
        };
        cutItem.Click += (_, _) => CutRadialSlot(target);
        menu.Items.Add(cutItem);

        var pasteItem = new MenuItem
        {
            Header = "粘贴到此槽位",
            IsEnabled = _cutSlotPayload != null
        };
        pasteItem.Click += (_, _) => PasteRadialSlot(target);
        menu.Items.Add(pasteItem);

        menu.Items.Add(new Separator());

        var addChildItem = new MenuItem
        {
            Header = "添加子环",
            IsEnabled = !target.Item.HasChildPage
        };
        addChildItem.Click += (_, _) => AddChildPageToTarget(target);
        menu.Items.Add(addChildItem);

        var clearChildItem = new MenuItem
        {
            Header = "删除子环",
            IsEnabled = target.Item.HasChildPage
        };
        clearChildItem.Click += (_, _) => ClearChildPageFromTarget(target);
        menu.Items.Add(clearChildItem);

        if (!string.IsNullOrWhiteSpace(_activeProcessName))
        {
            menu.Items.Add(new Separator());
            bool isBound = IsRadialSlotBoundToCurrentApp(target);
            if (isBound)
            {
                var unbindItem = new MenuItem { Header = $"取消绑定 (当前应用: {_activeProcessName})" };
                unbindItem.Click += (_, _) => UnbindRadialSlotFromCurrentApp(target);
                menu.Items.Add(unbindItem);
            }
            else
            {
                var bindItem = new MenuItem 
                { 
                    Header = $"绑定到当前应用: {_activeProcessName}",
                    IsEnabled = target.Item.Command != null || target.Item.HasChildPage
                };
                bindItem.Click += (_, _) => BindRadialSlotToCurrentApp(target);
                menu.Items.Add(bindItem);
            }
        }

        return menu;
    }

    private void AddCommandToTarget(RadialEditTarget target)
    {
        var picker = new RadialSlotPickerWindow(
            keyword => _mainWindow.GetRadialMenuCommandCandidates(keyword),
            allowAddChildPage: !target.Item.HasChildPage,
            createExtension: owner => _mainWindow.OpenAddExtensionForSlot(owner))
        {
            Owner = this
        };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        if (picker.SelectedAction == RadialSlotPickerWindow.PickerAction.AddChildPage)
        {
            AddChildPageToTarget(target);
            return;
        }

        if (picker.SelectedCommand == null)
        {
            return;
        }

        SaveRadialSlotCommand(target.PageId, target.Index, picker.SelectedCommand.ExtensionId, string.Empty);
        HostAssets.AppendLog($"Radial edit assigned command: page={target.PageId}, index={target.Index + 1}, command={picker.SelectedCommand.Title}.");
    }

    private void SetSimulatedKeyForTarget(RadialEditTarget target)
    {
        const string simulatedPrefix = "keysim::";
        var currentExtensionId = target.Item.Command?.ExtensionId ?? string.Empty;
        var initialShortcut = currentExtensionId.StartsWith(simulatedPrefix, StringComparison.OrdinalIgnoreCase)
            ? currentExtensionId[simulatedPrefix.Length..]
            : string.Empty;
        var initialDisplayName = target.Item.Command?.Title ?? string.Empty;
        var dialog = new HotkeyCaptureWindow(
            "模拟按键",
            "录制要在此槽位执行的组合键，并设置轮盘里显示的名称。",
            initialShortcut,
            initialDisplayName,
            allowEmpty: false,
            allowDoubleTap: false,
            allowModifierless: true)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var shortcut = dialog.ShortcutText.Trim();
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return;
        }

        var displayTitle = string.IsNullOrWhiteSpace(dialog.DisplayNameText) ? shortcut : dialog.DisplayNameText.Trim();
        SaveRadialSlotCommand(target.PageId, target.Index, $"{simulatedPrefix}{shortcut}", displayTitle);
        HostAssets.AppendLog($"Radial edit assigned simulated key: page={target.PageId}, index={target.Index + 1}, shortcut={shortcut}, displayTitle={displayTitle}.");
    }

    private void ClearCommandFromTarget(RadialEditTarget target)
    {
        SaveRadialSlotCommand(target.PageId, target.Index, null, null);
    }

    private void CutRadialSlot(RadialEditTarget target)
    {
        var payload = ReadRadialSlotPayload(target.PageId, target.Index);
        if (payload == null)
        {
            return;
        }

        _cutSlotPayload = payload;
        WriteRadialSlotPayload(target.PageId, target.Index, new RadialSlotPayload(null, null, null));
        ActiveTitle = "已剪切槽位，右键目标槽位粘贴";
    }

    private void PasteRadialSlot(RadialEditTarget target)
    {
        if (_cutSlotPayload == null)
        {
            return;
        }

        WriteRadialSlotPayload(target.PageId, target.Index, _cutSlotPayload);
        _cutSlotPayload = null;
        ActiveTitle = "已粘贴槽位";
    }

    private void MoveRadialSlot(RadialEditTarget source, RadialEditTarget target)
    {
        if (source.PageId.Equals(target.PageId, StringComparison.OrdinalIgnoreCase) && source.Index == target.Index)
        {
            return;
        }

        var sourcePayload = ReadRadialSlotPayload(source.PageId, source.Index);
        var targetPayload = ReadRadialSlotPayload(target.PageId, target.Index);
        if (sourcePayload == null)
        {
            return;
        }

        WriteRadialSlotPayload(target.PageId, target.Index, sourcePayload, refresh: false);
        WriteRadialSlotPayload(source.PageId, source.Index, targetPayload ?? new RadialSlotPayload(null, null, null));
        ActiveTitle = "已移动槽位";
    }

    private void AddDroppedPathToRadialSlot(RadialEditTarget target, IEnumerable<string> filePaths)
    {
        var firstPath = filePaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstPath))
        {
            return;
        }

        var currentPayload = ReadRadialSlotPayload(target.PageId, target.Index);
        var hasExisting = !string.IsNullOrWhiteSpace(currentPayload?.ExtensionId) ||
                          !string.IsNullOrWhiteSpace(currentPayload?.ChildPageId);
        if (hasExisting)
        {
            var confirm = System.Windows.MessageBox.Show(
                this,
                $"槽位 {target.Index + 1} 已有内容，是否用“{Path.GetFileName(firstPath)}”替换？",
                "替换轮盘槽位",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                ActiveTitle = "已取消替换";
                return;
            }
        }

        try
        {
            var command = _mainWindow.CreateQuickOpenExtensionFromPath(firstPath);
            _mainWindow.MarkExtensionAsNewFromQuickPanel(command);
            WriteRadialSlotPayload(
                target.PageId,
                target.Index,
                new RadialSlotPayload(command.ExtensionId, null, null));
            ActiveTitle = $"已添加：{command.Title}";
            HostAssets.AppendLog($"Radial dropped path assigned: page={target.PageId}, index={target.Index + 1}, path={firstPath}, command={command.ExtensionId}.");
        }
        catch (Exception ex)
        {
            ActiveTitle = "拖拽添加失败";
            _mainWindow.SyncStatus = $"拖拽添加到轮盘失败：{Path.GetFileName(firstPath)}，{ex.Message}";
            HostAssets.AppendLog($"Radial dropped path assign failed: page={target.PageId}, index={target.Index + 1}, path={firstPath}, error={ex}");
        }
    }

    private void AddChildPageToTarget(RadialEditTarget target)
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var page = settings.RadialMenu.Pages.FirstOrDefault(item => item.Id.Equals(target.PageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            return;
        }

        EnsureRadialPageSlotCapacity(page);

        if (!string.IsNullOrWhiteSpace(page.ChildPageIds[target.Index]))
        {
            return;
        }

        var childPage = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = GetNextRadialChildPageName(settings)
        };
        settings.RadialMenu.Pages.Add(childPage);
        page.ChildPageIds[target.Index] = childPage.Id;
        PersistRadialSettings(settings);
        RefreshFromSettings(target.PageId, target.Index, ensureChildRingVisible: true);
    }

    private void ClearChildPageFromTarget(RadialEditTarget target)
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var page = settings.RadialMenu.Pages.FirstOrDefault(item => item.Id.Equals(target.PageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            return;
        }

        EnsureRadialPageSlotCapacity(page);

        var removedId = page.ChildPageIds[target.Index];
        page.ChildPageIds[target.Index] = null;
        if (!string.IsNullOrWhiteSpace(removedId) && settings.RadialMenu.Pages.Count > 1)
        {
            settings.RadialMenu.Pages.RemoveAll(item => item.Id.Equals(removedId, StringComparison.OrdinalIgnoreCase));
            foreach (var item in settings.RadialMenu.Pages)
            {
                item.ChildPageIds = (item.ChildPageIds ?? [])
                    .Select(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase) ? null : id)
                    .ToList();
            }
        }

        PersistRadialSettings(settings);
        RefreshFromSettings(target.PageId, target.Index, ensureChildRingVisible: false);
    }

    private void SaveRadialSlotCommand(string pageId, int index, string? extensionId, string? displayTitle)
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var page = settings.RadialMenu.Pages.FirstOrDefault(item => item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            return;
        }

        page.Slots ??= [];
        EnsureRadialPageSlotCapacity(page);

        page.Slots[index] = string.IsNullOrWhiteSpace(extensionId) ? null : extensionId.Trim();
        page.SlotTitles[index] = string.IsNullOrWhiteSpace(displayTitle) ? null : displayTitle.Trim();
        PersistRadialSettings(settings);
        RefreshFromSettings(pageId, index, ensureChildRingVisible: !string.IsNullOrWhiteSpace(page.ChildPageIds?.ElementAtOrDefault(index)));
    }

    private RadialSlotPayload? ReadRadialSlotPayload(string pageId, int index)
    {
        var settings = AppSettingsStore.Load();
        var page = settings.RadialMenu?.Pages?.FirstOrDefault(item => item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null || index < 0 || index >= RadialMenuSettings.TotalSlotCount)
        {
            return null;
        }

        EnsureRadialPageSlotCapacity(page);
        return new RadialSlotPayload(
            page.Slots.ElementAtOrDefault(index),
            page.SlotTitles.ElementAtOrDefault(index),
            page.ChildPageIds.ElementAtOrDefault(index));
    }

    private void WriteRadialSlotPayload(string pageId, int index, RadialSlotPayload payload, bool refresh = true)
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var page = settings.RadialMenu.Pages.FirstOrDefault(item => item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null || index < 0 || index >= RadialMenuSettings.TotalSlotCount)
        {
            return;
        }

        EnsureRadialPageSlotCapacity(page);
        page.Slots[index] = string.IsNullOrWhiteSpace(payload.ExtensionId) ? null : payload.ExtensionId.Trim();
        page.SlotTitles[index] = string.IsNullOrWhiteSpace(payload.DisplayTitle) ? null : payload.DisplayTitle.Trim();
        page.ChildPageIds[index] = string.IsNullOrWhiteSpace(payload.ChildPageId) ? null : payload.ChildPageId.Trim();
        PersistRadialSettings(settings);
        if (refresh)
        {
            RefreshFromSettings(pageId, index, ensureChildRingVisible: !string.IsNullOrWhiteSpace(page.ChildPageIds[index]));
        }
    }

    private static void EnsureRadialPageSlotCapacity(RadialMenuPageSettings page)
    {
        page.Slots ??= [];
        page.SlotTitles ??= [];
        page.ChildPageIds ??= [];
        while (page.Slots.Count < RadialMenuSettings.TotalSlotCount) page.Slots.Add(null);
        while (page.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) page.SlotTitles.Add(null);
        while (page.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) page.ChildPageIds.Add(null);
    }

    private void PersistRadialSettings(AppSettings settings)
    {
        AppSettingsStore.Save(settings);
        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");
        LoadRadialMenuPages();
    }

    private void RefreshFromSettings(string pageId, int index, bool ensureChildRingVisible)
    {
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        var target = Items.FirstOrDefault(item => item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase) && item.Index == index)
            ?? OuterItems.FirstOrDefault(item => item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase) && item.Index == index)
            ?? ChildItems.FirstOrDefault(item => item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase) && item.Index == index)
            ?? GrandChildItems.FirstOrDefault(item => item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase) && item.Index == index);
        if (target != null)
        {
            if (pageId.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase))
            {
                SetSelectedItem(target);
            }

            if (ensureChildRingVisible && target.HasChildPage)
            {
                BuildChildRing(target);
            }
        }

        UpdateEditModeState();
        ActiveTitle = "编辑已保存，点击中心 X 关闭";
        _selectionTimer.Start();
    }

    private RadialEditTarget? ResolveCurrentEditTarget()
    {
        if (_selectedGrandChildItem != null)
        {
            return new RadialEditTarget(_selectedGrandChildItem.OwnerPageId, _selectedGrandChildItem.Index, _selectedGrandChildItem);
        }

        if (_selectedChildItem != null)
        {
            return new RadialEditTarget(_selectedChildItem.OwnerPageId, _selectedChildItem.Index, _selectedChildItem);
        }

        if (_selectedItem != null)
        {
            return new RadialEditTarget(_selectedItem.OwnerPageId, _selectedItem.Index, _selectedItem);
        }

        return null;
    }

    private bool TryHandleEmptySlotRelease(RadialMenuItemViewModel? item)
    {
        if (item == null || item.Command != null || item.HasChildPage)
        {
            return false;
        }

        _editModeLocked = true;
        LoadRadialMenuPages();
        UpdateEditModeState();
        AddCommandToTarget(new RadialEditTarget(item.OwnerPageId, item.Index, item));
        return true;
    }

    private static string GetNextRadialChildPageName(AppSettings settings)
    {
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var usedNumbers = settings.RadialMenu.Pages
            .Select(page => page.Name ?? string.Empty)
            .Select(name =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(name, @"^子环\s*(\d+)$");
                return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : (int?)null;
            })
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToHashSet();
        var next = 1;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        return $"子环 {next}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void TogglePinnedState()
    {
        _isPinned = !_isPinned;
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        HostAssets.AppendLog($"Radial menu pin toggled: pinned={_isPinned}.");
    }

    private void ToggleEditModeState()
    {
        _editModeLocked = !_editModeLocked;
        if (_editModeLocked)
        {
            LoadRadialMenuPages();
            if (_pages.Count > 0)
            {
                _currentPageId = _pages[0].Id;
            }
        }
        else
        {
            LoadRadialMenuPages();
        }
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        UpdateEditModeState();
        OnPropertyChanged(nameof(EditButtonBrush));
        HostAssets.AppendLog($"Radial menu edit mode toggled: locked={_editModeLocked}.");
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleEditModeState();
        e.Handled = true;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddRadialMenuContextMenu();
        e.Handled = true;
    }

    private void ShowAddRadialMenuContextMenu()
    {
        var contextMenu = new ContextMenu();

        var globalItem = new MenuItem { Header = "新建全局轮盘" };
        globalItem.Click += (s, e) => AddGlobalPage();
        contextMenu.Items.Add(globalItem);

        var currentAppName = string.IsNullOrWhiteSpace(_activeProcessName) ? "当前应用" : _activeProcessName;
        var appItem = new MenuItem { Header = $"新建 {currentAppName} 专属轮盘" };
        appItem.Click += (s, e) => AddAppPage();
        contextMenu.Items.Add(appItem);

        contextMenu.PlacementTarget = AddButton;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        contextMenu.IsOpen = true;
    }

    private void AddGlobalPage()
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        int globalCount = settings.RadialMenu.Pages.Count(p => string.IsNullOrEmpty(p.ContextProcessName)) + 1;
        var newPage = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"全局 {globalCount}",
            Slots = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            SlotTitles = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            ChildPageIds = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
        };
        settings.RadialMenu.Pages.Add(newPage);
        AppSettingsStore.Save(settings);

        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");

        LoadRadialMenuPages();
        _currentPageId = newPage.Id;
        BuildItems(_lastRadiusPixels);

        HostAssets.AppendLog($"New global page added: name={newPage.Name}, id={newPage.Id}");
    }

    private void AddAppPage()
    {
        if (string.IsNullOrWhiteSpace(_activeProcessName))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        var normalizedProcess = _activeProcessName.Trim().ToLowerInvariant().Replace(".exe", "");
        int appCount = settings.RadialMenu.Pages.Count(p => 
            !string.IsNullOrEmpty(p.ContextProcessName) && 
            p.ContextProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase)) + 1;

        var newPage = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = appCount > 1 ? $"{_activeProcessName}专属 {appCount}" : $"{_activeProcessName}",
            ContextProcessName = normalizedProcess,
            ContextDisplayName = _activeProcessName,
            Slots = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            SlotTitles = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            ChildPageIds = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
        };
        settings.RadialMenu.Pages.Add(newPage);
        AppSettingsStore.Save(settings);

        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");

        LoadRadialMenuPages();
        _currentPageId = newPage.Id;
        BuildItems(_lastRadiusPixels);

        HostAssets.AppendLog($"New app-specific page added: name={newPage.Name}, id={newPage.Id}, process={normalizedProcess}");
    }

    private bool UpdatePinHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInPinButton(cursorPoint);
        IsPinHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearChildRing();
        ActiveTitle = _isPinned ? "松开取消钉住" : "松开钉住";
        return true;
    }

    private bool IsPointInPinButton(System.Windows.Point point)
    {
        if (!PinButton.IsLoaded)
        {
            return false;
        }

        var topLeft = PinButton.TranslatePoint(new System.Windows.Point(0, 0), this);
        var bounds = new Rect(topLeft.X, topLeft.Y, PinButton.ActualWidth, PinButton.ActualHeight);
        return bounds.Contains(point);
    }

    private bool UpdateEditHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInEditButton(cursorPoint);
        IsEditHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearChildRing();
        ActiveTitle = _editModeLocked ? "松开退出编辑" : "松开进入编辑";
        return true;
    }

    private bool IsPointInEditButton(System.Windows.Point point)
    {
        if (!EditButton.IsLoaded)
        {
            return false;
        }

        var topLeft = EditButton.TranslatePoint(new System.Windows.Point(0, 0), this);
        var bounds = new Rect(topLeft.X, topLeft.Y, EditButton.ActualWidth, EditButton.ActualHeight);
        return bounds.Contains(point);
    }

    private bool UpdateAddHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInAddButton(cursorPoint);
        IsAddHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearChildRing();
        ActiveTitle = "松开添加轮盘";
        return true;
    }

    private bool IsPointInAddButton(System.Windows.Point point)
    {
        if (AddButton == null || !AddButton.IsLoaded)
        {
            return false;
        }

        var topLeft = AddButton.TranslatePoint(new System.Windows.Point(0, 0), this);
        var bounds = new Rect(topLeft.X, topLeft.Y, AddButton.ActualWidth, AddButton.ActualHeight);
        return bounds.Contains(point);
    }

    private bool IsRadialSlotBoundToCurrentApp(RadialEditTarget target)
    {
        if (string.IsNullOrWhiteSpace(_activeProcessName))
        {
            return false;
        }

        var settings = AppSettingsStore.Load();
        var normalizedProcess = _activeProcessName.Trim().ToLowerInvariant().Replace(".exe", "");
        var appPage = settings.RadialMenu?.Pages?.FirstOrDefault(item => 
            !string.IsNullOrEmpty(item.ContextProcessName) && 
            item.ContextProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase));

        if (appPage == null)
        {
            return false;
        }

        EnsureRadialPageSlotCapacity(appPage);
        return appPage.Slots.ElementAtOrDefault(target.Index) != null || 
               appPage.ChildPageIds.ElementAtOrDefault(target.Index) != null;
    }

    private void BindRadialSlotToCurrentApp(RadialEditTarget target)
    {
        if (string.IsNullOrWhiteSpace(_activeProcessName))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        var normalizedProcess = _activeProcessName.Trim().ToLowerInvariant().Replace(".exe", "");
        var appPage = settings.RadialMenu.Pages.FirstOrDefault(item => 
            !string.IsNullOrEmpty(item.ContextProcessName) && 
            item.ContextProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase));

        if (appPage == null)
        {
            appPage = new RadialMenuPageSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"{_activeProcessName}",
                ContextProcessName = normalizedProcess,
                ContextDisplayName = _activeProcessName,
                Slots = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
                SlotTitles = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
                ChildPageIds = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
            };
            settings.RadialMenu.Pages.Add(appPage);
        }

        EnsureRadialPageSlotCapacity(appPage);

        var extensionId = target.Item.Command?.ExtensionId;
        var displayTitle = target.Item.Command?.Title;
        var childPageId = target.Item.ChildPageId;

        appPage.Slots[target.Index] = string.IsNullOrWhiteSpace(extensionId) ? null : extensionId.Trim();
        appPage.SlotTitles[target.Index] = string.IsNullOrWhiteSpace(displayTitle) ? null : displayTitle.Trim();
        appPage.ChildPageIds[target.Index] = string.IsNullOrWhiteSpace(childPageId) ? null : childPageId.Trim();

        PersistRadialSettings(settings);
        
        RefreshFromSettings(target.PageId, target.Index, ensureChildRingVisible: !string.IsNullOrWhiteSpace(appPage.ChildPageIds?.ElementAtOrDefault(target.Index)));
        HostAssets.AppendLog($"Radial edit bound slot {target.Index + 1} to app {_activeProcessName}.");
    }

    private void UnbindRadialSlotFromCurrentApp(RadialEditTarget target)
    {
        if (string.IsNullOrWhiteSpace(_activeProcessName))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        var normalizedProcess = _activeProcessName.Trim().ToLowerInvariant().Replace(".exe", "");
        var appPage = settings.RadialMenu.Pages.FirstOrDefault(item => 
            !string.IsNullOrEmpty(item.ContextProcessName) && 
            item.ContextProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase));

        if (appPage == null)
        {
            return;
        }

        EnsureRadialPageSlotCapacity(appPage);

        appPage.Slots[target.Index] = null;
        appPage.SlotTitles[target.Index] = null;
        appPage.ChildPageIds[target.Index] = null;

        bool isPageEmpty = appPage.Slots.All(s => s == null) && 
                           appPage.ChildPageIds.All(c => c == null);
        if (isPageEmpty)
        {
            settings.RadialMenu.Pages.Remove(appPage);
        }

        PersistRadialSettings(settings);

        RefreshFromSettings(target.PageId, target.Index, ensureChildRingVisible: false);
        HostAssets.AppendLog($"Radial edit unbound slot {target.Index + 1} from app {_activeProcessName}.");
    }

    private void HideIfAllowed()
    {
        if (_isPinned)
        {
            HostAssets.AppendLog("Radial menu hide skipped because wheel is pinned.");
            return;
        }

        Hide();
    }

    public new void Hide()
    {
        _editModeLocked = false;
        _editInteractionActive = false;
        OnPropertyChanged(nameof(EditButtonBrush));
        base.Hide();
    }

    private static string? FindExecutablePath(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName = processName.Substring(0, processName.Length - 4);
        }

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                var path = processes[0].MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
        }
        catch { }

        var searchPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), processName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), processName)
        };

        var exeName = processName + ".exe";
        foreach (var dir in searchPaths)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                var fullPath = Path.Combine(dir, exeName);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch { }
        }

        return null;
    }

    private static ImageSource? GetProcessIcon(string processName)
    {
        try
        {
            var path = FindExecutablePath(processName);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    if (bitmapSource.CanFreeze)
                    {
                        bitmapSource.Freeze();
                    }
                    RadialMenuNativeMethods.DestroyIcon(icon.Handle);
                    return bitmapSource;
                }
            }
        }
        catch { }
        return null;
    }
}

public sealed class RadialMenuItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isHovered;
    private static readonly System.Windows.Media.Brush ChildPageAccentBrush =
        (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!;
    private static readonly System.Windows.Media.Brush EmptySlotSectorBrush =
        (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF64748B")!;
    private static readonly System.Windows.Media.Brush FilledSlotFallbackSectorBrush =
        (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF334155")!;

    public RadialMenuItemViewModel(
        string ownerPageId,
        int index,
        CommandItem? command,
        string childPageId,
        string childPageTitle,
        double x,
        double y,
        double angleDegrees,
        RadialMenuRing ring,
        Geometry? sectorGeometry = null)
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
        SectorGeometry = sectorGeometry;

        if (Command != null)
        {
            Command.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CommandItem.IsRunning))
                {
                    OnPropertyChanged(nameof(IsRunning));
                }
            };
        }
    }

    public string OwnerPageId { get; }

    public int Index { get; }

    public double AngleDegrees { get; }

    public RadialMenuRing Ring { get; }

    public CommandItem? Command { get; }

    public string ChildPageId { get; }

    public string ChildPageTitle { get; }

    public bool HasChildPage => !string.IsNullOrWhiteSpace(ChildPageId);

    public double X { get; }

    public double Y { get; }

    public Geometry? SectorGeometry { get; }

    public bool IsRunning => Command?.IsRunning == true;

    public string Title => !string.IsNullOrWhiteSpace(ChildPageId) ? ChildPageTitle : Command?.Title ?? string.Empty;

    public ImageSource? IconSource => Command?.IconSource;

    public Geometry? VectorIcon => Command?.VectorIcon;

    public bool HasImageIcon => Command?.HasImageIcon == true;

    public bool HasVectorIcon => Command?.HasVectorIcon == true;

    public bool UseGlyphIcon => Command == null || Command.UseGlyphIcon;

    public string DisplayGlyph => !string.IsNullOrWhiteSpace(ChildPageId) ? "›" : Command?.DisplayGlyph ?? "+";

    public System.Windows.Media.Brush AccentBrush => Command?.AccentBrush ?? (HasChildPage ? ChildPageAccentBrush : System.Windows.Media.Brushes.Transparent);

    public System.Windows.Media.Brush SectorBrush => IsEmpty
        ? EmptySlotSectorBrush
        : IsTransparentBrush(AccentBrush)
            ? FilledSlotFallbackSectorBrush
            : AccentBrush;

    public double SectorOpacity => IsSelected ? 0.58 : IsHovered ? 0.44 : IsEmpty ? 0.0 : 0.32;

    public bool IsSectorVisible => SectorGeometry != null && (!IsEmpty || IsHovered || IsSelected);

    public double Scale => IsSelected ? 1.12 : 1.0;

    public bool IsEmpty => Command == null && string.IsNullOrWhiteSpace(ChildPageId);

    public bool IsNotEmpty => !IsEmpty;

    public bool ShouldShowEmptyPlaceholder => IsEmpty && (IsHovered || IsSelected);

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (value == _isHovered)
            {
                return;
            }

            _isHovered = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
            OnPropertyChanged(nameof(SectorOpacity));
            OnPropertyChanged(nameof(IsSectorVisible));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Scale));
            OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
            OnPropertyChanged(nameof(SectorOpacity));
            OnPropertyChanged(nameof(IsSectorVisible));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static bool IsTransparentBrush(System.Windows.Media.Brush brush)
    {
        return brush is SolidColorBrush solidColorBrush && solidColorBrush.Color.A == 0;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record RadialSeparatorViewModel(double X1, double Y1, double X2, double Y2);

public sealed record RadialMenuRuntimeItem(CommandItem? Command, string ChildPageId);

internal sealed record RadialEditTarget(string PageId, int Index, RadialMenuItemViewModel Item);

public enum RadialMenuRing
{
    Inner,
    Outer,
    Child,
    GrandChild
}

internal sealed record RadialSlotPayload(string? ExtensionId, string? DisplayTitle, string? ChildPageId);

internal static partial class RadialMenuNativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}

public class PaginationDotViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
