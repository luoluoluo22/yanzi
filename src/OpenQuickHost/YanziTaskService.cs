using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

// One durable operation journal for native providers and extension providers.
// Persist the submission boundary BEFORE an irreversible action; never replay a
// recovered task whose effect is unknown. Business receipts remain authoritative.
public static class YanziTaskService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, YanziTaskRecord> Active = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,bool> CancelSignals = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly long ProcessStartTicks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    private sealed class JournalLock : IDisposable
    {
        private readonly Mutex _mutex=new(false,"Local\\YanziTaskJournal-"+Hash(Root));
        public JournalLock() {try{if(!_mutex.WaitOne(TimeSpan.FromSeconds(5)))throw new IOException("task_journal_busy");}catch(AbandonedMutexException){}}
        public void Dispose(){_mutex.ReleaseMutex();_mutex.Dispose();}
    }
    private static string Account => SyncSessionStore.Load()?.UserId ?? "local";
    private static string Root => RootFor(Hash(Account));
    private static string RootFor(string accountKey) => Path.Combine(HostAssets.ResolveDataDirectoryPath("operation-tasks"), accountKey);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Key(string requestId) => Hash(Account + "\n" + requestId);
    private static string RecordPath(string id) => Path.Combine(Root, id + ".json");
    private static void Save(YanziTaskRecord record)
    {
        var root=RootFor(record.AccountKey);Directory.CreateDirectory(root);
        string file = Path.Combine(root,record.TaskId+".json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(record, Json));
        File.Move(file + ".tmp", file, true);
    }
    private static YanziTaskRecord? Load(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-f0-9]{64}$")) throw new ArgumentException("invalid_task_id");
        Active.TryGetValue(id,out var active);
        if(active!=null&&active.AccountKey!=Hash(Account))return null;
        var file = RecordPath(id);
        if (!File.Exists(file)) return null;
        var saved = JsonSerializer.Deserialize<YanziTaskRecord>(File.ReadAllText(file), Json)!;
        // A process restart cannot prove whether an in-flight side effect happened.
        if (saved.Status == "running"&&!OwnerAlive(saved)) { saved.Status = "unknown"; saved.CanCancel = false; saved.Stage = "运行中断，需核对原回执"; }
        if(active!=null){Active[id]=saved;return saved;}
        return saved;
    }
    private static bool OwnerAlive(YanziTaskRecord record)
    {
        try{using var process=System.Diagnostics.Process.GetProcessById(record.OwnerProcessId);return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==record.OwnerStartTicks;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }
    public static YanziTaskRecord Begin(string operation, string requestId, string signature, string target)
    {
        if (requestId.Length is < 8 or > 200 || operation.Length == 0) throw new ArgumentException("invalid_request_id_or_operation");
        signature=Hash(signature);
        YanziTaskRecord record;
        lock (Gate)
        {
            using var journal=new JournalLock();
            string id = Key(requestId);
            var old = Load(id);
            if (old != null)
            {
                if (old.Operation != operation || old.Signature != signature) throw new ArgumentException("request_id_conflict");
                return old with { Replay = true };
            }
            record = new() { TaskId=id, AccountKey=Hash(Account), OwnerProcessId=Environment.ProcessId,OwnerStartTicks=ProcessStartTicks,RequestId=requestId, Operation=operation, Signature=signature, Target=target };
            Save(record); Active[id]=record;
        }
        YanziTaskProgressWindow.Refresh();
        return record;
    }
    public static YanziTaskRecord Status(string taskId) { lock (Gate) {using var journal=new JournalLock();return (Load(taskId) ?? throw new KeyNotFoundException("task_not_found")) with { };} }
    public static YanziTaskRecord StatusByRequest(string requestId) => Status(Key(requestId));
    private static YanziTaskRecord GetActive(string id) => Active.ContainsKey(id)&&(Load(id) is { } r)&&r.AccountKey==Hash(Account)
        ? r : throw new KeyNotFoundException("task_not_active");
    public static YanziTaskRecord[] Running() { lock (Gate) return Active.Values.Where(r=>r.AccountKey==Hash(Account)).Select(r=>r with { }).OrderBy(r=>r.CreatedAt).ToArray(); }
    public static YanziTaskRecord Update(string id, string stage)
    {
        YanziTaskRecord result;
        lock (Gate)
        {
            using var journal=new JournalLock();
            var r = GetActive(id);
            if (r.CancelRequested && !r.Submitted) return r with { };
            r.Stage=stage; r.UpdatedAt=DateTimeOffset.UtcNow; Save(r); result=r with { };
        }
        YanziTaskProgressWindow.Refresh(); return result;
    }
    public static void Check(string id) { var r=Status(id); if((r.CancelRequested||CancelSignals.ContainsKey(id))&&!r.Submitted)throw new OperationCanceledException("task_cancelled"); }
    internal static void RequestCancelFromKeyboard(string id)
    {
        CancelSignals[id]=true;
        _=Task.Run(()=>{try{Cancel(id);}catch(Exception ex){HostAssets.AppendLog("Task cancel persistence failed: "+ex.Message);}});
    }
    internal static void ForgetActiveForVerification(string id) {lock(Gate){using var journal=new JournalLock();var r=GetActive(id);Save(r with {OwnerProcessId=int.MaxValue});Active.Remove(id);}}
    public static YanziTaskRecord Submit(string id)
    {
        YanziTaskRecord result;
        lock (Gate)
        {
            using var journal=new JournalLock();
            var r=GetActive(id);
            if(r.CancelRequested||CancelSignals.ContainsKey(id)) {r.CancelRequested=true;r.CanCancel=false;Save(r);return r with { };}
            if(!YanziTaskProgressWindow.InterruptAvailable)throw new InvalidOperationException("task_interrupt_service_unavailable");
            r.Submitted=true; r.CanCancel=false; r.Stage="已提交 · 正在核对回执"; r.UpdatedAt=DateTimeOffset.UtcNow; Save(r); result=r with { };
        }
        YanziTaskProgressWindow.Refresh(); return result;
    }
    public static YanziTaskRecord Cancel(string id)
    {
        YanziTaskRecord result;
        lock(Gate)
        {
            using var journal=new JournalLock();
            var r=Load(id)??throw new KeyNotFoundException("task_not_found");
            if(r.Status=="running"&&!r.Submitted) {r.CancelRequested=true;r.CanCancel=false;r.Stage="正在中断 · 保留草稿";r.UpdatedAt=DateTimeOffset.UtcNow;Save(r);}
            result=r with { };
        }
        YanziTaskProgressWindow.Refresh();return result;
    }
    public static JsonElement Finish(string id, object result)
    {
        JsonElement output;
        YanziTaskRecord finished;
        lock(Gate)
        {
            using var journal=new JournalLock();
            var r=GetActive(id);
            var node=JsonSerializer.SerializeToNode(result,Json);
            if(node is JsonObject obj){obj["taskId"]=r.TaskId;obj["requestId"]=r.RequestId;}
            output=JsonSerializer.SerializeToElement(node,Json);
            r.Result=output;
            string state=output.ValueKind==JsonValueKind.Object&&output.TryGetProperty("status",out var status)?status.GetString()??"completed":"completed";
            r.Status=state is "uncertain" or "unknown" ? "unknown" : state=="cancelled"?"cancelled":state=="pending"||state=="prepared_unconfirmed"?"pending":state=="failed"?"failed":"completed";
            r.CanCancel=false;r.Stage=r.Status switch {"cancelled"=>"已中断", "unknown"=>"结果待核对", "pending"=>"等待回执", "failed"=>"任务失败", _=>"任务结束"};
            r.UpdatedAt=DateTimeOffset.UtcNow;Save(r);Active.Remove(id);CancelSignals.TryRemove(id,out _);
            finished=r with { };
        }
        YanziTaskProgressWindow.Refresh(finished);return output;
    }
    public static object ReplayResult(YanziTaskRecord record) => record.Result is { } result ? result : new
    {status=record.Status=="running"?"busy":"unknown",taskId=record.TaskId,requestId=record.RequestId,submitted=record.Submitted,reason="query_original_task_do_not_resend"};
    private static async Task<object?> QueryAsync(JsonElement input)
    {
        var record=S(input,"taskId").Length>0?Status(S(input,"taskId")):StatusByRequest(S(input,"requestId"));
        if(record.Status is not ("pending" or "unknown") && !(record.Operation=="chat.send"&&record.Status=="completed") ||record.Result is not { } result)return record;
        string? capability=null;object? args=null;
        if(record.Operation=="chat.send"&&S(result,"messageId").Length>0){capability="chat.status";args=new{messageId=S(result,"messageId")};}
        else if(record.Operation is "wechat.messages.sendFile" or "wechat.messages.sendImage"){capability="wechat.messages.attachmentStatus";args=new{requestId=record.RequestId};}
        if(capability==null)return record;
        var observed=await YanziCapabilityInvocationService.InvokeAsync(capability,args,YanziCapabilityCaller.LocalAgent);
        if(!observed.Success)return new{task=record,queryError=observed.Error};
        var node=JsonSerializer.SerializeToNode(observed.Data,Json);
        if(node is not JsonObject obj)return record;
        obj["taskId"]=record.TaskId;obj["requestId"]=record.RequestId;
        var updated=JsonSerializer.SerializeToElement(obj,Json);
        lock(Gate)
        {
            using var journal=new JournalLock();
            // A receipt query updates observation only; it never submits an action.
            record.Result=updated;
            var state=S(updated,"status");
            record.Status=state is "confirmed" or "completed"?"completed":state=="failed"?"failed":record.Status;
            record.UpdatedAt=DateTimeOffset.UtcNow;Save(record);
        }
        return record;
    }
    private static string S(JsonElement p,string name,string fallback="") => p.TryGetProperty(name,out var v)?v.GetString()??fallback:fallback;
    private static JsonElement Schema(string fields,string required) => YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{"+fields+"},\"required\":["+required+"],\"additionalProperties\":false}");
    public static IEnumerable<YanziCapabilityProviderDefinition> Providers()
    {
        var taskSchema=Schema("\"taskId\":{\"type\":\"string\"}","\"taskId\"");
        yield return new(){Name="task.begin",Audience="system",Permissions=["device.message.send"],InputSchema=Schema("\"operation\":{\"type\":\"string\"},\"requestId\":{\"type\":\"string\"},\"signature\":{\"type\":\"string\"},\"target\":{\"type\":\"string\"}","\"operation\",\"requestId\",\"signature\""),OutputSchema=YanziCapabilitySchema.Parse("{\"type\":\"object\"}"),Handler=p=>{var j=(JsonElement)p!;return Task.FromResult<object?>(Begin(S(j,"operation"),S(j,"requestId"),S(j,"signature"),S(j,"target")));}};
        yield return new(){Name="task.update",Audience="system",Permissions=["device.message.send"],InputSchema=Schema("\"taskId\":{\"type\":\"string\"},\"stage\":{\"type\":\"string\"}","\"taskId\",\"stage\""),Handler=p=>{var j=(JsonElement)p!;return Task.FromResult<object?>(Update(S(j,"taskId"),S(j,"stage")));}};
        yield return new(){Name="task.check",Audience="system",Permissions=["device.message.send"],InputSchema=taskSchema,Handler=p=>Task.FromResult<object?>(Status(S((JsonElement)p!,"taskId")))};
        yield return new(){Name="task.submit",Audience="system",Permissions=["device.message.send"],InputSchema=taskSchema,Handler=p=>Task.FromResult<object?>(Submit(S((JsonElement)p!,"taskId")))};
        yield return new(){Name="task.finish",Audience="system",Permissions=["device.message.send"],InputSchema=Schema("\"taskId\":{\"type\":\"string\"},\"result\":{\"type\":\"object\"}","\"taskId\",\"result\""),Handler=p=>{var j=(JsonElement)p!;return Task.FromResult<object?>(Finish(S(j,"taskId"),j.GetProperty("result")));}};
        yield return new(){Name="task.status",Description="只读查询原任务、请求编号及回执；不重新发送。taskId 与 requestId 二选一；待确认的手机/附件任务会只读核对原回执。",Permissions=["device.message.read"],InputSchema=Schema("\"taskId\":{\"type\":\"string\"},\"requestId\":{\"type\":\"string\"}",""),Handler=p=>QueryAsync((JsonElement)p!)};
        yield return new(){Name="task.cancel",Description="请求中断原任务；仅在提交前生效。已经提交的任务继续核对回执，不能撤回。",Permissions=["device.message.send"],InputSchema=taskSchema,Handler=p=>Task.FromResult<object?>(Cancel(S((JsonElement)p!,"taskId")))};
        yield return new(){Name="task.health",Description="查询统一任务服务、活动任务及能力提供者状态和版本。",Permissions=["application.read"],InputSchema=YanziCapabilitySchema.EmptyObject,Handler=_=>Task.FromResult<object?>(new{service="operation-tasks",version="1.0",activeTasks=Running().Select(r=>new{r.TaskId,r.Operation,r.Status,r.Stage,r.CanCancel,r.Submitted}),catalog=YanziAgentCapabilityCatalog.Create()})};
    }
}

public sealed record YanziTaskRecord
{
    public int OwnerProcessId {get;init;}
    public long OwnerStartTicks {get;init;}
    public string AccountKey {get;init;}="";
    public string TaskId {get;init;}="";
    public string RequestId {get;init;}="";
    public string Operation {get;init;}="";
    public string Signature {get;init;}="";
    public string Target {get;init;}="";
    public string Status {get;set;}="running";
    public string Stage {get;set;}="正在准备";
    public bool CanCancel {get;set;}=true;
    public bool CancelRequested {get;set;}
    public bool Submitted {get;set;}
    public bool Replay {get;init;}
    public DateTimeOffset CreatedAt {get;init;}=DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt {get;set;}=DateTimeOffset.UtcNow;
    public JsonElement? Result {get;set;}
}
