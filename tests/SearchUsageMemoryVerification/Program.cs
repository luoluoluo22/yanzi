using System.Text.Json;
using OpenQuickHost;

// Standalone regression links production persistence code with a temporary data root;
// never reads or writes the user's actual launcher profile.
var root = Path.Combine(Path.GetTempPath(), "yanzi-search-usage-verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    HostAssets.DataRootPath = root;
    var memory = new SearchUsageMemory();
    memory.Record("alpha");
    SearchUsageMemory.Save(memory);
    Check(SearchUsageMemory.GetCachedUsageCount("alpha") == 1, "pending update immediately reflected");
    memory.Record("alpha");
    Check(SearchUsageMemory.GetCachedUsageCount("alpha") == 1, "saved snapshot isolated from mutable caller");
    SearchUsageMemory.Save(memory);
    Check(SearchUsageMemory.GetCachedUsageCount("alpha") == 2, "latest pending update replaces older snapshot");
    SearchUsageMemory.FlushPendingSaves();
    Check(SearchUsageMemory.Load().GetUsageCount("alpha") == 2, "shutdown flush persisted latest value");
    Check(!File.Exists(HostAssets.SearchMemoryPath + ".tmp"), "atomic write has no abandoned temp file");

    var offThread = new SearchUsageMemory();
    offThread.Record("worker");
    await Task.Run(() => SearchUsageMemory.Save(offThread));
    var persistedWithoutDispatcher = false;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        if (SearchUsageMemory.Load().GetUsageCount("worker") == 1)
        {
            persistedWithoutDispatcher = true;
            break;
        }
        await Task.Delay(100);
    }
    Check(persistedWithoutDispatcher, "thread-pool debounce saves without dispatcher pump");
    SearchUsageMemory.FlushPendingSaves();

    // External sync changes are not permanently hidden by the UI cache.
    var external = new SearchUsageMemory();
    for (var i = 0; i < 2500; i++) external.Items["item-" + i] = new SearchUsageEntry { Count = i + 1 };
    SafeFile.AtomicWriteText(HostAssets.SearchMemoryPath, JsonSerializer.Serialize(external,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    Thread.Sleep(1150);
    Check(SearchUsageMemory.GetCachedUsageCount("item-2499") == 2500, "cached lookup observes external file replacement");
    for (var i = 0; i < 2500; i++)
        CheckQuiet(SearchUsageMemory.GetCachedUsageCount("item-" + i) == i + 1);
    Console.WriteLine("PASS repeated UI lookups return correct counts without repeated JSON parse");

    // Switching to an isolated profile must invalidate the cache even if its timestamp matches.
    var nextRoot = Path.Combine(root, "new-profile");
    Directory.CreateDirectory(nextRoot);
    HostAssets.DataRootPath = nextRoot;
    Check(SearchUsageMemory.GetCachedUsageCount("item-2499") == 0, "data-root change invalidates cache");

    var many = new SearchUsageMemory();
    for (var i = 0; i < 1000; i++) many.Record("key-" + i);
    for (var i = 0; i < 10; i++)
    {
        many.Record("last");
        SearchUsageMemory.Save(many);
    }
    SearchUsageMemory.FlushPendingSaves();
    Check(SearchUsageMemory.Load().GetUsageCount("last") == 10, "rapid updates preserve last snapshot");
    Check(!File.Exists(HostAssets.SearchMemoryPath + ".tmp"), "rapid writes leave no incomplete temp file");
    Check(HostAssets.Logs.Count == 0, "no persistence failures");
    Console.WriteLine("SEARCH_USAGE_MEMORY_VERIFIED");
}
finally
{
    SearchUsageMemory.FlushPendingSaves();
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}
static void CheckQuiet(bool ok)
{
    if (!ok) throw new InvalidOperationException("cached lookup returned wrong count");
}

namespace OpenQuickHost
{
    public static class HostAssets
    {
        public static string DataRootPath { get; set; } = "";
        public static string SearchMemoryPath => Path.Combine(DataRootPath, "search-memory.json");
        public static List<string> Logs { get; } = [];
        public static void AppendLog(string line) { lock (Logs) Logs.Add(line); }
    }
}
