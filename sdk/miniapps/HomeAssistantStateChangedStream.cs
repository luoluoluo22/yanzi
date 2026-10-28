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
