using System.IO;
using System.Text.Json;
using Yanzi.Core.TemporaryTransfer;

namespace OpenQuickHost;

/// <summary>
/// Opt-in file delivery. Native cloud sync, device notifications and existing LAN transfers remain unchanged.
/// A public upload must be explicitly authorized per invocation.
/// </summary>
public static class YanziTemporaryTransferCapabilityProvider
{
    private const string PlanningSchema = """
        {"type":"object","properties":{
          "source":{"type":"string","enum":["home","office","phone","other"]},
          "destination":{"type":"string","enum":["home","office","phone","other"]},
          "bytes":{"type":"integer","minimum":1},
          "allowPublicUpload":{"type":"boolean"},
          "domesticOnly":{"type":"boolean"},
          "preferredChannel":{"type":"string"}
        },"required":["source","destination","bytes"],"additionalProperties":false}
        """;

    private const string PublishSchema = """
        {"type":"object","properties":{
          "path":{"type":"string","minLength":1},
          "source":{"type":"string","enum":["home","office","phone","other"]},
          "destination":{"type":"string","enum":["home","office","phone","other"]},
          "allowPublicUpload":{"type":"boolean"},
          "domesticOnly":{"type":"boolean"},
          "preferredChannel":{"type":"string"},
          "targetDeviceId":{"type":"string","minLength":1},
          "backupCount":{"type":"integer","minimum":0,"maximum":2},
          "notifyTarget":{"type":"string"}
        },"required":["path","source","destination"],"additionalProperties":false}
        """;

    private const string ReceiveSchema = """
        {"type":"object","properties":{
          "receipt":{"type":"object"},
          "receipts":{"type":"array","items":{"type":"object"}}
        },"required":["receipt"],"additionalProperties":false}
        """;

    private static TransferChannel P2pChannel() =>
        YanziTemporaryTransferAdapters.LoadChannels().Single(c => c.Id == "easytier-p2p");

    private static async Task<object?> P2pStatusAsync(object? _)
    {
        if (YanziTransferServiceBridge.Installed)
            return await YanziTransferServiceBridge.StatusAsync();
        var profile = P2pChannel();
        var managed = YanziEasyTierSessionManager.IsOwned(profile);
        bool connected = false;
        if (managed)
            connected = await new TemporaryEasyTierP2pAdapter(YanziEasyTierSessionManager.IsOwned)
                .IsDirectAsync(profile, CancellationToken.None);
        return new { managed, connected, transport = connected ? "p2p/udp" : "unavailable",
            channelEnabled = profile.Enabled, zeroRelayFallback = true };
    }

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "files.temporary.channels",
            Description = "查看临时传输通道配置、容量和启用状态；不影响正式同步",
            Category = "files", Permissions = ["files.read"],
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = _ => Task.FromResult<object?>(new { channels = YanziTemporaryTransferAdapters.LoadChannels().Select(c =>
                new { c.Id, c.Kind, c.Region, c.Enabled, c.MaxFileBytes, c.SupportsUnattended, c.Note }) })
        };
        yield return new()
        {
            Name = "files.temporary.plan",
            Description = "按源设备、目标设备、容量及国内线路要求给临时文件渠道排序",
            Category = "files", Permissions = ["files.read"],
            InputSchema = YanziCapabilitySchema.Parse(PlanningSchema),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PlanAsync
        };
        yield return new()
        {
            Name = "files.temporary.publish",
            Description = "显式授权后按策略上传临时文件，失败时切换备用渠道并返回校验清单",
            Category = "files", Permissions = ["files.read", "device.message.send"],
            RiskLevel = "high",
            InputSchema = YanziCapabilitySchema.Parse(PublishSchema),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PublishAsync
        };
        yield return new()
        {
            Name = "files.temporary.receive",
            Description = "在燕子隔离收件箱内领取临时文件；大小与 SHA-256 双校验，不覆盖已有文件",
            Category = "files", Permissions = ["files.read", "files.write"],
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse(ReceiveSchema),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ReceiveAsync
        };
    }

    private static TransferEndpoint Endpoint(string? text)
    {
        if (!Enum.TryParse(text, ignoreCase: true, out TransferEndpoint value) ||
            !Enum.IsDefined(value))
            throw new ArgumentException("invalid_transfer_endpoint");
        return value;
    }

    private static TransferRequest BuildRequest(JsonElement json, long bytes) =>
        new(Endpoint(json.GetProperty("source").GetString()),
            Endpoint(json.GetProperty("destination").GetString()),
            bytes,
            json.TryGetProperty("allowPublicUpload", out var a) && a.ValueKind == JsonValueKind.True,
            json.TryGetProperty("domesticOnly", out var d) && d.ValueKind == JsonValueKind.True,
            json.TryGetProperty("preferredChannel", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null);

    private static TemporaryTransferCoordinator Engine() =>
        new(YanziTemporaryTransferAdapters.LoadChannels(), YanziTemporaryTransferAdapters.Create());

    private static Task<object?> PlanAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var request = BuildRequest(input, input.GetProperty("bytes").GetInt64());
        var rows = Engine().Plan(request);
        return Task.FromResult<object?>(new { direction = new { request.Source, request.Destination },
            request.Bytes, candidates = rows, eligible = rows.Count(x => x.Eligible) });
    }

    private static async Task<object?> PublishAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var file = Path.GetFullPath(input.GetProperty("path").GetString()!);
        if (!File.Exists(file)) throw new FileNotFoundException("temporary_file_missing", file);
        var request = BuildRequest(input, new FileInfo(file).Length);
        if (request.PreferredChannel == "easytier-p2p" ||
            YanziTemporaryTransferAdapters.LoadChannels().Any(c => c.Id == "easytier-p2p" && c.Enabled))
        {
            var targetDeviceId = input.TryGetProperty("targetDeviceId", out var targetDevice) &&
                targetDevice.ValueKind == JsonValueKind.String ? targetDevice.GetString() : null;
            if (string.IsNullOrWhiteSpace(OpenQuickHost.Sync.SyncSessionStore.Load()?.UserId))
                throw new UnauthorizedAccessException("p2p_account_login_required");
            var connected = false;
            if (string.IsNullOrWhiteSpace(targetDeviceId) &&
                YanziAccountTransferBootstrap.CanReuseConnectedWorker() &&
                YanziTransferServiceBridge.Installed)
            {
                var status = JsonSerializer.SerializeToElement(await YanziTransferServiceBridge.StatusAsync());
                connected = status.TryGetProperty("connected", out var current) &&
                    current.ValueKind == JsonValueKind.True;
            }
            if (!connected)
            {
                await YanziServiceModuleCatalog.EnsureCapabilityAsync("files.p2p.connect",
                    YanziCapabilityCaller.LocalAgent, targetDeviceQuery: targetDeviceId);
                object connectArguments = !string.IsNullOrWhiteSpace(targetDeviceId)
                    ? new { targetDeviceId } : new { };
                var connection = await YanziCapabilityInvocationService.InvokeAsync(
                    "files.p2p.connect", connectArguments, YanziCapabilityCaller.LocalAgent);
                if (!connection.Success) throw new IOException("p2p_account_connect_failed: " + connection.Error);
            }
        }
        var backups = input.TryGetProperty("backupCount", out var b) ? b.GetInt32() : 0;
        var result = await Engine().PublishWithBackupsAsync(file, request, backups, CancellationToken.None);
        object? notification = null;
        if (input.TryGetProperty("notifyTarget", out var target) &&
            target.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(target.GetString()))
        {
            // The recipient receives a receipt, not an unchecked executable or automatic install action.
            var text = "燕子临时文件：" + result.Receipts[0].FileName + "\n" +
                       JsonSerializer.Serialize(result.Receipts);
            var args = new Dictionary<string, object?> {
                ["target"] = target.GetString(), ["kind"] = "text", ["text"] = text, ["waitForAck"] = true
            };
            var status = await YanziCapabilityInvocationService.InvokeAsync(
                "chat.send", args, YanziCapabilityCaller.LocalAgent);
            notification = new { sent = status.Success, error = status.Error };
        }
        return new { Receipt = result.Receipts[0], result.Receipts, result.Attempts, notification, note = "receiver_must_verify_sha256" };
    }

    private static async Task<object?> ReceiveAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        IReadOnlyList<TransferReceipt> receipts;
        if (input.TryGetProperty("receipts", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            receipts = array.Deserialize<TransferReceipt[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("invalid_receipts");
        }
        else
        {
            var one = input.GetProperty("receipt").Deserialize<TransferReceipt>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("invalid_receipt");
            receipts = [one];
        }
        if (receipts.Any(r => r.ChannelId == "easytier-p2p"))
            await YanziServiceModuleCatalog.EnsureCapabilityAsync("files.p2p.connect", YanziCapabilityCaller.LocalAgent);
        var root = HostAssets.ResolveDataDirectoryPath("temporary-transfer/inbox");
        var output = await Engine().ReceiveFromCandidatesAsync(receipts, root, CancellationToken.None);
        return new { verified = true, output.FilePath, output.Bytes, output.Sha256, output.ChannelId };
    }
}
