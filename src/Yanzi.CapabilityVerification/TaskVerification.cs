using System.Reflection;
using System.Text.Json;
using OpenQuickHost;

internal static class TaskVerification
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"YanziDev","task-journal",Guid.NewGuid().ToString("N"));
        using var isolated=(IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[root])!;
        int checks=0;void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
        var record=YanziTaskService.Begin("chat.send","task-fixture-001","content-one","All phones");
        Check(record.CanCancel&&!record.Submitted&&!record.Replay,"new_task_cancellable");
        Check(YanziTaskService.Begin("chat.send","task-fixture-001","content-one","All phones").Replay,"concurrent_duplicate_not_started");
        try{YanziTaskService.Begin("chat.send","task-fixture-001","different-content","All phones");throw new Exception("conflict_not_rejected");}catch(ArgumentException){checks++;}
        YanziTaskService.Cancel(record.TaskId);
        Check(YanziTaskService.Submit(record.TaskId).CancelRequested&&!YanziTaskService.Status(record.TaskId).Submitted,"cancel_before_commit_blocks_submit");
        try{YanziTaskService.Check(record.TaskId);throw new Exception("cancel_check_not_rejected");}catch(OperationCanceledException){checks++;}
        var cancelled=YanziTaskService.Finish(record.TaskId,new{status="cancelled",sent=false});
        Check(cancelled.GetProperty("requestId").GetString()==record.RequestId,"response_has_stable_request_id");
        var replay=YanziTaskService.Begin("chat.send","task-fixture-001","content-one","All phones");
        Check(replay.Replay&&replay.Result!.Value.GetProperty("sent").GetBoolean()==false,"cancelled_request_replay_cannot_send");
        Check(YanziTaskService.Running().Length==0,"finished_tasks_removed_from_active_ui");
        var submitted=YanziTaskService.Begin("wechat.messages.sendFile","task-fixture-002","file-hash","Assistant");
        YanziTaskService.Submit(submitted.TaskId);
        var lateCancel=YanziTaskService.Cancel(submitted.TaskId);
        Check(lateCancel.Submitted&&!lateCancel.CancelRequested&&!lateCancel.CanCancel,"late_cancel_cannot_claim_recall");
        YanziTaskService.Check(submitted.TaskId);
        YanziTaskService.Finish(submitted.TaskId,new{status="uncertain",sent=(bool?)null});
        Check(YanziTaskService.StatusByRequest("task-fixture-002").Status=="unknown","uncertain_effect_kept_unknown");
        var interrupted=YanziTaskService.Begin("chat.send","task-fixture-003","same","All phones");
        YanziTaskService.Submit(interrupted.TaskId);
        typeof(YanziTaskService).GetMethod("ForgetActiveForVerification",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[interrupted.TaskId]);
        var recovered=YanziTaskService.Begin("chat.send","task-fixture-003","same","All phones");
        Check(recovered.Replay&&recovered.Status=="unknown"&&recovered.Submitted,"restart_recovery_never_replays_unknown_send");
        Check(YanziTaskService.StatusByRequest("task-fixture-003").TaskId==interrupted.TaskId,"query_by_request_after_restart");
        try{YanziTaskService.Status("../../outside");throw new Exception("path_not_rejected");}catch(ArgumentException){checks++;}
        YanziBuiltinCapabilityRegistration.Register();
        Check(YanziCapabilityRegistry.TryGet("task.status",out var status)&&status!.Audience=="user","status_discoverable");
        Check(YanziCapabilityRegistry.TryGet("task.cancel",out _),"cancel_discoverable");
        Check(YanziCapabilityRegistry.TryGet("task.health",out _),"health_discoverable");
        Check(YanziCapabilityRegistry.TryGet("task.submit",out var submit)&&submit!.Audience=="system","submission_bridge_not_public_mcp_tool");
        Console.WriteLine($"TASK_JOURNAL_IDEMPOTENCY_CANCEL_RECOVERY=PASSED; checks={checks}");
    }
}
