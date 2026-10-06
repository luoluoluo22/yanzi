using System.Text.Json;

namespace OpenQuickHost;

public static class YanziExtensionCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new(){Name="extension.list",Description="列出已安装燕子小程序及运行状态",Permissions=["extension.read"], Category="extensions",
            InputSchema=YanziCapabilitySchema.EmptyObject,OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"items":{"type":"array"}},"required":["items"]}"""),Handler=ListAsync};
        yield return new(){Name="extension.status",Description="查询指定燕子小程序是否已安装、版本及运行状态",Permissions=["extension.read"], Category="extensions",
            InputSchema=TargetSchema(),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=StatusAsync};
        yield return new(){Name="extension.open",Description="打开或启动指定燕子小程序；支持 ID 或名称",Permissions=["extension.run"], Category="extensions", RiskLevel="medium",
            InputSchema=TargetSchema(),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=OpenAsync};
        yield return new(){Name="extension.stop",Description="停止指定正在运行的燕子小程序",Permissions=["extension.run"], Category="extensions", RiskLevel="medium",
            InputSchema=TargetSchema(),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=StopAsync};
    }

    private static JsonElement TargetSchema()=>YanziCapabilitySchema.Parse("""{"type":"object","properties":{"target":{"type":"string","minLength":1}},"required":["target"],"additionalProperties":false}""");

    internal static CommandItem Resolve(string query)
    {
        var commands=OpenQuickHost.Sync.LocalExtensionCatalog.LoadCommands().ToArray();
        query=query.Trim();
        var found=commands.Where(c=>string.Equals(c.ExtensionId,query,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(found.Length==0) found=commands.Where(c=>string.Equals(c.Title,query,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(found.Length==0) found=commands.Where(c=>c.Title.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        return found.Length switch{1=>found[0],0=>throw new KeyNotFoundException("未找到小程序："+query),_=>throw new ArgumentException("小程序名称不唯一："+query)};
    }

    private static Task<object?> ListAsync(object? _)
    {
        var running=RunningExtensionRegistry.GetSnapshot().Select(x=>x.ExtensionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items=OpenQuickHost.Sync.LocalExtensionCatalog.LoadCommands().Select(c=>new{
            id=c.ExtensionId,name=c.Title,description=c.Subtitle,version=c.DeclaredVersion,runtime=c.Runtime,isRunning=running.Contains(c.ExtensionId)
        }).ToArray();
        return Task.FromResult<object?>(new{items});
    }

    private static Task<object?> StatusAsync(object? payload)
    {
        var q=((JsonElement)payload!).GetProperty("target").GetString()!;
        var c=Resolve(q);
        var instances=RunningExtensionRegistry.GetSnapshot().Where(x=>string.Equals(x.ExtensionId,c.ExtensionId,StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult<object?>(new{id=c.ExtensionId,name=c.Title,version=c.DeclaredVersion,runtime=c.Runtime,isRunning=instances.Length>0,instanceCount=instances.Length});
    }

    internal static async Task<object?> OpenByIdAsync(string query)
    {
        var c=Resolve(query);
        if(c.App!=null)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{
                if(!AppExtensionWindow.TryActivateExisting(c)) new AppExtensionWindow(c,"","capability").Show();
            });
            return new{id=c.ExtensionId,name=c.Title,opened=true,isRunning=true};
        }
        if(RunningExtensionRegistry.IsRunning(c.ExtensionId))
            return new{id=c.ExtensionId,name=c.Title,opened=true,isRunning=true,alreadyRunning=true};
        _=Task.Run(async()=>{
            try{ await ScriptExtensionRunner.ExecuteAsync(c,null,"capability",CancellationToken.None); }
            catch(Exception ex){HostAssets.AppendLog($"Capability extension.open failed: {c.ExtensionId}: {ex.Message}");}
        });
        for(var i=0;i<40&&!RunningExtensionRegistry.IsRunning(c.ExtensionId);i++) await Task.Delay(50);
        return new{id=c.ExtensionId,name=c.Title,opened=true,isRunning=RunningExtensionRegistry.IsRunning(c.ExtensionId)};
    }

    private static Task<object?> OpenAsync(object? payload)=>OpenByIdAsync(((JsonElement)payload!).GetProperty("target").GetString()!);

    internal static Task<object?> StopByIdAsync(string query)
    {
        var c=Resolve(query);
        var instances=RunningExtensionRegistry.GetSnapshot().Where(x=>string.Equals(x.ExtensionId,c.ExtensionId,StringComparison.OrdinalIgnoreCase)).ToArray();
        var stopped=0;
        foreach(var i in instances) if(RunningExtensionRegistry.TryTerminate(i.InstanceId,out _)) stopped++;
        return Task.FromResult<object?>(new{id=c.ExtensionId,name=c.Title,stopRequested=stopped>0,stoppedInstances=stopped,isRunning=RunningExtensionRegistry.IsRunning(c.ExtensionId)});
    }

    private static Task<object?> StopAsync(object? payload)=>StopByIdAsync(((JsonElement)payload!).GetProperty("target").GetString()!);
}
