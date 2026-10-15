using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace OpenQuickHost.Sync;

internal sealed record PlatformTaskRecord(string Id, string MessageId, string Title, string Kind, string Target, string Status, string Result, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int ProcessId = 0);

// Read-only receipts for the UI. Execution and replay remain owned by the existing outbox/ledger.
internal static class PlatformTaskJournal
{
    private static readonly object Gate = new();
    private static string Root(string account) => Path.Combine(Path.GetDirectoryName(HostAssets.MobileInboxPath)!, "task-journal", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account))));
    private static string PathFor(string account, string id) => Path.Combine(Root(account), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    private static bool Supported(string kind) => kind is "text" or "photo" or "file" or "screenshot" or "extension.handoff" or "capability.invoke" || kind.StartsWith("run-") || kind.StartsWith("fs-");
    public static bool Terminal(string state) => state is "completed" or "acked" or "failed" or "expired" or "cancelled";
    public static string Label(string state) => state switch { "queued" => "等待投递", "submitted" or "pending" => "等待设备处理", "executing" => "执行中", "completed" => "已完成", "acked" => "已接收", "failed" => "失败", "cancelled" => "已取消", "expired" => "已过期", _ => "结果待确认" };
    public static IReadOnlyList<PlatformTaskRecord> List(string account)
    {
        lock(Gate) {
            var root = Root(account);
            return !Directory.Exists(root) ? [] : Directory.EnumerateFiles(root,"*.json")
                .Select(path => JsonSerializer.Deserialize<PlatformTaskRecord>(File.ReadAllText(path))!).Select(record => record.Status == "executing" && record.ProcessId != Environment.ProcessId ? record with { Status = "unknown", Result = "上次执行已中断，请查询原任务或确认电脑上的实际结果。\n" + record.Result } : record).OrderByDescending(record => record.CreatedAt).ToArray();
        }
    }
    public static void Save(string account, PlatformTaskRecord record)
    {
        lock(Gate) {
            Directory.CreateDirectory(Root(account)); var path=PathFor(account,record.Id);
            if(File.Exists(path)) {
                var old=JsonSerializer.Deserialize<PlatformTaskRecord>(File.ReadAllText(path))!;
                if(Terminal(old.Status) || record.Status == "queued" && old.Status != "queued") return;
                record=record with {CreatedAt=old.CreatedAt, MessageId=string.IsNullOrEmpty(record.MessageId)?old.MessageId:record.MessageId, Target=string.IsNullOrEmpty(old.Target)?record.Target:old.Target};
            }
            record=record with {ProcessId=Environment.ProcessId, Result=record.Result.Length>8000 ? record.Result[..8000]+"…" : record.Result};
            var temporary=path+".tmp";
            using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)) { var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record)); output.Write(bytes); output.Flush(true); }
            File.Move(temporary,path,true);
            var files=Directory.EnumerateFiles(Root(account),"*.json").ToArray();
            if(files.Length>1000) foreach(var old in List(account).Where(item=>Terminal(item.Status)).OrderBy(item=>item.CreatedAt).Take(files.Length-1000)) File.Delete(PathFor(account,old.Id));
        }
    }
    public static void Envelope(string account,string body,string? messageId=null,string status="queued",string result="")
    {
        using var doc=JsonDocument.Parse(body); var root=doc.RootElement;
        string Read(string key)=>root.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString()??"" : "";
        var id=Read("clientMessageId"); if(string.IsNullOrEmpty(id)||!Supported(Read("kind"))) return;
        Save(account,new(id,messageId??"",Read("title"),Read("kind"),Read("targetDeviceId"),status,result,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
    }
    public static void Observed(string account,DeviceMessageRecord message)
    {
        if (!Supported(message.Kind)) return;
        var existing=List(account).FirstOrDefault(item=>item.MessageId==message.MessageId);
        var id=existing?.Id??message.OperationId??message.ClientMessageId??message.MessageId;
        var output=message.Payload.TryGetValue("executionResult",out var result) ? result.ToString() : "";
        Save(account,new(id,message.MessageId,message.Title,message.Kind,message.TargetDeviceId??message.TargetPlatform??"",message.Status,output,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
    }
}
