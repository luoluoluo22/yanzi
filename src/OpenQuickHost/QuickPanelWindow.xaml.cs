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
using System.Diagnostics;

namespace OpenQuickHost;

public partial class QuickPanelWindow : Window, INotifyPropertyChanged
{
    private const int GlobalSlotCount = 12;
    private const int ContextSlotCount = 12;
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
    private readonly DispatcherTimer _releaseTargetTimer;
    private ForegroundAppContext? _foregroundAppContext;
    private QuickPanelGroupItem? _selectedGlobalGroup;
    private QuickPanelGroupItem? _selectedContextGroup;
    private bool _isShowingGlobalFavorites;
    private bool _isShowingContextFavorites;

    public QuickPanelWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _releaseTargetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _releaseTargetTimer.Tick += (_, _) => PollReleaseTarget();
        
        LoadSlots();
        DataContext = this;

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Hide();
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

    public string PinButtonTooltip => _isPinned ? "已固定，失去焦点时不自动关闭" : "点击固定，失去焦点时不自动关闭";

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
                var extensionId = group?.Slots.ElementAtOrDefault(i);
                var command = string.IsNullOrEmpty(extensionId)
                    ? null
                    : allCommands.FirstOrDefault(c => c.ExtensionId == extensionId);
                var isFav = command != null && _settings.GlobalFavoriteExtensionIds.Contains(command.ExtensionId);
                GlobalSlots.Add(new SlotViewModel(i, command, isFav, isContextual: false));
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
                var extensionId = group?.Slots.ElementAtOrDefault(i);
                var command = string.IsNullOrEmpty(extensionId)
                    ? null
                    : allCommands.FirstOrDefault(c => c.ExtensionId == extensionId);
                var isFav = command != null && _settings.ContextFavoriteExtensionIds.Contains(command.ExtensionId);
                ContextSlots.Add(new SlotViewModel(i, command, isFav, isContextual: true));
            }
        }

        while (ContextSlots.Count < ContextSlotCount)
            ContextSlots.Add(new SlotViewModel(ContextSlots.Count, null, false, isContextual: true));

        _allGlobalSlots.Clear();
        _allGlobalSlots.AddRange(GlobalSlots);
        _allContextSlots.Clear();
        _allContextSlots.AddRange(ContextSlots);
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

        foreach (var group in _settings.QuickPanelContextGroups)
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
        var group = isContextual ? GetSelectedContextGroupSettings() : GetSelectedGlobalGroupSettings();
        if (group == null)
        {
            return;
        }

        group.Slots.Clear();
        var sourceSlots = isContextual ? ContextSlots : GlobalSlots;
        var slotCount = isContextual ? ContextSlotCount : GlobalSlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            var vm = sourceSlots.ElementAtOrDefault(i);
            group.Slots.Add(vm?.Command?.ExtensionId);
        }
        if (isContextual)
        {
            _settings.SelectedQuickPanelContextGroupId = group.Id;
        }
        else
        {
            _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        }
        AppSettingsStore.Save(_settings);
    }

    private QuickPanelGroupSettings? GetSelectedGlobalGroupSettings()
    {
        var selectedGroupId = SelectedGlobalGroup?.Id ?? _settings.SelectedQuickPanelGlobalGroupId;
        return _settings.QuickPanelGlobalGroups.FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase));
    }

    private QuickPanelGroupSettings? GetSelectedContextGroupSettings()
    {
        var selectedGroupId = SelectedContextGroup?.Id ?? _settings.SelectedQuickPanelContextGroupId;
        return _settings.QuickPanelContextGroups.FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase));
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
        Hide();
        _mainWindow.OpenSettingsWindow("quickpanel");
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
        AppSettingsStore.Save(_settings);
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
            Name = dialog.ValueText
        };
        _settings.QuickPanelContextGroups.Add(group);
        _settings.SelectedQuickPanelContextGroupId = group.Id;
        AppSettingsStore.Save(_settings);
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
        AppSettingsStore.Save(_settings);
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
        AppSettingsStore.Save(_settings);
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
        AppSettingsStore.Save(_settings);
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
        AppSettingsStore.Save(_settings);
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
        AppSettingsStore.Save(_settings);
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
        AppSettingsStore.Save(_settings);
        LoadSlots();
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
                _ = ExecuteSlotCommandAsync(vm, "quick-panel-click");
            }
            else if (!vm.IsContextual)
            {
                var newCommand = _mainWindow.OpenAddExtensionForSlot();
                if (newCommand != null)
                {
                    vm.SetCommand(newCommand, false);
                    SaveSlots(isContextual: false);
                }
            }
            else
            {
                var newCommand = _mainWindow.OpenAddExtensionForSlot();
                if (newCommand != null)
                {
                    vm.SetCommand(newCommand, false, isContextual: true);
                    SaveSlots(isContextual: true);
                }
            }
        }
    }

    private void SlotButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm } && vm.Command != null)
        {
            SetReleaseTarget(vm);
        }
    }

    private void SlotButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm } && ReferenceEquals(_hoveredSlot, vm))
        {
            ClearReleaseTarget();
        }
    }

    private void SlotButton_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm } && vm.Command != null)
        {
            SetReleaseTarget(vm);
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
            Hide();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (_previousForegroundWindow != IntPtr.Zero)
            {
                var restored = NativeMethods.SetForegroundWindow(_previousForegroundWindow);
                HostAssets.AppendLog($"Quick panel execute: restored previous foreground={restored}, {DescribeWindow(_previousForegroundWindow)}.");
            }

            await Task.Delay(120);
            var input = await SelectionCaptureService.CaptureSelectedInputAsync();
            HostAssets.AppendLog($"Quick panel execute: captured input length={input.Length}.");
            _mainWindow.ExecuteCommandExternally(command, input, launchSource);
        }
        finally
        {
            _isExecutingSlot = false;
            ClearReleaseTarget();
        }
    }

    private void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm)
        {
            vm.SetCommand(null, false, vm.IsContextual);
            SaveSlots(vm.IsContextual);
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

            AppSettingsStore.Save(_settings);
            vm.SetFavorite(favorites.Contains(id));
        }
    }

    private void ToggleGlobalFavorites_Click(object sender, RoutedEventArgs e)
    {
        _isShowingGlobalFavorites = !_isShowingGlobalFavorites;
        OnPropertyChanged(nameof(PanelTitle));
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

        AppSettingsStore.Save(_settings);
        LoadSlots();
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

        _releaseTargetTimer.Stop();
        Hide();
    }

    public void ShowAtMouse()
    {
        HostAssets.AppendLog("Quick panel show requested.");
        _previousForegroundWindow = NativeMethods.GetForegroundWindow();
        _foregroundAppContext = BuildForegroundAppContext(_previousForegroundWindow);
        var point = NativeMethods.GetCursorPosition();
        const double safeAnchorY = 310;
        Left = point.X - Width / 2;
        Top = point.Y - safeAnchorY;
        
        // Ensure within screen bounds
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
        if (Left < screen.Bounds.Left) Left = screen.Bounds.Left;
        if (Top < screen.Bounds.Top) Top = screen.Bounds.Top;
        if (Left + Width > screen.Bounds.Right) Left = screen.Bounds.Right - Width;
        if (Top + Height > screen.Bounds.Bottom) Top = screen.Bounds.Bottom - Height;

        HubSearchBox.Text = string.Empty; // Reset search on show
        _hoveredSlot = null;
        LoadSlots(); // Refresh
        var occupiedGlobal = GlobalSlots.Count(slot => slot.IsOccupied);
        var occupiedContext = ContextSlots.Count(slot => slot.IsOccupied);
        HostAssets.AppendLog($"Quick panel showing at ({Left:0},{Top:0}), cursor=({point.X:0},{point.Y:0}), occupiedGlobal={occupiedGlobal}, occupiedContext={occupiedContext}, totalGlobal={GlobalSlots.Count}, totalContext={ContextSlots.Count}.");
        OnPropertyChanged(nameof(ContextSectionTitle));
        OnPropertyChanged(nameof(ContextHintText));
        Show();
        _releaseTargetTimer.Start();
        Activate();
        Focus();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Focus();
            HubSearchBox.Focus();
            Keyboard.Focus(HubSearchBox);
            HubSearchBox.Select(0, 0);
            HubSearchBox.CaretIndex = 0;
        }, DispatcherPriority.ApplicationIdle);
    }

    private void PollReleaseTarget()
    {
        if (!IsVisible)
        {
            _releaseTargetTimer.Stop();
            ClearReleaseTarget();
            return;
        }

        _ = ResolveSlotUnderCursor(occupiedOnly: true, updateTarget: true);
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
    private CommandItem? _command;
    private bool _isFavorite;
    private bool _isReleaseTarget;
    private bool _isContextual;

    public CommandItem? Command => _command;

    public SlotViewModel(int index, CommandItem? command, bool isFavorite = false, bool isContextual = false)
    {
        Index = index;
        _command = command;
        _isFavorite = isFavorite;
        _isContextual = isContextual;
    }

    public void SetCommand(CommandItem? command, bool isFavorite = false, bool isContextual = false)
    {
        _command = command;
        _isFavorite = isFavorite;
        _isContextual = isContextual;
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOccupied));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(VectorIcon));
        OnPropertyChanged(nameof(HasImageIcon));
        OnPropertyChanged(nameof(HasVectorIcon));
        OnPropertyChanged(nameof(UseGlyphIcon));
        OnPropertyChanged(nameof(DisplayGlyph));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
        OnPropertyChanged(nameof(IsContextual));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanOpenDirectory));
        OnPropertyChanged(nameof(CanRemoveFromFixedSlots));
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

    public bool IsEmpty => _command == null;
    public bool IsOccupied => _command != null;
    public bool IsFavorite => _isFavorite;
    public bool IsContextual => _isContextual;
    public bool CanEdit => _command?.Source == CommandSource.LocalExtension;
    public bool CanOpenDirectory => CanEdit && !string.IsNullOrWhiteSpace(_command?.ExtensionDirectoryPath);
    public bool CanRemoveFromFixedSlots => _command != null && !_isContextual;
    public string FavoriteLabel => _isFavorite ? "取消收藏" : "收藏";
    public string Title => _command?.Title ?? string.Empty;
    public ImageSource? Icon => _command?.IconSource;
    public Geometry? VectorIcon => _command?.VectorIcon;
    public bool HasImageIcon => _command?.HasImageIcon ?? false;
    public bool HasVectorIcon => _command?.HasVectorIcon ?? false;
    public bool UseGlyphIcon => _command?.UseGlyphIcon ?? false;
    public string DisplayGlyph => _command?.DisplayGlyph ?? string.Empty;

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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

