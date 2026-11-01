using OpenQuickHost;
using System.Collections.Concurrent;

static void Require(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

var gate = new ExtensionWatcherBatchGate();
var scheduled = 0;
Parallel.For(0, 10_000, i =>
{
    var ext = "extension-" + (i % 20);
    if (gate.Record(ext, i % 101 == 0))
        Interlocked.Increment(ref scheduled);
});
var first = gate.Drain();
Require(scheduled == 1, "10,000 concurrent watcher notifications schedule one dispatcher batch");
Require(first.EventCount == 10_000, "no notifications lost");
Require(first.ExtensionCount == 20, "coalesce by extension ID");
Require(first.CatalogChanged, "manifest/source refresh flag retained");
Require(gate.Drain().EventCount == 0, "drained state is empty");
Require(gate.Record("extension-1", false), "next burst schedules again");
Require(!gate.Record("EXTENSION-1", false), "case-insensitive duplicate coalesced");
var second = gate.Drain();
Require(second.EventCount == 2 && second.ExtensionCount == 1 && !second.CatalogChanged,
    "subsequent non-catalog burst preserves counts and flags");

// Real Windows FileSystemWatcher burst: no production extension directory is touched.
var scratch = Path.Combine(Path.GetTempPath(), "yanzi-watcher-phase1-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var osGate = new ExtensionWatcherBatchGate();
    var drained = new ConcurrentQueue<ExtensionWatcherBatch>();
    var notifications = 0;
    var flushes = 0;
    void Changed(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref notifications);
        if (!osGate.Record("qa-extension", true))
            return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            drained.Enqueue(osGate.Drain());
            Interlocked.Increment(ref flushes);
        });
    }

    using (var watcher = new FileSystemWatcher(scratch)
    {
        Filter = "manifest.json",
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        EnableRaisingEvents = true
    })
    {
        watcher.Created += Changed;
        watcher.Changed += Changed;
        var file = Path.Combine(scratch, "manifest.json");
        for (var i = 0; i < 200; i++)
            File.WriteAllText(file, "{\"version\":" + i + "}");
        await Task.Delay(1600);
    }

    var totalDrained = drained.Sum(batch => batch.EventCount);
    Require(notifications > 5, "real Windows filesystem generated multiple notifications");
    Require(flushes >= 1 && flushes <= 3, "real watcher burst collapsed into bounded flushes");
    Require(totalDrained == notifications, "real watcher notifications fully accounted for");
    Require(drained.All(batch => batch.CatalogChanged && batch.ExtensionCount == 1),
        "real watcher batch retains catalog invalidation");
    Console.WriteLine($"REAL_WATCHER_EVENTS={notifications}; FLUSHES={flushes}");
}
finally
{
    Directory.Delete(scratch, recursive: true);
}
Console.WriteLine("EXTENSION_WATCHER_BATCH_VERIFICATION_PASSED");
