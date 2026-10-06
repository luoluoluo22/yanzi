using System.IO;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class YanziDeviceCapabilityProvider
{
    private static string AliasPath => HostAssets.ResolveDataFilePath("chat-target-aliases.json");

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "device.list",
            Description = "列出当前燕子账号已连接设备、在线状态和本地别名",
            Permissions = ["device.read"], Category = "devices",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"items":{"type":"array"}},"required":["items"]}"""),
            Handler = ListAsync
        };
        yield return new()
        {
            Name = "device.status",
            Description = "查询指定燕子设备的在线状态；支持设备 ID、名称或本地别名",
            Permissions = ["device.read"], Category = "devices",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"target":{"type":"string","minLength":1}},"required":["target"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };
        yield return new()
        {
            Name = "device.alias.set",
            Description = "为燕子设备设置本地自然语言别名，例如“我的手机”“开发手机”",
            Permissions = ["device.write"], Category = "devices", RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"alias":{"type":"string","minLength":1},"target":{"type":"string","minLength":1}},"required":["alias","target"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = AliasAsync
        };
    }

    internal static IReadOnlyDictionary<string,string> LoadAliases()
    {
        try
        {
            if (!File.Exists(AliasPath)) return new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string,string>(
                JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(AliasPath)) ?? [],
                StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); }
    }

    internal static async Task<(CloudSyncClient Cloud, PeerDeviceInfo[] Devices)> LoadDevicesAsync()
    {
        var cloud = new CloudSyncClient(SyncConfigLoader.Load());
        await cloud.ReloadPersistedSessionAsync();
        if (!cloud.HasCredential || string.IsNullOrWhiteSpace(cloud.CurrentUserId))
            throw new InvalidOperationException("请先登录燕子账号。");
        return (cloud, (await cloud.ListPeerDevicesAsync()).ToArray());
    }

    internal static PeerDeviceInfo Resolve(IEnumerable<PeerDeviceInfo> source, string query)
    {
        var devices = source.ToArray();
        var aliases = LoadAliases();
        query = query.Trim();
        if (aliases.TryGetValue(query, out var aliased)) query = aliased;
        var matches = devices.Where(d => string.Equals(d.DeviceId, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            matches = devices.Where(d => string.Equals(d.DisplayName, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            matches = devices.Where(d => d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new KeyNotFoundException("未找到设备：" + query),
            _ => throw new ArgumentException("设备名称不唯一，请使用设备 ID：" + query)
        };
    }

    internal static PeerDeviceInfo ResolvePreferredPhone(IEnumerable<PeerDeviceInfo> source)
        => source.Where(d => string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Online)
            .ThenByDescending(d => DateTimeOffset.TryParse(d.LastSeenAt, out var seen) ? seen : DateTimeOffset.MinValue)
            .FirstOrDefault() ?? throw new InvalidOperationException("账号下没有 Android 手机。");

    private static async Task<object?> ListAsync(object? _)
    {
        var (_, devices) = await LoadDevicesAsync();
        var aliases = LoadAliases();
        return new
        {
            items = devices.Select(d => new
            {
                deviceId = d.DeviceId, displayName = d.DisplayName, platform = d.Platform,
                online = d.Online, lastSeenAt = d.LastSeenAt, lastLocation = d.LastLocation,
                aliases = aliases.Where(a => string.Equals(a.Value, d.DeviceId, StringComparison.OrdinalIgnoreCase)).Select(a => a.Key).ToArray()
            }).ToArray()
        };
    }

    private static async Task<object?> StatusAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var (_, devices) = await LoadDevicesAsync();
        var d = Resolve(devices, input.GetProperty("target").GetString()!);
        return new { deviceId=d.DeviceId, displayName=d.DisplayName, platform=d.Platform, online=d.Online, lastSeenAt=d.LastSeenAt, lastLocation=d.LastLocation };
    }

    private static async Task<object?> AliasAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        var alias=input.GetProperty("alias").GetString()!.Trim();
        if (alias.Length > 40) throw new ArgumentException("设备别名不能超过 40 个字符。");
        var (_, devices)=await LoadDevicesAsync();
        var d=Resolve(devices,input.GetProperty("target").GetString()!);
        var aliases=new Dictionary<string,string>(LoadAliases(),StringComparer.OrdinalIgnoreCase){[alias]=d.DeviceId};
        Directory.CreateDirectory(Path.GetDirectoryName(AliasPath)!);
        var tmp=AliasPath+".tmp";
        await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(aliases,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(tmp,AliasPath,true);
        return new { alias, deviceId=d.DeviceId, displayName=d.DisplayName };
    }
}
