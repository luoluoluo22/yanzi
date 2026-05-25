using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.IO;
using System.Net.NetworkInformation;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;
using WpfCursors = System.Windows.Input.Cursors;

namespace OpenQuickHost;

public partial class YanmOverlayWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly YanmBridgeService _yanmBridgeService;
    private bool _isPinned;
    private bool _isInteractiveHoldPinned;
    private bool _isEditMode;
    private bool _isSelecting;
    private bool _isMovingComponent;
    private bool _isResizingComponent;
    private WpfPoint _selectionStart;
    private WpfPoint _moveStartPoint;
    private WpfPoint _moveStartComponentPoint;
    private WpfPoint _resizeStartPoint;
    private WpfSize _resizeStartSize;
    private string _movingComponentId = string.Empty;
    private string _resizingComponentId = string.Empty;
    private string _selectedComponentId = string.Empty;
    private YanmSettings _settings = new();
    private readonly Dictionary<string, YanmComponentView> _componentViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingComponentState = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Threading.DispatcherTimer? _componentStateSaveTimer;
    private System.Windows.Threading.DispatcherTimer? _webDavVisibleRefreshTimer;
    private System.Windows.Threading.DispatcherTimer? _webDavLocalChangeSyncTimer;
    private bool _webDavStateRefreshRunning;
    private bool _webDavStateRefreshPending;
    private bool _webDavStateRefreshPendingForce;
    private string _webDavStateRefreshPendingReason = string.Empty;
    private DateTime _lastWebDavStateRefreshUtc = DateTime.MinValue;
    private bool _interactiveOutsideClickCandidate;
    private WpfPoint _interactiveOutsideClickStart;
    private bool _isWebView2Available = true;
    private CoreWebView2Environment? _webView2Environment;
    private const double ComponentMoveHandleHeight = 24;
    private static readonly TimeSpan WebDavStateRefreshCooldown = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WebDavVisibleRefreshInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan WebDavLocalChangeSyncDelay = TimeSpan.FromSeconds(2);
    public YanmOverlayWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _yanmBridgeService = new YanmBridgeService(
            getAllCommands: () => _mainWindow.GetAllCommands(),
            findCurrentComponent: FindCurrentComponent,
            getComponentState: GetComponentStateValue,
            sendComponentState: SendComponentState,
            sendSystemInfo: SendSystemInfoToComponent,
            queueComponentStateSave: QueueComponentStateSave,
            sendReply: SendComponentReply,
            executeCommandExternally: _mainWindow.ExecuteCommandExternally,
            log: HostAssets.AppendLog);
        InitializeComponent();
        UpdateDynamicTexts();
        SourceInitialized += (_, _) => ApplyScreenBounds();
        Closing += (_, _) => FlushPendingComponentState();
        Deactivated += (_, _) =>
        {
            if (_isPinned)
            {
                Topmost = true;
            }
        };
    }

    public void ShowTemporary()
    {
        if (_isPinned)
        {
            HostAssets.AppendLog("Yanm: temporary show skipped because overlay is pinned.");
            return;
        }

        if (_isInteractiveHoldPinned && IsVisible)
        {
            HostAssets.AppendLog("Yanm: temporary show skipped because component interaction is active.");
            return;
        }

        HostAssets.AppendLog("Yanm: temporary show requested.");
        ShowOverlay(pinned: false);
    }

    public void ToggleFromShortcut()
    {
        if (IsVisible)
        {
            HostAssets.AppendLog("Yanm: shortcut toggle requested hide.");
            HideOverlay();
            return;
        }

        HostAssets.AppendLog("Yanm: shortcut toggle requested show.");
        ShowOverlay(pinned: false);
    }

    public void HideTemporary()
    {
        if (_isInteractiveHoldPinned)
        {
            HostAssets.AppendLog("Yanm: temporary hide skipped because component interaction is active.");
            return;
        }

        if (!_isPinned && (_isEditMode || _isSelecting))
        {
            _isPinned = true;
            _isEditMode = true;
            HintText.Text = BuildEditModeHint();
            UpdateCornerHint();
            HostAssets.AppendLog($"Yanm: temporary hide converted to pinned edit mode, selecting={_isSelecting}.");
            return;
        }

        if (!_isPinned)
        {
            HostAssets.AppendLog("Yanm: temporary hide requested.");
            HideOverlay();
        }
    }

    public void TogglePinned()
    {
        if (_isPinned)
        {
            HostAssets.AppendLog("Yanm: unpin requested.");
            HideOverlay();
            return;
        }

        HostAssets.AppendLog("Yanm: pin requested.");
        ShowOverlay(pinned: true);
    }

    public void ReloadSettings()
    {
        _settings = AppSettingsStore.Load().Yanm ?? new YanmSettings();
        if (!_settings.Enabled)
        {
            HideOverlay();
            HostAssets.AppendLog("Yanm: hidden because feature was disabled from settings.");
            return;
        }

        if (IsVisible)
        {
            ApplyScreenBounds();
            UpdateDynamicTexts();
            RenderAll();
        }
    }

    private void ShowOverlay(bool pinned)
    {
        _settings = AppSettingsStore.Load().Yanm ?? new YanmSettings();
        if (!_settings.Enabled)
        {
            HostAssets.AppendLog("Yanm: show skipped because feature is disabled.");
            return;
        }

        _isPinned = pinned;
        _isInteractiveHoldPinned = false;
        ResetInteractionState(clearEditMode: false);
        ApplyScreenBounds();
        UpdateDynamicTexts();
        _isWebView2Available = CheckWebView2RuntimeAvailable();
        Root.Background = new SolidColorBrush(WpfColor.FromArgb(
            (byte)Math.Clamp(_settings.OverlayOpacity * 255, 32, 217),
            1,
            3,
            8));
        HintText.Text = pinned
            ? BuildPinnedHint()
            : BuildTemporaryHint();
        _isEditMode = pinned;
        UpdateCornerHint();
        HostAssets.AppendLog($"Yanm: showing overlay, pinned={pinned}, editMode={_isEditMode}, components={_settings.Components.Count}, bounds=({Left},{Top},{Width},{Height}).");
        RenderAll();
        Show();
        ApplyScreenBounds();
        Activate();
        StartWebDavVisibleRefreshTimer();
        QueueWebDavStateRefresh("overlay-shown");
    }

    private void StartWebDavVisibleRefreshTimer()
    {
        _webDavVisibleRefreshTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = WebDavVisibleRefreshInterval
        };
        _webDavVisibleRefreshTimer.Tick -= WebDavVisibleRefreshTimer_Tick;
        _webDavVisibleRefreshTimer.Tick += WebDavVisibleRefreshTimer_Tick;
        _webDavVisibleRefreshTimer.Stop();
        _webDavVisibleRefreshTimer.Start();
    }

    private void StopWebDavVisibleRefreshTimer()
    {
        _webDavVisibleRefreshTimer?.Stop();
    }

    private void WebDavVisibleRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            StopWebDavVisibleRefreshTimer();
            return;
        }

        QueueWebDavStateRefresh("visible-poll");
    }

    private void QueueWebDavStateRefresh(string reason, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (_webDavStateRefreshRunning)
        {
            _webDavStateRefreshPending = true;
            _webDavStateRefreshPendingForce |= force;
            _webDavStateRefreshPendingReason = reason;
            HostAssets.AppendLog($"Yanm: WebDAV state refresh deferred, reason={reason}, force={force}.");
            return;
        }

        if (!force && now - _lastWebDavStateRefreshUtc < WebDavStateRefreshCooldown)
        {
            return;
        }

        _lastWebDavStateRefreshUtc = now;
        _webDavStateRefreshRunning = true;
        SetSyncStatus("同步中", WpfColor.FromRgb(96, 165, 250), visible: true);
        HostAssets.AppendLog($"Yanm: WebDAV state refresh queued, reason={reason}, force={force}.");
        _ = RefreshWebDavStateAsync();
    }

    private async Task RefreshWebDavStateAsync()
    {
        try
        {
            var cloudResult = await _mainWindow.PullYanmStateFromCloudNowAsync();
            var currentSettings = AppSettingsStore.Load();
            var hasWebDav = currentSettings.EnableWebDavSync && _mainWindow.HasWebDavCredential();
            (bool ok, string message, bool uploaded, bool pulled, int payloadBytes) result = hasWebDav
                ? await _mainWindow.SyncYanmStateNowAsync()
                : (true, "未启用 WebDAV，已跳过坚果云同步。", false, false, 0);
            if (cloudResult.ok || result.ok)
            {
                if (Dispatcher.CheckAccess())
                {
                    ApplyWebDavStateRefreshToComponents();
                }
                else
                {
                    await Dispatcher.InvokeAsync(ApplyWebDavStateRefreshToComponents);
                }

                await Dispatcher.InvokeAsync(() =>
                    SetSyncStatus(
                        cloudResult.pulled || result.pulled ? "已拉取" : result.uploaded ? "已上传" : "已同步",
                        WpfColor.FromRgb(52, 211, 153),
                        visible: true));
            }
            else
            {
                await Dispatcher.InvokeAsync(() => SetSyncStatus("同步失败", WpfColor.FromRgb(248, 113, 113), visible: true));
            }

            HostAssets.AppendLog($"Yanm: cloud state refresh {(cloudResult.ok ? "completed" : "failed")}: {cloudResult.message}");
            HostAssets.AppendLog($"Yanm: WebDAV state refresh {(result.ok ? "completed" : "failed")}: {result.message}");
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => SetSyncStatus("同步失败", WpfColor.FromRgb(248, 113, 113), visible: true));
            HostAssets.AppendLog($"Yanm: WebDAV state refresh failed: {ex.Message}");
        }
        finally
        {
            _webDavStateRefreshRunning = false;
            if (_webDavStateRefreshPending)
            {
                var pendingReason = string.IsNullOrWhiteSpace(_webDavStateRefreshPendingReason)
                    ? "pending"
                    : _webDavStateRefreshPendingReason;
                var pendingForce = _webDavStateRefreshPendingForce;
                _webDavStateRefreshPending = false;
                _webDavStateRefreshPendingForce = false;
                _webDavStateRefreshPendingReason = string.Empty;
                await Dispatcher.InvokeAsync(() => QueueWebDavStateRefresh(pendingReason, pendingForce));
            }
        }
    }

    private void SetSyncStatus(string text, WpfColor color, bool visible)
    {
        if (SyncStatusPanel == null || SyncStatusText == null || SyncStatusDot == null)
        {
            return;
        }

        SyncStatusText.Text = text;
        SyncStatusDot.Fill = new SolidColorBrush(color);
        SyncStatusPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyWebDavStateRefreshToComponents()
    {
        if (!IsVisible)
        {
            return;
        }

        _settings = AppSettingsStore.Load().Yanm ?? new YanmSettings();
        _settings.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        UpdateDynamicTexts();
        RenderAll();

        var keys = _settings.ComponentState.Keys.ToList();
        foreach (var view in _componentViews.Values)
        {
            foreach (var key in keys)
            {
                SendComponentState(view.Id, key);
            }
        }

        HostAssets.AppendLog($"Yanm: component state broadcast after WebDAV refresh, components={_componentViews.Count}, keys={keys.Count}.");
    }

    private void ApplyScreenBounds()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle == IntPtr.Zero ? helper.EnsureHandle() : helper.Handle;
        var source = HwndSource.FromHwnd(handle) ?? PresentationSource.FromVisual(this) as HwndSource;
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var target = GetTargetScreenBounds();
        var topLeft = transform.Transform(new WpfPoint(target.Left, target.Top));
        var bottomRight = transform.Transform(new WpfPoint(target.Right, target.Bottom));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, bottomRight.X - topLeft.X);
        Height = Math.Max(1, bottomRight.Y - topLeft.Y);
        HostAssets.AppendLog(
            $"Yanm: screen bounds applied, target={DescribeScreen(target)}, all={DescribeAllScreens()}, dip=({Left:0},{Top:0},{Width:0},{Height:0}), m11={transform.M11:0.###}, m22={transform.M22:0.###}, hwnd=0x{handle.ToInt64():X}.");
    }

    private static System.Drawing.Rectangle GetTargetScreenBounds()
    {
        if (GetCursorPos(out var point))
        {
            return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).Bounds;
        }

        return System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.Screen.AllScreens[0].Bounds;
    }

    private static string DescribeAllScreens()
    {
        return string.Join("|", System.Windows.Forms.Screen.AllScreens.Select(screen => DescribeScreen(screen.Bounds)));
    }

    private static string DescribeScreen(System.Drawing.Rectangle bounds)
    {
        return $"({bounds.X},{bounds.Y},{bounds.Width},{bounds.Height})";
    }

    private void RenderAll()
    {
        DrawGrid();
        RenderComponents();
    }

    private void DrawGrid()
    {
        GridCanvas.Children.Clear();
        if (!_isEditMode)
        {
            return;
        }

        var grid = Math.Max(5, _settings.GridSizePixels);
        var lineBrush = new SolidColorBrush(WpfColor.FromArgb(34, 255, 255, 255));
        for (var x = 0.0; x <= Width; x += grid)
        {
            GridCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = Height,
                Stroke = lineBrush,
                StrokeThickness = x % (grid * 10) == 0 ? 0.8 : 0.35
            });
        }

        for (var y = 0.0; y <= Height; y += grid)
        {
            GridCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0,
                Y1 = y,
                X2 = Width,
                Y2 = y,
                Stroke = lineBrush,
                StrokeThickness = y % (grid * 10) == 0 ? 0.8 : 0.35
            });
        }
    }

    private void RenderComponents()
    {
        WebView2WarningPanel.Visibility = !_isWebView2Available ? Visibility.Visible : Visibility.Collapsed;
        ComponentCanvas.Visibility = _isWebView2Available ? Visibility.Visible : Visibility.Collapsed;
        WelcomePanel.Visibility = _settings.Components.Count == 0 && _isWebView2Available ? Visibility.Visible : Visibility.Collapsed;
        if (!_isWebView2Available)
        {
            return;
        }

        var activeIds = _settings.Components
            .Select(component => component.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in _componentViews.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            if (_componentViews.TryGetValue(staleId, out var staleView))
            {
                ComponentCanvas.Children.Remove(staleView.Frame);
            }

            _componentViews.Remove(staleId);
        }

        foreach (var component in _settings.Components)
        {
            var view = GetOrCreateComponentView(component);
            ApplyComponentView(view, component);
            if (!ComponentCanvas.Children.Contains(view.Frame))
            {
                ComponentCanvas.Children.Add(view.Frame);
            }
        }
    }

    private YanmComponentView GetOrCreateComponentView(YanmComponentSettings component)
    {
        if (_componentViews.TryGetValue(component.Id, out var existing))
        {
            return existing;
        }

        var host = new Grid
        {
            Width = component.Width,
            Height = component.Height
        };
        var browser = new WebView2
        {
            Width = component.Width,
            Height = component.Height,
            DefaultBackgroundColor = System.Drawing.Color.Transparent
        };
        host.Children.Add(browser);

        var selectionBorder = new Border
        {
            CornerRadius = new CornerRadius(20),
            IsHitTestVisible = false
        };
        host.Children.Add(selectionBorder);

        var frame = new Border
        {
            Width = component.Width,
            Height = component.Height,
            CornerRadius = new CornerRadius(20),
            ClipToBounds = true,
            Tag = component,
            Child = host
        };
        var view = new YanmComponentView(component.Id, frame, host, browser, selectionBorder)
        {
            Component = component,
            Html = component.Html,
            Locked = component.Locked
        };
        frame.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (TryPromoteHeldTriggerToEditMode())
            {
                e.Handled = true;
                return;
            }

            var current = frame.Tag as YanmComponentSettings ?? view.Component;
            SelectComponent(current);
            frame.ContextMenu.PlacementTarget = frame;
            frame.ContextMenu.IsOpen = true;
            e.Handled = true;
        };
        frame.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindAncestor<WebView2>(e.OriginalSource as DependencyObject) != null && !_isEditMode)
            {
                return;
            }

            if (TryPromoteHeldTriggerToEditMode())
            {
                e.Handled = true;
                return;
            }

            var current = frame.Tag as YanmComponentSettings ?? view.Component;
            SelectComponent(current);
            if (current.Locked)
            {
                return;
            }

            BeginMoveComponent(current, e.GetPosition(Root));
            e.Handled = true;
        };

        browser.Loaded += (_, _) => NavigateComponentBrowser(view, view.Component);
        _componentViews[component.Id] = view;
        return view;
    }

    private void ApplyComponentView(YanmComponentView view, YanmComponentSettings component)
    {
        var htmlChanged = !string.Equals(view.Html, component.Html, StringComparison.Ordinal);
        var lockChanged = view.Locked != component.Locked;

        view.Component = component;
        view.Frame.Tag = component;
        view.Frame.Width = component.Width;
        view.Frame.Height = component.Height;
        view.Frame.ContextMenu = BuildComponentMenu(component);
        view.Frame.Cursor = _isEditMode && !component.Locked ? WpfCursors.SizeAll : WpfCursors.Arrow;
        view.Host.Width = component.Width;
        view.Host.Height = component.Height;
        view.Browser.Width = Math.Max(1, component.Width);
        view.Browser.Height = Math.Max(1, component.Height);
        Canvas.SetLeft(view.Frame, component.X);
        Canvas.SetTop(view.Frame, component.Y);
        UpdateComponentSelectionVisual(view);

        if (htmlChanged && view.Browser.IsLoaded)
        {
            NavigateComponentBrowser(view, component);
        }
        else if (lockChanged && view.Browser.IsLoaded)
        {
            ApplyComponentLockState(view, component.Locked);
        }

        view.Html = component.Html;
        view.Locked = component.Locked;
    }

    private void UpdateComponentSelectionVisual(YanmComponentView view)
    {
        var selected = IsComponentSelected(view.Component);
        view.Frame.BorderBrush = new SolidColorBrush(selected ? WpfColor.FromRgb(100, 199, 255) : WpfColor.FromArgb(80, 255, 255, 255));
        view.Frame.BorderThickness = _isPinned ? new Thickness(selected ? 2 : 1) : new Thickness(0);
        view.SelectionBorder.BorderBrush = new SolidColorBrush(selected ? WpfColor.FromRgb(100, 199, 255) : WpfColor.FromArgb(80, 255, 255, 255));
        view.SelectionBorder.BorderThickness = selected ? new Thickness(2) : new Thickness(0);
    }

    private void BeginMoveComponent(YanmComponentSettings component, WpfPoint startPoint)
    {
        _isMovingComponent = true;
        _movingComponentId = component.Id;
        _moveStartPoint = startPoint;
        _moveStartComponentPoint = new WpfPoint(component.X, component.Y);
        Mouse.Capture(Root, CaptureMode.SubTree);
        SelectComponent(component);
        HostAssets.AppendLog($"Yanm: move started component={component.Title}, at=({_moveStartComponentPoint.X:0},{_moveStartComponentPoint.Y:0}).");
    }

    private void BeginResizeComponent(YanmComponentSettings component, WpfPoint startPoint)
    {
        _isResizingComponent = true;
        _resizingComponentId = component.Id;
        _resizeStartPoint = startPoint;
        _resizeStartSize = new WpfSize(component.Width, component.Height);
        Mouse.Capture(Root, CaptureMode.SubTree);
        SelectComponent(component);
        HostAssets.AppendLog($"Yanm: resize started component={component.Title}, size=({_resizeStartSize.Width:0},{_resizeStartSize.Height:0}).");
    }

    private async void NavigateComponentBrowser(YanmComponentView view, YanmComponentSettings component)
    {
        try
        {
            var environment = await GetYanmWebView2EnvironmentAsync();
            await view.Browser.EnsureCoreWebView2Async(environment);
            view.Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            view.Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            if (!view.WebMessageAttached)
            {
                view.Browser.CoreWebView2.WebMessageReceived += (_, args) => HandleComponentWebMessage(view.Id, args);
                view.WebMessageAttached = true;
            }

            view.Browser.CoreWebView2.NavigateToString(CreateRuntimeHtml(component));
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanm: WebView2 component render failed, title={component.Title}, error={ex.Message}");
            ShowWebView2Warning();
            view.Browser.CoreWebView2?.NavigateToString(YanmComponentSettings.DefaultHtml(component.Title));
        }
    }

    private async Task<CoreWebView2Environment> GetYanmWebView2EnvironmentAsync()
    {
        if (_webView2Environment != null)
        {
            return _webView2Environment;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost",
            "YanmWebView2");
        Directory.CreateDirectory(userDataFolder);
        _webView2Environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder);
        HostAssets.AppendLog($"Yanm: WebView2 environment created, userDataFolder={userDataFolder}.");
        return _webView2Environment;
    }

    private void ShowWebView2Warning()
    {
        _isWebView2Available = false;
        ComponentCanvas.Visibility = Visibility.Collapsed;
        WebView2WarningPanel.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Collapsed;
    }

    private static bool CheckWebView2RuntimeAvailable(bool logSuccess = true)
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var ok = !string.IsNullOrWhiteSpace(version);
            if (ok && logSuccess)
            {
                HostAssets.AppendLog($"Yanm: WebView2 runtime available, version={version}.");
            }
            else if (!ok)
            {
                HostAssets.AppendLog("Yanm: WebView2 runtime is unavailable, empty version.");
            }

            return ok;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanm: WebView2 runtime unavailable, error={ex.Message}");
            return false;
        }
    }

    private static void ApplyComponentLockState(YanmComponentView view, bool locked)
    {
        if (view.Browser.CoreWebView2 == null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new { type = "setLocked", locked });
        view.Browser.CoreWebView2.PostWebMessageAsString(payload);
    }

    private string CreateRuntimeHtml(YanmComponentSettings component)
    {
        var script = $$"""
<script>document.documentElement.setAttribute('data-yanm-locked','{{(component.Locked ? "true" : "false")}}');</script>
<style>
#yanm-locked-move-handle{position:fixed;left:0;top:0;right:0;height:{{ComponentMoveHandleHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;z-index:2147483646;cursor:not-allowed;background:rgba(255,255,255,.001);}
#yanm-locked-move-handle:after{content:"🔒";position:absolute;left:50%;top:5px;transform:translateX(-50%);width:22px;height:22px;line-height:22px;text-align:center;border-radius:8px;background:rgba(17,19,24,.88);font-size:13px;opacity:0;transition:.12s;}
#yanm-locked-move-handle:hover{background:rgba(255,255,255,.035);}
#yanm-locked-move-handle:hover:after{opacity:1;}
#yanm-lock-handle{position:fixed;right:0;bottom:0;width:28px;height:28px;z-index:2147483647;cursor:pointer;background:rgba(255,255,255,.001);}
#yanm-lock-handle:after{content:"🔒";position:absolute;right:4px;bottom:4px;width:20px;height:20px;line-height:20px;text-align:center;border-radius:8px;background:rgba(17,19,24,.88);font-size:12px;opacity:0;transition:.12s;}
#yanm-lock-handle:hover:after{opacity:1;background:rgba(100,199,255,.92);}
#yanm-move-handle{position:fixed;left:0;top:0;right:0;height:{{ComponentMoveHandleHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;z-index:2147483646;cursor:move;background:rgba(255,255,255,.001);}
#yanm-move-handle:after{content:"移动";position:absolute;left:50%;top:5px;transform:translateX(-50%);padding:3px 9px;border-radius:999px;background:rgba(17,19,24,.84);color:white;font-size:11px;opacity:0;transition:.12s;}
#yanm-move-handle:hover{background:rgba(100,199,255,.08);}
#yanm-move-handle:hover:after{opacity:1;}
#yanm-resize-handle{position:fixed;right:0;bottom:0;width:28px;height:28px;z-index:2147483647;cursor:nwse-resize;background:rgba(255,255,255,.001);}
#yanm-resize-handle:after{content:"↘";position:absolute;right:4px;bottom:4px;width:18px;height:18px;line-height:18px;text-align:center;border-radius:7px;background:rgba(100,199,255,.92);color:#07131f;font-weight:900;font-size:12px;opacity:0;transition:.12s;box-shadow:0 0 0 1px rgba(255,255,255,.45) inset;}
#yanm-resize-handle:hover:after{opacity:1;}
:root[data-yanm-locked="true"] #yanm-move-handle,
:root[data-yanm-locked="true"] #yanm-resize-handle{display:none;}
:root[data-yanm-locked="false"] #yanm-locked-move-handle,
:root[data-yanm-locked="false"] #yanm-lock-handle{display:none;}
</style>
<div id="yanm-locked-move-handle" title="组件已锁定，点击可解锁"></div>
<div id="yanm-lock-handle" title="点击解锁组件"></div>
<div id="yanm-move-handle" title="拖动移动组件"></div>
<div id="yanm-resize-handle" title="拖动调整大小"></div>
<script>
(function(){
  function post(o){ try { chrome.webview.postMessage(JSON.stringify(o)); } catch(e) {} }
  var pendingInvokes = {};
  var invokeSeq = 0;
  function invoke(method, args){
    return new Promise(function(resolve, reject){
      var id = 'inv_' + (++invokeSeq) + '_' + Date.now();
      pendingInvokes[id] = { resolve: resolve, reject: reject };
      post({ type: 'yanm.invoke', id: id, method: String(method || ''), args: args || {} });
      setTimeout(function(){
        if (pendingInvokes[id]) {
          delete pendingInvokes[id];
          reject(new Error('YANM_INVOCATION_TIMEOUT'));
        }
      }, 10000);
    });
  }
  function normalizeReply(data){
    if (!data || data.type !== 'yanm.reply' || !data.id) {
      return false;
    }
    var pending = pendingInvokes[data.id];
    if (!pending) {
      return true;
    }
    delete pendingInvokes[data.id];
    if (data.ok) {
      pending.resolve(data.result);
    } else {
      pending.reject(new Error(data.error || 'YANM_INVOCATION_FAILED'));
    }
    return true;
  }
  function setLocked(locked){ document.documentElement.setAttribute('data-yanm-locked', locked ? 'true' : 'false'); }
  function isEditable(el){
    while(el && el !== document.documentElement){
      var tag=(el.tagName||'').toLowerCase();
      if(tag==='input'||tag==='textarea'||tag==='select'||el.isContentEditable){ return true; }
      el=el.parentElement;
    }
    return false;
  }
  window.yanm = window.yanm || {};
  window.yanm.componentId = '{{component.Id}}';
  window.yanm.componentTitle = {{JsonSerializer.Serialize(component.Title)}};
  window.yanm.invoke = invoke;
  window.yanm.on = function(type, handler){
    window.addEventListener('yanm:message', function(e){
      if(e.detail && e.detail.type === type){ handler(e.detail); }
    });
  };
  window.yanmHost = window.yanmHost || {};
  window.__yanmComponentId = '{{component.Id}}';
  window.yanmHost.requestSystemInfo = function(){ return invoke('system.info'); };
  window.yanmHost.getState = function(key){ return invoke('state.get', { key: String(key||'') }); };
  window.yanmHost.setState = function(key,value){ return invoke('state.set', { key: String(key||''), value: String(value||'') }); };
  function shouldSuppressInteractionFocus(){
    return window.__yanmSuppressNextInteractionFocusUntil && Date.now() < window.__yanmSuppressNextInteractionFocusUntil;
  }
  document.addEventListener('pointerdown', function(e){
    if(isEditable(e.target) && !shouldSuppressInteractionFocus()){ post({type:'interactionFocus'}); }
  }, true);
  document.addEventListener('mousedown', function(e){
    if(isEditable(e.target) && !shouldSuppressInteractionFocus()){ post({type:'interactionFocus'}); }
  }, true);
  document.addEventListener('focusin', function(e){
    if(isEditable(e.target) && !shouldSuppressInteractionFocus()){ post({type:'interactionFocus'}); }
  }, true);
  ['yanm-lock-handle','yanm-locked-move-handle'].forEach(function(id){
    var lock=document.getElementById(id);
    if(lock){lock.addEventListener('click',function(e){e.preventDefault();e.stopPropagation();post({type:'unlockRequest'});},true);}
  });
  function bind(id, kind){
    var el=document.getElementById(id); if(!el) return;
    el.addEventListener('pointerdown', function(e){
      e.preventDefault(); e.stopPropagation();
      var startX=e.clientX, startY=e.clientY;
      try { el.setPointerCapture(e.pointerId); } catch(_e) {}
      post({type:kind+'Start'});
      function move(ev){ post({type:kind+'Move', dx:ev.clientX-startX, dy:ev.clientY-startY}); }
      function up(ev){
        document.removeEventListener('pointermove', move, true);
        document.removeEventListener('pointerup', up, true);
        post({type:kind+'End', dx:ev.clientX-startX, dy:ev.clientY-startY});
      }
      document.addEventListener('pointermove', move, true);
      document.addEventListener('pointerup', up, true);
    }, true);
  }
  bind('yanm-move-handle','move');
  bind('yanm-resize-handle','resize');
  if(window.chrome && chrome.webview){
    chrome.webview.addEventListener('message', function(event){
      try {
        var data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        if(data && data.type === 'setLocked'){ setLocked(!!data.locked); }
        if(normalizeReply(data)){ return; }
        window.dispatchEvent(new CustomEvent('yanm:message', { detail: data }));
      } catch(e) {}
    });
  }
  document.addEventListener('contextmenu', function(e){
    e.preventDefault();
    e.stopPropagation();
    post({type:'contextMenu'});
    return false;
  }, true);
})();
</script>
""";

        var html = component.Html;
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd >= 0
            ? html.Insert(bodyEnd, script)
            : html + script;
    }

    private void HandleComponentWebMessage(string componentId, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var component = FindCurrentComponent(componentId);
        if (component == null)
        {
            HostAssets.AppendLog($"Yanm: component message ignored because component is missing, id={componentId}.");
            return;
        }

        try
        {
            var payload = args.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() ?? string.Empty : string.Empty;
            var dx = root.TryGetProperty("dx", out var dxProperty) ? dxProperty.GetDouble() : 0;
            var dy = root.TryGetProperty("dy", out var dyProperty) ? dyProperty.GetDouble() : 0;
            switch (type)
            {
                case "yanm.invoke":
                    _yanmBridgeService.HandleInvoke(componentId, root);
                    break;
                case "moveStart":
                    BeginMoveComponent(component, GetCursorPointInOverlay());
                    break;
                case "moveMove":
                    MoveComponentPreview(GetCursorPointInOverlay());
                    break;
                case "moveEnd":
                    CommitComponentMove(GetCursorPointInOverlay());
                    break;
                case "resizeStart":
                    BeginResizeComponent(component, GetCursorPointInOverlay());
                    break;
                case "resizeMove":
                    ResizeComponentPreview(GetCursorPointInOverlay());
                    break;
                case "resizeEnd":
                    CommitComponentResize(GetCursorPointInOverlay());
                    break;
                case "contextMenu":
                    Dispatcher.Invoke(() => ShowComponentContextMenu(component));
                    break;
                case "unlockRequest":
                    Dispatcher.Invoke(() => ConfirmUnlockComponent(component));
                    break;
                case "host.systemInfo":
                    SendSystemInfoToComponent(componentId);
                    break;
                case "host.stateGet":
                    var getStateKey = root.TryGetProperty("key", out var getKeyProperty) ? getKeyProperty.GetString() ?? string.Empty : string.Empty;
                    HostAssets.AppendLog($"Yanm: component state requested, component={component.Title}, key={getStateKey}.");
                    SendComponentState(componentId, getStateKey);
                    break;
                case "host.stateSet":
                    var setStateKey = root.TryGetProperty("key", out var setKeyProperty) ? setKeyProperty.GetString() ?? string.Empty : string.Empty;
                    var setStateValue = root.TryGetProperty("value", out var setValueProperty) ? setValueProperty.GetString() ?? string.Empty : string.Empty;
                    HostAssets.AppendLog($"Yanm: component state queued, component={component.Title}, key={setStateKey}, valueLength={setStateValue.Length}.");
                    QueueComponentStateSave(
                        setStateKey,
                        setStateValue);
                    break;
                case "interactionFocus":
                    Dispatcher.Invoke(EnterInteractivePinnedMode);
                    break;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanm: component message failed, title={component.Title}, error={ex.Message}");
        }
    }

    private void HandleComponentInvoke(string componentId, YanmComponentSettings component, JsonElement root)
    {
        var invokeId = root.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? string.Empty : string.Empty;
        var method = root.TryGetProperty("method", out var methodProperty) ? methodProperty.GetString() ?? string.Empty : string.Empty;
        var args = root.TryGetProperty("args", out var argsProperty) ? argsProperty : default;

        try
        {
            var result = DispatchComponentCapability(componentId, component, method, args);
            SendComponentReply(componentId, invokeId, ok: true, result: result, error: null);
            if (method == "system.info")
            {
                SendSystemInfoToComponent(componentId);
            }
            else if (method == "state.get")
            {
                SendComponentState(componentId, GetInvokeString(args, "key"));
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanm: invoke failed, component={component.Title}, method={method}, error={ex.Message}");
            SendComponentReply(componentId, invokeId, ok: false, result: null, error: ex.Message);
        }
    }

    private object? DispatchComponentCapability(string componentId, YanmComponentSettings component, string method, JsonElement args)
    {
        return method switch
        {
            "system.info" => BuildSystemInfoResult(),
            "state.get" => BuildStateGetResult(componentId, args),
            "state.set" => BuildStateSetResult(args),
            "clipboard.read" => ClipboardService.GetText() ?? string.Empty,
            "clipboard.write" => BuildClipboardWriteResult(args),
            "desktop.list" => BuildDesktopListResult(),
            "command.execute" => BuildCommandExecuteResult(args),
            _ => throw new InvalidOperationException($"未知能力：{method}")
        };
    }

    private object BuildSystemInfoResult()
    {
        var memory = GetMemoryStatus();
        return new
        {
            cpuCores = Environment.ProcessorCount,
            isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable(),
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            time = DateTime.Now.ToString("HH:mm"),
            date = DateTime.Now.ToString("yyyy-MM-dd"),
            totalMemoryMb = memory.totalMb,
            availableMemoryMb = memory.availableMb,
            usedMemoryPercent = memory.usedPercent
        };
    }

    private object BuildStateGetResult(string componentId, JsonElement args)
    {
        var key = GetInvokeString(args, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("state.get 缺少 key。");
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.Yanm.ComponentState.TryGetValue(key, out var value);
        HostAssets.AppendLog($"Yanm: state.get, component={FindCurrentComponent(componentId)?.Title ?? componentId}, key={key}.");
        return new { key, value = value ?? string.Empty };
    }

    private object BuildStateSetResult(JsonElement args)
    {
        var key = GetInvokeString(args, "key");
        var value = GetInvokeString(args, "value");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("state.set 缺少 key。");
        }

        QueueComponentStateSave(key, value);
        return new { key, value };
    }

    private object BuildClipboardWriteResult(JsonElement args)
    {
        var text = GetInvokeString(args, "text");
        ClipboardService.SetText(text);
        return new { ok = true, length = text.Length };
    }

    private object BuildDesktopListResult()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var entries = Directory.Exists(desktop)
            ? Directory.EnumerateFileSystemEntries(desktop)
                .Take(200)
                .Select(path => new
                {
                    name = Path.GetFileName(path),
                    path,
                    isDirectory = Directory.Exists(path),
                    modifiedTime = File.Exists(path) ? File.GetLastWriteTime(path) : Directory.GetLastWriteTime(path)
                })
                .ToList()
            : [];

        return new { root = desktop, items = entries };
    }

    private object BuildCommandExecuteResult(JsonElement args)
    {
        var extensionId = GetInvokeString(args, "extensionId");
        var commandId = GetInvokeString(args, "commandId");
        var input = GetInvokeString(args, "input");
        var launchSource = string.IsNullOrWhiteSpace(GetInvokeString(args, "launchSource"))
            ? "yanm"
            : GetInvokeString(args, "launchSource");

        var targetId = !string.IsNullOrWhiteSpace(extensionId) ? extensionId : commandId;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException("command.execute 缺少 extensionId 或 commandId。");
        }

        var command = _mainWindow.GetAllCommands().FirstOrDefault(item =>
            item.ExtensionId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            throw new InvalidOperationException($"未找到命令：{targetId}");
        }

        _mainWindow.ExecuteCommandExternally(command, input, launchSource);
        return new { executed = true, extensionId = command.ExtensionId, title = command.Title };
    }

    private static string GetInvokeString(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object &&
               args.TryGetProperty(name, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private void SendComponentReply(string componentId, string invokeId, bool ok, object? result, string? error)
    {
        if (string.IsNullOrWhiteSpace(invokeId) ||
            !_componentViews.TryGetValue(componentId, out var view) ||
            view.Browser.CoreWebView2 == null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "yanm.reply",
            id = invokeId,
            ok,
            result,
            error
        });
        view.Browser.CoreWebView2.PostWebMessageAsString(payload);
    }

    private void SendComponentState(string componentId, string key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !_componentViews.TryGetValue(componentId, out var view) ||
            view.Browser.CoreWebView2 == null)
        {
            return;
        }

        var value = GetComponentStateValue(componentId, key);
        var payload = JsonSerializer.Serialize(new { type = "host.state", key, value });
        view.Browser.CoreWebView2.PostWebMessageAsString(payload);
    }

    private string GetComponentStateValue(string componentId, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.Yanm.ComponentState.TryGetValue(key, out var value);
        HostAssets.AppendLog($"Yanm: component state read, component={FindCurrentComponent(componentId)?.Title ?? componentId}, key={key}, valueLength={value?.Length ?? 0}.");
        return value ?? string.Empty;
    }

    private void QueueComponentStateSave(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _pendingComponentState[key] = value ?? string.Empty;
        _componentStateSaveTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _componentStateSaveTimer.Tick -= ComponentStateSaveTimer_Tick;
        _componentStateSaveTimer.Tick += ComponentStateSaveTimer_Tick;
        _componentStateSaveTimer.Stop();
        _componentStateSaveTimer.Start();
    }

    private void ComponentStateSaveTimer_Tick(object? sender, EventArgs e)
    {
        _componentStateSaveTimer?.Stop();
        if (_pendingComponentState.Count == 0)
        {
            return;
        }

        var pending = _pendingComponentState.ToArray();
        _pendingComponentState.Clear();
        var settings = AppSettingsStore.Load();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pending)
        {
            settings.Yanm.ComponentState[key] = value;
        }

        AppSettingsStore.Save(settings);
        _settings = AppSettingsStore.Load().Yanm;
        _mainWindow.NotifyQuickPanelSettingsChanged("yanm-component-state-saved", refreshYanmOverlay: false);
        QueueWebDavLocalChangeSync("component-state-saved");
        HostAssets.AppendLog($"Yanm: component state saved, count={pending.Length}.");
    }

    private void QueueWebDavLocalChangeSync(string reason)
    {
        var currentSettings = AppSettingsStore.Load();
        if (!currentSettings.EnableWebDavSync || !_mainWindow.HasWebDavCredential())
        {
            return;
        }

        _webDavLocalChangeSyncTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = WebDavLocalChangeSyncDelay
        };
        _webDavLocalChangeSyncTimer.Tag = reason;
        _webDavLocalChangeSyncTimer.Tick -= WebDavLocalChangeSyncTimer_Tick;
        _webDavLocalChangeSyncTimer.Tick += WebDavLocalChangeSyncTimer_Tick;
        _webDavLocalChangeSyncTimer.Stop();
        _webDavLocalChangeSyncTimer.Start();
        SetSyncStatus("待同步", WpfColor.FromRgb(251, 191, 36), visible: true);
        HostAssets.AppendLog($"Yanm: WebDAV local change sync scheduled, reason={reason}.");
    }

    private void WebDavLocalChangeSyncTimer_Tick(object? sender, EventArgs e)
    {
        _webDavLocalChangeSyncTimer?.Stop();
        var reason = _webDavLocalChangeSyncTimer?.Tag as string ?? "local-change";
        QueueWebDavStateRefresh(reason, force: true);
    }

    private void EnterInteractivePinnedMode()
    {
        if (_isPinned || _isInteractiveHoldPinned)
        {
            return;
        }

        _isInteractiveHoldPinned = true;
        _isEditMode = false;
        HintText.Text = BuildInteractiveHint();
        UpdateDynamicTexts();
        HostAssets.AppendLog("Yanm: temporary overlay converted to pinned interactive mode because component input received focus.");
    }

    private void SendSystemInfoToComponent(string componentId)
    {
        if (!_componentViews.TryGetValue(componentId, out var view) ||
            view.Browser.CoreWebView2 == null)
        {
            return;
        }

        var memory = GetMemoryStatus();
        var payload = JsonSerializer.Serialize(new
        {
            type = "host.systemInfo",
            cpuCores = Environment.ProcessorCount,
            isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable(),
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            time = DateTime.Now.ToString("HH:mm"),
            date = DateTime.Now.ToString("yyyy-MM-dd"),
            totalMemoryMb = memory.totalMb,
            availableMemoryMb = memory.availableMb,
            usedMemoryPercent = memory.usedPercent
        });
        view.Browser.CoreWebView2.PostWebMessageAsString(payload);
    }

    private static (ulong totalMb, ulong availableMb, double usedPercent) GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return (0, 0, 0);
        }

        var totalMb = status.ullTotalPhys / 1024 / 1024;
        var availableMb = status.ullAvailPhys / 1024 / 1024;
        var usedPercent = Math.Clamp((1 - (double)status.ullAvailPhys / status.ullTotalPhys) * 100, 0, 100);
        return (totalMb, availableMb, usedPercent);
    }

    private WpfPoint GetCursorPointInOverlay()
    {
        if (!GetCursorPos(out var point))
        {
            return _moveStartPoint;
        }

        return PointFromScreen(new WpfPoint(point.X, point.Y));
    }

    private YanmComponentSettings? FindCurrentComponent(string componentId)
    {
        return _settings.Components.FirstOrDefault(item =>
            item.Id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowComponentContextMenu(YanmComponentSettings component)
    {
        component = FindCurrentComponent(component.Id) ?? component;
        SelectComponent(component);
        if (!_componentViews.TryGetValue(component.Id, out var view))
        {
            return;
        }

        view.Frame.ContextMenu = BuildComponentMenu(component);
        view.Frame.ContextMenu.PlacementTarget = view.Frame;
        view.Frame.ContextMenu.IsOpen = true;
        HostAssets.AppendLog($"Yanm: component context menu opened from WebView2, title={component.Title}.");
    }

    private void ConfirmUnlockComponent(YanmComponentSettings component)
    {
        if (!component.Locked)
        {
            return;
        }

        var confirm = WpfMessageBox.Show(
            this,
            $"确认解除“{component.Title}”的位置锁定吗？",
            "解锁燕幕组件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ToggleComponentLock(component);
    }

    private ContextMenu BuildComponentMenu(YanmComponentSettings component)
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "编辑 HTML" };
        edit.Click += (_, _) => EditComponent(component);
        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) => RenameComponent(component);
        var lockItem = new MenuItem { Header = component.Locked ? "解除位置锁定" : "锁定位置" };
        lockItem.Click += (_, _) => ToggleComponentLock(component);
        var delete = new MenuItem { Header = "删除组件" };
        delete.Click += (_, _) => DeleteComponent(component);
        menu.Items.Add(edit);
        menu.Items.Add(rename);
        menu.Items.Add(lockItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        return menu;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HostAssets.AppendLog($"Yanm: left down, pinned={_isPinned}, editMode={_isEditMode}, source={e.OriginalSource?.GetType().Name}.");
        if (IsInteractiveOverlaySource(e.OriginalSource as DependencyObject))
        {
            HostAssets.AppendLog("Yanm: left down ignored because source is overlay chrome.");
            return;
        }

        if (FindAncestor<Border>(e.OriginalSource as DependencyObject, border => border.Tag is YanmComponentSettings) != null)
        {
            HostAssets.AppendLog("Yanm: root left down ignored because component handled target.");
            return;
        }

        if (FindAncestor<WebView2>(e.OriginalSource as DependencyObject) != null)
        {
            HostAssets.AppendLog("Yanm: left down ignored because source is component webview.");
            return;
        }

        if (_isInteractiveHoldPinned ||
            (_isEditMode && !_isSelecting && !_isMovingComponent && !_isResizingComponent) ||
            (!_isPinned && !_isEditMode && KeyboardDoubleTapService.IsYanmTriggerHeld))
        {
            _interactiveOutsideClickCandidate = true;
            _interactiveOutsideClickStart = e.GetPosition(Root);
            var capturedForExit = Mouse.Capture(Root, CaptureMode.SubTree);
            HostAssets.AppendLog($"Yanm: outside click candidate started, interactive={_isInteractiveHoldPinned}, editMode={_isEditMode}, captured={capturedForExit}.");
            e.Handled = true;
            return;
        }

        if (!_isPinned)
        {
            _isPinned = true;
            _isInteractiveHoldPinned = false;
            HostAssets.AppendLog("Yanm: selection start promoted temporary overlay into pinned edit mode.");
            UpdateCornerHint();
        }

        TryPromoteHeldTriggerToEditMode();
        if (!_isEditMode)
        {
            EnterEditMode("拖拽框选已进入编辑模式。");
        }

        BeginSelection(e.GetPosition(Root));
        e.Handled = true;
    }

    private void BeginSelection(WpfPoint startPoint)
    {
        _isSelecting = true;
        _selectionStart = startPoint;
        HostAssets.AppendLog($"Yanm: selection started at ({_selectionStart.X:0},{_selectionStart.Y:0}).");
        SelectionBox.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Collapsed;
        HoverCell.Visibility = Visibility.Collapsed;
        UpdateSelectionBox(_selectionStart, _selectionStart, snap: true);
        var captured = Mouse.Capture(Root, CaptureMode.SubTree);
        HostAssets.AppendLog($"Yanm: mouse capture requested, captured={captured}.");
    }

    private void Root_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_interactiveOutsideClickCandidate)
        {
            var current = e.GetPosition(Root);
            var candidateRect = SnapRect(BuildRect(_interactiveOutsideClickStart, current));
            var minSelectionSize = Math.Max(18, _settings.GridSizePixels * 2);
            if (candidateRect.Width >= minSelectionSize || candidateRect.Height >= minSelectionSize)
            {
                _interactiveOutsideClickCandidate = false;
                _isInteractiveHoldPinned = false;
                _isPinned = true;
                EnterEditMode("拖拽框选已进入编辑模式。");
                BeginSelection(_interactiveOutsideClickStart);
                UpdateSelectionBox(_selectionStart, current, snap: true);
                HostAssets.AppendLog($"Yanm: outside click converted to selection drag, rect=({candidateRect.Width:0},{candidateRect.Height:0}).");
            }

            e.Handled = true;
            return;
        }

        if (_isResizingComponent)
        {
            ResizeComponentPreview(e.GetPosition(Root));
            e.Handled = true;
            return;
        }

        if (_isMovingComponent)
        {
            MoveComponentPreview(e.GetPosition(Root));
            e.Handled = true;
            return;
        }

        if (!_isSelecting)
        {
            if (_isEditMode)
            {
                var hover = SnapCell(e.GetPosition(Root));
                Canvas.SetLeft(HoverCell, hover.X);
                Canvas.SetTop(HoverCell, hover.Y);
                HoverCell.Width = hover.Width;
                HoverCell.Height = hover.Height;
                HoverCell.Visibility = Visibility.Visible;
            }

            return;
        }

        UpdateSelectionBox(_selectionStart, e.GetPosition(Root), snap: true);
        e.Handled = true;
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HostAssets.AppendLog($"Yanm: left up, selecting={_isSelecting}, captured={ReferenceEquals(Mouse.Captured, Root)}, source={e.OriginalSource?.GetType().Name}.");
        if (_interactiveOutsideClickCandidate)
        {
            _interactiveOutsideClickCandidate = false;
            Mouse.Capture(null);
            HostAssets.AppendLog("Yanm: interactive outside single click detected, hiding overlay.");
            HideOverlay();
            e.Handled = true;
            return;
        }

        if (_isResizingComponent)
        {
            CommitComponentResize(e.GetPosition(Root));
            e.Handled = true;
            return;
        }

        if (_isMovingComponent)
        {
            CommitComponentMove(e.GetPosition(Root));
            e.Handled = true;
            return;
        }

        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        Mouse.Capture(null);
        SelectionBox.Visibility = Visibility.Collapsed;
        HoverCell.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
        var rect = SnapRect(BuildRect(_selectionStart, e.GetPosition(Root)));
        HostAssets.AppendLog($"Yanm: selection completed rect=({rect.X:0},{rect.Y:0},{rect.Width:0},{rect.Height:0}).");
        if (rect.Width < Math.Max(18, _settings.GridSizePixels * 2) || rect.Height < Math.Max(18, _settings.GridSizePixels * 2))
        {
            HostAssets.AppendLog("Yanm: selection ignored because rect is too small.");
            return;
        }

        CreateComponent(rect);
        e.Handled = true;
    }

    private void Root_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        HostAssets.AppendLog($"Yanm: right down, editMode={_isEditMode}, source={e.OriginalSource?.GetType().Name}.");
        if (FindAncestor<Border>(e.OriginalSource as DependencyObject, border => border.Tag is YanmComponentSettings) != null)
        {
            HostAssets.AppendLog("Yanm: root right down ignored because component handled target.");
            return;
        }

        if (TryPromoteHeldTriggerToEditMode())
        {
            e.Handled = true;
            return;
        }

        if (!_isEditMode)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        var add = new MenuItem { Header = "在此处新建组件" };
        var point = e.GetPosition(Root);
        add.Click += (_, _) => CreateComponent(new Rect(Snap(point.X), Snap(point.Y), 320, 180));
        menu.Items.Add(add);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = WpfMessageBox.Show(
            this,
            "确认恢复默认燕幕组件库吗？当前燕幕组件布局和内容会被替换为内置模板。",
            "重置默认组件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm.Components = YanmComponentSettings.CreateDefaultComponents();
        settings.Yanm.HasInitializedDefaultComponents = true;
        settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        SaveSettings(settings, "yanm-default-components-reset");
        e.Handled = true;
    }

    private void OpenYanmSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog("Yanm: open settings requested from overlay.");
        _isPinned = false;
        _isInteractiveHoldPinned = false;
        HideOverlay();

        if (System.Windows.Application.Current is App app)
        {
            Dispatcher.BeginInvoke(() => app.OpenSettingsWindow("yanm"));
        }
        else
        {
            HostAssets.AppendLog("Yanm: open settings skipped because current application is not App.");
        }

        e.Handled = true;
    }

    private void CopyYanmPromptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClipboardService.SetText(YanmComponentSettings.BuildAiPrompt());
            HintText.Text = "已复制燕幕组件提示词，可直接发给 AI 生成 HTML 组件。";
            HostAssets.AppendLog("Yanm: component AI prompt copied.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanm: copy component prompt failed, error={ex.Message}");
            WpfMessageBox.Show(this, $"复制提示词失败：{ex.Message}", "燕幕组件提示词", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private void DownloadWebView2Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanm: open WebView2 download page failed, error={ex.Message}");
            WpfMessageBox.Show(this, "无法打开 WebView2 下载页，请手动搜索并安装 Microsoft Edge WebView2 Runtime。", "燕幕组件环境", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        e.Handled = true;
    }

    private void CreateComponent(Rect rect)
    {
        var defaultName = "信息卡片";
        var dialog = new YanmComponentEditorWindow(
            "新建燕幕组件",
            defaultName,
            YanmComponentSettings.DefaultHtml(defaultName))
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            HostAssets.AppendLog("Yanm: create component canceled.");
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm.Components.Add(new YanmComponentSettings
        {
            Title = dialog.ComponentName,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            Html = dialog.ComponentHtml
        });
        SaveSettings(settings, "yanm-component-created");
        HostAssets.AppendLog($"Yanm: component created title={dialog.ComponentName}, rect=({rect.X:0},{rect.Y:0},{rect.Width:0},{rect.Height:0}).");
    }

    private void RenameComponent(YanmComponentSettings component)
    {
        var dialog = new SimpleTextInputWindow("重命名燕幕组件", "输入新的组件名称。", component.Title)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var target = settings.Yanm.Components.FirstOrDefault(item => item.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return;
        }

        target.Title = dialog.ValueText;
        SaveSettings(settings, "yanm-component-renamed");
    }

    private void EditComponent(YanmComponentSettings component)
    {
        var dialog = new YanmComponentEditorWindow("编辑燕幕组件", component.Title, component.Html)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var target = settings.Yanm.Components.FirstOrDefault(item => item.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return;
        }

        target.Title = dialog.ComponentName;
        target.Html = dialog.ComponentHtml;
        SaveSettings(settings, "yanm-component-html-edited");
    }

    private void ToggleComponentLock(YanmComponentSettings component)
    {
        var settings = AppSettingsStore.Load();
        var target = settings.Yanm.Components.FirstOrDefault(item => item.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return;
        }

        target.Locked = !target.Locked;
        SaveSettings(settings, target.Locked ? "yanm-component-locked" : "yanm-component-unlocked", rerender: false);
    }

    private bool IsComponentSelected(YanmComponentSettings component)
    {
        return !string.IsNullOrWhiteSpace(_selectedComponentId) &&
               component.Id.Equals(_selectedComponentId, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectComponent(YanmComponentSettings component)
    {
        var previousId = _selectedComponentId;
        if (component.Id.Equals(_selectedComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (_componentViews.TryGetValue(component.Id, out var currentView))
            {
                UpdateComponentSelectionVisual(currentView);
            }

            return;
        }

        _selectedComponentId = component.Id;
        if (!string.IsNullOrWhiteSpace(previousId) && _componentViews.TryGetValue(previousId, out var previousView))
        {
            UpdateComponentSelectionVisual(previousView);
        }

        if (_componentViews.TryGetValue(component.Id, out var selectedView))
        {
            UpdateComponentSelectionVisual(selectedView);
        }
    }

    private void MoveComponentPreview(WpfPoint currentPoint)
    {
        MoveComponentByDelta(currentPoint.X - _moveStartPoint.X, currentPoint.Y - _moveStartPoint.Y);
    }

    private void MoveComponentByDelta(double dx, double dy)
    {
        var frame = ComponentCanvas.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Tag is YanmComponentSettings component &&
                                      component.Id.Equals(_movingComponentId, StringComparison.OrdinalIgnoreCase));
        if (frame == null)
        {
            return;
        }

        var nextX = Snap(_moveStartComponentPoint.X + dx);
        var nextY = Snap(_moveStartComponentPoint.Y + dy);
        Canvas.SetLeft(frame, Math.Max(0, nextX));
        Canvas.SetTop(frame, Math.Max(0, nextY));
    }

    private void CommitComponentMove(WpfPoint currentPoint)
    {
        CommitComponentMoveByDelta(currentPoint.X - _moveStartPoint.X, currentPoint.Y - _moveStartPoint.Y);
    }

    private void CommitComponentMoveByDelta(double dx, double dy)
    {
        var settings = AppSettingsStore.Load();
        var target = settings.Yanm.Components.FirstOrDefault(item => item.Id.Equals(_movingComponentId, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.X = Math.Max(0, Snap(_moveStartComponentPoint.X + dx));
            target.Y = Math.Max(0, Snap(_moveStartComponentPoint.Y + dy));
            HostAssets.AppendLog($"Yanm: move committed component={target.Title}, to=({target.X:0},{target.Y:0}).");
            SaveSettings(settings, "yanm-component-moved", rerender: false);
        }

        _isMovingComponent = false;
        _movingComponentId = string.Empty;
        _selectedComponentId = string.Empty;
        Mouse.Capture(null);
    }

    private void ResizeComponentPreview(WpfPoint currentPoint)
    {
        ResizeComponentByDelta(currentPoint.X - _resizeStartPoint.X, currentPoint.Y - _resizeStartPoint.Y);
    }

    private void ResizeComponentByDelta(double dx, double dy)
    {
        var frame = ComponentCanvas.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Tag is YanmComponentSettings component &&
                                      component.Id.Equals(_resizingComponentId, StringComparison.OrdinalIgnoreCase));
        if (frame == null || frame.Child is not FrameworkElement child)
        {
            return;
        }

        var nextWidth = Math.Max(120, Snap(_resizeStartSize.Width + dx));
        var nextHeight = Math.Max(90, Snap(_resizeStartSize.Height + dy));
        frame.Width = nextWidth;
        frame.Height = nextHeight;
        child.Width = nextWidth;
        child.Height = nextHeight;
        if (child is Grid grid && grid.Children.OfType<WebView2>().FirstOrDefault() is { } browser)
        {
            browser.Width = Math.Max(1, nextWidth);
            browser.Height = Math.Max(1, nextHeight);
        }
    }

    private void CommitComponentResize(WpfPoint currentPoint)
    {
        CommitComponentResizeByDelta(currentPoint.X - _resizeStartPoint.X, currentPoint.Y - _resizeStartPoint.Y);
    }

    private void CommitComponentResizeByDelta(double dx, double dy)
    {
        var settings = AppSettingsStore.Load();
        var target = settings.Yanm.Components.FirstOrDefault(item => item.Id.Equals(_resizingComponentId, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.Width = Math.Max(120, Snap(_resizeStartSize.Width + dx));
            target.Height = Math.Max(90, Snap(_resizeStartSize.Height + dy));
            HostAssets.AppendLog($"Yanm: resize committed component={target.Title}, size=({target.Width:0},{target.Height:0}).");
            SaveSettings(settings, "yanm-component-resized", rerender: false);
        }

        _isResizingComponent = false;
        _resizingComponentId = string.Empty;
        Mouse.Capture(null);
    }

    private void DeleteComponent(YanmComponentSettings component)
    {
        var confirm = WpfMessageBox.Show(this, $"确认删除“{component.Title}”吗？", "删除燕幕组件", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm.Components.RemoveAll(item => item.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase));
        SaveSettings(settings, "yanm-component-deleted");
    }

    private void SaveSettings(AppSettings settings, string reason, bool rerender = true)
    {
        AppSettingsStore.Save(settings);
        _settings = AppSettingsStore.Load().Yanm;
        _mainWindow.NotifyQuickPanelSettingsChanged(reason, refreshYanmOverlay: rerender);
        QueueWebDavLocalChangeSync(reason);
        HostAssets.AppendLog($"Yanm: settings saved, reason={reason}, components={_settings.Components.Count}.");
        if (rerender)
        {
            RenderAll();
            return;
        }

        SyncCachedComponentViews();
    }

    private void SyncCachedComponentViews()
    {
        var currentIds = _settings.Components
            .Select(component => component.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in _componentViews.Keys.Where(id => !currentIds.Contains(id)).ToList())
        {
            if (_componentViews.TryGetValue(staleId, out var staleView))
            {
                ComponentCanvas.Children.Remove(staleView.Frame);
            }

            _componentViews.Remove(staleId);
        }

        foreach (var component in _settings.Components)
        {
            if (_componentViews.TryGetValue(component.Id, out var view))
            {
                ApplyComponentView(view, component);
            }
        }
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _isPinned = false;
            HostAssets.AppendLog("Yanm: escape pressed, hiding overlay.");
            HideOverlay();
            e.Handled = true;
        }
    }

    private void ResetInteractionState(bool clearEditMode)
    {
        if (clearEditMode)
        {
            ClearComponentInputFocus();
        }

        _isSelecting = false;
        _isMovingComponent = false;
        _isResizingComponent = false;
        _interactiveOutsideClickCandidate = false;
        _movingComponentId = string.Empty;
        _resizingComponentId = string.Empty;
        _selectedComponentId = string.Empty;
        if (clearEditMode)
        {
            _isEditMode = false;
            _isInteractiveHoldPinned = false;
        }

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (ReferenceEquals(Mouse.Captured, Root))
        {
            Mouse.Capture(null);
        }

        SelectionBox.Visibility = Visibility.Collapsed;
        HoverCell.Visibility = Visibility.Collapsed;
    }

    private void FlushPendingComponentState()
    {
        if (_pendingComponentState.Count == 0)
        {
            return;
        }

        _componentStateSaveTimer?.Stop();
        var pending = _pendingComponentState.ToArray();
        _pendingComponentState.Clear();

        var settings = AppSettingsStore.Load();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pending)
        {
            settings.Yanm.ComponentState[key] = value;
        }

        AppSettingsStore.Save(settings);
        _settings = AppSettingsStore.Load().Yanm;
        _mainWindow.NotifyQuickPanelSettingsChanged("yanm-component-state-flushed", refreshYanmOverlay: false);
        QueueWebDavLocalChangeSync("component-state-flushed");
        HostAssets.AppendLog($"Yanm: component state flushed, count={pending.Length}.");
    }

    private void ClearComponentInputFocus()
    {
        foreach (var view in _componentViews.Values)
        {
            try
            {
                view.Browser.CoreWebView2?.ExecuteScriptAsync("""
(function(){
  try {
    if (document.activeElement && document.activeElement.blur) {
      document.activeElement.blur();
    }
    window.__yanmSuppressNextInteractionFocusUntil = Date.now() + 800;
  } catch(e) {}
})();
""");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Yanm: clear component focus failed, component={view.Component.Title}, error={ex.Message}");
            }
        }

        Keyboard.ClearFocus();
        Focus();
        HostAssets.AppendLog("Yanm: component input focus cleared.");
    }

    private void EnterEditMode(string hint)
    {
        _isEditMode = true;
        _isPinned = true;
        _isInteractiveHoldPinned = false;
        HintText.Text = hint;
        UpdateDynamicTexts();
        UpdateCornerHint();
        DrawGrid();
    }

    private bool TryPromoteHeldTriggerToEditMode()
    {
        if (!IsVisible || _isEditMode || _isPinned || !KeyboardDoubleTapService.IsYanmTriggerHeld)
        {
            return false;
        }

        EnterEditMode("检测到按住触发键并点击鼠标，已切换到编辑模式。");
        HostAssets.AppendLog("Yanm: temporary overlay promoted to edit mode because trigger key was held during mouse click.");
        return true;
    }

    private void CornerHintPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EnterEditMode("已通过左上角提示进入编辑模式。");
        e.Handled = true;
    }

    private void SyncStatusPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPinned = true;
        _isInteractiveHoldPinned = false;
        UpdateCornerHint();
        FlushPendingComponentState();
        QueueWebDavStateRefresh("manual-click", force: true);
        e.Handled = true;
    }

    private void UpdateCornerHint()
    {
        if (CornerEditIcon != null)
        {
            CornerEditIcon.Foreground = new SolidColorBrush(
                _isEditMode
                    ? WpfColor.FromRgb(96, 165, 250)
                    : WpfColor.FromRgb(244, 244, 245));
        }

        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        if (string.Equals(activationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.CustomShortcut))
        {
            CornerHintText.Text = _isEditMode || _isPinned
                ? $"编辑模式 · Esc 退出 · {_settings.CustomShortcut} 隐藏"
                : $"点击画笔编辑 · 再按 {_settings.CustomShortcut} 隐藏";
            return;
        }

        CornerHintText.Text = _isEditMode || _isPinned
            ? "编辑模式 · Esc 退出"
            : $"点击画笔编辑 · 松开 {GetActivationKeyLabel()} 退出";
    }

    private void UpdateDynamicTexts()
    {
        if (WelcomeTitleText == null || WelcomeBodyText == null || WelcomeActionText == null || HintText == null)
        {
            return;
        }

        var keyLabel = GetActivationKeyLabel();
        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        var useCustom = string.Equals(activationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(_settings.CustomShortcut);
        var customShortcut = useCustom ? _settings.CustomShortcut.Trim() : string.Empty;

        WelcomeTitleText.Text = "欢迎使用燕幕";
        WelcomeBodyText.Text = "这是一个全局信息层：你可以把日历、待办、网页数据、脚本结果和 AI 生成的小组件放在任意位置。";
        WelcomeActionText.Text = useCustom
            ? $"按下 {customShortcut} 即可显示燕幕，再按一次隐藏；显示后可直接拖拽一块区域新建组件。"
            : $"按住 {keyLabel} 后直接拖拽一块区域，即可新建第一个组件。双击 {keyLabel} 可固定进入编辑模式。";

        if (!_isPinned && !_isEditMode)
        {
            HintText.Text = useCustom
                ? $"再次按 {customShortcut} 隐藏；也可以直接拖拽空白区域新建组件。"
                : BuildTemporaryHint();
        }

        UpdateCornerHint();
    }

    private string BuildPinnedHint()
    {
        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        if (string.Equals(activationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.CustomShortcut))
        {
            return $"燕幕已固定：拖拽空白区域新建 HTML 组件；再次按 {_settings.CustomShortcut} 可隐藏；右键组件可编辑或删除。";
        }

        return "燕幕已固定：拖拽空白区域新建 HTML 组件；右键组件可编辑或删除。";
    }

    private string BuildEditModeHint()
    {
        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        if (string.Equals(activationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.CustomShortcut))
        {
            return $"已进入燕幕编辑模式：完成框选后新建组件，Esc 关闭；再次按 {_settings.CustomShortcut} 也可隐藏。";
        }

        return "已进入燕幕编辑模式：完成框选后新建组件，Esc 关闭。";
    }

    private string BuildInteractiveHint()
    {
        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        if (string.Equals(activationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.CustomShortcut))
        {
            return $"燕幕已进入交互模式：可继续输入；按 Esc 关闭，再按 {_settings.CustomShortcut} 也可隐藏。";
        }

        return $"燕幕已进入交互模式：可以松开触发键继续输入；按 Esc 关闭。";
    }

    private string BuildTemporaryHint()
    {
        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        if (string.Equals(activationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_settings.CustomShortcut))
        {
            return $"再次按 {_settings.CustomShortcut} 隐藏；也可以直接拖拽空白区域新建组件。";
        }

        return $"松开 {GetActivationKeyLabel()} 隐藏；按住 {GetActivationKeyLabel()} 点击鼠标会进入编辑模式，也可以直接拖拽区域新建组件。";
    }

    private string GetActivationKeyLabel()
    {
        var activationKey = YanmActivationKeys.Normalize(_settings.ActivationKey);
        if (string.Equals(activationKey, YanmActivationKeys.CapsLock, StringComparison.OrdinalIgnoreCase))
        {
            return "CapsLock";
        }

        return "Win";
    }

    private void HideOverlay()
    {
        FlushPendingComponentState();
        StopWebDavVisibleRefreshTimer();
        if (_webDavLocalChangeSyncTimer?.IsEnabled == true)
        {
            _webDavLocalChangeSyncTimer.Stop();
            QueueWebDavStateRefresh("overlay-hidden", force: true);
        }
        _isPinned = false;
        _isInteractiveHoldPinned = false;
        ResetInteractionState(clearEditMode: true);
        Hide();
    }

    private void UpdateSelectionBox(WpfPoint a, WpfPoint b, bool snap)
    {
        var rect = snap ? SnapRect(BuildRect(a, b)) : BuildRect(a, b);
        Canvas.SetLeft(SelectionBox, rect.X);
        Canvas.SetTop(SelectionBox, rect.Y);
        SelectionBox.Width = rect.Width;
        SelectionBox.Height = rect.Height;
    }

    private Rect SnapRect(Rect rect)
    {
        var x = Snap(rect.X);
        var y = Snap(rect.Y);
        var right = Snap(rect.Right);
        var bottom = Snap(rect.Bottom);
        return new Rect(x, y, Math.Max(10, right - x), Math.Max(10, bottom - y));
    }

    private double Snap(double value)
    {
        var grid = Math.Max(5, _settings.GridSizePixels);
        return Math.Round(value / grid) * grid;
    }

    private Rect SnapCell(WpfPoint point)
    {
        var grid = Math.Max(5, _settings.GridSizePixels);
        return new Rect(Math.Floor(point.X / grid) * grid, Math.Floor(point.Y / grid) * grid, grid, grid);
    }

    private static T? FindAncestor<T>(DependencyObject? current, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T typed && (predicate == null || predicate(typed)))
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool IsInteractiveOverlaySource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement element &&
                (string.Equals(element.Name, "SyncStatusPanel", StringComparison.Ordinal) ||
                 string.Equals(element.Name, "CornerHintPanel", StringComparison.Ordinal)))
            {
                return true;
            }

            if (source is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.TextBox or
                System.Windows.Controls.ComboBox or
                System.Windows.Controls.ListBox or
                System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static Rect BuildRect(WpfPoint a, WpfPoint b)
    {
        return new Rect(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));
    }

    private static double Distance(WpfPoint a, WpfPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed class YanmComponentView(
        string id,
        Border frame,
        Grid host,
        WebView2 browser,
        Border selectionBorder)
    {
        public string Id { get; } = id;

        public Border Frame { get; } = frame;

        public Grid Host { get; } = host;

        public WebView2 Browser { get; } = browser;

        public Border SelectionBorder { get; } = selectionBorder;

        public YanmComponentSettings Component { get; set; } = new();

        public string Html { get; set; } = string.Empty;

        public bool Locked { get; set; }

        public bool WebMessageAttached { get; set; }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
