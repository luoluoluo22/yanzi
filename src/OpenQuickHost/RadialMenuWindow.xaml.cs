using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class RadialMenuWindow : Window, INotifyPropertyChanged
{
    public const double NormalWindowSize = 1400.0;
    public const double NormalMenuCenter = 700.0;
    public const double CompactWindowSize = 1000.0;
    public const double CompactMenuCenter = 500.0;

    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _selectionTimer;
    private readonly Action _globalEscapeHandler;
    private bool _isClosing;
    private System.Drawing.Point _centerPixels;
    private System.Windows.Size _lastShownWorkAreaSize = new(1920, 1080);
    private RadialMenuItemViewModel? _selectedItem;
    private long _shownTimestamp;

    private List<RadialMenuPageSettings> _pages = [];
    private List<RadialMenuPageSettings> _topLevelPages = [];
    private string _currentPageId = string.Empty;
    private string? _activeProcessName;
    private static readonly ConcurrentDictionary<string, ImageSource?> _processIconCache = new(StringComparer.OrdinalIgnoreCase);
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
        SubRings.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSubRings));
            // 子环出现/消失时按实际内容范围调整窗口大小（Render 拍延后，避免同帧闪烁）
            ScheduleWindowToFitContent();
        };
        DataContext = this;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_editModeLocked)
                {
                    _editModeLocked = false;
                    ApplyVisualContentRootMode();
                    _selectionTimer.Stop();
                    Width = NormalWindowSize;
                    Height = NormalWindowSize;
                    SubRings.Clear();
                    BuildItems((AppSettingsStore.LoadCached().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
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
        MouseMove += (_, e) => QueueSelectionUpdate(e.GetPosition(this));
        MouseWheel += RadialMenuWindow_MouseWheel;
        MouseLeftButtonDown += RadialMenuWindow_MouseLeftButtonDown;
        MouseRightButtonDown += RadialMenuWindow_MouseRightButtonDown;
        Deactivated += (_, _) =>
        {
            HostAssets.AppendLog($"[PickerLog] RadialMenu Deactivated: isVisible={IsVisible}, isEditLocked={_editModeLocked}, wasActivatedForEdit={_wasActivatedForEdit}, isOpeningSubDialog={_isOpeningSubDialog}, isEditActive={_editInteractionActive}, isPickerMode={_mainWindow.IsRadialPickerMode}, popupOpen={_mainWindow.SearchScopePopup?.IsOpen}.");
            if (_mainWindow.IsRadialPickerMode || _mainWindow.SearchScopePopup?.IsOpen == true)
            {
                return;
            }
            if (_editModeLocked || _isOpeningSubDialog || _editInteractionActive)
            {
                return;
            }
            if (IsVisible)
            {
                _selectionTimer.Stop();
                Hide();
            }
        };
        SourceInitialized += (_, _) =>
        {
            EnsureNoActivateStyle();
            // 运行期改变显示缩放：WM_DPICHANGED 的建议矩形会移动窗口使轮盘中心漂离锚点，
            // 延迟一帧按锚点物理校正（窗口生命周期与应用相同，无需退订）
            var source = (System.Windows.Interop.HwndSource?)PresentationSource.FromVisual(this);
            source?.AddHook(RadialWindowWndProc);
        };
        Loaded += (_, _) =>
        {
            EnsureNoActivateStyle();
            RebuildItemsForCurrentLayout("loaded");
        };
        _globalEscapeHandler = () =>
        {
            if (IsVisible)
            {
                if (_editModeLocked)
                {
                    _editModeLocked = false;
                    ApplyVisualContentRootMode();
                    _selectionTimer.Stop();
                    Width = NormalWindowSize;
                    Height = NormalWindowSize;
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
        InputHookService.OnGlobalEscapePressed += _globalEscapeHandler;
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RebuildItemsForCurrentLayout("size-changed");
            }
            OnPropertyChanged(nameof(EditContentViewportHeight));
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

    private double _editScrollOffsetY = 0;
    public double EditScrollOffsetY
    {
        get => _editScrollOffsetY;
        set
        {
            if (Math.Abs(_editScrollOffsetY - value) > 0.1)
            {
                _editScrollOffsetY = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EditContentTranslateY));
            }
        }
    }

    public double EditContentTranslateY => -_editScrollOffsetY;

    private double _editMaxScrollOffset = 0;
    public double EditMaxScrollOffset
    {
        get => _editMaxScrollOffset;
        set
        {
            _editMaxScrollOffset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsScrollBarVisible));
        }
    }

    public bool IsScrollBarVisible => _editModeLocked && _editMaxScrollOffset > 10;

    private bool _isEditLoading = false;
    public bool IsEditLoading
    {
        get => _isEditLoading;
        set
        {
            _isEditLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditIconVisible));
        }
    }

    public bool IsEditIconVisible => !_isEditLoading;

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

        RadialMenuNestedRingViewModel? activeRing = null;
        foreach (var ring in SubRings)
        {
            var isActive = string.Equals(ring.PageId, pageId, StringComparison.OrdinalIgnoreCase);
            ring.IsActive = isActive;
            if (isActive)
            {
                activeRing = ring;
            }
        }

        if (activeRing != null && SubRings.Count > 1)
        {
            var oldIdx = SubRings.IndexOf(activeRing);
            if (oldIdx >= 0 && oldIdx != SubRings.Count - 1)
            {
                SubRings.Move(oldIdx, SubRings.Count - 1);
            }
        }

        var activePage = _pages.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))
            ?? AppSettingsStore.LoadCached().RadialMenu?.Pages?.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
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

    internal void LoadRadialMenuPages()
    {
        // 复用停靠实例后，外部（扩展/页面/设置变更）调本方法刷新数据。
        // 重置当前页面 ID，强制下次 ShowAtMouse 重新计算 targetPageId 并走重建分支。
        // 注意：绝不能在此清空 _activeProcessName，因为 ShowAtMouse 和 PrewarmDeep 是先解析出当前进程名，
        // 再调用 LoadRadialMenuPages 依此过滤应用专属页面！清空会导致应用专属轮盘彻底失效。
        _currentPageId = string.Empty;
        _pages.Clear();
        SubRings.Clear();
        IsEditHoverActive = false;
        IsPinHoverActive = false;
        IsAddHoverActive = false;
        IsDeleteHoverActive = false;
        IsSearchHoverActive = false;
        IsCloseHoverActive = false;

        var settings = AppSettingsStore.LoadCached();
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

    private bool _isPrewarmed = false;

    /// <summary>
    /// 深度就绪态预热：在后台空闲时提前创建 HWND 句柄、加载默认通用页面、构建扇形布局并在屏幕外完成静默首帧上屏。
    /// 彻底消除用户呼出轮盘时的 D3D 交换链创建、XAML 编译与排版阻塞。
    /// </summary>
    public void PrewarmDeep()
    {
        try
        {
            HostAssets.AppendLog("RadialMenuWindow: PrewarmDeep starting...");
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.EnsureHandle();
            EnsureNoActivateStyle();
            ApplyVisualContentRootMode();
            // 预热也只开紧凑尺寸，进一步压低首次呼出的合成表面成本
            if (Math.Abs(Width - CompactWindowSize) > 0.5) Width = CompactWindowSize;
            if (Math.Abs(Height - CompactWindowSize) > 0.5) Height = CompactWindowSize;

            // 预先解析当前前台应用并构建对应页面（与 ShowAtMouse 同套解析逻辑），
            // 让首次真实呼出大概率命中"已构建页面"，避免呼出瞬间全量重建
            var prewarmProcessName = ResolveForegroundProcessNameForPrewarm();
            if (!string.IsNullOrWhiteSpace(prewarmProcessName))
            {
                _activeProcessName = prewarmProcessName;
            }

            LoadRadialMenuPages();
            var settings = AppSettingsStore.LoadCached().RadialMenu ?? new RadialMenuSettings();
            _lastRadiusPixels = settings.RadiusPixels;
            _cachedDeadZonePixels = Math.Max(36, settings.DeadZonePixels);

            var prewarmFirstAppPage = _pages.FirstOrDefault(page => !string.IsNullOrEmpty(page.ContextProcessName));
            string prewarmTargetPageId;
            if (prewarmFirstAppPage != null)
            {
                prewarmTargetPageId = prewarmFirstAppPage.Id;
            }
            else if (!string.IsNullOrWhiteSpace(settings.SelectedPageId) &&
                     _pages.Any(p => p.Id.Equals(settings.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                prewarmTargetPageId = settings.SelectedPageId;
            }
            else
            {
                prewarmTargetPageId = _pages.FirstOrDefault()?.Id ?? string.Empty;
            }

            _currentPageId = prewarmTargetPageId;
            BuildItems(_lastRadiusPixels);
            UpdateCenterText();
            ActiveTitle = "取消";

            // 将窗口放置于屏幕外深处，并置为完全透明隐藏
            Opacity = 0;
            RootGrid.Visibility = Visibility.Hidden;
            Left = OverlayWindowManager.OffScreenCoordinate;
            Top = OverlayWindowManager.OffScreenCoordinate;

            // 屏幕外静默呈现，驱动 WPF 完成 Direct3D 交换链分配、Visual 树首次渲染与 Shader 编译
            Show();
            _isPrewarmed = true;

            HostAssets.AppendLog("RadialMenuWindow: PrewarmDeep completed successfully.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"RadialMenuWindow.PrewarmDeep EXCEPTION: {ex}");
        }
    }

    /// <summary>
    /// 预热阶段尽力解析当前前台应用（仅用于决定预热构建哪个页面，失败返回 null 不影响流程）。
    /// </summary>
    private static string? ResolveForegroundProcessNameForPrewarm()
    {
        try
        {
            var (_, processName) = WindowSensorHelper.ResolveActiveTargetWindowAndProcess();
            return string.IsNullOrWhiteSpace(processName) ? null : processName;
        }
        catch
        {
            return null;
        }
    }

    public void Warmup()
    {
        PrewarmDeep();
    }

    public void ShowAtMouse(System.Drawing.Point? anchorPoint = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 复用停靠实例时复位关闭标志（Hide 停靠不销毁，Closed 事件不会触发）
        _isClosing = false;
        HostAssets.AppendDebug($"[RadialResidualDebug] ShowAtMouse start: isVisible={IsVisible}, _isPrewarmed={_isPrewarmed}, SubRings.Count={SubRings.Count}, anchor={anchorPoint}.");

        var screenCtx = ScreenHelper.GetScreenContextAtPoint(
            anchorPoint.HasValue
                ? new System.Windows.Point(anchorPoint.Value.X, anchorPoint.Value.Y)
                : ScreenHelper.GetCursorPhysicalPosition());
        var showWorkArea = screenCtx.DipWorkArea;
        _lastShownWorkAreaSize = new System.Windows.Size(showWorkArea.Width, showWorkArea.Height);
        // 呼出初始用小窗（只覆盖主轮盘），子环展开时再按需放大（见 UpdateWindowToFitContent）
        var targetWidth = Math.Min(CompactWindowSize, Math.Max(600, showWorkArea.Width));
        var targetHeight = Math.Min(CompactWindowSize, Math.Max(600, showWorkArea.Height));
        if (Math.Abs(Width - targetWidth) > 0.5) Width = targetWidth;
        if (Math.Abs(Height - targetHeight) > 0.5) Height = targetHeight;
        // 彻底移除 UpdateLayout()，杜绝主线程全量同步排版阻塞

        _isExecuting = false;
        _wasActivatedForEdit = false;
        _editModeLocked = false;
        _editInteractionActive = false;
        ApplyVisualContentRootMode();
        UpdateEditModeState();
        IsChildRingLocked = false;
        IsGrandChildRingLocked = false;
        IsGreatGrandChildRingLocked = false;
        IsChildCenterHovered = false;
        IsGrandChildCenterHovered = false;
        IsGreatGrandChildCenterHovered = false;
        IsGuideLineVisible = false;
        _selectionTimer.Stop();

        var settings = AppSettingsStore.LoadCached().RadialMenu ?? new RadialMenuSettings();
        _shownTimestamp = Environment.TickCount64;
        _lastRadiusPixels = settings.RadiusPixels;
        _cachedDeadZonePixels = Math.Max(36, settings.DeadZonePixels);

        var helperHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        // 对齐背包（QuickPanel）100% 可靠前台应用感知体系：
        // 优先探测光标悬浮顶层窗口，若光标下为 Electron/Chromium 渲染子控件、临时句柄或无效句柄，
        // 坚决无条件回退到系统真实前台活动窗口（GetForegroundWindow），彻底排除燕子进程自身所有浮窗干扰。
        var (targetHwnd, currentProcessName) = WindowSensorHelper.ResolveActiveTargetWindowAndProcess(
            excludeHwnd: helperHwnd,
            cursorPoint: anchorPoint);

        _previousForegroundWindow = targetHwnd;
        _activeProcessName = string.IsNullOrWhiteSpace(currentProcessName) ? null : currentProcessName;
        LoadRadialMenuPages();

        HostAssets.AppendLog($"[RadialProcessDebug] targetWnd=0x{_previousForegroundWindow.ToInt64():X}, process={_activeProcessName ?? "(null)"}, pages={_pages.Count}, topLevel={_topLevelPages.Count}, currentPage={_currentPageId}, selectedPage={settings.SelectedPageId ?? "(null)"}.");

        // 精确匹配当前活动进程的专属页面（LoadRadialMenuPages 已按进程过滤，firstAppPage 一定是当前进程的）
        var firstAppPage = _pages.FirstOrDefault(page => !string.IsNullOrEmpty(page.ContextProcessName));
        string targetPageId;
        if (firstAppPage != null)
        {
            targetPageId = firstAppPage.Id;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(settings.SelectedPageId) &&
                _pages.Any(p => p.Id.Equals(settings.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                targetPageId = settings.SelectedPageId;
            }
            else
            {
                targetPageId = _pages.FirstOrDefault()?.Id ?? string.Empty;
            }
        }

        HostAssets.AppendLog($"[RadialProcessDebug] targetPage={targetPageId}, firstAppPage={(firstAppPage?.Id ?? "(null)")}, needRebuildCandidate={(_currentPageId != targetPageId || Items.Count == 0)}.");

        // 页面切换或内容为空时才重建视觉树（进程检测/页面过滤是廉价的，BuildItems 才是昂贵的）
        bool needRebuild = false;
        if (_currentPageId != targetPageId || Items.Count == 0)
        {
            _currentPageId = targetPageId;
            needRebuild = true;
        }

        if (needRebuild)
        {
            CenterIcon = null;
            Items.Clear();
            OuterItems.Clear();
            ChildItems.Clear();
            GrandChildItems.Clear();
            GreatGrandChildItems.Clear();
            SubRings.Clear();
            PageTitle = "燕环";

            BuildItems(_lastRadiusPixels);
        }

        _pageStack.Clear();
        _centerPixels = anchorPoint ?? Forms.Cursor.Position;
        UpdateCenterText();
        ActiveTitle = "取消";

        // 通过 Win32 API 快速定位到光标中心（传入当前显示器的准确 DPI）
        PositionAroundCursor(screenCtx.DpiScale.DpiScaleX);

        if (!IsVisible)
        {
            Show();
        }

        // 内容仍不可见时先做物理像素中心校正：此时窗口已移到目标显示器、
        // 物理尺寸已定型，实测修正不会产生"先出帧再跳动"的可见顿挫。
        // 旧实现放在 Render 优先级异步执行，首帧会以偏移位置亮相，随后被拉回。
        CenterOnAnchorPhysically("pre-reveal");

        RootGrid.Visibility = Visibility.Visible;
        Opacity = 1.0;
        PlayEntryAnimation();

        _selectionTimer.Start();
        UpdateSelectionFromCursor();

        sw.Stop();
        HostAssets.AppendLog($"[RadialMenuTiming] ShowAtMouse finished in {sw.ElapsedMilliseconds}ms: page={_currentPageId}, process={_activeProcessName ?? "(none)"}, items={Items.Count}, needRebuild={needRebuild}.");
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

        if (reason == "loaded" && Items.Count > 0 && !_editModeLocked)
        {
            return;
        }

        BuildItems(_lastRadiusPixels);
        HostAssets.AppendLog($"Radial menu layout rebuilt: reason={reason}, size=({ActualWidth:0.##},{ActualHeight:0.##}), page={_currentPageId}.");
    }

    private const int WmDpiChanged = 0x02E1;
    private double _lastWindowDpi = 1.0;
    private (double X, double Y) _pendingDpiCenter;

    private IntPtr RadialWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged && IsVisible)
        {
            // 运行期改变显示缩放：窗口按建议矩形重排后，锚点的物理坐标在新缩放世界里
            // 对应另一个视觉位置，轮盘会"跳"离用户所见位置。按显示器原点+新旧 DPI 比例
            // 重映射锚点，渲染帧末把窗口放回"视觉位置不变"的新坐标，并重新钳制高度。
            // （官方指引：尺寸以 WM_DPICHANGED lParam 的建议矩形为准，这里直接让 WPF
            //   完成其默认处理，再由物理校正把中心对齐到重映射后的锚点。）
            try
            {
                var newDpi = (wParam.ToInt32() & 0xFFFF) / 96.0;
                var oldDpi = _lastWindowDpi > 0 ? _lastWindowDpi : newDpi;
                _lastWindowDpi = newDpi;
                if (newDpi > 0 &&
                    Win32Native.MonitorFromWindow(hwnd, Win32Native.MonitorDefaultToNearest) is var hMon &&
                    hMon != IntPtr.Zero)
                {
                    var mi = new Win32Native.MONITORINFO();
                    mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.MONITORINFO>();
                    if (Win32Native.GetMonitorInfo(hMon, ref mi))
                    {
                        var ratio = newDpi / oldDpi;
                        // 锚点重映射：相对显示器原点的物理偏移按 DPI 比例缩放（视觉位置不变）
                        _centerPixels = new System.Drawing.Point(
                            (int)Math.Round(mi.rcMonitor.Left + (_centerPixels.X - mi.rcMonitor.Left) * ratio),
                            (int)Math.Round(mi.rcMonitor.Top + (_centerPixels.Y - mi.rcMonitor.Top) * ratio));
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!IsVisible)
                            {
                                return;
                            }

                            var workAreaHeightDip = (mi.rcWork.Bottom - mi.rcWork.Top) / newDpi;
                            var workAreaWidthDip = (mi.rcWork.Right - mi.rcWork.Left) / newDpi;
                            _lastShownWorkAreaSize = new System.Windows.Size(workAreaWidthDip, workAreaHeightDip);
                            if (_editModeLocked)
                            {
                                Height = Math.Min(NormalWindowSize, Math.Max(600, workAreaHeightDip));
                            }
                            else
                            {
                                Height = Math.Min(CompactWindowSize, Math.Max(600, workAreaHeightDip));
                            }
                            CenterOnPhysically(_centerPixels.X, _centerPixels.Y, "dpi-changed");
                            if (!_editModeLocked)
                            {
                                UpdateWindowToFitContent();
                            }
                        }), DispatcherPriority.Render);
                    }
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[RadialMenuLog] DPI change recenter failed: {ex.Message}");
            }
        }

        return IntPtr.Zero;
    }

    private void PositionAroundCursor(double? dpiHint = null)
    {
        // 关键：不用 WPF 的 Left/Top 属性放置！
        // dotnet/wpf#3105（Open/Future）：PerMonitorV2 下 Left/Top 会被 WPF 按
        // "窗口当前所在显示器 DPI 与目标显示器 DPI 的比值"再缩放一次，跨屏呼出必然错位。
        // 改用 Win32 SetWindowPos 直接以物理像素定位——坐标含义无歧义，绕开该缺陷。
        // 只定位不改变大小（NOSIZE）：尺寸交给 WPF 的 DPI 机制按 Width/Height DP 管理，
        // 初始的中心误差由 CenterOnPhysically 在显示后按物理像素校正兜底。
        var windowDpi = (dpiHint.HasValue && dpiHint.Value > 0) ? dpiHint.Value : GetWindowDpiScale();
        var widthPhys = Width * windowDpi;
        var heightPhys = Height * windowDpi;
        var targetLeft = (int)Math.Round(_centerPixels.X - widthPhys / 2);
        var targetTop = (int)Math.Round(_centerPixels.Y - heightPhys / 2);
        HostAssets.AppendLog($"[RadialMenuLog] Placement intent (physical): anchor=({_centerPixels.X},{_centerPixels.Y}), left={targetLeft}, top={targetTop}, size=({widthPhys:F0}x{heightPhys:F0}), windowDpi={windowDpi:F2}.");
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle != IntPtr.Zero)
        {
            Win32Native.SetWindowPos(
                handle,
                Win32Native.HWND_TOPMOST,
                targetLeft,
                targetTop,
                0,
                0,
                Win32Native.SWP_NOSIZE | Win32Native.SWP_NOACTIVATE | Win32Native.SWP_SHOWWINDOW);
        }
    }

    private double GetWindowDpiScale()
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var handle = helper.Handle;
            if (handle != IntPtr.Zero)
            {
                var dpi = ScreenHelper.GetDpiForWindow(handle);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
        }
        catch
        {
            // 兜底走 VisualTreeHelper
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.DpiScaleX > 0)
            {
                return dpi.DpiScaleX;
            }
        }
        catch
        {
            // 最终兜底 1.0
        }

        return 1.0;
    }

    /// <summary>
    /// 物理像素校正：实测窗口当前物理中心与目标点的偏差，按窗口当前 DPI 折回 DIP 修正 Left/Top。
    /// 不依赖任何 DPI 假设——无论 WPF 用哪个上下文解释 Left/Top，测多少补多少。
    /// </summary>
    private void CenterOnPhysically(double targetX, double targetY, string pass)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var handle = helper.Handle;
            if (handle == IntPtr.Zero || !Win32Native.GetWindowRect(handle, out var rect))
            {
                return;
            }

            var currentCenterX = (rect.Left + rect.Right) / 2.0;
            var currentCenterY = (rect.Top + rect.Bottom) / 2.0;
            var deltaX = targetX - currentCenterX;
            var deltaY = targetY - currentCenterY;
            var windowDpi = GetWindowDpiScale();
            _lastWindowDpi = windowDpi;
            HostAssets.AppendLog($"[RadialMenuLog] Center check ({pass}): target=({targetX:F0},{targetY:F0}), rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom}), center=({currentCenterX:F0},{currentCenterY:F0}), delta=({deltaX:F0},{deltaY:F0}), dpi={windowDpi:F2}, state={WindowState}.");
            if (Math.Abs(deltaX) < 1 && Math.Abs(deltaY) < 1)
            {
                return;
            }

            // 纯物理像素修正（SetWindowPos 直改位置），全程零 DPI 换算——
            // 不经过 WPF Left/Top 属性路径，天然免疫 dotnet/wpf#3105 的跨屏缩放
            var targetLeft = (int)Math.Round(rect.Left + deltaX);
            var targetTop = (int)Math.Round(rect.Top + deltaY);
            Win32Native.SetWindowPos(
                handle,
                IntPtr.Zero,
                targetLeft,
                targetTop,
                0,
                0,
                Win32Native.SWP_NOSIZE | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);
            HostAssets.AppendLog($"[RadialMenuLog] Center corrected ({pass}): moved to ({targetLeft},{targetTop}) physical.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[RadialMenuLog] CenterOnPhysically failed: {ex.Message}");
        }
    }

    private void CenterOnAnchorPhysically(string pass)
    {
        CenterOnPhysically(_centerPixels.X, _centerPixels.Y, pass);
    }

    /// <summary>编辑模式滚动条的视口高度 = 窗口实际内容高度（滚动条在 VisualContentRoot 内，不能绑窗口 ActualHeight）。</summary>
    public double EditContentViewportHeight => _editModeLocked
        ? (ActualHeight > 1 ? ActualHeight : Height) - 100.0
        : 1300.0;

    /// <summary>切换 VisualContentRoot 布局模式：编辑模式铺满窗口（逻辑=窗口坐标），常态固定 1400 画布自动居中。</summary>
    private void ApplyVisualContentRootMode()
    {
        if (VisualContentRoot == null)
        {
            return;
        }

        if (_editModeLocked)
        {
            VisualContentRoot.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            VisualContentRoot.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            VisualContentRoot.ClearValue(WidthProperty);
            VisualContentRoot.ClearValue(HeightProperty);
        }
        else
        {
            VisualContentRoot.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            VisualContentRoot.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            VisualContentRoot.Width = NormalWindowSize;
            VisualContentRoot.Height = NormalWindowSize;
        }
        OnPropertyChanged(nameof(EditContentViewportHeight));
    }

    private void PlayEntryAnimation()
    {
        try
        {
            if (_editModeLocked || RootGrid == null || WheelEntryScale == null)
            {
                return;
            }

            _isEntryAnimationActive = true;
            _fitContentPending = false;

            // 关键：先用普通属性把动画起点设到位，再 BeginAnimation。
            // 如果先显式 Opacity=1 再动画拉回 0，首帧会以完整画面亮相一瞬再变透明，
            // 视觉上就是"出现→消失→再淡入"。同理 scale 起点在显示前就位。
            WheelEntryScale.ScaleX = 0.94;
            WheelEntryScale.ScaleY = 0.94;

            // 缩放动画：EaseOut 无过冲（BackEase 的过冲会在呼出瞬间放大抖动）；
            // 不再对 RootGrid.Opacity 做动画，避免与 AllowsTransparency 合成/隐藏路径打架。
            var scaleEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var anim = new DoubleAnimation(0.94, 1.0, new Duration(TimeSpan.FromMilliseconds(110)))
            {
                EasingFunction = scaleEase
            };
            anim.Completed += (_, _) =>
            {
                WheelEntryScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                WheelEntryScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                WheelEntryScale.ScaleX = 1.0;
                WheelEntryScale.ScaleY = 1.0;
                _isEntryAnimationActive = false;
                // 动画期间积累的尺寸需求（子环展开）在此一次性执行，避免与缩放动画叠加闪烁
                if (_fitContentPending)
                {
                    _fitContentPending = false;
                    UpdateWindowToFitContent();
                }
            };

            WheelEntryScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            WheelEntryScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[RadialMenuLog] entry animation failed: {ex.Message}");
            _isEntryAnimationActive = false;
            if (WheelEntryScale != null)
            {
                WheelEntryScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                WheelEntryScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                WheelEntryScale.ScaleX = 1.0;
                WheelEntryScale.ScaleY = 1.0;
            }
        }
    }

    private bool _isEntryAnimationActive = false;
    private bool _fitContentPending = false;
    private bool _fitContentScheduled = false;

    /// <summary>
    /// 子环集合变化触发的窗口按需放大入口。
    /// 延后到 Render 优先级：让子环内容先随本帧上屏，窗口 resize 在下一拍完成，
    /// 避免"新子环 + 窗口改尺寸"挤在同一帧里互相拉扯造成错位闪烁。
    /// </summary>
    private void ScheduleWindowToFitContent()
    {
        if (_editModeLocked || _fitContentScheduled)
        {
            return;
        }

        _fitContentScheduled = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _fitContentScheduled = false;
            UpdateWindowToFitContent();
        }), DispatcherPriority.Render);
    }

    private System.Windows.Size GetMenuSize()
    {
        // 编辑模式：窗口铺满工作区，逻辑空间 == 窗口空间，直接返回窗口实际尺寸
        if (_editModeLocked)
        {
            var width = ActualWidth > 1 ? ActualWidth : Width;
            var height = ActualHeight > 1 ? ActualHeight : Height;
            return new System.Windows.Size(width, height);
        }

        // 常态呼出：返回"可见的逻辑视口尺寸"——1410 画布正对窗口中心时，
        // 可见范围 = 窗口尺寸映射回 1400 坐标系（取 min(1400, 窗口尺寸)）
        var viewW = Math.Min(NormalWindowSize, ActualWidth > 1 ? ActualWidth : Width);
        var viewH = Math.Min(NormalWindowSize, ActualHeight > 1 ? ActualHeight : Height);
        return new System.Windows.Size(viewW, viewH);
    }

    /// <summary>
    /// 断言窗口尺寸足以容纳当前内容（主轮盘 + 已展开子环），不够则按需放大。
    /// 只放大不缩小（收起子环不回收空间，避免反复 resize 抖动）；放大时用单次
    /// SetWindowPos 同时改尺寸并保持几何中心不动，杜绝"先错位再校正"的中间帧闪烁。
    /// 呼出入场动画进行中则延迟到动画完成再执行。
    /// </summary>
    private void UpdateWindowToFitContent()
    {
        if (_editModeLocked || !IsContentVisible)
        {
            return;
        }

        if (_isEntryAnimationActive)
        {
            _fitContentPending = true;
            return;
        }

        var required = ComputeRequiredLogicalSize();
        var desiredWidth = Math.Min(NormalWindowSize, Math.Max(CompactWindowSize, Math.Min(required.Width, _lastShownWorkAreaSize.Width)));
        var desiredHeight = Math.Min(NormalWindowSize, Math.Max(CompactWindowSize, Math.Min(required.Height, _lastShownWorkAreaSize.Height)));

        // 只放大不缩小：当前窗口已足且只是子环收起时，保持现状
        desiredWidth = Math.Max(desiredWidth, Width);
        desiredHeight = Math.Max(desiredHeight, Height);
        if (Math.Abs(Width - desiredWidth) <= 0.5 && Math.Abs(Height - desiredHeight) <= 0.5)
        {
            return;
        }

        // 单次物理 SetWindowPos：改尺寸 + 按当前物理中心重算左上角，一步到位保持中心
        ResizeWindowKeepingCenterPhysical(desiredWidth, desiredHeight);
    }

    /// <summary>
    /// 单次 SetWindowPos 完成"改尺寸 + 保持窗口几何中心不动"。
    /// 纯物理像素、一次调用，WPF 的 Width/Height 同步为 DIP 值让布局跟随。
    /// </summary>
    private void ResizeWindowKeepingCenterPhysical(double newWidthDip, double newHeightDip)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var handle = helper.Handle;
            if (handle == IntPtr.Zero)
            {
                Width = newWidthDip;
                Height = newHeightDip;
                return;
            }

            var windowDpi = GetWindowDpiScale();
            var newWidthPhys = (int)Math.Round(newWidthDip * windowDpi);
            var newHeightPhys = (int)Math.Round(newHeightDip * windowDpi);
            if (!Win32Native.GetWindowRect(handle, out var rect))
            {
                Width = newWidthDip;
                Height = newHeightDip;
                return;
            }

            var centerX = (rect.Left + rect.Right) / 2.0;
            var centerY = (rect.Top + rect.Bottom) / 2.0;
            var targetLeft = (int)Math.Round(centerX - newWidthPhys / 2.0);
            var targetTop = (int)Math.Round(centerY - newHeightPhys / 2.0);

            Win32Native.SetWindowPos(
                handle,
                IntPtr.Zero,
                targetLeft,
                targetTop,
                newWidthPhys,
                newHeightPhys,
                Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);

            Width = newWidthDip;
            Height = newHeightDip;
            HostAssets.AppendDebug($"[RadialMenuLog] ResizeWindowKeepingCenterPhysical -> {newWidthDip:F0}x{newHeightDip:F0} @ ({targetLeft},{targetTop})");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[RadialMenuLog] ResizeWindowKeepingCenterPhysical failed: {ex.Message}");
            Width = newWidthDip;
            Height = newHeightDip;
        }
    }

    /// <summary>
    /// 计算当前内容所需的逻辑尺寸：主轮盘外圈固定 280，加上所有已展开子环的最远视觉外缘。
    /// 子环自身的可视半径：独立大轮盘约 190，普通子环约 118。
    /// </summary>
    private System.Windows.Size ComputeRequiredLogicalSize()
    {
        if (_editModeLocked)
        {
            var w = ActualWidth > 1 ? ActualWidth : Width;
            var h = ActualHeight > 1 ? ActualHeight : Height;
            return new System.Windows.Size(w, h);
        }

        double maxRadius = 280;
        foreach (var ring in SubRings)
        {
            double ringRadius = ring.IsStandaloneRadial ? 190 : 118;
            maxRadius = Math.Max(maxRadius, Math.Max(Math.Abs(ring.CenterX - NormalMenuCenter), Math.Abs(ring.CenterY - NormalMenuCenter)) + ringRadius);
        }

        var size = Math.Clamp(Math.Ceiling(maxRadius * 2 + 120), CompactWindowSize, NormalWindowSize);
        return new System.Windows.Size(size, size);
    }

    /// <summary>
    /// 逻辑空间（1400 画布坐标/视觉内容坐标）的轮盘中心。
    /// 常态呼出固定为画布中心 (700,700)；编辑模式为窗口实际中心。
    /// 所有命中测试、引导线、子环布局都必须用这个中心，保证缩窗后与视觉严格对齐。
    /// </summary>
    private System.Windows.Point GetWindowCenter()
    {
        if (_editModeLocked)
        {
            var size = GetMenuSize();
            return new System.Windows.Point(size.Width / 2, size.Height / 2);
        }
        return new System.Windows.Point(NormalMenuCenter, NormalMenuCenter);
    }

    /// <summary>
    /// 容器坐标系（1400x1400 画布）的轮盘中心，仅供 BuildItems 等容器内布局使用。
    /// </summary>
    private System.Windows.Point GetMenuCenter()
    {
        return new System.Windows.Point(NormalMenuCenter, NormalMenuCenter);
    }

    /// <summary>窗口 DIP 坐标 → 逻辑坐标（编辑模式为恒等映射）。</summary>
    private System.Windows.Point WindowDipToLogical(System.Windows.Point windowDip)
    {
        if (_editModeLocked)
        {
            return windowDip;
        }
        return new System.Windows.Point(
            windowDip.X + (1400.0 - (ActualWidth > 1 ? ActualWidth : Width)) / 2.0,
            windowDip.Y + (1400.0 - (ActualHeight > 1 ? ActualHeight : Height)) / 2.0);
    }

    public void ExecuteSelectedFromHoldRelease()
    {
        // 用内容可见性而非 IsVisible 判定：停靠屏外的实例 IsVisible 恒为 true（不销毁），
        // 钩子的释放事件若误入会触发执行/重置逻辑
        if (!IsContentVisible || _isExecuting)
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

        var cursorPoint = GetCursorWindowPoint();
        if (IsPointInToolBar(cursorPoint))
        {
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

        cursorPoint = GetCursorWindowPoint();
        var center = GetWindowCenter();
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
                var restored = Win32Native.SetForegroundWindow(_previousForegroundWindow);
                HostAssets.AppendLog($"Radial menu restore foreground: restored={restored}, {DescribeWindow(_previousForegroundWindow)}.");
            }

            var currentForeground = Win32Native.GetForegroundWindow();
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
        _ = Win32Native.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = Win32Native.GetWindowThreadProcessId(hwnd, out var processId);
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
        HostAssets.AppendDebug($"[RadialResidualDebug] BuildItems start: _currentPageId={_currentPageId}, _editModeLocked={_editModeLocked}, SubRings.Count before={SubRings.Count}.");
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
        var appName = GetProcessDisplayName(_activeProcessName);
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
            var vm = new RadialMenuItemViewModel(
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
                center.Y)
            {
                IsEditMode = _editModeLocked
            };
            Items.Add(vm);
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
            var vm = new RadialMenuItemViewModel(
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
                center.Y)
            {
                IsEditMode = _editModeLocked
            };
            MiddleItems.Add(vm);
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
            var vm = new RadialMenuItemViewModel(
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
                center.Y)
            {
                IsEditMode = _editModeLocked
            };
            OuterItems.Add(vm);
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
        HostAssets.AppendDebug($"[RadialResidualDebug] BuildItems end: Items={Items.Count}, MiddleItems={MiddleItems.Count}, OuterItems={OuterItems.Count}, SubRings={SubRings.Count}.");
    }

    private void UpdateOuterWheelVisibility()
    {
        var old = _isOuterWheelVisible;
        // 编辑模式下是多轮盘平铺，不需要在屏幕中央显示静态单层外圈；普通模式下划向外圈时动态显现
        IsOuterWheelVisible = (!_editModeLocked) && _isOuterRingHoverActive;
        if (old != IsOuterWheelVisible)
        {
            HostAssets.AppendDebug($"[RadialOuterLog] UpdateOuterWheelVisibility changed: locked={_editModeLocked}, hoverActive={_isOuterRingHoverActive} => IsOuterWheelVisible={IsOuterWheelVisible}");
        }
    }

    // 单飞(single-flight)鼠标更新合并：高回报率鼠标(125~1000Hz)下 MouseMove 远密于显示刷新率，
    // 若每条事件都同步跑一遍完整命中检测会做大量无效功。这里只记录最新光标位置，
    // 用 Render 优先级的 BeginInvoke 把一帧内的多次移动合并为一次更新（对标 StarPie 的 QueueHighlightUpdate）。
    private bool _selectionUpdateScheduled;
    private System.Windows.Point _pendingSelectionPoint;
    private bool _hasPendingSelectionPoint;

    private void QueueSelectionUpdate(System.Windows.Point windowPoint)
    {
        _pendingSelectionPoint = windowPoint;
        _hasPendingSelectionPoint = true;
        if (_selectionUpdateScheduled)
        {
            return;
        }

        _selectionUpdateScheduled = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _selectionUpdateScheduled = false;
            if (!_hasPendingSelectionPoint)
            {
                return;
            }
            _hasPendingSelectionPoint = false;
            UpdateSelectionFromCursor(_pendingSelectionPoint);
        }), DispatcherPriority.Render);
    }

    private void UpdateSelectionFromCursor(System.Windows.Point? preCalculatedPoint = null)
    {
        // 停靠屏外的复用实例 IsVisible 恒为 true，必须用内容可见性短路，
        // 避免 16ms 定时器在停靠态空转打日志、刷属性
        if (!IsContentVisible)
        {
            return;
        }

        if (_editInteractionActive)
        {
            IsGuideLineVisible = false;
            return;
        }

        var cursorPoint = preCalculatedPoint ?? GetCursorWindowPoint();
        if (!_editModeLocked)
        {
            cursorPoint = WindowDipToLogical(cursorPoint);
        }
        if (UpdateAllToolBarHoverStates(cursorPoint))
        {
            IsCenterHovered = false;
            _isOuterRingHoverActive = false;
            UpdateOuterWheelVisibility();
            return;
        }

        IsEditHoverActive = false;
        IsPinHoverActive = false;
        IsAddHoverActive = false;
        IsDeleteHoverActive = false;
        IsSearchHoverActive = false;
        IsCloseHoverActive = false;

        var center = GetWindowCenter();

        // 如果处于编辑模式，计算相对于 SubRings 画布的实际坐标 (加上垂直滚动偏移)
        var contentPoint = _editModeLocked
            ? new System.Windows.Point(cursorPoint.X, cursorPoint.Y + _editScrollOffsetY)
            : cursorPoint;

        // 优先检查所有展开的独立子环或放射子环命中！
        for (int i = SubRings.Count - 1; i >= 0; i--)
        {
            var ring = SubRings[i];
            var maxRadius = ring.IsStandaloneRadial ? 280.0 : 100.0;

            // AABB 极速粗筛：若光标明显不在该轮盘包围盒内，瞬间跳过，0 几何算力开销！
            if (Math.Abs(contentPoint.X - ring.CenterX) > maxRadius || Math.Abs(contentPoint.Y - ring.CenterY) > maxRadius)
            {
                if (ring.SelectedItem != null)
                {
                    ring.SelectedItem.IsSelected = false;
                    ring.SelectedItem = null;
                }
                continue;
            }

            if (TryUpdateSubRingSelection(ring, contentPoint))
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
                            if (!_editModeLocked && !string.IsNullOrWhiteSpace(ring.SelectedItem.ChildPageId))
                            {
                                var (expectedX, expectedY) = ComputeSubRingCenter(ring.SelectedItem, ring.CenterX, ring.CenterY, ring.Level + 1);
                                // 目标子环已存在且位置一致时跳过清空重建（每 tick 都会走到这里）
                                if (!HasSubRingAtLevel(ring.Level + 1, ring.SelectedItem.ChildPageId, expectedX, expectedY))
                                {
                                    ClearSubRingsAboveLevel(ring.Level + 1);
                                    var slotCenterX = ring.SelectedItem.X + 25;
                                    var slotCenterY = ring.SelectedItem.Y + 20;
                                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _currentPageId };
                                    foreach (var r in SubRings.Take(i + 1)) visited.Add(r.PageId);
                                    RecursivelyBuildSubRings(ring.SelectedItem, ring.CenterX, ring.CenterY, slotCenterX, slotCenterY, ring.SelectedItem.AngleDegrees, ring.Level + 1, visited);
                                }
                            }
                            else if (!_editModeLocked)
                            {
                                ClearSubRingsAboveLevel(ring.Level + 1);
                            }
                        }
                    }
                    else
                    {
                        var innerIdx = ring.SelectedItem.Index;
                        if (innerIdx >= 0 && innerIdx < ring.Items.Count)
                        {
                            if (!_editModeLocked && !string.IsNullOrWhiteSpace(ring.SelectedItem.ChildPageId))
                            {
                                var (expectedX, expectedY) = ComputeSubRingCenter(ring.SelectedItem, ring.CenterX, ring.CenterY, ring.Level + 1);
                                if (!HasSubRingAtLevel(ring.Level + 1, ring.SelectedItem.ChildPageId, expectedX, expectedY))
                                {
                                    ClearSubRingsAboveLevel(ring.Level + 1);
                                    var slotCenterX = ring.SelectedItem.X + 32;
                                    var slotCenterY = ring.SelectedItem.Y + 25;
                                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _currentPageId };
                                    foreach (var r in SubRings.Take(i + 1)) visited.Add(r.PageId);
                                    RecursivelyBuildSubRings(ring.SelectedItem, ring.CenterX, ring.CenterY, slotCenterX, slotCenterY, ring.SelectedItem.AngleDegrees, ring.Level + 1, visited);
                                }
                            }
                            else if (!_editModeLocked)
                            {
                                ClearSubRingsAboveLevel(ring.Level + 1);
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

        if (_editModeLocked)
        {
            SetSelectedItem(null);
            IsGuideLineVisible = false;
            Cursor = System.Windows.Input.Cursors.Arrow;
            return;
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
            HostAssets.AppendDebug($"[RadialOuterLog] outerHit: dist={distance:0.#}, outerIdx={outerIndex}, outerItem={outerItem?.Title ?? "empty"}, isSel={outerItem?.IsSelected}, isSecVis={outerItem?.IsSectorVisible}, op={outerItem?.SectorOpacity}, brush={outerItem?.SectorBrush?.GetType().Name}");
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
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero && Win32Native.GetCursorPos(out var pt))
            {
                if (Win32Native.ScreenToClient(hwnd, ref pt))
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                    var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
                    return new System.Windows.Point(pt.X / scaleX, pt.Y / scaleY);
                }
            }
        }
        catch
        {
            // 异常时降级至屏幕上下文换算
        }

        var cursorFallback = Forms.Cursor.Position;
        var screenCtx = ScreenHelper.GetScreenContextAtPoint(new System.Windows.Point(cursorFallback.X, cursorFallback.Y));
        var screenDips = ScreenHelper.PhysicalToDip(new System.Windows.Point(cursorFallback.X, cursorFallback.Y), screenCtx.DpiScale);
        return new System.Windows.Point(screenDips.X - Left, screenDips.Y - Top);
    }



    private void RadialMenuWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var wheelPoint = WindowDipToLogical(e.GetPosition(this));
        if (_editModeLocked)
        {
            if (EditMaxScrollOffset > 0)
            {
                // 向下滚 (Delta < 0) -> 内容向上推移 (EditScrollOffsetY 增加，往下滚动浏览)
                // 向上滚 (Delta > 0) -> 内容向下推移 (EditScrollOffsetY 减少，往上回卷浏览)
                double delta = e.Delta;
                double step = 100.0;
                double newOffset = EditScrollOffsetY - (delta > 0 ? step : -step);
                EditScrollOffsetY = Math.Clamp(newOffset, 0, EditMaxScrollOffset);
                UpdateSelectionFromCursor(wheelPoint);
            }
            e.Handled = true;
            return;
        }

        if (_topLevelPages.Count <= 1)
        {
            return;
        }

        var currentIndex = _topLevelPages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }
        var deltaIndex = e.Delta < 0 ? 1 : -1;
        var nextIndex = (currentIndex + deltaIndex + _topLevelPages.Count) % _topLevelPages.Count;
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
        double minX = -300;
        double maxX = 300;
        double minY = -300;
        double maxY = 300;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { page.Id };
        var items = _mainWindow.GetRadialMenuItems(page.Id);
        
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!string.IsNullOrWhiteSpace(item.ChildPageId))
            {
                double offsetAngleDegrees;
                if (i < RadialMenuSettings.InnerSlotCount)
                {
                    // 内圈 8 槽位 (0~7)
                    offsetAngleDegrees = -90 + i * 45.0;
                }
                else if (i < RadialMenuSettings.InnerSlotCount + RadialMenuSettings.MiddleSlotCount)
                {
                    // 中圈 16 槽位 (8~23)
                    offsetAngleDegrees = -90 + (i - RadialMenuSettings.InnerSlotCount) * 22.5;
                }
                else
                {
                    // 外圈 8 槽位 (24~31)
                    offsetAngleDegrees = -90 + (i - RadialMenuSettings.InnerSlotCount - RadialMenuSettings.MiddleSlotCount) * 45.0;
                }

                // 编辑模式下从独立大轮盘延伸出的子环统一位于外圈外侧 350px 处
                double offsetDist = (i % 2 == 0 ? 350 : 380);

                var angleRad = offsetAngleDegrees * Math.PI / 180.0;
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
                            var grandAngle = -90 + (c % 8) * 45.0;
                            var grandAngleRad = grandAngle * Math.PI / 180.0;
                            double gCenterX = subCenterX + Math.Cos(grandAngleRad) * 180;
                            double gCenterY = subCenterY + Math.Sin(grandAngleRad) * 180;

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
        var swExpand = System.Diagnostics.Stopwatch.StartNew();
        SubRings.Clear();
        var settings = AppSettingsStore.LoadCached();
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
            var swPage = System.Diagnostics.Stopwatch.StartNew();
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
            swPage.Stop();
            HostAssets.AppendLog($"[EditPerf] Built topLevel ring '{page.Name}' in {swPage.ElapsedMilliseconds} ms.");
        }

        double totalContentHeight = currentY + currentRowMaxHeight + 100.0;
        double viewHeight = ActualHeight > 400 ? ActualHeight : Height;
        EditMaxScrollOffset = Math.Max(0, totalContentHeight - viewHeight);
        EditScrollOffsetY = Math.Clamp(EditScrollOffsetY, 0, EditMaxScrollOffset);

        // 默认激活当前选中的轮盘或第 1 个轮盘
        var activeId = string.IsNullOrWhiteSpace(_currentPageId) ? topLevelPages.FirstOrDefault()?.Id : _currentPageId;
        SetActiveRadial(activeId);

        swExpand.Stop();
        HostAssets.AppendLog($"[EditPerf] ExpandAllSubRingsInEditMode COMPLETE in {swExpand.ElapsedMilliseconds} ms: totalTopLevel={topLevelPages.Count}, SubRings.Count={SubRings.Count}, totalHeight={totalContentHeight:F0}, maxScroll={EditMaxScrollOffset:F0}.");
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

        // 1. 内圈分隔线 (8条) 与 中间层分隔线 (16条) 与 最外层分隔线 (8条)
        BuildSeparators(ring.Separators, cX, cY, 36, 100, RadialMenuSettings.InnerSlotCount);
        BuildSeparators(ring.OuterSeparators, cX, cY, 100, 165, RadialMenuSettings.MiddleSlotCount);
        BuildSeparators(ring.MostOuterSeparators, cX, cY, 165, 270, RadialMenuSettings.OuterSlotCount, isFadeOut: true);

        // 2. 内圈 8 个槽位 (半径 36~100)
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
                CreateSectorGeometry(cX, cY, 36, 100, childAngleDegrees - 22.5, childAngleDegrees + 22.5),
                cX,
                cY);
            ring.Items.Add(vm);
        }

        // 3. 中间层 16 个槽位 (半径 100~165)
        const double outerRadius = 132;
        for (var offset = 0; offset < RadialMenuSettings.MiddleSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + offset;
            var childAngleDegrees = -90 + offset * 22.5;
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
                RadialMenuRing.Middle,
                CreateSectorGeometry(cX, cY, 100, 165, childAngleDegrees - 11.25, childAngleDegrees + 11.25),
                cX,
                cY);
            ring.OuterItems.Add(vm);
        }

        // 4. 最外层 8 个槽位 (半径 165~280)
        const double mostOuterRadius = 198;
        for (var offset = 0; offset < RadialMenuSettings.OuterSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + RadialMenuSettings.MiddleSlotCount + offset;
            var childAngleDegrees = -90 + offset * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = cX + Math.Cos(childAngle) * mostOuterRadius - 25;
            var y = cY + Math.Sin(childAngle) * mostOuterRadius - 20;
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
                CreateSectorGeometry(cX, cY, 165, 280, childAngleDegrees - 22.5, childAngleDegrees + 22.5),
                cX,
                cY);
            ring.MostOuterItems.Add(vm);
        }

        SubRings.Add(ring);

        // 5. 如果该独立轮盘自身有子环，向外放射展开！
        var subRingSources = ring.Items.Concat(ring.OuterItems).Concat(ring.MostOuterItems).Where(it => !string.IsNullOrWhiteSpace(it.ChildPageId)).ToList();
        foreach (var childItem in subRingSources)
        {
            var cSlotCenterX = childItem.X + (childItem.Ring == RadialMenuRing.Inner ? 32 : 25);
            var cSlotCenterY = childItem.Y + (childItem.Ring == RadialMenuRing.Inner ? 25 : 20);
            RecursivelyBuildSubRings(childItem, cX, cY, cSlotCenterX, cSlotCenterY, childItem.AngleDegrees, 1, visitedPageIds);
        }
    }

    private (double X, double Y) ComputeSubRingCenter(RadialMenuItemViewModel parent, double parentCenterX, double parentCenterY, int level)
    {
        var angle = parent.AngleDegrees * Math.PI / 180.0;

        // 动态交错轨道算法：
        // 1. 如果父级是独立大轮盘（level == 1），子环统一靠外圈 350px 展开，确保大轮盘自身 3 层槽位完整展示且不与子环发生物理重叠；
        // 2. 如果父级是普通子环（level > 1），二级子环紧贴上一级子环展开（180px）。
        double offsetDistance = (level == 1)
            ? (parent.Index % 2 == 0 ? 350 : 380)
            : (parent.Index % 2 == 0 ? 180 : 210);

        double cX = parentCenterX + Math.Cos(angle) * offsetDistance;
        double cY = parentCenterY + Math.Sin(angle) * offsetDistance;
        ClampRingCenter(ref cX, ref cY, 112);
        return (cX, cY);
    }

    private bool HasSubRingAtLevel(int level, string pageId, double centerX, double centerY)
    {
        return SubRings.Count == level &&
            string.Equals(SubRings[level - 1].PageId, pageId, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(SubRings[level - 1].CenterX - centerX) < 0.5 &&
            Math.Abs(SubRings[level - 1].CenterY - centerY) < 0.5;
    }

    private void RecursivelyBuildSubRings(RadialMenuItemViewModel parent, double parentCenterX, double parentCenterY, double parentSlotCenterX, double parentSlotCenterY, double parentAngleDegrees, int level, HashSet<string> visitedPageIds)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId) || visitedPageIds.Contains(parent.ChildPageId) || level > 3)
        {
            HostAssets.AppendDebug($"[EditModeDebug] RecursivelyBuildSubRings skipped: childPageId={parent.ChildPageId}, visited={visitedPageIds.Contains(parent.ChildPageId ?? string.Empty)}, level={level}");
            return;
        }

        visitedPageIds.Add(parent.ChildPageId);

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);

        var (cX, cY) = ComputeSubRingCenter(parent, parentCenterX, parentCenterY, level);

        // 幂等守卫：16ms 选中定时器每 tick 都会走到这里，目标层级/页面/位置都一致时
        // 跳过整棵子树的重建（清空+新建+全量重布局），否则悬停子环槽位就会持续卡顿
        if (HasSubRingAtLevel(level, parent.ChildPageId, cX, cY))
        {
            return;
        }

        HostAssets.AppendDebug($"[EditModeDebug] RecursivelyBuildSubRings: childPageId={parent.ChildPageId}, itemsCount={items.Count}, cX={cX:F1}, cY={cY:F1}, level={level}");

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

        var angle = parentAngleDegrees * Math.PI / 180.0;
        double offsetDistance = 180;
        if (level == 1)
        {
            offsetDistance = parent.Ring switch
            {
                RadialMenuRing.Inner => 180,
                RadialMenuRing.Middle => 245,
                RadialMenuRing.Outer => 350,
                _ => 180
            };
        }
        else
        {
            offsetDistance = 180;
        }

        double cX = parentCenterX + Math.Cos(angle) * offsetDistance;
        double cY = parentCenterY + Math.Sin(angle) * offsetDistance;
        ClampRingCenter(ref cX, ref cY, 112);

        // 幂等守卫：16ms 选中定时器每 tick 都会到这里，同层级同页面同位置且无更深子环时
        // 直接跳过重建（否则每秒 60 次 清空+新建+全量重布局+磁盘读设置，悬停即卡顿）。
        var existing = SubRings.Count == level ? SubRings.ElementAtOrDefault(level - 1) : null;
        if (existing != null &&
            string.Equals(existing.PageId, parent.ChildPageId, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(existing.CenterX - cX) < 0.5 &&
            Math.Abs(existing.CenterY - cY) < 0.5)
        {
            return;
        }

        ClearSubRingsAboveLevel(level);

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);

        var ring = new RadialMenuNestedRingViewModel
        {
            PageId = parent.ChildPageId,
            Level = level,
            Title = parent.ChildPageTitle,
            CenterX = cX,
            CenterY = cY,
            ParentX = parent.X + (parent.Ring == RadialMenuRing.Inner ? 32 : (parent.Ring == RadialMenuRing.Middle ? 28 : 25)),
            ParentY = parent.Y + (parent.Ring == RadialMenuRing.Inner ? 25 : (parent.Ring == RadialMenuRing.Middle ? 22 : 20))
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

        if (distance < 36)
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

        if (ring.IsStandaloneRadial)
        {
            if (distance > 165)
            {
                // 第 3 层：最外层 8 槽位 (165 ~ 280, 步长 45 度)
                var outerIndex = ((int)Math.Round((angle + 90) / 45.0) % RadialMenuSettings.OuterSlotCount + RadialMenuSettings.OuterSlotCount) % RadialMenuSettings.OuterSlotCount;
                item = ring.MostOuterItems.ElementAtOrDefault(outerIndex);
            }
            else if (distance > 100)
            {
                // 第 2 层：中间层 16 槽位 (100 ~ 165, 步长 22.5 度)
                var midIndex = ((int)Math.Round((angle + 90) / 22.5) % RadialMenuSettings.MiddleSlotCount + RadialMenuSettings.MiddleSlotCount) % RadialMenuSettings.MiddleSlotCount;
                item = ring.OuterItems.ElementAtOrDefault(midIndex);
            }
            else
            {
                // 第 1 层：内圈 8 槽位 (36 ~ 100, 步长 45 度)
                var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
                item = ring.Items.ElementAtOrDefault(index);
            }
        }
        else
        {
            // 普通单层子环：内圈 8 槽位 (36 ~ 100, 步长 45 度)
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
        if (_editModeLocked)
        {
            var full = GetMenuSize();
            x = Math.Clamp(x, radius + 8, full.Width - radius - 8);
            y = Math.Max(y, radius + 8);
            return;
        }

        // 常态呼出：逻辑画布固定 1400x1400，子环按画布范围摆放；
        // 窗口尺寸交给 UpdateWindowToFitContent 事后按"实际使用范围"放大，保证不被裁走
        var minX = radius + 8;
        var maxX = NormalWindowSize - radius - 8;
        var minY = radius + 8;
        var maxY = NormalWindowSize - radius - 8;
        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);
    }

    private void RadialMenuWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var clickPoint = WindowDipToLogical(e.GetPosition(this));
        HostAssets.AppendLog($"[PickerLog] RadialMenu LeftButtonDown: isPickerMode={_mainWindow.IsRadialPickerMode}, popupOpen={_mainWindow.SearchScopePopup?.IsOpen}, isEditLocked={_editModeLocked}, pageStack={_pageStack.Count}, point=({clickPoint.X:F1},{clickPoint.Y:F1}).");
        if (_mainWindow.IsRadialPickerMode || _mainWindow.SearchScopePopup?.IsOpen == true)
        {
            return;
        }

        var center = GetWindowCenter();
        var dxMain = clickPoint.X - center.X;
        var dyMain = clickPoint.Y - center.Y;
        var distMain = Math.Sqrt(dxMain * dxMain + dyMain * dyMain);

        if (_editModeLocked)
        {
            var contentPoint = new System.Windows.Point(clickPoint.X, clickPoint.Y + _editScrollOffsetY);

            // 阶段 1：全局最高优先级——优先检查是否点击了任何轮盘（父轮盘或子环）的中心区域 (dist <= 48)
            RadialMenuNestedRingViewModel? hitCenterRing = null;
            double minCenterDist = double.MaxValue;
            foreach (var ring in SubRings)
            {
                var dx = contentPoint.X - ring.CenterX;
                var dy = contentPoint.Y - ring.CenterY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= 48 && dist < minCenterDist)
                {
                    minCenterDist = dist;
                    hitCenterRing = ring;
                }
            }

            if (hitCenterRing != null)
            {
                SetActiveRadial(hitCenterRing.PageId);
                e.Handled = true;
                return;
            }

            // 阶段 2：检查是否点击在某个轮盘的区域内 (槽位或轮盘主体)
            for (int i = SubRings.Count - 1; i >= 0; i--)
            {
                var ring = SubRings[i];
                var dx = contentPoint.X - ring.CenterX;
                var dy = contentPoint.Y - ring.CenterY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var maxRadius = ring.IsStandaloneRadial ? 280 : 100;
                if (dist <= maxRadius)
                {
                    if (ring.SelectedItem != null)
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
                    else
                    {
                        // 点击了该轮盘的内部/背景区域：直接选中并激活该轮盘！
                        SetActiveRadial(ring.PageId);
                    }
                    e.Handled = true;
                    return;
                }
            }

            // 阶段 3：在编辑模式下，点击任何外部空白区域均不退出编辑模式
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
            ApplyVisualContentRootMode();
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

        var point = WindowDipToLogical(e.GetPosition(this));
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
        var clickPoint = WindowDipToLogical(e.GetPosition(this));

        // 如果在编辑模式下，点击点实际落在了任何一个轮盘的中心圆内 (dist <= 48)，优先触发该轮盘中心激活！
        if (_editModeLocked)
        {
            var contentPoint = new System.Windows.Point(clickPoint.X, clickPoint.Y + _editScrollOffsetY);
            foreach (var ring in SubRings)
            {
                var dx = contentPoint.X - ring.CenterX;
                var dy = contentPoint.Y - ring.CenterY;
                if (Math.Sqrt(dx * dx + dy * dy) <= 48)
                {
                    SetActiveRadial(ring.PageId);
                    e.Handled = true;
                    return;
                }
            }
        }

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

            // 如果是空白槽位，鼠标左键点击立即弹出添加菜单！
            if (item.IsEmpty)
            {
                var target = new RadialEditTarget(item.OwnerPageId, item.Index, item);
                ShowAddMenuForTarget(target);
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
        var center = GetWindowCenter();
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
        BeginModalChildDialog();
        try
        {
            var defaultProcess = page.ContextProcessName ?? _activeProcessName ?? "explorer";
            var initialList = string.IsNullOrWhiteSpace(page.ContextProcessName)
                ? new List<string>()
                : new List<string> { page.ContextProcessName };

            var inputWindow = new ProcessPickerWindow("绑定应用", $"请选择【{page.Name}】绑定的专属应用进程（留空表示全局通用）：", defaultProcess, initialList)
            {
                Owner = this,
                Topmost = true
            };
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
        }
        finally
        {
            EndModalChildDialog();
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
            {
                Activate();
                _selectionTimer.Start();
            }
        }
    }

    private bool ShowForegroundConfirmDialog(string message, string title)
    {
        _isOpeningSubDialog = true;
        _editInteractionActive = true;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32Native.SetForegroundWindow(hwnd);

            uint flags = Win32Native.MB_YESNO |
                         Win32Native.MB_ICONWARNING |
                         Win32Native.MB_DEFBUTTON2 |
                         Win32Native.MB_SETFOREGROUND |
                         Win32Native.MB_TOPMOST;

            int result = Win32Native.MessageBox(hwnd, message, title, flags);
            return result == Win32Native.IDYES;
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

        // 级联删除：将当前轮盘及其拥有的所有层级后代子环页面一并移除并清理引用
        var deletedIds = radialSettings.CascadeDeletePages([page.Id]);

        settings.RadialMenu = radialSettings;
        AppSettingsStore.Save(settings);

        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("radial-inline-edit");

        LoadRadialMenuPages();
        _currentPageId = radialSettings.SelectedPageId;
        if (_editModeLocked)
        {
            ExpandAllSubRingsInEditMode();
        }
        else
        {
            BuildItems(_lastRadiusPixels);
        }
        ActiveTitle = $"已删除轮盘：{page.Name}";
        HostAssets.AppendLog($"[RadialMenuLog] Radial page cascade deleted: {page.Name} ({page.Id}), total removed pages={deletedIds.Count}");
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
        BeginModalChildDialog();
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
                Owner = this,
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
            EndModalChildDialog();
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
            {
                Activate();
                _selectionTimer.Start();
            }
        }
    }

    private void RadialSubRingCenter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuNestedRingViewModel ring } && !string.IsNullOrWhiteSpace(ring.PageId))
        {
            SetActiveRadial(ring.PageId);
            e.Handled = true;
        }
    }

    private void UpdateEditModeState()
    {
        CenterPrimaryText = PageTitle;
        IsCenterCloseMode = false;
        OnPropertyChanged(nameof(IsEditModeLocked));
        OnPropertyChanged(nameof(IsCenterWheelVisible));
        OnPropertyChanged(nameof(EditButtonBrush));
        OnPropertyChanged(nameof(HasSubRings));
        OnPropertyChanged(nameof(IsScrollBarVisible));
        if (CenterMainWheelContainer != null)
        {
            CenterMainWheelContainer.Visibility = _editModeLocked ? Visibility.Collapsed : Visibility.Visible;
        }

        foreach (var item in Items) item.IsEditMode = _editModeLocked;
        foreach (var item in MiddleItems) item.IsEditMode = _editModeLocked;
        foreach (var item in OuterItems) item.IsEditMode = _editModeLocked;
        foreach (var item in ChildItems) item.IsEditMode = _editModeLocked;
        foreach (var item in GrandChildItems) item.IsEditMode = _editModeLocked;
        foreach (var item in GreatGrandChildItems) item.IsEditMode = _editModeLocked;
        foreach (var ring in SubRings)
        {
            foreach (var item in ring.Items) item.IsEditMode = _editModeLocked;
        }

        if (_editModeLocked)
        {
            _selectionTimer.Stop();
        }
        else
        {
            _editScrollOffsetY = 0;
            OnPropertyChanged(nameof(EditScrollOffsetY));
            OnPropertyChanged(nameof(EditContentTranslateY));
        }
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

        // 1. 仓库 (搜索图标，点击直接调出主搜索/选择界面)
        var existingExtensionItem = new MenuItem
        {
            Header = BrandTerms.Format("{Warehouse}"),
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
            Header = BrandTerms.Format("新建{MiniApp}"),
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
            // 在模态录制窗口打开前同步关闭菜单，确保弹层销毁与鼠标捕获释放先于 ShowDialog 完成
            if (parentMenu is ContextMenu addMenu && addMenu.IsOpen)
            {
                addMenu.IsOpen = false;
            }
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
            var friendlyAppName = GetProcessDisplayName(_activeProcessName);
            bool isBound = IsRadialSlotBoundToCurrentApp(target);
            if (isBound)
            {
                menu.Items.Add(new Separator());
                var unbindItem = new MenuItem
                {
                    Header = $"取消绑定 (当前应用: {friendlyAppName})",
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
                    Header = $"绑定到当前应用: {friendlyAppName}",
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
                : (!string.IsNullOrWhiteSpace(result.Command.OpenTarget) ? $"{ExtensionIdPrefixes.SearchResult}{result.Command.OpenTarget}" : null);

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
            // 必须复位，否则菜单 Closed 处理器的 !_isOpeningSubDialog 永远为 false，
            // _editInteractionActive 卡 true → 轮盘失焦不隐藏、Esc 关不掉、选中逻辑冻结
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            EnsureActivatedForEdit();
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
            // 同上：不复位会导致轮盘进入“冻结”死状态
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            EnsureActivatedForEdit();
            UpdateCenterText();
        }
    }

    /// <summary>
    /// 模态子对话框统一入场：释放残留鼠标捕获，并让轮盘整窗对鼠标穿透。
    /// </summary>
    private void BeginModalChildDialog()
    {
        // 释放可能残留在轮盘/菜单弹层上的 WPF 鼠标捕获，避免模态对话框客户区收不到鼠标消息
        Mouse.Capture(null);
        // 编辑锁定模式下轮盘全屏遮罩会吃掉对话框区域的鼠标点击，对话框期间整窗穿透
        SetMouseClickThrough(true);
    }

    /// <summary>
    /// 模态子对话框统一退场：恢复轮盘鼠标命中。
    /// </summary>
    private void EndModalChildDialog()
    {
        SetMouseClickThrough(false);
    }

    private void SetSimulatedKeyForTarget(RadialEditTarget target)
    {
        HostAssets.AppendLog($"[SetSimulatedKeyLog] SetSimulatedKeyForTarget: page={target.PageId}, index={target.Index}");
        _isOpeningSubDialog = true;
        _editInteractionActive = true;
        IsHitTestVisible = false;
        BeginModalChildDialog();
        try
        {
            const string simulatedPrefix = ExtensionIdPrefixes.SimulatedKey;
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
                // 显式父子关系：保证对话框永远位于轮盘覆盖层之上，并在关闭后归还焦点
                Owner = this,
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
            EndModalChildDialog();
            IsHitTestVisible = true;
            _isOpeningSubDialog = false;
            _editInteractionActive = false;
            if (IsVisible && !_mainWindow.IsRadialPickerMode)
            {
                EnsureActivatedForEdit();
                _selectionTimer.Start();
            }
        }
    }

    private void ClearCommandFromTarget(RadialEditTarget target)
    {
        EnsureActivatedForEdit();
        SaveRadialSlotCommand(target.PageId, target.Index, null, null);
        ActiveTitle = "已清空槽位内容";
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

            const string simulatedPrefix = ExtensionIdPrefixes.SimulatedKey;
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
                BuildItems(_lastRadiusPixels);
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
            _editInteractionActive = false;
            EnsureActivatedForEdit();
            UpdateCenterText();
        }
    }

    private void ClearSlotContentFromTarget(RadialEditTarget target)
    {
        EnsureActivatedForEdit();
        if (target.Item.HasChildPage)
        {
            ClearChildPageFromTarget(target);
        }
        else
        {
            ClearCommandFromTarget(target);
        }
        ActiveTitle = "已清空槽位内容";
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
        if (!string.IsNullOrWhiteSpace(removedId))
        {
            settings.RadialMenu.CascadeDeletePages([removedId]);
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
        if (_editModeLocked)
        {
            EnsureActivatedForEdit();
            LoadRadialMenuPages();
            BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
            UpdateEditModeState();
            ActiveTitle = "编辑已保存";
            _selectionTimer.Start();
            return;
        }

        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
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
        if (_isEditLoading)
        {
            return;
        }

        if (!_editModeLocked)
        {
            // ===== 阶段 1：第 0 毫秒即时视觉响应 =====
            _editModeLocked = true;
            ApplyVisualContentRootMode();
            IsEditLoading = true;
            _selectionTimer.Stop();

            // 立即隐藏原本的呼出前单层轮盘和子环
            if (CenterMainWheelContainer != null)
            {
                CenterMainWheelContainer.Visibility = Visibility.Collapsed;
            }
            SubRings.Clear();

            // 立即显示遮罩层与加载动画
            UpdateEditModeState();
            EnsureActivatedForEdit();

            HostAssets.AppendLog($"[EditPerf] ToggleEditModeState phase 1 instant visual feedback applied (wheel hidden, backdrop on, spinner spinning).");

            // ===== 阶段 2：异步派发执行重型窗口全屏扩展与全量平铺轮盘构建 =====
            Dispatcher.InvokeAsync(() =>
            {
                var swPhase2 = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // 1. 全屏覆盖屏幕工作区
                    OverlayWindowManager.CoverActiveScreen(this, new System.Windows.Point(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));

                    // 2. 加载页面并校验选中项
                    LoadRadialMenuPages();
                    if (string.IsNullOrEmpty(_currentPageId) || !_pages.Any(p => p.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (_pages.Count > 0)
                        {
                            _currentPageId = _pages[0].Id;
                        }
                    }

                    // 3. 构建全部平铺轮盘
                    BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
                }
                finally
                {
                    IsEditLoading = false;
                    UpdateEditModeState();
                    swPhase2.Stop();
                    HostAssets.AppendLog($"[EditPerf] ToggleEditModeState phase 2 async loading COMPLETE in {swPhase2.ElapsedMilliseconds} ms, expandedSubRings={SubRings.Count}.");
                }
            }, DispatcherPriority.Render);
        }
        else
        {
            // ===== 退出编辑模式 =====
            _editModeLocked = false;
            ApplyVisualContentRootMode();
            IsEditLoading = false;
            Width = NormalWindowSize;
            Height = NormalWindowSize;
            UpdateLayout();
            LoadRadialMenuPages();
            SubRings.Clear();
            PositionAroundCursor();
            CenterOnAnchorPhysically("exit-edit");
            BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
            UpdateEditModeState();
            HostAssets.AppendLog($"[EditPerf] Exited edit mode.");
        }
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

        var currentAppName = string.IsNullOrWhiteSpace(_activeProcessName) ? "当前应用" : GetProcessDisplayName(_activeProcessName);
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
        var processName = ResolveCurrentRadialProcessNameForEdit();
        if (string.IsNullOrWhiteSpace(processName))
        {
            HostAssets.AppendLog("[RadialMenuLog] AddAppPage skipped: unable to resolve current application process.");
            return;
        }

        _activeProcessName = processName;

        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];

        var normalizedProcess = processName.Trim().ToLowerInvariant().Replace(".exe", "");
        var friendlyName = GetProcessDisplayName(processName);
        int appCount = settings.RadialMenu.Pages.Count(p => 
            !string.IsNullOrEmpty(p.ContextProcessName) && 
            p.ContextProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase)) + 1;

        var newPage = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = appCount > 1 ? $"{friendlyName}专属 {appCount}" : $"{friendlyName}专属",
            ContextProcessName = normalizedProcess,
            ContextDisplayName = friendlyName,
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
            ApplyVisualContentRootMode();
            _selectionTimer.Stop();
            SubRings.Clear();
            Width = NormalWindowSize;
            Height = NormalWindowSize;
            BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
            UpdateEditModeState();
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

    private bool IsPointInToolBar(System.Windows.Point point)
    {
        if (EditToolBar == null || !EditToolBar.IsLoaded || EditToolBar.Visibility != Visibility.Visible)
        {
            return false;
        }
        try
        {
            var topLeft = EditToolBar.TranslatePoint(new System.Windows.Point(0, 0), VisualContentRoot);
            var rect = new Rect(topLeft.X, topLeft.Y, EditToolBar.ActualWidth, EditToolBar.ActualHeight);
            rect.Inflate(4, 4);
            return rect.Contains(point);
        }
        catch
        {
            return false;
        }
    }

    private bool IsPointInButton(FrameworkElement? button, System.Windows.Point point)
    {
        if (button == null || !button.IsLoaded || button.Visibility != Visibility.Visible)
        {
            return false;
        }
        try
        {
            var topLeft = button.TranslatePoint(new System.Windows.Point(0, 0), VisualContentRoot);
            var rect = new Rect(topLeft.X, topLeft.Y, button.ActualWidth, button.ActualHeight);
            rect.Inflate(2, 2);
            return rect.Contains(point);
        }
        catch
        {
            return false;
        }
    }

    private bool IsPointInPinButton(System.Windows.Point point) => IsPointInButton(PinButton, point);
    private bool IsPointInEditButton(System.Windows.Point point) => IsPointInButton(EditButton, point);
    private bool IsPointInAddButton(System.Windows.Point point) => IsPointInButton(AddButton, point);
    private bool IsPointInDeleteButton(System.Windows.Point point) => IsPointInButton(DeleteButton, point);
    private bool IsPointInSearchButton(System.Windows.Point point) => IsPointInButton(SearchButton, point);
    private bool IsPointInCloseButton(System.Windows.Point point) => IsPointInButton(CloseButton, point);

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
        ActiveTitle = _isPinned ? "松开取消置顶" : "松开置顶轮盘";
        return true;
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
        ActiveTitle = _editModeLocked ? "松开退出编辑" : "松开进入编辑";
        return true;
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
        if (!_editModeLocked)
        {
            ClearSubRingsAboveLevel(1);
        }
        ActiveTitle = "松开添加轮盘";
        return true;
    }

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

    private bool UpdateAllToolBarHoverStates(System.Windows.Point cursorPoint)
    {
        bool inToolBar = IsPointInToolBar(cursorPoint);

        bool editHover = UpdateEditHoverState(cursorPoint);
        bool pinHover = UpdatePinHoverState(cursorPoint);
        bool addHover = UpdateAddHoverState(cursorPoint);
        bool deleteHover = UpdateDeleteHoverState(cursorPoint);
        bool searchHover = UpdateSearchHoverState(cursorPoint);
        bool closeHover = UpdateCloseHoverState(cursorPoint);

        if (editHover || pinHover || addHover || deleteHover || searchHover || closeHover || inToolBar)
        {
            SetSelectedItem(null);
            if (!_editModeLocked)
            {
                ClearSubRingsAboveLevel(1);
            }
            IsGuideLineVisible = false;
            Cursor = System.Windows.Input.Cursors.Hand;
            if (inToolBar && !editHover && !pinHover && !addHover && !deleteHover && !searchHover && !closeHover)
            {
                ActiveTitle = "工具栏";
            }
            return true;
        }

        return false;
    }


    private bool IsRadialSlotBoundToCurrentApp(RadialEditTarget target)
    {
        if (string.IsNullOrWhiteSpace(_activeProcessName))
        {
            return false;
        }

        var settings = AppSettingsStore.LoadCached();
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
            settings.RadialMenu.CascadeDeletePages([appPage.Id]);
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
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        HostAssets.AppendDebug($"[RadialResidualDebug] Hide/Dock called: _editModeLocked={_editModeLocked}, SubRings.Count={SubRings.Count}, Opacity={Opacity}.");

        try
        {
            _selectionTimer.Stop();
            if (_wasActivatedForEdit && _previousForegroundWindow != IntPtr.Zero)
            {
                try
                {
                    Win32Native.SetForegroundWindow(_previousForegroundWindow);
                }
                catch { }
            }
            _wasActivatedForEdit = false;
            _editModeLocked = false;
            _editInteractionActive = false;
            IsGuideLineVisible = false;
            // 钉住态的生命周期只限单次呼出：停靠复用前必须复位，
            // 否则下次呼出走 IsPinned 分支直接 Activate 屏外透明窗口
            if (_isPinned)
            {
                _isPinned = false;
                OnPropertyChanged(nameof(IsPinned));
                OnPropertyChanged(nameof(PinButtonBrush));
                OnPropertyChanged(nameof(PinButtonTooltip));
            }
            // 复位 hover 状态：停靠实例复用时不能残留上一次的工具栏/hover 语义
            IsEditHoverActive = false;
            IsPinHoverActive = false;
            IsAddHoverActive = false;
            IsDeleteHoverActive = false;
            IsSearchHoverActive = false;
            IsCloseHoverActive = false;
            IsCenterHovered = false;
            _isOuterRingHoverActive = false;
            Opacity = 0;
            RootGrid.Visibility = Visibility.Hidden;
            DockOffscreen();
            // 注意：不再 Close()。窗口实例保持"停靠屏外"状态被 MainWindow 复用，
            // HWND/D3D 交换链/视觉树全部保留，二次呼出零重建成本。
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"RadialMenuWindow.Hide/Dock exception: {ex.Message}");
        }
    }

    /// <summary>窗口是否处于"已呼出过并停靠屏外、可被复用"状态。</summary>
    public bool IsDockedAvailable => _isPrewarmed && !_isClosing && !IsContentVisible;

    /// <summary>轮盘内容当前是否真正可见（停靠/隐藏态为 false；复用实例的 IsVisible 恒为 true，不能用于此判定）。</summary>
    public bool IsContentVisible => RootGrid.Visibility == Visibility.Visible && Opacity > 0.01 && IsVisible;

    /// <summary>停靠到屏幕外深处：清空动态子环、复位状态、物理移出屏幕（不销毁 HWND）。</summary>
    private void DockOffscreen()
    {
        try
        {
            SubRings.Clear();
            _pageStack.Clear();
            _fitContentPending = false;
            _isEntryAnimationActive = false;
            ApplyVisualContentRootMode();

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var handle = helper.Handle;
            if (handle != IntPtr.Zero)
            {
                // 紧凑尺寸 + 屏外坐标，一次 SetWindowPos 完成（保持复用时窗口状态确定）
                var windowDpi = GetWindowDpiScale();
                var wPhys = (int)Math.Round(CompactWindowSize * windowDpi);
                var hPhys = (int)Math.Round(CompactWindowSize * windowDpi);
                var offscreen = (int)OverlayWindowManager.OffScreenCoordinate;
                Win32Native.SetWindowPos(
                    handle,
                    IntPtr.Zero,
                    offscreen,
                    offscreen,
                    wPhys,
                    hPhys,
                    Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);
                Width = CompactWindowSize;
                Height = CompactWindowSize;
            }
            else
            {
                Left = OverlayWindowManager.OffScreenCoordinate;
                Top = OverlayWindowManager.OffScreenCoordinate;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[RadialMenuLog] DockOffscreen failed: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _selectionTimer.Stop();
            if (_globalEscapeHandler != null)
            {
                InputHookService.OnGlobalEscapePressed -= _globalEscapeHandler;
            }

            var source = (System.Windows.Interop.HwndSource?)PresentationSource.FromVisual(this);
            source?.RemoveHook(RadialWindowWndProc);

            Items.Clear();
            MiddleItems.Clear();
            OuterItems.Clear();
            ChildItems.Clear();
            GrandChildItems.Clear();
            GreatGrandChildItems.Clear();
            SubRings.Clear();

            MemoryOptimizationService.OptimizeMemoryInBackground();
            HostAssets.AppendDebug("[RadialResidualDebug] RadialMenuWindow OnClosed complete.");
        }
        catch { }
        base.OnClosed(e);
    }

    private void EnsureNoActivateStyle()
    {
        this.ApplyNoActivateToolWindowStyle();
    }

    /// <summary>
    /// 临时开启/关闭整窗鼠标穿透 (WS_EX_TRANSPARENT)。
    /// 编辑锁定模式下轮盘是带全屏不透明遮罩的 Topmost 覆盖层，会参与系统鼠标命中测试；
    /// 模态子对话框（模拟按键录制等）打开期间必须让本窗口对鼠标完全穿透，否则落在本窗口
    /// 矩形内的鼠标点击会被吞掉，表现为对话框确认/取消按钮点不动（键盘与非客户区仍正常）。
    /// </summary>
    private void SetMouseClickThrough(bool enabled)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var style = Win32Native.GetWindowLongPtr(hwnd, Win32Native.GWL_EXSTYLE).ToInt64();
            var newStyle = enabled
                ? style | Win32Native.WS_EX_TRANSPARENT
                : style & ~Win32Native.WS_EX_TRANSPARENT;
            if (newStyle == style)
            {
                return;
            }

            Win32Native.SetWindowLongPtr(hwnd, Win32Native.GWL_EXSTYLE, new IntPtr(newStyle));
            // 通知系统重新套用扩展样式，立即刷新鼠标命中测试结果
            Win32Native.SetWindowPos(
                hwnd,
                Win32Native.HWND_TOPMOST,
                0, 0, 0, 0,
                Win32Native.SWP_NOMOVE | Win32Native.SWP_NOSIZE |
                Win32Native.SWP_NOACTIVATE | Win32Native.SWP_FRAMECHANGED);
            HostAssets.AppendLog($"[SetSimulatedKeyLog] Radial window mouse click-through: {enabled}.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[SetSimulatedKeyLog] SetMouseClickThrough({enabled}) error: {ex.Message}");
        }
    }

    private void EnsureActivatedForEdit()
    {
        _wasActivatedForEdit = true;
        try
        {
            if (!IsVisible)
            {
                Show();
            }
            Activate();
            UpdateEditModeState();
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

    /// <summary>
    /// 解析当前应用进程名：优先用呼出时解析好的 _activeProcessName；
    /// 为空时（进程识别临时失败）现场再解析一次光标下窗口 → 前台窗口，保证创建专属轮盘不被卡死。
    /// </summary>
    private string ResolveCurrentRadialProcessNameForEdit()
    {
        if (!string.IsNullOrWhiteSpace(_activeProcessName))
        {
            return _activeProcessName;
        }

        var helperHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var (targetHwnd, name) = WindowSensorHelper.ResolveActiveTargetWindowAndProcess(excludeHwnd: helperHwnd);
        HostAssets.AppendLog($"[RadialMenuLog] AddAppPage resolved process on the fly: hwnd=0x{targetHwnd.ToInt64():X}, process={name ?? "(null)"}.");
        return name;
    }

    private static string GetProcessDisplayName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "全局";
        if (string.Equals(processName, "desktop", StringComparison.OrdinalIgnoreCase)) return "桌面";
        if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)) return "文件资源管理器";
        return processName;
    }

    private static ImageSource? GetProcessIcon(string processName)
    {
        try
        {
            // 进程图标按进程名缓存：FindExecutablePath 内部做 Process.GetProcessesByName +
            // exe 图标提取，是呼出路径上最重的同步开销，不能每次重建都重复执行
            if (_processIconCache.TryGetValue(processName, out var cached))
            {
                return cached;
            }

            ImageSource? icon = null;
            if (string.Equals(processName, "desktop", StringComparison.OrdinalIgnoreCase))
            {
                icon = ExtensionIconLibrary.ResolveImageSource("mdi:monitor-dashboard", null);
            }
            else
            {
                var path = FindExecutablePath(processName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    icon = ExtensionIconLibrary.TryExtractAssociatedIcon(path);
                }
            }

            _processIconCache[processName] = icon;
            return icon;
        }
        catch { }
        return null;
    }

    /// <summary>扩展变更后清除进程图标缓存，避免图标陈旧。</summary>
    public static void InvalidateProcessIconCache()
    {
        _processIconCache.Clear();
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

        var localPoint = WindowDipToLogical(new System.Windows.Point(screenPoint.X - Left, screenPoint.Y - Top));

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
        var center = GetWindowCenter();
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

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode == value) return;
            _isEditMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShouldShowEmptyPlaceholder));
            OnPropertyChanged(nameof(SectorOpacity));
            OnPropertyChanged(nameof(IsSectorVisible));
            OnPropertyChanged(nameof(SectorBrush));
        }
    }

    public double SectorOpacity => Ring == RadialMenuRing.Outer
        ? (IsEmpty ? 0.0 : 1.0)
        : (IsEmpty ? 0.0 : (IsSelected ? 0.58 : IsHovered ? 0.44 : 0.0));

    public bool IsSectorVisible => SectorGeometry != null && !IsEmpty && (Ring == RadialMenuRing.Outer ? (IsSelected || IsHovered) : true);

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
