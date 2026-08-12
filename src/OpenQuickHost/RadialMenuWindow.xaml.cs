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

    private List<RadialMenuPageSettings> _pages = [];
    private List<RadialMenuPageSettings> _topLevelPages = [];
    private string _currentPageId = string.Empty;
    private string? _activeProcessName;
    private bool _isEditHoverActive;
    private bool _isAddHoverActive;
    private bool _isDeleteHoverActive;
    private bool _isSearchHoverActive;
    private bool _isCloseHoverActive;
    private readonly Stack<string> _pageStack = new();
    private bool _isExecuting;
    private string _activeTitle = "取消";
    private string _pageTitle = "燕环";

    private IntPtr _previousForegroundWindow;
    private bool _editModeLocked;
    private bool _editInteractionActive;
    private bool _isChildRingLocked;
    private bool _isGrandChildRingLocked;
    private bool _isGreatGrandChildRingLocked;
    private bool _isPinned;
    private bool _isPinHoverActive;
    private string _centerPrimaryText = "燕环";
    private bool _isCenterCloseMode;
    private bool _isCenterHovered;
    private RadialSlotPayload? _cutSlotPayload;
    private RadialMenuItemViewModel? _dragSourceItem;
    private int _lastRadiusPixels = 96;
    private int _cachedDeadZonePixels = 36;

    public RadialMenuWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _selectionTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _selectionTimer.Tick += (_, _) => UpdateSelectionFromCursor(null);
        DataContext = this;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _editModeLocked = false;
                Hide();
            }
        };
        MouseMove += (_, e) => UpdateSelectionFromCursor(e.GetPosition(this));
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

    public ObservableCollection<RadialMenuItemViewModel> GreatGrandChildItems { get; } = [];

    public ObservableCollection<RadialMenuNestedRingViewModel> SubRings { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> MainSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> OuterSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> ChildSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> GrandChildSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> GreatGrandChildSeparators { get; } = [];



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

    private static System.Windows.Media.Brush DefaultButtonIconBrush =>
        (System.Windows.Application.Current?.TryFindResource("BrushRadialText") as System.Windows.Media.Brush)
        ?? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFFFFFFF")!;

    public System.Windows.Media.Brush PinButtonBrush => _isPinned
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFF59E0B")!
        : DefaultButtonIconBrush;

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
        : DefaultButtonIconBrush;

    public bool IsDeleteHoverActive
    {
        get => _isDeleteHoverActive;
        private set
        {
            if (value == _isDeleteHoverActive) return;
            _isDeleteHoverActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeleteButtonBrush));
        }
    }

    public System.Windows.Media.Brush DeleteButtonBrush => _isDeleteHoverActive
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFEF4444")!
        : DefaultButtonIconBrush;

    public bool IsSearchHoverActive
    {
        get => _isSearchHoverActive;
        private set
        {
            if (value == _isSearchHoverActive) return;
            _isSearchHoverActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchButtonBrush));
        }
    }

    public System.Windows.Media.Brush SearchButtonBrush => _isSearchHoverActive
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : DefaultButtonIconBrush;

    public bool IsCloseHoverActive
    {
        get => _isCloseHoverActive;
        private set
        {
            if (value == _isCloseHoverActive) return;
            _isCloseHoverActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CloseButtonBrush));
        }
    }

    public System.Windows.Media.Brush CloseButtonBrush => _isCloseHoverActive
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFEF4444")!
        : DefaultButtonIconBrush;

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

    private bool _isChildCenterHovered;
    public bool IsChildCenterHovered
    {
        get => _isChildCenterHovered;
        private set
        {
            if (value == _isChildCenterHovered) return;
            _isChildCenterHovered = value;
            OnPropertyChanged(nameof(IsChildCenterHovered));
            OnPropertyChanged(nameof(ChildCenterContentVisibility));
            OnPropertyChanged(nameof(ChildCenterPinVisibility));
        }
    }

    private bool _isGrandChildCenterHovered;
    public bool IsGrandChildCenterHovered
    {
        get => _isGrandChildCenterHovered;
        private set
        {
            if (value == _isGrandChildCenterHovered) return;
            _isGrandChildCenterHovered = value;
            OnPropertyChanged(nameof(IsGrandChildCenterHovered));
            OnPropertyChanged(nameof(GrandChildCenterContentVisibility));
            OnPropertyChanged(nameof(GrandChildCenterPinVisibility));
        }
    }

    public bool IsChildRingLocked
    {
        get => _isChildRingLocked;
        set
        {
            if (value == _isChildRingLocked) return;
            _isChildRingLocked = value;
            OnPropertyChanged(nameof(IsChildRingLocked));
            OnPropertyChanged(nameof(ChildCenterContentVisibility));
            OnPropertyChanged(nameof(ChildCenterPinVisibility));
            OnPropertyChanged(nameof(ChildPinBrush));
        }
    }

    public bool IsGrandChildRingLocked
    {
        get => _isGrandChildRingLocked;
        set
        {
            if (value == _isGrandChildRingLocked) return;
            _isGrandChildRingLocked = value;
            OnPropertyChanged(nameof(IsGrandChildRingLocked));
            OnPropertyChanged(nameof(GrandChildCenterContentVisibility));
            OnPropertyChanged(nameof(GrandChildCenterPinVisibility));
            OnPropertyChanged(nameof(GrandChildPinBrush));
        }
    }

    public bool IsGreatGrandChildRingLocked
    {
        get => _isGreatGrandChildRingLocked;
        set
        {
            if (value == _isGreatGrandChildRingLocked) return;
            _isGreatGrandChildRingLocked = value;
            OnPropertyChanged(nameof(IsGreatGrandChildRingLocked));
            OnPropertyChanged(nameof(GreatGrandChildCenterContentVisibility));
            OnPropertyChanged(nameof(GreatGrandChildCenterPinVisibility));
            OnPropertyChanged(nameof(GreatGrandChildPinBrush));
        }
    }

    public Visibility ChildCenterContentVisibility => (IsChildCenterHovered || IsChildRingLocked) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ChildCenterPinVisibility => (IsChildCenterHovered || IsChildRingLocked) ? Visibility.Visible : Visibility.Collapsed;
    public System.Windows.Media.Brush ChildPinBrush => IsChildRingLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFA0A0A0")!;

    public Visibility GrandChildCenterContentVisibility => (IsGrandChildCenterHovered || IsGrandChildRingLocked) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GrandChildCenterPinVisibility => (IsGrandChildCenterHovered || IsGrandChildRingLocked) ? Visibility.Visible : Visibility.Collapsed;
    public System.Windows.Media.Brush GrandChildPinBrush => IsGrandChildRingLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFA0A0A0")!;

    private bool _isGreatGrandChildCenterHovered;
    public bool IsGreatGrandChildCenterHovered
    {
        get => _isGreatGrandChildCenterHovered;
        set
        {
            if (value == _isGreatGrandChildCenterHovered) return;
            _isGreatGrandChildCenterHovered = value;
            OnPropertyChanged(nameof(IsGreatGrandChildCenterHovered));
            OnPropertyChanged(nameof(GreatGrandChildCenterContentVisibility));
            OnPropertyChanged(nameof(GreatGrandChildCenterPinVisibility));
            OnPropertyChanged(nameof(GreatGrandChildPinBrush));
        }
    }

    public Visibility GreatGrandChildCenterContentVisibility => (IsGreatGrandChildCenterHovered || IsGreatGrandChildRingLocked) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GreatGrandChildCenterPinVisibility => (IsGreatGrandChildCenterHovered || IsGreatGrandChildRingLocked) ? Visibility.Visible : Visibility.Collapsed;
    public System.Windows.Media.Brush GreatGrandChildPinBrush => IsGreatGrandChildRingLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFA0A0A0")!;

    public ObservableCollection<PaginationDotViewModel> PaginationDots { get; } = new();

    public System.Windows.Media.Brush EditButtonBrush => _editModeLocked
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : DefaultButtonIconBrush;

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

        var childPageIdsSet = settings.RadialMenu.GetChildPageIdsSet();
        _topLevelPages = _pages.Where(p => !childPageIdsSet.Contains(p.Id)).ToList();
        if (_topLevelPages.Count == 0 && _pages.Count > 0)
        {
            _topLevelPages = [_pages[0]];
        }

        while (PaginationDots.Count < _topLevelPages.Count)
        {
            PaginationDots.Add(new PaginationDotViewModel { IsSelected = false });
        }
        while (PaginationDots.Count > _topLevelPages.Count)
        {
            PaginationDots.RemoveAt(PaginationDots.Count - 1);
        }
    }

    /// <summary>
    /// 预热轮盘窗口：在程序启动时提前创建 HWND 句柄、完成 WPF 首次透明窗口尺寸测量与 VisualTree 编译，
    /// 彻底消除用户首次呼出轮盘时的掉帧和顿挫感。
    /// </summary>
    public void Warmup()
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.EnsureHandle();

            LoadRadialMenuPages();
            _currentPageId = _pages.FirstOrDefault()?.Id ?? string.Empty;
            BuildItems(220);

            Opacity = 0;
            Left = -10000;
            Top = -10000;
            Show();
            UpdateLayout();
            Hide();
            Opacity = 1.0;
            HostAssets.AppendLog("RadialMenuWindow: Warmup completed successfully.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"RadialMenuWindow.Warmup EXCEPTION: {ex}");
        }
    }

    public void ShowAtMouse()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(Forms.Cursor.Position);
        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            dpiScaleX = dpi.DpiScaleX;
            dpiScaleY = dpi.DpiScaleY;
        }
        catch
        {
            if (System.Windows.Application.Current.MainWindow != null)
            {
                try
                {
                    var dpi = VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow);
                    dpiScaleX = dpi.DpiScaleX;
                    dpiScaleY = dpi.DpiScaleY;
                }
                catch { }
            }
        }

        double dipWidth = screen.Bounds.Width / dpiScaleX;
        double dipHeight = screen.Bounds.Height / dpiScaleY;

        // 强力兜底保护：确保背景画布足够大（最小 2400 像素以上）
        Width = Math.Max(dipWidth, 2400);
        Height = Math.Max(dipHeight, 2400);

        _isExecuting = false;
        _editModeLocked = false;
        _editInteractionActive = false;
        IsChildRingLocked = false;
        IsGrandChildRingLocked = false;
        IsGreatGrandChildRingLocked = false;
        IsChildCenterHovered = false;
        IsGrandChildCenterHovered = false;
        IsGreatGrandChildCenterHovered = false;
        _selectionTimer.Stop();

        // 立即清空上次残留的图标和列表，防止窗口重显时出现旧内容闪烁
        CenterIcon = null;
        Items.Clear();
        OuterItems.Clear();
        ChildItems.Clear();
        GrandChildItems.Clear();
        GreatGrandChildItems.Clear();
        SubRings.Clear();
        PageTitle = "燕环";

        var settings = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        _lastRadiusPixels = settings.RadiusPixels;
        _cachedDeadZonePixels = Math.Max(36, settings.DeadZonePixels);

        // 获取鼠标所在位置的顶级窗口，而不是当前前台窗口（更符合直觉）
        _previousForegroundWindow = IntPtr.Zero;
        if (RadialMenuNativeMethods.GetCursorPos(out var pt))
        {
            var hwndUnderMouse = RadialMenuNativeMethods.WindowFromPoint(pt);
            if (hwndUnderMouse != IntPtr.Zero)
            {
                _previousForegroundWindow = RadialMenuNativeMethods.GetAncestor(hwndUnderMouse, RadialMenuNativeMethods.GA_ROOT);
            }
        }
        
        // 兜底方案
        if (_previousForegroundWindow == IntPtr.Zero)
        {
            _previousForegroundWindow = RadialMenuNativeMethods.GetForegroundWindow();
        }
        _activeProcessName = null;
        if (_previousForegroundWindow != IntPtr.Zero)
        {
            try
            {
                RadialMenuNativeMethods.GetWindowThreadProcessId(_previousForegroundWindow, out var processId);
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                var name = process.ProcessName;
                // 跳过轮盘宿主自身，避免把 OpenQuickHost 识别为目标应用
                if (!name.Equals("OpenQuickHost", StringComparison.OrdinalIgnoreCase))
                {
                    _activeProcessName = name;
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"RadialMenu: Failed to get process name: {ex.Message}");
            }
        }
        LoadRadialMenuPages();
        // 精确匹配当前活动进程的专属页面（LoadRadialMenuPages 已过滤，firstAppPage 一定是当前进程的）
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

        _ = Dispatcher.InvokeAsync(() =>
        {
            Opacity = 1;
            Activate();
            _selectionTimer.Start();
            UpdateSelectionFromCursor();
        }, DispatcherPriority.Render);
        HostAssets.AppendLog($"Radial menu shown: page={_currentPageId}, process={_activeProcessName ?? "(none)"}, items={Items.Count}, center=({_centerPixels.X},{_centerPixels.Y}).");
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

        if (IsDeleteHoverActive)
        {
            DeleteCurrentPage();
            return;
        }

        if (IsSearchHoverActive)
        {
            ShowSearchRadialMenuContextMenu();
            return;
        }

        RadialMenuItemViewModel? selectedSubItem = null;
        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            if (ring.SelectedItem != null)
            {
                selectedSubItem = ring.SelectedItem;
                break;
            }
        }

        var selected = _selectedItem;

        if (TryHandleEmptySlotRelease(selectedSubItem) ||
            TryHandleEmptySlotRelease(selected))
        {
            return;
        }

        HideIfAllowed();
        if (selectedSubItem?.Command != null)
        {
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing subring item: index={selectedSubItem.Index}, command={selectedSubItem.Command.Title}.");
            _ = ExecuteCommandAfterForegroundRestoreAsync(selectedSubItem.Command, "radial-menu-subring");
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
            await Task.Delay(50);

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

        if (!_isPinned)
        {
            _selectionTimer.Stop();
        }
        HideIfAllowed();
    }

    private void BuildItems(int radius)
    {
        var effectiveRadius = Math.Clamp(radius - 10, 82, 96);
        Items.Clear();
        OuterItems.Clear();

        SubRings.Clear();
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
        BuildSeparators(MainSeparators, center.X, center.Y, 36, 100, RadialMenuSettings.InnerSlotCount);
        BuildSeparators(OuterSeparators, center.X, center.Y, 100, 165, RadialMenuSettings.OuterSlotCount);
        for (var index = 0; index < RadialMenuSettings.InnerSlotCount; index++)
        {
            var angleDegrees = -90 + index * 45.0;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 72 - 32;
            var y = center.Y + Math.Sin(angle) * 72 - 25;
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
                CreateSectorGeometry(center.X, center.Y, 36, 100, angleDegrees - 22.5, angleDegrees + 22.5)));
        }

        for (var offset = 0; offset < RadialMenuSettings.OuterSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + offset;
            var angleDegrees = -90 + offset * 22.5;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 125 - 25;
            var y = center.Y + Math.Sin(angle) * 125 - 20;
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
                CreateSectorGeometry(center.X, center.Y, 100, 165, angleDegrees - 11.25, angleDegrees + 11.25)));
        }

        var currentIndex = _topLevelPages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < PaginationDots.Count; i++)
        {
            PaginationDots[i].IsSelected = (i == currentIndex);
        }
    }

    private void UpdateSelectionFromCursor(System.Windows.Point? preCalculatedPoint = null)
    {
        var cursorPoint = preCalculatedPoint ?? GetCursorWindowPoint();
        if (UpdateEditHoverState(cursorPoint))
        {
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsDeleteHoverActive = false;
            IsSearchHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdatePinHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsAddHoverActive = false;
            IsDeleteHoverActive = false;
            IsSearchHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdateAddHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsPinHoverActive = false;
            IsDeleteHoverActive = false;
            IsSearchHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdateDeleteHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsSearchHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdateSearchHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsDeleteHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (UpdateCloseHoverState(cursorPoint))
        {
            IsEditHoverActive = false;
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsDeleteHoverActive = false;
            IsSearchHoverActive = false;
            IsCenterHovered = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            if (TryUpdateSubRingSelection(ring, cursorPoint))
            {
                IsCenterHovered = false;
                Cursor = System.Windows.Input.Cursors.Hand;
                
                var selectedItem = ring.SelectedItem;
                if (selectedItem != null)
                {
                    if (!string.IsNullOrWhiteSpace(selectedItem.ChildPageId))
                    {
                        ActiveTitle = _editModeLocked ? $"松开可编辑：{selectedItem.ChildPageTitle}" : $"展开：{selectedItem.ChildPageTitle}";
                        BuildSubRing(selectedItem, ring.CenterX, ring.CenterY, selectedItem.AngleDegrees, ring.Level + 1);
                    }
                    else
                    {
                        ClearSubRingsAboveLevel(ring.Level + 1);
                    }
                }
                else
                {
                    ClearSubRingsAboveLevel(ring.Level + 1);
                }
                return;
            }
        }

        var center = GetMenuCenter();
        var dx = cursorPoint.X - center.X;
        var dy = cursorPoint.Y - center.Y;
        var deadZone = _cachedDeadZonePixels;
        UpdateEditModeState();
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < deadZone)
        {
            SetSelectedItem(null);
            ClearSubRingsAboveLevel(1);
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "取消";
            IsCenterHovered = true;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        IsCenterHovered = false;

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (distance > 165)
        {
            SetSelectedItem(null);
            ClearSubRingsAboveLevel(1);
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "取消";
            Cursor = System.Windows.Input.Cursors.Arrow;
            ClearAllItemsHoverState();
            return;
        }

        Cursor = System.Windows.Input.Cursors.Hand;

        if (distance > 100)
        {
            var outerIndex = ((int)Math.Round((angle + 90) / 22.5) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
            var outerItem = OuterItems.ElementAtOrDefault(outerIndex);
            SetSelectedItem(outerItem);
            ActiveTitle = ResolveActiveTitle(outerItem?.Command?.Title, outerItem?.Command == null);
            if (!string.IsNullOrWhiteSpace(outerItem?.ChildPageId))
            {
                ActiveTitle = _editModeLocked ? $"松开可编辑：{outerItem.ChildPageTitle}" : $"展开：{outerItem.ChildPageTitle}";
                BuildSubRing(outerItem, center.X, center.Y, outerItem.AngleDegrees, 1);
            }
            else
            {
                ClearSubRingsAboveLevel(1);
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
            BuildSubRing(item, center.X, center.Y, item.AngleDegrees, 1);
        }
        else
        {
            ClearSubRingsAboveLevel(1);
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



    private void RadialMenuWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_topLevelPages.Count <= 1)
        {
            return;
        }

        var currentIndex = _topLevelPages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }
        var delta = e.Delta < 0 ? 1 : -1;
        var nextIndex = (currentIndex + delta + _topLevelPages.Count) % _topLevelPages.Count;
        _currentPageId = _topLevelPages[nextIndex].Id;
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
            var ring = SubRings.FirstOrDefault(r => r.PageId.Equals(item.OwnerPageId, StringComparison.OrdinalIgnoreCase));
            if (ring != null)
            {
                if (!ReferenceEquals(ring.SelectedItem, item))
                {
                    if (ring.SelectedItem != null)
                    {
                        ring.SelectedItem.IsSelected = false;
                    }
                    ring.SelectedItem = item;
                    if (ring.SelectedItem != null)
                    {
                        ring.SelectedItem.IsSelected = true;
                    }
                }
            }
            return;
        }

        SetSelectedItem(item);
    }

    private void BuildSubRing(RadialMenuItemViewModel parent, double parentCenterX, double parentCenterY, double parentAngleDegrees, int level)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearSubRingsAboveLevel(level);
            return;
        }

        ClearSubRingsAboveLevel(level);

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);
        var angle = parentAngleDegrees * Math.PI / 180.0;
        double offsetDistance = (level == 1 && parent.Ring == RadialMenuRing.Outer) ? 260 : 200;
        double cX = parentCenterX + Math.Cos(angle) * offsetDistance;
        double cY = parentCenterY + Math.Sin(angle) * offsetDistance;
        ClampRingCenter(ref cX, ref cY, 112);

        var ring = new RadialMenuNestedRingViewModel
        {
            PageId = parent.ChildPageId,
            Level = level,
            Title = parent.ChildPageTitle,
            CenterX = cX,
            CenterY = cY
        };

        BuildSeparators(ring.Separators, cX, cY, 36, 100, RadialMenuSettings.InnerSlotCount);

        const double radius = 72;
        for (var index = 0; index < 8; index++)
        {
            var childAngleDegrees = -90 + index * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = cX + Math.Cos(childAngle) * radius - 32;
            var y = cY + Math.Sin(childAngle) * radius - 25;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            ring.Items.Add(new RadialMenuItemViewModel(
                parent.ChildPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.Child,
                CreateSectorGeometry(cX, cY, 36, 100, childAngleDegrees - 22.5, childAngleDegrees + 22.5)));
        }

        SubRings.Add(ring);
    }

    private void ClearSubRingsAboveLevel(int level)
    {
        while (SubRings.Count >= level)
        {
            var idx = SubRings.Count - 1;
            var ring = SubRings[idx];
            if (ring.SelectedItem != null)
            {
                ring.SelectedItem.IsSelected = false;
            }
            ring.SelectedItem = null;
            ring.Items.Clear();
            ring.Separators.Clear();
            SubRings.RemoveAt(idx);
        }
    }

    private bool TryUpdateSubRingSelection(RadialMenuNestedRingViewModel ring, System.Windows.Point cursorPoint)
    {
        var dx = cursorPoint.X - ring.CenterX;
        var dy = cursorPoint.Y - ring.CenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (_editModeLocked)
        {
            ring.IsCenterHovered = (distance <= 36);
        }
        else
        {
            ring.IsCenterHovered = false;
        }

        if (distance > 100)
        {
            if (_editModeLocked && ring.IsLocked)
            {
                if (ring.SelectedItem != null)
                {
                    ring.SelectedItem.IsSelected = false;
                    ring.SelectedItem = null;
                }
                return true;
            }
            if (ring.SelectedItem != null)
            {
                ring.SelectedItem.IsSelected = false;
                ring.SelectedItem = null;
            }
            return false;
        }

        if (distance < 20)
        {
            if (ring.SelectedItem != null)
            {
                ring.SelectedItem.IsSelected = false;
                ring.SelectedItem = null;
            }
            ActiveTitle = _editModeLocked ? "点击中心 X 关闭" : "返回上一级";
            return true;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = ring.Items.ElementAtOrDefault(index);
        
        if (!ReferenceEquals(ring.SelectedItem, item))
        {
            if (ring.SelectedItem != null)
            {
                ring.SelectedItem.IsSelected = false;
            }
            ring.SelectedItem = item;
            if (ring.SelectedItem != null)
            {
                ring.SelectedItem.IsSelected = true;
            }
        }

        ActiveTitle = ResolveActiveTitle(item?.Command?.Title, item?.Command == null, isChildRing: true);
        return true;
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
        var clickPoint = e.GetPosition(this);
        if (_editModeLocked)
        {
            if (IsPointInCenter(clickPoint))
            {
                _editModeLocked = false;
                _selectionTimer.Stop();
                Hide();
                e.Handled = true;
                return;
            }

            for (int i = SubRings.Count - 1; i >= 0; i--)
            {
                var ring = SubRings[i];
                var dx = clickPoint.X - ring.CenterX;
                var dy = clickPoint.Y - ring.CenterY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= 36)
                {
                    ring.IsLocked = !ring.IsLocked;
                    e.Handled = true;
                    return;
                }
            }

            foreach (var ring in SubRings)
            {
                ring.IsLocked = false;
            }
        }

        // 如果在子页面中，点击空白处返回上一级；如果在顶级主页，点击空白处直接隐去关盘
        if (_pageStack.Count > 0)
        {
            ReturnToParentPage();
        }
        else
        {
            _editModeLocked = false;
            _selectionTimer.Stop();
            Hide();
        }
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

        var point = e.GetPosition(this);
        if (ShowCenterRenameContextMenuIfHit(point))
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
            if (item.Ring != RadialMenuRing.Child)
            {
                foreach (var ring in SubRings)
                {
                    ring.IsLocked = false;
                }
            }
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
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, item, System.Windows.DragDropEffects.Move);
        }
        finally
        {
            item.IsHovered = false;
            if (_dragSourceItem != null)
            {
                _dragSourceItem.IsHovered = false;
                _dragSourceItem = null;
            }
            ClearAllItemsHoverState();
        }
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

        if (e.Data.GetDataPresent(typeof(CommandItem)))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
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
        
        target.IsHovered = false;

        if (e.Data.GetData(typeof(RadialMenuItemViewModel)) is RadialMenuItemViewModel source)
        {
            MoveRadialSlot(new RadialEditTarget(source.OwnerPageId, source.Index, source), new RadialEditTarget(target.OwnerPageId, target.Index, target));
            _dragSourceItem = null;
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(CommandItem)) is CommandItem commandItem)
        {
            SaveRadialSlotCommand(target.OwnerPageId, target.Index, commandItem.ExtensionId, string.Empty);
            HostAssets.AppendLog($"Radial drag-dropped command: page={target.OwnerPageId}, index={target.Index + 1}, command={commandItem.Title}.");
            e.Handled = true;
            return;
        }

        if (TryGetDroppedFilePaths(e, out var filePaths))
        {
            AddDroppedPathToRadialSlot(new RadialEditTarget(target.OwnerPageId, target.Index, target), filePaths);
        }

        e.Handled = true;
    }

    private void RadialSlot_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            item.IsHovered = true;
        }
    }

    private void RadialSlot_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuItemViewModel item })
        {
            item.IsHovered = false;
        }
    }

    private void ClearAllItemsHoverState()
    {
        foreach (var item in Items)
        {
            if (item != null) item.IsHovered = false;
        }
        foreach (var item in OuterItems)
        {
            if (item != null) item.IsHovered = false;
        }
        foreach (var item in ChildItems)
        {
            if (item != null) item.IsHovered = false;
        }
        foreach (var item in GrandChildItems)
        {
            if (item != null) item.IsHovered = false;
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

    private bool ShowCenterRenameContextMenuIfHit(System.Windows.Point point)
    {
        string? targetPageId = null;

        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            if (IsPointNear(point, ring.CenterX, ring.CenterY, 36))
            {
                targetPageId = ring.PageId;
                break;
            }
        }

        if (targetPageId == null && IsPointInCenter(point))
        {
            targetPageId = _currentPageId;
        }

        if (string.IsNullOrEmpty(targetPageId))
        {
            return false;
        }

        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        var page = settings.RadialMenu.Pages.FirstOrDefault(item => item.Id.Equals(targetPageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            return false;
        }

        ShowPageCenterContextMenu(page);
        return true;
    }

    private void ShowPageCenterContextMenu(RadialMenuPageSettings page)
    {
        _editInteractionActive = true;
        var menu = new ContextMenu();
        var normalBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BrushTextSec") ?? System.Windows.Media.Brushes.Gray;

        var renameItem = new MenuItem
        {
            Header = "重命名",
            Icon = CreateMenuIcon("pencil", normalBrush)
        };
        renameItem.Click += (_, _) =>
        {
            RenameRadialPage(page.Id);
        };
        menu.Items.Add(renameItem);

        menu.PlacementTarget = this;
        menu.Closed += (_, _) =>
        {
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
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
        ActiveTitle = $"轮盘：{page.Name}";
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

        var dialog = new SimpleTextInputWindow("重命名轮盘", "输入新的轮盘名称。", page.Name)
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
        LoadRadialMenuPages();
        BuildItems((settings.RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        UpdateCenterText();
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

    private string ResolveActiveTitle(string? commandTitle, bool isEmptySlot, bool isChildRing = false, bool isGrandChildRing = false, bool isGreatGrandChildRing = false)
    {
        if (!_editModeLocked && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return commandTitle ?? "松开可新建";
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
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
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

    private static System.Windows.Shapes.Path CreateMenuIcon(string iconKey, System.Windows.Media.Brush fillBrush)
    {
        var geometry = ExtensionIconLibrary.ResolveVectorIcon(iconKey.StartsWith("mdi:", StringComparison.OrdinalIgnoreCase) ? iconKey : $"mdi:{iconKey}");
        return new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = fillBrush,
            Stretch = Stretch.Uniform,
            Width = 16,
            Height = 16,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
    }

    private void PopulateAddMenuOptions(ItemsControl parentMenu, RadialEditTarget target)
    {
        var normalBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BrushTextSec") ?? System.Windows.Media.Brushes.Gray;

        // 1. 已有扩展 (搜索图标，点击直接调出原有搜索界面)
        var existingExtensionItem = new MenuItem
        {
            Header = "已有扩展",
            Icon = CreateMenuIcon("search", normalBrush)
        };
        existingExtensionItem.Click += (_, _) =>
        {
            OpenSearchPickerForTarget(target);
        };
        parentMenu.Items.Add(existingExtensionItem);

        // 2. 新建扩展 (一级独立项)
        var createNewExtensionItem = new MenuItem
        {
            Header = "新建扩展",
            Icon = CreateMenuIcon("plus", normalBrush)
        };
        createNewExtensionItem.Click += (_, _) =>
        {
            CreateNewExtensionForTarget(target);
        };
        parentMenu.Items.Add(createNewExtensionItem);

        // 3. 模拟按键
        var setSimulatedKeyItem = new MenuItem
        {
            Header = "模拟按键",
            Icon = CreateMenuIcon("keyboard", normalBrush)
        };
        setSimulatedKeyItem.Click += (_, _) =>
        {
            SetSimulatedKeyForTarget(target);
        };
        parentMenu.Items.Add(setSimulatedKeyItem);

        // 4. 子环
        if (!target.Item.HasChildPage)
        {
            var addChildItem = new MenuItem
            {
                Header = "子环",
                Icon = CreateMenuIcon("circle-outline", normalBrush)
            };
            addChildItem.Click += (_, _) =>
            {
                AddChildPageToTarget(target);
            };
            parentMenu.Items.Add(addChildItem);
        }
    }

    private ContextMenu BuildAddMenu(RadialEditTarget target)
    {
        var menu = new ContextMenu();
        PopulateAddMenuOptions(menu, target);
        return menu;
    }

    private void ShowAddMenuForTarget(RadialEditTarget target)
    {
        _editInteractionActive = true;
        var menu = BuildAddMenu(target);
        menu.PlacementTarget = this;
        menu.Closed += (_, _) =>
        {
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
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
        ActiveTitle = "添加槽位项";
    }

    private ContextMenu BuildEditContextMenu(RadialEditTarget target)
    {
        var menu = new ContextMenu();
        var normalBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BrushTextSec") ?? System.Windows.Media.Brushes.Gray;
        var dangerBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));

        bool hasContent = target.Item.Command != null || target.Item.HasChildPage;

        if (hasContent)
        {
            var editItem = new MenuItem
            {
                Header = "编辑",
                Icon = CreateMenuIcon("pencil", normalBrush)
            };
            editItem.Click += (_, _) => EditSlotContentFromTarget(target);
            menu.Items.Add(editItem);

            var clearItem = new MenuItem
            {
                Header = "删除",
                Icon = CreateMenuIcon("trash", dangerBrush)
            };
            clearItem.Click += (_, _) => ClearSlotContentFromTarget(target);
            menu.Items.Add(clearItem);

            var cutItem = new MenuItem
            {
                Header = "剪切槽位",
                Icon = CreateMenuIcon("cut", normalBrush)
            };
            cutItem.Click += (_, _) => CutRadialSlot(target);
            menu.Items.Add(cutItem);

            if (_cutSlotPayload != null)
            {
                var pasteItem = new MenuItem
                {
                    Header = "粘贴到此槽位",
                    Icon = CreateMenuIcon("paste", normalBrush)
                };
                pasteItem.Click += (_, _) => PasteRadialSlot(target);
                menu.Items.Add(pasteItem);
            }

            menu.Items.Add(new Separator());
            PopulateAddMenuOptions(menu, target);
        }
        else
        {
            PopulateAddMenuOptions(menu, target);

            if (_cutSlotPayload != null)
            {
                var pasteItem = new MenuItem
                {
                    Header = "粘贴到此槽位",
                    Icon = CreateMenuIcon("paste", normalBrush)
                };
                pasteItem.Click += (_, _) => PasteRadialSlot(target);
                menu.Items.Add(pasteItem);
            }
        }

        if (!string.IsNullOrWhiteSpace(_activeProcessName))
        {
            bool isBound = IsRadialSlotBoundToCurrentApp(target);
            if (isBound)
            {
                menu.Items.Add(new Separator());
                var unbindItem = new MenuItem
                {
                    Header = $"取消绑定 (当前应用: {_activeProcessName})",
                    Icon = CreateMenuIcon("link", normalBrush)
                };
                unbindItem.Click += (_, _) => UnbindRadialSlotFromCurrentApp(target);
                menu.Items.Add(unbindItem);
            }
            else if (hasContent)
            {
                menu.Items.Add(new Separator());
                var bindItem = new MenuItem 
                { 
                    Header = $"绑定到当前应用: {_activeProcessName}",
                    Icon = CreateMenuIcon("link", normalBrush)
                };
                bindItem.Click += (_, _) => BindRadialSlotToCurrentApp(target);
                menu.Items.Add(bindItem);
            }
        }

        return menu;
    }

    private void AddCommandToTarget(RadialEditTarget target)
    {
        ShowAddMenuForTarget(target);
    }

    private async void OpenSearchPickerForTarget(RadialEditTarget target)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var result = await _mainWindow.ShowForRadialPickerAsync(!target.Item.HasChildPage);
                if (result == null)
                {
                    return;
                }

                if (result.Action == RadialSlotPickerWindow.PickerAction.AddChildPage)
                {
                    AddChildPageToTarget(target);
                    return;
                }

                if (result.Command == null)
                {
                    return;
                }

                SaveRadialSlotCommand(target.PageId, target.Index, result.Command.ExtensionId, string.Empty);
                HostAssets.AppendLog($"Radial edit assigned command: page={target.PageId}, index={target.Index + 1}, command={result.Command.Title}.");
            }
            finally
            {
                _editModeLocked = false;
                UpdateCenterText();
            }
        }, DispatcherPriority.Input);
    }

    private void CreateNewExtensionForTarget(RadialEditTarget target)
    {
        try
        {
            var createdCommand = _mainWindow.OpenAddExtensionForSlot(this);
            if (createdCommand != null)
            {
                SaveRadialSlotCommand(target.PageId, target.Index, createdCommand.ExtensionId, string.Empty);
                HostAssets.AppendLog($"Radial edit assigned new created extension: page={target.PageId}, index={target.Index + 1}, command={createdCommand.Title}.");
            }
        }
        finally
        {
            _editModeLocked = false;
            UpdateCenterText();
        }
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

    private async void EditSlotContentFromTarget(RadialEditTarget target)
    {
        try
        {
            if (target.Item.HasChildPage)
            {
                ShowPageCenterContextMenu(new RadialMenuPageSettings
                {
                    Id = target.Item.ChildPageId,
                    Name = target.Item.ChildPageTitle
                });
                return;
            }

            var command = target.Item.Command;
            if (command == null)
            {
                return;
            }

            const string simulatedPrefix = "keysim::";
            if (command.ExtensionId.StartsWith(simulatedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                SetSimulatedKeyForTarget(target);
                return;
            }

            var result = await _mainWindow.EditExtensionFromSettingsAsync(command.ExtensionId, this);
            if (result.ok)
            {
                ActiveTitle = string.IsNullOrWhiteSpace(result.message) ? "已保存扩展修改" : result.message;
                LoadRadialMenuPages();
            }
            else if (!string.IsNullOrWhiteSpace(result.message))
            {
                ActiveTitle = result.message;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Radial edit slot failed: page={target.PageId}, index={target.Index + 1}, error={ex}");
        }
        finally
        {
            _editModeLocked = false;
            UpdateCenterText();
        }
    }

    private void ClearSlotContentFromTarget(RadialEditTarget target)
    {
        if (target.Item.HasChildPage)
        {
            ClearChildPageFromTarget(target);
        }
        else
        {
            ClearCommandFromTarget(target);
        }
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

        page.Slots[target.Index] = null;
        page.SlotTitles[target.Index] = null;

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

    public void SaveRadialSlotCommand(string pageId, int index, string? extensionId, string? displayTitle)
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

        // 重新载入页面设定，并清空所有子环让其重新在悬浮时构建
        SubRings.Clear();

        UpdateEditModeState();
        ActiveTitle = "编辑已保存，点击中心 X 关闭";
        _selectionTimer.Start();
    }

    private RadialEditTarget? ResolveCurrentEditTarget()
    {
        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            if (ring.SelectedItem != null)
            {
                return new RadialEditTarget(ring.SelectedItem.OwnerPageId, ring.SelectedItem.Index, ring.SelectedItem);
            }
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

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteCurrentPage();
        e.Handled = true;
    }

    private void DeleteCurrentPage()
    {
        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        var pageToDelete = settings.RadialMenu.Pages.FirstOrDefault(p => p.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        if (pageToDelete == null) return;

        var result = System.Windows.MessageBox.Show(
            $"确定要删除当前轮盘“{pageToDelete.Name}”吗？",
            "确认删除",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            settings.RadialMenu.Pages.Remove(pageToDelete);
            AppSettingsStore.Save(settings);

            _mainWindow.RefreshAppSettings();
            _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");

            LoadRadialMenuPages();
            
            _currentPageId = _pages.FirstOrDefault()?.Id ?? string.Empty;
            BuildItems(_lastRadiusPixels);

            HostAssets.AppendLog($"Radial page deleted: name={pageToDelete.Name}, id={pageToDelete.Id}");
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSearchRadialMenuContextMenu();
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        e.Handled = true;
    }

    private void ShowSearchRadialMenuContextMenu()
    {
        var contextMenu = new ContextMenu();

        var allPages = _pages;
        var pageMap = allPages.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // 查找所有子环页面ID集合
        var childIdsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in allPages)
        {
            if (page.ChildPageIds == null) continue;
            foreach (var childId in page.ChildPageIds)
            {
                if (!string.IsNullOrWhiteSpace(childId) && !string.Equals(childId, page.Id, StringComparison.OrdinalIgnoreCase))
                {
                    childIdsSet.Add(childId);
                }
            }
        }

        // 确定根页面
        var rootPages = allPages.Where(p => !childIdsSet.Contains(p.Id)).ToList();
        if (rootPages.Count == 0 && allPages.Count > 0)
        {
            rootPages = [allPages[0]];
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPageHierarchyToMenu(RadialMenuPageSettings page, int level)
        {
            if (visited.Contains(page.Id)) return;
            visited.Add(page.Id);

            // 根据 level 计算缩进
            string indent = level switch
            {
                0 => "",
                1 => "   └─ ",
                2 => "      └─ ",
                3 => "         └─ ",
                _ => new string(' ', level * 3) + "└─ "
            };

            var header = $"{indent}{page.Name}";
            var item = new MenuItem { Header = header };
            var pageId = page.Id;
            item.Click += (s, e) =>
            {
                _currentPageId = pageId;
                BuildItems(_lastRadiusPixels);
            };
            contextMenu.Items.Add(item);

            if (page.ChildPageIds != null)
            {
                foreach (var childId in page.ChildPageIds)
                {
                    if (!string.IsNullOrWhiteSpace(childId) && pageMap.TryGetValue(childId, out var childPage))
                    {
                        AddPageHierarchyToMenu(childPage, level + 1);
                    }
                }
            }
        }

        foreach (var root in rootPages)
        {
            AddPageHierarchyToMenu(root, 0);
        }

        // 兜底防漏：如果有页面因为异常引用的情况没被加入 visited 列表，直接平铺加在最后
        foreach (var page in allPages)
        {
            if (!visited.Contains(page.Id))
            {
                var header = page.Name;
                var item = new MenuItem { Header = header };
                var pageId = page.Id;
                item.Click += (s, e) =>
                {
                    _currentPageId = pageId;
                    BuildItems(_lastRadiusPixels);
                };
                contextMenu.Items.Add(item);
            }
        }

        if (contextMenu.Items.Count == 0)
        {
            contextMenu.Items.Add(new MenuItem { Header = "无可用轮盘", IsEnabled = false });
        }

        contextMenu.PlacementTarget = SearchButton;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        contextMenu.IsOpen = true;
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
        ClearSubRingsAboveLevel(1);
        ActiveTitle = _isPinned ? "松开取消钉住" : "松开钉住";
        return true;
    }

    private Rect? _pinButtonRect;
    private Rect? _editButtonRect;
    private Rect? _addButtonRect;
    private Rect? _deleteButtonRect;
    private Rect? _searchButtonRect;
    private Rect? _closeButtonRect;

    private void InvalidateButtonRects()
    {
        _pinButtonRect = null;
        _editButtonRect = null;
        _addButtonRect = null;
        _deleteButtonRect = null;
        _searchButtonRect = null;
        _closeButtonRect = null;
    }

    private bool IsPointInButton(FrameworkElement? button, ref Rect? cachedRect, System.Windows.Point point)
    {
        if (button == null || !button.IsLoaded)
        {
            return false;
        }

        if (cachedRect == null)
        {
            var topLeft = button.TranslatePoint(new System.Windows.Point(0, 0), this);
            cachedRect = new Rect(topLeft.X, topLeft.Y, button.ActualWidth, button.ActualHeight);
        }

        return cachedRect.Value.Contains(point);
    }

    private bool IsPointInPinButton(System.Windows.Point point) => IsPointInButton(PinButton, ref _pinButtonRect, point);

    private bool UpdateEditHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInEditButton(cursorPoint);
        IsEditHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearSubRingsAboveLevel(1);
        ActiveTitle = _editModeLocked ? "松开退出编辑" : "松开进入编辑";
        return true;
    }

    private bool IsPointInEditButton(System.Windows.Point point) => IsPointInButton(EditButton, ref _editButtonRect, point);

    private bool UpdateAddHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInAddButton(cursorPoint);
        IsAddHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearSubRingsAboveLevel(1);
        ActiveTitle = "松开添加轮盘";
        return true;
    }

    private bool IsPointInAddButton(System.Windows.Point point) => IsPointInButton(AddButton, ref _addButtonRect, point);

    private bool UpdateDeleteHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInDeleteButton(cursorPoint);
        IsDeleteHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearSubRingsAboveLevel(1);
        ActiveTitle = "松开删除当前轮盘";
        return true;
    }

    private bool IsPointInDeleteButton(System.Windows.Point point) => IsPointInButton(DeleteButton, ref _deleteButtonRect, point);

    private bool UpdateSearchHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInSearchButton(cursorPoint);
        IsSearchHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearSubRingsAboveLevel(1);
        ActiveTitle = "松开查询与切换轮盘";
        return true;
    }

    private bool IsPointInSearchButton(System.Windows.Point point) => IsPointInButton(SearchButton, ref _searchButtonRect, point);

    private bool UpdateCloseHoverState(System.Windows.Point cursorPoint)
    {
        var hovered = IsPointInCloseButton(cursorPoint);
        IsCloseHoverActive = hovered;
        if (!hovered)
        {
            return false;
        }

        SetSelectedItem(null);
        ClearSubRingsAboveLevel(1);
        ActiveTitle = "松开关闭燕环";
        return true;
    }

    private bool IsPointInCloseButton(System.Windows.Point point) => IsPointInButton(CloseButton, ref _closeButtonRect, point);


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
        Opacity = 0;

        // 默认状态清理工作，还原页面与应用关联属性，消除上一次轮盘的“遗像”残留
        _activeProcessName = null;
        _currentPageId = "default";
        _pageStack.Clear();

        // 重置 UI 文本及绑定的图像，确保其在下一次呼出前即为默认初始态
        PageDisplaySummary = string.Empty;
        PageTitle = "燕环";
        CenterIcon = null;
        ActiveTitle = "取消";

        // 彻底清空图形缓存项，释放 UI 控件
        Items.Clear();
        OuterItems.Clear();
        ChildItems.Clear();
        GrandChildItems.Clear();

        base.Hide();
        MemoryOptimizationService.OptimizeMemoryInBackground();
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

    public void SetDragHoverItem(RadialMenuItemViewModel? item)
    {
        foreach (var slot in Items)
        {
            if (slot != null) slot.IsHovered = false;
        }
        foreach (var slot in OuterItems)
        {
            if (slot != null) slot.IsHovered = false;
        }
        foreach (var slot in ChildItems)
        {
            if (slot != null) slot.IsHovered = false;
        }
        foreach (var slot in GrandChildItems)
        {
            if (slot != null) slot.IsHovered = false;
        }

        if (item != null)
        {
            item.IsHovered = true;
        }
    }

    public RadialMenuItemViewModel? FindSlotAtScreenPoint(System.Windows.Point screenPoint)
    {
        if (!IsVisible) return null;

        var localPoint = new System.Windows.Point(screenPoint.X - Left, screenPoint.Y - Top);
        
        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            var dx = localPoint.X - ring.CenterX;
            var dy = localPoint.Y - ring.CenterY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= 100 && distance > 36)
            {
                var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
                return ring.Items.ElementAtOrDefault(index);
            }
        }

        // 3. 主环
        var center = GetMenuCenter();
        var dxMain = localPoint.X - center.X;
        var dyMain = localPoint.Y - center.Y;
        var distanceMain = Math.Sqrt(dxMain * dxMain + dyMain * dyMain);
        var deadZone = _cachedDeadZonePixels;

        if (distanceMain <= 165 && distanceMain > deadZone)
        {
            var angle = Math.Atan2(dyMain, dxMain) * 180.0 / Math.PI;
            if (distanceMain > 100)
            {
                var outerIndex = ((int)Math.Round((angle + 90) / 22.5) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
                return OuterItems.ElementAtOrDefault(outerIndex);
            }
            else
            {
                var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
                return Items.ElementAtOrDefault(index);
            }
        }

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

    private System.Windows.Media.Brush GetThemeBrush(string resourceKey, System.Windows.Media.Brush fallback)
    {
        var brush = System.Windows.Application.Current.TryFindResource(resourceKey);
        if (brush is System.Windows.Media.Brush b) return b;
        return fallback;
    }

    public System.Windows.Media.Brush AccentBrush => Command?.AccentBrush ?? (HasChildPage ? GetThemeBrush("BrushRadialChildAccentSector", ChildPageAccentBrush) : System.Windows.Media.Brushes.Transparent);

    public System.Windows.Media.Brush SectorBrush => IsEmpty
        ? GetThemeBrush("BrushRadialEmptySector", EmptySlotSectorBrush)
        : (IsHovered || IsSelected)
            ? GetThemeBrush("BrushRadialChildAccentSector", ChildPageAccentBrush)
            : GetThemeBrush("BrushRadialEmptySector", EmptySlotSectorBrush);

    public double SectorOpacity => IsSelected ? 0.58 : IsHovered ? 0.44 : 0.0;

    public bool IsSectorVisible => SectorGeometry != null && (!IsEmpty || IsHovered || IsSelected);

    public double Scale => 1.0;

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
            OnPropertyChanged(nameof(SectorBrush));
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
    GrandChild,
    GreatGrandChild
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    public const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
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
