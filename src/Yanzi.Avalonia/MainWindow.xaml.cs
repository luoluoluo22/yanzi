using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RadialMenuService _radialMenuService;
    private readonly RadialMenuSettings _radialSettings;
    private readonly GlobalInputTriggerSettings _inputTriggerSettings;
    private readonly IGlobalInputTriggerListenerFactory _globalInputTriggerListenerFactory;
    private readonly ICommandActionExecutor _commandActionExecutor;
    private readonly object _inputTriggerListenerLock = new();
    private IGlobalInputTriggerListener? _globalInputTriggerListener;
    private int _inputTriggerListenerVersion;
    private bool _inputTriggerListenerShouldRun = true;
    private bool _isClosing;
    private QuickPanelWindow? _quickPanel;
    private LauncherWindow? _launcherWindow;

    public ClipboardMonitorService ClipboardMonitor { get; private set; } = null!;

    private bool _canPointerCancel;
    private RadialMenuActivationSource _activeActivationSource = RadialMenuActivationSource.Unknown;
    private RadialMenuItemViewModel? _activeItem;
    private Point _radialCenter = new(700, 700);
    private double _overlayWidth = 1400;
    private double _overlayHeight = 1400;
    
    private string _activeTitle = "取消";
    private bool _isExecuting;
    private bool _isContentVisible;
    private bool _isRadialMenuActive; // Logical visibility flag (replaces Hide/Show)
    private int _visibilityVersion;

    public bool IsContentVisible
    {
        get => _isContentVisible;
        private set => SetField(ref _isContentVisible, value);
    }

    public GlobalInputTriggerSettings InputTriggerSettings => _inputTriggerSettings;

    public bool IsServiceRunning
    {
        get
        {
            lock (_inputTriggerListenerLock)
            {
                return _globalInputTriggerListener?.IsRunning == true;
            }
        }
    }

    public void ToggleService()
    {
        lock (_inputTriggerListenerLock)
        {
            if (_globalInputTriggerListener == null)
                return;

            if (_globalInputTriggerListener.IsRunning)
            {
                _inputTriggerListenerShouldRun = false;
                _globalInputTriggerListener.Stop();
                Console.WriteLine("[service] Service paused via tray menu");
            }
            else
            {
                _inputTriggerListenerShouldRun = true;
                _globalInputTriggerListener.Start();
                Console.WriteLine("[service] Service started via tray menu");
            }
        }
    }

    public void RestartInputTriggerListener(bool shouldStart)
    {
        lock (_inputTriggerListenerLock)
        {
            if (_globalInputTriggerListener != null)
            {
                UnwireInputTriggerListener(_globalInputTriggerListener);
                _globalInputTriggerListener.Stop();
                _globalInputTriggerListener.Dispose();
            }

            _globalInputTriggerListener = CreateInputTriggerListener();
            _inputTriggerListenerShouldRun = shouldStart;
            _inputTriggerListenerVersion++;

            if (shouldStart)
            {
                _globalInputTriggerListener.Start();
                Console.WriteLine("[service] Listener restarted and started");
            }
            else
            {
                Console.WriteLine("[service] Listener restarted and kept stopped");
            }
        }

        RefreshSnippetAbbreviations();
    }


    public ObservableCollection<RadialMenuItemViewModel> Items => _radialMenuService.Items;
    public ObservableCollection<RadialMenuItemViewModel> OuterItems => _radialMenuService.OuterItems;
    public ObservableCollection<RadialMenuItemViewModel> ChildItems => _radialMenuService.ChildItems;
    public ObservableCollection<RadialMenuItemViewModel> GrandChildItems => _radialMenuService.GrandChildItems;
    public ObservableCollection<RadialSeparatorViewModel> MainSeparators => _radialMenuService.MainSeparators;
    public ObservableCollection<RadialSeparatorViewModel> OuterSeparators => _radialMenuService.OuterSeparators;
    public ObservableCollection<RadialSeparatorViewModel> ChildSeparators => _radialMenuService.ChildSeparators;
    public ObservableCollection<RadialSeparatorViewModel> GrandChildSeparators => _radialMenuService.GrandChildSeparators;

    public bool HasChildRing => _radialMenuService.HasChildRing;
    public bool HasGrandChildRing => _radialMenuService.HasGrandChildRing;
    public string ChildRingTitle => _radialMenuService.ChildRingTitle;
    public string GrandChildRingTitle => _radialMenuService.GrandChildRingTitle;
    public string PageTitle => _radialMenuService.PageTitle;
    public string CenterPrimaryText => _radialMenuService.CenterPrimaryText;

    public double ChildRingEllipseX => _radialMenuService.ChildRingEllipseX;
    public double ChildRingEllipseY => _radialMenuService.ChildRingEllipseY;
    public double ChildRingCenterEllipseX => _radialMenuService.ChildRingCenterEllipseX;
    public double ChildRingCenterEllipseY => _radialMenuService.ChildRingCenterEllipseY;
    public double ChildRingTitleX => _radialMenuService.ChildRingTitleX;
    public double ChildRingTitleY => _radialMenuService.ChildRingTitleY;
    public double GrandChildRingEllipseX => _radialMenuService.GrandChildRingEllipseX;
    public double GrandChildRingEllipseY => _radialMenuService.GrandChildRingEllipseY;
    public double GrandChildRingCenterEllipseX => _radialMenuService.GrandChildRingCenterEllipseX;
    public double GrandChildRingCenterEllipseY => _radialMenuService.GrandChildRingCenterEllipseY;
    public double GrandChildRingTitleX => _radialMenuService.GrandChildRingTitleX;
    public double GrandChildRingTitleY => _radialMenuService.GrandChildRingTitleY;
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

    public double MainOuterEllipseX => _radialCenter.X - 215;
    public double MainOuterEllipseY => _radialCenter.Y - 215;
    public double MainInnerEllipseX => _radialCenter.X - 135;
    public double MainInnerEllipseY => _radialCenter.Y - 135;
    public double CenterCircleX => _radialCenter.X - 32;
    public double CenterCircleY => _radialCenter.Y - 32;
    public double CenterTextX => _radialCenter.X - 80;
    public double CenterTextY => _radialCenter.Y - 14;
    public double ActiveTitleX => _radialCenter.X - 110;
    public double ActiveTitleY => _radialCenter.Y + 168;

    public string ActiveTitle
    {
        get => _activeTitle;
        private set => SetField(ref _activeTitle, value);
    }

    public MainWindow()
        : this(new DisabledGlobalInputTriggerListenerFactory(), new DisabledCommandActionExecutor())
    {
    }

    public MainWindow(IGlobalInputTriggerListenerFactory globalInputTriggerListenerFactory)
        : this(globalInputTriggerListenerFactory, new DisabledCommandActionExecutor())
    {
    }

    public MainWindow(
        IGlobalInputTriggerListenerFactory globalInputTriggerListenerFactory,
        ICommandActionExecutor commandActionExecutor)
    {
        InitializeComponent();
        _globalInputTriggerListenerFactory = globalInputTriggerListenerFactory;
        _commandActionExecutor = commandActionExecutor;
        
        _radialSettings = new RadialMenuSettings
        {
            Enabled = true,
            TriggerRightButtonLongPress = true,
            TriggerRightButtonDrag = true,
            RadiusPixels = 110,
            DeadZonePixels = 30,
            DragThresholdPixels = 30
        };

        _radialSettings.Pages.Add(new RadialMenuPageSettings { Id = "default", Name = "燕环" });

        _inputTriggerSettings = new GlobalInputTriggerSettings
        {
            LongPressThresholdMs = 400,
            DragThresholdPixels = 30,
            EnableSecondaryButtonLongPress = true,
            EnableSecondaryButtonDrag = true,
            EnableTrackpadGesture = true,
            TrackpadGestureFingerCount = 4,
            TrackpadGestureMode = TrackpadGestureModes.FingerMove,
            TrackpadGestureMoveThresholdPixels = 18,
            TrackpadGestureScrollThreshold = 3,
            TrackpadGestureResetMs = 700,
            TrackpadGestureReleaseDelayMs = 220,
            TrackpadGestureNormalizedThreshold = 0.025,
            EnableInputDiagnostics = true
        };

        _radialMenuService = new RadialMenuService(GetDefaultCommands, _radialSettings);
        _quickPanel = new QuickPanelWindow(_radialSettings, _commandActionExecutor, this);
        
        // Instantiate and start background Clipboard monitoring
        ClipboardMonitor = new ClipboardMonitorService(this);
        ClipboardMonitor.Start();
        
        _launcherWindow = new LauncherWindow(_commandActionExecutor, this);
        
        DataContext = this;

        IsContentVisible = false;
        Opacity = 0;
        _isRadialMenuActive = false;

        int startupListenerVersion;
        lock (_inputTriggerListenerLock)
        {
            _globalInputTriggerListener = CreateInputTriggerListener();
            _inputTriggerListenerShouldRun = true;
            _inputTriggerListenerVersion++;
            startupListenerVersion = _inputTriggerListenerVersion;
        }

        RefreshSnippetAbbreviations();
        StartInputTriggerListener(startupListenerVersion, "startup");

        Closed += MainWindow_Closed;
        Closed += (s, e) => {
            ClipboardMonitor.Stop();
            _quickPanel?.Close();
            _launcherWindow?.Close();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        
        Resources["BoolToColor"] = new FuncValueConverter<bool, Color>(isSelected => 
            Color.FromRgb(255, 255, 255));
        Resources["SelectedTitleBrush"] = new FuncValueConverter<bool, IBrush>(isSelected =>
            isSelected ? new SolidColorBrush(Color.Parse("#FF60A5FA")) : Brushes.White);
    }

    private IGlobalInputTriggerListener CreateInputTriggerListener()
    {
        var listener = _globalInputTriggerListenerFactory.Create(_inputTriggerSettings);
        listener.ActivationRequested += GlobalInputTriggerListener_ActivationRequested;
        listener.ActivationUpdated += GlobalInputTriggerListener_ActivationUpdated;
        listener.ActivationReleased += GlobalInputTriggerListener_ActivationReleased;
        listener.LauncherRequested += GlobalInputTriggerListener_LauncherRequested;
        listener.HotkeyTriggered += GlobalInputTriggerListener_HotkeyTriggered;
        return listener;
    }

    private void UnwireInputTriggerListener(IGlobalInputTriggerListener listener)
    {
        listener.ActivationRequested -= GlobalInputTriggerListener_ActivationRequested;
        listener.ActivationUpdated -= GlobalInputTriggerListener_ActivationUpdated;
        listener.ActivationReleased -= GlobalInputTriggerListener_ActivationReleased;
        listener.LauncherRequested -= GlobalInputTriggerListener_LauncherRequested;
        listener.HotkeyTriggered -= GlobalInputTriggerListener_HotkeyTriggered;
    }

    private void StartInputTriggerListener(int listenerVersion, string reason)
    {
        global::Yanzi.Avalonia.App.WriteLog($"MainWindow: Starting _globalInputTriggerListener immediately. reason={reason}");

        try
        {
            lock (_inputTriggerListenerLock)
            {
                if (_isClosing)
                {
                    global::Yanzi.Avalonia.App.WriteLog("MainWindow: Skipping listener start because window is closing.");
                    return;
                }

                if (!_inputTriggerListenerShouldRun)
                {
                    global::Yanzi.Avalonia.App.WriteLog("MainWindow: Skipping listener start because service is paused.");
                    return;
                }

                if (listenerVersion != _inputTriggerListenerVersion)
                {
                    global::Yanzi.Avalonia.App.WriteLog($"MainWindow: Skipping stale listener start. expected={listenerVersion}, current={_inputTriggerListenerVersion}");
                    return;
                }

                if (_globalInputTriggerListener == null)
                {
                    global::Yanzi.Avalonia.App.WriteLog("MainWindow: Skipping listener start because listener is null.");
                    return;
                }

                _globalInputTriggerListener.Start();
                global::Yanzi.Avalonia.App.WriteLog($"MainWindow: _globalInputTriggerListener.Start() completed. IsRunning={_globalInputTriggerListener.IsRunning}");
            }
        }
        catch (Exception ex)
        {
            global::Yanzi.Avalonia.App.WriteLog($"MainWindow ERROR: _globalInputTriggerListener.Start() failed: {ex.GetType().Name} - {ex.Message}\nStack:{ex.StackTrace}");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        lock (_inputTriggerListenerLock)
        {
            _isClosing = true;
            _inputTriggerListenerShouldRun = false;
            _inputTriggerListenerVersion++;

            if (_globalInputTriggerListener != null)
            {
                UnwireInputTriggerListener(_globalInputTriggerListener);
                _globalInputTriggerListener.Stop();
                _globalInputTriggerListener.Dispose();
                _globalInputTriggerListener = null;
            }
        }
    }

    private IEnumerable<CommandItem> GetDefaultCommands(string pageId)
    {
        yield return Shortcut("copy", "复制", "⌘C", "c");
        yield return Shortcut("paste", "粘贴", "⌘V", "v");
        yield return Shortcut("cut", "剪切", "⌘X", "x");
        yield return Shortcut("select-all", "全选", "⌘A", "a");
        yield return App("yanzi-web", "燕子官网", "🌐", "https://yanzi.luoluoluo.cc.cd");
        yield return App("微信", "微信", "💬", "WeChat");
        yield return App("terminal", "终端", "💻", "Terminal");
        yield return App("safari", "Safari", "🧭", "Safari");
        yield return App("notes", "备忘录", "📝", "Notes");
        yield return App("music", "网易云音乐", "🎵", "NeteaseMusic");
        yield return App("finder", "访达", "📁", "Finder");
        yield return Shortcut("screenshot", "截图", "📸", "4", shift: true);
    }

    private static CommandItem Shortcut(
        string id,
        string title,
        string glyph,
        string key,
        bool command = true,
        bool shift = false,
        bool option = false,
        bool control = false)
    {
        return new CommandItem
        {
            ExtensionId = id,
            Title = title,
            Glyph = glyph,
            ActionKind = CommandActionKind.KeyboardShortcut,
            ShortcutKey = key,
            ShortcutCommand = command,
            ShortcutShift = shift,
            ShortcutOption = option,
            ShortcutControl = control
        };
    }

    private static CommandItem App(string id, string title, string glyph, string applicationName)
    {
        return new CommandItem
        {
            ExtensionId = id,
            Title = title,
            Glyph = glyph,
            ActionKind = CommandActionKind.LaunchApplication,
            ApplicationName = applicationName
        };
    }

    public void ShowQuickPanelFromTray()
    {
        _quickPanel?.ShowPanel(null);
    }

    public void ShowLauncherFromTray()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowLauncherFromTray);
            return;
        }

        if (_launcherWindow != null)
        {
            _launcherWindow.Show();
            _launcherWindow.Activate();
            _launcherWindow.FindControl<TextBox>("SearchInput")?.Focus();
        }
    }

    private void GlobalInputTriggerListener_LauncherRequested(object? sender, EventArgs e)
    {
        ShowLauncher();
    }

    private void GlobalInputTriggerListener_HotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Hotkey))
            return;

        var normalizedPressed = NormalizeHotkey(e.Hotkey);

        // Gather all default and custom command items
        var commands = new List<CommandItem>();
        
        if (_launcherWindow != null)
        {
            commands.AddRange(_launcherWindow.GetCustomExtensions());
        }

        commands.AddRange(GetRadialMenuCommandCandidates(string.Empty));

        var match = commands.FirstOrDefault(cmd => 
            !string.IsNullOrEmpty(cmd.GlobalHotkey) && 
            NormalizeHotkey(cmd.GlobalHotkey) == normalizedPressed);

        if (match != null)
        {
            e.Handled = true;
            
            // Execute on background task so we do not block event tap thread
            Task.Run(() =>
            {
                try
                {
                    _commandActionExecutor.Execute(match);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Global hotkey execution failed: {ex.Message}");
                }
            });
        }
    }

    private static string NormalizeHotkey(string hotkey)
    {
        var parts = hotkey.Trim().ToLowerInvariant().Split('+');
        var modifiers = new HashSet<string>();
        string key = string.Empty;

        foreach (var p in parts)
        {
            if (p == "cmd" || p == "command" || p == "⌘") modifiers.Add("cmd");
            else if (p == "alt" || p == "opt" || p == "option" || p == "⌥") modifiers.Add("alt");
            else if (p == "ctrl" || p == "control" || p == "⌃") modifiers.Add("ctrl");
            else if (p == "shift" || p == "⇧") modifiers.Add("shift");
            else key = p;
        }

        var sorted = new List<string>();
        if (modifiers.Contains("cmd")) sorted.Add("cmd");
        if (modifiers.Contains("ctrl")) sorted.Add("ctrl");
        if (modifiers.Contains("alt")) sorted.Add("alt");
        if (modifiers.Contains("shift")) sorted.Add("shift");
        if (!string.IsNullOrEmpty(key)) sorted.Add(key);

        return string.Join("+", sorted);
    }

    public void ShowLauncher()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowLauncher);
            return;
        }

        if (_launcherWindow == null)
            return;

        if (_launcherWindow.IsVisible)
        {
            _launcherWindow.Hide();
        }
        else
        {
            _launcherWindow.Show();
            _launcherWindow.Activate();
            _launcherWindow.FindControl<TextBox>("SearchInput")?.Focus();
        }
    }

    private void GlobalInputTriggerListener_ActivationRequested(object? sender, RadialMenuActivationEventArgs e)
    {
        if (e.IsLongPress)
        {
            _quickPanel?.ShowPanel(e);
        }
        else
        {
            ShowRadialMenu(e);
        }
    }

    private void GlobalInputTriggerListener_ActivationReleased(object? sender, RadialMenuActivationEventArgs e)
    {
        if (_quickPanel != null && _quickPanel.IsVisible)
        {
            // Do not close the quick panel when releasing right click hold
        }
        else
        {
            ExecuteSelectedFromHoldRelease(e);
        }
    }

    private void GlobalInputTriggerListener_ActivationUpdated(object? sender, RadialMenuActivationEventArgs e)
    {
        if (_quickPanel != null && _quickPanel.IsVisible)
        {
            _quickPanel.UpdateSelectionFromActivation(e);
        }
        else
        {
            UpdateSelectionFromActivation(e);
        }
    }

    private void ShowRadialMenu(RadialMenuActivationEventArgs? activation = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowRadialMenu(activation));
            return;
        }

        Console.WriteLine($"[ui-perf] {DateTime.UtcNow:HH:mm:ss.fff} ShowRadialMenu. Source={activation?.Source}, ScreenX={activation?.ScreenX}, ScreenY={activation?.ScreenY}");

        _isExecuting = false;
        _canPointerCancel = false;
        _activeActivationSource = activation?.Source ?? RadialMenuActivationSource.Unknown;
        ClearActiveItem();
        var visibilityVersion = ++_visibilityVersion;

        Opacity = 0;
        IsContentVisible = false;

        PrepareOverlayForActivation(activation);
        _radialMenuService.BuildItems(_radialSettings.RadiusPixels, Width, Height, _radialCenter.X, _radialCenter.Y);
        NotifyRadialBindings();
        ActiveTitle = "取消";

        if (!IsVisible)
            Show();

        _isRadialMenuActive = true;
        IsContentVisible = true;
        Dispatcher.UIThread.Post(() => RevealRadialMenu(visibilityVersion), DispatcherPriority.Loaded);

        Console.WriteLine($"[ui-perf] {DateTime.UtcNow:HH:mm:ss.fff} Radial menu staged at center={_radialCenter.X:0},{_radialCenter.Y:0}");
    }

    private void RevealRadialMenu(int visibilityVersion)
    {
        if (!_isRadialMenuActive || visibilityVersion != _visibilityVersion)
            return;

        Opacity = 1;
        Topmost = true;
        Console.WriteLine($"[ui-perf] {DateTime.UtcNow:HH:mm:ss.fff} Radial menu revealed");
    }

    private void ExecuteSelectedFromHoldRelease(RadialMenuActivationEventArgs? activation = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ExecuteSelectedFromHoldRelease(activation));
            return;
        }

        if (_isExecuting)
            return;

        _isExecuting = true;
        if (_activeItem?.HasChildPage == true)
        {
            BuildNestedRingForItem(_activeItem);
            NotifyRadialBindings();
            _isExecuting = false;
            return;
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

        HideRadialMenu();
        
        ActiveTitle = "取消";
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            if (IsOutsideRadialMenu(pointer.Position))
            {
                HideRadialMenu();
                e.Handled = true;
                return;
            }

            _radialMenuService.ReturnToParentPage();
            _radialMenuService.ClearChildRing();
            _radialMenuService.BuildItems(_radialSettings.RadiusPixels, Width, Height, _radialCenter.X, _radialCenter.Y);
            NotifyRadialBindings();
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isRadialMenuActive)
            return;

        var position = e.GetPosition(this);
        if (_activeActivationSource == RadialMenuActivationSource.TrackpadGesture)
            return;

        if (!_canPointerCancel)
        {
            _canPointerCancel = HasMovedAwayFromCenter(position);
            return;
        }

        if (IsInCenterCancelZone(position) || IsOutsideRadialMenu(position))
        {
            HideRadialMenu();
            e.Handled = true;
        }
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
    }

    private void RadialSlot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: RadialMenuItemViewModel item })
            return;

        e.Handled = true;
        SetActiveItem(item);

        var pressedPoint = e.GetCurrentPoint(this);
        if (pressedPoint.Properties.IsRightButtonPressed && _activeActivationSource == RadialMenuActivationSource.TrackpadGesture)
        {
            OpenSlotActionWindow(item);
            return;
        }

        if (item.Command != null)
        {
            ExecuteCommand(item.Command);
        }
        else if (item.HasChildPage)
        {
            BuildNestedRingForItem(item);
            NotifyRadialBindings();
        }
    }

    private void RadialSlot_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border { DataContext: RadialMenuItemViewModel item })
        {
            SetActiveItem(item);
            ActiveTitle = GetItemPrompt(item);
        }
    }

    private void RadialSlot_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border { DataContext: RadialMenuItemViewModel item })
        {
            if (ReferenceEquals(_activeItem, item))
                ClearActiveItem();
        }
    }

    private string GetItemPrompt(RadialMenuItemViewModel item)
    {
        if (item.HasChildPage)
            return string.IsNullOrWhiteSpace(item.Title) ? "松开可展开" : item.Title;

        return string.IsNullOrWhiteSpace(item.Title) ? "松开可新建" : item.Title;
    }

    private bool CreateChildPageForEmptySlot(RadialMenuItemViewModel item)
    {
        if (item.Command != null || item.HasChildPage)
            return false;

        var pageName = GetNextChildPageName();
        var pageId = $"child-{pageName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)}-{item.OwnerPageId}-{item.Index}";
        if (_radialSettings.Pages.All(page => !page.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase)))
        {
            _radialSettings.Pages.Add(new RadialMenuPageSettings { Id = pageId, Name = pageName });
        }

        var slotKey = $"{item.OwnerPageId}_{item.Index}";
        if (!_radialSettings.Slots.TryGetValue(slotKey, out var slot))
        {
            slot = new RadialMenuSlotSettings();
            _radialSettings.Slots[slotKey] = slot;
        }

        slot.ChildPageId = pageId;
        RefreshRadialState(item.OwnerPageId, item.Index, $"已新建：{pageName}");
        return true;
    }

    private void OpenSlotActionWindow(RadialMenuItemViewModel item)
    {
        var window = new RadialSlotActionWindow(
            item.IsEmpty ? "搜索扩展或新建子环" : "修改槽位",
            GetRadialMenuCommandCandidates,
            command => AssignCommandToSlot(item.OwnerPageId, item.Index, command),
            () => CreateChildPageForSlot(item.OwnerPageId, item.Index),
            () => RemoveCommandFromSlot(item.OwnerPageId, item.Index),
            () => RemoveChildPageFromSlot(item.OwnerPageId, item.Index),
            allowDeleteCommand: item.Command != null,
            allowDeleteChildPage: item.HasChildPage);

        window.Show(this);
        window.Activate();
    }

    public IReadOnlyList<CommandItem> GetRadialMenuCommandCandidates(string keyword)
    {
        var commands = GetDefaultCommands(_radialMenuService.PageTitle).ToList();
        if (string.IsNullOrWhiteSpace(keyword))
            return commands;

        var query = keyword.Trim();
        return commands.Where(command =>
            Contains(command.Title, query) ||
            Contains(command.Glyph, query) ||
            Contains(command.Description, query) ||
            Contains(command.ApplicationName, query) ||
            Contains(command.ExtensionId, query)).ToList();
    }

    private void AssignCommandToSlot(string pageId, int index, CommandItem command)
    {
        var slot = EnsureSlot(pageId, index);
        slot.ExtensionId = command.ExtensionId;
        RefreshRadialState(pageId, index, $"已设置：{command.Title}");
    }

    private void RemoveCommandFromSlot(string pageId, int index)
    {
        var slot = EnsureSlot(pageId, index);
        slot.ExtensionId = null;
        RefreshRadialState(pageId, index, "已删除扩展");
    }

    private void RemoveChildPageFromSlot(string pageId, int index)
    {
        var slot = EnsureSlot(pageId, index);
        var removedPageId = slot.ChildPageId;
        slot.ChildPageId = null;

        if (!string.IsNullOrWhiteSpace(removedPageId))
        {
            var isReferencedElsewhere = _radialSettings.Slots
                .Where(entry => !entry.Key.Equals($"{pageId}_{index}", StringComparison.OrdinalIgnoreCase))
                .Any(entry => string.Equals(entry.Value.ChildPageId, removedPageId, StringComparison.OrdinalIgnoreCase));
            if (!isReferencedElsewhere)
            {
                _radialSettings.Pages.RemoveAll(page => page.Id.Equals(removedPageId, StringComparison.OrdinalIgnoreCase));
            }
        }

        RefreshRadialState(pageId, index, "已删除子环");
    }

    private void CreateChildPageForSlot(string pageId, int index)
    {
        var slot = EnsureSlot(pageId, index);
        if (!string.IsNullOrWhiteSpace(slot.ChildPageId))
        {
            RefreshRadialState(pageId, index, "子环已存在");
            return;
        }

        var pageName = GetNextChildPageName();
        var pageIdValue = $"child-{pageName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)}-{pageId}-{index}";
        _radialSettings.Pages.Add(new RadialMenuPageSettings { Id = pageIdValue, Name = pageName });
        slot.ChildPageId = pageIdValue;
        RefreshRadialState(pageId, index, $"已新建：{pageName}");
    }

    private RadialMenuSlotSettings EnsureSlot(string pageId, int index)
    {
        var slotKey = $"{pageId}_{index}";
        if (!_radialSettings.Slots.TryGetValue(slotKey, out var slot))
        {
            slot = new RadialMenuSlotSettings();
            _radialSettings.Slots[slotKey] = slot;
        }

        return slot;
    }

    private void RefreshRadialState(string pageId, int index, string message)
    {
        _radialMenuService.BuildItems(_radialSettings.RadiusPixels, Width, Height, _radialCenter.X, _radialCenter.Y);
        var item = FindSlotItem(pageId, index);
        if (item != null)
        {
            SetActiveItem(item);
            if (item.HasChildPage)
            {
                BuildNestedRingForItem(item);
            }
        }

        NotifyRadialBindings();
        ActiveTitle = message;
    }

    private RadialMenuItemViewModel? FindSlotItem(string pageId, int index)
    {
        return Items.FirstOrDefault(item => item.Index == index && item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase))
            ?? OuterItems.FirstOrDefault(item => item.Index == index && item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase))
            ?? ChildItems.FirstOrDefault(item => item.Index == index && item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase))
            ?? GrandChildItems.FirstOrDefault(item => item.Index == index && item.OwnerPageId.Equals(pageId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Contains(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private string GetNextChildPageName()
    {
        var usedNumbers = _radialSettings.Pages
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

    private async void HideRadialMenu()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HideRadialMenu);
            return;
        }

        var visibilityVersion = ++_visibilityVersion;
        _isRadialMenuActive = false;
        IsContentVisible = false;
        Opacity = 0;
        
        _canPointerCancel = false;
        _activeActivationSource = RadialMenuActivationSource.Unknown;
        ClearActiveItem();
        _radialMenuService.ClearChildRing();
        NotifyRadialBindings();

        // Wait 50ms to ensure the window has rendered at least one completely transparent frame,
        // so that macOS caches a fully transparent texture for this window.
        await Task.Delay(50);

        if (!_isRadialMenuActive && visibilityVersion == _visibilityVersion)
        {
            Hide();
        }
    }

    private void UpdateSelectionFromActivation(RadialMenuActivationEventArgs activation)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateSelectionFromActivation(activation));
            return;
        }

        if (!_isRadialMenuActive || !activation.HasScreenPosition)
            return;

        var position = GetLocalActivationPoint(activation);
        if (!_canPointerCancel)
            _canPointerCancel = HasMovedAwayFromCenter(position);

        if (TryUpdateGrandChildSelection(position))
            return;

        if (TryUpdateChildSelection(position))
            return;

        if (activation.Source == RadialMenuActivationSource.TrackpadGesture)
        {
            if (IsInCenterCancelZone(position) || IsOutsideInteractiveArea(position))
            {
                SetActiveItem(null);
                return;
            }

            UpdateMainSelection(position);
            return;
        }

        if (_canPointerCancel && (IsInCenterCancelZone(position) || IsOutsideInteractiveArea(position)))
        {
            HideRadialMenu();
            return;
        }

        UpdateMainSelection(position);
    }

    private void PrepareOverlayForActivation(RadialMenuActivationEventArgs? activation)
    {
        var screenPoint = activation?.HasScreenPosition == true
            ? new Point(activation.ScreenX!.Value, activation.ScreenY!.Value)
            : new Point(Position.X + Width / 2, Position.Y + Height / 2);

        var screen = Screens.ScreenFromPoint(ToPixelPoint(screenPoint)) ??
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

        if (_inputTriggerSettings.EnableInputDiagnostics)
        {
            Console.WriteLine(
                $"[ui] activation={activation?.Source} raw={screenPoint.X:0},{screenPoint.Y:0} " +
                $"screen={bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height} scale={screen.Scaling:0.##} " +
                $"center={_radialCenter.X:0},{_radialCenter.Y:0}");
        }
    }

    private Point GetLocalActivationPoint(RadialMenuActivationEventArgs activation)
    {
        return new Point(activation.ScreenX!.Value - Position.X, activation.ScreenY!.Value - Position.Y);
    }

    private static PixelPoint ToPixelPoint(Point point)
    {
        return new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private void SetRadialCenter(Point center)
    {
        _radialCenter = center;
        OnPropertyChanged(nameof(MainOuterEllipseX));
        OnPropertyChanged(nameof(MainOuterEllipseY));
        OnPropertyChanged(nameof(MainInnerEllipseX));
        OnPropertyChanged(nameof(MainInnerEllipseY));
        OnPropertyChanged(nameof(CenterCircleX));
        OnPropertyChanged(nameof(CenterCircleY));
        OnPropertyChanged(nameof(CenterTextX));
        OnPropertyChanged(nameof(CenterTextY));
        OnPropertyChanged(nameof(ActiveTitleX));
        OnPropertyChanged(nameof(ActiveTitleY));
    }

    private void NotifyRadialBindings()
    {
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(OuterItems));
        OnPropertyChanged(nameof(ChildItems));
        OnPropertyChanged(nameof(GrandChildItems));
        OnPropertyChanged(nameof(MainSeparators));
        OnPropertyChanged(nameof(OuterSeparators));
        OnPropertyChanged(nameof(ChildSeparators));
        OnPropertyChanged(nameof(GrandChildSeparators));
        OnPropertyChanged(nameof(HasChildRing));
        OnPropertyChanged(nameof(HasGrandChildRing));
        OnPropertyChanged(nameof(ChildRingTitle));
        OnPropertyChanged(nameof(GrandChildRingTitle));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CenterPrimaryText));
        OnPropertyChanged(nameof(ChildRingEllipseX));
        OnPropertyChanged(nameof(ChildRingEllipseY));
        OnPropertyChanged(nameof(ChildRingCenterEllipseX));
        OnPropertyChanged(nameof(ChildRingCenterEllipseY));
        OnPropertyChanged(nameof(ChildRingTitleX));
        OnPropertyChanged(nameof(ChildRingTitleY));
        OnPropertyChanged(nameof(GrandChildRingEllipseX));
        OnPropertyChanged(nameof(GrandChildRingEllipseY));
        OnPropertyChanged(nameof(GrandChildRingCenterEllipseX));
        OnPropertyChanged(nameof(GrandChildRingCenterEllipseY));
        OnPropertyChanged(nameof(GrandChildRingTitleX));
        OnPropertyChanged(nameof(GrandChildRingTitleY));
    }

    private void BuildNestedRingForItem(RadialMenuItemViewModel item)
    {
        if (item.Ring == RadialMenuRing.Child)
        {
            _radialMenuService.BuildGrandChildRing(item, Width, Height);
            return;
        }

        _radialMenuService.BuildChildRing(item, Width, Height, _radialCenter.X, _radialCenter.Y);
    }

    private void UpdateMainSelection(Point position)
    {
        var item = FindMainRingItem(position);
        SetActiveItem(item);

        if (item?.HasChildPage == true)
        {
            BuildNestedRingForItem(item);
        }
        else
        {
            _radialMenuService.ClearChildRing();
        }

        NotifyRadialBindings();
    }

    private bool TryUpdateChildSelection(Point position)
    {
        if (!HasChildRing)
            return false;

        var dx = position.X - _radialMenuService.ChildRingCenterX;
        var dy = position.Y - _radialMenuService.ChildRingCenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 150)
        {
            return false;
        }

        if (distance < 26)
        {
            SetActiveItem(null);
            ActiveTitle = "返回上一级";
            return true;
        }

        var item = FindRingItem(position, _radialMenuService.ChildRingCenterX, _radialMenuService.ChildRingCenterY, ChildItems, 8, 34, 128);
        SetActiveItem(item);
        if (item?.HasChildPage == true)
        {
            _radialMenuService.BuildGrandChildRing(item, Width, Height);
        }
        else
        {
            _radialMenuService.ClearGrandChildRing();
        }

        NotifyRadialBindings();
        return true;
    }

    private bool TryUpdateGrandChildSelection(Point position)
    {
        if (!HasGrandChildRing)
            return false;

        var dx = position.X - _radialMenuService.GrandChildRingCenterX;
        var dy = position.Y - _radialMenuService.GrandChildRingCenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 138)
        {
            return false;
        }

        if (distance < 24)
        {
            SetActiveItem(null);
            ActiveTitle = "返回上一级";
            return true;
        }

        var item = FindRingItem(position, _radialMenuService.GrandChildRingCenterX, _radialMenuService.GrandChildRingCenterY, GrandChildItems, 8, 27, 98);
        SetActiveItem(item);
        NotifyRadialBindings();
        return true;
    }

    private void ExecuteCommand(CommandItem command)
    {
        _isExecuting = true;
        HideRadialMenu();
        ActiveTitle = "取消";
        _ = ExecuteCommandAfterOverlaySettlesAsync(command);
    }

    private async Task ExecuteCommandAfterOverlaySettlesAsync(CommandItem command)
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
        ActiveTitle = GetItemPrompt(_activeItem);
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

    private RadialMenuItemViewModel? FindRadialItemAt(Point position)
    {
        if (HasGrandChildRing)
        {
            var grandChild = FindRingItem(position, _radialMenuService.GrandChildRingCenterX, _radialMenuService.GrandChildRingCenterY, GrandChildItems, 8, 27, 98);
            if (grandChild != null)
                return grandChild;
        }

        if (HasChildRing)
        {
            var child = FindRingItem(position, _radialMenuService.ChildRingCenterX, _radialMenuService.ChildRingCenterY, ChildItems, 8, 34, 128);
            if (child != null)
                return child;
        }

        return FindMainRingItem(position);
    }

    private RadialMenuItemViewModel? FindMainRingItem(Point position)
    {
        var centerX = _radialCenter.X;
        var centerY = _radialCenter.Y;
        var dx = position.X - centerX;
        var dy = position.Y - centerY;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance is < 44 or > 225)
            return null;

        return distance < 136
            ? FindRingItem(position, centerX, centerY, Items, 8, 44, 136)
            : FindRingItem(position, centerX, centerY, OuterItems, 16, 136, 225);
    }

    private static RadialMenuItemViewModel? FindRingItem(
        Point position,
        double centerX,
        double centerY,
        IReadOnlyList<RadialMenuItemViewModel> items,
        int count,
        double innerRadius,
        double outerRadius)
    {
        if (items.Count == 0)
            return null;

        var dx = position.X - centerX;
        var dy = position.Y - centerY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < innerRadius || distance > outerRadius)
            return null;

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var normalized = NormalizeDegrees(angle + 90);
        var index = (int)Math.Round(normalized / (360.0 / count)) % count;
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    private bool IsOutsideRadialMenu(Point position)
    {
        var centerX = _radialCenter.X;
        var centerY = _radialCenter.Y;
        var dx = position.X - centerX;
        var dy = position.Y - centerY;
        const double closeRadius = 230;
        return dx * dx + dy * dy > closeRadius * closeRadius;
    }

    private bool IsOutsideInteractiveArea(Point position)
    {
        if (!IsOutsideRadialMenu(position))
            return false;

        if (HasGrandChildRing)
        {
            var grandChildDx = position.X - _radialMenuService.GrandChildRingCenterX;
            var grandChildDy = position.Y - _radialMenuService.GrandChildRingCenterY;
            const double grandChildRadius = 110;
            if (grandChildDx * grandChildDx + grandChildDy * grandChildDy <= grandChildRadius * grandChildRadius)
                return false;
        }

        if (!HasChildRing)
            return true;

        var childDx = position.X - _radialMenuService.ChildRingCenterX;
        var childDy = position.Y - _radialMenuService.ChildRingCenterY;
        const double childRadius = 132;
        return childDx * childDx + childDy * childDy > childRadius * childRadius;
    }

    private bool IsInCenterCancelZone(Point position)
    {
        var centerX = _radialCenter.X;
        var centerY = _radialCenter.Y;
        var dx = position.X - centerX;
        var dy = position.Y - centerY;
        const double cancelRadius = 44;
        return dx * dx + dy * dy <= cancelRadius * cancelRadius;
    }

    private bool HasMovedAwayFromCenter(Point position)
    {
        var centerX = _radialCenter.X;
        var centerY = _radialCenter.Y;
        var dx = position.X - centerX;
        var dy = position.Y - centerY;
        const double armRadius = 72;
        return dx * dx + dy * dy > armRadius * armRadius;
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    public void RefreshSnippetAbbreviations()
    {
        var abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_launcherWindow != null)
        {
            var customExts = _launcherWindow.GetCustomExtensions();
            foreach (var ext in customExts)
            {
                if (!string.IsNullOrEmpty(ext.Abbreviation) && !string.IsNullOrEmpty(ext.SnippetText))
                {
                    abbreviations[ext.Abbreviation.Trim()] = ext.SnippetText;
                }
            }
        }

        lock (_inputTriggerListenerLock)
        {
            if (_globalInputTriggerListener == null)
                return;

            _globalInputTriggerListener.UpdateAbbreviations(abbreviations);
        }

        Console.WriteLine($"[snippet] Refreshed {abbreviations.Count} abbreviation mappings globally.");
    }
}

public class FuncValueConverter<TInput, TOutput> : IValueConverter
{
    private readonly Func<TInput, TOutput> _convert;

    public FuncValueConverter(Func<TInput, TOutput> convert)
    {
        _convert = convert;
    }

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return _convert((TInput)value!);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
