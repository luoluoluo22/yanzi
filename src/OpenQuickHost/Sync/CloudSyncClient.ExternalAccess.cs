using System.Net.Http;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed partial class CloudSyncClient
{
    public async Task<JsonElement> ExternalAccessAsync(string path, object? body = null, CancellationToken cancellationToken = default, bool delete = false)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var account = CurrentUserId;
        if (account != SyncSessionStore.Load()?.UserId) throw new InvalidOperationException("账号已切换，请重新打开授权窗口。");
        using var response = body == null
            ? await SendAsyncWithFallback(delete ? HttpMethod.Delete : HttpMethod.Get, path, true, cancellationToken)
            : await SendJsonAsync(HttpMethod.Post, path, body, true, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        if (account != SyncSessionStore.Load()?.UserId) throw new InvalidOperationException("账号已切换，请重新打开授权窗口。");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }
}
