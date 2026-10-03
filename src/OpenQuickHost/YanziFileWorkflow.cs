using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

// Generic companion file tickets and durable workflow results; no gallery business logic.
public static class YanziFileWorkflow
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    public const long Limit = 30L * 1024 * 1024;
    public static string Root => HostAssets.ResolveDataDirectoryPath("companion-files/" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SyncSessionStore.Load()?.UserId ?? "local"))));
    public static string Ticket(string id) {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("invalid_file_ticket");
        Directory.CreateDirectory(Root); return Path.Combine(Root, id + ".bin");
    }
    public static async Task<CloudSyncClient> Cloud() {
        var app = System.Windows.Application.Current;
        return await app.Dispatcher.InvokeAsync(() => (app.MainWindow as MainWindow)?.CloudSyncClient ?? throw new InvalidOperationException("account_login_required"));
    }
    public static IEnumerable<YanziCapabilityProviderDefinition> Providers() {
        yield return new() { Name="files.workflow.run", Description="将附件交给小程序处理，返回可校验的文件结果", Permissions=["files.workflow"],
            InputSchema=YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"jobId\":{\"type\":\"string\"},\"transferId\":{\"type\":\"string\"},\"attachmentId\":{\"type\":\"string\"},\"capability\":{\"type\":\"string\"},\"parameters\":{\"type\":\"object\"}},\"required\":[\"jobId\",\"capability\"]}"),
            OutputSchema=YanziCapabilitySchema.Parse("{\"type\":\"object\"}"), Handler=p=>Run((JsonElement)p!) };
    }
    private static async Task<object?> Run(JsonElement p) {
        var job=p.GetProperty("jobId").GetString()!; Ticket(job);
        var gate=Locks.GetOrAdd(Root+job,_=>new(1,1)); await gate.WaitAsync();
        try {
            var cloud=await Cloud(); var root=Root;
            var ledger=Path.Combine(root,job+".json");
            var capability=p.GetProperty("capability").GetString()!;
            var parameters=p.TryGetProperty("parameters",out var a)?a:JsonSerializer.SerializeToElement(new{});
            var input=p.TryGetProperty("transferId",out var t)?Ticket(t.GetString()!):await cloud.DownloadMobileAttachmentAsync(p.GetProperty("attachmentId").GetString()!,root);
            if (!File.Exists(input) || new FileInfo(input).Length>Limit) throw new IOException("invalid_workflow_input");
            var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(input))).ToLowerInvariant();
            var signature=hash+"\n"+capability+"\n"+parameters.GetRawText();
            if (File.Exists(ledger)) {
                using var old=JsonDocument.Parse(await File.ReadAllTextAsync(ledger));
                if(old.RootElement.GetProperty("signature").GetString()!=signature) throw new IOException("job_id_conflict");
                if(old.RootElement.TryGetProperty("result",out var saved)) {
                    if (!p.TryGetProperty("transferId",out _) && saved.TryGetProperty("transferId",out var localTicket)) {
                        var restored=Path.Combine(root,job,saved.GetProperty("fileName").GetString()!);
                        File.Copy(Ticket(localTicket.GetString()!),restored,true);
                        return new {jobId=job,attachment=await cloud.UploadMobileAttachmentAsync(restored)};
                    }
                    return saved.Clone();
                }
                throw new IOException("workflow_result_unknown");
            }
            if (!YanziCapabilityRegistry.Contains(capability) || capability=="files.workflow.run") throw new IOException("workflow_not_running");
            var outputDir=Path.Combine(root,job); Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(ledger,JsonSerializer.Serialize(new{signature,state="processing"}));
            var invoked=await YanziCapabilityInvocationService.InvokeAsync(capability,new {inputPath=input,outputDirectory=outputDir,parameters},YanziCapabilityCaller.LocalAgent);
            if(!invoked.Success) throw new IOException(invoked.Error ?? "workflow_failed");
            var data=JsonSerializer.SerializeToElement(invoked.Data);
            var output=data.GetProperty("outputPath").GetString()!;
            if(!YanziDeviceResourceAuthorization.AllowsFile(output,[outputDir]) || !File.Exists(output) || new FileInfo(output).Length<=0 || new FileInfo(output).Length>Limit) throw new IOException("workflow_output_denied");
            var resultId=Guid.NewGuid().ToString("N"); var resultFile=Ticket(resultId); File.Copy(output,resultFile);
            var resultHash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(resultFile))).ToLowerInvariant();
            object result=p.TryGetProperty("transferId",out _)?new {jobId=job,transferId=resultId,fileName=Path.GetFileName(output),sha256=resultHash,size=new FileInfo(output).Length}:
                new {jobId=job,attachment=await cloud.UploadMobileAttachmentAsync(output)};
            var temporary=ledger+".tmp"; await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new{signature,state="completed",result})); File.Move(temporary,ledger,true);
            return result;
        } finally { gate.Release(); }
    }
}
