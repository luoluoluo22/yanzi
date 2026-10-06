using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost;

public static class YanziCalendarCapabilityProvider
{
    private static string DataPath=>Path.Combine(ExtensionStorageService.GetExtensionStorageDirectoryPath("taskbar-calendar"),"calendar_reminders.json");

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new(){Name="calendar.upcoming",Description="读取从指定日期起未来若干天的燕子日历提醒",Permissions=["calendar.read"],Category="calendar",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"from":{"type":"string"},"days":{"type":"integer","minimum":1,"maximum":365}},"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=UpcomingAsync};
        yield return new(){Name="calendar.update",Description="按 ID 修改燕子日历提醒的日期或标题",Permissions=["calendar.write"],Category="calendar",RiskLevel="medium",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"id":{"type":"string","minLength":1},"date":{"type":"string"},"title":{"type":"string"}},"required":["id"],"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object"}"""),Handler=UpdateAsync};
    }

    private static JsonArray Load()
    {
        if(!File.Exists(DataPath)) return new JsonArray();
        return JsonNode.Parse(File.ReadAllText(DataPath))?.AsArray()??new JsonArray();
    }
    private static void Save(JsonArray items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        var tmp=DataPath+".tmp";File.WriteAllText(tmp,items.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.Move(tmp,DataPath,true);
    }
    private static DateTime Date(string? text,DateTime fallback)
    {
        if(string.IsNullOrWhiteSpace(text)) return fallback.Date;
        if(!DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
            throw new ArgumentException("date/from 必须是 YYYY-MM-DD。");
        return date;
    }

    private static Task<object?> UpcomingAsync(object? payload)
    {
        var input=(JsonElement)payload!;var from=Date(input.TryGetProperty("from",out var f)?f.GetString():null,DateTime.Today);var days=input.TryGetProperty("days",out var d)?d.GetInt32():7;var until=from.AddDays(days);
        var items=Load().OfType<JsonObject>().Select(x=>new{
            id=x["Id"]?.GetValue<string>()??"",title=x["Title"]?.GetValue<string>()??"",
            date=x["TargetDate"]?.GetValue<DateTime>()??DateTime.MinValue,isAlarm=x["IsAlarm"]?.GetValue<bool>()??false
        }).Where(x=>x.date.Date>=from&&x.date.Date<until).OrderBy(x=>x.date).Select(x=>new{x.id,x.title,date=x.date.ToString("yyyy-MM-dd"),x.isAlarm}).ToArray();
        return Task.FromResult<object?>(new{from=from.ToString("yyyy-MM-dd"),days,items});
    }

    private static async Task<object?> UpdateAsync(object? payload)
    {
        var input=(JsonElement)payload!;var id=input.GetProperty("id").GetString()!;var wasRunning=RunningExtensionRegistry.IsRunning("taskbar-calendar");
        if(wasRunning)
        {
            await YanziExtensionCapabilityProvider.StopByIdAsync("taskbar-calendar");
            for(var i=0;i<40&&RunningExtensionRegistry.IsRunning("taskbar-calendar");i++) await Task.Delay(50);
            if(RunningExtensionRegistry.IsRunning("taskbar-calendar"))
                throw new InvalidOperationException("日历小程序未能及时停止，未修改数据。");
        }
        try
        {
            var items=Load();var item=items.OfType<JsonObject>().FirstOrDefault(x=>string.Equals(x["Id"]?.GetValue<string>(),id,StringComparison.Ordinal))
                ??throw new KeyNotFoundException("日历提醒不存在："+id);
            if(input.TryGetProperty("date",out var date)) item["TargetDate"]=Date(date.GetString(),DateTime.Today);
            if(input.TryGetProperty("title",out var title)){var t=title.GetString()?.Trim();if(string.IsNullOrWhiteSpace(t))throw new ArgumentException("标题不能为空。");item["Title"]=t;}
            Save(items);
            return new{id,date=(item["TargetDate"]?.GetValue<DateTime>()??DateTime.Today).ToString("yyyy-MM-dd"),title=item["Title"]?.GetValue<string>()??"",updated=true};
        }
        finally{if(wasRunning) _=YanziExtensionCapabilityProvider.OpenByIdAsync("taskbar-calendar");}
    }
}
