using System.Text.Json;
using OpenQuickHost;

internal static class HostDispatchVerification
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Host dispatch verification: " + message);
        Console.WriteLine("PASS " + message);
    }

    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "yanzi-dispatch-verification-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "journal.json");
        try
        {
            var journal = new HostDispatchJournal(path);
            var actionCount = 0;
            await journal.DispatchAsync("fixture-1", "host.idle", _ => {
                actionCount++;
                return Task.CompletedTask;
            });
            Check(actionCount == 1 && journal.Snapshot().Last(x => x.EventName == "host.idle").State == "dispatched",
                "dispatch receipt is durable");

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var longRun = journal.DispatchAsync("fixture-1", "host.schedule", async _ => {
                entered.TrySetResult();
                await release.Task;
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicateRan = false;
            var dispatchedDuplicate = await journal.DispatchAsync("fixture-1", "host.idle", _ => {
                duplicateRan = true; return Task.CompletedTask;
            });
            Check(!dispatchedDuplicate && !duplicateRan, "same extension cannot run concurrently");
            var exitSaved = false;
            await journal.DispatchExitAsync("fixture-1", _ => {
                exitSaved = true;
                return Task.CompletedTask;
            }, CancellationToken.None);
            Check(exitSaved && journal.Snapshot().Any(x => x.EventName == "host.beforeExit"
                && x.State == "dispatched"), "exit save bypasses busy normal task slot");
            release.SetResult();
            await longRun;
            Check(journal.Snapshot().Any(x => x.EventName == "host.schedule" && x.State == "dispatched"),
                "in-flight job completed");

            try
            {
                await journal.DispatchAsync("fixture-2", "network.online", _ =>
                    throw new InvalidOperationException("fixture-failure"));
                throw new InvalidOperationException("failure should propagate");
            }
            catch (InvalidOperationException ex) when (ex.Message == "fixture-failure") { }
            Check(journal.Snapshot().Any(x => x.ExtensionId == "fixture-2" &&
                x.State == "failed" && (x.Detail ?? "").Contains("fixture-failure")), "failed job is recorded");

            journal.RecordDecision("fixture-3", "host.idle", "fullscreen");
            journal.RecordDecision("fixture-3", "host.idle", "fullscreen");
            Check(journal.Snapshot().Count(x => x.ExtensionId == "fixture-3" && x.State == "waiting") == 1,
                "idle reasons are coalesced");

            var all = journal.Snapshot().ToList();
            all.Add(new HostDispatchJournal.Entry("interrupted-fixture", "fixture-4",
                "host.schedule", "running", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            File.WriteAllText(path, JsonSerializer.Serialize(all));
            var resumed = new HostDispatchJournal(path);
            Check(resumed.Interrupted().Any(x => x.Id == "interrupted-fixture"), "restart detects interrupted jobs");
            Check(!resumed.Snapshot().Any(x => x.Id == "interrupted-fixture" && x.State == "dispatched"),
                "restart does not falsely acknowledge interrupted work");
            resumed.MarkRecovered("interrupted-fixture");
            Check(resumed.Snapshot().Any(x => x.Id == "interrupted-fixture" && x.State == "recovered"),
                "explicit recovery is tracked");
            Check(new HostDispatchJournal(path).Snapshot().Any(x => x.Id == "interrupted-fixture" && x.State == "recovered"),
                "recovery receipts survive another restart");

            File.WriteAllText(path, "{invalid-journal");
            var recoveredFromDamage = new HostDispatchJournal(path);
            Check(recoveredFromDamage.Snapshot().Count == 0 &&
                Directory.GetFiles(root, "journal.json.corrupt-*").Length == 1,
                "corrupt journal is preserved before recovering");


            var manifest = JsonSerializer.Deserialize<OpenQuickHost.Sync.LocalExtensionManifest>(
                """{"id":"stage2-fixture","name":"test","events":{"execution":{"priority":"high","conditions":{"requireOnline":true,"maxCpuPercent":30}}}}""",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Check(manifest?.Events?.Execution?.Priority == "high" &&
                manifest.Events.Execution.Conditions?.RequireOnline == true &&
                manifest.Events.Execution.Conditions.MaxCpuPercent == 30,
                "stage2 event conditions load from manifest.json");

            // Stage 2: fail-closed compound resource conditions.
            var policy = new OpenQuickHost.Sync.LocalExtensionHostExecutionManifest
            {
                Priority = "low",
                Conditions = new OpenQuickHost.Sync.LocalExtensionHostConditionsManifest
                {
                    RequireOnline = true, RequireAcPower = true,
                    MaxCpuPercent = 30, MinIdleMinutes = 10
                }
            };
            var full = new HostDispatchPolicy.Snapshot(true, true, 18, 12);
            Check(HostDispatchPolicy.Evaluate(policy, full).Allowed, "AND conditions all true");
            Check(HostDispatchPolicy.Evaluate(policy, full with { Online = false }).Reason == "network-offline",
                "network offline prevents dispatch");
            Check(HostDispatchPolicy.Evaluate(policy, full with { AcPower = null }).Reason == "power-unknown",
                "unknown AC state fails closed");
            Check(HostDispatchPolicy.Evaluate(policy, full with { CpuPercent = 72 }).Reason == "cpu-high",
                "CPU threshold is enforced");
            Check(HostDispatchPolicy.Evaluate(policy, full with { CpuPercent = null }).Reason == "cpu-sampling",
                "missing CPU sample cannot be treated as idle");
            Check(HostDispatchPolicy.Evaluate(policy, full with { IdleMinutes = 3 }).Reason == "waiting-idle-condition",
                "idle duration is enforced");
            Check(HostDispatchPolicy.Evaluate(policy, full with { Fullscreen = true }).Reason == "fullscreen",
                "fullscreen activity prevents idle-only dispatch");
            Check(HostDispatchPolicy.Evaluate(new() { Priority = "urgent" }, full).Reason == "invalid-priority",
                "invalid priority fails closed");
            Check(HostDispatchPolicy.Priority(policy) == 0 && HostDispatchPolicy.Priority(null) == 1,
                "priority defaults and low mapping");
            Check(HostDispatchPolicy.Evaluate(null, new(null, null, null, null)).Allowed,
                "legacy miniapps remain unrestricted");

            var priorityGate = new HostPriorityGate(concurrency: 1);
            var blocker = await priorityGate.AcquireAsync(1);
            var low = priorityGate.AcquireAsync(0);
            var high = priorityGate.AcquireAsync(2);
            var normal = priorityGate.AcquireAsync(1);
            Check(priorityGate.Snapshot().Waiting == 3, "priority queue holds three tasks");
            blocker!.Dispose();
            using (var highLease = await high.WaitAsync(TimeSpan.FromSeconds(2)))
            {
                Check(!low.IsCompleted && !normal.IsCompleted, "high priority runs before lower priorities");
            }
            using (var normalLease = await normal.WaitAsync(TimeSpan.FromSeconds(2)))
            {
                Check(!low.IsCompleted, "normal priority runs before low");
            }
            using (var lowLease = await low.WaitAsync(TimeSpan.FromSeconds(2))) { }
            Check(priorityGate.Snapshot().Running == 0, "all priority leases returned");

            var fakeNow = DateTimeOffset.UtcNow;
            var aging = new HostPriorityGate(concurrency: 1, clock: () => fakeNow);
            var agingBlocker = await aging.AcquireAsync(1);
            var oldLow = aging.AcquireAsync(0);
            fakeNow = fakeNow.AddSeconds(120);
            var newHigh = aging.AcquireAsync(2);
            agingBlocker!.Dispose();
            using (var agedLease = await oldLow.WaitAsync(TimeSpan.FromSeconds(2)))
                Check(!newHigh.IsCompleted, "waiting low task ages above new high task");
            using (var newerLease = await newHigh.WaitAsync(TimeSpan.FromSeconds(2))) { }

            var cancelGate = new HostPriorityGate(concurrency: 1, maxQueue: 1);
            using (var reserved = await cancelGate.AcquireAsync(1))
            {
                using var cancel = new CancellationTokenSource();
                var queued = cancelGate.AcquireAsync(1, cancel.Token);
                Check(await cancelGate.AcquireAsync(1) == null, "bounded queue rejects overflow");
                cancel.Cancel();
                try { await queued; throw new Exception("cancel was not propagated"); }
                catch (OperationCanceledException) { }
                Check(cancelGate.Snapshot().Waiting == 0, "cancelled waiter removed without leaking slots");
            }
            Check(cancelGate.Snapshot().Running == 0, "cancelled queue slot is released");

            // No production miniapp is ever started by this isolated test.
            Console.WriteLine("HOST_DISPATCH_VERIFICATION_PASS");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
