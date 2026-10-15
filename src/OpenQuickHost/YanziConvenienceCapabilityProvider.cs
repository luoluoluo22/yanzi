using System.Text.Json;

namespace OpenQuickHost;

public static class YanziConvenienceCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new(){Name="album.status",Description="查看相册处理能力是否已安装、运行及可调用",Permissions=["album.read"],Category="media",
            InputSchema=YanziCapabilitySchema.EmptyObject,OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=AlbumStatusAsync};
        yield return new(){Name="mirror.open",Description="打开镜子注意力记录小程序",Permissions=["extension.run"],Category="attention",
            InputSchema=YanziCapabilitySchema.EmptyObject,OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=_=>YanziExtensionCapabilityProvider.OpenByIdAsync("mirror")};
        yield return new(){Name="mirror.close",Description="关闭镜子注意力记录小程序",Permissions=["extension.run"],Category="attention",
            InputSchema=YanziCapabilitySchema.EmptyObject,OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=_=>YanziExtensionCapabilityProvider.StopByIdAsync("mirror")};
    }

    private static Task<object?> AlbumStatusAsync(object? _)
    {
        var commands=OpenQuickHost.Sync.LocalExtensionCatalog.LoadCommands();
        var installed=commands.Any(c=>string.Equals(c.ExtensionId,"yanzi-album",StringComparison.OrdinalIgnoreCase));
        var running=RunningExtensionRegistry.IsRunning("yanzi-album");
        var available=YanziCapabilityRegistry.Contains("album.process");
        var declared=commands.FirstOrDefault(c=>string.Equals(c.ExtensionId,"yanzi-album",StringComparison.OrdinalIgnoreCase)) is { } command &&
            YanziAgentCapabilityCatalog.ForExtension(command).Any(c=>c.Name=="album.process");
        return Task.FromResult<object?>(new{installed,running,declared,available});
    }
}
