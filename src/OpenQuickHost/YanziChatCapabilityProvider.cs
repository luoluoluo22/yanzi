using System.IO;
using System.Net.Http;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>Built-in, schema-described chat capabilities; no dynamic extension compilation.</summary>
public static class YanziChatCapabilityProvider
{
    private static readonly JsonElement ResultSchema = YanziCapabilitySchema.Parse("""
    {"type":"object","properties":{
      "messageId":{"type":"string"},"targetDeviceId":{"type":"string"},"targetName":{"type":"string"},
      "kind":{"type":"string"},"status":{"type":"string","enum":["pending","completed","failed","expired","cancelled","unknown"]},
      "delivered":{"type":"boolean"},"acked":{"type":"boolean"},"ackedAt":{"type":"string"},
      "waitTimedOut":{"type":"boolean"},"error":{"type":"string"}},
      "required":["messageId","targetDeviceId","targetName","kind","status","delivered","acked","ackedAt","waitTimedOut","error"]}
    """);

    public static IReadOnlyList<YanziCapabilityProviderDefinition> GetProviders() =>
    [
        new()
        {
            Name = "chat.send",
            Description = "向燕子手机发送文字、原图或文件；省略 target 时选择在线且最近活跃的 Android 手机。completed 才表示指定手机成功 ACK；pending 时用 chat.status 继续查询，勿重复发送。",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{
              "target":{"type":"string","description":"设备 ID、设备名称或本地配置的别名（例如 K70）；名称片段须唯一。省略则自动选择最近在线手机"},
              "text":{"type":"string","default":""},
              "filePath":{"type":"string","description":"本机文件绝对路径；图片扩展名自动识别为 photo，附件 1 字节至 30 MB"},
              "kind":{"type":"string","enum":["text","photo","file"]},
              "waitForAck":{"type":"boolean","default":true},
              "timeoutSeconds":{"type":"integer","minimum":1,"maximum":60,"default":30}},
              "additionalProperties":false}
            """),
            OutputSchema = ResultSchema,
            Permissions = ["device.message.send"], Category = "messaging",
            Handler = SendAsync
        },
        new()
        {
            Name = "chat.status",
            Description = "只读查询已发送消息及指定手机回执；不轮询手机队列，不修改 deliveredAt。等待超时后使用原 messageId 继续查询。",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{
              "messageId":{"type":"string","minLength":1},
              "targetDeviceId":{"type":"string","minLength":1,"description":"可省略，使用消息原指定手机；广播消息必须提供"},
              "waitForAck":{"type":"boolean","default":false},
              "timeoutSeconds":{"type":"integer","minimum":1,"maximum":60,"default":30}},
              "required":["messageId"],"additionalProperties":false}
            """),
            OutputSchema = ResultSchema,
            Permissions = ["device.message.read"], Category = "messaging",
            Handler = StatusAsync
        }
    ];

    private static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static bool WaitRequested(JsonElement input, bool fallback) =>
        input.TryGetProperty("waitForAck", out var value) ? value.GetBoolean() : fallback;
    private static int Timeout(JsonElement input) =>
        input.TryGetProperty("timeoutSeconds", out var value) ? value.GetInt32() : 30;

    internal static (string Kind, string? FilePath, string Text) ValidateContent(JsonElement input)
    {
        var text = GetString(input, "text") ?? "";
        var filePath = GetString(input, "filePath");
        if (string.IsNullOrWhiteSpace(filePath)) filePath = null;
        if (filePath != null)
        {
            if (!Path.IsPathFullyQualified(filePath)) throw new ArgumentException("filePath 必须是绝对路径。");
            filePath = Path.GetFullPath(filePath);
            if (!File.Exists(filePath)) throw new FileNotFoundException("发送文件不存在。", filePath);
            var size = new FileInfo(filePath).Length;
            if (size <= 0 || size > 30L * 1024 * 1024) throw new IOException("附件大小需为 1 字节至 30 MB。");
        }
        var kind = GetString(input, "kind") ?? (filePath == null ? "text" :
            Path.GetExtension(filePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" ? "photo" : "file");
        if (kind is not ("text" or "photo" or "file")) throw new ArgumentException("不支持的消息类型。");
        if (kind == "text" && filePath != null) throw new ArgumentException("文字消息不能携带文件。");
        if (kind == "text" && string.IsNullOrWhiteSpace(text)) throw new ArgumentException("文字消息不能为空。");
        if (kind is "photo" or "file" && filePath == null) throw new ArgumentException("图片或文件消息必须提供 filePath。");
        return (kind, filePath, text);
    }

    internal static PeerDeviceInfo SelectTarget(IEnumerable<PeerDeviceInfo> peers, string? query,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        var devices = peers.Where(d => string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (devices.Length == 0) throw new InvalidOperationException("账号下没有 Android 手机。");
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return devices.OrderByDescending(d => d.Online)
                .ThenByDescending(d => DateTimeOffset.TryParse(d.LastSeenAt, out var seen) ? seen : DateTimeOffset.MinValue)
                .ThenBy(d => d.DeviceId, StringComparer.Ordinal).First();
        var exact = devices.Where(d => string.Equals(d.DeviceId, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 0) exact = devices.Where(d => string.Equals(d.DisplayName, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 0 && aliases != null && aliases.TryGetValue(query, out var deviceId))
            exact = devices.Where(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 0 && !(aliases?.ContainsKey(query) ?? false))
            exact = devices.Where(d => d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return exact.Length switch
        {
            1 => exact[0],
            0 => throw new KeyNotFoundException($"未找到手机：{query}。请使用设备 ID 或配置别名。"),
            _ => throw new ArgumentException($"手机名称不唯一：{query}。请使用设备 ID。")
        };
    }

    private static IReadOnlyDictionary<string, string> LoadAliases()
    {
        var path = HostAssets.ResolveDataFilePath("chat-target-aliases.json");
        return File.Exists(path)
            ? new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [], StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<CloudSyncClient> GetCloudAsync()
    {
        var cloud = new CloudSyncClient(SyncConfigLoader.Load());
        await cloud.ReloadPersistedSessionAsync();
        if (!cloud.HasCredential || string.IsNullOrWhiteSpace(cloud.CurrentUserId))
            throw new InvalidOperationException("请先登录燕子账号。");
        return cloud;
    }

    private static async Task<object?> SendAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var content = ValidateContent(input);
        var cloud = await GetCloudAsync();
        var target = SelectTarget(await cloud.ListPeerDevicesAsync(), GetString(input, "target"), LoadAliases());
        var job = DesktopChatOutbox.Enqueue(cloud.CurrentUserId!, DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            Guid.NewGuid().ToString("N"), content.Kind, content.Text, content.FilePath, target.DeviceId);
        var messageId = await cloud.DeliverChatJobAsync(job);
        if (string.IsNullOrWhiteSpace(messageId)) throw new IOException("服务器未返回 messageId；请检查持久发件箱，勿重复发送。");
        var initial = new DeviceMessageRecord { MessageId = messageId, TargetDeviceId = target.DeviceId, Kind = content.Kind, Status = "pending" };
        return await ObserveAsync(initial, target.DeviceId, target.DisplayName, WaitRequested(input, true), Timeout(input),
            token => cloud.GetDeviceMessageAsync(messageId, token));
    }

    private static async Task<object?> StatusAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var cloud = await GetCloudAsync();
        var messageId = GetString(input, "messageId")!;
        var message = await cloud.GetDeviceMessageAsync(messageId) ?? throw new KeyNotFoundException("消息不存在。");
        var targetId = GetString(input, "targetDeviceId") ?? message.TargetDeviceId;
        if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("广播消息须提供 targetDeviceId。");
        var name = message.Receipts.FirstOrDefault(r => r.DeviceId == targetId)?.DisplayName ?? targetId;
        return await ObserveAsync(message, targetId, name, WaitRequested(input, false), Timeout(input),
            token => cloud.GetDeviceMessageAsync(messageId, token));
    }

    internal static ChatDeliveryResult MakeResult(DeviceMessageRecord message, string targetId, string targetName,
        bool timedOut = false, string? error = null)
    {
        // Account chat has per-device receipts. Global ACK may belong to another phone.
        var receipt = message.Receipts.FirstOrDefault(r => string.Equals(r.DeviceId, targetId, StringComparison.OrdinalIgnoreCase));
        var accountChat = message.Payload.TryGetValue("accountChat", out var shared) && shared.ValueKind == JsonValueKind.True;
        // Explicit device routing has no per-device receipt table; the server authorizes
        // only this target to ACK. Account chat must always use the per-device receipt.
        var direct = !accountChat && string.Equals(message.TargetDeviceId, targetId, StringComparison.OrdinalIgnoreCase);
        var ackedAt = receipt?.AckedAt ?? (direct ? message.AckedAt : null);
        var targetStatus = receipt?.Status ?? (direct ? message.Status : null);
        var successful = !string.IsNullOrWhiteSpace(ackedAt) && targetStatus is "acked" or "completed";
        var status = successful ? "completed" :
            targetStatus is "failed" or "expired" or "cancelled" or "unknown" ? targetStatus :
            message.Status is "expired" or "cancelled" ? message.Status : "pending";
        return new(message.MessageId, targetId, targetName, message.Kind, status,
            receipt != null || (message.TargetDeviceId == targetId && !string.IsNullOrWhiteSpace(message.DeliveredAt)),
            successful, ackedAt ?? "", timedOut, error ?? "");
    }

    internal static async Task<ChatDeliveryResult> ObserveAsync(DeviceMessageRecord initial, string targetId, string targetName,
        bool wait, int timeoutSeconds, Func<CancellationToken, Task<DeviceMessageRecord?>> poll)
    {
        var current = initial;
        if (!wait) return MakeResult(current, targetId, targetName);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        string? error = null;
        while (!deadline.IsCancellationRequested)
        {
            var result = MakeResult(current, targetId, targetName);
            if (result.status != "pending") return result;
            try
            {
                current = await poll(deadline.Token) ?? current;
                error = null;
                result = MakeResult(current, targetId, targetName);
                if (result.status != "pending") return result;
                await Task.Delay(500, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException or UnauthorizedAccessException)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                try { await Task.Delay(500, deadline.Token); }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
            }
        }
        return MakeResult(current, targetId, targetName, timedOut: true, error: error);
    }
}

public sealed record ChatDeliveryResult(string messageId, string targetDeviceId, string targetName, string kind,
    string status, bool delivered, bool acked, string ackedAt, bool waitTimedOut, string error);
