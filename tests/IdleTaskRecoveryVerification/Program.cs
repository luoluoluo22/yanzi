using System;
using System.IO;
using System.Reflection;

if (args.Length > 0 && args[0] == "--force-once")
{
    // Explicitly requested one-shot by owner. Do not modify automatic idle settings.
    var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "ExtensionStorage", "idle-chatgpt-tasks");
    var before = IdleTaskRepository.Load(data);
    Console.WriteLine("BEFORE=" + string.Join(";", before.Where(t => t.RepeatEnabled)
        .Select(t => t.Id + ":" + t.Status + ":" + t.RetryCount)));
    var output = await IdleTaskWorker.ProcessNextAsync(new IdleTaskRuntime(data, "idle-chatgpt-tasks",
        "http://127.0.0.1:53919", ""));
    Console.WriteLine("FORCE_ONCE_RESULT=" + output.Substring(0, Math.Min(output.Length, 320)));
    var after = IdleTaskRepository.Load(data);
    Console.WriteLine("AFTER=" + string.Join(";", after.Where(t => t.RepeatEnabled)
        .Select(t => t.Id + ":" + t.Status + ":" + t.RetryCount + ":" + t.BridgeJobId)));
    return;
}

if (args.Length > 0 && args[0] == "--production-smoke")
{
    var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "ExtensionStorage", "idle-chatgpt-tasks");
    var snapshot = IdleTriggerRuntimeSnapshot.TryLoad(dataDirectory);
    if (snapshot == null || DateTimeOffset.Now - snapshot.ObservedAt > TimeSpan.FromSeconds(30)
        || snapshot.IdleSeconds < 300 || snapshot.Fullscreen)
        throw new InvalidOperationException("Production idle gate is not currently satisfied");
    var tasksBefore = IdleTaskRepository.Load(dataDirectory);
    Console.WriteLine("PROD_SMOKE_BEFORE=" +
        string.Join(";", tasksBefore.Where(t => t.RepeatEnabled).Select(t => t.Title + ":" + t.Status)));
    var result = await IdleTaskWorker.ProcessNextAsync(new IdleTaskRuntime(dataDirectory, "idle-chatgpt-tasks",
        "http://127.0.0.1:53919", ""));
    Console.WriteLine("PROD_WORKER_RESULT=" + result.Substring(0, Math.Min(result.Length, 180)));
    var tasksAfter = IdleTaskRepository.Load(dataDirectory);
    Console.WriteLine("PROD_SMOKE_AFTER=" +
        string.Join(";", tasksAfter.Where(t => t.RepeatEnabled)
            .Select(t => t.Title + ":" + t.Status + ":retry=" + t.RetryCount)));
    return;
}

var root = Path.Combine(Path.GetTempPath(), "yanzi-idle-retry-verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("FAILED: " + label);
    checks++;
}
IdleTaskRecord? Pick(string dir)
{
    var method = typeof(IdleTaskWorker).GetMethod("SelectNextTask", BindingFlags.Static | BindingFlags.NonPublic)!;
    return (IdleTaskRecord?)method.Invoke(null, new object[] {dir});
}
string Folder() { var d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
IdleTaskRecord Build(string id, string status, int retries = 0) => new()
{
    Id = id, Title = id, Prompt = "SAFE FIXTURE", Status = status, Kind = "chatgpt", Enabled = true,
    RepeatEnabled = true, BridgeJobId = "fake-old-bridge-" + id,
    CreatedAt = DateTimeOffset.Now.AddDays(-2), UpdatedAt = DateTimeOffset.Now.AddHours(-1),
    LastCompletedAt = DateTimeOffset.Now.AddHours(-1), RetryCount = retries, QueuedAt = DateTimeOffset.Now.AddHours(-2)
};
try
{
    var a = Folder();
    var interrupted = Build("interrupted-legacy", "interrupted");
    IdleTaskRepository.Upsert(a, interrupted);
    Check(Pick(a)?.Id == interrupted.Id, "legacy interrupted automatically due");

    var transportFolder = Folder();
    var channelDropped = Build("channel-dropped", "error");
    channelDropped.Error = "A listener indicated an asynchronous response by returning true, but the message channel closed before a response was received";
    IdleTaskRepository.Upsert(transportFolder, channelDropped);
    Check(IdleTaskWorker.IsMessageChannelFailure(channelDropped.Error),
        "Chrome async message channel failure is recognized");
    Check(Pick(transportFolder)?.Id == channelDropped.Id,
        "legacy message channel error is scheduled for controlled reconciliation");
    channelDropped.RetryCount = IdleTaskWorker.MaxInterruptedRetries;
    IdleTaskRepository.Upsert(transportFolder, channelDropped);
    Check(Pick(transportFolder) == null, "message channel retry respects limit");

    var genericErrorFolder = Folder();
    var permanentError = Build("permanent-error", "error");
    permanentError.Error = "business action rejected by server";
    IdleTaskRepository.Upsert(genericErrorFolder, permanentError);
    Check(Pick(genericErrorFolder) == null, "generic errors never auto-retry");

    var noPreviousJobFolder = Folder();
    var noPreviousJob = Build("channel-no-previous", "error");
    noPreviousJob.Error = channelDropped.Error;
    noPreviousJob.BridgeJobId = null;
    IdleTaskRepository.Upsert(noPreviousJobFolder, noPreviousJob);
    Check(Pick(noPreviousJobFolder) == null, "transport failure without previous job is held for inspection");

    var reviewFolder = Folder();
    var review = Build("uncertain-review", "needs-review");
    IdleTaskRepository.Upsert(reviewFolder, review);
    Check(Pick(reviewFolder) == null, "uncertain page delivery never auto-runs");
    Check(IdleTaskRecord.IsError(review), "uncertain delivery appears in abnormal task filter");

    using (var uncertainJson = System.Text.Json.JsonDocument.Parse(
        """{"status":"error","message":"closed","data":{"deliveryUncertain":true,"tabId":123}}"""))
    {
        var parser = typeof(IdleTaskBridge).GetMethod("ParseJob", BindingFlags.Static | BindingFlags.NonPublic)!;
        var uncertainJob = (IdleBridgeJob)parser.Invoke(null, new object[] {uncertainJson.RootElement})!;
        Check(uncertainJob.DeliveryUncertain && uncertainJob.Status == "error",
            "bridge marks uncertain delivery distinctly from ordinary error");
    }

    var b = Folder();
    var timeout = Build("timeout-legacy", "timeout");
    IdleTaskRepository.Upsert(b, timeout);
    Check(Pick(b)?.Id == timeout.Id, "legacy timeout automatically due");

    var c = Folder();
    var cooling = Build("cooldown", "interrupted");
    cooling.NextRetryAt = DateTimeOffset.Now.AddMinutes(5);
    IdleTaskRepository.Upsert(c, cooling);
    Check(Pick(c) == null, "respect next retry schedule");

    var d = Folder();
    var exhausted = Build("exhausted", "timeout", IdleTaskWorker.MaxInterruptedRetries);
    IdleTaskRepository.Upsert(d, exhausted);
    Check(Pick(d) == null, "enforce retry limit");

    var e = Folder();
    var missingId = Build("missing-id", "interrupted");
    missingId.BridgeJobId = null;
    IdleTaskRepository.Upsert(e, missingId);
    Check(Pick(e) == null, "do not blindly resend without prior Job ID");

    var f = Folder();
    var active = Build("active", "running");
    active.LastCheckedAt = DateTimeOffset.Now;
    IdleTaskRepository.Upsert(f, active);
    var other = Build("retry", "timeout");
    IdleTaskRepository.Upsert(f, other);
    Check(Pick(f) == null, "active job reserves queue until next poll");
    active.LastCheckedAt = DateTimeOffset.Now.AddMinutes(-11);
    IdleTaskRepository.Upsert(f, active);
    Check(Pick(f)?.Id == active.Id, "live job polled at ten minutes before retry");

    var g = Folder();
    var succeeded = Build("completed", "success");
    IdleTaskRepository.Upsert(g, succeeded);
    Check(Pick(g) == null, "successful task never retried");

    var cooldownDir = Folder();
    var delayed = Build("delayed-pending", "pending");
    delayed.BridgeJobId = null;
    delayed.NextEligibleAt = DateTimeOffset.Now.AddMinutes(90);
    IdleTaskRepository.Upsert(cooldownDir, delayed);
    Check(Pick(cooldownDir) == null, "long-term pending observation gate prevents wakeup");
    delayed.NextEligibleAt = DateTimeOffset.Now.AddMinutes(-1);
    IdleTaskRepository.Upsert(cooldownDir, delayed);
    Check(Pick(cooldownDir)?.Id == delayed.Id, "pending task becomes selectable when gate expires");

    var h = Folder();
    var pending = Build("pending", "pending");
    pending.BridgeJobId = null;
    IdleTaskRepository.Upsert(h, pending);
    Check(Pick(h)?.Id == pending.Id, "ordinary queued tasks still selected");

    var p = Build("prompt", "timeout", 1);
    Check(IdleTaskBridge.ComposePrompt(p).Contains("自动恢复 1/3"), "resume prompt contains retry metadata");
    Check(IdleTaskBridge.ComposePrompt(p) == IdleTaskBridge.ComposePrompt(p), "same payload for submit and reconciliation");
    Check(IdleTaskBridge.ComposePrompt(Build("initial","pending")) == "SAFE FIXTURE", "original task prompt unchanged");
    Check(IdleTaskRecord.ReadReportedStatus(@"note: {""deploymentStatus"":""blocked""}", "deploymentStatus") == "blocked", "detect deployment blocked in successful Bridge result");
    Check(IdleTaskRecord.ReadReportedStatus(@"note: {""releaseStatus"":""published""}", "releaseStatus") == "published", "detect published release");
    Check(IdleTaskRecord.ReadReportedStatus("completed, but no JSON status", "deploymentStatus") == null, "no inferred business outcome without evidence");
    Check(IdleTaskRecord.ReadReportedStatus("note: {\\\"token\\\":\\\"something\\\"}", "token") == null, "only known business fields accepted");
    var complete = typeof(IdleTaskWorker).GetMethod("CompleteSuccess", BindingFlags.NonPublic | BindingFlags.Static)!;
    var businessFolder = Folder();
    var blockedTask = Build("business-blocked", "running");
    blockedTask.Result = @"Agent response: {""deploymentStatus"":""blocked"",""releaseStatus"":""blocked""}";
    blockedTask.MinimumRepeatMinutes = 360;
    IdleTaskRepository.Upsert(businessFolder, blockedTask);
    complete.Invoke(null, new object[] { businessFolder, blockedTask });
    var blockedSaved = IdleTaskRepository.Load(businessFolder).Single(x => x.Id == "business-blocked");
    Check(blockedSaved.LastRunStatus == "success" && blockedSaved.LastBusinessStatus == "blocked",
        "technical success keeps business blocked distinct");
    Check(blockedSaved.LastDeploymentStatus == "blocked" && blockedSaved.LastReleaseStatus == "blocked",
        "business statuses survive durable repository save");
    Check(blockedSaved.NextEligibleAt > DateTimeOffset.Now.AddHours(5),
        "blocked response respects retry observation gate instead of busy looping");

    var publishedFolder = Folder();
    var publishedTask = Build("business-published", "running");
    publishedTask.Result = @"Agent response: {""releaseStatus"":""published""}";
    IdleTaskRepository.Upsert(publishedFolder, publishedTask);
    complete.Invoke(null, new object[] { publishedFolder, publishedTask });
    Check(IdleTaskRepository.Load(publishedFolder).Single().LastBusinessStatus == "published",
        "published business status survives completion");

    Console.WriteLine("IDLE_RETRY_TESTS_PASS=" + checks);
}
finally
{
    Directory.Delete(root, true);
}
