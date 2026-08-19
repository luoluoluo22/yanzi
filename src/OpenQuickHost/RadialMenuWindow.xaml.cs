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
    private long _shownTimestamp;

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
    private bool _wasActivatedForEdit;
    private bool _editModeLocked;
    private bool _editInteractionActive;
    private bool _isOpeningSubDialog;
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

    private double _guideLineX1 = 700;
    private double _guideLineY1 = 700;
    private double _guideLineX2 = 700;
    private double _guideLineY2 = 700;
    private bool _isGuideLineVisible;

    public double GuideLineX1
    {
        get => _guideLineX1;
        set
        {
            _guideLineX1 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GuideStartDotLeft));
        }
    }

    public double GuideLineY1
    {
        get => _guideLineY1;
        set
        {
            _guideLineY1 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GuideStartDotTop));
        }
    }

    public double GuideLineX2
    {
        get => _guideLineX2;
        set
        {
            _guideLineX2 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GuidePointerDotLeft));
        }
    }

    public double GuideLineY2
    {
        get => _guideLineY2;
        set
        {
            _guideLineY2 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GuidePointerDotTop));
        }
    }

    public bool IsGuideLineVisible
    {
        get => _isGuideLineVisible;
        set { _isGuideLineVisible = value; OnPropertyChanged(); }
    }

    public double GuideStartDotLeft => _guideLineX1 - 3.0;
    public double GuideStartDotTop => _guideLineY1 - 3.0;
    public double GuidePointerDotLeft => _guideLineX2 - 3.5;
    public double GuidePointerDotTop => _guideLineY2 - 3.5;

    private void UpdateGuideLine(double centerX, double centerY, double cursorX, double cursorY, double innerRadius = 36.0)
    {
        if (_editModeLocked)
        {
            IsGuideLineVisible = false;
            return;
        }

        var dx = cursorX - centerX;
        var dy = cursorY - centerY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= innerRadius)
        {
            IsGuideLineVisible = false;
            return;
        }

        var ux = dx / distance;
        var uy = dy / distance;

        // 起点：最内圆圆周接触点（蓝点）
        GuideLineX1 = centerX + ux * innerRadius;
        GuideLineY1 = centerY + uy * innerRadius;

        // 终点：鼠标当前位置
        GuideLineX2 = cursorX;
        GuideLineY2 = cursorY;

        IsGuideLineVisible = true;
    }

    public RadialMenuWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _selectionTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _selectionTimer.Tick += (_, _) => UpdateSelectionFromCursor(null);
        SubRings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSubRings));
        DataContext = this;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_editModeLocked)
                {
                    _editModeLocked = false;
                    _selectionTimer.Stop();
                    Width = 1400;
                    Height = 1400;
                    SubRings.Clear();
                    BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
                    UpdateEditModeState();
                    OnPropertyChanged(nameof(IsEditModeLocked));
                    OnPropertyChanged(nameof(EditButtonBrush));
                    OnPropertyChanged(nameof(HasSubRings));
                    Dispatcher.InvokeAsync(() => Hide(), DispatcherPriority.Render);
                }
                else
                {
                    Hide();
                }
                e.Handled = true;
            }
        };
        MouseMove += (_, e) => UpdateSelectionFromCursor(e.GetPosition(this));
        MouseWheel += RadialMenuWindow_MouseWheel;
        MouseLeftButtonDown += RadialMenuWindow_MouseLeftButtonDown;
        MouseRightButtonDown += RadialMenuWindow_MouseRightButtonDown;
        Deactivated += (_, _) =>
        {
            HostAssets.AppendLog($"[PickerLog] RadialMenu Deactivated: isVisible={IsVisible}, isEditLocked={_editModeLocked}, isEditActive={_editInteractionActive}, isPickerMode={_mainWindow.IsRadialPickerMode}, popupOpen={_mainWindow.SearchScopePopup?.IsOpen}.");
            if (_mainWindow.IsRadialPickerMode || _mainWindow.SearchScopePopup?.IsOpen == true)
            {
                return;
            }
            if (IsVisible && !_editModeLocked && !_editInteractionActive)
            {
                _selectionTimer.Stop();
                Hide();
            }
        };
        SourceInitialized += (_, _) => EnsureNoActivateStyle();
        Loaded += (_, _) =>
        {
            EnsureNoActivateStyle();
            RebuildItemsForCurrentLayout("loaded");
        };
        InputHookService.OnGlobalEscapePressed += () =>
        {
            if (IsVisible)
            {
                if (_editModeLocked)
                {
                    _editModeLocked = false;
                    _selectionTimer.Stop();
                    Width = 1400;
                    Height = 1400;
                    SubRings.Clear();
                    BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
                    UpdateEditModeState();
                    OnPropertyChanged(nameof(IsEditModeLocked));
                    OnPropertyChanged(nameof(EditButtonBrush));
                    OnPropertyChanged(nameof(HasSubRings));
                    Dispatcher.InvokeAsync(() => Hide(), DispatcherPriority.Render);
                }
                else if (!_editInteractionActive)
                {
                    _selectionTimer.Stop();
                    Hide();
                }
            }
        };
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RebuildItemsForCurrentLayout("size-changed");
            }
        };
    }

    public ObservableCollection<RadialMenuItemViewModel> Items { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> MiddleItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> OuterItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> ChildItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> GrandChildItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> GreatGrandChildItems { get; } = [];

    public ObservableCollection<RadialMenuNestedRingViewModel> SubRings { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> MainSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> MiddleSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> OuterSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> ChildSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> GrandChildSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> GreatGrandChildSeparators { get; } = [];



    private bool _isOuterWheelVisible = true;
    private bool _isOuterRingHoverActive = false;

    public bool IsOuterWheelVisible
    {
        get => _isOuterWheelVisible;
        set
        {
            if (value == _isOuterWheelVisible) return;
            _isOuterWheelVisible = value;
            OnPropertyChanged();
        }
    }

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

    public bool IsEditModeLocked => _editModeLocked;
    public bool IsPinned => _isPinned;
    public bool HasSubRings => SubRings.Count > 0;
    public bool IsCenterWheelVisible => !_editModeLocked;

    private double _addPageButtonLeft = 0;
    public double AddPageButtonLeft
    {
        get => _addPageButtonLeft;
        set { _addPageButtonLeft = value; OnPropertyChanged(); }
    }

    private double _addPageButtonTop = 0;
    public double AddPageButtonTop
    {
        get => _addPageButtonTop;
        set { _addPageButtonTop = value; OnPropertyChanged(); }
    }

    private bool _isMainRadialActive = true;
    public bool IsMainRadialActive
    {
        get => _isMainRadialActive;
        set
        {
            _isMainRadialActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainRadialBorderBrush));
            OnPropertyChanged(nameof(MainRadialBorderThickness));
        }
    }

    public System.Windows.Media.Brush MainRadialBorderBrush => IsMainRadialActive
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#2AFFFFFF")!;

    public double MainRadialBorderThickness => IsMainRadialActive ? 2.8 : 1.0;

    public void SetActiveRadial(string? pageId)
    {
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            _currentPageId = pageId;
        }

        foreach (var ring in SubRings)
        {
            ring.IsActive = string.Equals(ring.PageId, pageId, StringComparison.OrdinalIgnoreCase);
        }

        var activePage = _pages.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))
            ?? AppSettingsStore.Load().RadialMenu?.Pages?.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (activePage != null)
        {
            ActiveTitle = $"轮盘：{activePage.Name}";
        }

        OnPropertyChanged(nameof(IsMainRadialActive));
        OnPropertyChanged(nameof(MainRadialBorderBrush));
        OnPropertyChanged(nameof(MainRadialBorderThickness));
        HostAssets.AppendLog($"[EditModeActive] Active radial set to: {pageId}, name={activePage?.Name ?? "(unknown)"}");
    }

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
            EnsureNoActivateStyle();

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
        HostAssets.AppendLog($"[RadialResidualDebug] ShowAtMouse start: isVisible={IsVisible}, _editModeLocked={_editModeLocked}, SubRings.Count={SubRings.Count}.");

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

        Width = 1400;
        Height = 1400;

        _isExecuting = false;
        _wasActivatedForEdit = false;
        _editModeLocked = false;
        _editInteractionActive = false;
        IsChildRingLocked = false;
        IsGrandChildRingLocked = false;
        IsGreatGrandChildRingLocked = false;
        IsChildCenterHovered = false;
        IsGrandChildCenterHovered = false;
        IsGreatGrandChildCenterHovered = false;
        IsGuideLineVisible = false;
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
        _shownTimestamp = Environment.TickCount64;
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

        BuildItems(_lastRadiusPixels);
        UpdateCenterText();
        ActiveTitle = "取消";

        PositionAroundCursor();
        RootGrid.Visibility = Visibility.Visible;
        Opacity = 1.0;

        if (!IsVisible)
        {
            Show();
        }

        _selectionTimer.Start();
        UpdateSelectionFromCursor();

        HostAssets.AppendLog($"Radial menu shown: page={_currentPageId}, process={_activeProcessName ?? "(none)"}, items={Items.Count}, center=({_centerPixels.X},{_centerPixels.Y}).");
    }

    private void RebuildItemsForCurrentLayout(string reason)
    {
        if (string.IsNullOrWhiteSpace(_currentPageId))
        {
            return;
        }

        // 普通日常模式下，尺寸改变不需要重建轮盘布局，只有编辑模式才需要重排
        if (reason == "size-changed" && !_editModeLocked)
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
        if (!IsVisible || _isExecuting)
        {
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

        if (IsCloseHoverActive)
        {
            HideIfAllowed();
            return;
        }

        if (_editModeLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _editModeLocked = true;
            }
            EnsureActivatedForEdit();
            LoadRadialMenuPages();
            _selectionTimer.Stop();
            UpdateEditModeState();
            OpenEditMenuForCurrentSelection();
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

        if (selectedSubItem?.Command != null)
        {
            HideIfAllowed();
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing subring item: index={selectedSubItem.Index}, command={selectedSubItem.Command.Title}.");
            _ = ExecuteCommandAfterForegroundRestoreAsync(selectedSubItem.Command, "radial-menu-subring");
            return;
        }

        if (selected?.Command != null)
        {
            if (!string.IsNullOrWhiteSpace(selected.ChildPageId))
            {
                HostAssets.AppendLog($"Radial menu release: parent child slot selected without child command, childPage={selected.ChildPageId}.");
                _selectionTimer.Start();
                return;
            }

            HideIfAllowed();
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing: index={selected.Index}, command={selected.Command.Title}.");
            _ = ExecuteCommandAfterForegroundRestoreAsync(selected.Command, "radial-menu");
            return;
        }

        if (_editModeLocked || _isPinned)
        {
            _selectionTimer.Start();
            UpdateSelectionFromCursor();
            return;
        }

        var cursorPoint = GetCursorWindowPoint();
        var center = GetMenuCenter();
        var dx = cursorPoint.X - center.X;
        var dy = cursorPoint.Y - center.Y;
        var distanceFromCenter = Math.Sqrt(dx * dx + dy * dy);
        var elapsedMs = Environment.TickCount64 - _shownTimestamp;

        // 极速点按唤出保护：仅在用户瞬间点按（<200ms）且鼠标留在中心死区未移动时，保持轮盘常驻以便鼠标点击操作
        if (elapsedMs < 200 && distanceFromCenter < _cachedDeadZonePixels && !_isExecuting)
        {
            HostAssets.AppendLog($"Radial menu release: quick tap detected ({elapsedMs}ms, dist={distanceFromCenter:F1}), keeping wheel open for click.");
            _selectionTimer.Start();
            UpdateSelectionFromCursor();
            return;
        }

        // 其余情况（落位在中心圆取消区、落位在轮盘及编辑菜单外、或手势划动松开取消）：自动消失
        HostAssets.AppendLog($"Radial menu release: dismissed on center/outside/empty (elapsed={elapsedMs}ms, dist={distanceFromCenter:F1}).");
        HideIfAllowed();
    }

    private async Task ExecuteCommandAfterForegroundRestoreAsync(CommandItem command, string launchSource)
    {
        InputHookService.MarkCapsLockAsUsed();
        try
        {
            if (_wasActivatedForEdit && _previousForegroundWindow != IntPtr.Zero)
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
            _wasActivatedForEdit = false;
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
        if (_editModeLocked || _editInteractionActive || _mainWindow.IsRadialPickerMode)
        {
            return;
        }

        if (!_isPinned)
        {
            _selectionTimer.Stop();
        }
        HideIfAllowed();
    }

    public void ActivateForEditInteraction()
    {
        _editInteractionActive = true;
        RootGrid.Visibility = Visibility.Visible;
        Opacity = 1.0;
        if (!IsVisible)
        {
            Show();
        }
    }

    private void BuildItems(int radius)
    {
        HostAssets.AppendLog($"[RadialResidualDebug] BuildItems start: _currentPageId={_currentPageId}, _editModeLocked={_editModeLocked}, SubRings.Count before={SubRings.Count}.");
        var effectiveRadius = Math.Clamp(radius - 10, 82, 96);
        Items.Clear();
        MiddleItems.Clear();
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

        // 1. 生成 3 层分隔线
        BuildSeparators(MainSeparators, center.X, center.Y, 36, 100, RadialMenuSettings.InnerSlotCount);
        BuildSeparators(MiddleSeparators, center.X, center.Y, 100, 165, RadialMenuSettings.MiddleSlotCount);
        BuildSeparators(OuterSeparators, center.X, center.Y, 165, 270, RadialMenuSettings.OuterSlotCount, isFadeOut: true);

        // 2. 第 1 层：内圈 8 方向槽位 (36 ~ 100)
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
                CreateSectorGeometry(center.X, center.Y, 36, 100, angleDegrees - 22.5, angleDegrees + 22.5),
                center.X,
                center.Y));
        }

        // 3. 第 2 层：中间层 16 槽位 (100 ~ 165)
        for (var offset = 0; offset < RadialMenuSettings.MiddleSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + offset;
            var angleDegrees = -90 + offset * 22.5;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 132 - 25;
            var y = center.Y + Math.Sin(angle) * 132 - 20;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            MiddleItems.Add(new RadialMenuItemViewModel(
                _currentPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                angleDegrees,
                RadialMenuRing.Middle,
                CreateSectorGeometry(center.X, center.Y, 100, 165, angleDegrees - 11.25, angleDegrees + 11.25),
                center.X,
                center.Y));
        }

        // 4. 第 3 层：最外层 8 方向槽位 (165 ~ 280，向外柔和渐变消融)
        for (var offset = 0; offset < RadialMenuSettings.OuterSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + RadialMenuSettings.MiddleSlotCount + offset;
            var angleDegrees = -90 + offset * 45.0;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 198 - 25;
            var y = center.Y + Math.Sin(angle) * 198 - 20;
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
                CreateSectorGeometry(center.X, center.Y, 165, 280, angleDegrees - 22.5, angleDegrees + 22.5),
                center.X,
                center.Y));
        }

        var currentIndex = _topLevelPages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < PaginationDots.Count; i++)
        {
            PaginationDots[i].IsSelected = (i == currentIndex);
        }

        if (_editModeLocked)
        {
            ExpandAllSubRingsInEditMode();
        }

        UpdateOuterWheelVisibility();
        HostAssets.AppendLog($"[RadialResidualDebug] BuildItems end: Items={Items.Count}, MiddleItems={MiddleItems.Count}, OuterItems={OuterItems.Count}, SubRings={SubRings.Count}.");
    }

    private void UpdateOuterWheelVisibility()
    {
        var old = _isOuterWheelVisible;
        // 编辑模式下是多轮盘平铺，不需要在屏幕中央显示静态单层外圈；普通模式下划向外圈时动态显现
        IsOuterWheelVisible = (!_editModeLocked) && _isOuterRingHoverActive;
        if (old != IsOuterWheelVisible)
        {
            HostAssets.AppendLog($"[RadialOuterLog] UpdateOuterWheelVisibility changed: locked={_editModeLocked}, hoverActive={_isOuterRingHoverActive} => IsOuterWheelVisible={IsOuterWheelVisible}");
        }
    }

    private void UpdateSelectionFromCursor(System.Windows.Point? preCalculatedPoint = null)
    {
        if (_editInteractionActive)
        {
            IsGuideLineVisible = false;
            return;
        }

        var cursorPoint = preCalculatedPoint ?? GetCursorWindowPoint();
        if (UpdateEditHoverState(cursorPoint))
        {
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsDeleteHoverActive = false;
            IsSearchHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            IsGuideLineVisible = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        IsEditHoverActive = false;
        IsPinHoverActive = false;
        IsAddHoverActive = false;
        IsDeleteHoverActive = false;
        IsSearchHoverActive = false;
        IsCloseHoverActive = false;

        var center = GetMenuCenter();

        // 优先检查所有展开的独立子环或放射子环命中！
        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            if (TryUpdateSubRingSelection(ring, cursorPoint))
            {
                SetSelectedItem(null); // 命中子环时清空主轮盘选中，避免选中态冲突
                Cursor = System.Windows.Input.Cursors.Hand;
                UpdateGuideLine(center.X, center.Y, cursorPoint.X, cursorPoint.Y);
                if (ring.SelectedItem != null)
                {
                    if (ring.SelectedItem.Ring == RadialMenuRing.Outer || ring.SelectedItem.Ring == RadialMenuRing.Middle)
                    {
                        var outerIdx = ring.SelectedItem.Index - RadialMenuSettings.InnerSlotCount;
                        if (outerIdx >= 0 && outerIdx < ring.OuterItems.Count)
                        {
                            if (!_editModeLocked)
                            {
                                ClearSubRingsAboveLevel(ring.Level + 1);
                                if (!string.IsNullOrWhiteSpace(ring.SelectedItem.ChildPageId))
                                {
                                    var slotCenterX = ring.SelectedItem.X + 25;
                                    var slotCenterY = ring.SelectedItem.Y + 20;
                                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _currentPageId };
                                    foreach (var r in SubRings.Take(i + 1)) visited.Add(r.PageId);
                                    RecursivelyBuildSubRings(ring.SelectedItem, ring.CenterX, ring.CenterY, slotCenterX, slotCenterY, ring.SelectedItem.AngleDegrees, ring.Level + 1, visited);
                                }
                            }
                        }
                    }
                    else
                    {
                        var innerIdx = ring.SelectedItem.Index;
                        if (innerIdx >= 0 && innerIdx < ring.Items.Count)
                        {
                            if (!_editModeLocked)
                            {
                                ClearSubRingsAboveLevel(ring.Level + 1);
                                if (!string.IsNullOrWhiteSpace(ring.SelectedItem.ChildPageId))
                                {
                                    var slotCenterX = ring.SelectedItem.X + 32;
                                    var slotCenterY = ring.SelectedItem.Y + 25;
                                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _currentPageId };
                                    foreach (var r in SubRings.Take(i + 1)) visited.Add(r.PageId);
                                    RecursivelyBuildSubRings(ring.SelectedItem, ring.CenterX, ring.CenterY, slotCenterX, slotCenterY, ring.SelectedItem.AngleDegrees, ring.Level + 1, visited);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (!_editModeLocked)
                    {
                        ClearSubRingsAboveLevel(ring.Level + 1);
                    }
                }
                return;
            }
        }

        var dx = cursorPoint.X - center.X;
        var dy = cursorPoint.Y - center.Y;
        var deadZone = _cachedDeadZonePixels;
        UpdateEditModeState();
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < deadZone)
        {
            _isOuterRingHoverActive = false;
            UpdateOuterWheelVisibility();
            SetSelectedItem(null);
            if (!_editModeLocked)
            {
                ClearSubRingsAboveLevel(1);
            }
            ActiveTitle = _editModeLocked ? "点击激活轮盘" : "取消";
            IsCenterHovered = true;
            IsGuideLineVisible = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        IsCenterHovered = false;

        // 引导线始终跟随光标，无论距离多远
        Cursor = System.Windows.Input.Cursors.Hand;
        UpdateGuideLine(center.X, center.Y, cursorPoint.X, cursorPoint.Y);

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        // 第 3 层：只要超出轮盘中圈 (R > 165)，且是这个方向，就默认视为该外圈槽位，无需距离上限！
        if (distance > 165)
        {
            _isOuterRingHoverActive = true;
            UpdateOuterWheelVisibility();

            var outerIndex = ((int)Math.Round((angle + 90) / 45.0) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
            var outerItem = OuterItems.ElementAtOrDefault(outerIndex);
            SetSelectedItem(outerItem);
            ActiveTitle = ResolveActiveTitle(outerItem?.Command?.Title, outerItem?.Command == null);
            HostAssets.AppendLog($"[RadialOuterLog] outerHit: dist={distance:0.#}, outerIdx={outerIndex}, outerItem={outerItem?.Title ?? "empty"}, isSel={outerItem?.IsSelected}, isSecVis={outerItem?.IsSectorVisible}, op={outerItem?.SectorOpacity}, brush={outerItem?.SectorBrush?.GetType().Name}");
            if (!string.IsNullOrWhiteSpace(outerItem?.ChildPageId))
            {
                ActiveTitle = _editModeLocked ? $"子环：{outerItem.ChildPageTitle}" : $"展开：{outerItem.ChildPageTitle}";
                if (!_editModeLocked)
                {
                    BuildSubRing(outerItem, center.X, center.Y, outerItem.AngleDegrees, 1);
                }
            }
            else
            {
                if (!_editModeLocked)
                {
                    ClearSubRingsAboveLevel(1);
                }
            }

            return;
        }

        _isOuterRingHoverActive = false;
        UpdateOuterWheelVisibility();

        // 第 2 层：中间层 16 槽位 (100 ~ 165)
        if (distance > 100)
        {
            var midIndex = ((int)Math.Round((angle + 90) / 22.5) % RadialMenuSettings.MiddleSlotCount + RadialMenuSettings.MiddleSlotCount) % RadialMenuSettings.MiddleSlotCount;
            var midItem = MiddleItems.ElementAtOrDefault(midIndex);
            SetSelectedItem(midItem);
            ActiveTitle = ResolveActiveTitle(midItem?.Command?.Title, midItem?.Command == null);
            if (!string.IsNullOrWhiteSpace(midItem?.ChildPageId))
            {
                ActiveTitle = _editModeLocked ? $"子环：{midItem.ChildPageTitle}" : $"展开：{midItem.ChildPageTitle}";
                if (!_editModeLocked)
                {
                    BuildSubRing(midItem, center.X, center.Y, midItem.AngleDegrees, 1);
                }
            }
            else
            {
                if (!_editModeLocked)
                {
                    ClearSubRingsAboveLevel(1);
                }
            }

            return;
        }

        // 第 1 层：内圈 8 方向 (36 ~ 100)
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = Items.ElementAtOrDefault(index);
        SetSelectedItem(item);
        ActiveTitle = ResolveActiveTitle(item?.Command?.Title, item?.Command == null);
        if (!string.IsNullOrWhiteSpace(item?.ChildPageId))
        {
            ActiveTitle = _editModeLocked ? $"子环：{item.ChildPageTitle}" : $"展开：{item.ChildPageTitle}";
            if (!_editModeLocked)
            {
                BuildSubRing(item, center.X, center.Y, item.AngleDegrees, 1);
            }
        }
        else
        {
            if (!_editModeLocked)
            {
                ClearSubRingsAboveLevel(1);
            }
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

    private record RadialSubtreeBounds(
        RadialMenuPageSettings Page,
        double MinX, double MaxX, double MinY, double MaxY)
    {
        public double Width => (MaxX - MinX) + 40.0;
        public double Height => (MaxY - MinY) + 40.0;
        public double OriginOffsetX => -MinX + 20.0;
        public double OriginOffsetY => -MinY + 20.0;
    }

    private RadialSubtreeBounds ComputeSubtreeBounds(RadialMenuPageSettings page, RadialMenuSettings settings)
    {
        double minX = -170;
        double maxX = 170;
        double minY = -170;
        double maxY = 170;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { page.Id };
        var items = _mainWindow.GetRadialMenuItems(page.Id);
        
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!string.IsNullOrWhiteSpace(item.ChildPageId))
            {
                var isOuter = i >= RadialMenuSettings.InnerSlotCount;
                var offsetAngleDegrees = isOuter ? (-90 + (i - RadialMenuSettings.InnerSlotCount) * 22.5) : (-90 + i * 45.0);
                var angleRad = offsetAngleDegrees * Math.PI / 180.0;
                double offsetDist = isOuter ? 270 : 220;

                double subCenterX = Math.Cos(angleRad) * offsetDist;
                double subCenterY = Math.Sin(angleRad) * offsetDist;

                minX = Math.Min(minX, subCenterX - 110);
                maxX = Math.Max(maxX, subCenterX + 110);
                minY = Math.Min(minY, subCenterY - 110);
                maxY = Math.Max(maxY, subCenterY + 110);

                if (!visited.Contains(item.ChildPageId))
                {
                    visited.Add(item.ChildPageId);
                    var childItems = _mainWindow.GetRadialMenuItems(item.ChildPageId);
                    for (int c = 0; c < childItems.Count; c++)
                    {
                        var grandChildItem = childItems[c];
                        if (!string.IsNullOrWhiteSpace(grandChildItem.ChildPageId))
                        {
                            var grandIsOuter = c >= RadialMenuSettings.InnerSlotCount;
                            var grandAngle = grandIsOuter ? (-90 + (c - RadialMenuSettings.InnerSlotCount) * 22.5) : (-90 + c * 45.0);
                            var grandAngleRad = grandAngle * Math.PI / 180.0;
                            double gCenterX = subCenterX + Math.Cos(grandAngleRad) * 220;
                            double gCenterY = subCenterY + Math.Sin(grandAngleRad) * 220;

                            minX = Math.Min(minX, gCenterX - 110);
                            maxX = Math.Max(maxX, gCenterX + 110);
                            minY = Math.Min(minY, gCenterY - 110);
                            maxY = Math.Max(maxY, gCenterY + 110);
                        }
                    }
                }
            }
        }

        return new RadialSubtreeBounds(page, minX, maxX, minY, maxY);
    }

    private void ExpandAllSubRingsInEditMode()
    {
        SubRings.Clear();
        var settings = AppSettingsStore.Load();
        var radialSettings = settings.RadialMenu ?? new RadialMenuSettings();
        var allPages = radialSettings.Pages ?? new List<RadialMenuPageSettings>();
        var childPageIdsSet = radialSettings.GetChildPageIdsSet();

        // 筛选所有独立的顶层大轮盘（不属于任何子环的独立页面）
        var topLevelPages = allPages
            .Where(p => !childPageIdsSet.Contains(p.Id))
            .ToList();

        if (topLevelPages.Count == 0 && allPages.Count > 0)
        {
            topLevelPages.Add(allPages[0]);
        }

        var visitedPageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 智能流式排版（充分考虑根轮盘及上下左右所有子环占用的包围盒）
        double screenWidth = ActualWidth > 400 ? ActualWidth : Width;
        const double startX = 60.0;
        const double startY = 60.0;
        double currentX = startX;
        double currentY = startY;
        double currentRowMaxHeight = 0;

        foreach (var page in topLevelPages)
        {
            var bounds = ComputeSubtreeBounds(page, radialSettings);

            // 如果当前行剩余空间不足以容纳该轮盘树的完整包围盒，自动折行
            if (currentX + bounds.Width > screenWidth - 40.0 && currentX > startX)
            {
                currentX = startX;
                currentY += currentRowMaxHeight + 40.0;
                currentRowMaxHeight = 0;
            }

            double cX = currentX + bounds.OriginOffsetX;
            double cY = currentY + bounds.OriginOffsetY;
            ClampRingCenter(ref cX, ref cY, 170);

            BuildStandaloneRadialRing(page, cX, cY, 0, visitedPageIds);

            currentX += bounds.Width + 40.0;
            currentRowMaxHeight = Math.Max(currentRowMaxHeight, bounds.Height);
        }

        // 计算末尾“新建轮盘”圆圈按钮的坐标（位于最后一个轮盘之后）
        const double addBtnSize = 200.0;
        if (currentX + addBtnSize > screenWidth - 40.0 && currentX > startX)
        {
            currentX = startX;
            currentY += currentRowMaxHeight + 40.0;
        }

        AddPageButtonLeft = currentX + 20.0;
        AddPageButtonTop = currentY + 70.0;

        // 默认激活当前选中的轮盘或第 1 个轮盘
        var activeId = string.IsNullOrWhiteSpace(_currentPageId) ? topLevelPages.FirstOrDefault()?.Id : _currentPageId;
        SetActiveRadial(activeId);

        HostAssets.AppendLog($"[EditModeDebug] ExpandAllSubRingsInEditMode flow packing complete: totalTopLevel={topLevelPages.Count}, SubRings.Count={SubRings.Count}.");
    }

    private void AddPageCircle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ShowAddRadialMenuContextMenu();
        e.Handled = true;
    }

    private void BuildStandaloneRadialRing(RadialMenuPageSettings page, double cX, double cY, double radialAngleDegrees, HashSet<string> visitedPageIds)
    {
        if (visitedPageIds.Contains(page.Id))
        {
            return;
        }
        visitedPageIds.Add(page.Id);

        var items = _mainWindow.GetRadialMenuItems(page.Id);
        var ring = new RadialMenuNestedRingViewModel
        {
            PageId = page.Id,
            Level = 1,
            Title = string.IsNullOrWhiteSpace(page.Name) ? "轮盘" : page.Name,
            CenterX = cX,
            CenterY = cY,
            ParentX = 0,
            ParentY = 0,
            IsLocked = true,
            IsStandaloneRadial = true
        };

        // 1. 内圈分隔线 (8条) 与 外圈分隔线 (16条)
        BuildSeparators(ring.Separators, cX, cY, 36, 100, RadialMenuSettings.InnerSlotCount);
        BuildSeparators(ring.OuterSeparators, cX, cY, 100, 165, RadialMenuSettings.OuterSlotCount);

        // 2. 内圈 8 个槽位
        const double innerRadius = 72;
        for (var index = 0; index < RadialMenuSettings.InnerSlotCount; index++)
        {
            var childAngleDegrees = -90 + index * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = cX + Math.Cos(childAngle) * innerRadius - 32;
            var y = cY + Math.Sin(childAngle) * innerRadius - 25;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            var vm = new RadialMenuItemViewModel(
                page.Id,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.Inner,
                CreateSectorGeometry(cX, cY, 36, 100, childAngleDegrees - 22.5, childAngleDegrees + 22.5));
            ring.Items.Add(vm);
        }

        // 3. 外圈 8 个槽位
        const double outerRadius = 132;
        for (var offset = 0; offset < RadialMenuSettings.OuterSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + offset;
            var childAngleDegrees = -90 + offset * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = cX + Math.Cos(childAngle) * outerRadius - 25;
            var y = cY + Math.Sin(childAngle) * outerRadius - 20;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            var vm = new RadialMenuItemViewModel(
                page.Id,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.Outer,
                CreateSectorGeometry(cX, cY, 100, 165, childAngleDegrees - 22.5, childAngleDegrees + 22.5));
            ring.OuterItems.Add(vm);
        }

        SubRings.Add(ring);

        // 4. 如果该独立轮盘自身有子环，继续向外放射展开！
        var subRingSources = ring.Items.Concat(ring.OuterItems).Where(it => !string.IsNullOrWhiteSpace(it.ChildPageId)).ToList();
        foreach (var childItem in subRingSources)
        {
            var cSlotCenterX = childItem.X + (childItem.Ring == RadialMenuRing.Outer ? 25 : 32);
            var cSlotCenterY = childItem.Y + (childItem.Ring == RadialMenuRing.Outer ? 20 : 25);
            RecursivelyBuildSubRings(childItem, cX, cY, cSlotCenterX, cSlotCenterY, childItem.AngleDegrees, 2, visitedPageIds);
        }
    }

    private void RecursivelyBuildSubRings(RadialMenuItemViewModel parent, double parentCenterX, double parentCenterY, double parentSlotCenterX, double parentSlotCenterY, double parentAngleDegrees, int level, HashSet<string> visitedPageIds)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId) || visitedPageIds.Contains(parent.ChildPageId) || level > 3)
        {
            HostAssets.AppendLog($"[EditModeDebug] RecursivelyBuildSubRings skipped: childPageId={parent.ChildPageId}, visited={visitedPageIds.Contains(parent.ChildPageId ?? string.Empty)}, level={level}");
            return;
        }

        visitedPageIds.Add(parent.ChildPageId);

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);
        var angle = parentAngleDegrees * Math.PI / 180.0;
        
        // 动态交错轨道算法：相邻子环采用近远交错排布，中心距离至少 285~360px，彻底消除物理重叠
        double offsetDistance = 285;
        if (parent.Ring == RadialMenuRing.Outer)
        {
            offsetDistance = (parent.Index % 2 == 0) ? 310 : 390;
        }
        else
        {
            offsetDistance = (parent.Index % 2 == 0) ? 285 : 360;
        }
        if (level > 1)
        {
            offsetDistance = 280;
        }

        double cX = parentCenterX + Math.Cos(angle) * offsetDistance;
        double cY = parentCenterY + Math.Sin(angle) * offsetDistance;
        ClampRingCenter(ref cX, ref cY, 112);
        HostAssets.AppendLog($"[EditModeDebug] RecursivelyBuildSubRings: childPageId={parent.ChildPageId}, itemsCount={items.Count}, cX={cX:F1}, cY={cY:F1}, level={level}");

        var ring = new RadialMenuNestedRingViewModel
        {
            PageId = parent.ChildPageId,
            Level = level,
            Title = parent.ChildPageTitle,
            CenterX = cX,
            CenterY = cY,
            ParentX = parentSlotCenterX,
            ParentY = parentSlotCenterY,
            IsLocked = true
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
            var vm = new RadialMenuItemViewModel(
                parent.ChildPageId,
                index,
                command,
                childPageId,
                ResolvePageName(childPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.Child,
                CreateSectorGeometry(cX, cY, 36, 100, childAngleDegrees - 22.5, childAngleDegrees + 22.5));
            ring.Items.Add(vm);
        }

        SubRings.Add(ring);

        // 递归展开更深层级的子环（如二级、三级子环）
        foreach (var childItem in ring.Items.Where(it => !string.IsNullOrWhiteSpace(it.ChildPageId)))
        {
            var cSlotCenterX = childItem.X + 32;
            var cSlotCenterY = childItem.Y + 25;
            RecursivelyBuildSubRings(childItem, cX, cY, cSlotCenterX, cSlotCenterY, childItem.AngleDegrees, level + 1, visitedPageIds);
        }
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
        double offsetDistance = (level == 1 && parent.Ring == RadialMenuRing.Outer) ? 310 : 285;
        double cX = parentCenterX + Math.Cos(angle) * offsetDistance;
        double cY = parentCenterY + Math.Sin(angle) * offsetDistance;
        ClampRingCenter(ref cX, ref cY, 112);

        var ring = new RadialMenuNestedRingViewModel
        {
            PageId = parent.ChildPageId,
            Level = level,
            Title = parent.ChildPageTitle,
            CenterX = cX,
            CenterY = cY,
            ParentX = parent.X + 32,
            ParentY = parent.Y + 25
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
            ring.OuterItems.Clear();
            ring.Separators.Clear();
            ring.OuterSeparators.Clear();
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

        double maxRadius = ring.IsStandaloneRadial ? 280.0 : (ring.OuterItems.Count > 0 ? 280.0 : 120.0);
        if (distance > maxRadius)
        {
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
            ActiveTitle = _editModeLocked ? $"{ring.Title}" : "返回上一级";
            return true;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        RadialMenuItemViewModel? item = null;

        if (ring.OuterItems.Count > 0 && distance > 100)
        {
            var outerIndex = ((int)Math.Round((angle + 90) / 45.0) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
            item = ring.OuterItems.ElementAtOrDefault(outerIndex);
        }
        else
        {
            var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
            item = ring.Items.ElementAtOrDefault(index);
        }
        
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



    private static void BuildSeparators(ObservableCollection<RadialSeparatorViewModel> target, double centerX, double centerY, double innerRadius, double outerRadius, int count, bool isFadeOut = false)
    {
        target.Clear();
        var step = 360.0 / count;
        for (var index = 0; index < count; index++)
        {
            var angle = (-90 - step / 2 + index * step) * Math.PI / 180.0;
            var x1 = centerX + Math.Cos(angle) * innerRadius;
            var y1 = centerY + Math.Sin(angle) * innerRadius;
            var x2 = centerX + Math.Cos(angle) * outerRadius;
            var y2 = centerY + Math.Sin(angle) * outerRadius;

            System.Windows.Media.Brush? strokeBrush = null;
            if (isFadeOut)
            {
                var startX = Math.Abs(x2 - x1) < 1e-4 ? 0.5 : (x1 < x2 ? 0.0 : 1.0);
                var startY = Math.Abs(y2 - y1) < 1e-4 ? 0.5 : (y1 < y2 ? 0.0 : 1.0);
                var endX = Math.Abs(x2 - x1) < 1e-4 ? 0.5 : (x1 < x2 ? 1.0 : 0.0);
                var endY = Math.Abs(y2 - y1) < 1e-4 ? 0.5 : (y1 < y2 ? 1.0 : 0.0);

                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    StartPoint = new System.Windows.Point(startX, startY),
                    EndPoint = new System.Windows.Point(endX, endY),
                    GradientStops =
                    [
                        new GradientStop(System.Windows.Media.Color.FromArgb(160, 148, 163, 184), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(80, 148, 163, 184), 0.45),
                        new GradientStop(System.Windows.Media.Color.FromArgb(0, 148, 163, 184), 1.0)
                    ]
                };
                brush.Freeze();
                strokeBrush = brush;
            }
            else
            {
                strokeBrush = (System.Windows.Media.Brush?)System.Windows.Application.Current?.TryFindResource("BrushRadialBorder") 
                    ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255));
            }

            target.Add(new RadialSeparatorViewModel(x1, y1, x2, y2, strokeBrush));
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
        HostAssets.AppendLog($"[PickerLog] RadialMenu LeftButtonDown: isPickerMode={_mainWindow.IsRadialPickerMode}, popupOpen={_mainWindow.SearchScopePopup?.IsOpen}, isEditLocked={_editModeLocked}, pageStack={_pageStack.Count}, point=({clickPoint.X:F1},{clickPoint.Y:F1}).");
        if (_mainWindow.IsRadialPickerMode || _mainWindow.SearchScopePopup?.IsOpen == true)
        {
            return;
        }

        var center = GetMenuCenter();
        var dxMain = clickPoint.X - center.X;
        var dyMain = clickPoint.Y - center.Y;
        var distMain = Math.Sqrt(dxMain * dxMain + dyMain * dyMain);

        if (_editModeLocked)
        {
            // 1. 检查是否点击在主轮盘内 (distMain <= 280)
            if (distMain <= 280)
            {
                if (distMain <= 36)
                {
                    // 点击中心：如果有子页面返回上一级
                    if (_pageStack.Count > 0)
                    {
                        ReturnToParentPage();
                    }
                    e.Handled = true;
                    return;
                }

                // 点击在主轮盘槽位区域：触发当前槽位编辑或添加
                if (_selectedItem != null)
                {
                    var target = new RadialEditTarget(_currentPageId, _selectedItem.Index, _selectedItem);
                    if (_selectedItem.IsEmpty)
                    {
                        ShowAddMenuForTarget(target);
                    }
                    else
                    {
                        OpenEditMenuForTarget(target);
                    }
                }
                e.Handled = true;
                return;
            }

            // 2. 检查是否点击在子环星系内
            for (int i = SubRings.Count - 1; i >= 0; i--)
            {
                var ring = SubRings[i];
                var dx = clickPoint.X - ring.CenterX;
                var dy = clickPoint.Y - ring.CenterY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= 280)
                {
                    if (dist <= 36)
                    {
                        SetActiveRadial(ring.PageId);
                    }
                    else if (ring.SelectedItem != null)
                    {
                        var target = new RadialEditTarget(ring.PageId, ring.SelectedItem.Index, ring.SelectedItem);
                        if (ring.SelectedItem.IsEmpty)
                        {
                            ShowAddMenuForTarget(target);
                        }
                        else
                        {
                            OpenEditMenuForTarget(target);
                        }
                    }
                    e.Handled = true;
                    return;
                }
            }

            // 3. 检查是否点击在底部工具栏区域
            if (Math.Abs(clickPoint.X - center.X) < 220 && (clickPoint.Y - center.Y) >= 280 && (clickPoint.Y - center.Y) <= 450)
            {
                e.Handled = true;
                return;
            }

            // 4. 在编辑模式下，点击任何空白区域均不退出编辑模式，保护编辑工作！
            // （退出编辑模式的唯一方式：按 ESC 键 或 点击工具栏编辑铅笔按钮）
            e.Handled = true;
            return;
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
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _editModeLocked = true;
            }
            EnsureActivatedForEdit();
            if (!string.IsNullOrWhiteSpace(item.ChildPageId))
            {
                // 点击了带有子环的槽位图标：标识并激活对应子环，对应子环外边高亮蓝色边，且放到最上面！
                SetActiveRadial(item.ChildPageId);
            }
            else
            {
                // 点击普通槽位：激活该槽位所在的轮盘！
                SetActiveRadial(item.OwnerPageId);
            }

            _selectionTimer.Stop();
            UpdateEditModeState();
            // 左键仅做激活/选择，右键才弹出编辑菜单
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

        EnsureActivatedForEdit();
        SetSelectedForItem(item);
        if (!string.IsNullOrWhiteSpace(item.ChildPageId))
        {
            SetActiveRadial(item.ChildPageId);
        }
        else
        {
            SetActiveRadial(item.OwnerPageId);
        }
        _selectionTimer.Stop();
        UpdateEditModeState();
        var target = new RadialEditTarget(item.OwnerPageId, item.Index, item);
        OpenEditMenuForTarget(target);
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
        foreach (var item in MiddleItems)
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
        var dangerBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BrushDanger") ?? System.Windows.Media.Brushes.Red;

        // 1. 重命名
        var renameItem = new MenuItem
        {
            Header = "重命名",
            Icon = CreateMenuIcon("pencil", normalBrush)
        };
        renameItem.Click += (_, _) =>
        {
            _isOpeningSubDialog = true;
            Dispatcher.BeginInvoke(new Action(() => RenameRadialPage(page.Id)));
        };
        menu.Items.Add(renameItem);

        // 2. 克隆轮盘（以当前轮盘为母本创建副本）
        var cloneItem = new MenuItem
        {
            Header = "克隆轮盘",
            Icon = CreateMenuIcon("copy", normalBrush)
        };
        cloneItem.Click += (_, _) =>
        {
            CloneRadialPage(page);
        };
        menu.Items.Add(cloneItem);

        // 3. 绑定应用（参考全局黑名单/应用选择器）
        var currentBound = string.IsNullOrWhiteSpace(page.ContextProcessName) ? "全局通用" : page.ContextProcessName;
        var bindItem = new MenuItem
        {
            Header = $"绑定应用 ({currentBound})",
            Icon = CreateMenuIcon("gear", normalBrush)
        };
        bindItem.Click += (_, _) =>
        {
            _isOpeningSubDialog = true;
            Dispatcher.BeginInvoke(new Action(() => BindProcessForRadialPage(page)));
        };
        menu.Items.Add(bindItem);

        menu.Items.Add(new Separator());

        // 4. 删除轮盘
        var deleteItem = new MenuItem
        {
            Header = "删除轮盘",
            Icon = CreateMenuIcon("trash", dangerBrush),
            Foreground = dangerBrush
        };
        deleteItem.Click += (_, _) =>
        {
            DeleteRadialPage(page);
        };
        menu.Items.Add(deleteItem);

        menu.PlacementTarget = this;
        menu.Closed += (_, _) =>
        {
            HostAssets.AppendLog($"[RadialMenuLog] PageCenterContextMenu closed: isOpeningSubDialog={_isOpeningSubDialog}");
            if (!_isOpeningSubDialog)
            {
                _editInteractionActive = false;
                if (IsVisible && !_mainWindow.IsRadialPickerMode)
                {
                    Activate();
                    _selectionTimer.Start();
                }
            }
        };

        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        ActiveTitle = $"轮盘：{page.Name}";
    }

    private void CloneRadialPage(RadialMenuPageSettings page)
    {
        var settings = AppSettingsStore.Load();
        var radialSettings = settings.RadialMenu ?? new RadialMenuSettings();
        radialSettings.Pages ??= new List<RadialMenuPageSettings>();

        var clonedPage = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{page.Name} (副本)",
            Slots = page.Slots != null ? new List<string?>(page.Slots) : Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            SlotTitles = page.SlotTitles != null ? new List<string?>(page.SlotTitles) : Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            ChildPageIds = page.ChildPageIds != null ? new List<string?>(page.ChildPageIds) : Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList(),
            ContextProcessName = page.ContextProcessName,
            ContextDisplayName = page.ContextDisplayName
        };

        radialSettings.Pages.Add(clonedPage);
        settings.RadialMenu = radialSettings;
        AppSettingsStore.Save(settings);

        LoadRadialMenuPages();
        ExpandAllSubRingsInEditMode();
        SetActiveRadial(clonedPage.Id);
        ActiveTitle = $"已克隆轮盘：{clonedPage.Name}";
        HostAssets.AppendLog($"[RadialMenuLog] Radial page cloned: original={page.Name} ({page.Id}), new={clonedPage.Name} ({clonedPage.Id})");
    }

    private void BindProcessForRadialPage(RadialMenuPageSettings page)
    {
        _isOpeningSubDialog = true;
        _editInteractionActive = true;
        var defaultProcess = page.ContextProcessName ?? _activeProcessName ?? "explorer";
        var initialList = string.IsNullOrWhiteSpace(page.ContextProcessName)
            ? new List<string>()
            : new List<string> { page.ContextProcessName };

        var inputWindow = new ProcessPickerWindow("绑定应用", $"请选择【{page.Name}】绑定的专属应用进程（留空表示全局通用）：", defaultProcess, initialList);
        if (inputWindow.ShowDialog() == true)
        {
            var selected = inputWindow.Blacklist.FirstOrDefault()?.ProcessName;
            var settings = AppSettingsStore.Load();
            var targetPage = settings.RadialMenu?.Pages?.FirstOrDefault(p => p.Id.Equals(page.Id, StringComparison.OrdinalIgnoreCase));
            if (targetPage != null)
            {
                targetPage.ContextProcessName = string.IsNullOrWhiteSpace(selected) ? null : selected;
                targetPage.ContextDisplayName = string.IsNullOrWhiteSpace(selected) ? null : selected;
                AppSettingsStore.Save(settings);
                LoadRadialMenuPages();
                ExpandAllSubRingsInEditMode();
                SetActiveRadial(targetPage.Id);
                ActiveTitle = string.IsNullOrWhiteSpace(selected)
                    ? $"已将【{page.Name}】设为全局通用轮盘"
                    : $"已将【{page.Name}】绑定到应用：{selected}";
                HostAssets.AppendLog($"[RadialMenuLog] Radial page process bound: page={page.Name}, process={selected ?? "(Global)"}");
            }
        }

        _isOpeningSubDialog = false;
        _editInteractionActive = false;
        if (IsVisible && !_mainWindow.IsRadialPickerMode)
        {
            Activate();
            _selectionTimer.Start();
        }
    }

    private bool ShowForegroundConfirmDialog(string message, string title)
    {
        _isOpeningSubDialog = true;
        _editInteractionActive = true;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            RadialMenuNativeMethods.SetForegroundWindow(hwnd);

            uint flags = RadialMenuNativeMethods.MB_YESNO |
                         RadialMenuNativeMethods.MB_ICONWARNING |
                         RadialMenuNativeMethods.MB_DEFBUTTON2 |
                         RadialMenuNativeMethods.MB_SETFOREGROUND |
                         RadialMenuNativeMethods.MB_TOPMOST;

            int result = RadialMenuNativeMethods.MessageBox(hwnd, message, title, flags);
            return result == RadialMenuNativeMethods.IDYES;
        }
        finally
        {
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
            {
                Activate();
                _selectionTimer.Start();
            }
        }
    }

    private void DeleteRadialPage(RadialMenuPageSettings page)
    {
        var settings = AppSettingsStore.Load();
        var radialSettings = settings.RadialMenu ?? new RadialMenuSettings();
        var allPages = radialSettings.Pages ?? new List<RadialMenuPageSettings>();
        var targetPage = allPages.FirstOrDefault(p => p.Id.Equals(page.Id, StringComparison.OrdinalIgnoreCase));
        if (targetPage == null)
        {
            return;
        }

        // 保护：如果只剩一个顶层大轮盘，不允许删除
        var childPageIdsSet = radialSettings.GetChildPageIdsSet();
        var topLevelPages = allPages.Where(p => !childPageIdsSet.Contains(p.Id)).ToList();
        if (topLevelPages.Count <= 1 && topLevelPages.Any(p => p.Id.Equals(page.Id, StringComparison.OrdinalIgnoreCase)))
        {
            ActiveTitle = "无法删除最后一个顶层轮盘";
            return;
        }

        // 强制前台置顶获得焦点的确认对话框
        if (!ShowForegroundConfirmDialog($"确定要删除轮盘“{page.Name}”吗？", "确认删除"))
        {
            ActiveTitle = "已取消删除";
            return;
        }

        allPages.Remove(targetPage);
        // 清理所有轮盘槽位中对该子环页面的引用
        foreach (var p in allPages)
        {
            if (p.ChildPageIds != null)
            {
                for (int i = 0; i < p.ChildPageIds.Count; i++)
                {
                    if (string.Equals(p.ChildPageIds[i], page.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        p.ChildPageIds[i] = null;
                    }
                }
            }
        }

        settings.RadialMenu = radialSettings;
        AppSettingsStore.Save(settings);

        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");

        LoadRadialMenuPages();
        _currentPageId = _pages.FirstOrDefault()?.Id ?? string.Empty;
        if (_editModeLocked)
        {
            ExpandAllSubRingsInEditMode();
        }
        else
        {
            BuildItems(_lastRadiusPixels);
        }
        ActiveTitle = $"已删除轮盘：{page.Name}";
        HostAssets.AppendLog($"[RadialMenuLog] Radial page deleted: {page.Name} ({page.Id})");
    }

    private static bool IsPointNear(System.Windows.Point point, double centerX, double centerY, double radius)
    {
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        return Math.Sqrt(dx * dx + dy * dy) <= radius;
    }

    private void RenameRadialPage(string pageId)
    {
        _isOpeningSubDialog = true;
        _editInteractionActive = true;
        try
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
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
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
        finally
        {
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
            {
                Activate();
                _selectionTimer.Start();
            }
        }
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
            ActiveTitle = "点击激活轮盘";
            _selectionTimer.Start();
            return;
        }

        OpenEditMenuForTarget(target);
    }

    private void OpenEditMenuForTarget(RadialEditTarget target)
    {
        EnsureActivatedForEdit();
        _editInteractionActive = true;
        _selectionTimer.Stop();
        IsGuideLineVisible = false;
        var menu = BuildEditContextMenu(target);
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Relative;
        menu.PlacementRectangle = new Rect(target.Item.X + 25, target.Item.Y + 20, 0, 0);
        menu.Closed += (_, _) =>
        {
            HostAssets.AppendLog($"[RadialMenuLog] EditMenu closed: isOpeningSubDialog={_isOpeningSubDialog}");
            if (!_isOpeningSubDialog)
            {
                _editInteractionActive = false;
                if (IsVisible && !_mainWindow.IsRadialPickerMode)
                {
                    Activate();
                    _selectionTimer.Start();
                }
            }
        };

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
            _isOpeningSubDialog = true;
            Dispatcher.BeginInvoke(new Action(() => OpenSearchPickerForTarget(target)));
        };
        parentMenu.Items.Add(existingExtensionItem);

        // 2. 新建小程序 (一级独立项)
        var createNewExtensionItem = new MenuItem
        {
            Header = "新建小程序",
            Icon = CreateMenuIcon("plus", normalBrush)
        };
        createNewExtensionItem.Click += (_, _) =>
        {
            _isOpeningSubDialog = true;
            Dispatcher.BeginInvoke(new Action(() => CreateNewExtensionForTarget(target)));
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
            _isOpeningSubDialog = true;
            Dispatcher.BeginInvoke(new Action(() => SetSimulatedKeyForTarget(target)));
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
                Dispatcher.BeginInvoke(new Action(() => AddChildPageToTarget(target)));
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
        HostAssets.AppendLog($"[RadialMenuLog] ShowAddMenuForTarget: page={target.PageId}, index={target.Index}");
        EnsureActivatedForEdit();
        _editInteractionActive = true;
        _selectionTimer.Stop();
        IsGuideLineVisible = false;
        var menu = BuildAddMenu(target);
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Relative;
        menu.PlacementRectangle = new Rect(target.Item.X + 25, target.Item.Y + 20, 0, 0);
        menu.Closed += (_, _) =>
        {
            HostAssets.AppendLog($"[RadialMenuLog] Add menu closed: isOpeningSubDialog={_isOpeningSubDialog}");
            if (!_isOpeningSubDialog)
            {
                _editInteractionActive = false;
                if (IsVisible && !_mainWindow.IsRadialPickerMode)
                {
                    Activate();
                    _selectionTimer.Start();
                }
            }
        };

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
            editItem.Click += (_, _) =>
            {
                _isOpeningSubDialog = true;
                Dispatcher.BeginInvoke(new Action(() => EditSlotContentFromTarget(target)));
            };
            menu.Items.Add(editItem);

            var clearItem = new MenuItem
            {
                Header = "删除",
                Icon = CreateMenuIcon("trash", dangerBrush)
            };
            clearItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => ClearSlotContentFromTarget(target)));
            menu.Items.Add(clearItem);

            var cutItem = new MenuItem
            {
                Header = "剪切槽位",
                Icon = CreateMenuIcon("cut", normalBrush)
            };
            cutItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => CutRadialSlot(target)));
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
        HostAssets.AppendLog($"[PickerLog] OpenSearchPickerForTarget START: page={target.PageId}, index={target.Index}.");
        _editInteractionActive = true;
        try
        {
            var result = await _mainWindow.ShowForRadialPickerAsync(!target.Item.HasChildPage, this);
            HostAssets.AppendLog($"[PickerLog] ShowForRadialPickerAsync RETURNED: resultIsNull={(result == null)}, action={result?.Action}, commandTitle='{result?.Command?.Title}', extId='{result?.Command?.ExtensionId}', openTarget='{result?.Command?.OpenTarget}'.");
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

            var effectiveId = !string.IsNullOrWhiteSpace(result.Command.ExtensionId)
                ? result.Command.ExtensionId
                : (!string.IsNullOrWhiteSpace(result.Command.OpenTarget) ? $"result::{result.Command.OpenTarget}" : null);

            if (string.IsNullOrWhiteSpace(effectiveId))
            {
                HostAssets.AppendLog("Radial edit assigned command FAIL: effectiveId is null or empty.");
                return;
            }

            SaveRadialSlotCommand(target.PageId, target.Index, effectiveId, string.Empty);
            HostAssets.AppendLog($"Radial edit assigned command: page={target.PageId}, index={target.Index + 1}, command={result.Command.Title}, id={effectiveId}.");

            if (!IsVisible)
            {
                Show();
            }
            Activate();
            RebuildItemsForCurrentLayout("assigned-picker-command");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[PickerLog] OpenSearchPickerForTarget EXCEPTION: {ex}");
        }
        finally
        {
            _editInteractionActive = false;
            UpdateCenterText();
        }
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
        HostAssets.AppendLog($"[SetSimulatedKeyLog] SetSimulatedKeyForTarget: page={target.PageId}, index={target.Index}");
        _isOpeningSubDialog = true;
        _editInteractionActive = true;
        try
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
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            HostAssets.AppendLog($"[SetSimulatedKeyLog] Before ShowDialog: Topmost={dialog.Topmost}");
            var result = dialog.ShowDialog();
            HostAssets.AppendLog($"[SetSimulatedKeyLog] After ShowDialog: result={result}, shortcut='{dialog.ShortcutText}'");

            if (result != true)
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
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[SetSimulatedKeyLog] Exception in SetSimulatedKeyForTarget: {ex}");
        }
        finally
        {
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
            {
                Activate();
                _selectionTimer.Start();
            }
        }
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
        ActiveTitle = "编辑已保存";
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

        HostAssets.AppendLog($"[RadialMenuLog] TryHandleEmptySlotRelease on empty slot: page={item.OwnerPageId}, index={item.Index}");
        EnsureActivatedForEdit();
        _editInteractionActive = true;
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
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        HostAssets.AppendLog($"Radial menu pin toggled: pinned={_isPinned}.");
    }

    private void ToggleEditModeState()
    {
        _editModeLocked = !_editModeLocked;
        if (_editModeLocked)
        {
            EnsureActivatedForEdit();
            LoadRadialMenuPages();
            if (string.IsNullOrEmpty(_currentPageId) || !_pages.Any(p => p.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase)))
            {
                if (_pages.Count > 0)
                {
                    _currentPageId = _pages[0].Id;
                }
            }

            // 编辑模式全屏覆盖当前屏幕工作区，实现以屏幕左上角为原点的整齐网格排布
            var screen = System.Windows.Forms.Screen.FromPoint(Forms.Cursor.Position);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                dpiScaleX = dpi.DpiScaleX;
                dpiScaleY = dpi.DpiScaleY;
            }
            catch { }
            Left = screen.WorkingArea.Left / dpiScaleX;
            Top = screen.WorkingArea.Top / dpiScaleY;
            Width = screen.WorkingArea.Width / dpiScaleX;
            Height = screen.WorkingArea.Height / dpiScaleY;
        }
        else
        {
            Width = 1400;
            Height = 1400;
            LoadRadialMenuPages();
            SubRings.Clear();
            PositionAroundCursor();
        }
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        UpdateEditModeState();
        OnPropertyChanged(nameof(IsEditModeLocked));
        OnPropertyChanged(nameof(IsCenterWheelVisible));
        OnPropertyChanged(nameof(EditButtonBrush));
        OnPropertyChanged(nameof(HasSubRings));
        HostAssets.AppendLog($"Radial menu edit mode toggled: locked={_editModeLocked}, expandedSubRings={SubRings.Count}.");
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
        EnsureActivatedForEdit();
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
        var radialSettings = settings.RadialMenu ?? new RadialMenuSettings();
        var allPages = radialSettings.Pages ?? new List<RadialMenuPageSettings>();
        var pageToDelete = allPages.FirstOrDefault(p => p.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        if (pageToDelete == null)
        {
            return;
        }

        DeleteRadialPage(pageToDelete);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSearchRadialMenuContextMenu();
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editModeLocked)
        {
            _editModeLocked = false;
            _selectionTimer.Stop();
            SubRings.Clear();
            BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
            UpdateEditModeState();
            OnPropertyChanged(nameof(IsEditModeLocked));
            OnPropertyChanged(nameof(EditButtonBrush));
            OnPropertyChanged(nameof(HasSubRings));
            Dispatcher.InvokeAsync(() => Hide(), DispatcherPriority.Render);
        }
        else
        {
            Hide();
        }
        e.Handled = true;
    }

    private void ShowSearchRadialMenuContextMenu()
    {
        EnsureActivatedForEdit();
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
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
        HostAssets.AppendLog($"[RadialResidualDebug] Hide called: _editModeLocked={_editModeLocked}, SubRings.Count={SubRings.Count}, Opacity={Opacity}.");
        if (_wasActivatedForEdit && _previousForegroundWindow != IntPtr.Zero)
        {
            try
            {
                RadialMenuNativeMethods.SetForegroundWindow(_previousForegroundWindow);
            }
            catch { }
        }
        _wasActivatedForEdit = false;
        _editModeLocked = false;
        _editInteractionActive = false;
        IsGuideLineVisible = false;
        OnPropertyChanged(nameof(IsEditModeLocked));
        OnPropertyChanged(nameof(EditButtonBrush));
        Opacity = 0;
        RootGrid.Visibility = Visibility.Hidden;

        // 默认状态清理工作，还原页面与应用关联属性，消除上一次轮盘的“遗像”残留
        _activeProcessName = null;
        _currentPageId = "default";
        _pageStack.Clear();

        // 重置 UI 文本及绑定的图像，确保其在下一次呼出前即为默认初始态
        PageDisplaySummary = string.Empty;
        PageTitle = "燕环";
        CenterIcon = null;
        ActiveTitle = "取消";

        // 彻底清空图形缓存项与全景子环星系，消除残影
        Items.Clear();
        OuterItems.Clear();
        ChildItems.Clear();
        GrandChildItems.Clear();
        SubRings.Clear();

        UpdateEditModeState();
        UpdateLayout();

        // 将窗口移到屏幕外，彻底阻断 DWM 缓存残影闪烁
        Left = -32000;
        Top = -32000;

        base.Hide();
        MemoryOptimizationService.OptimizeMemoryInBackground();
        HostAssets.AppendLog($"[RadialResidualDebug] Hide finished: Left={Left}, Top={Top}, SubRings.Count={SubRings.Count}.");
    }

    private void EnsureNoActivateStyle()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var style = RadialMenuNativeMethods.GetWindowLongPtr(handle, RadialMenuNativeMethods.GWL_EXSTYLE);
            RadialMenuNativeMethods.SetWindowLongPtr(
                handle,
                RadialMenuNativeMethods.GWL_EXSTYLE,
                new IntPtr(style.ToInt64() | RadialMenuNativeMethods.WS_EX_TOOLWINDOW | RadialMenuNativeMethods.WS_EX_NOACTIVATE));
        }
        catch
        {
            // Best effort
        }
    }

    private void EnsureActivatedForEdit()
    {
        _wasActivatedForEdit = true;
        try
        {
            Activate();
        }
        catch
        {
            // Best effort
        }
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
        foreach (var slot in MiddleItems)
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

        if (distanceMain > deadZone)
        {
            var angle = Math.Atan2(dyMain, dxMain) * 180.0 / Math.PI;
            if (distanceMain > 165)
            {
                var outerIndex = ((int)Math.Round((angle + 90) / 45.0) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
                return OuterItems.ElementAtOrDefault(outerIndex);
            }
            else if (distanceMain > 100)
            {
                var midIndex = ((int)Math.Round((angle + 90) / 22.5) % RadialMenuSettings.MiddleSlotCount + RadialMenuSettings.MiddleSlotCount) % RadialMenuSettings.MiddleSlotCount;
                return MiddleItems.ElementAtOrDefault(midIndex);
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
    private readonly double _centerX;
    private readonly double _centerY;
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
        Geometry? sectorGeometry = null,
        double centerX = 0,
        double centerY = 0)
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
        _centerX = centerX;
        _centerY = centerY;

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

    public System.Windows.Media.Brush SectorBrush
    {
        get
        {
            if (Ring == RadialMenuRing.Outer)
            {
                return CreateOuterGradientSectorBrush();
            }

            return IsEmpty
                ? GetThemeBrush("BrushRadialEmptySector", EmptySlotSectorBrush)
                : (IsHovered || IsSelected)
                    ? GetThemeBrush("BrushRadialChildAccentSector", ChildPageAccentBrush)
                    : GetThemeBrush("BrushRadialEmptySector", EmptySlotSectorBrush);
        }
    }

    private System.Windows.Media.Brush CreateOuterGradientSectorBrush()
    {
        var rad = AngleDegrees * Math.PI / 180.0;
        var dx = Math.Cos(rad);
        var dy = Math.Sin(rad);

        // 使用 RelativeToBoundingBox 模式：起点在内侧弧（靠近轮盘中心），终点在外侧弧（远离中心）
        var startPoint = new System.Windows.Point(0.5 - 0.5 * dx, 0.5 - 0.5 * dy);
        var endPoint = new System.Windows.Point(0.5 + 0.5 * dx, 0.5 + 0.5 * dy);

        System.Windows.Media.Color baseColor;
        if (IsSelected)
        {
            baseColor = System.Windows.Media.Color.FromRgb(226, 232, 240); // #E2E8F0 高亮浅灰
        }
        else if (IsHovered)
        {
            baseColor = System.Windows.Media.Color.FromRgb(203, 213, 225); // #CBD5E1 悬浮浅灰
        }
        else
        {
            baseColor = System.Windows.Media.Color.FromRgb(148, 163, 184); // #94A3B8 默认淡灰
        }

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = startPoint,
            EndPoint = endPoint,
            GradientStops =
            [
                new GradientStop(System.Windows.Media.Color.FromArgb((byte)(IsSelected ? 180 : (IsHovered ? 120 : 50)), baseColor.R, baseColor.G, baseColor.B), 0.0),
                new GradientStop(System.Windows.Media.Color.FromArgb((byte)(IsSelected ? 100 : (IsHovered ? 60 : 25)), baseColor.R, baseColor.G, baseColor.B), 0.40),
                new GradientStop(System.Windows.Media.Color.FromArgb((byte)(IsSelected ? 35 : (IsHovered ? 15 : 8)), baseColor.R, baseColor.G, baseColor.B), 0.75),
                new GradientStop(System.Windows.Media.Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B), 1.0)
            ]
        };
        brush.Freeze();
        return brush;
    }

    public double SectorOpacity => Ring == RadialMenuRing.Outer
        ? 1.0
        : (IsSelected ? 0.58 : IsHovered ? 0.44 : 0.0);

    public bool IsSectorVisible => SectorGeometry != null && (Ring == RadialMenuRing.Outer ? (IsSelected || IsHovered || !IsEmpty) : (!IsEmpty || IsHovered || IsSelected));

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
            OnPropertyChanged(nameof(SectorBrush));
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

public sealed record RadialSeparatorViewModel(double X1, double Y1, double X2, double Y2, System.Windows.Media.Brush? StrokeBrush = null);

public sealed record RadialMenuRuntimeItem(CommandItem? Command, string ChildPageId);

internal sealed record RadialEditTarget(string PageId, int Index, RadialMenuItemViewModel Item);

public enum RadialMenuRing
{
    Inner,
    Middle,
    Outer,
    Child,
    GrandChild,
    GreatGrandChild
}

internal sealed record RadialSlotPayload(string? ExtensionId, string? DisplayTitle, string? ChildPageId);

internal static partial class RadialMenuNativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    public const uint MB_YESNO = 0x00000004;
    public const uint MB_ICONWARNING = 0x00000030;
    public const uint MB_ICONQUESTION = 0x00000020;
    public const uint MB_DEFBUTTON2 = 0x00000100;
    public const uint MB_SETFOREGROUND = 0x00010000;
    public const uint MB_TOPMOST = 0x00040000;
    public const int IDYES = 6;

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
