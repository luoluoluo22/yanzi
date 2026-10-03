using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;

namespace OpenQuickHost;

public partial class AppExtensionWindow : Window
{
    private const int WmNchittest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int HtCaption = 2;
    private const int WmSyscommand = 0x0112;
    private const int ScSize = 0xF000;
    private const int ScMove = 0xF010;
    private const int ResizeBorderThicknessDips = 8;
    private static readonly object SingleInstanceGate = new();
    private static readonly Dictionary<string, WeakReference<AppExtensionWindow>> SingleInstanceWindows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<AppExtensionWindow> BackgroundWindows = [];
    private readonly CommandItem _command;
    private TaskCompletionSource<bool> _navigationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _initialInput;
    private readonly string _launchSource;
    private readonly AppExtensionDefinition _definition;
    private static CoreWebView2Environment? _environment;

    public AppExtensionWindow(CommandItem command, string? initialInput, string launchSource)
    {
        InitializeComponent();
        _command = command;
        _initialInput = initialInput ?? string.Empty;
        _launchSource = launchSource;
        _definition = command.App ?? throw new InvalidOperationException("应用扩展缺少 app 声明。");
        Title = command.Title;
        LoadingTitle.Text = $"正在打开 {command.Title}";
        Icon = command.IconSource ?? TryRenderVectorIcon(command.VectorIcon, command.AccentBrush) ?? TryLoadDefaultIcon();
        ApplyHostedWindowChrome();
        ApplyWindowMetrics();
        Loaded += AppExtensionWindow_Loaded;
        SourceInitialized += AppExtensionWindow_SourceInitialized;
        Closed += AppExtensionWindow_Closed;
        if (_definition.RunInBackground)
        {
            BackgroundWindows.Add(this);
            Closing += (_, e) => {
                if (System.Windows.Application.Current.MainWindow is MainWindow { AllowClose: true } || Dispatcher.HasShutdownStarted) return;
                e.Cancel = true;
                Hide();
            };
            if (launchSource == "app-startup") { WindowState = WindowState.Minimized; ShowActivated = false; ShowInTaskbar = false; }
        }
    }

    public static bool TryActivateExisting(CommandItem command, bool activate = true)
    {
        var definition = command.App;
        if (definition == null || !definition.SingleInstance)
        {
            return false;
        }

        AppExtensionWindow? existingWindow = null;
        var windowKey = GetSingleInstanceKey(command.ExtensionId);
        lock (SingleInstanceGate)
        {
            if (SingleInstanceWindows.TryGetValue(windowKey, out var reference) &&
                reference.TryGetTarget(out var trackedWindow) &&
                (trackedWindow.IsLoaded || BackgroundWindows.Contains(trackedWindow)))
            {
                existingWindow = trackedWindow;
            }
            else
            {
                SingleInstanceWindows.Remove(windowKey);
            }
        }

        if (existingWindow == null)
        {
            return false;
        }

        if (activate) existingWindow.Dispatcher.Invoke(existingWindow.BringToFront);
        return true;
    }

    // Generic resource handoff: the extension owns input validation and navigation.
    public static async Task<string> OpenResourceAsync(CommandItem command, string input)
    {
        AppExtensionWindow? window = TrackedWindows().FirstOrDefault(w => w._command.ExtensionId == command.ExtensionId);
        if (window == null) { window = new AppExtensionWindow(command, "", "handoff"); window.Show(); }
        else window.BringToFront();
        if (!command.App!.BridgeApis.Contains("handoff")) throw new InvalidOperationException("此小程序尚未支持接续打开");
        if (!await window._navigationReady.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new InvalidOperationException("小程序页面加载失败");
        for (var attempt = 0; attempt < 3; attempt++)
        {
        var expression = "(async()=>{for(let n=0;n<100&&!window.yanzi?.handoffHandler;n++)await new Promise(r=>setTimeout(r,100));if(!window.yanzi?.handoffHandler)throw Error('小程序打开接口未就绪');return await window.yanzi.handoffHandler(" + JsonSerializer.Serialize(input) + ");})()";
        var json = await window.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true })).WaitAsync(TimeSpan.FromSeconds(45));
        using var result = JsonDocument.Parse(json);
        if (result.RootElement.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.ToString());
        var value = result.RootElement.GetProperty("result").GetProperty("value");
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("navigate", out var redirect))
        {
            var root = Path.GetDirectoryName(window.ResolveEntryPath())!;
            var destination = Path.GetFullPath(Path.Combine(root, redirect.GetString() ?? ""));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !destination.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || !File.Exists(destination)) throw new InvalidOperationException("小程序页面路径无效");
            window.Browser.CoreWebView2.Navigate(new Uri(destination).AbsoluteUri);
            await Task.Delay(100);
            if (!await window._navigationReady.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new InvalidOperationException("小程序页面加载失败");
            continue;
        }
        window.BringToFront();
        return value.ToString();
        }
        throw new InvalidOperationException("小程序页面重定向过多");
    }

    private async Task<object?> HandoffDevicesAsync()
    {
        if (!_definition.BridgeApis.Contains("handoff")) throw new InvalidOperationException("未声明 handoff 桥接权限");
        var client = new Sync.CloudSyncClient(Sync.SyncConfigLoader.Load());
        var peers = await client.ListPeerDevicesAsync();
        return new { accountId = client.CurrentUserId, items = peers.Where(p => p.Platform == "desktop").Select(p => new { deviceId = p.DeviceId, displayName = p.DisplayName, online = p.Online }) };
    }
    private async Task<object?> HandoffOpenAsync(JsonElement parameters)
    {
        if (!_definition.BridgeApis.Contains("handoff")) throw new InvalidOperationException("未声明 handoff 桥接权限");
        var target = GetString(parameters, "targetDeviceId", true)!;
        var input = GetString(parameters, "input", true)!;
        var client = new Sync.CloudSyncClient(Sync.SyncConfigLoader.Load());
        if (input.Length > 4096 || GetString(parameters, "accountId", true) != client.CurrentUserId) throw new InvalidOperationException("账号已切换或打开参数过长");
        var peers = await client.ListPeerDevicesAsync();
        if (Sync.SyncSessionStore.Load()?.UserId != client.CurrentUserId) throw new InvalidOperationException("账号已切换");
        if (!peers.Any(p => p.DeviceId == target && p.Platform == "desktop")) throw new InvalidOperationException("目标电脑不存在");
        if (target == Sync.DeviceIdentityStore.GetOrCreateDesktopDeviceId()) return new { opened = true, output = await OpenResourceAsync(_command, input) };
        var id = await client.SendDeviceMessageAsync(Sync.DeviceIdentityStore.GetOrCreateDesktopDeviceId(), "desktop", "extension.handoff", "", "", target,
            new { extensionId = _command.ExtensionId, input, accountId = client.CurrentUserId, clientOperationId = Guid.NewGuid().ToString() }, expiresAt: DateTimeOffset.UtcNow.AddDays(1));
        return new { queued = true, messageId = id };
    }
    private async Task<object?> HandoffStatusAsync(JsonElement parameters)
    {
        if (!_definition.BridgeApis.Contains("handoff")) throw new InvalidOperationException("未声明 handoff 桥接权限");
        var client = new Sync.CloudSyncClient(Sync.SyncConfigLoader.Load());
        if (GetString(parameters, "accountId", true) != client.CurrentUserId) throw new InvalidOperationException("账号已切换");
        var message = await client.GetDeviceMessageAsync(GetString(parameters, "messageId", true)!);
        if (message == null || !message.Payload.TryGetValue("extensionId", out var extension) || extension.GetString() != _command.ExtensionId) throw new InvalidOperationException("接续请求不存在");
        return new { status = message.Status, payload = message.Payload };
    }

    public static void NotifyStorageChanged(string extensionId, string key)
    {
        foreach (var window in TrackedWindows().Where(w => w._command.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase)))
            window.DispatchStorageEvent("yanzi:storage-changed", new { key });
    }

    public static void NotifyAccountConnected()
    {
        foreach (var window in TrackedWindows()) window.DispatchStorageEvent("yanzi:account-connected", new { });
    }

    private static AppExtensionWindow[] TrackedWindows()
    {
        lock (SingleInstanceGate)
            return BackgroundWindows.Concat(SingleInstanceWindows.Values.Select(r => r.TryGetTarget(out var w) ? w : null)
                .OfType<AppExtensionWindow>()).Distinct().ToArray();
    }

    private async void DispatchStorageEvent(string name, object detail)
    {
        if (Browser.CoreWebView2 == null || !_definition.BridgeApis.Contains("storage")) return;
        try { await Browser.ExecuteScriptAsync($"window.dispatchEvent(new CustomEvent({JsonSerializer.Serialize(name)},{{detail:{JsonSerializer.Serialize(detail)}}}))"); }
        catch (Exception ex) { HostAssets.AppendLog($"Storage event delivery deferred: {ex.GetType().Name}"); }
    }

    private async void AppExtensionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AppExtensionWindow_Loaded;
        await InitializeAsync();
        if (_definition.RunInBackground)
        {
            if (_launchSource == "app-startup") { Hide(); WindowState = WindowState.Normal; }
        }
    }

    private void AppExtensionWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyDarkWindowTheme();
        AttachWindowHook();

        if (!_definition.SingleInstance)
        {
            return;
        }

        var windowKey = GetSingleInstanceKey(_command.ExtensionId);
        lock (SingleInstanceGate)
        {
            SingleInstanceWindows[windowKey] = new WeakReference<AppExtensionWindow>(this);
        }
    }

    private void AppExtensionWindow_Closed(object? sender, EventArgs e)
    {
        BackgroundWindows.Remove(this);
        Browser.Dispose();
        if (!_definition.SingleInstance)
        {
            return;
        }

        var windowKey = GetSingleInstanceKey(_command.ExtensionId);
        lock (SingleInstanceGate)
        {
            if (SingleInstanceWindows.TryGetValue(windowKey, out var reference) &&
                reference.TryGetTarget(out var trackedWindow) &&
                ReferenceEquals(trackedWindow, this))
            {
                SingleInstanceWindows.Remove(windowKey);
            }
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var entryPath = ResolveEntryPath();
            if (!File.Exists(entryPath))
            {
                ShowError($"找不到入口文件：{entryPath}");
                return;
            }

            await Browser.EnsureCoreWebView2Async(await GetEnvironmentAsync());
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 17, 17, 17);
            Browser.CoreWebView2.WebMessageReceived += Browser_WebMessageReceived;
            Browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CreateBridgeScript());
            Browser.CoreWebView2.Navigate(new Uri(entryPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AppExtensionWindow load failed: id={_command.ExtensionId}, error={ex}");
            ShowError(ex.Message);
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _navigationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HostAssets.AppendLog(
            $"AppExtensionWindow navigation starting: id={_command.ExtensionId}, uri={e.Uri}, userInitiated={e.IsUserInitiated}, redirected={e.IsRedirected}.");
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        Browser.Visibility = Visibility.Hidden;
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        HostAssets.AppendLog(
            $"AppExtensionWindow navigation completed: id={_command.ExtensionId}, success={e.IsSuccess}, status={e.WebErrorStatus}, uri={Browser.Source}.");
        _navigationReady.TrySetResult(e.IsSuccess);
        Browser.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    private static BitmapImage? TryLoadDefaultIcon()
    {
        try
        {
            if (!File.Exists(HostAssets.LogoPath))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(HostAssets.LogoPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryRenderVectorIcon(Geometry? geometry, System.Windows.Media.Brush accentBrush)
    {
        if (geometry == null)
        {
            return null;
        }

        try
        {
            const int size = 64;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRoundedRectangle(accentBrush, null, new Rect(0, 0, size, size), 14, 14);

                var bounds = geometry.Bounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    var scale = Math.Min(34 / bounds.Width, 34 / bounds.Height);
                    var offsetX = (size - bounds.Width * scale) / 2 - bounds.X * scale;
                    var offsetY = (size - bounds.Height * scale) / 2 - bounds.Y * scale;
                    context.PushTransform(new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY));
                    context.DrawGeometry(System.Windows.Media.Brushes.White, null, geometry);
                    context.Pop();
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyWindowMetrics()
    {
        Width = Math.Max(_definition.WindowWidth ?? Width, MinWidth);
        Height = Math.Max(_definition.WindowHeight ?? Height, MinHeight);
        MinWidth = Math.Max(_definition.MinWindowWidth ?? MinWidth, 480);
        MinHeight = Math.Max(_definition.MinWindowHeight ?? MinHeight, 360);
    }

    private void ApplyHostedWindowChrome()
    {
        ResizeOverlay.Visibility = Visibility.Collapsed;

        if (!_definition.HideTitleBar)
        {
            return;
        }

        var gutter = new Thickness(8);
        Browser.Margin = gutter;
        LoadingPanel.Margin = gutter;
        ErrorPanel.Margin = gutter;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = false
        });
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_definition.HideTitleBar || sender is not FrameworkElement { Tag: string tagText } || !int.TryParse(tagText, out var hit))
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        e.Handled = true;
        ReleaseCapture();
        _ = SendMessage(handle, WmSyscommand, (IntPtr)(ScSize + hit), IntPtr.Zero);
    }

    private void AttachWindowHook()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (HwndSource.FromHwnd(handle) is { } source)
        {
            source.AddHook(WndProc);
        }
    }

    private string ResolveEntryPath()
    {
        if (string.IsNullOrWhiteSpace(_command.ExtensionDirectoryPath))
        {
            throw new InvalidOperationException("应用扩展缺少扩展目录。");
        }

        var entry = (_definition.Entry ?? "app/index.html").Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_command.ExtensionDirectoryPath, entry));
        var extensionRoot = Path.GetFullPath(_command.ExtensionDirectoryPath);
        if (!fullPath.StartsWith(extensionRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("应用入口不能指向扩展目录外部。");
        }

        return fullPath;
    }

    private static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment != null)
        {
            return _environment;
        }

        var userDataFolder = HostAssets.ResolveDataDirectoryPath("AppWebView2");
        Directory.CreateDirectory(userDataFolder);
        _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        return _environment;
    }

    private string CreateBridgeScript()
    {
        var context = JsonSerializer.Serialize(new
        {
            extensionId = _command.ExtensionId,
            title = _command.Title,
            launchSource = _launchSource,
            inputText = _initialInput,
            storage = new
            {
                mode = _definition.StorageMode,
                engine = _definition.StorageEngine,
                sync = _definition.Sync,
                @namespace = GetStorageNamespace()
            }
        });

        return $$"""
        (() => {
          const applyYanziDarkSurface = () => {
            try {
              document.documentElement.style.backgroundColor = '#111111';
              document.documentElement.style.colorScheme = 'dark';
              if (document.body) {
                document.body.style.backgroundColor = '#111111';
              }
              let style = document.getElementById('yanzi-webview-dark-surface');
              if (!style) {
                style = document.createElement('style');
                style.id = 'yanzi-webview-dark-surface';
                style.textContent = 'html,body{background:#111111;color-scheme:dark;}';
                (document.head || document.documentElement).appendChild(style);
              }
            } catch (_) {}
          };
          applyYanziDarkSurface();
          document.addEventListener('DOMContentLoaded', applyYanziDarkSurface, { once: true });
          if (window.yanzi) return;
          const pending = new Map();
          let seq = 0;
          const context = {{context}};
          function postDebug(eventName, payload) {
            try {
              chrome.webview.postMessage({
                type: 'yanzi.debug',
                event: String(eventName || ''),
                payload: payload || null
              });
            } catch (_) {}
          }
          function call(method, params) {
            const id = String(++seq);
            chrome.webview.postMessage({ id, method, params: params || {} });
            return new Promise((resolve, reject) => {
              pending.set(id, { resolve, reject });
            });
          }
          chrome.webview.addEventListener('message', event => {
            const message = event.data || {};
            const request = pending.get(String(message.id));
            if (!request) return;
            pending.delete(String(message.id));
            if (message.ok) request.resolve(message.result);
            else request.reject(new Error(message.error || 'Yanzi bridge request failed'));
          });
          window.yanzi = {
            context,
            handoff: { devices: () => call('handoff.devices'), open: (targetDeviceId, input, accountId) => call('handoff.open', { targetDeviceId, input, accountId }), status: (messageId, accountId) => call('handoff.status', { messageId, accountId }) },
            storage: {
              get: (key, options) => call('storage.get', { key, scope: options && options.scope }),
              accountRead: key => call('storage.accountRead', { key }),
              accountWrite: (key, content, expectedRevision, accountId) => call('storage.accountWrite', { key, content, expectedRevision, accountId }),
              put: (key, content, options) => call('storage.put', { key, content, scope: options && options.scope }),
              list: prefix => call('storage.list', { prefix }),
              delete: key => call('storage.delete', { key })
            },
            capability: {
              list: () => call('capability.list'),
              describe: name => call('capability.describe', { name }),
              invoke: (name, payload) => call('capability.invoke', { name, payload })
            },
            sync: {
              status: () => call('sync.status'),
              now: () => call('sync.now')
            },
            env: {
              get: name => call('env.get', { name })
            },
            window: {
              startDrag: () => call('window.startDrag'),
              minimize: () => call('window.minimize'),
              toggleMaximize: () => call('window.toggleMaximize'),
              close: () => call('window.close'),
              isAlwaysOnTop: () => call('window.isAlwaysOnTop'),
              setAlwaysOnTop: value => call('window.setAlwaysOnTop', { value: !!value }),
              unminimize: () => call('window.unminimize')
            }
          };
          const originalHistoryBack = window.history.back ? window.history.back.bind(window.history) : null;
          const originalHistoryGo = window.history.go ? window.history.go.bind(window.history) : null;
          window.history.back = function () {
            postDebug('history.back.blocked', { href: window.location.href });
          };
          window.history.go = function (delta) {
            if (typeof delta === 'number' && delta < 0) {
              postDebug('history.go.blocked', { href: window.location.href, delta: delta });
              return;
            }
            if (originalHistoryGo) {
              return originalHistoryGo(delta);
            }
          };
          window.addEventListener('popstate', () => {
            postDebug('popstate', { href: window.location.href });
          });
          window.addEventListener('beforeunload', () => {
            postDebug('beforeunload', { href: window.location.href });
          });
          document.addEventListener('submit', event => {
            const form = event.target;
            postDebug('submit', {
              href: window.location.href,
              tagName: form && form.tagName ? String(form.tagName) : '',
              action: form && form.action ? String(form.action) : ''
            });
          }, true);
          document.addEventListener('click', event => {
            const target = event.target && event.target.closest
              ? event.target.closest('button, a, input[type="submit"]')
              : null;
            if (!target) {
              return;
            }
            postDebug('click', {
              href: window.location.href,
              tagName: target.tagName ? String(target.tagName) : '',
              type: target.type ? String(target.type) : '',
              text: target.innerText ? String(target.innerText).trim().slice(0, 80) : '',
              targetHref: target.href ? String(target.href) : ''
            });
          }, true);
        })();
        """;
    }

    private async void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? id = null;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var typeElement) &&
                string.Equals(typeElement.GetString(), "yanzi.debug", StringComparison.Ordinal))
            {
                var eventName = root.TryGetProperty("event", out var eventElement)
                    ? eventElement.GetString() ?? string.Empty
                    : string.Empty;
                var payload = root.TryGetProperty("payload", out var payloadElement)
                    ? payloadElement.GetRawText()
                    : "null";
                HostAssets.AppendLog($"AppExtensionWindow debug: id={_command.ExtensionId}, event={eventName}, payload={payload}");
                return;
            }
            id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement
                : default;

            var result = await HandleBridgeRequestAsync(method, parameters);
            PostBridgeResponse(id, true, result, null);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"App bridge request failed: id={_command.ExtensionId}, error={ex.Message}");
            PostBridgeResponse(id, false, null, ex.Message);
        }
    }

    private async Task<object?> HandleBridgeRequestAsync(string? method, JsonElement parameters)
    {
        return method switch
        {
            "handoff.devices" => await HandoffDevicesAsync(),
            "handoff.open" => await HandoffOpenAsync(parameters),
            "handoff.status" => await HandoffStatusAsync(parameters),
            "storage.get" => await StorageGetAsync(parameters),
            "storage.accountRead" => await AccountStorageAsync(parameters, false),
            "storage.accountWrite" => await AccountStorageAsync(parameters, true),
            "storage.put" => await StoragePutAsync(parameters),
            "storage.list" => StorageList(parameters),
            "storage.delete" => StorageDelete(parameters),
            "capability.list" => CapabilityList(),
            "capability.describe" => YanziCapabilityQueryService.Describe(GetString(parameters, "name", required: true)!)
                ?? throw new InvalidOperationException("能力不存在"),
            "capability.invoke" => await CapabilityInvokeAsync(parameters),
            "sync.status" => SyncStatus(),
            "sync.now" => SyncNow(),
            "env.get" => EnvGet(parameters),
            "window.startDrag" => await WindowStartDragAsync(),
            "window.minimize" => await WindowMinimizeAsync(),
            "window.toggleMaximize" => await WindowToggleMaximizeAsync(),
            "window.close" => await WindowCloseAsync(),
            "window.isAlwaysOnTop" => await WindowIsAlwaysOnTopAsync(),
            "window.setAlwaysOnTop" => await WindowSetAlwaysOnTopAsync(parameters),
            "window.unminimize" => await WindowUnminimizeAsync(),
            _ => throw new InvalidOperationException($"不支持的应用桥接方法：{method}")
        };
    }

    private async Task<object?> StorageGetAsync(JsonElement parameters)
    {
        var key = BuildStorageKey(GetString(parameters, "key", required: true));
        var result = await ExtensionStorageService.ReadTextAsync(_command.ExtensionId, key, GetString(parameters, "scope") ?? "both");
        return new
        {
            found = result.Found,
            content = result.Content ?? string.Empty,
            source = result.Source
        };
    }

    // Versioned account bridge for any WebView companion, scoped to its own identity/namespace.
    private async Task<object?> AccountStorageAsync(JsonElement parameters, bool write)
    {
        var key = BuildStorageKey(GetString(parameters, "key", required: true));
        var accountId = new Sync.CloudSyncClient(Sync.SyncConfigLoader.Load()).CurrentUserId;
        if (string.IsNullOrWhiteSpace(accountId)) throw new InvalidOperationException("请先登录燕子账号");
        if (!write)
        {
            var value = await Sync.AccountExtensionDataStore.TryReadAsync(_command.ExtensionId, key);
            if (!value.Available) throw new InvalidOperationException("账号同步暂不可用");
            if (new Sync.CloudSyncClient(Sync.SyncConfigLoader.Load()).CurrentUserId != accountId)
                throw new InvalidOperationException("账号已切换，未读取旧账号数据");
            return new { ok = true, exists = value.Exists, revision = value.Revision, content = value.Content, accountId };
        }
        var content = GetString(parameters, "content") ?? "";
        if (System.Text.Encoding.UTF8.GetByteCount(content) > 262144) throw new InvalidOperationException("同步对象超过 256 KiB");
        if (!parameters.TryGetProperty("expectedRevision", out var revision) || !revision.TryGetInt64(out var expectedRevision) || expectedRevision < 0)
            throw new InvalidOperationException("expectedRevision 必填");
        var expectedAccount = GetString(parameters, "accountId", required: true);
        if (expectedAccount != accountId) throw new InvalidOperationException("账号已切换，未写入");
        try
        {
            var value = await Sync.AccountExtensionDataStore.WriteAsync(_command.ExtensionId, key, content, expectedRevision, expectedAccountId: expectedAccount);
            if (!value.Available) throw new InvalidOperationException("账号同步暂不可用");
            return new { ok = true, revision = value.Revision, accountId };
        }
        catch (Sync.CloudSyncRevisionConflictException) { return new { ok = false, conflict = true, accountId }; }
    }

    private async Task<object?> StoragePutAsync(JsonElement parameters)
    {
        var key = BuildStorageKey(GetString(parameters, "key", required: true));
        var content = GetString(parameters, "content") ?? string.Empty;
        var scope = GetString(parameters, "scope") ?? (_definition.Sync.Equals("webdav", StringComparison.OrdinalIgnoreCase) ? "both" : "local");
        var result = await ExtensionStorageService.WriteTextAsync(_command.ExtensionId, key, content, scope);
        return new
        {
            ok = true,
            localPath = result.LocalPath,
            scope = result.Scope,
            cloudMessage = result.CloudMessage
        };
    }

    private object StorageList(JsonElement parameters)
    {
        var prefix = NormalizeRelativeStorageKey(GetString(parameters, "prefix") ?? string.Empty);
        var root = ExtensionStorageService.GetExtensionStorageDirectoryPath(_command.ExtensionId);
        var basePath = Path.Combine(root, GetStorageNamespace());
        if (!Directory.Exists(basePath))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(basePath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => string.IsNullOrWhiteSpace(prefix) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private object StorageDelete(JsonElement parameters)
    {
        var key = BuildStorageKey(GetString(parameters, "key", required: true));
        var root = ExtensionStorageService.GetExtensionStorageDirectoryPath(_command.ExtensionId);
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("存储路径越界。");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new { ok = true };
    }

    private object CapabilityList()
    {
        return new
        {
            capabilities = YanziCapabilityRegistry.List()
        };
    }

    private async Task<object?> CapabilityInvokeAsync(JsonElement parameters)
    {
        var name = GetString(parameters, "name", required: true)!;
        var payload = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("payload", out var payloadElement)
            ? (object)payloadElement.Clone()
            : null;
        var result = await YanziCapabilityInvocationService.InvokeAsync(name, payload,
            new YanziCapabilityCaller(_command.ExtensionId, _command.Permissions));
        if (!result.Success) throw new InvalidOperationException(result.Error);
        return result.Data;
    }

    private object SyncStatus()
    {
        var settings = AppSettingsStore.Load();
        return new
        {
            enabled = settings.EnableWebDavSync,
            provider = _definition.Sync,
            configured = settings.EnableWebDavSync && !string.IsNullOrWhiteSpace(settings.WebDavServerUrl)
        };
    }

    private object SyncNow()
    {
        return new { queued = true, message = "应用数据采用保存即入队的本地优先同步。" };
    }

    private object EnvGet(JsonElement parameters)
    {
        var name = GetString(parameters, "name", required: true);
        var value = AppEnvironmentVariableStore.GetValue(name);
        if (string.IsNullOrEmpty(value))
        {
            if (string.Equals(name, "HOST_GITHUB_TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                value = Sync.PersonalSyncSecretStore.Load()?.GitHubToken ?? string.Empty;
            }
            else if (string.Equals(name, "HOST_GITEE_TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                value = Sync.PersonalSyncSecretStore.Load()?.GiteeToken ?? string.Empty;
            }
        }
        return new
        {
            name,
            value = value ?? string.Empty
        };
    }


    private async Task<object?> WindowStartDragAsync()
    {
        var started = await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                ReleaseCapture();
                _ = SendMessage(handle, WmSyscommand, (IntPtr)(ScMove + HtCaption), IntPtr.Zero);
                return true;
            }
            catch
            {
                return false;
            }
        });

        return new { ok = started };
    }

    private async Task<object?> WindowMinimizeAsync()
    {
        await Dispatcher.InvokeAsync(() => WindowState = WindowState.Minimized);
        return new { ok = true };
    }

    private async Task<object?> WindowToggleMaximizeAsync()
    {
        var maximized = await Dispatcher.InvokeAsync(() =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return WindowState == WindowState.Maximized;
        });

        return new { ok = true, maximized };
    }

    private async Task<object?> WindowCloseAsync()
    {
        await Dispatcher.InvokeAsync(Close);
        return new { ok = true };
    }

    private async Task<object?> WindowIsAlwaysOnTopAsync()
    {
        return await Dispatcher.InvokeAsync(() => Topmost);
    }

    private async Task<object?> WindowSetAlwaysOnTopAsync(JsonElement parameters)
    {
        var value = false;
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("value", out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
        }

        await Dispatcher.InvokeAsync(() => Topmost = value);
        return new { ok = true, value };
    }

    private async Task<object?> WindowUnminimizeAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            BringToFront();
        });

        return new { ok = true };
    }

    private string BuildStorageKey(string? key, bool allowEmpty = false)
    {
        var normalizedKey = NormalizeRelativeStorageKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) && !allowEmpty)
        {
            throw new InvalidOperationException("storage key 不能为空。");
        }

        var storageNamespace = GetStorageNamespace();
        return string.IsNullOrWhiteSpace(normalizedKey)
            ? storageNamespace
            : $"{storageNamespace}/{normalizedKey}";
    }

    private static string NormalizeRelativeStorageKey(string? key)
    {
        return (key ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private string GetStorageNamespace()
    {
        var value = string.IsNullOrWhiteSpace(_definition.Namespace)
            ? "app"
            : _definition.Namespace.Replace('\\', '/').Trim('/');
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
        {
            return "app";
        }

        return string.Join("/", segments);
    }

    private static string? GetString(JsonElement element, string propertyName, bool required = false)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind != JsonValueKind.Null &&
            property.ValueKind != JsonValueKind.Undefined)
        {
            return property.GetString();
        }

        if (required)
        {
            throw new InvalidOperationException($"缺少参数：{propertyName}");
        }

        return null;
    }

    private void PostBridgeResponse(string? id, bool ok, object? result, string? error)
    {
        if (Browser.CoreWebView2 == null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            id,
            ok,
            result,
            error
        });
        Browser.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void ShowError(string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void BringToFront()
    {
        ShowInTaskbar = true;
        ShowActivated = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Focus();
        Topmost = true;
        Topmost = false;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(handle);
        }
    }

    private void ApplyDarkWindowTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
    }

    private static string GetSingleInstanceKey(string extensionId)
    {
        return string.IsNullOrWhiteSpace(extensionId) ? string.Empty : extensionId.Trim();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_definition.HideTitleBar || msg != WmNchittest || WindowState == WindowState.Maximized)
        {
            return IntPtr.Zero;
        }

        var screenX = (short)(lParam.ToInt32() & 0xFFFF);
        var screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
        var point = PointFromScreen(new System.Windows.Point(screenX, screenY));

        var onLeft = point.X >= 0 && point.X <= ResizeBorderThicknessDips;
        var onRight = point.X <= ActualWidth && point.X >= ActualWidth - ResizeBorderThicknessDips;
        var onTop = point.Y >= 0 && point.Y <= ResizeBorderThicknessDips;
        var onBottom = point.Y <= ActualHeight && point.Y >= ActualHeight - ResizeBorderThicknessDips;

        if (onTop && onLeft)
        {
            handled = true;
            return (IntPtr)HtTopLeft;
        }

        if (onTop && onRight)
        {
            handled = true;
            return (IntPtr)HtTopRight;
        }

        if (onBottom && onLeft)
        {
            handled = true;
            return (IntPtr)HtBottomLeft;
        }

        if (onBottom && onRight)
        {
            handled = true;
            return (IntPtr)HtBottomRight;
        }

        if (onLeft)
        {
            handled = true;
            return (IntPtr)HtLeft;
        }

        if (onRight)
        {
            handled = true;
            return (IntPtr)HtRight;
        }

        if (onTop)
        {
            handled = true;
            return (IntPtr)HtTop;
        }

        if (onBottom)
        {
            handled = true;
            return (IntPtr)HtBottom;
        }

        handled = false;
        return (IntPtr)HtClient;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
