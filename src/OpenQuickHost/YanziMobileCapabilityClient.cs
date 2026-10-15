using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

internal static class YanziMobileCapabilityClient
{
    public static async Task<object?> InvokeAsync(string? targetQuery, string name, object? arguments, int timeoutSeconds = 25)
    {
        var (cloud, devices) = await YanziDeviceCapabilityProvider.LoadDevicesAsync();
        var target = string.IsNullOrWhiteSpace(targetQuery)
            ? YanziDeviceCapabilityProvider.ResolvePreferredPhone(devices)
            : YanziDeviceCapabilityProvider.Resolve(devices.Where(d => string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase)), targetQuery!);

        var peerEndpoint = YanziPeerRegistry.Resolve(target.DeviceId);
        if (peerEndpoint != null)
        {
            try
            {
                using var handler = new SecureLanHttpHandler();
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Min(8, timeoutSeconds)) };
                using var content = new StringContent(JsonSerializer.Serialize(new { name, arguments = arguments ?? new { } }), Encoding.UTF8, "application/json");
                using var reply = await client.PostAsync($"http://{peerEndpoint.Address}:{peerEndpoint.Port}/v1/mobile/capabilities/invoke", content);
                var body = await reply.Content.ReadAsStringAsync();
                using var json = JsonDocument.Parse(body);
                if (!reply.IsSuccessStatusCode) throw new InvalidOperationException("手机局域网能力调用失败：" + body);
                var root = json.RootElement;
                var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result)
                    ? result.Clone()
                    : root.Clone();
                return new { deviceId = target.DeviceId, deviceName = target.DisplayName, source = "lan", data };
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
            {
                HostAssets.AppendLog("Phone app capability LAN fallback: " + error.GetType().Name + ": " + error.Message);
            }
        }

        var messageId = await cloud.SendDeviceMessageAsync(
            DeviceIdentityStore.GetOrCreateDesktopDeviceId(), "android", "capability.invoke", "手机能力调用", "",
            target.DeviceId, new { name, arguments = arguments ?? new { } },
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, timeoutSeconds + 5)));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = await cloud.GetDeviceMessageAsync(messageId);
            if (message?.Status is "completed" or "failed" or "unknown" or "expired" or "cancelled")
            {
                var raw = message.Payload.TryGetValue("executionResult", out var result) ? result : (JsonElement?)null;
                var data = raw.HasValue ? NormalizeResult(raw.Value) : null;
                if (message.Status != "completed")
                    throw new InvalidOperationException($"手机能力调用失败：{message.Status}，{data}");
                return new { deviceId = target.DeviceId, deviceName = target.DisplayName, source = "cloud", messageId, data };
            }
            await Task.Delay(400);
        }

        return new { deviceId = target.DeviceId, deviceName = target.DisplayName, source = "cloud", messageId, status = "pending" };
    }

    private static object? NormalizeResult(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("success", out var success) &&
            success.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var output = result.TryGetProperty("output", out var outputElement)
                ? outputElement
                : default;
            if (!success.GetBoolean())
            {
                var message = output.ValueKind == JsonValueKind.String ? output.GetString() : result.ToString();
                throw new InvalidOperationException("手机能力执行失败：" + message);
            }
            return output.ValueKind == JsonValueKind.Undefined ? result.Clone() : ParseMaybeJson(output);
        }

        return ParseMaybeJson(result);
    }

    private static object? ParseMaybeJson(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return value.Clone();
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return text;
        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch
        {
            return text;
        }
    }
}
