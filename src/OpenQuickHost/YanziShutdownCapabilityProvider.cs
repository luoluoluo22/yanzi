using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace OpenQuickHost;

public static class YanziShutdownCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new(){Name="shutdown.schedule",Description="启动燕子延时关机倒计时；沿用关机前智能灯联动",Permissions=["system.shutdown"],Category="power",RiskLevel="high",RequiresConfirmation=true,
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"seconds":{"type":"integer","minimum":1,"maximum":86400},"confirmed":{"type":"boolean","description":"用户已明确确认本次关机"}},"required":["seconds","confirmed"],"additionalProperties":false}"""),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=ScheduleAsync};
        yield return new(){Name="shutdown.cancel",Description="取消当前燕子延时关机并同时撤销 Windows 待处理关机",Permissions=["system.shutdown"],Category="power",RiskLevel="medium",
            InputSchema=YanziCapabilitySchema.EmptyObject,OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=CancelAsync};
        yield return new(){Name="shutdown.pause",Description="暂停或继续当前燕子延时关机倒计时",Permissions=["system.shutdown"],Category="power",RiskLevel="medium",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"paused":{"type":"boolean"}},"required":["paused"],"additionalProperties":false}"""),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=PauseAsync};
        yield return new(){Name="shutdown.status",Description="读取延时关机小程序当前运行状态",Permissions=["system.read"],Category="power",
            InputSchema=YanziCapabilitySchema.EmptyObject,OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=StatusAsync};
    }

    private static async Task<object?> ScheduleAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        if(!input.GetProperty("confirmed").GetBoolean()) throw new InvalidOperationException("本次关机尚未获得用户明确确认。");
        var seconds=input.GetProperty("seconds").GetInt32();
        var command=YanziExtensionCapabilityProvider.Resolve("shutdown-timer");
        if(RunningExtensionRegistry.IsRunning(command.ExtensionId))
            throw new InvalidOperationException("已有延时关机任务正在运行，请先取消或调整现有任务。");
        _=Task.Run(async()=>{
            try{await ScriptExtensionRunner.ExecuteAsync(command,seconds.ToString(),"capability",CancellationToken.None);}
            catch(Exception ex){HostAssets.AppendLog("shutdown.schedule failed: "+ex.Message);}
        });
        for(var i=0;i<60&&!RunningExtensionRegistry.IsRunning(command.ExtensionId);i++) await Task.Delay(50);
        return new{scheduled=RunningExtensionRegistry.IsRunning(command.ExtensionId),seconds};
    }

    private static async Task<object?> CancelAsync(object? _)
    {
        var result=await YanziExtensionCapabilityProvider.StopByIdAsync("shutdown-timer");
        try{Process.Start(new ProcessStartInfo{FileName="shutdown.exe",Arguments="/a",UseShellExecute=false,CreateNoWindow=true})?.WaitForExit(3000);}catch{}
        return result;
    }

    private static Task<object?> PauseAsync(object? payload)
    {
        var desired=((JsonElement)payload!).GetProperty("paused").GetBoolean();
        if(!HostObjectRegistry.TryGetObject("shutdown-timer-window",out var bridge)||bridge==null)
            throw new InvalidOperationException("当前没有正在运行的延时关机窗口。");
        var bridgeType=bridge.GetType();
        var field=bridgeType.GetField("_window",BindingFlags.NonPublic|BindingFlags.Instance)
            ??throw new InvalidOperationException("延时关机桥接版本不支持暂停。");
        var window=field.GetValue(bridge)??throw new InvalidOperationException("延时关机窗口不可用。");
        var type=window.GetType();
        var pausedField=type.GetField("_paused",BindingFlags.NonPublic|BindingFlags.Instance)
            ??throw new InvalidOperationException("延时关机版本不支持暂停状态。");
        var toggle=type.GetMethod("TogglePause",BindingFlags.NonPublic|BindingFlags.Instance)
            ??throw new InvalidOperationException("延时关机版本不支持暂停操作。");
        if(window is not DispatcherObject dispatcherObject) throw new InvalidOperationException("延时关机窗口线程不可用。");
        dispatcherObject.Dispatcher.Invoke(()=>{
            var current=pausedField.GetValue(window) is bool b&&b;
            if(current!=desired) toggle.Invoke(window,null);
        });
        return Task.FromResult<object?>(new{paused=desired});
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var running=RunningExtensionRegistry.GetSnapshot().Where(x=>string.Equals(x.ExtensionId,"shutdown-timer",StringComparison.OrdinalIgnoreCase)).ToArray();
        bool? paused=null;
        if(HostObjectRegistry.TryGetObject("shutdown-timer-window",out var bridge)&&bridge!=null)
        {
            try
            {
                var window=bridge.GetType().GetField("_window",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(bridge);
                paused=window?.GetType().GetField("_paused",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(window) as bool?;
            }catch{}
        }
        return Task.FromResult<object?>(new{isRunning=running.Length>0,paused,instanceCount=running.Length});
    }
}
