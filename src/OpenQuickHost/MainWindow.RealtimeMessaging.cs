using System.IO;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;
public partial class MainWindow
{
    private sealed record MobileExecutionReceipt(bool Completed, bool Success, string Output);
    private static string GetMobileExecutionReceiptPath(string key) {
        var directory = HostAssets.ResolveDataDirectoryPath("mobile-execution-receipts");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    }
    private static void SaveMobileExecutionReceipt(string path, MobileExecutionReceipt receipt) {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
        File.Move(temporary, path, true);
    }
    internal event Action<string, string>? MobileReceiptReceived;
    private readonly Dictionary<string, string> _mobileReceipts = new();
    internal string? GetMobileReceipt(string id) => _mobileReceipts.GetValueOrDefault(id);
    private async Task RunMobileWebSocketAsync(CancellationToken cancellationToken)
    {
        using var socket = await _cloudSyncClient!.ConnectDeviceRelayAsync(_desktopDeviceId!, cancellationToken);
        await Dispatcher.InvokeAsync(() => _mobileMessagePollTimer.Stop());
        HostAssets.AppendLog("Mobile bridge realtime connected; periodic D1 polling suspended.");
        await Dispatcher.InvokeAsync(AppExtensionWindow.NotifyAccountConnected);
        AccountEnvironmentSecretVault.Attach(_cloudSyncClient!);
        AccountEnvironmentSecretVault.QueueRestoreFromCloud();
        await PollMobileMessagesSafeAsync("websocket-resync");
        var buffer = new byte[16384];
        long lastSocketActivity = Stopwatch.GetTimestamp();
        int processingMessage = 0;
        using var heartbeatLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Cloudflare's device relay understands application-level "ping" / "pong". Unlike
        // the framework's transport keepalive this also detects a silently stalled relay.
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatLifetime.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    await Task.Delay(TimeSpan.FromSeconds(25), heartbeatLifetime.Token);
                    if (Volatile.Read(ref processingMessage) == 0 &&
                        Stopwatch.GetElapsedTime(Interlocked.Read(ref lastSocketActivity)) > TimeSpan.FromSeconds(90))
                    {
                        HostAssets.AppendLog("Mobile bridge websocket stale for 90s; reconnecting.");
                        socket.Abort();
                        break;
                    }
                    if (socket.State != WebSocketState.Open) break;
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping")),
                        WebSocketMessageType.Text, true, heartbeatLifetime.Token);
                }
            }
            catch (OperationCanceledException) when (heartbeatLifetime.IsCancellationRequested) { }
            catch (Exception error) when (!heartbeatLifetime.IsCancellationRequested)
            {
                HostAssets.AppendLog("Mobile bridge websocket heartbeat failed: " + error.GetType().Name);
                socket.Abort();
            }
        }, heartbeatLifetime.Token);
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var body = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Realtime connection closed");
                    if (body.Length + result.Count > 2 * 1024 * 1024) throw new IOException("Realtime frame exceeds limit");
                    body.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                Interlocked.Exchange(ref lastSocketActivity, Stopwatch.GetTimestamp());
                var text = Encoding.UTF8.GetString(body.ToArray());
                if (text == "pong") continue;
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type == "message")
                {
                    var message = JsonSerializer.Deserialize<DeviceMessageRecord>(root.GetProperty("message").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    Interlocked.Exchange(ref processingMessage, 1);
                    try
                    {
                        var handled = await HandleMobileDeviceMessageAsync(message);
                        if (handled.hasResult) await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId!, handled.success, handled.output, cancellationToken);
                        else await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId!, cancellationToken: cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref lastSocketActivity, Stopwatch.GetTimestamp());
                        Interlocked.Exchange(ref processingMessage, 0);
                    }
                }
                else if (type == "messages-ready" || type == "ready") await PollMobileMessagesSafeAsync("websocket-hint");
                else if (type == "external-access-ready")
                {
                    await Dispatcher.InvokeAsync(() => (System.Windows.Application.Current as App)?.NotifyExternalAccessReady());
                }
                else if (type == "sync-ready")
                {
                    var updatedByDeviceId = root.TryGetProperty("updatedByDeviceId", out var updatedByElement)
                        ? updatedByElement.GetString()
                        : null;
                    var objectId = root.TryGetProperty("objectId", out var objectIdElement)
                        ? objectIdElement.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(updatedByDeviceId) ||
                        !string.Equals(updatedByDeviceId, _desktopDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        ExtensionStorageService.NotifyAccountCloudChanged();
                        if (string.Equals(objectId, AccountEnvironmentSecretVault.ObjectId, StringComparison.Ordinal))
                            AccountEnvironmentSecretVault.QueueRestoreFromCloud();
                    }
                }
                else if (type == "receipt")
                {
                    var id = root.GetProperty("messageId").GetString()!;
                    var status = root.GetProperty("status").GetString()!;
                    await Dispatcher.InvokeAsync(() => {
                        if (_mobileReceipts.Count > 2048) _mobileReceipts.Clear();
                        _mobileReceipts[id] = status; MobileReceiptReceived?.Invoke(id, status);
                    });
                }
            }
        }
        finally
        {
            heartbeatLifetime.Cancel();
            try { await heartbeat; }
            catch (OperationCanceledException) { }
            await Dispatcher.InvokeAsync(() =>
            {
                _mobileMessagePollTimer.Interval = TimeSpan.FromSeconds(60);
                _mobileMessagePollTimer.Start();
            });
        }
    }
}
