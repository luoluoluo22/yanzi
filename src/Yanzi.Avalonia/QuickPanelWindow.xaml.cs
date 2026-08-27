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
    private bool _isActionWindowOpen;

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

    private string _contextSectionTitle = "应用专属 • 默认";
    public string ContextSectionTitle
    {
        get => _contextSectionTitle;
        private set => SetField(ref _contextSectionTitle, value);
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

    static QuickPanelWindow()
    {
        IsPinnedProperty.Changed.AddClassHandler<QuickPanelWindow>((w, e) =>
        {
            if (e.NewValue is bool isPinned && w._radialSettings != null)
            {
                w._radialSettings.IsPinned = isPinned;
                w._radialSettings.Save();
            }
        });
    }

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
        IsPinned = _radialSettings.IsPinned;

        // Eagerly load and cache slot items and native icons at startup
        LoadSlots();
        if (_mainWindow != null)
        {
            var preloadTargets = _mainWindow.GetDefaultCommands(string.Empty)
                .Where(c => c.ActionKind == CommandActionKind.LaunchApplication && !string.IsNullOrEmpty(c.ApplicationName))
                .Select(c => c.ApplicationName!)
                .ToList();
            MacIconExtractor.PreloadIcons(preloadTargets);
        }
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

        var frontApp = MacIconExtractor.GetFrontmostApplicationName();
        ContextSectionTitle = string.IsNullOrWhiteSpace(frontApp) ? "应用专属 • 默认" : $"应用专属 • {frontApp}";

        PrepareOverlayForActivation(activation);
        if (GlobalSlots.Count == 0 || ContextSlots.Count == 0)
        {
            LoadSlots();
        }
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

    private bool _isContextMenuOpen;
    private DateTime _lastContextMenuClosedAt = DateTime.MinValue;
    private static CommandItem? _copiedSlotCommand;
    private static bool _isCutSlot;
    private static RadialMenuItemViewModel? _cutSourceItem;
    private RadialMenuItemViewModel? _draggedSlot;
    private Point _dragStartPoint;
    private bool _isDraggingInternalSlot;

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isActionWindowOpen || _isContextMenuOpen || _isDraggingInternalSlot)
            return;

        if (DateTime.UtcNow - _lastContextMenuClosedAt <= TimeSpan.FromMilliseconds(300))
            return;

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
        var pointer = e.GetCurrentPoint(this);

        if (pointer.Properties.IsLeftButtonPressed && _draggedSlot != null && !_draggedSlot.IsEmpty)
        {
            var diff = position - _dragStartPoint;
            if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
            {
                _isDraggingInternalSlot = true;
                var hoverTarget = FindSlotAtPosition(position);
                if (hoverTarget != null)
                {
                    SetActiveItem(hoverTarget);
                    ActiveTitle = hoverTarget.IsEmpty
                        ? $"移动到空槽位"
                        : (ReferenceEquals(hoverTarget, _draggedSlot) ? $"拖拽中：「{_draggedSlot.Title}」" : $"与「{hoverTarget.Title}」互换");
                }
                else
                {
                    ActiveTitle = $"拖拽中：「{_draggedSlot.Title}」";
                }
                return;
            }
        }

        UpdateSelection(position);
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingInternalSlot && _draggedSlot != null)
        {
            var releasePos = e.GetPosition(this);
            var targetSlot = FindSlotAtPosition(releasePos);

            if (targetSlot != null && !ReferenceEquals(targetSlot, _draggedSlot))
            {
                SwapOrMoveSlots(_draggedSlot, targetSlot);
            }
            else
            {
                ActiveTitle = _draggedSlot.Title;
            }

            _isDraggingInternalSlot = false;
            _draggedSlot = null;
            e.Handled = true;
            return;
        }

        if (_draggedSlot != null && !_isDraggingInternalSlot)
        {
            var item = _draggedSlot;
            _draggedSlot = null;

            SetActiveItem(item);
            if (item.Command != null)
            {
                ExecuteCommand(item.Command);
            }
            else
            {
                OpenAddExtensionForSlot(item);
            }
            e.Handled = true;
            return;
        }

        _draggedSlot = null;
        _isDraggingInternalSlot = false;
    }

    private void OpenAddExtensionForSlot(RadialMenuItemViewModel item)
    {
        _isActionWindowOpen = true;
        var window = new AddExtensionWindow(_mainWindow);
        window.Closed += (_, _) =>
        {
            _isActionWindowOpen = false;
            if (window.ResultCommand != null)
            {
                AssignCommandToSlot(item, window.ResultCommand);
            }
        };
        window.Show();
        window.Activate();
    }

    private RadialMenuItemViewModel? FindSlotAtPosition(Point position)
    {
        var hit = this.InputHitTest(position);
        if (hit is Control control)
        {
            var parent = control;
            while (parent != null)
            {
                if (parent.DataContext is RadialMenuItemViewModel slotItem)
                {
                    return slotItem;
                }
                parent = parent.Parent as Control;
            }
        }
        return null;
    }

    private void SwapOrMoveSlots(RadialMenuItemViewModel source, RadialMenuItemViewModel target)
    {
        var sourceKey = source.OwnerPageId;
        var targetKey = target.OwnerPageId;

        var sourceExtId = source.Command?.ExtensionId ?? string.Empty;
        var targetExtId = target.Command?.ExtensionId ?? string.Empty;

        if (!_radialSettings.Slots.TryGetValue(sourceKey, out var sourceSlot))
        {
            sourceSlot = new RadialMenuSlotSettings();
            _radialSettings.Slots[sourceKey] = sourceSlot;
        }
        if (!_radialSettings.Slots.TryGetValue(targetKey, out var targetSlot))
        {
            targetSlot = new RadialMenuSlotSettings();
            _radialSettings.Slots[targetKey] = targetSlot;
        }

        sourceSlot.ExtensionId = targetExtId;
        targetSlot.ExtensionId = sourceExtId;

        _radialSettings.Save();
        LoadSlots();

        ActiveTitle = string.IsNullOrEmpty(targetExtId)
            ? $"已移动「{source.Title}」"
            : $"已互换「{source.Title}」与「{target.Title}」";
    }

    private void Slot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: RadialMenuItemViewModel item })
            return;

        var pressedPoint = e.GetCurrentPoint(this);
        if (pressedPoint.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            SetActiveItem(item);
            ShowSlotContextMenu(sender as Control, item);
            return;
        }

        if (pressedPoint.Properties.IsLeftButtonPressed)
        {
            _draggedSlot = item;
            _dragStartPoint = pressedPoint.Position;
            _isDraggingInternalSlot = false;
            e.Handled = true;
        }
    }

    private void ShowSlotContextMenu(Control? target, RadialMenuItemViewModel item)
    {
        if (target == null) return;

        var menu = new ContextMenu();
        _isContextMenuOpen = true;

        menu.Closed += (_, _) =>
        {
            _isContextMenuOpen = false;
            _lastContextMenuClosedAt = DateTime.UtcNow;
        };

        if (!item.IsEmpty && item.Command != null)
        {
            var launchItem = new MenuItem { Header = $"🚀 启动「{item.Title}」" };
            launchItem.Click += (_, _) => ExecuteCommand(item.Command);
            menu.Items.Add(launchItem);

            menu.Items.Add(new Separator());

            var editItem = new MenuItem { Header = "✏️ 修改插槽小程序..." };
            editItem.Click += (_, _) => OpenSlotActionWindow(item);
            menu.Items.Add(editItem);

            var copyItem = new MenuItem { Header = "📋 复制小程序" };
            copyItem.Click += (_, _) =>
            {
                _copiedSlotCommand = item.Command;
                _isCutSlot = false;
                _cutSourceItem = null;
                ActiveTitle = $"已复制：{item.Title}";
            };
            menu.Items.Add(copyItem);

            var cutItem = new MenuItem { Header = "✂️ 剪切小程序" };
            cutItem.Click += (_, _) =>
            {
                _copiedSlotCommand = item.Command;
                _isCutSlot = true;
                _cutSourceItem = item;
                ActiveTitle = $"已剪切：{item.Title}";
            };
            menu.Items.Add(cutItem);
        }
        else
        {
            var addItem = new MenuItem { Header = "➕ 添加小程序到此插槽..." };
            addItem.Click += (_, _) => OpenSlotActionWindow(item);
            menu.Items.Add(addItem);
        }

        if (_copiedSlotCommand != null)
        {
            var pasteItem = new MenuItem { Header = $"📥 粘贴「{_copiedSlotCommand.Title}」" };
            pasteItem.Click += (_, _) =>
            {
                AssignCommandToSlot(item, _copiedSlotCommand);
                if (_isCutSlot && _cutSourceItem != null && !ReferenceEquals(_cutSourceItem, item))
                {
                    RemoveCommandFromSlot(_cutSourceItem);
                    _isCutSlot = false;
                    _cutSourceItem = null;
                    _copiedSlotCommand = null;
                }
            };
            menu.Items.Add(pasteItem);
        }

        if (!item.IsEmpty && item.Command != null)
        {
            string? targetPath = null;
            if (!string.IsNullOrEmpty(item.Command.ApplicationName))
            {
                if (item.Command.ApplicationName.StartsWith("/"))
                    targetPath = item.Command.ApplicationName;
                else
                    targetPath = MacIconExtractor.GetApplicationPath(item.Command.ApplicationName);
            }

            if (!string.IsNullOrEmpty(targetPath))
            {
                var revealItem = new MenuItem { Header = "📁 在访达中显示" };
                revealItem.Click += (_, _) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start("open", $"-R \"{targetPath}\"");
                    }
                    catch { }
                };
                menu.Items.Add(revealItem);
            }

            menu.Items.Add(new Separator());

            var removeItem = new MenuItem { Header = "🗑️ 清空插槽" };
            removeItem.Click += (_, _) => RemoveCommandFromSlot(item);
            menu.Items.Add(removeItem);
        }

        menu.Open(target);
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
        _isActionWindowOpen = true;

        var window = new RadialSlotActionWindow(
            item.IsEmpty ? "搜索小程序 / 添加到插槽" : "修改插槽小程序",
            _mainWindow.GetRadialMenuCommandCandidates,
            command => AssignCommandToSlot(item, command),
            () => { }, // Sub-pages are not used in Grid panel
            () => RemoveCommandFromSlot(item),
            () => { },
            allowDeleteCommand: item.Command != null,
            allowDeleteChildPage: false);

        window.Closed += (_, _) =>
        {
            _isActionWindowOpen = false;
        };

        window.Show();
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
        _radialSettings.Save();
        LoadSlots();
        ActiveTitle = $"已设置：{command.Title}";
    }

    private void RemoveCommandFromSlot(RadialMenuItemViewModel item)
    {
        var slotKey = item.OwnerPageId;
        if (!_radialSettings.Slots.TryGetValue(slotKey, out var slot))
        {
            slot = new RadialMenuSlotSettings();
            _radialSettings.Slots[slotKey] = slot;
        }
        slot.ExtensionId = string.Empty;
        _radialSettings.Save();
        LoadSlots();
        ActiveTitle = "已清空插槽";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        HidePanel();
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        HidePanel();
        if (Application.Current is App app)
        {
            app.OpenSettings();
        }
    }

    private void MobileMessages_Click(object? sender, RoutedEventArgs e)
    {
        ActiveTitle = "手机消息暂未连接";
    }

    private void HubSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
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
            if (_radialSettings.Slots.TryGetValue(key, out var slot))
            {
                if (!string.IsNullOrEmpty(slot.ExtensionId))
                {
                    command = allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, slot.ExtensionId, StringComparison.OrdinalIgnoreCase));
                    if (command == null && slot.ExtensionId.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
                    {
                        var appName = slot.ExtensionId.Substring(4);
                        command = new CommandItem
                        {
                            ExtensionId = slot.ExtensionId,
                            Title = appName,
                            ActionKind = CommandActionKind.LaunchApplication,
                            ApplicationName = appName
                        };
                    }
                }
            }
            else if (i < defaultCommands.Count)
            {
                command = defaultCommands[i];
            }

            var vm = new RadialMenuItemViewModel(key, i, command, string.Empty, string.Empty, 0, 0, 0, RadialMenuRing.Inner);
            GlobalSlots.Add(vm);
            
            if (command != null && command.ActionKind == CommandActionKind.LaunchApplication && !string.IsNullOrEmpty(command.ApplicationName))
            {
                var cached = MacIconExtractor.GetCachedBitmap(command.ApplicationName);
                if (cached != null)
                {
                    vm.RealIcon = cached;
                }
                else
                {
                    LoadNativeIconForViewModelAsync(vm, command.ApplicationName);
                }
            }
        }

        // Load 12 Context Slots
        for (int i = 0; i < 12; i++)
        {
            var key = $"quickpanel_context_{i}";
            CommandItem? command = null;
            if (_radialSettings.Slots.TryGetValue(key, out var slot))
            {
                if (!string.IsNullOrEmpty(slot.ExtensionId))
                {
                    command = allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, slot.ExtensionId, StringComparison.OrdinalIgnoreCase));
                    if (command == null && slot.ExtensionId.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
                    {
                        var appName = slot.ExtensionId.Substring(4);
                        command = new CommandItem
                        {
                            ExtensionId = slot.ExtensionId,
                            Title = appName,
                            ActionKind = CommandActionKind.LaunchApplication,
                            ApplicationName = appName
                        };
                    }
                }
            }

            var vm = new RadialMenuItemViewModel(key, i, command, string.Empty, string.Empty, 0, 0, 0, RadialMenuRing.Outer);
            ContextSlots.Add(vm);
            
            if (command != null && command.ActionKind == CommandActionKind.LaunchApplication && !string.IsNullOrEmpty(command.ApplicationName))
            {
                var cached = MacIconExtractor.GetCachedBitmap(command.ApplicationName);
                if (cached != null)
                {
                    vm.RealIcon = cached;
                }
                else
                {
                    LoadNativeIconForViewModelAsync(vm, command.ApplicationName);
                }
            }
        }
    }
    
    private async void LoadNativeIconForViewModelAsync(RadialMenuItemViewModel vm, string path)
    {
        try
        {
            var bitmap = await Task.Run(() => MacIconExtractor.GetCachedBitmap(path));
            if (bitmap != null)
            {
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
        ActiveTitle = string.IsNullOrWhiteSpace(_activeItem.Title) ? "松开或右键可配置小程序" : _activeItem.Title;
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

        if (localX >= 42 && localX < 284)
        {
            if (localY >= 40 && localY < 250)
            {
                int col = (int)((localX - 44) / 58);
                int row = (int)((localY - 40) / 70);
                col = Math.Clamp(col, 0, 3);
                row = Math.Clamp(row, 0, 2);
                int index = row * 4 + col;
                if (index >= 0 && index < GlobalSlots.Count)
                    return GlobalSlots[index];
            }
            else if (localY >= 275 && localY < 485)
            {
                int col = (int)((localX - 44) / 58);
                int row = (int)((localY - 275) / 70);
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
        
        // Window bounds including shadow padding (40px on all sides)
        int windowWidth = 364;
        int windowHeight = 640;
        PanelLeft = 40;
        PanelTop = 40;

        // Calculate top-left of the PANEL on screen
        double panelScreenLeft = screenPoint.X - 142;
        double panelScreenTop = screenPoint.Y - 280;
        
        // Clamp PANEL inside screen bounds with 10px margin
        panelScreenLeft = Math.Clamp(panelScreenLeft, bounds.X + 10, bounds.X + bounds.Width - 284 - 10);
        panelScreenTop = Math.Clamp(panelScreenTop, bounds.Y + 10, bounds.Y + bounds.Height - 560 - 10);

        // Window position is panel position minus the shadow padding
        Position = new PixelPoint((int)panelScreenLeft - 40, (int)panelScreenTop - 40);
        
        Width = windowWidth;
        Height = windowHeight;
        OverlayWidth = windowWidth;
        OverlayHeight = windowHeight;
        
        SetRadialCenter(new Point(182, 320));
    }

    private void SetRadialCenter(Point center)
    {
        _radialCenter = center;
    }

    private bool IsOutsidePanel(Point position)
    {
        double localX = position.X - PanelLeft;
        double localY = position.Y - PanelTop;
        return localX < -30 || localX > 314 || localY < -30 || localY > 590;
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
