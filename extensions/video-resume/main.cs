using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    private const string Key = "video-resume-service";

    public static async Task<string> RunAsync(YanziActionContext context)
    {
        try
        {
            var existing = context.GetRegisteredObject?.Invoke(Key);
            if (existing != null)
            {
                existing.GetType().GetMethod("Activate")?.Invoke(existing, null);
                return "看视频小程序已在运行。";
            }
        }
        catch { }

        var service = new VideoResumeService(context);
        try
        {
            context.RegisterObject?.Invoke(Key, service);
            context.RegisterObject?.Invoke($"{context.ExtensionId}-window", service);
        }
        catch { }

        service.Start();

        bool startup =
            string.Equals(context.LaunchSource, "app-startup", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.LaunchSource, "startup", StringComparison.OrdinalIgnoreCase);

        if (!startup) service.Activate();

        await service.WaitAsync();

        try
        {
            context.RegisterObject?.Invoke(Key, null!);
            context.RegisterObject?.Invoke($"{context.ExtensionId}-window", null!);
        }
        catch { }

        return "看视频小程序已停止。";
    }
}

public sealed class VideoResumeService
{
    private readonly YanziActionContext _context;
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Dispatcher? _dispatcher;
    private DispatcherTimer? _timer;
    private WatchVideoWindow? _window;
    private TimeSlot? _slot;
    private string _suppressed = "";
    private bool _manual;
    private volatile bool _stop;
    private readonly RiceCookerFinishMonitor _riceMonitor;
    private CancellationTokenSource? _riceCancellation;
    private Task? _riceTask;

    public bool IsRunning => !_stop;

    public VideoResumeService(YanziActionContext context)
    {
        _context = context;
        _riceMonitor = new RiceCookerFinishMonitor(context.ExtensionDirectory);
    }

    public void Start()
    {
        var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _timer.Tick += (_, _) => Tick();
                _timer.Start();
                Tick();
                ready.Set();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Log("UI线程异常: " + ex);
                ready.Set();
                _stopped.TrySetResult(true);
            }
        })
        {
            IsBackground = true,
            Name = "YanziVideoResume"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(3000);
        _riceCancellation = new CancellationTokenSource();
        _riceTask = Task.Run(() => _riceMonitor.WatchAsync(
            () =>
            {
                if (!_stop && _dispatcher is { HasShutdownStarted: false } dispatcher)
                    dispatcher.BeginInvoke(new Action(async () =>
                    {
                        if (_stop) return;
                        Log("收到电饭煲 cancel 事件并确认保温转待机：直接续播。");
                        HideButton();
                        await OpenLatestFromRiceAsync();
                    }));
                return Task.CompletedTask;
            }, Log, _riceCancellation.Token));
        Log("服务已启动：午晚视频入口 + Home Assistant 事件订阅。");
    }

    public Task WaitAsync() => _stopped.Task;

    public void Stop()
    {
        if (_stop) return;
        _stop = true;
        try { _riceCancellation?.Cancel(); } catch { }
        if (_dispatcher != null && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.BeginInvoke(() =>
            {
                try { _timer?.Stop(); } catch { }
                try { _window?.Close(); } catch { }
                try { _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } catch { }
            });
        }
        _stopped.TrySetResult(true);
    }

    public void Quit() => Stop();
    public void Toggle() => Activate();
    public void Show() => Activate();

    public void Activate()
    {
        if (_dispatcher == null || _stop) return;
        _dispatcher.BeginInvoke(() =>
        {
            var now = DateTime.Now;
            if (!TimeSlot.TryActive(now, out var slot))
            {
                slot = now.Hour < 16 ? TimeSlot.Noon : TimeSlot.Evening;
                _manual = true;
            }
            else _manual = false;

            ShowButton(slot);
        });
    }

    public string Run(string input)
    {
        input = (input ?? "").Trim();
        if (input.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            Stop();
            return "已停止。";
        }
        Activate();
        return "已显示看视频按钮。";
    }

    private void Tick()
    {
        if (_stop) return;
        var now = DateTime.Now;
        if (TimeSlot.TryActive(now, out var slot))
        {
            _manual = false;
            if (_suppressed != slot.Key(now.Date)) ShowButton(slot);
            else HideButton();
        }
        else if (!_manual) HideButton();
        // Cooker actions are delivered by the Home Assistant WebSocket listener,
        // not by this 5-second UI window scheduling timer.
    }

    private async Task OpenLatestFromRiceAsync()
    {
        try
        {
            var match = await new HistoryFinder(_context.ExtensionDirectory, Log)
                .FindAsync(DateTime.Today);
            if (match == null)
            {
                Log("电饭煲取消保温后未找到可续播的视频历史记录。");
                return;
            }
            Log($"米家自动续播 {match.Browser}/{match.Profile} | {match.Title} | {match.Url}");
            Open(match);
            if (TimeSlot.TryActive(DateTime.Now, out var slot))
                _suppressed = slot.Key(DateTime.Today);
            _manual = false;
            HideButton();
        }
        catch (Exception ex) { Log("电饭煲自动打开失败: " + ex); }
    }

    private void ShowButton(TimeSlot slot)
    {
        _slot = slot;
        _window ??= new WatchVideoWindow(this);
        _window.SetText("看视频");
        _window.Position();
        if (!_window.IsVisible) _window.Show();
    }

    private void HideButton()
    {
        if (_window?.IsVisible == true) _window.Hide();
        _slot = null;
    }

    internal async Task ClickAsync()
    {
        if (_slot == null || _window == null) return;

        var slot = _slot;
        _window.SetBusy(true);
        _window.SetText("正在查找…");

        try
        {
            var match = await new HistoryFinder(_context.ExtensionDirectory, Log)
                .FindAsync(DateTime.Today);

            if (match == null)
            {
                _window.SetText("未找到");
                await Task.Delay(2000);
                if (_window.IsVisible) _window.SetText("看视频");
                return;
            }

            Log($"命中 {match.Browser}/{match.Profile} | {match.LocalTime:yyyy-MM-dd HH:mm:ss} | {match.Title} | {match.Url}");
            Open(match);

            _suppressed = slot.Key(DateTime.Today);
            _manual = false;
            HideButton();
        }
        catch (Exception ex)
        {
            Log("查找/打开失败: " + ex);
            _window.SetText("打开失败");
            await Task.Delay(2000);
            if (_window.IsVisible) _window.SetText("看视频");
        }
        finally
        {
            _window?.SetBusy(false);
        }
    }

    private static void Open(VideoMatch match)
    {
        if (!string.IsNullOrWhiteSpace(match.Exe) && File.Exists(match.Exe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = match.Exe,
                Arguments = "\"" + match.Url.Replace("\"", "%22") + "\"",
                UseShellExecute = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = match.Url,
                UseShellExecute = true
            });
        }
    }

    private void Log(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(_context.ExtensionDirectory, "debug.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { try { _context.Log(text); } catch { } }
    }
}

// Reusable miniapp event adapter: WebSocket auth, HA state_changed subscription,
// an initial get_states snapshot, entity allowlisting, heartbeats and reconnect.
// Appliance-specific decisions stay in the consumer, not in this transport.
// <YANZI_HA_EVENT_STREAM>
public sealed record YanziHaEntityUpdate(string EntityId, string State, JsonElement Attributes, bool IsBaseline);
public sealed record YanziHaSubscriptionSettings(Uri Uri, string Token, string[] EntityIds);

public static class YanziHaStateChangedStream
{
    internal static bool TryParseChange(JsonElement packet, IReadOnlySet<string> entities,
        out YanziHaEntityUpdate? update)
    {
        update = null;
        if (packet.ValueKind != JsonValueKind.Object
            || !packet.TryGetProperty("type", out var type)
            || type.GetString() != "event"
            || !packet.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.Number || id.GetInt32() != 1
            || !packet.TryGetProperty("event", out var ev)
            || !ev.TryGetProperty("event_type", out var kind)
            || kind.GetString() != "state_changed"
            || !ev.TryGetProperty("data", out var data)
            || !data.TryGetProperty("entity_id", out var entity)
            || entity.ValueKind != JsonValueKind.String) return false;
        string name = entity.GetString() ?? "";
        if (!entities.Contains(name)
            || !data.TryGetProperty("new_state", out var newState)
            || newState.ValueKind != JsonValueKind.Object
            || !newState.TryGetProperty("state", out var state)
            || state.ValueKind != JsonValueKind.String) return false;
        var attrs = newState.TryGetProperty("attributes", out var attributes)
            ? attributes.Clone() : default;
        update = new YanziHaEntityUpdate(name, state.GetString() ?? "", attrs, false);
        return true;
    }

    private static async Task SendAsync(ClientWebSocket socket, object value, CancellationToken stop)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text, true, stop).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadAsync(
        ClientWebSocket socket, CancellationToken stop, int seconds = 70)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var buffer = new byte[16384];
        using var stream = new MemoryStream();
        WebSocketReceiveResult frame;
        do
        {
            frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),
                timeout.Token).ConfigureAwait(false);
            if (frame.MessageType != WebSocketMessageType.Text)
                throw new IOException("WebSocket closed or sent unsupported data");
            if (stream.Length + frame.Count > 2 * 1024 * 1024)
                throw new IOException("Home Assistant event exceeds 2 MB");
            stream.Write(buffer, 0, frame.Count);
        } while (!frame.EndOfMessage);
        using var json = JsonDocument.Parse(stream.ToArray());
        return json.RootElement.Clone();
    }

    private static string Type(JsonElement message) =>
        message.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "";

    private static void ConfirmResult(JsonElement message, int expectedId)
    {
        if (Type(message) != "result"
            || !message.TryGetProperty("id", out var id) || id.GetInt32() != expectedId
            || !message.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new IOException("Home Assistant subscription/snapshot command failed");
    }

    public static async Task SubscribeAsync(
        Uri baseUri, string accessToken, IReadOnlyCollection<string> entityIds,
        Func<YanziHaEntityUpdate, Task> onChange,
        Action<bool, string>? onConnection, CancellationToken stop)
    {
        var allowed = new HashSet<string>(entityIds, StringComparer.Ordinal);
        if (allowed.Count == 0) throw new ArgumentException("At least one entity is required");
        if (baseUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Home Assistant must use HTTP or HTTPS");
        var endpoint = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/api/websocket", Query = "", Fragment = "",
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port
        }.Uri;

        int attempt = 0;
        while (!stop.IsCancellationRequested)
        {
            bool connected = false;
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    await socket.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
                }
                var hello = await ReadAsync(socket, stop, 12).ConfigureAwait(false);
                if (Type(hello) != "auth_required")
                    throw new IOException("Home Assistant did not request authentication");
                await SendAsync(socket, new { type = "auth", access_token = accessToken }, stop)
                    .ConfigureAwait(false);
                var auth = await ReadAsync(socket, stop, 12).ConfigureAwait(false);
                if (Type(auth) != "auth_ok") throw new IOException("Home Assistant authorization failed");

                await SendAsync(socket, new { id = 1, type = "subscribe_events",
                    event_type = "state_changed" }, stop).ConfigureAwait(false);
                var subscribed = await ReadAsync(socket, stop, 12).ConfigureAwait(false);
                ConfirmResult(subscribed, 1);

                // Subscribe BEFORE getting state; discard events during bootstrap.
                // This deliberately avoids retroactive triggers after reconnect.
                await SendAsync(socket, new { id = 2, type = "get_states" }, stop)
                    .ConfigureAwait(false);
                JsonElement snapshot;
                while (true)
                {
                    var msg = await ReadAsync(socket, stop, 15).ConfigureAwait(false);
                    if (Type(msg) == "result" && msg.TryGetProperty("id", out var id)
                        && id.GetInt32() == 2)
                    {
                        ConfirmResult(msg, 2);
                        snapshot = msg.GetProperty("result");
                        break;
                    }
                }
                if (snapshot.ValueKind != JsonValueKind.Array)
                    throw new IOException("Home Assistant get_states returned no array");
                foreach (var item in snapshot.EnumerateArray())
                {
                    if (!item.TryGetProperty("entity_id", out var name)) continue;
                    var entity = name.GetString() ?? "";
                    if (!allowed.Contains(entity)) continue;
                    var state = item.GetProperty("state").GetString() ?? "";
                    var attributes = item.TryGetProperty("attributes", out var attrs)
                        ? attrs.Clone() : default;
                    await onChange(new YanziHaEntityUpdate(entity, state, attributes, true))
                        .ConfigureAwait(false);
                }
                attempt = 0;
                connected = true;
                onConnection?.Invoke(true, "WebSocket subscribed to state_changed");

                using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var heartbeats = Task.Run(async () =>
                {
                    int pingId = 100;
                    try
                    {
                        while (!session.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(20), session.Token)
                                .ConfigureAwait(false);
                            await SendAsync(socket, new { id = pingId++, type = "ping" },
                                session.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (session.IsCancellationRequested) { }
                }, session.Token);
                try
                {
                    while (!session.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        var message = await ReadAsync(socket, session.Token).ConfigureAwait(false);
                        if (TryParseChange(message, allowed, out var changed) && changed != null)
                            await onChange(changed).ConfigureAwait(false);
                    }
                    if (!stop.IsCancellationRequested) throw new IOException("WebSocket disconnected");
                }
                finally
                {
                    session.Cancel();
                    try { await heartbeats.ConfigureAwait(false); } catch { }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                onConnection?.Invoke(false, "订阅失败：" + ex.GetType().Name + " / " + ex.Message);
            }
            finally
            {
                if (connected) onConnection?.Invoke(false, "连接已断开，等待重新订阅");
            }
            if (stop.IsCancellationRequested) break;
            var delay = TimeSpan.FromSeconds(Math.Min(45, (int)Math.Pow(2, Math.Min(++attempt, 5))));
            try { await Task.Delay(delay, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
// </YANZI_HA_EVENT_STREAM>

public sealed class RiceCookerFinishMonitor
{
    private readonly string _linkPath;
    private readonly string _homeLinkPath;
    private string _lastStatus = "";
    private string _armedCompletion = "";
    private bool _armed;
    private DateTimeOffset _warmObservedAt;

    public RiceCookerFinishMonitor(string extensionDirectory)
    {
        _linkPath = Path.Combine(extensionDirectory, "rice-cooker-link.json");
        _homeLinkPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "ExtensionStorage", "shutdown-timer", "smart-light-link.json");
    }

    public void ResetConnection()
    {
        _lastStatus = "";
        _armedCompletion = "";
        _armed = false;
        _warmObservedAt = default;
    }

    internal bool ObserveStatus(string status, string completionEvent,
        string runEventState, string runEventPayload, DateTimeOffset now)
    {
        bool recentCompletion = DateTimeOffset.TryParse(completionEvent,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var completed)
            && completed <= now.AddMinutes(1) && completed >= now.AddHours(-8);
        if (status == "保温中")
        {
            if (_lastStatus != status || _armedCompletion != completionEvent || !_armed)
                _warmObservedAt = now;
            _armed = recentCompletion;
            _armedCompletion = recentCompletion ? completionEvent : "";
            _lastStatus = status;
            return false;
        }
        bool nativeCancel = runEventPayload.StartsWith("cancel_", StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.TryParse(runEventState, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var canceledAt)
            && canceledAt >= _warmObservedAt
            && canceledAt >= completed
            && canceledAt <= now.AddMinutes(1)
            && canceledAt >= now.AddMinutes(-5);
        bool confirmed = status == "待机中" && _lastStatus == "保温中"
            && _armed && recentCompletion && nativeCancel
            && string.Equals(_armedCompletion, completionEvent, StringComparison.Ordinal);
        _armed = false;
        _armedCompletion = "";
        _warmObservedAt = default;
        _lastStatus = status;
        return confirmed;
    }

    internal static string ExtractRunStatus(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Object) return "";
        foreach (var property in attributes.EnumerateObject())
        {
            if (property.Name.Contains("EventRunStatus", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString() ?? "";
        }
        return "";
    }

    private static bool ValidEntity(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.Ordinal) && id.Length > prefix.Length
        && id.Length <= 160 && id[prefix.Length..].All(ch =>
            (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_');

    public YanziHaSubscriptionSettings? LoadConnection()
    {
        if (!File.Exists(_linkPath) || !File.Exists(_homeLinkPath)) return null;
        using var link = JsonDocument.Parse(File.ReadAllText(_linkPath, Encoding.UTF8));
        if (!link.RootElement.TryGetProperty("enabled", out var enabled)
            || enabled.ValueKind != JsonValueKind.True) return null;
        string completed = link.RootElement.GetProperty("eventEntityId").GetString() ?? "";
        string status = link.RootElement.GetProperty("statusEntityId").GetString() ?? "";
        string runStatus = link.RootElement.GetProperty("runStatusEventId").GetString() ?? "";
        if (!ValidEntity(completed, "event.") || !ValidEntity(status, "sensor.")
            || !ValidEntity(runStatus, "event."))
            throw new InvalidOperationException("Home Assistant 实体 ID 不合法");

        using var home = JsonDocument.Parse(File.ReadAllText(_homeLinkPath, Encoding.UTF8));
        var root = home.RootElement;
        if (!root.GetProperty("Enabled").GetBoolean()) return null;
        var url = root.GetProperty("HomeAssistantUrl").GetString()?.TrimEnd('/') ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Home Assistant 连接地址无效");
        var cipher = Convert.FromBase64String(root.GetProperty("ProtectedToken").GetString() ?? "");
        var clear = ProtectedData.Unprotect(cipher, Encoding.UTF8.GetBytes("YanziShutdownLight-v1"),
            DataProtectionScope.CurrentUser);
        string token;
        try { token = Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        return new YanziHaSubscriptionSettings(uri, token, new[] { completed, status, runStatus });
    }

    public async Task WatchAsync(Func<Task> onCanceled, Action<string> log, CancellationToken stop)
    {
        var data = new Dictionary<string, YanziHaEntityUpdate>(StringComparer.Ordinal);
        DateTime lastError = DateTime.MinValue;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var config = LoadConnection();
                if (config is not null)
                {
                    string finishedId = config.EntityIds[0];
                    string statusId = config.EntityIds[1];
                    string runId = config.EntityIds[2];
                    await YanziHaStateChangedStream.SubscribeAsync(
                        config.Uri, config.Token, config.EntityIds,
                        async change =>
                        {
                            data[change.EntityId] = change;
                            if (!data.TryGetValue(finishedId, out var finished)
                                || !data.TryGetValue(statusId, out var status)
                                || !data.TryGetValue(runId, out var run)) return;
                            bool trigger = ObserveStatus(status.State, finished.State, run.State,
                                ExtractRunStatus(run.Attributes), DateTimeOffset.Now);
                            if (trigger && !change.IsBaseline) await onCanceled().ConfigureAwait(false);
                        },
                        (isOpen, info) =>
                        {
                            if (!isOpen)
                            {
                                data.Clear();
                                ResetConnection();
                                if (DateTime.Now - lastError > TimeSpan.FromMinutes(5))
                                {
                                    lastError = DateTime.Now;
                                    log("HA 订阅状态：" + info);
                                }
                            }
                            else log("HA WebSocket 已连接，监听三个电饭煲实体。");
                        },
                        stop).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                ResetConnection();
                data.Clear();
                if (DateTime.Now - lastError > TimeSpan.FromMinutes(5))
                {
                    lastError = DateTime.Now;
                    log("HA 事件监听异常：" + ex.GetType().Name + " / " + ex.Message);
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}

public sealed class WatchVideoWindow : Window
{
    private readonly Border _card;
    private readonly TextBlock _text;
    private bool _busy;

    public WatchVideoWindow(VideoResumeService service)
    {
        Width = 118;
        Height = 48;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;

        _text = new TextBlock
        {
            Text = "看视频",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _card = new Border
        {
            Background = B("#E817171A"),
            BorderBrush = B("#3F3F46"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Cursor = Cursors.Hand,
            Child = _text,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.32
            }
        };

        _card.MouseEnter += (_, _) =>
        {
            if (!_busy) _card.Background = B("#F027272A");
        };
        _card.MouseLeave += (_, _) => _card.Background = B("#E817171A");
        _card.MouseLeftButtonUp += async (_, _) =>
        {
            if (!_busy) await service.ClickAsync();
        };

        Content = _card;
        Loaded += (_, _) => Position();
    }

    public void Position()
    {
        var a = SystemParameters.WorkArea;
        Left = Math.Max(a.Left + 8, a.Right - Width - 18);
        Top = Math.Max(a.Top + 8, a.Bottom - Height - 18);
    }

    public void SetText(string value) => _text.Text = value;

    public void SetBusy(bool value)
    {
        _busy = value;
        _card.Cursor = value ? Cursors.Arrow : Cursors.Hand;
        _card.Opacity = value ? 0.92 : 1.0;
    }

    private static SolidColorBrush B(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

public sealed class TimeSlot
{
    public static readonly TimeSlot Noon =
        new("午间", new TimeSpan(12, 0, 0), new TimeSpan(13, 0, 0));

    public static readonly TimeSlot Evening =
        new("晚间", new TimeSpan(19, 0, 0), new TimeSpan(20, 0, 0));

    public string Name { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }

    private TimeSlot(string name, TimeSpan start, TimeSpan end)
    {
        Name = name;
        Start = start;
        End = end;
    }

    public string Key(DateTime date) =>
        $"{date:yyyy-MM-dd}:{Start:hh\\:mm}-{End:hh\\:mm}";

    public static bool TryActive(DateTime now, out TimeSlot slot)
    {
        foreach (var x in new[] { Noon, Evening })
        {
            if (now.TimeOfDay >= x.Start && now.TimeOfDay < x.End)
            {
                slot = x;
                return true;
            }
        }
        slot = Noon;
        return false;
    }
}

public sealed class VideoMatch
{
    public string Browser { get; init; } = "";
    public string Profile { get; init; } = "";
    public string Exe { get; init; } = "";
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public DateTime LocalTime { get; init; }
    public long VisitTime { get; init; }
    public int Score { get; init; }
}

public sealed class HistorySource
{
    public string Browser { get; init; } = "";
    public string Profile { get; init; } = "";
    public string History { get; init; } = "";
    public string Exe { get; init; } = "";
}

public sealed class HistoryFinder
{
    public const int LookbackDays = 45;

    private readonly string _sqlite;
    private readonly Action<string> _log;

    public HistoryFinder(string extensionDir, Action<string> log)
    {
        _sqlite = Path.Combine(extensionDir, "sqlite3.exe");
        _log = log;
    }

    public Task<VideoMatch?> FindAsync(DateTime today) =>
        Task.Run(() => Find(today));

    private VideoMatch? Find(DateTime today)
    {
        if (!File.Exists(_sqlite))
            throw new FileNotFoundException("缺少 sqlite3.exe。", _sqlite);

        var all = new List<VideoMatch>();
        foreach (var source in Sources())
        {
            try
            {
                all.AddRange(Query(source, today));
            }
            catch (Exception ex)
            {
                _log($"读取 {source.Browser}/{source.Profile} 失败: {ex.Message}");
            }
        }

        // A recent multi-episode TV series is more likely the intended "resume" than
        // a single unrelated short video visited afterwards.
        var series = all
            .Where(x => x.LocalTime >= today.AddDays(-7) && SeriesKey(x) != null)
            .GroupBy(x => SeriesKey(x)!)
            .Where(g => g.Select(x => new Uri(x.Url).AbsolutePath)
                         .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .Select(g => g.OrderByDescending(x => x.VisitTime).First())
            .OrderByDescending(x => x.VisitTime)
            .FirstOrDefault();

        return series ?? all.OrderByDescending(x => x.VisitTime).FirstOrDefault();
    }

    private static string? SeriesKey(VideoMatch match)
    {
        if (!match.Title.Contains("电视剧", StringComparison.Ordinal)
            || !Uri.TryCreate(match.Url, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("v.qq.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var pieces = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length >= 4 && pieces[0] == "x" && pieces[1] == "cover"
            ? "qq:" + pieces[2] : null;
    }

    private IEnumerable<HistorySource> Sources()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var s in Chromium(
            "Edge",
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Existing(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"))))
            yield return s;

        foreach (var s in Chromium(
            "Chrome",
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Existing(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"))))
            yield return s;
    }

    private static IEnumerable<HistorySource> Chromium(
        string browser,
        string userData,
        string exe)
    {
        if (!Directory.Exists(userData)) yield break;

        var dirs = new List<string>();
        var def = Path.Combine(userData, "Default");
        if (Directory.Exists(def)) dirs.Add(def);

        try { dirs.AddRange(Directory.EnumerateDirectories(userData, "Profile *")); }
        catch { }

        foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var h = Path.Combine(d, "History");
            if (!File.Exists(h)) continue;
            yield return new HistorySource
            {
                Browser = browser,
                Profile = Path.GetFileName(d),
                History = h,
                Exe = exe
            };
        }
    }

    private List<VideoMatch> Query(HistorySource source, DateTime today)
    {
        string temp = Path.Combine(
            Path.GetTempPath(),
            "yanzi-video-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            Snapshot(source.History, temp);

            var psi = new ProcessStartInfo
            {
                FileName = _sqlite,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-readonly");
            psi.ArgumentList.Add("-json");
            psi.ArgumentList.Add(temp);
            psi.ArgumentList.Add(Sql(today));

            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 sqlite3.exe。");

            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();

            if (!p.WaitForExit(6000))
            {
                try { p.Kill(true); } catch { }
                throw new TimeoutException("读取历史超时。");
            }

            if (p.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "SQLite 查询失败。" : stderr.Trim());

            if (string.IsNullOrWhiteSpace(stdout)) return new List<VideoMatch>();

            using var doc = JsonDocument.Parse(stdout);
            var matches = new List<VideoMatch>();

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                string url = row.GetProperty("url").GetString() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https")) continue;

                string dt = row.GetProperty("local_dt").GetString() ?? "";
                if (!DateTime.TryParseExact(dt, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
                    continue;

                var m = new VideoMatch
                {
                    Browser = source.Browser,
                    Profile = source.Profile,
                    Exe = source.Exe,
                    Title = row.GetProperty("title").GetString() ?? "",
                    Url = url,
                    LocalTime = localTime,
                    VisitTime = row.GetProperty("visit_time").GetInt64(),
                    Score = row.GetProperty("page_score").GetInt32()
                };

                matches.Add(m);
            }

            return matches;
        }
        finally
        {
            foreach (var f in new[] { temp, temp + "-journal", temp + "-wal", temp + "-shm" })
                try { File.Delete(f); } catch { }
        }
    }

    private static void Snapshot(string source, string dest)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(true);
    }

    private static string Sql(DateTime today)
    {
        string t = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string min = today.AddDays(-LookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return $@"
WITH h AS (
  SELECT v.visit_time, COALESCE(u.title,'') AS title, u.url,
         datetime(v.visit_time/1000000-11644473600,'unixepoch','localtime') AS local_dt
  FROM visits v JOIN urls u ON u.id=v.url
)
SELECT visit_time,title,url,local_dt,
CASE
  WHEN lower(url) LIKE '%v.qq.com/x/%' THEN 120
  WHEN lower(url) LIKE '%bilibili.com/video/%' THEN 120
  WHEN lower(url) LIKE '%iqiyi.com/v_%' OR lower(url) LIKE '%iqiyi.com/a_%' THEN 120
  WHEN lower(url) LIKE '%v.youku.com/%' OR lower(url) LIKE '%youku.com/v_%' THEN 120
  WHEN lower(url) LIKE '%mgtv.com/b/%' OR lower(url) LIKE '%mgtv.com/l/%' THEN 120
  WHEN lower(url) LIKE '%youtube.com/watch%' OR lower(url) LIKE '%youtu.be/%' THEN 120
  WHEN lower(url) LIKE '%douyin.com/video/%' OR lower(url) LIKE '%kuaishou.com/short-video/%' THEN 120
  ELSE 60
END AS page_score
FROM h
WHERE date(local_dt) <= '{t}'
  AND date(local_dt) >= '{min}'
  AND (
       lower(url) LIKE '%v.qq.com/x/cover/%/%'
    OR lower(url) LIKE '%v.qq.com/x/page/%'
    OR lower(url) LIKE '%bilibili.com/video/%'
    OR lower(url) LIKE '%iqiyi.com/v_%'
    OR lower(url) LIKE '%iqiyi.com/a_%'
    OR lower(url) LIKE '%v.youku.com/v_show/%'
    OR lower(url) LIKE '%mgtv.com/b/%'
    OR lower(url) LIKE '%mgtv.com/l/%'
    OR lower(url) LIKE '%youtube.com/watch?%'
    OR lower(url) LIKE '%youtu.be/%'
    OR lower(url) LIKE '%douyin.com/video/%'
    OR lower(url) LIKE '%kuaishou.com/short-video/%'
  )
ORDER BY visit_time DESC
LIMIT 300;";
    }

    private static string Existing(params string[] paths) =>
        paths.FirstOrDefault(File.Exists) ?? "";
}
