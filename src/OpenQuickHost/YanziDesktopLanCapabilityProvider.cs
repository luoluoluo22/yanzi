using System.Text.Json;

namespace OpenQuickHost;

public static class YanziDesktopLanCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "device.lan.scan",
            Description = "扫描局域网内安装完整版燕子的 Windows 设备，通过同账号加密握手确认可信身份",
            Permissions = ["device.read"], Category = "devices",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"ip":{"type":"string"}},"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"items":{"type":"array"}},"required":["items"]}"""),
            Handler = async payload =>
            {
                var ip = payload is JsonElement args && args.ValueKind == JsonValueKind.Object &&
                         args.TryGetProperty("ip", out var element) ? element.GetString() : null;
                var items = await YanziDesktopLanService.DiscoverAsync(ip);
                return new { items };
            }
        };
        yield return new()
        {
            Name = "device.lan.execute",
            Description = "通过已验证的同账号 AES-GCM 局域网通道，在另一台燕子 Windows 电脑上运行 PowerShell 命令",
            Permissions = ["device.write"], Category = "devices", RiskLevel = "high",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"deviceId":{"type":"string","minLength":1},"command":{"type":"string","minLength":1}},"required":["deviceId","command"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = async payload =>
            {
                var args = (JsonElement)payload!;
                var id = args.GetProperty("deviceId").GetString() ?? "";
                var command = args.GetProperty("command").GetString() ?? "";
                var peer = YanziPeerRegistry.Resolve(id) ??
                    throw new InvalidOperationException("设备未建立认证连接，请先调用 device.lan.scan。");
                var computer = new YanziDesktopLanService.Computer(peer.DeviceId, peer.DisplayName,
                    peer.Address, peer.Port, true, "已认证");
                return new { deviceId = id, transport = "lan",
                    output = await YanziDesktopLanService.ExecuteAsync(computer, command) };
            }
        };
    }
}
