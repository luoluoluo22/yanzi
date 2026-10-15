using System.Net.Http;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed partial class CloudSyncClient
{
    /// <summary>
    /// Pushes short-lived AI preview frames directly to the account's realtime relay.
    /// This path deliberately does not enqueue in the durable D1 device-message outbox.
    /// </summary>
    public async Task SendTransientChatStreamAsync(
        string sourceDeviceId, string targetDeviceId, string content,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new { sourceDeviceId, targetDeviceId, content });
        using var request = CreateJsonRequest(
            HttpMethod.Post, "/v1/me/mobile/messages/stream", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }
}
