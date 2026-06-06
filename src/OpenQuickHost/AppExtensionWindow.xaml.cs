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
    private readonly CommandItem _command;
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
    }

    public static bool TryActivateExisting(CommandItem command)
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
                trackedWindow.IsLoaded)
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

        existingWindow.Dispatcher.Invoke(existingWindow.BringToFront);
        return true;
    }

    private async void AppExtensionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AppExtensionWindow_Loaded;
        await InitializeAsync();
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
            storage: {
              get: key => call('storage.get', { key }),
              put: (key, content, options) => call('storage.put', { key, content, scope: options && options.scope }),
              list: prefix => call('storage.list', { prefix }),
              delete: key => call('storage.delete', { key })
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
            "storage.get" => await StorageGetAsync(parameters),
            "storage.put" => await StoragePutAsync(parameters),
            "storage.list" => StorageList(parameters),
            "storage.delete" => StorageDelete(parameters),
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
        var result = await ExtensionStorageService.ReadTextAsync(_command.ExtensionId, key, "both");
        return new
        {
            found = result.Found,
            content = result.Content ?? string.Empty,
            source = result.Source
        };
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
