using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // Read-only mobile endpoint API, protected by the host's existing API authentication.
    private async Task<bool> TryHandleMobileDeviceApiAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var listDevices = request.HttpMethod == "GET" && path == "/v1/me/devices";
        var readResult = request.HttpMethod == "GET" && parts.Length == 6 && parts[0] == "v1" && parts[1] == "me" && parts[2] == "devices" && parts[4] == "results";
        var readEnvironment = request.HttpMethod == "GET" && parts.Length == 6 && parts[0] == "v1" && parts[1] == "me" && parts[2] == "devices" && parts[4] == "environment";
        if (!listDevices && !readResult && !readEnvironment && (parts.Length != 5 || parts[0] != "v1" || parts[1] != "me" || parts[2] != "devices" ||
            parts[4] is not ("state" or "capabilities" or "invoke"))) return false;
        var app = System.Windows.Application.Current;
        var cloud = app == null ? null : await app.Dispatcher.InvokeAsync(() => (app.MainWindow as MainWindow)?.CloudSyncClient);
        if (cloud?.HasCredential != true) { await WriteJsonAsync(response, 401, new { error = "account_login_required" }); return true; }
        if (listDevices) { await WriteJsonAsync(response, 200, new { items = await cloud.ListPeerDevicesAsync() }); return true; }
        var deviceId = Uri.UnescapeDataString(parts[3]);
        if (readEnvironment)
        {
            await WriteJsonAsync(response, 200, await cloud.GetPeerEnvironmentAsync(deviceId, Uri.UnescapeDataString(parts[5])));
            return true;
        }
        if (readResult)
        {
            var message = await cloud.GetDeviceMessageAsync(Uri.UnescapeDataString(parts[5]));
            if (message == null || message.TargetDeviceId != deviceId || message.Kind != "capability.invoke")
                await WriteJsonAsync(response, 404, new { error = "capability_result_not_found" });
            else await WriteJsonAsync(response, 200, message);
            return true;
        }
        if (request.HttpMethod == "GET" && parts[4] != "invoke")
        {
            var descriptor = await cloud.GetPeerDescriptorAsync(deviceId);
            if (descriptor == null) { await WriteJsonAsync(response, 404, new { error = "device_not_found" }); return true; }
            var peer = descriptor.Value;
            var capabilities = peer.GetProperty("capabilities");
            var online = peer.TryGetProperty("online", out var active) && active.ValueKind == JsonValueKind.True;
            if (parts[4] == "capabilities")
                await WriteJsonAsync(response, 200, new { deviceId, online, source = "cloud-cache",
                    items = capabilities.TryGetProperty("capabilityCatalog", out var catalog) ? catalog : JsonSerializer.SerializeToElement(Array.Empty<object>()) });
            else
                await WriteJsonAsync(response, 200, new { deviceId, online, source = "cloud-cache", lastSeenAt = peer.GetProperty("lastSeenAt"),
                    snapshot = capabilities.TryGetProperty("stateSnapshot", out var state) ? state : (JsonElement?)null,
                    sync = capabilities.TryGetProperty("syncStatus", out var sync) ? sync : (JsonElement?)null });
            return true;
        }
        if (request.HttpMethod != "POST" || parts[4] != "invoke") { await WriteJsonAsync(response, 405, new { error = "method_not_allowed" }); return true; }
        var input = await ReadJsonBodyAsync(request);
        var name = GetString(input, "name") ?? "";
        var arguments = input.TryGetProperty("arguments", out var args) ? args.Clone() : JsonSerializer.SerializeToElement(new { });
        var supported = new[] { "mobile.status.get", "mobile.capabilities.list", "mobile.files.list", "mobile.files.read", "mobile.extensions.list", "mobile.sync.status", "mobile.data.read" };
        if (!supported.Contains(name)) { await WriteJsonAsync(response, 400, new { error = "unsupported_mobile_capability" }); return true; }
        var target = await cloud.GetPeerDescriptorAsync(deviceId);
        if (target == null || target.Value.GetProperty("platform").GetString() != "android") { await WriteJsonAsync(response, 404, new { error = "mobile_device_not_found" }); return true; }
        var advertised = target.Value.GetProperty("capabilities");
        if (!advertised.TryGetProperty("mobileCapabilityProtocol", out var protocol) || protocol.GetInt32() != 1)
        { await WriteJsonAsync(response, 409, new { error = "mobile_upgrade_required" }); return true; }
        var peerEndpoint = YanziPeerRegistry.Resolve(deviceId);
        if (peerEndpoint != null)
        {
            try
            {
                using var handler = new SecureLanHttpHandler();
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                using var content = new StringContent(JsonSerializer.Serialize(new { name, arguments }), Encoding.UTF8, "application/json");
                using var reply = await client.PostAsync($"http://{peerEndpoint.Address}:{peerEndpoint.Port}/v1/mobile/capabilities/invoke", content);
                using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                await WriteJsonAsync(response, (int)reply.StatusCode, new { deviceId, source = "lan", collectedAt = DateTimeOffset.UtcNow, data = json.RootElement.Clone() });
                return true;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            { HostAssets.AppendLog("Mobile capability LAN unavailable: " + error.GetType().Name); }
        }
        var id = await cloud.SendDeviceMessageAsync(DeviceIdentityStore.GetOrCreateDesktopDeviceId(), "android", "capability.invoke", "手机能力调用", "",
            deviceId, new { name, arguments }, expiresAt: DateTimeOffset.UtcNow.AddSeconds(30));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = await cloud.GetDeviceMessageAsync(id);
            if (message?.Status is "completed" or "failed" or "unknown" or "expired" or "cancelled")
            {
                await WriteJsonAsync(response, message.Status == "completed" ? 200 : 409, new { deviceId, source = "cloud", messageId = id,
                    status = message.Status, result = message.Payload.TryGetValue("executionResult", out var result) ? result : (JsonElement?)null });
                return true;
            }
            await Task.Delay(400);
        }
        await WriteJsonAsync(response, 202, new { deviceId, source = "cloud", messageId = id, status = "pending", resultUrl = "/v1/me/devices/" + Uri.EscapeDataString(deviceId) + "/results/" + Uri.EscapeDataString(id) });
        return true;
    }
}
