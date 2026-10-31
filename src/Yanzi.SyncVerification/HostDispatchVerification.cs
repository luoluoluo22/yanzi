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

            // No production miniapp is ever started by this isolated test.
            Console.WriteLine("HOST_DISPATCH_VERIFICATION_PASS");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
