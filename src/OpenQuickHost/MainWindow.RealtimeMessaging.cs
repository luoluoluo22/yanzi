using System.IO;
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
        await Dispatcher.InvokeAsync(() => _mobileMessagePollTimer.Interval = TimeSpan.FromSeconds(30));
        HostAssets.AppendLog("Mobile bridge realtime connected.");
        await Dispatcher.InvokeAsync(AppExtensionWindow.NotifyAccountConnected);
        await PollMobileMessagesSafeAsync("websocket-resync");
        var buffer = new byte[16384];
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
                var text = Encoding.UTF8.GetString(body.ToArray());
                if (text == "pong") continue;
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type == "message")
                {
                    var message = JsonSerializer.Deserialize<DeviceMessageRecord>(root.GetProperty("message").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    var handled = await HandleMobileDeviceMessageAsync(message);
                    if (handled.hasResult) await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId!, handled.success, handled.output, cancellationToken);
                    else await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId!, cancellationToken: cancellationToken);
                }
                else if (type == "messages-ready" || type == "ready") await PollMobileMessagesSafeAsync("websocket-hint");
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
        finally { await Dispatcher.InvokeAsync(() => _mobileMessagePollTimer.Interval = TimeSpan.FromSeconds(5)); }
    }
}
