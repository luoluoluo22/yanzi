using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private async Task HandleAccountRoute0Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        await WriteJsonAsync(response, 200, new { items = YanziPeerRegistry.List() });
        return;
    }

    private async Task HandleAccountRoute3Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        await WriteJsonAsync(response, 200, YanziDeviceMessageProtocol.Describe());
        return;
    }

    private async Task HandleAccountRoute10Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var clientText = GetString(payload, "text");
        bool isWrite = false;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("write", out var writeProp))
        {
            isWrite = writeProp.ValueKind == JsonValueKind.True;
        }

        string currentPcText = "";
        try
        {
            if (isWrite && !string.IsNullOrEmpty(clientText))
            {
                ClipboardService.SetText(clientText);
            }
            currentPcText = ClipboardService.GetText() ?? "";
        }
        catch (Exception ex)
        {
            currentPcText = "[错误] 无法访问 PC 剪贴板: " + ex.Message;
        }

        await WriteJsonAsync(response, 200, new
        {
            ok = true,
            text = currentPcText
        });
        return;
    }

    private async Task HandleAccountRoute15Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var deviceId = GetString(payload, "deviceId") ?? "android-lan";

        LastKnownMobileDeviceModel = MobileDeviceNameNormalizer.Normalize(deviceId);
        MobileDeviceConnected?.Invoke(LastKnownMobileDeviceModel);

        await WriteJsonAsync(response, 200, new
        {
            ok = true,
            source = "local-agent-api",
            deviceId
        });
        return;
    }

    private async Task HandleAccountRoute16Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var maxLinesStr = request.QueryString["maxLines"];
        if (!int.TryParse(maxLinesStr, out var maxLines) || maxLines <= 0)
        {
            maxLines = 1000;
        }
        var lines = HostAssets.ReadHostLogTailLines(1024 * 512, maxLines);
        await WriteJsonAsync(response, 200, new { ok = true, logs = lines });
        return;
    }

    private async Task HandleAccountRoute23Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var title = GetString(payload, "title") ?? "Yanzi";
        var message = GetString(payload, "message") ?? string.Empty;

        if (_onShowNotification != null)
        {
            await _onShowNotification(title, message);
        }

        await WriteJsonAsync(response, 200, new { ok = true });
        return;
    }

    private async Task HandleAccountRoute24Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        if (_onMobileMessage == null)
        {
            await WriteJsonAsync(response, 400, new { error = "not_supported" });
            return;
        }
        var payload = await ReadJsonBodyAsync(request);

        var sourceDeviceId = GetString(payload, "sourceDeviceId");
        if (payload.TryGetProperty("protocolVersion", out var protocolVersion) &&
            (protocolVersion.ValueKind != JsonValueKind.Number || !protocolVersion.TryGetInt32(out var version) || version != YanziDeviceMessageProtocol.Version))
        {
            await WriteJsonAsync(response, 426, new { error = "unsupported_message_protocol",
                details = new { supportedVersions = new[] { YanziDeviceMessageProtocol.Version } } });
            return;
        }
        var targetDeviceId = GetString(payload, "targetDeviceId");
        if (!string.IsNullOrEmpty(targetDeviceId) && targetDeviceId != DeviceIdentityStore.GetOrCreateDesktopDeviceId())
        {
            await WriteJsonAsync(response, 404, new { error = "target_device_not_found" });
            return;
        }
        if (!string.IsNullOrEmpty(sourceDeviceId))
        {
            if (request.RemoteEndPoint is { } remote && !IPAddress.IsLoopback(remote.Address))
                YanziPeerRegistry.ObserveAuthenticated(sourceDeviceId, remote.Address,
                    payload.TryGetProperty("notificationPort", out var notificationPort) && notificationPort.TryGetInt32(out var advertisedPort) ? advertisedPort : 42981,
                    GetString(payload, "sourceDeviceName") ?? sourceDeviceId);
            LastKnownMobileDeviceModel = MobileDeviceNameNormalizer.Normalize(sourceDeviceId);
            MobileDeviceConnected?.Invoke(LastKnownMobileDeviceModel);
        }

        var messageId = Guid.NewGuid().ToString("N");
        var message = new DeviceMessageRecord
        {
            MessageId = messageId,
            TraceId = GetString(payload, "traceId") ?? GetString(payload, "clientMessageId"),
            SourceDeviceId = sourceDeviceId ?? "lan",
            TargetDeviceId = targetDeviceId,
            TargetPlatform = GetString(payload, "targetPlatform") ?? "desktop",
            Kind = (GetString(payload, "kind") ?? "text").ToLowerInvariant(),
            Title = GetString(payload, "title") ?? "局域网消息",
            Text = GetString(payload, "text") ?? "",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            ExpiresAt = GetString(payload, "expiresAt") ?? DateTimeOffset.UtcNow.AddMinutes(2).ToString("O"),
            Payload = new Dictionary<string, JsonElement>()
        };

        if (payload.TryGetProperty("payload", out var payloadObj) && payloadObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in payloadObj.EnumerateObject())
            {
                message.Payload[prop.Name] = prop.Value;
            }
        }

        message.Payload["authorization"] = JsonSerializer.SerializeToElement(request.Headers["X-Yanzi-Capability-Grant"] is { } capGrant ?
            (object)new {type = "lan-pair", capability = capGrant} : new {type = request.Headers["X-Yanzi-Pair-Authority"] ?? "account-owner"});
        var logicalId = GetString(payload, "clientMessageId");
        if (!string.IsNullOrWhiteSpace(logicalId) &&
            !message.Payload.ContainsKey("clientOperationId"))
            message.Payload["clientOperationId"] = JsonSerializer.SerializeToElement(logicalId);
        var result = await _onMobileMessage(message);
        var completedMessage = CreateLocalMobileMessageDetail(message, result.success, result.output);
        _localMobileMessages[messageId] = completedMessage;
        TrimLocalMobileMessages();
        await WriteJsonAsync(response, 200, new { ok = true, messageId, success = result.success, output = result.output });
        return;
    }

    private async Task HandleAccountRoute25Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var messageId = Uri.UnescapeDataString(path["/v1/me/mobile/messages/".Length..]);
        if (_localMobileMessages.TryGetValue(messageId, out var message))
        {
            await WriteJsonAsync(response, 200, message);
            return;
        }

        await WriteJsonAsync(response, 404, new { error = "not_found" });
        return;
    }

    private async Task HandleAccountRoute33Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        await WriteJsonAsync(response, 200, GetWebDavConfigDto());
        return;
    }

    private async Task HandleAccountRoute34Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var settings = AppSettingsStore.Load();
        var secrets = PersonalSyncSecretStore.Load();
        await WriteJsonAsync(response, 200, new
        {
            ok = true,
            enabled = settings.PersonalSync.Enabled,
            provider = settings.PersonalSync.Provider,
            settings = settings.PersonalSync,
            secrets = secrets
        });
        return;
    }

    private async Task HandleAccountRoute35Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        if (_onTriggerSync != null)
        {
            _onTriggerSync();
        }

        await WriteJsonAsync(response, 200, new { ok = true, message = "sync triggered" });
        return;
    }

    private async Task HandleAccountRoute44Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/rename".Length]);
        var payload = await ReadJsonBodyAsync(request);
        var name = GetString(payload, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            await WriteJsonAsync(response, 400, new { error = "name_required" });
            return;
        }

        var command = LocalExtensionCatalog.RenameExtension(id, name);
        _onMutated(command.ExtensionId);
        await WriteJsonAsync(response, 200, new { item = ToDto(command) });
        return;
    }

    private async Task HandleAccountRoute45Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/shortcut".Length]);
        var payload = await ReadJsonBodyAsync(request);
        var shortcut = GetString(payload, "shortcut");
        var command = LocalExtensionCatalog.SetGlobalShortcut(id, shortcut);
        _onMutated(command.ExtensionId);
        await WriteJsonAsync(response, 200, new { item = ToDto(command) });
        return;
    }

    private async Task HandleAccountRoute53Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        if (_onGetMe == null)
        {
            await WriteJsonAsync(response, 400, new { error = "auth_not_supported" });
            return;
        }
        var me = await _onGetMe();
        await WriteJsonAsync(response, 200, new { ok = true, user = me });
        return;
    }
}
