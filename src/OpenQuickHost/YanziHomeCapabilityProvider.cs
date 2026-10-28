using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

public static class YanziHomeCapabilityProvider
{
    private sealed class Config
    {
        public bool Enabled { get; set; }
        public string HomeAssistantUrl { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string ProtectedToken { get; set; } = "";
    }

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name="home.light.get", Description="读取延时关机小程序中已配置的卧室灯当前状态",
            Permissions=["home.read"], Category="smart-home",
            InputSchema=YanziCapabilitySchema.EmptyObject, OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler=GetAsync
        };
        yield return new()
        {
            Name="home.light.set", Description="控制延时关机小程序中已配置的卧室灯开或关，并验证最终状态",
            Permissions=["home.write"], Category="smart-home", RiskLevel="medium",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"state":{"type":"string","enum":["on","off"]}},"required":["state"],"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""), Handler=SetAsync
        };
    }

    private static async Task<Config> LoadAsync()
    {
        var saved=await ExtensionStorageService.ReadTextAsync("shutdown-timer","smart-light-link.json","local");
        if(!saved.Found||string.IsNullOrWhiteSpace(saved.Content))
            throw new InvalidOperationException("尚未在延时关机小程序中配置卧室灯。");
        var config=JsonSerializer.Deserialize<Config>(saved.Content)??throw new InvalidOperationException("卧室灯配置无效。");
        Validate(config);
        return config;
    }

    private static Uri Validate(Config config)
    {
        if(!Uri.TryCreate(config.HomeAssistantUrl.Trim().TrimEnd('/'),UriKind.Absolute,out var uri)||
           (uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps)||
           !string.IsNullOrWhiteSpace(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||
           uri.AbsolutePath!="/")
            throw new InvalidOperationException("Home Assistant 地址配置无效。");
        if(!Regex.IsMatch(config.EntityId.Trim(),@"^(light|switch)\.[A-Za-z0-9_-]{1,120}$"))
            throw new InvalidOperationException("卧室灯实体 ID 配置无效。");
        if(string.IsNullOrWhiteSpace(config.ProtectedToken)) throw new InvalidOperationException("Home Assistant 令牌尚未配置。");
        return uri;
    }

    private static string Token(Config config)
    {
        var raw=Convert.FromBase64String(config.ProtectedToken);
        var plain=ProtectedData.Unprotect(raw,Encoding.UTF8.GetBytes("YanziShutdownLight-v1"),DataProtectionScope.CurrentUser);
        try{return Encoding.UTF8.GetString(plain);}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }

    private static HttpClient Client(Config config)
    {
        var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(8)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",Token(config));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    private static Uri Url(Config config,string relative)=>new(Validate(config).GetLeftPart(UriPartial.Authority).TrimEnd('/')+relative);

    private static async Task<string> ReadStateAsync(Config config)
    {
        using var http=Client(config);
        using var response=await http.GetAsync(Url(config,"/api/states/"+Uri.EscapeDataString(config.EntityId.Trim())));
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"读取卧室灯失败，HTTP {(int)response.StatusCode}。");
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("state",out var state)?state.GetString()??"unknown":"unknown";
    }

    private static async Task<object?> GetAsync(object? _)
    {
        var config=await LoadAsync();
        var state=await ReadStateAsync(config);
        return new{entityId=config.EntityId,state};
    }

    private static async Task<object?> SetAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        var desired=input.GetProperty("state").GetString()!;
        var config=await LoadAsync();
        using(var http=Client(config))
        using(var body=new StringContent(JsonSerializer.Serialize(new{entity_id=config.EntityId.Trim()}),Encoding.UTF8,"application/json"))
        using(var response=await http.PostAsync(Url(config,$"/api/services/{config.EntityId.Split('.')[0]}/turn_{desired}"),body))
            if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"卧室灯指令未被接受，HTTP {(int)response.StatusCode}。");

        for(var i=0;i<6;i++)
        {
            await Task.Delay(450);
            var state=await ReadStateAsync(config);
            if(string.Equals(state,desired,StringComparison.OrdinalIgnoreCase))
                return new{entityId=config.EntityId,state,confirmed=true};
        }
        throw new InvalidOperationException("指令已发送，但未确认卧室灯达到目标状态。");
    }
}
