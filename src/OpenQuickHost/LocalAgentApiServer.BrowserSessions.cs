using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private sealed class BrowserSession(WebSocket socket, int serial, string browserName)
    {
        public WebSocket Socket { get; } = socket;
        public string ConnectionId { get; } = $"browser-{serial}";
        public string BrowserName { get; } = browserName;
        public DateTime ConnectedUtc { get; } = DateTime.UtcNow;
        public SemaphoreSlim SendGate { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<WebSocket, BrowserSession> _browserSessions = new();
    private readonly ConcurrentDictionary<string, WebSocket> _browserTaskOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _browserRoutingLock = new();

    private BrowserSession? SelectBrowser(string? connectionId = null, string? browserName = null,
        bool preferEdge = false)
    {
        var online = _browserSessions.Values.Where(v => v.Socket.State == WebSocketState.Open).ToArray();
        if (!string.IsNullOrWhiteSpace(connectionId))
            return online.FirstOrDefault(v => v.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(browserName))
            return online.Where(v => v.BrowserName.Equals(browserName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.ConnectedUtc).FirstOrDefault();
        if (preferEdge)
        {
            var edge = online.FirstOrDefault(v => v.BrowserName == "Edge");
            if (edge != null) return edge;
        }
        return online.OrderBy(v => v.BrowserName == "Ego" ? 0 : v.BrowserName == "Edge" ? 1 : 2)
            .ThenByDescending(v => v.ConnectedUtc).FirstOrDefault();
    }

    private void RefreshPrimaryBrowser()
    {
        bool connected;
        lock (_browserRoutingLock)
        {
            var chosen = SelectBrowser();
            _activeBrowserSocket = chosen?.Socket;
            ConnectedBrowserName = chosen?.BrowserName ?? "";
            connected = chosen != null;
        }
        BrowserConnectionChanged?.Invoke(connected);
    }

    private void RegisterBrowser(WebSocket socket, int serial, string browser)
    {
        _browserSessions[socket] = new BrowserSession(socket, serial, browser);
        RefreshPrimaryBrowser();
    }

    private void DisconnectBrowser(WebSocket socket)
    {
        _browserSessions.TryRemove(socket, out _);
        foreach (var pair in _browserTaskOwners.ToArray())
        {
            if (!ReferenceEquals(pair.Value, socket)) continue;
            if (!_browserTaskOwners.TryRemove(pair.Key, out _)) continue;
            if (_pendingBrowserTasks.TryRemove(pair.Key, out var promise))
            {
                using var doc = JsonDocument.Parse(
                    "{\"status\":\"error\",\"message\":\"browser_connection_lost\",\"data\":null}");
                promise.TrySetResult(doc.RootElement.Clone());
            }
        }
        RefreshPrimaryBrowser();
    }

    private void RegisterPendingBrowserTask(string taskId, WebSocket socket,
        TaskCompletionSource<JsonElement> promise)
    {
        _browserTaskOwners[taskId] = socket;
        _pendingBrowserTasks[taskId] = promise;
    }

    private void ClearPendingBrowserTask(string taskId)
    {
        _browserTaskOwners.TryRemove(taskId, out _);
        _pendingBrowserTasks.TryRemove(taskId, out _);
    }

    private bool CompleteBrowserTask(WebSocket responder, string taskId, JsonElement payload)
    {
        if (!_browserTaskOwners.TryGetValue(taskId, out var owner) ||
            !ReferenceEquals(owner, responder))
            return false;
        if (!_browserTaskOwners.TryRemove(taskId, out _)) return false;
        if (_pendingBrowserTasks.TryRemove(taskId, out var promise))
            return promise.TrySetResult(payload.Clone());
        return false;
    }

    private async Task SendBrowserFrameAsync(WebSocket socket, byte[] bytes, CancellationToken cancellation)
    {
        if (!_browserSessions.TryGetValue(socket, out var session) ||
            socket.State != WebSocketState.Open)
            throw new InvalidOperationException("browser_connection_lost");
        await session.SendGate.WaitAsync(cancellation);
        try
        {
            if (socket.State != WebSocketState.Open ||
                !_browserSessions.ContainsKey(socket))
                throw new InvalidOperationException("browser_connection_lost");
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                true, cancellation);
        }
        finally
        {
            session.SendGate.Release();
        }
    }

    private string[] GetConnectedBrowserNames()
        => _browserSessions.Values
            .Where(v => v.Socket.State == WebSocketState.Open)
            .Select(v => v.BrowserName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToArray();

    public object[] ConnectedBrowserSessions => GetConnectedBrowserDetails();

    private object[] GetConnectedBrowserDetails()
    {
        var selected = SelectBrowser()?.ConnectionId;
        return _browserSessions.Values
            .Where(v => v.Socket.State == WebSocketState.Open)
            .OrderBy(v => v.ConnectedUtc)
            .Select(v => (object)new
            {
                connectionId = v.ConnectionId,
                browser = v.BrowserName,
                isDefault = v.ConnectionId == selected,
                connectedUtc = v.ConnectedUtc
            })
            .ToArray();
    }

    private static string? RouteValue(IReadOnlyDictionary<string, object?> payload, string key)
        => payload.TryGetValue(key, out var value) ? value?.ToString() : null;
}
