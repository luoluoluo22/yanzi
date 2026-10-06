using OpenQuickHost;
using OpenQuickHost.Sync;
using System.Text.Json;
internal static class PlatformTaskVerification
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"YanziDev","task-journal",Guid.NewGuid().ToString("N"));
        using var scope=HostAssets.UseIsolatedDataRootForVerification(root);
        var account="https://fixture.invalid\nuser-a";var id=Guid.NewGuid().ToString("N");
        var body=JsonSerializer.Serialize(new {clientMessageId=id,kind="run-extension",title="任务验证",targetDeviceId="original-pc",text="must-not-journal-request-body"});
        PlatformTaskJournal.Envelope(account,body);PlatformTaskJournal.Envelope(account,body);
        Check(PlatformTaskJournal.List(account).Count==1,"Stable logical id");
        PlatformTaskJournal.Envelope(account,body,status:"unknown",result:"timeout");
        Check(PlatformTaskJournal.List(account)[0].Status=="unknown","Ambiguous transport status");
        PlatformTaskJournal.Envelope(account,body,"msg_fixture","submitted");
        var message=new DeviceMessageRecord {MessageId="msg_fixture",Kind="run-extension",Title="任务验证",Status="completed",TargetDeviceId="original-pc",Payload=new() { ["executionResult"]=JsonSerializer.SerializeToElement(new {output="original result"}) }};
        PlatformTaskJournal.Observed(account,message);message.Status="pending";PlatformTaskJournal.Observed(account,message);
        var receipt=PlatformTaskJournal.List(account).Single();
        Check(receipt.Status=="completed"&&receipt.Target=="original-pc"&&receipt.Result.Contains("original result"),"Late receipt cannot regress result or target");
        Check(PlatformTaskJournal.List("https://fixture.invalid\nuser-b").Count==0&&PlatformTaskJournal.List("https://other.invalid\nuser-a").Count==0,"Account/server isolation");
        Check(!Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories).Any(path=>File.ReadAllText(path).Contains("must-not-journal-request-body")),"Request body excluded");
        Check(CloudSyncHistoryVersionView.FromRecord(new CloudSyncObjectHistoryRecord { Revision=4, Deleted=false },7).CanRestore,"Tombstoned current object permits live history recovery");
        Console.WriteLine("PLATFORM_TASK_JOURNAL=PASSED (persistence, stable ids, ambiguity, monotonic receipts, target, account/server isolation, request-body exclusion, recovery visibility)");
    }
    private static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);}
}
