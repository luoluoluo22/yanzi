using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class YanziNotesCapabilityProvider
{
    private const string ExtensionId="yanzi-notes";
    private const string IndexKey="notes/sync.v1.json";

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return Def("notes.search","搜索燕子笔记正文与标题","notes.read",
            """{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}""",SearchAsync);
        yield return Def("notes.read","读取指定燕子笔记","notes.read",
            """{"type":"object","properties":{"path":{"type":"string","minLength":1}},"required":["path"],"additionalProperties":false}""",ReadAsync);
        yield return Def("notes.create","创建一篇燕子 Markdown 笔记并进入账号同步","notes.write",
            """{"type":"object","properties":{"title":{"type":"string","minLength":1},"content":{"type":"string"}},"required":["title"],"additionalProperties":false}""",CreateAsync);
        yield return Def("notes.update","替换或追加燕子笔记内容","notes.write",
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"content":{"type":"string"},"append":{"type":"boolean","default":false}},"required":["path","content"],"additionalProperties":false}""",UpdateAsync);
        yield return Def("notes.open","在电脑或指定燕子手机上打开一篇笔记","notes.open",
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"target":{"type":"string","description":"desktop、设备名称、设备 ID 或别名；默认 desktop"},"waitForAck":{"type":"boolean","default":true}},"required":["path"],"additionalProperties":false}""",OpenAsync);
    }

    private static YanziCapabilityProviderDefinition Def(string name,string description,string permission,string schema,Func<object?,Task<object?>> handler)=>new()
    {Name=name,Description=description,Permissions=[permission],Category="notes",RiskLevel=permission=="notes.read"?"low":"medium",InputSchema=YanziCapabilitySchema.Parse(schema),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=handler};

    private static string ValidPath(string path)
    {
        path=path.Replace('\\','/').Trim('/');
        if(!path.StartsWith("article/",StringComparison.Ordinal)||!path.EndsWith(".md",StringComparison.OrdinalIgnoreCase)||path.Contains("..",StringComparison.Ordinal))
            throw new ArgumentException("笔记路径必须是 article/*.md。");
        return path;
    }

    private static async Task<(JsonObject Doc,long Revision,string Account)> LoadAsync()
    {
        var cloud=new CloudSyncClient(SyncConfigLoader.Load());
        await cloud.ReloadPersistedSessionAsync();
        if(!cloud.HasCredential||string.IsNullOrWhiteSpace(cloud.CurrentUserId)) throw new InvalidOperationException("请先登录燕子账号。");
        var read=await AccountExtensionDataStore.TryReadAsync(ExtensionId,IndexKey);
        if(!read.Available) throw new IOException("燕子笔记账号存储暂不可用。");
        JsonObject doc;
        try{doc=read.Exists&&!string.IsNullOrWhiteSpace(read.Content)?JsonNode.Parse(read.Content!)?.AsObject()??new JsonObject():new JsonObject();}
        catch(JsonException){throw new IOException("燕子笔记索引损坏，未进行写入。");}
        if(doc["records"] is not JsonObject) doc["records"]=new JsonObject();
        return(doc,read.Revision,cloud.CurrentUserId!);
    }

    private static IEnumerable<(string Path,JsonObject Record,string Content)> Records(JsonObject doc)
    {
        var records=doc["records"]!.AsObject();
        foreach(var pair in records)
        {
            if(pair.Value is not JsonObject record||record["deleted"]?.GetValue<bool>()==true) continue;
            var content=record["item"]?["content"]?.GetValue<string>()??"";
            yield return(pair.Key,record,content);
        }
    }

    private static string Title(string path,string content)
    {
        var first=content.Split('\n').Select(x=>x.Trim()).FirstOrDefault(x=>x.Length>0);
        if(first?.StartsWith("#")==true) return first.TrimStart('#',' ');
        return Path.GetFileNameWithoutExtension(path);
    }

    private static async Task<object?> SearchAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        var q=input.TryGetProperty("query",out var query)?query.GetString()??"":""; var limit=input.TryGetProperty("limit",out var l)?l.GetInt32():20;
        var (doc,_,_)=await LoadAsync();
        var items=Records(doc).Where(x=>string.IsNullOrWhiteSpace(q)||x.Path.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Content.Contains(q,StringComparison.OrdinalIgnoreCase))
            .Take(limit).Select(x=>new{path=x.Path,title=Title(x.Path,x.Content),updatedAt=x.Record["item"]?["updatedAt"]?.GetValue<string>()??"",preview=x.Content.Length<=180?x.Content:x.Content[..180]}).ToArray();
        return new{items};
    }

    private static async Task<object?> ReadAsync(object? payload)
    {
        var path=ValidPath(((JsonElement)payload!).GetProperty("path").GetString()!);
        var (doc,_,_)=await LoadAsync();
        var found=Records(doc).FirstOrDefault(x=>string.Equals(x.Path,path,StringComparison.Ordinal));
        if(found.Record==null) throw new KeyNotFoundException("笔记不存在："+path);
        return new{path,title=Title(path,found.Content),content=found.Content,updatedAt=found.Record["item"]?["updatedAt"]?.GetValue<string>()??""};
    }

    private static async Task WriteAsync(JsonObject doc,long revision,string account,string path,string content)
    {
        var records=doc["records"]!.AsObject();
        records[path]=new JsonObject{{"version",Guid.NewGuid().ToString("N")},{"deleted",false},{"item",new JsonObject{{"content",content},{"updatedAt",DateTimeOffset.UtcNow.ToString("O")}}}};
        var json=doc.ToJsonString(new JsonSerializerOptions{WriteIndented=false});
        var result=await AccountExtensionDataStore.WriteAsync(ExtensionId,IndexKey,json,revision,expectedAccountId:account);
        if(!result.Available) throw new IOException("燕子笔记账号存储写入失败。");
        var localKey = "notes/" + path;
        await ExtensionStorageService.WriteTextAsync(ExtensionId,localKey,content,"local");
        AppExtensionWindow.NotifyStorageChanged(ExtensionId,localKey);
    }

    private static async Task<object?> CreateAsync(object? payload)
    {
        var input=(JsonElement)payload!; var title=input.GetProperty("title").GetString()!.Trim(); var body=input.TryGetProperty("content",out var c)?c.GetString()??"":"";
        var content=body.TrimStart().StartsWith("#")?body:$"# {title}\n\n{body}";
        var path="article/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".md";
        var (doc,revision,account)=await LoadAsync(); await WriteAsync(doc,revision,account,path,content);
        return new{path,title,created=true};
    }

    private static async Task<object?> UpdateAsync(object? payload)
    {
        var input=(JsonElement)payload!; var path=ValidPath(input.GetProperty("path").GetString()!); var addition=input.GetProperty("content").GetString()??"";
        var (doc,revision,account)=await LoadAsync(); var current=Records(doc).FirstOrDefault(x=>x.Path==path);
        if(current.Record==null) throw new KeyNotFoundException("笔记不存在："+path);
        var append=input.TryGetProperty("append",out var a)&&a.GetBoolean(); var content=append?current.Content+addition:addition;
        await WriteAsync(doc,revision,account,path,content); return new{path,updated=true,title=Title(path,content)};
    }

    private static async Task<object?> OpenAsync(object? payload)
    {
        var input=(JsonElement)payload!; var path=ValidPath(input.GetProperty("path").GetString()!); var target=input.TryGetProperty("target",out var t)?t.GetString()?.Trim():"desktop";
        var (_,_,account)=await LoadAsync(); var handoff=JsonSerializer.Serialize(new{action="open-note",path,accountId=account});
        if(string.IsNullOrWhiteSpace(target)||string.Equals(target,"desktop",StringComparison.OrdinalIgnoreCase)||string.Equals(target,"电脑",StringComparison.OrdinalIgnoreCase))
        {
            var command=YanziExtensionCapabilityProvider.Resolve(ExtensionId);
            var output=await System.Windows.Application.Current.Dispatcher.InvokeAsync(async()=>await AppExtensionWindow.OpenResourceAsync(command,handoff)).Task.Unwrap();
            return new{opened=true,target="desktop",path,output};
        }
        var (cloud,devices)=await YanziDeviceCapabilityProvider.LoadDevicesAsync(); var device=YanziDeviceCapabilityProvider.Resolve(devices,target!);
        var id=await cloud.SendDeviceMessageAsync(DeviceIdentityStore.GetOrCreateDesktopDeviceId(),device.Platform,"extension.handoff","","",device.DeviceId,
            new{extensionId=ExtensionId,input=handoff,accountId=account,clientOperationId=Guid.NewGuid().ToString("N")},expiresAt:DateTimeOffset.UtcNow.AddDays(1));
        var wait=!input.TryGetProperty("waitForAck",out var w)||w.GetBoolean();
        if(wait) for(var i=0;i<40;i++){var m=await cloud.GetDeviceMessageAsync(id);if(!string.IsNullOrWhiteSpace(m?.AckedAt)) return new{opened=true,target=device.DisplayName,path,messageId=id,acked=true};await Task.Delay(500);}
        return new{opened=true,target=device.DisplayName,path,messageId=id,acked=false};
    }
}
