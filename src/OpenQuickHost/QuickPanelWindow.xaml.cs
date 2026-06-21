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
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class QuickPanelWindow : Window, INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const int GlobalSlotCount = 12;
    private const int ContextSlotCount = 12;
    private const int FolderSlotCount = 24;
    private const int QuickPanelColumnCount = 4;
    private const double FolderOverlayBaseHeightDips = 520;
    private const double FolderOverlayDepthStepDips = 42;
    private const double FolderOverlayMinHeightDips = 360;
    private const double SidebarWidthDips = 42;
    private const double SlotGridHorizontalMarginDips = 6;
    private const double SlotIconWidthDips = 40;
    private const double CursorIconSafetyDips = 6;
    private readonly MainWindow _mainWindow;
    private AppSettings _settings;
    private readonly List<SlotViewModel> _allGlobalSlots = new();
    private readonly List<SlotViewModel> _allContextSlots = new();
    private readonly List<QuickPanelGroupItem> _allGlobalGroups = new();
    private readonly List<QuickPanelGroupItem> _allContextGroups = new();
    private bool _isPinned;
    private SlotViewModel? _hoveredSlot;
    private bool _isExecutingSlot;
    private IntPtr _previousForegroundWindow;
    private IntPtr _previousFocusWindow;
    private readonly DispatcherTimer _releaseTargetTimer;
    private ForegroundAppContext? _foregroundAppContext;
    private QuickPanelGroupItem? _selectedGlobalGroup;
    private QuickPanelGroupItem? _selectedContextGroup;
    private bool _isShowingGlobalFavorites;
    private bool _isShowingContextFavorites;
    private DateTime _suppressAutoHideUntilUtc = DateTime.MinValue;
    private DateTime _lastContextMenuClosedAt = DateTime.MinValue;
    private bool _isEditMode;
    private System.Windows.Point? _dragStartPoint;
    private SlotViewModel? _dragSourceSlot;
    private readonly DispatcherTimer _folderCreationTimer;
    private SlotViewModel? _folderHoverTarget;
    private ObservableCollection<SlotViewModel> _activeFolderSlots = [];
    private string _activeFolderTitle = string.Empty;
    private bool _isFolderExpanded;
    private bool _isFolderPinnedOpen;
    private QuickPanelSlotReference? _activeFolderReference;
    private bool _isDraggingSlot;
    private SlotViewModel? _previewFolderSlot;
    private ObservableCollection<FolderPreviewIconViewModel> _folderPreviewItems = [];
    private string _folderPreviewTitle = string.Empty;
    private bool _isFolderPreviewVisible;
    private DateTimeOffset _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _suspendOutsideClickHideUntilUtc = DateTimeOffset.MinValue;

    public static bool HasUnreadMessages { get; set; } = false;

    public QuickPanelWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        ShowActivated = false;
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _releaseTargetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _releaseTargetTimer.Tick += (_, _) => PollReleaseTarget();
        _folderCreationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _folderCreationTimer.Tick += FolderCreationTimer_Tick;
        
        var mobileDetectTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        mobileDetectTimer.Tick += (_, _) =>
        {
            if (HasUnreadMessages)
            {
                MobileMessageBadge.Visibility = Visibility.Visible;
                MobileDiscoverBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                MobileMessageBadge.Visibility = Visibility.Collapsed;
                MobileDiscoverBadge.Visibility = LanDiscoveryService.LastKnownMobileIp != null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        };
        mobileDetectTimer.Start();
        
        LoadSlots();
        DataContext = this;

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };

        InputHookService.OnGlobalMouseDown += InputHookService_OnGlobalMouseDown;
        AddHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler(QuickPanelWindow_ContextMenuOpening));

        RunningExtensionRegistry.Changed += RunningExtensionRegistry_Changed;
        Closed += (s, e) =>
        {
            RunningExtensionRegistry.Changed -= RunningExtensionRegistry_Changed;
        };
    }

    public ObservableCollection<SlotViewModel> GlobalSlots { get; } = new();

    public ObservableCollection<SlotViewModel> ContextSlots { get; } = new();

    public ObservableCollection<QuickPanelGroupItem> GlobalGroups { get; } = new();

    public ObservableCollection<QuickPanelGroupItem> ContextGroups { get; } = new();

    public string GlobalSectionTitle => "通用工具";

    public string GlobalHintText => "不管切换到哪个窗口，这些工具一直在。";

    public string ContextSectionTitle => _foregroundAppContext == null
        ? "应用专属"
        : $"应用专属 · {_foregroundAppContext.ProcessName}";

    public string ContextHintText => _foregroundAppContext == null
        ? "你在用什么软件，这里就显示它专属的工具。"
        : $"你在用什么软件，这里就显示它专属的工具。当前识别：{_foregroundAppContext.ProcessName}。";

    public System.Windows.Media.Brush PinButtonBrush => _isPinned
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFF59E0B")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF888888")!;

    public string PinButtonTooltip => _isPinned ? "已常驻，失去焦点时不自动关闭" : "点击后常驻，失去焦点时不自动关闭";

    public QuickPanelGroupItem? SelectedGlobalGroup
    {
        get => _selectedGlobalGroup;
        private set
        {
            if (ReferenceEquals(_selectedGlobalGroup, value))
            {
                return;
            }

            if (_selectedGlobalGroup != null)
            {
                _selectedGlobalGroup.IsSelected = false;
            }

            _selectedGlobalGroup = value;
            if (_selectedGlobalGroup != null)
            {
                _selectedGlobalGroup.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelTitle));
            OnPropertyChanged(nameof(EditModeHintText));
        }
    }

    public QuickPanelGroupItem? SelectedContextGroup
    {
        get => _selectedContextGroup;
        private set
        {
            if (ReferenceEquals(_selectedContextGroup, value))
            {
                return;
            }

            if (_selectedContextGroup != null)
            {
                _selectedContextGroup.IsSelected = false;
            }

            _selectedContextGroup = value;
            if (_selectedContextGroup != null)
            {
                _selectedContextGroup.IsSelected = true;
            }

            OnPropertyChanged();
        }
    }

    public string PanelTitle => _isShowingGlobalFavorites
        ? "通用收藏"
        : SelectedGlobalGroup?.Name ?? "通用工具";

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (value == _isEditMode)
            {
                return;
            }

            _isEditMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditButtonTooltip));
            OnPropertyChanged(nameof(EditModeHintText));
        }
    }

    public string EditButtonTooltip => IsEditMode ? "完成编辑" : "编辑面板";

    public string EditModeHintText => IsEditMode
        ? "编辑模式：拖动图标可移动/交换；按住 Ctrl 拖到图标上成组。"
        : PanelTitle;

    public ObservableCollection<SlotViewModel> ActiveFolderSlots
    {
        get => _activeFolderSlots;
        private set
        {
            _activeFolderSlots = value;
            OnPropertyChanged();
        }
    }

    public string ActiveFolderTitle
    {
        get => _activeFolderTitle;
        private set
        {
            _activeFolderTitle = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolderExpanded
    {
        get => _isFolderExpanded;
        private set
        {
            if (value == _isFolderExpanded)
            {
                return;
            }

        _isFolderExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveFolderOverlayHeight));
        }
    }

    public bool IsDraggingSlot
    {
        get => _isDraggingSlot;
        private set
        {
            if (value == _isDraggingSlot)
            {
                return;
            }

            _isDraggingSlot = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<FolderPreviewIconViewModel> FolderPreviewItems
    {
        get => _folderPreviewItems;
        private set
        {
            _folderPreviewItems = value;
            OnPropertyChanged();
        }
    }

    public string FolderPreviewTitle
    {
        get => _folderPreviewTitle;
        private set
        {
            _folderPreviewTitle = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolderPreviewVisible
    {
        get => _isFolderPreviewVisible;
        private set
        {
            if (value == _isFolderPreviewVisible)
            {
                return;
            }

            _isFolderPreviewVisible = value;
            OnPropertyChanged();
        }
    }

    public double ActiveFolderOverlayHeight =>
        Math.Max(FolderOverlayMinHeightDips, FolderOverlayBaseHeightDips - GetActiveFolderDepth() * FolderOverlayDepthStepDips);

    private int GetActiveFolderDepth() => (_activeFolderReference?.ContainerPath.Count ?? 0) + (_activeFolderReference == null ? 0 : 1);

    private void LoadSlots()
    {
        _settings = AppSettingsStore.Load();
        LoadGroups();
        GlobalSlots.Clear();
        ContextSlots.Clear();
        var allCommands = _mainWindow.GetAllCommands();

        if (_isShowingGlobalFavorites)
        {
            var favIds = _settings.GlobalFavoriteExtensionIds;
            foreach (var favId in favIds)
            {
                var command = allCommands.FirstOrDefault(c => c.ExtensionId == favId);
                if (command != null)
                    GlobalSlots.Add(new SlotViewModel(GlobalSlots.Count, command, true));
            }
            while (GlobalSlots.Count < GlobalSlotCount)
                GlobalSlots.Add(new SlotViewModel(GlobalSlots.Count, null, false));
        }
        else
        {
            var group = GetSelectedGlobalGroupSettings();
            for (int i = 0; i < GlobalSlotCount; i++)
            {
                var slotItem = group?.SlotItems.ElementAtOrDefault(i);
                GlobalSlots.Add(CreateSlotViewModel(i, slotItem, allCommands, isContextual: false, group?.Id, []));
            }
        }

        if (_isShowingContextFavorites)
        {
            var favIds = _settings.ContextFavoriteExtensionIds;
            foreach (var favId in favIds)
            {
                var command = allCommands.FirstOrDefault(c => c.ExtensionId == favId);
                if (command != null)
                    ContextSlots.Add(new SlotViewModel(ContextSlots.Count, command, true, isContextual: true));
            }
        }
        else
        {
            var group = GetSelectedContextGroupSettings();
            for (int i = 0; i < ContextSlotCount; i++)
            {
                var slotItem = group?.SlotItems.ElementAtOrDefault(i);
                ContextSlots.Add(CreateSlotViewModel(i, slotItem, allCommands, isContextual: true, group?.Id, []));
            }
        }

        while (ContextSlots.Count < ContextSlotCount)
            ContextSlots.Add(new SlotViewModel(ContextSlots.Count, null, false, isContextual: true));

        _allGlobalSlots.Clear();
        _allGlobalSlots.AddRange(GlobalSlots);
        _allContextSlots.Clear();
        _allContextSlots.AddRange(ContextSlots);
    }

    private SlotViewModel CreateSlotViewModel(
        int index,
        QuickPanelSlotItem? item,
        IReadOnlyList<CommandItem> allCommands,
        bool isContextual,
        string? groupId = null,
        IReadOnlyList<int>? containerPath = null)
    {
        if (item == null)
        {
            var empty = new SlotViewModel(index, null, false, isContextual: isContextual);
            empty.SetSlotLocation(groupId, containerPath);
            return empty;
        }

        if (item.IsFolder)
        {
            var folderSlotItems = GetFolderSlotItems(item);
            var resolvedCommands = folderSlotItems
                .Where(static slot => slot != null && !slot.IsFolder && !string.IsNullOrWhiteSpace(slot.ExtensionId))
                .Select(slot => allCommands.FirstOrDefault(command => string.Equals(command.ExtensionId, slot!.ExtensionId, StringComparison.OrdinalIgnoreCase)))
                .OfType<CommandItem>()
                .ToList();
            var folder = SlotViewModel.CreateFolder(
                index,
                item.FolderName ?? "新分组",
                resolvedCommands.Select(static command => command.ExtensionId).ToList(),
                folderSlotItems,
                resolvedCommands,
                isContextual);
            folder.SetSlotLocation(groupId, containerPath);
            return folder;
        }

        var command = string.IsNullOrWhiteSpace(item.ExtensionId)
            ? null
            : allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, item.ExtensionId, StringComparison.OrdinalIgnoreCase));
        var isFav = command != null &&
                    (isContextual
                        ? _settings.ContextFavoriteExtensionIds.Contains(command.ExtensionId)
                        : _settings.GlobalFavoriteExtensionIds.Contains(command.ExtensionId));
        var vm = new SlotViewModel(index, command, isFav, isContextual: isContextual);
        if (item != null)
        {
            vm.IsShortcut = item.IsShortcut;
        }
        vm.SetSlotLocation(groupId, containerPath);
        return vm;
    }

    private void LoadGroups()
    {
        GlobalGroups.Clear();
        ContextGroups.Clear();
        _allGlobalGroups.Clear();
        _allContextGroups.Clear();
        foreach (var group in _settings.QuickPanelGlobalGroups)
        {
            var item = new QuickPanelGroupItem(group.Id, group.Name);
            _allGlobalGroups.Add(item);
            GlobalGroups.Add(item);
        }

        foreach (var group in GetVisibleContextGroups())
        {
            var item = new QuickPanelGroupItem(group.Id, group.Name);
            _allContextGroups.Add(item);
            ContextGroups.Add(item);
        }

        SelectedGlobalGroup = GlobalGroups.FirstOrDefault(group => string.Equals(group.Id, _settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
            ?? GlobalGroups.FirstOrDefault();
        SelectedContextGroup = ContextGroups.FirstOrDefault(group => string.Equals(group.Id, _settings.SelectedQuickPanelContextGroupId, StringComparison.OrdinalIgnoreCase))
            ?? ContextGroups.FirstOrDefault();
    }

    private void HubSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = HubSearchBox.Text.Trim().ToLower();
        if (string.IsNullOrEmpty(query))
        {
            RestoreSlotCollections();
            return;
        }

        var filteredGlobal = _allGlobalSlots
            .Where(s => s.IsOccupied && s.Title.ToLower().Contains(query))
            .ToList();
        var filteredContext = _allContextSlots
            .Where(s => s.IsOccupied && s.Title.ToLower().Contains(query))
            .ToList();

        GlobalSlots.Clear();
        foreach (var slot in filteredGlobal) GlobalSlots.Add(slot);
        ContextSlots.Clear();
        foreach (var slot in filteredContext) ContextSlots.Add(slot);
    }

    private void SaveSlots(bool isContextual)
    {
        var group = isContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (group == null)
        {
            return;
        }

        group.SlotItems.Clear();
        var sourceSlots = isContextual ? ContextSlots : GlobalSlots;
        var slotCount = isContextual ? ContextSlotCount : GlobalSlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            var vm = sourceSlots.ElementAtOrDefault(i);
            group.SlotItems.Add(vm?.CloneSlotItem());
        }
        group.Slots = group.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        if (isContextual)
        {
            _settings.SelectedQuickPanelContextGroupId = group.Id;
        }
        else
        {
            _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        }
        SaveQuickPanelSettings(isContextual ? "quickpanel-save-context-slots" : "quickpanel-save-global-slots");
    }

    private QuickPanelGroupSettings? GetSelectedGlobalGroupSettings()
    {
        var selectedGroupId = SelectedGlobalGroup?.Id ?? _settings.SelectedQuickPanelGlobalGroupId;
        return _settings.QuickPanelGlobalGroups.FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase));
    }

    private QuickPanelGroupSettings? GetSelectedContextGroupSettings()
    {
        var selectedGroupId = SelectedContextGroup?.Id ?? _settings.SelectedQuickPanelContextGroupId;
        return GetVisibleContextGroups().FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase))
            ?? GetVisibleContextGroups().FirstOrDefault();
    }

    private void RestoreSlotCollections()
    {
        GlobalSlots.Clear();
        foreach (var slot in _allGlobalSlots)
        {
            GlobalSlots.Add(slot);
        }

        ContextSlots.Clear();
        foreach (var slot in _allContextSlots)
        {
            ContextSlots.Add(slot);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HidePanelIfAllowed();
        _mainWindow.OpenSettingsWindow("quickpanel");
    }

    private void MobileMessagesButton_Click(object sender, RoutedEventArgs e)
    {
        HidePanelIfAllowed();
        HasUnreadMessages = false;
        _mainWindow.ShowMobileInboxWindow();
    }

    private void AddGlobalGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SimpleTextInputWindow("新建分组", "输入新分组名称。", string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var group = new QuickPanelGroupSettings
        {
            Name = dialog.ValueText
        };
        _settings.QuickPanelGlobalGroups.Add(group);
        _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-add-global-group");
        LoadSlots();
    }

    private void AddContextGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SimpleTextInputWindow("新建专属分组", "输入新的专属分组名称。", string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var group = new QuickPanelGroupSettings
        {
            Name = dialog.ValueText,
            ContextProcessName = NormalizeProcessName(_foregroundAppContext?.ProcessName),
            ContextDisplayName = _foregroundAppContext?.ProcessName
        };
        _settings.QuickPanelContextGroups.Add(group);
        _settings.SelectedQuickPanelContextGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-add-context-group");
        LoadSlots();
    }

    private void GlobalGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickPanelGroupItem group })
        {
            return;
        }

        _isShowingGlobalFavorites = false;
        _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-select-global-group");
        OnPropertyChanged(nameof(PanelTitle));
        LoadSlots();
    }

    private void ContextGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickPanelGroupItem group })
        {
            return;
        }

        _isShowingContextFavorites = false;
        _settings.SelectedQuickPanelContextGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-select-context-group");
        LoadSlots();
    }

    private void RenameGlobalGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        var group = _settings.QuickPanelGlobalGroups.FirstOrDefault(item => string.Equals(item.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名分组", "输入新的分组名称。", group.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        group.Name = dialog.ValueText;
        SaveQuickPanelSettings("quickpanel-rename-global-group");
        LoadSlots();
    }

    private void RenameContextGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        var group = _settings.QuickPanelContextGroups.FirstOrDefault(item => string.Equals(item.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名专属分组", "输入新的分组名称。", group.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        group.Name = dialog.ValueText;
        SaveQuickPanelSettings("quickpanel-rename-context-group");
        LoadSlots();
    }

    private void DeleteGlobalGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        if (_settings.QuickPanelGlobalGroups.Count <= 1)
        {
            System.Windows.MessageBox.Show(this, "至少保留一个分组。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(this, $"确认删除分组“{groupItem.Name}”吗？", "删除分组", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.QuickPanelGlobalGroups.RemoveAll(group => string.Equals(group.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        _settings.SelectedQuickPanelGlobalGroupId = _settings.QuickPanelGlobalGroups[0].Id;
        SaveQuickPanelSettings("quickpanel-delete-global-group");
        LoadSlots();
    }

    private void DeleteContextGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        if (_settings.QuickPanelContextGroups.Count <= 1)
        {
            System.Windows.MessageBox.Show(this, "至少保留一个专属分组。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(this, $"确认删除分组“{groupItem.Name}”吗？", "删除分组", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.QuickPanelContextGroups.RemoveAll(group => string.Equals(group.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        _settings.SelectedQuickPanelContextGroupId = _settings.QuickPanelContextGroups[0].Id;
        SaveQuickPanelSettings("quickpanel-delete-context-group");
        LoadSlots();
    }

    private void PinAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        HostAssets.AppendLog($"Quick panel pin toggled: pinned={_isPinned}.");
        Activate();
        BringToFront();
    }

    private void SlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is SlotViewModel vm)
        {
            if (vm.IsFolder)
            {
                HideFolderPreview();
                ExpandFolder(vm, pinnedOpen: true);
                return;
            }

            if (IsEditMode)
            {
                return;
            }

            if (vm.Command != null)
            {
                _ = ExecuteSlotCommandAsync(vm, "quick-panel-click");
            }
            else if (!vm.IsContextual)
            {
                _suppressAutoHideUntilUtc = DateTime.UtcNow.AddSeconds(2);
                var newCommand = _mainWindow.OpenAddExtensionForSlot(this);
                if (newCommand != null)
                {
                    _mainWindow.MarkExtensionAsNewFromQuickPanel(newCommand);
                    AddCommandToSlot(vm, newCommand);
                    BringToFront();
                }
            }
            else
            {
                _suppressAutoHideUntilUtc = DateTime.UtcNow.AddSeconds(2);
                var newCommand = _mainWindow.OpenAddExtensionForSlot(this);
                if (newCommand != null)
                {
                    _mainWindow.MarkExtensionAsNewFromQuickPanel(newCommand);
                    AddCommandToSlot(vm, newCommand);
                    BringToFront();
                }
            }
        }
    }

    private void EditModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsEditMode = !IsEditMode;
        if (!IsEditMode)
        {
            StopFolderHoverTimer();
        }
    }

    private void FolderBackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToParentFolder();
    }

    private void ActiveFolderTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || _activeFolderReference == null)
        {
            return;
        }

        var folderItem = GetSlotItem(_activeFolderReference);
        if (folderItem?.IsFolder != true)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名分组", "输入新的分组名称。", folderItem.FolderName ?? ActiveFolderTitle)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        folderItem.FolderName = dialog.ValueText;
        ActiveFolderTitle = folderItem.FolderName;
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-rename-folder");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
        e.Handled = true;
    }

    private void FolderBackButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(SlotViewModel)) &&
            e.Data.GetData(typeof(SlotViewModel)) is SlotViewModel { ContainerPath.Count: > 0 })
        {
            NavigateToParentFolder();
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void SlotButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel vm })
        {
            return;
        }

        if (vm.IsFolder)
        {
            ShowFolderPreview(vm);
            ClearReleaseTarget();
            return;
        }

        if (vm.Command != null)
        {
            SetReleaseTarget(vm);
        }
    }

    private void SlotButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm })
        {
            if (ReferenceEquals(_hoveredSlot, vm))
            {
                ClearReleaseTarget();
            }

            if (ReferenceEquals(_previewFolderSlot, vm))
            {
                HideFolderPreview();
            }
        }
    }

    private void SlotButton_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel vm })
        {
            return;
        }

        if (vm.IsFolder)
        {
            ShowFolderPreview(vm);
            ClearReleaseTarget();
            return;
        }

        if (vm.Command != null)
        {
            SetReleaseTarget(vm);
        }
    }

    private void SlotButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel vm } || vm.Item == null)
        {
            _dragStartPoint = null;
            _dragSourceSlot = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragSourceSlot = vm;
    }

    private void SlotButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _dragStartPoint == null ||
            _dragSourceSlot?.Item == null)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StopFolderHoverTimer();
        HideFolderPreview();
        var payload = new System.Windows.DataObject();
        payload.SetData(typeof(SlotViewModel), _dragSourceSlot);

        WindowBindingDropOverlayWindow? bindingOverlay = null;
        DispatcherTimer? bindingOverlayTimer = null;
        var draggedCommand = _dragSourceSlot.Command;
        if (!_dragSourceSlot.IsFolder && draggedCommand != null)
        {
            payload.SetData(typeof(CommandItem), draggedCommand);
            bindingOverlay = new WindowBindingDropOverlayWindow(draggedCommand, _mainWindow.GetWindowBindingMarginPixels());
            bindingOverlay.BindingDropped += (hwnd, corner, offsetX, offsetY) =>
            {
                _ = _mainWindow.BindExtensionToWindowFromDropAsync(draggedCommand, hwnd, corner, offsetX, offsetY);
            };
            bindingOverlayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            bindingOverlayTimer.Tick += (_, _) =>
            {
                if (bindingOverlay == null || bindingOverlay.IsVisible || IsCursorInsideQuickPanel())
                {
                    return;
                }

                bindingOverlay.ShowFullDesktop();
            };
            bindingOverlayTimer.Start();
        }

        var sourceElement = (UIElement)sender;
        System.Windows.GiveFeedbackEventHandler feedbackHandler = (_, args) =>
        {
            args.UseDefaultCursors = false;
            System.Windows.Input.Mouse.SetCursor(System.Windows.Input.Cursors.Arrow);
            args.Handled = true;
        };
        sourceElement.GiveFeedback += feedbackHandler;

        try
        {
            IsDraggingSlot = true;
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            DragDrop.DoDragDrop((DependencyObject)sender, payload, System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy);
        }
        finally
        {
            IsDraggingSlot = false;
            sourceElement.GiveFeedback -= feedbackHandler;
            bindingOverlayTimer?.Stop();
            if (bindingOverlay?.IsVisible == true)
            {
                bindingOverlay.Close();
            }
        }
        _dragStartPoint = null;
        _dragSourceSlot = null;
        ClearReleaseTarget();
    }

    private bool IsCursorInsideQuickPanel()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var point = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
    }

    private ContextMenu? _currentContextMenu;

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            _currentContextMenu = menu;
            HostAssets.AppendLog("QuickPanel: ContextMenu opened.");
        }
    }

    private void QuickPanelWindow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        HostAssets.AppendLog("QuickPanel: ContextMenu opening.");
        try
        {
            Activate();
            Focus();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"QuickPanel ContextMenuOpening Activate error: {ex.Message}");
        }
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && _currentContextMenu == menu)
        {
            _currentContextMenu = null;
            _lastContextMenuClosedAt = DateTime.UtcNow;
            HostAssets.AppendLog("QuickPanel: ContextMenu closed.");
        }
    }

    private bool IsCursorInsideContextMenu()
    {
        if (_currentContextMenu == null || !_currentContextMenu.IsOpen)
        {
            return false;
        }
        try
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            var point = new NativeMethods.POINT { X = cursor.X, Y = cursor.Y };
            var hwndAtCursor = NativeMethods.WindowFromPoint(point);
            if (hwndAtCursor != IntPtr.Zero)
            {
                _ = NativeMethods.GetWindowThreadProcessId(hwndAtCursor, out var processId);
                var currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (processId == currentPid)
                {
                    var classBuilder = new StringBuilder(256);
                    if (NativeMethods.GetClassName(hwndAtCursor, classBuilder, classBuilder.Capacity) > 0)
                    {
                        var className = classBuilder.ToString();
                        if (className.StartsWith("HwndWrapper", StringComparison.OrdinalIgnoreCase))
                        {
                            HostAssets.AppendLog($"IsCursorInsideContextMenu: cursor is inside a WPF Popup Window ({className}) of this process.");
                            return true;
                        }
                    }
                }
            }

            // Check all WPF PopupRoots
            foreach (PresentationSource currentSource in PresentationSource.CurrentSources)
            {
                if (currentSource is System.Windows.Interop.HwndSource hSource)
                {
                    if (hSource.RootVisual != null && hSource.RootVisual.GetType().Name == "PopupRoot")
                    {
                        if (NativeMethods.GetWindowRect(hSource.Handle, out var r))
                        {
                            if (cursor.X >= r.left && cursor.X <= r.right &&
                                cursor.Y >= r.top && cursor.Y <= r.bottom)
                            {
                                HostAssets.AppendLog($"IsCursorInsideContextMenu: cursor is inside PopupRoot rect=({r.left},{r.top},{r.right},{r.bottom})");
                                return true;
                            }
                        }
                    }
                }
            }

            var source = PresentationSource.FromVisual(_currentContextMenu);
            if (source is System.Windows.Interop.HwndSource hwndSource)
            {
                var hwnd = hwndSource.Handle;
                if (NativeMethods.GetWindowRect(hwnd, out var rect))
                {
                    bool inside = cursor.X >= rect.left && cursor.X <= rect.right &&
                                  cursor.Y >= rect.top && cursor.Y <= rect.bottom;
                    HostAssets.AppendLog($"IsCursorInsideContextMenu: fallback rect=({rect.left},{rect.top},{rect.right},{rect.bottom}), cursor=({cursor.X},{cursor.Y}), inside={inside}");
                    return inside;
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"IsCursorInsideContextMenu error: {ex.Message}");
        }
        return false;
    }

    private void InputHookService_OnGlobalMouseDown()
    {
        if (!IsVisible)
        {
            return;
        }

        if (DateTimeOffset.UtcNow < _suspendOutsideClickHideUntilUtc)
        {
            return;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return;
        }

        if (_isDraggingSlot || IsCursorInsideQuickPanel() || IsCursorInsideContextMenu())
        {
            return;
        }

        HidePanelIfAllowed();
    }

    private void SlotButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel target })
        {
            e.Effects = System.Windows.DragDropEffects.None;
            StopFolderHoverTimer();
            return;
        }

        // Internal panel slot drag (highest priority)
        if (e.Data.GetDataPresent(typeof(QuickPanelFolderChildDragPayload)))
        {
            var source = e.Data.GetData(typeof(QuickPanelFolderChildDragPayload)) as QuickPanelFolderChildDragPayload;
            if (source == null)
            {
                e.Effects = System.Windows.DragDropEffects.None;
                return;
            }

            e.Effects = System.Windows.DragDropEffects.Move;
            _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(350);
            SetReleaseTarget(target);
            StopFolderHoverTimer();
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(SlotViewModel)))
        {
            var source = e.Data.GetData(typeof(SlotViewModel)) as SlotViewModel;
            if (source == null || ReferenceEquals(source, target))
            {
                e.Effects = System.Windows.DragDropEffects.None;
                StopFolderHoverTimer();
                return;
            }

            e.Effects = System.Windows.DragDropEffects.Move;
            _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(350);
            SetReleaseTarget(target);
            StopFolderHoverTimer();

            e.Handled = true;
            return;
        }

        // External command drop (from main window)
        if (e.Data.GetDataPresent(typeof(CommandItem)))
        {
            var command = e.Data.GetData(typeof(CommandItem)) as CommandItem;
            if (command == null || (target.Item != null && !target.IsFolder))
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            else
            {
                _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(350);
                SetReleaseTarget(target);
                e.Effects = System.Windows.DragDropEffects.Copy;
            }

            StopFolderHoverTimer();
            e.Handled = true;
            return;
        }

        if (TryGetDroppedFilePaths(e, out _))
        {
            if (target.Item != null)
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            else
            {
                _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(350);
                SetReleaseTarget(target);
                e.Effects = System.Windows.DragDropEffects.Copy;
            }

            StopFolderHoverTimer();
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.None;
        StopFolderHoverTimer();

        e.Handled = true;
    }

    private void SlotButton_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        StopFolderHoverTimer();
    }

    private void SlotButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel target })
        {
            return;
        }

        // Check for internal panel slot drag first (has both SlotViewModel and CommandItem)
        if (e.Data.GetDataPresent(typeof(QuickPanelFolderChildDragPayload)))
        {
            var source = e.Data.GetData(typeof(QuickPanelFolderChildDragPayload)) as QuickPanelFolderChildDragPayload;
            if (source != null)
            {
                StopFolderHoverTimer();
                MoveFolderChildToSlot(source, target);
                e.Handled = true;
                return;
            }
        }

        if (e.Data.GetDataPresent(typeof(SlotViewModel)))
        {
            var source = e.Data.GetData(typeof(SlotViewModel)) as SlotViewModel;
            if (source != null && !ReferenceEquals(source, target))
            {
                StopFolderHoverTimer();
                if (IsControlKeyDown(e) && !source.IsFolder && source.Command != null)
                {
                    if (target.IsFolder)
                    {
                        AddSlotToFolder(source, target);
                    }
                    else if (target.Command != null)
                    {
                        CreateFolderFromSlots(source, target);
                    }
                    else
                    {
                        MoveOrSwapSlot(source, target);
                    }
                }
                else
                {
                    MoveOrSwapSlot(source, target);
                }

                e.Handled = true;
                return;
            }
        }

        if (e.Data.GetDataPresent(typeof(CommandItem)) && !e.Data.GetDataPresent(typeof(SlotViewModel)))
        {
            var command = e.Data.GetData(typeof(CommandItem)) as CommandItem;
            if (command != null && target.IsFolder)
            {
                AddCommandToFolder(target, command);
            }
            else if (command != null && target.Item == null)
            {
                AddCommandToSlot(target, command);
            }

            StopFolderHoverTimer();
            ClearReleaseTarget();
            e.Handled = true;
            return;
        }

        if (TryGetDroppedFilePaths(e, out var filePaths))
        {
            if (target.Item == null)
            {
                AddDroppedPathsToSlot(target, filePaths);
            }

            StopFolderHoverTimer();
            ClearReleaseTarget();
            e.Handled = true;
            return;
        }
        e.Handled = true;
    }

    private static bool IsControlKeyDown(System.Windows.DragEventArgs e) =>
        (e.KeyStates & System.Windows.DragDropKeyStates.ControlKey) == System.Windows.DragDropKeyStates.ControlKey;

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

    private void AddDroppedPathsToSlot(SlotViewModel target, IEnumerable<string> filePaths)
    {
        var firstPath = filePaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstPath))
        {
            return;
        }

        try
        {
            var newCommand = _mainWindow.CreateQuickOpenExtensionFromPath(firstPath);
            _mainWindow.MarkExtensionAsNewFromQuickPanel(newCommand);
            target.SetCommand(newCommand, false, target.IsContextual);
            SaveSlots(target.IsContextual);
            LoadSlots();
            BringToFront();
            _mainWindow.LastRunMessage = $"已拖拽创建扩展并放入槽位：{newCommand.Title}";
        }
        catch (Exception ex)
        {
            _mainWindow.SyncStatus = $"拖拽创建扩展失败：{Path.GetFileName(firstPath)}，{ex.Message}";
        }
    }

    public void ExecuteHoveredSlotFromHoldRelease()
    {
        if (!IsVisible)
        {
            HostAssets.AppendLog("Quick panel hold release: panel is not visible.");
            return;
        }

        var slot = _hoveredSlot ?? ResolveSlotUnderCursor();
        if (slot?.Command == null)
        {
            HostAssets.AppendLog("Quick panel hold release: no occupied slot under cursor.");
            return;
        }

        HostAssets.AppendLog($"Quick panel hold release: executing slot {slot.Index}, extension={slot.Command.ExtensionId}.");
        _ = ExecuteSlotCommandAsync(slot, "quick-panel-hold-release");
    }

    private void SetReleaseTarget(SlotViewModel? slot)
    {
        if (ReferenceEquals(_hoveredSlot, slot))
        {
            return;
        }

        if (_hoveredSlot != null)
        {
            _hoveredSlot.IsReleaseTarget = false;
        }

        _hoveredSlot = slot;
        if (_hoveredSlot != null)
        {
            _hoveredSlot.IsReleaseTarget = true;
        }
    }

    private void ClearReleaseTarget()
    {
        if (_hoveredSlot != null)
        {
            _hoveredSlot.IsReleaseTarget = false;
        }

        _hoveredSlot = null;
    }

    private async Task ExecuteSlotCommandAsync(SlotViewModel vm, string launchSource)
    {
        if (_isExecutingSlot || vm.Command == null)
        {
            return;
        }

        _isExecutingSlot = true;
        try
        {
            var command = vm.Command;
            HostAssets.AppendLog($"Quick panel execute: source={launchSource}, slot={vm.Index}, extension={command.ExtensionId}.");
            _releaseTargetTimer.Stop();
            if (TryExtractGeneratedPasteText(command, out var pasteText))
            {
                await ExecuteGeneratedPasteAsync(command, pasteText, launchSource);
                return;
            }

            HidePanelIfAllowed();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (_previousForegroundWindow != IntPtr.Zero)
            {
                var restored = NativeMethods.SetForegroundWindow(_previousForegroundWindow);
                HostAssets.AppendLog($"Quick panel execute: restored previous foreground={restored}, {DescribeWindow(_previousForegroundWindow)}.");
                RestorePreviousFocus("execute");
            }

            var input = string.Empty;
            if (command.ShouldCaptureSelectedInput)
            {
                await Task.Delay(120);
                input = await SelectionCaptureService.CaptureSelectedInputAsync();
                HostAssets.AppendLog($"Quick panel execute: captured input length={input.Length}.");
            }
            else
            {
                HostAssets.AppendLog("Quick panel execute: selection capture skipped for command without context input.");
            }

            _mainWindow.ExecuteCommandExternally(command, input, launchSource);
        }
        finally
        {
            _isExecutingSlot = false;
            ClearReleaseTarget();
            if (IsVisible)
            {
                _releaseTargetTimer.Start();
            }
        }
    }

    private async Task ExecuteGeneratedPasteAsync(CommandItem command, string text, string launchSource)
    {
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            var clipboardStopwatch = Stopwatch.StartNew();
            ClipboardService.SetText(text);
            clipboardStopwatch.Stop();

            var restoreStopwatch = Stopwatch.StartNew();
            HidePanelIfAllowed();
            var restoredForeground = false;
            if (_previousForegroundWindow != IntPtr.Zero)
            {
                restoredForeground = NativeMethods.SetForegroundWindow(_previousForegroundWindow);
            }

            var focusRestored = RestorePreviousFocus("paste");
            restoreStopwatch.Stop();

            var settleDelayMs = string.Equals(launchSource, "quick-panel-hold-release", StringComparison.OrdinalIgnoreCase)
                ? 35
                : 20;
            await Task.Delay(settleDelayMs);

            var sendStopwatch = Stopwatch.StartNew();
            var sent = NativeMethods.SendCtrlV(out var inputCount, out var lastError);
            sendStopwatch.Stop();

            _mainWindow.LastRunMessage = $"已粘贴：{command.Title}";
            _mainWindow.SyncStatus = "已粘贴。";
            HostAssets.AppendRecent(command.Title);
            HostAssets.AppendLog(
                $"Quick panel paste: id={command.ExtensionId}, title={command.Title}, source={launchSource}, textLength={text.Length}, SendInput sent={sent}/{inputCount}, lastError={lastError}, elapsedMs={totalStopwatch.ElapsedMilliseconds}, clipboardMs={clipboardStopwatch.ElapsedMilliseconds}, restoreMs={restoreStopwatch.ElapsedMilliseconds}, settleMs={settleDelayMs}, sendMs={sendStopwatch.ElapsedMilliseconds}, foregroundRestored={restoredForeground}, focusRestored={focusRestored}.");
        }
        catch (Exception ex)
        {
            _mainWindow.LastRunMessage = $"粘贴失败：{command.Title}";
            _mainWindow.SyncStatus = $"粘贴失败：{ex.Message}";
            HostAssets.AppendLog($"Quick panel paste failed: id={command.ExtensionId}, title={command.Title}, elapsedMs={totalStopwatch.ElapsedMilliseconds}, error={ex}");
        }
    }

    private bool RestorePreviousFocus(string stage)
    {
        if (_previousFocusWindow == IntPtr.Zero)
        {
            return false;
        }

        if (_previousFocusWindow == _previousForegroundWindow)
        {
            HostAssets.AppendLog(
                $"Quick panel focus restore: stage={stage}, skipped=top-level-focus, focus={DescribeWindow(_previousFocusWindow)}.");
            return false;
        }

        var restored = NativeMethods.TryRestoreFocus(_previousForegroundWindow, _previousFocusWindow, out var detail);
        HostAssets.AppendLog(
            $"Quick panel focus restore: stage={stage}, restored={restored}, focus={DescribeWindow(_previousFocusWindow)}, detail={detail}.");
        return restored;
    }

    private static bool TryExtractGeneratedPasteText(CommandItem command, out string text)
    {
        text = string.Empty;
        var script = command.InlineScriptSource;
        if (string.IsNullOrWhiteSpace(script) ||
            !string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase) ||
            !script.Contains("FromBase64String", StringComparison.OrdinalIgnoreCase) ||
            !(script.Contains("Set-Clipboard", StringComparison.OrdinalIgnoreCase) ||
              script.Contains("Clipboard.SetText", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var match = Regex.Match(
            script,
            @"FromBase64String\((['""])(?<payload>[A-Za-z0-9+/=]+)\1\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["payload"].Value));
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private int CountExtensionReferences(string? extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return 0;
        }

        var count = 0;
        foreach (var group in _settings.QuickPanelGlobalGroups.Concat(_settings.QuickPanelContextGroups))
        {
            group.SlotItems ??= [];
            foreach (var item in group.SlotItems)
            {
                count += CountReferencesInItem(item, extensionId);
            }
        }
        return count;
    }

    private int CountReferencesInItem(QuickPanelSlotItem? item, string extensionId)
    {
        if (item == null)
        {
            return 0;
        }

        var count = 0;
        if (!item.IsFolder)
        {
            if (string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        else
        {
            item.FolderSlotItems ??= [];
            foreach (var child in item.FolderSlotItems)
            {
                count += CountReferencesInItem(child, extensionId);
            }
        }
        return count;
    }

    private void RunningExtensionRegistry_Changed(object? sender, System.EventArgs e)
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            foreach (var slot in _allGlobalSlots)
            {
                slot.RefreshRunningState();
            }
            foreach (var slot in _allContextSlots)
            {
                slot.RefreshRunningState();
            }
            if (ActiveFolderSlots != null)
            {
                foreach (var slot in ActiveFolderSlots)
                {
                    slot.RefreshRunningState();
                }
            }
        }));
    }

    private void TerminateExtension_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        HostAssets.AppendLog("QuickPanel TerminateExtension_Click called.");
        if (sender is MenuItem mi)
        {
            if (mi.CommandParameter is SlotViewModel vm)
            {
                HostAssets.AppendLog($"QuickPanel TerminateExtension_Click: vm={vm.Title}, IsFolder={vm.IsFolder}, CommandNull={vm.Command == null}");
                if (vm.IsFolder || vm.Command == null || string.IsNullOrEmpty(vm.Command.ExtensionId))
                {
                    HostAssets.AppendLog("QuickPanel TerminateExtension_Click: ignored because folder or command extensionId is empty.");
                    return;
                }

                var extensionId = vm.Command.ExtensionId;
                var runningInstances = RunningExtensionRegistry.GetSnapshot()
                    .Where(x => string.Equals(x.ExtensionId, extensionId, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();

                HostAssets.AppendLog($"QuickPanel TerminateExtension_Click: runningInstances count={runningInstances.Count}");
                if (runningInstances.Count == 0)
                {
                    System.Windows.MessageBox.Show(this, "该扩展当前没有在运行。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                var failedMessages = new System.Collections.Generic.List<string>();

                foreach (var instance in runningInstances)
                {
                    HostAssets.AppendLog($"QuickPanel TerminateExtension_Click: attempting to terminate instance={instance.InstanceId}");
                    if (!RunningExtensionRegistry.TryTerminate(instance.InstanceId, out var msg))
                    {
                        failedMessages.Add(msg);
                        HostAssets.AppendLog($"QuickPanel TerminateExtension_Click: terminate failed: {msg}");
                    }
                    else
                    {
                        HostAssets.AppendLog($"QuickPanel TerminateExtension_Click: terminate success: {msg}");
                    }
                }

                if (failedMessages.Count > 0)
                {
                    var errorMsg = string.Join("\n", failedMessages);
                    System.Windows.MessageBox.Show(this, $"结束部分实例失败：\n{errorMsg}", "停止运行失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            else
            {
                HostAssets.AppendLog($"QuickPanel TerminateExtension_Click: mi.CommandParameter is not SlotViewModel, but {mi.CommandParameter?.GetType().FullName}");
            }
        }
        else
        {
            HostAssets.AppendLog("QuickPanel TerminateExtension_Click: sender is not MenuItem.");
        }
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm)
        {
            if (!vm.IsFolder && (vm.IsShortcut || CountExtensionReferences(vm.Command?.ExtensionId) > 1))
            {
                var refShort = BuildSlotReference(vm);
                var contShort = refShort == null ? null : GetSlotContainer(refShort);
                if (contShort != null && refShort!.Index >= 0 && refShort.Index < contShort.Count)
                {
                    contShort[refShort.Index] = null;
                    RefreshAllLegacySlots();
                    SaveQuickPanelSettings("quickpanel-remove-slot-shortcut");
                    LoadSlots();
                    RefreshActiveFolderAfterMutation();
                }
                return;
            }

            if (!vm.IsFolder && vm.Command?.Source == CommandSource.LocalExtension)
            {
                var result = await _mainWindow.DeleteExtensionFromQuickPanelAsync(vm.Command.ExtensionId, this);
                if (!result.ok && !string.IsNullOrWhiteSpace(result.message))
                {
                    System.Windows.MessageBox.Show(this, result.message, "删除扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                LoadSlots();
                return;
            }

            var reference = BuildSlotReference(vm);
            var container = reference == null ? null : GetSlotContainer(reference);
            if (container != null && reference!.Index >= 0 && reference.Index < container.Count)
            {
                container[reference.Index] = null;
                RefreshAllLegacySlots();
                SaveQuickPanelSettings("quickpanel-remove-slot");
                LoadSlots();
                RefreshActiveFolderAfterMutation();
            }
        }
    }

    private void CopySlotExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm })
        {
            return;
        }

        _mainWindow.SetQuickPanelClipboard(vm.Command!, isCut: false, BuildSlotReference(vm));
    }

    private void CutSlotExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm })
        {
            return;
        }

        _mainWindow.SetQuickPanelClipboard(vm.Command!, isCut: true, BuildSlotReference(vm));
    }

    private void PasteSlotExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel vm })
        {
            return;
        }

        var clipboard = _mainWindow.GetQuickPanelClipboard();
        if (clipboard == null)
        {
            if (!_mainWindow.TryImportExtensionFromSystemClipboard(out var importedCommand, out var importMessage) ||
                importedCommand == null)
            {
                _mainWindow.SyncStatus = string.IsNullOrWhiteSpace(importMessage)
                    ? "扩展剪贴板为空。先复制扩展，或把扩展 JSON 放进系统剪贴板后再粘贴。"
                    : importMessage;
                return;
            }

            clipboard = new QuickPanelClipboardItem(importedCommand.ExtensionId, importedCommand.Title, false, null);
        }

        if (!TryPasteClipboardIntoSlot(vm, clipboard, out var message))
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _mainWindow.SyncStatus = message;
            }
            return;
        }

        _mainWindow.LastRunMessage = message;
    }

    private void SlotContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        _currentContextMenu = menu;

        var clipboard = _mainWindow.GetQuickPanelClipboard();
        
        MenuItem? pasteNormal = menu.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.Tag?.ToString() == "PasteNormal");
        MenuItem? pasteShortcut = menu.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.Tag?.ToString() == "PasteShortcut");
        MenuItem? pasteCopy = menu.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.Tag?.ToString() == "PasteCopy");

        if (pasteNormal == null) return;

        if (clipboard == null)
        {
            pasteNormal.Visibility = Visibility.Visible;
            pasteNormal.Header = "粘贴扩展";
            if (pasteShortcut != null) pasteShortcut.Visibility = Visibility.Collapsed;
            if (pasteCopy != null) pasteCopy.Visibility = Visibility.Collapsed;
        }
        else if (clipboard.IsCut)
        {
            pasteNormal.Visibility = Visibility.Visible;
            pasteNormal.Header = "移动到此处";
            if (pasteShortcut != null) pasteShortcut.Visibility = Visibility.Collapsed;
            if (pasteCopy != null) pasteCopy.Visibility = Visibility.Collapsed;
        }
        else
        {
            pasteNormal.Visibility = Visibility.Collapsed;
            if (pasteShortcut != null) pasteShortcut.Visibility = Visibility.Visible;
            if (pasteCopy != null) pasteCopy.Visibility = Visibility.Visible;
        }
    }

    private void PasteShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel vm })
        {
            return;
        }

        var clipboard = _mainWindow.GetQuickPanelClipboard();
        if (clipboard == null || clipboard.IsCut)
        {
            return;
        }

        if (!TryPasteShortcutIntoSlot(vm, clipboard, out var message))
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _mainWindow.SyncStatus = message;
            }
            return;
        }

        _mainWindow.LastRunMessage = message;
    }

    private bool TryPasteShortcutIntoSlot(SlotViewModel targetSlot, QuickPanelClipboardItem clipboard, out string message)
    {
        var command = _mainWindow.GetAllCommands()
            .FirstOrDefault(item => string.Equals(item.ExtensionId, clipboard.ExtensionId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            message = $"找不到扩展：{clipboard.Title}";
            _mainWindow.ClearQuickPanelClipboard();
            return false;
        }

        var targetReference = BuildSlotReference(targetSlot);
        var targetContainer = targetReference == null ? null : GetSlotContainer(targetReference);
        if (targetReference == null || targetContainer == null)
        {
            message = "当前鼠标面板分组不可用。";
            return false;
        }

        targetContainer[targetReference.Index] = new QuickPanelSlotItem
        {
            ExtensionId = clipboard.ExtensionId,
            IsShortcut = true
        };
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-paste-shortcut-slot");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
        _mainWindow.ClearQuickPanelClipboard();
        message = targetSlot.Item == null
            ? $"已将扩展粘贴为快捷方式到第 {targetSlot.Index + 1} 个槽位：{clipboard.Title}"
            : $"已替换第 {targetSlot.Index + 1} 个槽位为快捷方式：{clipboard.Title}";
        return true;
    }

    private async void PasteCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel targetSlot })
        {
            return;
        }

        var clipboard = _mainWindow.GetQuickPanelClipboard();
        if (clipboard == null || clipboard.IsCut)
        {
            return;
        }

        var parentCommand = _mainWindow.GetAllCommands()
            .FirstOrDefault(item => string.Equals(item.ExtensionId, clipboard.ExtensionId, StringComparison.OrdinalIgnoreCase));
        if (parentCommand == null)
        {
            _mainWindow.SyncStatus = $"找不到母扩展：{clipboard.Title}";
            return;
        }

        if (string.IsNullOrWhiteSpace(parentCommand.ExtensionDirectoryPath) || !Directory.Exists(parentCommand.ExtensionDirectoryPath))
        {
            _mainWindow.SyncStatus = "母扩展目录不存在，无法克隆副本。";
            return;
        }

        try
        {
            _mainWindow.SyncStatus = "正在创建并注册副本...";
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var originalId = parentCommand.ExtensionId;
            var newId = $"{originalId}_copy_{timestamp}";
            var catalogRoot = LocalExtensionCatalog.CatalogRootPath;
            var newDir = Path.Combine(catalogRoot, newId);

            await Task.Run(() => CopyDirectory(parentCommand.ExtensionDirectoryPath, newDir));

            var manifestPath = Path.Combine(newDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                _mainWindow.SyncStatus = "复制出的文件夹中找不到 manifest.json。";
                return;
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(json, JsonOptions);
            if (manifest == null)
            {
                _mainWindow.SyncStatus = "解析 manifest.json 失败。";
                return;
            }

            manifest = manifest with
            {
                Id = newId,
                Name = $"{manifest.Name ?? "未命名"} (副本)",
                Startup = null,
                GlobalShortcut = null,
                HotkeyBehavior = null
            };

            var newJson = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(manifestPath, newJson);

            var newCommand = _mainWindow.PersistJsonExtensionFromDialog(newJson, isEditMode: false);
            if (newCommand == null)
            {
                _mainWindow.SyncStatus = "注册新扩展失败。";
                return;
            }

            var targetReference = BuildSlotReference(targetSlot);
            var targetContainer = targetReference == null ? null : GetSlotContainer(targetReference);
            if (targetReference == null || targetContainer == null)
            {
                _mainWindow.SyncStatus = "当前槽位位置不可用。";
                return;
            }

            targetContainer[targetReference.Index] = new QuickPanelSlotItem
            {
                ExtensionId = newId,
                IsShortcut = false
            };

            RefreshAllLegacySlots();
            SaveQuickPanelSettings("quickpanel-paste-copy-slot");
            LoadSlots();
            RefreshActiveFolderAfterMutation();
            _mainWindow.ClearQuickPanelClipboard();

            _mainWindow.SyncStatus = $"已成功创建并粘贴扩展副本：{newCommand.Title}";
            _mainWindow.LastRunMessage = $"已添加副本到第 {targetSlot.Index + 1} 个槽位。";
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"PasteCopy_Click error: {ex}");
            _mainWindow.SyncStatus = $"克隆扩展副本失败：{ex.Message}";
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    private async void EditExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm } ||
            !vm.CanEdit)
        {
            return;
        }

        var result = await _mainWindow.EditExtensionFromQuickPanelAsync(vm.Command!.ExtensionId, this);
        if (!result.ok && !string.IsNullOrWhiteSpace(result.message))
        {
            System.Windows.MessageBox.Show(this, result.message, "编辑扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadSlots();
    }

    private void OpenExtensionDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm } ||
            !vm.CanOpenDirectory)
        {
            return;
        }

        if (!_mainWindow.TryOpenExtensionDirectory(vm.Command!.ExtensionId, out var message) &&
            !string.IsNullOrWhiteSpace(message))
        {
            System.Windows.MessageBox.Show(this, message, "打开目录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PublishExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm } ||
            !vm.CanPublish)
        {
            return;
        }

        var result = await _mainWindow.PublishExtensionFromSettingsAsync(vm.Command!.ExtensionId);
        System.Windows.MessageBox.Show(
            this,
            result.message,
            result.ok ? "发布到商店" : "发布到商店失败",
            MessageBoxButton.OK,
            result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void CopyStoreLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm })
        {
            return;
        }

        try
        {
            var result = _mainWindow.CopyExtensionStoreLink(vm.Command!.ExtensionId);
            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "复制商店链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "复制商店链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenStoreLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm })
        {
            return;
        }

        try
        {
            var result = _mainWindow.OpenExtensionStoreLink(vm.Command!.ExtensionId);
            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "打开商店链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "打开商店链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm && vm.Command != null)
        {
            var id = vm.Command.ExtensionId;
            var favorites = vm.IsContextual ? _settings.ContextFavoriteExtensionIds : _settings.GlobalFavoriteExtensionIds;
            if (favorites.Contains(id))
                favorites.Remove(id);
            else
                favorites.Add(id);

            SaveQuickPanelSettings(vm.IsContextual ? "quickpanel-toggle-context-favorite" : "quickpanel-toggle-global-favorite");
            vm.SetFavorite(favorites.Contains(id));
        }
    }

    private void ToggleGlobalFavorites_Click(object sender, RoutedEventArgs e)
    {
        _isShowingGlobalFavorites = !_isShowingGlobalFavorites;
        OnPropertyChanged(nameof(PanelTitle));
        OnPropertyChanged(nameof(EditModeHintText));
        LoadSlots();
    }

    private void ToggleContextFavorites_Click(object sender, RoutedEventArgs e)
    {
        _isShowingContextFavorites = !_isShowingContextFavorites;
        LoadSlots();
    }

    private void GlobalPanel_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isShowingGlobalFavorites || GlobalGroups.Count <= 1)
        {
            return;
        }

        CycleGroups(GlobalGroups, SelectedGlobalGroup, e.Delta, isContextual: false);
        e.Handled = true;
    }

    private void ContextPanel_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isShowingContextFavorites || ContextGroups.Count <= 1)
        {
            return;
        }

        CycleGroups(ContextGroups, SelectedContextGroup, e.Delta, isContextual: true);
        e.Handled = true;
    }

    private void CycleGroups(IReadOnlyList<QuickPanelGroupItem> groups, QuickPanelGroupItem? selectedGroup, int delta, bool isContextual)
    {
        if (groups.Count == 0)
        {
            return;
        }

        var currentIndex = selectedGroup == null
            ? 0
            : groups.ToList().FindIndex(group => string.Equals(group.Id, selectedGroup.Id, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var direction = delta < 0 ? 1 : -1;
        var nextIndex = (currentIndex + direction + groups.Count) % groups.Count;
        var nextGroup = groups[nextIndex];
        if (isContextual)
        {
            _settings.SelectedQuickPanelContextGroupId = nextGroup.Id;
        }
        else
        {
            _settings.SelectedQuickPanelGlobalGroupId = nextGroup.Id;
            OnPropertyChanged(nameof(PanelTitle));
        }

        SaveQuickPanelSettings(isContextual ? "quickpanel-cycle-context-group" : "quickpanel-cycle-global-group");
        LoadSlots();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (DateTime.UtcNow <= _suppressAutoHideUntilUtc)
        {
            return;
        }

        if (_currentContextMenu != null)
        {
            HostAssets.AppendLog("Window_Deactivated: ignored because ContextMenu is open.");
            return;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return;
        }

        _releaseTargetTimer.Stop();
        HidePanelIfAllowed();
    }

    public void ShowAtMouse()
    {
        try
        {
            HostAssets.AppendLog("Quick panel show requested.");
            _previousForegroundWindow = NativeMethods.GetForegroundWindow();
            _previousFocusWindow = NativeMethods.GetForegroundFocusWindow();
            _foregroundAppContext = BuildForegroundAppContext(_previousForegroundWindow);
            var cursorPixels = NativeMethods.GetCursorPosition();
            var cursorDips = DeviceToDips(cursorPixels);

            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)cursorPixels.X, (int)cursorPixels.Y));
            var screenBounds = DeviceRectToDips(screen.Bounds);
            var safeAnchorY = Height / 2;
            var requestedTop = cursorDips.Y - safeAnchorY;
            var topConstrained = requestedTop <= screenBounds.Top;
            Left = CalculateMousePanelLeft(cursorDips.X, screenBounds, topConstrained);
            Top = requestedTop;
            if (Top < screenBounds.Top) Top = screenBounds.Top;
            if (Top + Height > screenBounds.Bottom) Top = screenBounds.Bottom - Height;

            HubSearchBox.Text = string.Empty; // Reset search on show
            _hoveredSlot = null;
            LoadSlots(); // Refresh
            var occupiedGlobal = GlobalSlots.Count(slot => slot.IsOccupied);
            var occupiedContext = ContextSlots.Count(slot => slot.IsOccupied);
            HostAssets.AppendLog($"Quick panel showing at ({Left:0},{Top:0}), cursorPixels=({cursorPixels.X:0},{cursorPixels.Y:0}), cursorDips=({cursorDips.X:0},{cursorDips.Y:0}), cursorLocalX={cursorDips.X - Left:0}, topConstrained={topConstrained}, screenDips=({screenBounds.Left:0},{screenBounds.Top:0},{screenBounds.Right:0},{screenBounds.Bottom:0}), occupiedGlobal={occupiedGlobal}, occupiedContext={occupiedContext}, totalGlobal={GlobalSlots.Count}, totalContext={ContextSlots.Count}, previousFocus={DescribeWindow(_previousFocusWindow)}.");
            OnPropertyChanged(nameof(ContextSectionTitle));
            OnPropertyChanged(nameof(ContextHintText));
            Topmost = true;
            Show();
            NativeMethods.ShowWithoutActivation(new WindowInteropHelper(this).Handle);
            _suspendOutsideClickHideUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(250);
            _releaseTargetTimer.Start();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Quick panel show failed: {ex}");
        }
    }

    private double CalculateMousePanelLeft(double cursorXDips, Rect screenBounds, bool topConstrained)
    {
        var defaultLeft = Clamp(cursorXDips - Width / 2, screenBounds.Left, screenBounds.Right - Width);
        if (!topConstrained)
        {
            return defaultLeft;
        }

        var slotGridWidth = Math.Max(0, Width - SidebarWidthDips - SlotGridHorizontalMarginDips * 2);
        var cellWidth = slotGridWidth / QuickPanelColumnCount;
        var slotGridLeft = SidebarWidthDips + SlotGridHorizontalMarginDips;
        var targetLocalXs = new[]
        {
            Width / 2,
            slotGridLeft + cellWidth * 2,
            slotGridLeft + cellWidth,
            slotGridLeft + cellWidth * 3,
            SidebarWidthDips / 2,
            Width - 12
        };

        return targetLocalXs
            .Select(targetLocalX =>
            {
                var left = Clamp(cursorXDips - targetLocalX, screenBounds.Left, screenBounds.Right - Width);
                var actualLocalX = cursorXDips - left;
                var isOverIcon = IsOverTopRowIcon(actualLocalX, slotGridLeft, cellWidth);
                var score = (isOverIcon ? 100000 : 0) + Math.Abs(left - defaultLeft);
                return new { Left = left, Score = score };
            })
            .OrderBy(static candidate => candidate.Score)
            .First()
            .Left;
    }

    private static bool IsOverTopRowIcon(double localX, double slotGridLeft, double cellWidth)
    {
        for (var column = 0; column < QuickPanelColumnCount; column++)
        {
            var iconLeft = slotGridLeft + column * cellWidth + (cellWidth - SlotIconWidthDips) / 2 - CursorIconSafetyDips;
            var iconRight = iconLeft + SlotIconWidthDips + CursorIconSafetyDips * 2;
            if (localX >= iconLeft && localX <= iconRight)
            {
                return true;
            }
        }

        return false;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    public void ReloadSlots()
    {
        LoadSlots();
    }

    public void RefreshSettingsFromStore()
    {
        _settings = AppSettingsStore.Load();
        LoadSlots();
    }

    private IReadOnlyList<QuickPanelGroupSettings> GetVisibleContextGroups()
    {
        var normalizedProcessName = NormalizeProcessName(_foregroundAppContext?.ProcessName);
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return [];
        }

        return _settings.QuickPanelContextGroups
            .Where(group => string.Equals(NormalizeProcessName(group.ContextProcessName), normalizedProcessName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private QuickPanelGroupSettings? EnsureContextGroupForCurrentApp()
    {
        var current = GetSelectedContextGroupSettings();
        if (current != null)
        {
            return current;
        }

        var normalizedProcessName = NormalizeProcessName(_foregroundAppContext?.ProcessName);
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return null;
        }

        var existingUnbound = _settings.QuickPanelContextGroups.FirstOrDefault(group =>
            string.IsNullOrWhiteSpace(group.ContextProcessName) &&
            group.SlotItems.Any(static slot => slot != null));
        if (existingUnbound != null)
        {
            existingUnbound.ContextProcessName = normalizedProcessName;
            existingUnbound.ContextDisplayName = _foregroundAppContext?.ProcessName;
            _settings.SelectedQuickPanelContextGroupId = existingUnbound.Id;
            SaveQuickPanelSettings("quickpanel-bind-existing-context-group");
            LoadGroups();
            return existingUnbound;
        }

        var autoGroup = new QuickPanelGroupSettings
        {
            Name = _foregroundAppContext?.ProcessName ?? "专属",
            ContextProcessName = normalizedProcessName,
            ContextDisplayName = _foregroundAppContext?.ProcessName
        };
        _settings.QuickPanelContextGroups.Add(autoGroup);
        _settings.SelectedQuickPanelContextGroupId = autoGroup.Id;
        HostAssets.AppendLog(
            $"Quick panel context group auto-created: id={autoGroup.Id}, process={autoGroup.ContextProcessName}, display={autoGroup.ContextDisplayName}.");
        SaveQuickPanelSettings("quickpanel-auto-create-context-group");
        LoadGroups();
        return autoGroup;
    }

    private static string NormalizeProcessName(string? processName)
    {
        return string.IsNullOrWhiteSpace(processName)
            ? string.Empty
            : processName.Trim().ToLowerInvariant();
    }

    private QuickPanelSlotReference? BuildSlotReference(SlotViewModel vm)
    {
        if (!string.IsNullOrWhiteSpace(vm.SourceGroupId))
        {
            return new QuickPanelSlotReference(vm.IsContextual, vm.SourceGroupId!, vm.Index, vm.ContainerPath.ToList());
        }

        var group = vm.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        return group == null ? null : new QuickPanelSlotReference(vm.IsContextual, group.Id, vm.Index, []);
    }

    private bool TryPasteClipboardIntoSlot(SlotViewModel targetSlot, QuickPanelClipboardItem clipboard, out string message)
    {
        var command = _mainWindow.GetAllCommands()
            .FirstOrDefault(item => string.Equals(item.ExtensionId, clipboard.ExtensionId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            message = $"找不到扩展：{clipboard.Title}";
            _mainWindow.ClearQuickPanelClipboard();
            return false;
        }

        var targetReference = BuildSlotReference(targetSlot);
        var targetContainer = targetReference == null ? null : GetSlotContainer(targetReference);
        if (targetReference == null || targetContainer == null)
        {
            message = "当前鼠标面板分组不可用。";
            return false;
        }

        if (clipboard.IsCut && clipboard.SourceSlot != null)
        {
            var sourceContainer = GetSlotContainer(clipboard.SourceSlot);
            if (sourceContainer != null)
            {
                if (clipboard.SourceSlot.Index == targetReference.Index &&
                    clipboard.SourceSlot.IsContextual == targetReference.IsContextual &&
                    string.Equals(clipboard.SourceSlot.GroupId, targetReference.GroupId, StringComparison.OrdinalIgnoreCase) &&
                    clipboard.SourceSlot.ContainerPath.SequenceEqual(targetReference.ContainerPath))
                {
                    message = $"扩展已在当前位置：{clipboard.Title}";
                    _mainWindow.ClearQuickPanelClipboard();
                    return true;
                }

                var sourceItem = clipboard.SourceSlot.Index >= 0 && clipboard.SourceSlot.Index < sourceContainer.Count
                    ? sourceContainer[clipboard.SourceSlot.Index]
                    : null;
                var sourceIsShortcut = sourceItem?.IsShortcut ?? false;

                var targetExisting = targetContainer[targetReference.Index];
                targetContainer[targetReference.Index] = new QuickPanelSlotItem
                {
                    ExtensionId = clipboard.ExtensionId,
                    IsShortcut = sourceIsShortcut
                };
                if (clipboard.SourceSlot.Index >= 0 && clipboard.SourceSlot.Index < sourceContainer.Count)
                {
                    sourceContainer[clipboard.SourceSlot.Index] = targetExisting;
                }

                RefreshAllLegacySlots();
                SaveQuickPanelSettings("quickpanel-move-slot");
                _mainWindow.ClearQuickPanelClipboard();
                LoadSlots();
                RefreshActiveFolderAfterMutation();
                message = targetExisting == null
                    ? $"已移动到第 {targetSlot.Index + 1} 个槽位：{clipboard.Title}"
                    : $"已与第 {targetSlot.Index + 1} 个槽位交换位置：{clipboard.Title}";
                return true;
            }
        }

        targetContainer[targetReference.Index] = new QuickPanelSlotItem
        {
            ExtensionId = clipboard.ExtensionId,
            IsShortcut = true
        };
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-paste-slot");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
        _mainWindow.ClearQuickPanelClipboard();
        message = targetSlot.Item == null
            ? $"已粘贴到第 {targetSlot.Index + 1} 个槽位：{clipboard.Title}"
            : $"已替换第 {targetSlot.Index + 1} 个槽位为：{clipboard.Title}";
        return true;
    }

    private void BringToFront()
    {
        Activate();
        Focus();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void HidePanel()
    {
        _releaseTargetTimer.Stop();
        StopFolderHoverTimer();
        HideFolderPreview();
        CollapseFolder();
        ClearReleaseTarget();
        Topmost = false;
        Hide();
    }

    private void HidePanelIfAllowed()
    {
        if (_isPinned)
        {
            HostAssets.AppendLog("Quick panel hide skipped because panel is pinned.");
            return;
        }

        HidePanel();
    }

    private System.Windows.Point DeviceToDips(System.Windows.Point point)
    {
        var transform = GetTransformFromDevice();
        return transform.Transform(point);
    }

    private Rect DeviceRectToDips(System.Drawing.Rectangle rectangle)
    {
        var topLeft = DeviceToDips(new System.Windows.Point(rectangle.Left, rectangle.Top));
        var bottomRight = DeviceToDips(new System.Windows.Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private Matrix GetTransformFromDevice()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(handle);
        return source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
    }

    private void SaveQuickPanelSettings(string reason)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged(reason);
    }

    private static List<QuickPanelSlotItem?> GetFolderSlotItems(QuickPanelSlotItem folderItem)
    {
        folderItem.FolderSlotItems ??= [];
        if (folderItem.FolderSlotItems.Count == 0 && folderItem.FolderExtensionIds.Count > 0)
        {
            folderItem.FolderSlotItems = folderItem.FolderExtensionIds
                .Take(FolderSlotCount)
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : new QuickPanelSlotItem { ExtensionId = id })
                .ToList();
        }

        while (folderItem.FolderSlotItems.Count < FolderSlotCount)
        {
            folderItem.FolderSlotItems.Add(null);
        }

        if (folderItem.FolderSlotItems.Count > FolderSlotCount)
        {
            folderItem.FolderSlotItems = folderItem.FolderSlotItems.Take(FolderSlotCount).ToList();
        }

        RefreshFolderLegacyIds(folderItem);
        return folderItem.FolderSlotItems;
    }

    private QuickPanelGroupSettings? GetGroupByReference(QuickPanelSlotReference reference) =>
        reference.IsContextual
            ? _settings.QuickPanelContextGroups.FirstOrDefault(group => string.Equals(group.Id, reference.GroupId, StringComparison.OrdinalIgnoreCase))
            : _settings.QuickPanelGlobalGroups.FirstOrDefault(group => string.Equals(group.Id, reference.GroupId, StringComparison.OrdinalIgnoreCase));

    private List<QuickPanelSlotItem?>? GetSlotContainer(QuickPanelSlotReference reference)
    {
        var group = GetGroupByReference(reference);
        if (group == null)
        {
            return null;
        }

        var container = group.SlotItems;
        while (container.Count < 12)
        {
            container.Add(null);
        }

        foreach (var folderIndex in reference.ContainerPath)
        {
            if (folderIndex < 0 || folderIndex >= container.Count)
            {
                return null;
            }

            var folder = container[folderIndex];
            if (folder?.IsFolder != true)
            {
                return null;
            }

            container = GetFolderSlotItems(folder);
        }

        return container;
    }

    private QuickPanelSlotItem? GetSlotItem(QuickPanelSlotReference reference)
    {
        var container = GetSlotContainer(reference);
        return container != null && reference.Index >= 0 && reference.Index < container.Count
            ? container[reference.Index]
            : null;
    }

    private static void RefreshFolderLegacyIds(QuickPanelSlotItem folderItem)
    {
        folderItem.FolderExtensionIds = (folderItem.FolderSlotItems ?? [])
            .Where(static slot => slot != null && !slot.IsFolder && !string.IsNullOrWhiteSpace(slot.ExtensionId))
            .Select(static slot => slot!.ExtensionId!)
            .ToList();
    }

    private void RefreshAllLegacySlots()
    {
        foreach (var group in _settings.QuickPanelGlobalGroups.Concat(_settings.QuickPanelContextGroups))
        {
            RefreshLegacySlots(group);
        }
    }

    private void RefreshActiveFolderAfterMutation()
    {
        if (_activeFolderReference == null)
        {
            return;
        }

        if (LoadActiveFolderFromReference(_activeFolderReference, _isFolderPinnedOpen))
        {
            return;
        }

        CollapseFolder();
    }

    private bool LoadActiveFolderFromReference(QuickPanelSlotReference folderReference, bool pinnedOpen)
    {
        var folderItem = GetSlotItem(folderReference);
        if (folderItem?.IsFolder != true)
        {
            return false;
        }

        var commands = _mainWindow.GetAllCommands();
        var folderPath = folderReference.ContainerPath.Concat([folderReference.Index]).ToList();
        var activeSlots = new ObservableCollection<SlotViewModel>();
        var folderSlotItems = GetFolderSlotItems(folderItem);
        for (var index = 0; index < FolderSlotCount; index++)
        {
            var slot = CreateSlotViewModel(
                index,
                folderSlotItems.ElementAtOrDefault(index),
                commands,
                folderReference.IsContextual,
                folderReference.GroupId,
                folderPath);
            slot.SetFolderChildSource(folderReference.GroupId, folderReference.Index, index);
            activeSlots.Add(slot);
        }

        _activeFolderReference = folderReference;
        ActiveFolderTitle = folderItem.FolderName ?? "新分组";
        ActiveFolderSlots = activeSlots;
        _isFolderPinnedOpen = pinnedOpen;
        OnPropertyChanged(nameof(ActiveFolderOverlayHeight));
        IsFolderExpanded = true;
        return true;
    }

    private void NavigateToParentFolder()
    {
        if (_activeFolderReference == null)
        {
            CollapseFolder();
            return;
        }

        if (_activeFolderReference.ContainerPath.Count == 0)
        {
            CollapseFolder();
            return;
        }

        var parentPath = _activeFolderReference.ContainerPath.ToList();
        var parentIndex = parentPath[^1];
        parentPath.RemoveAt(parentPath.Count - 1);
        var parentReference = new QuickPanelSlotReference(
            _activeFolderReference.IsContextual,
            _activeFolderReference.GroupId,
            parentIndex,
            parentPath);
        if (!LoadActiveFolderFromReference(parentReference, pinnedOpen: true))
        {
            CollapseFolder();
        }
    }

    private static QuickPanelSlotItem? CloneSlotItem(QuickPanelSlotItem? item)
    {
        return item == null
            ? null
            : new QuickPanelSlotItem
            {
                ItemType = item.ItemType,
                ExtensionId = item.ExtensionId,
                FolderName = item.FolderName,
                FolderExtensionIds = item.FolderExtensionIds.ToList(),
                FolderSlotItems = item.FolderSlotItems.Select(CloneSlotItem).ToList()
            };
    }

    private static List<QuickPanelSlotItem?> CreateFolderSlotItems(params QuickPanelSlotItem?[] initialItems)
    {
        var items = initialItems.Take(FolderSlotCount).ToList();
        while (items.Count < FolderSlotCount)
        {
            items.Add(null);
        }

        return items;
    }

    private void ExpandFolder(SlotViewModel folderSlot, bool pinnedOpen)
    {
        if (!folderSlot.IsFolder)
        {
            return;
        }

        var commands = _mainWindow.GetAllCommands();
        var parentReference = BuildSlotReference(folderSlot);
        if (parentReference == null)
        {
            return;
        }

        var folderItem = GetSlotItem(parentReference);
        if (folderItem?.IsFolder != true)
        {
            return;
        }

        var folderPath = parentReference.ContainerPath.Concat([parentReference.Index]).ToList();
        _activeFolderReference = new QuickPanelSlotReference(parentReference.IsContextual, parentReference.GroupId, parentReference.Index, parentReference.ContainerPath.ToList());
        ActiveFolderTitle = folderSlot.Title;
        var activeSlots = new ObservableCollection<SlotViewModel>();
        var folderSlotItems = GetFolderSlotItems(folderItem);
        for (var index = 0; index < FolderSlotCount; index++)
        {
            var slot = CreateSlotViewModel(
                index,
                folderSlotItems.ElementAtOrDefault(index),
                commands,
                folderSlot.IsContextual,
                parentReference.GroupId,
                folderPath);
            slot.SetFolderChildSource(parentReference.GroupId, parentReference.Index, index);
            activeSlots.Add(slot);
        }

        ActiveFolderSlots = activeSlots;
        _isFolderPinnedOpen = pinnedOpen;
        if (!pinnedOpen)
        {
            _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(160);
            ClearReleaseTarget();
        }

        IsFolderExpanded = true;
    }

    private void ShowFolderPreview(SlotViewModel folderSlot)
    {
        if (!folderSlot.IsFolder || ReferenceEquals(_previewFolderSlot, folderSlot))
        {
            return;
        }

        _previewFolderSlot = folderSlot;
        FolderPreviewTitle = folderSlot.Title;
        FolderPreviewItems = new ObservableCollection<FolderPreviewIconViewModel>(
            ResolveFolderPreviewCommands(folderSlot).Select(static command => new FolderPreviewIconViewModel(command)));
        IsFolderPreviewVisible = FolderPreviewItems.Count > 0;
    }

    private void HideFolderPreview()
    {
        _previewFolderSlot = null;
        FolderPreviewTitle = string.Empty;
        FolderPreviewItems = [];
        IsFolderPreviewVisible = false;
    }

    private IReadOnlyList<CommandItem> ResolveFolderPreviewCommands(SlotViewModel folderSlot)
    {
        if (!folderSlot.IsFolder)
        {
            return [];
        }

        var commands = _mainWindow.GetAllCommands();
        return folderSlot.FolderExtensionIds
            .Select(id => commands.FirstOrDefault(command => string.Equals(command.ExtensionId, id, StringComparison.OrdinalIgnoreCase)))
            .OfType<CommandItem>()
            .ToList();
    }

    private void CollapseFolder()
    {
        ActiveFolderTitle = string.Empty;
        ActiveFolderSlots = [];
        _activeFolderReference = null;
        _isFolderPinnedOpen = false;
        IsFolderExpanded = false;
        ClearReleaseTarget();
    }

    private void FolderOverlay_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFolderPinnedOpen && !IsEditMode)
        {
            CollapseFolder();
        }
    }

    private void StartFolderHoverTimer(SlotViewModel source, SlotViewModel target)
    {
        _dragSourceSlot = source;
        _folderHoverTarget = target;
        if (!_folderCreationTimer.IsEnabled)
        {
            _folderCreationTimer.Start();
        }
    }

    private void StopFolderHoverTimer()
    {
        _folderCreationTimer.Stop();
        _folderHoverTarget = null;
    }

    private void FolderCreationTimer_Tick(object? sender, EventArgs e)
    {
        var source = _dragSourceSlot;
        var target = _folderHoverTarget;
        StopFolderHoverTimer();
        if (source == null || target == null)
        {
            return;
        }

        CreateFolderFromSlots(source, target);
    }

    private void MoveOrSwapSlot(SlotViewModel source, SlotViewModel target)
    {
        var sourceReference = BuildSlotReference(source);
        var targetReference = BuildSlotReference(target);
        if (sourceReference == null || targetReference == null)
        {
            HostAssets.AppendLog(
                $"Quick panel drag ignored: missing slot reference, sourceContext={source.IsContextual}, targetContext={target.IsContextual}, sourceGroup={source.SourceGroupId ?? ""}, targetGroup={target.SourceGroupId ?? ""}.");
            return;
        }

        var sourceContainer = GetSlotContainer(sourceReference);
        var targetContainer = GetSlotContainer(targetReference);
        if (sourceContainer == null || targetContainer == null)
        {
            HostAssets.AppendLog(
                $"Quick panel drag ignored: missing slot container, sourceGroup={sourceReference.GroupId}, targetGroup={targetReference.GroupId}.");
            return;
        }

        while (sourceContainer.Count < 12)
        {
            sourceContainer.Add(null);
        }

        while (targetContainer.Count < 12)
        {
            targetContainer.Add(null);
        }

        var sourceItem = sourceContainer[sourceReference.Index];
        var targetItem = targetContainer[targetReference.Index];
        targetContainer[targetReference.Index] = sourceItem;
        sourceContainer[sourceReference.Index] = targetItem;
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-drag-swap-slot");
        HostAssets.AppendLog(
            $"Quick panel drag saved: sourceGroup={sourceReference.GroupId}, sourceIndex={sourceReference.Index}, targetGroup={targetReference.GroupId}, targetIndex={targetReference.Index}, targetContext={targetReference.IsContextual}.");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
    }

    private void MoveFolderChildToSlot(QuickPanelFolderChildDragPayload source, SlotViewModel target)
    {
        var sourceGroup = source.IsContextual
            ? _settings.QuickPanelContextGroups.FirstOrDefault(group => string.Equals(group.Id, source.GroupId, StringComparison.OrdinalIgnoreCase))
            : _settings.QuickPanelGlobalGroups.FirstOrDefault(group => string.Equals(group.Id, source.GroupId, StringComparison.OrdinalIgnoreCase));
        var targetGroup = target.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (sourceGroup == null || targetGroup == null)
        {
            return;
        }

        while (sourceGroup.SlotItems.Count < (source.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            sourceGroup.SlotItems.Add(null);
        }

        while (targetGroup.SlotItems.Count < (target.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            targetGroup.SlotItems.Add(null);
        }

        if (source.FolderIndex < 0 || source.FolderIndex >= sourceGroup.SlotItems.Count)
        {
            return;
        }

        var sourceFolder = sourceGroup.SlotItems[source.FolderIndex];
        if (sourceFolder?.IsFolder != true)
        {
            return;
        }

        var sourceChildIndex = FindFolderChildIndex(sourceFolder, source);
        if (sourceChildIndex < 0)
        {
            return;
        }

        if (target.IsFolder)
        {
            if (ReferenceEquals(sourceGroup, targetGroup) && source.FolderIndex == target.Index)
            {
                return;
            }

            var targetFolder = targetGroup.SlotItems.ElementAtOrDefault(target.Index);
            if (targetFolder?.IsFolder != true)
            {
                return;
            }

            if (!targetFolder.FolderExtensionIds.Any(id => string.Equals(id, source.ExtensionId, StringComparison.OrdinalIgnoreCase)))
            {
                targetFolder.FolderExtensionIds.Add(source.ExtensionId);
            }

            sourceFolder.FolderExtensionIds.RemoveAt(sourceChildIndex);
            NormalizeFolderSlotAfterRemoval(sourceGroup, source.FolderIndex);
            RefreshLegacySlots(sourceGroup);
            RefreshLegacySlots(targetGroup);
            SaveQuickPanelSettings("quickpanel-move-folder-child-to-folder");
            CollapseFolder();
            LoadSlots();
            return;
        }

        var targetExisting = targetGroup.SlotItems[target.Index];
        if (targetExisting?.IsFolder == true)
        {
            return;
        }

        if (targetExisting == null)
        {
            sourceFolder.FolderExtensionIds.RemoveAt(sourceChildIndex);
        }
        else if (!string.IsNullOrWhiteSpace(targetExisting.ExtensionId))
        {
            sourceFolder.FolderExtensionIds[sourceChildIndex] = targetExisting.ExtensionId;
        }

        targetGroup.SlotItems[target.Index] = new QuickPanelSlotItem
        {
            ExtensionId = source.ExtensionId
        };
        NormalizeFolderSlotAfterRemoval(sourceGroup, source.FolderIndex);
        RefreshLegacySlots(sourceGroup);
        RefreshLegacySlots(targetGroup);
        SaveQuickPanelSettings("quickpanel-move-folder-child-to-slot");
        CollapseFolder();
        LoadSlots();
    }

    private static int FindFolderChildIndex(QuickPanelSlotItem folderItem, QuickPanelFolderChildDragPayload source)
    {
        if (source.ItemIndex >= 0 &&
            source.ItemIndex < folderItem.FolderExtensionIds.Count &&
            string.Equals(folderItem.FolderExtensionIds[source.ItemIndex], source.ExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            return source.ItemIndex;
        }

        return folderItem.FolderExtensionIds.FindIndex(id => string.Equals(id, source.ExtensionId, StringComparison.OrdinalIgnoreCase));
    }

    private static void NormalizeFolderSlotAfterRemoval(QuickPanelGroupSettings group, int folderIndex)
    {
        if (folderIndex < 0 || folderIndex >= group.SlotItems.Count)
        {
            return;
        }

        var folder = group.SlotItems[folderIndex];
        if (folder?.IsFolder != true)
        {
            return;
        }

        folder.FolderExtensionIds = folder.FolderExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folder.FolderExtensionIds.Count == 0)
        {
            group.SlotItems[folderIndex] = null;
        }
        else if (folder.FolderExtensionIds.Count == 1)
        {
            group.SlotItems[folderIndex] = new QuickPanelSlotItem
            {
                ExtensionId = folder.FolderExtensionIds[0]
            };
        }
    }

    private static void RefreshLegacySlots(QuickPanelGroupSettings group)
    {
        foreach (var item in group.SlotItems)
        {
            RefreshNestedFolderLegacyIds(item);
        }

        group.Slots = group.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
    }

    private static void RefreshNestedFolderLegacyIds(QuickPanelSlotItem? item)
    {
        if (item?.IsFolder != true)
        {
            return;
        }

        foreach (var child in GetFolderSlotItems(item))
        {
            RefreshNestedFolderLegacyIds(child);
        }

        RefreshFolderLegacyIds(item);
    }

    private void CreateFolderFromSlots(SlotViewModel source, SlotViewModel target)
    {
        if (source.Command == null || target.Command == null || source.IsFolder || target.IsFolder)
        {
            return;
        }

        if (string.Equals(source.Command.ExtensionId, target.Command.ExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourceReference = BuildSlotReference(source);
        var targetReference = BuildSlotReference(target);
        if (sourceReference == null || targetReference == null)
        {
            return;
        }

        var sourceContainer = GetSlotContainer(sourceReference);
        var targetContainer = GetSlotContainer(targetReference);
        if (sourceContainer == null || targetContainer == null)
        {
            return;
        }

        var sourceItem = sourceContainer[sourceReference.Index];
        var targetItem = targetContainer[targetReference.Index];
        if (sourceItem == null || targetItem == null)
        {
            return;
        }

        targetContainer[targetReference.Index] = new QuickPanelSlotItem
        {
            ItemType = "folder",
            FolderName = $"{target.Title}组",
            FolderSlotItems = CreateFolderSlotItems(CloneSlotItem(targetItem), CloneSlotItem(sourceItem))
        };
        RefreshFolderLegacyIds(targetContainer[targetReference.Index]!);
        sourceContainer[sourceReference.Index] = null;
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-auto-create-folder");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
    }

    private void AddSlotToFolder(SlotViewModel source, SlotViewModel targetFolder)
    {
        if (source.Command == null || source.IsFolder || !targetFolder.IsFolder)
        {
            return;
        }

        var sourceReference = BuildSlotReference(source);
        var targetReference = BuildSlotReference(targetFolder);
        if (sourceReference == null || targetReference == null)
        {
            return;
        }

        var sourceContainer = GetSlotContainer(sourceReference);
        var targetContainer = GetSlotContainer(targetReference);
        if (sourceContainer == null || targetContainer == null)
        {
            return;
        }

        var sourceItem = sourceContainer[sourceReference.Index];
        var folderItem = targetContainer[targetReference.Index];
        if (sourceItem == null || folderItem?.IsFolder != true)
        {
            return;
        }

        var folderSlots = GetFolderSlotItems(folderItem);
        var emptyIndex = folderSlots.FindIndex(static slot => slot == null);
        if (emptyIndex < 0)
        {
            return;
        }

        folderSlots[emptyIndex] = CloneSlotItem(sourceItem);
        RefreshFolderLegacyIds(folderItem);
        sourceContainer[sourceReference.Index] = null;
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-add-slot-to-folder");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
    }

    private void AddCommandToFolder(SlotViewModel targetFolder, CommandItem command)
    {
        if (!targetFolder.IsFolder)
        {
            return;
        }

        var targetReference = BuildSlotReference(targetFolder);
        if (targetReference == null)
        {
            return;
        }

        var targetContainer = GetSlotContainer(targetReference);
        if (targetContainer == null)
        {
            return;
        }

        var folderItem = targetContainer.ElementAtOrDefault(targetReference.Index);
        if (folderItem?.IsFolder != true)
        {
            return;
        }

        var folderSlots = GetFolderSlotItems(folderItem);
        if (folderSlots.Any(slot => slot != null && !slot.IsFolder && string.Equals(slot.ExtensionId, command.ExtensionId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var emptyIndex = folderSlots.FindIndex(static slot => slot == null);
        if (emptyIndex < 0)
        {
            return;
        }

        folderSlots[emptyIndex] = new QuickPanelSlotItem { ExtensionId = command.ExtensionId };
        RefreshFolderLegacyIds(folderItem);
        RefreshAllLegacySlots();
        SaveQuickPanelSettings("quickpanel-drop-command-to-folder");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
    }

    private void AddCommandToSlot(SlotViewModel target, CommandItem command)
    {
        if (target.IsFolder)
        {
            AddCommandToFolder(target, command);
            return;
        }

        var targetReference = BuildSlotReference(target);
        if (targetReference == null)
        {
            return;
        }

        var targetContainer = GetSlotContainer(targetReference);
        if (targetContainer == null || targetContainer[targetReference.Index] != null)
        {
            return;
        }

        targetContainer[targetReference.Index] = new QuickPanelSlotItem { ExtensionId = command.ExtensionId };
        RefreshAllLegacySlots();
        SaveQuickPanelSettings(target.IsContextual ? "quickpanel-drop-from-launcher-context" : "quickpanel-drop-from-launcher-global");
        LoadSlots();
        RefreshActiveFolderAfterMutation();
    }

    private void PollReleaseTarget()
    {
        if (!IsVisible)
        {
            _releaseTargetTimer.Stop();
            ClearReleaseTarget();
            return;
        }

        if (DateTimeOffset.UtcNow < _suspendReleaseTargetPollingUntilUtc)
        {
            return;
        }

        if (ShouldHideForOutsidePointerDown())
        {
            HidePanelIfAllowed();
            return;
        }

        _ = ResolveSlotUnderCursor(occupiedOnly: true, updateTarget: true);
    }

    private bool ShouldHideForOutsidePointerDown()
    {
        if (DateTimeOffset.UtcNow < _suspendOutsideClickHideUntilUtc)
        {
            return false;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return false;
        }

        if (_isDraggingSlot || IsCursorInsideQuickPanel())
        {
            return false;
        }

        return IsMouseButtonDown(VkLeftButton) ||
               IsMouseButtonDown(VkRightButton) ||
               IsMouseButtonDown(VkMiddleButton);
    }

    private SlotViewModel? ResolveSlotUnderCursor(bool occupiedOnly = false, bool updateTarget = true)
    {
        var point = NativeMethods.GetCursorPosition();
        var localPoint = PointFromScreen(point);
        var hit = InputHitTest(localPoint) as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement { Tag: SlotViewModel taggedSlot })
            {
                if (occupiedOnly && taggedSlot.Command == null)
                {
                    if (updateTarget)
                    {
                        ClearReleaseTarget();
                    }

                    return null;
                }

                if (updateTarget)
                {
                    SetReleaseTarget(taggedSlot);
                }

                return taggedSlot;
            }

            if (hit is FrameworkElement { DataContext: SlotViewModel contextSlot })
            {
                if (occupiedOnly && contextSlot.Command == null)
                {
                    if (updateTarget)
                    {
                        ClearReleaseTarget();
                    }

                    return null;
                }

                if (updateTarget)
                {
                    SetReleaseTarget(contextSlot);
                }

                return contextSlot;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        if (updateTarget)
        {
            ClearReleaseTarget();
        }

        return null;
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "hwnd=0x0";
        }

        var titleBuilder = new StringBuilder(256);
        _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return $"hwnd=0x{hwnd.ToInt64():X}, pid={processId}, title=\"{titleBuilder}\"";
    }

    private static ForegroundAppContext? BuildForegroundAppContext(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var titleBuilder = new StringBuilder(256);
        _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        try
        {
            var process = Process.GetProcessById((int)processId);
            return new ForegroundAppContext(process.ProcessName, titleBuilder.ToString().Trim());
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => 
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static bool IsMouseButtonDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}

public class QuickPanelGroupItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public QuickPanelGroupItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public string ShortName => Name.Length <= 2 ? Name : Name[..2];

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
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class SlotViewModel : INotifyPropertyChanged
{
    public int Index { get; }
    private QuickPanelSlotItem? _item;
    private CommandItem? _command;
    private bool _isFavorite;
    private bool _isReleaseTarget;
    private bool _isContextual;
    private string _folderName = string.Empty;
    private List<string> _folderExtensionIds = [];
    private List<QuickPanelSlotItem?> _folderSlotItems = [];
    private List<FolderPreviewIconViewModel> _folderPreviewItems = [];
    private bool _isFolderChild;
    private string? _sourceGroupId;
    private List<int> _containerPath = [];
    private int _sourceFolderIndex = -1;
    private int _sourceFolderItemIndex = -1;

    public CommandItem? Command => _command;

    public QuickPanelSlotItem? Item => _item;

    public SlotViewModel(int index, CommandItem? command, bool isFavorite = false, bool isContextual = false)
    {
        Index = index;
        SetCommand(command, isFavorite, isContextual);
    }

    public static SlotViewModel CreateFolder(
        int index,
        string folderName,
        IReadOnlyList<string> folderExtensionIds,
        IReadOnlyList<QuickPanelSlotItem?> folderSlotItems,
        IReadOnlyList<CommandItem> previewCommands,
        bool isContextual)
    {
        var vm = new SlotViewModel(index, null, false, isContextual)
        {
            _item = new QuickPanelSlotItem
            {
                ItemType = "folder",
                FolderName = folderName,
                FolderExtensionIds = folderExtensionIds.ToList(),
                FolderSlotItems = folderSlotItems.Select(CloneSlotItem).ToList()
            },
            _command = null,
            _folderName = folderName,
            _folderExtensionIds = folderExtensionIds.ToList(),
            _folderSlotItems = folderSlotItems.Select(CloneSlotItem).ToList(),
            _folderPreviewItems = previewCommands.Take(4).Select(static command => new FolderPreviewIconViewModel(command)).ToList(),
            _isFavorite = false,
            _isContextual = isContextual
        };
        vm.NotifyAll();
        return vm;
    }

    public void SetCommand(CommandItem? command, bool isFavorite = false, bool isContextual = false)
    {
        DetachCommandEvents();
        _item = command == null
            ? null
            : new QuickPanelSlotItem
            {
                ExtensionId = command.ExtensionId
            };
        _command = command;
        _isFavorite = isFavorite;
        _isContextual = isContextual;
        _folderName = string.Empty;
        _folderExtensionIds = [];
        _folderSlotItems = [];
        _folderPreviewItems = [];
        _isFolderChild = false;
        _sourceGroupId = null;
        _containerPath = [];
        _sourceFolderIndex = -1;
        _sourceFolderItemIndex = -1;
        AttachCommandEvents();
        NotifyAll();
    }

    public void SetSlotLocation(string? groupId, IReadOnlyList<int>? containerPath)
    {
        _sourceGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId;
        _containerPath = containerPath?.ToList() ?? [];
        OnPropertyChanged(nameof(SourceGroupId));
        OnPropertyChanged(nameof(ContainerPath));
    }

    public void SetFolderChildSource(string groupId, int folderIndex, int itemIndex)
    {
        _isFolderChild = true;
        _sourceGroupId = groupId;
        _sourceFolderIndex = folderIndex;
        _sourceFolderItemIndex = itemIndex;
        OnPropertyChanged(nameof(IsFolderChild));
        OnPropertyChanged(nameof(SourceGroupId));
        OnPropertyChanged(nameof(SourceFolderIndex));
        OnPropertyChanged(nameof(SourceFolderItemIndex));
    }

    public void SetFavorite(bool isFavorite)
    {
        _isFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
    }

    public bool IsReleaseTarget
    {
        get => _isReleaseTarget;
        set
        {
            if (value == _isReleaseTarget)
            {
                return;
            }

            _isReleaseTarget = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolder => _item?.IsFolder ?? false;
    public bool IsEmpty => _item == null;
    public bool IsOccupied => _item != null;
    public bool IsFavorite => _isFavorite && !IsFolder;
    public bool IsContextual => _isContextual;
    public bool IsFolderChild => _isFolderChild;
    public string? SourceGroupId => _sourceGroupId;
    public IReadOnlyList<int> ContainerPath => _containerPath;
    public int SourceFolderIndex => _sourceFolderIndex;
    public int SourceFolderItemIndex => _sourceFolderItemIndex;
    public bool CanEdit => !IsFolder && _command?.Source == CommandSource.LocalExtension;
    public bool CanPublish => !IsFolder && _command?.Source == CommandSource.LocalExtension;
    public bool CanOpenDirectory => CanEdit && !string.IsNullOrWhiteSpace(_command?.ExtensionDirectoryPath);
    public bool CanRemoveFromFixedSlots => _item != null;
    public string FavoriteLabel => _isFavorite ? "取消收藏" : "收藏";
    public string Title => IsFolder ? _folderName : _command?.Title ?? string.Empty;
    public string DisplayTitle => IsCSharpPrebuilding ? "编译中..." : Title;
    public ImageSource? Icon => IsFolder ? null : _command?.IconSource;
    public Geometry? VectorIcon => IsFolder ? null : _command?.VectorIcon;
    public bool HasImageIcon => !IsFolder && (_command?.HasImageIcon ?? false);
    public bool HasVectorIcon => !IsFolder && (_command?.HasVectorIcon ?? false);
    public bool UseGlyphIcon => !IsFolder && (_command?.UseGlyphIcon ?? false);
    public string DisplayGlyph => _command?.DisplayGlyph ?? string.Empty;
    public bool HasNewBadge => !IsFolder && (_command?.HasNewBadge ?? false);
    public bool IsCSharpPrebuilding => !IsFolder && (_command?.IsCSharpPrebuilding ?? false);
    public bool HasFolderPreview => IsFolder && _folderPreviewItems.Count > 0;
    public IReadOnlyList<FolderPreviewIconViewModel> FolderPreviewItems => _folderPreviewItems;
    public bool HasFolderBadge => IsFolder;
    public string FolderBadgeText => _folderExtensionIds.Count > 99 ? "99+" : _folderExtensionIds.Count.ToString();
    public IReadOnlyList<string> FolderExtensionIds => _folderExtensionIds;
    public IReadOnlyList<QuickPanelSlotItem?> FolderSlotItems => _folderSlotItems;

    public bool IsShortcut
    {
        get => _item?.IsShortcut ?? false;
        set
        {
            if (_item != null && _item.IsShortcut != value)
            {
                _item.IsShortcut = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            if (IsFolder || IsEmpty || _command == null || string.IsNullOrEmpty(_command.ExtensionId))
            {
                return false;
            }
            return RunningExtensionRegistry.GetSnapshot().Any(x => string.Equals(x.ExtensionId, _command.ExtensionId, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RefreshRunningState()
    {
        OnPropertyChanged(nameof(IsRunning));
    }

    public QuickPanelSlotItem? CloneSlotItem()
    {
        return CloneSlotItem(_item);
    }

    private static QuickPanelSlotItem? CloneSlotItem(QuickPanelSlotItem? item)
    {
        return item == null
            ? null
            : new QuickPanelSlotItem
            {
                ItemType = item.ItemType,
                ExtensionId = item.ExtensionId,
                FolderName = item.FolderName,
                FolderExtensionIds = item.FolderExtensionIds.ToList(),
                FolderSlotItems = item.FolderSlotItems.Select(CloneSlotItem).ToList(),
                IsShortcut = item.IsShortcut
            };
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOccupied));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(VectorIcon));
        OnPropertyChanged(nameof(HasImageIcon));
        OnPropertyChanged(nameof(HasVectorIcon));
        OnPropertyChanged(nameof(UseGlyphIcon));
        OnPropertyChanged(nameof(DisplayGlyph));
        OnPropertyChanged(nameof(HasNewBadge));
        OnPropertyChanged(nameof(IsCSharpPrebuilding));
        OnPropertyChanged(nameof(HasFolderPreview));
        OnPropertyChanged(nameof(FolderPreviewItems));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
        OnPropertyChanged(nameof(IsContextual));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(CanOpenDirectory));
        OnPropertyChanged(nameof(CanRemoveFromFixedSlots));
        OnPropertyChanged(nameof(HasFolderBadge));
        OnPropertyChanged(nameof(FolderBadgeText));
        OnPropertyChanged(nameof(FolderExtensionIds));
        OnPropertyChanged(nameof(FolderSlotItems));
        OnPropertyChanged(nameof(IsFolderChild));
        OnPropertyChanged(nameof(SourceGroupId));
        OnPropertyChanged(nameof(ContainerPath));
        OnPropertyChanged(nameof(IsShortcut));
        OnPropertyChanged(nameof(SourceFolderIndex));
        OnPropertyChanged(nameof(SourceFolderItemIndex));
        OnPropertyChanged(nameof(IsRunning));
    }

    private void AttachCommandEvents()
    {
        if (_command != null)
        {
            _command.PropertyChanged += Command_PropertyChanged;
        }
    }

    private void DetachCommandEvents()
    {
        if (_command != null)
        {
            _command.PropertyChanged -= Command_PropertyChanged;
        }
    }

    private void Command_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) ||
            string.Equals(e.PropertyName, nameof(CommandItem.HasNewBadge), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasNewBadge));
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName) ||
            string.Equals(e.PropertyName, nameof(CommandItem.IsCSharpPrebuilding), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsCSharpPrebuilding));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record QuickPanelSlotReference(bool IsContextual, string GroupId, int Index, List<int> ContainerPath);

public sealed record QuickPanelClipboardItem(string ExtensionId, string Title, bool IsCut, QuickPanelSlotReference? SourceSlot);

public sealed record QuickPanelFolderChildDragPayload(
    bool IsContextual,
    string GroupId,
    int FolderIndex,
    int ItemIndex,
    string ExtensionId);

public sealed class FolderPreviewIconViewModel
{
    public FolderPreviewIconViewModel(CommandItem command)
    {
        IconSource = command.IconSource;
        VectorIcon = command.VectorIcon;
        AccentBrush = command.AccentBrush;
        DisplayGlyph = command.DisplayGlyph;
        HasImageIcon = command.HasImageIcon;
        HasVectorIcon = command.HasVectorIcon;
        UseGlyphIcon = command.UseGlyphIcon;
    }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public bool HasImageIcon { get; }

    public bool HasVectorIcon { get; }

    public bool UseGlyphIcon { get; }
}

internal static class NativeMethods
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr SimulatedInputMarker = new(0x59414E5B);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO guiThreadInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();



    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct InputUnion
    {
        [System.Runtime.InteropServices.FieldOffset(0)]
        public MOUSEINPUT mi;

        [System.Runtime.InteropServices.FieldOffset(0)]
        public KEYBDINPUT ki;

        [System.Runtime.InteropServices.FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public static uint SendCtrlV(out int inputCount, out int lastError)
    {
        var inputs = new[]
        {
            KeyInput(VkControl, 0),
            KeyInput(VkV, 0),
            KeyInput(VkV, KeyeventfKeyup),
            KeyInput(VkControl, KeyeventfKeyup)
        };

        inputCount = inputs.Length;
        var sent = SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        lastError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return sent;
    }

    private static INPUT KeyInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags,
                    dwExtraInfo = SimulatedInputMarker
                }
            }
        };
    }

    public static void ShowWithoutActivation(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    public static IntPtr GetForegroundFocusWindow()
    {
        var info = new GUITHREADINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>()
        };

        return GetGUIThreadInfo(0, ref info)
            ? info.hwndFocus != IntPtr.Zero ? info.hwndFocus : info.hwndCaret
            : IntPtr.Zero;
    }

    public static bool TryRestoreFocus(IntPtr foregroundWindow, IntPtr focusWindow, out string detail)
    {
        if (focusWindow == IntPtr.Zero)
        {
            detail = "focus=zero";
            return false;
        }

        if (!IsWindow(focusWindow))
        {
            detail = "focus window no longer exists";
            return false;
        }

        var currentThreadId = GetCurrentThreadId();
        var focusThreadId = GetWindowThreadProcessId(focusWindow, out _);
        var foregroundThreadId = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var attachedFocus = false;
        var attachedForeground = false;
        try
        {
            if (focusThreadId != 0 && focusThreadId != currentThreadId)
            {
                attachedFocus = AttachThreadInput(currentThreadId, focusThreadId, true);
            }

            if (foregroundThreadId != 0 &&
                foregroundThreadId != currentThreadId &&
                foregroundThreadId != focusThreadId)
            {
                attachedForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            _ = SetFocus(focusWindow);
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            detail = $"currentThread={currentThreadId}, focusThread={focusThreadId}, foregroundThread={foregroundThreadId}, attachFocus={attachedFocus}, attachForeground={attachedForeground}, setFocusError={error}";
            return error == 0;
        }
        finally
        {
            if (attachedForeground)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedFocus)
            {
                _ = AttachThreadInput(currentThreadId, focusThreadId, false);
            }
        }
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
