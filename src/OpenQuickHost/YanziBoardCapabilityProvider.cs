using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost;

public static class YanziBoardCapabilityProvider
{
    private static string Root => ExtensionStorageService.GetExtensionStorageDirectoryPath("inspiration-board");
    private static string Boards => Path.Combine(Root,"boards");

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return Def("board.create","创建新的灵感白板文件","board.write",
            """{"type":"object","properties":{"title":{"type":"string","minLength":1}},"required":["title"],"additionalProperties":false}""",CreateAsync);
        yield return Def("board.add","向灵感白板添加文字卡片","board.write",
            """{"type":"object","properties":{"board":{"type":"string"},"title":{"type":"string"},"content":{"type":"string"},"x":{"type":"number"},"y":{"type":"number"}},"additionalProperties":false}""",AddAsync);
        yield return Def("board.connect","连接灵感白板中的两张卡片","board.write",
            """{"type":"object","properties":{"board":{"type":"string"},"fromCardId":{"type":"string","minLength":1},"toCardId":{"type":"string","minLength":1},"label":{"type":"string"}},"required":["fromCardId","toCardId"],"additionalProperties":false}""",ConnectAsync);
    }
    private static YanziCapabilityProviderDefinition Def(string n,string d,string p,string s,Func<object?,Task<object?>> h)=>new(){Name=n,Description=d,Permissions=[p],Category="board",RiskLevel="medium",InputSchema=YanziCapabilitySchema.Parse(s),OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=h};

    private static string SafeTitle(string title)
    {
        var invalid=Path.GetInvalidFileNameChars(); var s=new string(title.Trim().Select(ch=>invalid.Contains(ch)?'_':ch).ToArray());
        return string.IsNullOrWhiteSpace(s)?"灵感白板":s;
    }
    private static string ResolveBoard(string? query)
    {
        Directory.CreateDirectory(Boards);
        if(!string.IsNullOrWhiteSpace(query))
        {
            var full=Path.IsPathFullyQualified(query)?Path.GetFullPath(query):Path.Combine(Boards,SafeTitle(query)+(query.EndsWith(".yzboard",StringComparison.OrdinalIgnoreCase)?"":".yzboard"));
            if(!YanziDeviceResourceAuthorization.AllowsFile(full,[Boards])) throw new UnauthorizedAccessException("白板路径越界。");
            if(!File.Exists(full)) throw new FileNotFoundException("白板不存在。",full); return full;
        }
        var config=Path.Combine(Root,"workspace-config.json");
        try{if(File.Exists(config)){var node=JsonNode.Parse(File.ReadAllText(config))?["LastOpenedFilePath"]?.GetValue<string>();if(!string.IsNullOrWhiteSpace(node)&&File.Exists(node))return node;}}catch{}
        return Directory.EnumerateFiles(Boards,"*.yzboard").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()??throw new FileNotFoundException("尚没有灵感白板。");
    }
    private static JsonObject Load(string path)=>JsonNode.Parse(File.ReadAllText(path))?.AsObject()??new JsonObject();
    private static void Save(string path,JsonObject data){data["UpdatedAt"]=DateTimeOffset.Now;var tmp=path+".tmp";File.WriteAllText(tmp,data.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.Move(tmp,path,true);}

    private static Task<object?> CreateAsync(object? payload)
    {
        var title=((JsonElement)payload!).GetProperty("title").GetString()!;Directory.CreateDirectory(Boards);var path=Path.Combine(Boards,SafeTitle(title)+".yzboard");
        if(File.Exists(path)) path=Path.Combine(Boards,SafeTitle(title)+"-"+DateTime.Now.ToString("HHmmss")+".yzboard");
        var data=new JsonObject{{"ViewportX",0},{"ViewportY",0},{"Zoom",1.0},{"Cards",new JsonArray()},{"Connections",new JsonArray()},{"UpdatedAt",DateTimeOffset.Now}};
        Save(path,data);return Task.FromResult<object?>(new{board=path,created=true});
    }
    private static Task<object?> AddAsync(object? payload)
    {
        var input=(JsonElement)payload!;var path=ResolveBoard(input.TryGetProperty("board",out var b)?b.GetString():null);var data=Load(path);
        var cards=data["Cards"] as JsonArray??new JsonArray();data["Cards"]=cards;var id=Guid.NewGuid().ToString("N");
        cards.Add(new JsonObject{{"Id",id},{"Type","text"},{"X",input.TryGetProperty("x",out var x)?x.GetDouble():100+cards.Count*24},{"Y",input.TryGetProperty("y",out var y)?y.GetDouble():100+cards.Count*24},{"Width",240},{"Height",190},{"Title",input.TryGetProperty("title",out var t)?t.GetString()??"想法":"想法"},{"Content",input.TryGetProperty("content",out var c)?c.GetString()??"":""},{"ThemeColor","#FBD1E5"},{"FontSize",14},{"CornerRadius",12},{"Opacity",1},{"BorderColor",""},{"BorderStyle","solid"},{"HasShadow",true},{"Tags",new JsonArray()},{"IconKey",""},{"Notes",""},{"Link",""},{"IsLocked",false},{"GroupId",""},{"FrameId",""},{"Rotation",0},{"IsCollapsed",false},{"IsAiSuggestion",false},{"CreatedAt",DateTimeOffset.Now}});
        Save(path,data);return Task.FromResult<object?>(new{board=path,cardId=id,created=true});
    }
    private static Task<object?> ConnectAsync(object? payload)
    {
        var input=(JsonElement)payload!;var path=ResolveBoard(input.TryGetProperty("board",out var b)?b.GetString():null);var data=Load(path);var cards=data["Cards"] as JsonArray??new JsonArray();
        var from=input.GetProperty("fromCardId").GetString()!;var to=input.GetProperty("toCardId").GetString()!;
        bool exists(string id)=>cards.OfType<JsonObject>().Any(c=>c["Id"]?.GetValue<string>()==id);if(!exists(from)||!exists(to))throw new KeyNotFoundException("连接的卡片不存在。");
        var conns=data["Connections"] as JsonArray??new JsonArray();data["Connections"]=conns;var id=Guid.NewGuid().ToString("N");
        conns.Add(new JsonObject{{"Id",id},{"FromCardId",from},{"ToCardId",to},{"FromAnchor","right"},{"ToAnchor","left"},{"Label",input.TryGetProperty("label",out var l)?l.GetString()??"":""},{"Color","#F59E0B"},{"IsDashed",false},{"ControlOffsetX",0},{"ControlOffsetY",0},{"RelationType","关联"}});
        Save(path,data);return Task.FromResult<object?>(new{board=path,connectionId=id,created=true});
    }
}
