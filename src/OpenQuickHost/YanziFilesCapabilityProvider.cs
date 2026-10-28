using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziFilesCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new(){Name="files.search",Description="使用 Everything 搜索本机文件和文件夹",Permissions=["files.read"], Category="files",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"query":{"type":"string","minLength":1},"limit":{"type":"integer","minimum":1,"maximum":100}},"required":["query"],"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=SearchAsync};
        yield return new(){Name="files.open",Description="使用 Windows 默认程序打开指定本机文件或文件夹",Permissions=["files.open"], Category="files", RiskLevel="medium",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"path":{"type":"string","minLength":1}},"required":["path"],"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=OpenAsync};
        yield return new(){Name="files.send",Description="把指定本机文件发送到燕子手机，并可等待手机确认接收",Permissions=["files.read","device.message.send"], Category="files",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"path":{"type":"string","minLength":1},"target":{"type":"string"},"text":{"type":"string"},"waitForAck":{"type":"boolean","default":true}},"required":["path"],"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=SendAsync};
    }

    private static Task<object?> SearchAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        var limit=input.TryGetProperty("limit",out var l)?l.GetInt32():20;
        var result=EverythingSearchService.Search(input.GetProperty("query").GetString(),limit);
        if(!result.Success) throw new InvalidOperationException(result.ErrorMessage??"文件搜索失败。");
        return Task.FromResult<object?>(new{total=result.TotalCount,items=result.Results.Select(x=>new{path=x.FullPath,name=x.Name,isFolder=Directory.Exists(x.FullPath),size=x.SizeText}).ToArray()});
    }

    private static Task<object?> OpenAsync(object? payload)
    {
        var path=Path.GetFullPath(((JsonElement)payload!).GetProperty("path").GetString()!);
        if(!File.Exists(path)&&!Directory.Exists(path)) throw new FileNotFoundException("文件或文件夹不存在。",path);
        Process.Start(new ProcessStartInfo{FileName=path,UseShellExecute=true});
        return Task.FromResult<object?>(new{path,opened=true});
    }

    private static async Task<object?> SendAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        var path=Path.GetFullPath(input.GetProperty("path").GetString()!);
        if(!File.Exists(path)) throw new FileNotFoundException("文件不存在。",path);
        var args=new Dictionary<string,object?>{{"filePath",path},{"waitForAck",!input.TryGetProperty("waitForAck",out var w)||w.GetBoolean()}};
        if(input.TryGetProperty("target",out var target)&&target.ValueKind==JsonValueKind.String) args["target"]=target.GetString();
        if(input.TryGetProperty("text",out var text)&&text.ValueKind==JsonValueKind.String) args["text"]=text.GetString();
        var result=await YanziCapabilityInvocationService.InvokeAsync("chat.send",args,YanziCapabilityCaller.LocalAgent);
        if(!result.Success) throw new InvalidOperationException(result.Error??"发送失败。");
        return result.Data;
    }
}
