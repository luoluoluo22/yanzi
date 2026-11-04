using System.Text;
using OpenQuickHost;

var root = Path.Combine(Path.GetTempPath(), "yanzi-ai-log-perf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var path = Path.Combine(root, "ai-chat.log");
try
{
    var cache = new AiChatLogSnapshotCache();
    var reads = 0;
    long revision = 0;

    string[] ReadFile(string p) { reads++; return File.ReadAllLines(p, Encoding.UTF8); }
    string Read(string? topic = null, bool allTopics = false) =>
        cache.ReadRecent(path, topic, allTopics, () => Interlocked.Read(ref revision), ReadFile);
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
    }

    Check(Read().Contains("尚无 AI 对话日志"), "missing log has informative empty state");
    File.WriteAllText(path, "time |topic=A| first\ntime |topic=B| second\n", Encoding.UTF8);
    Check(Read("A").Contains("first"), "topic A reads matching lines");
    for (var i = 0; i < 2000; i++)
        if (!Read("A").Contains("first")) throw new InvalidOperationException("repeat read mismatch");
    Check(reads == 1, "2000 UI refreshes avoid repeated full file reads");
    Check(Read("B").Contains("second") && !Read("B").Contains("first"), "topic change re-filters cached lines");
    Check(reads == 1, "topic switching does not re-read unchanged file");
    Check(Read("A", true).Contains("first") && Read("A", true).Contains("second"),
        "all conversations filter remains correct");
    Check(reads == 1, "filter toggle still uses cached snapshot");

    // In-process revision must detect a rewrite even if external metadata is identical.
    var originalWrite = File.GetLastWriteTimeUtc(path);
    var originalLength = new FileInfo(path).Length;
    File.WriteAllText(path, "time |topic=A| third\ntime |topic=B| fourth\n", Encoding.UTF8);
    File.SetLastWriteTimeUtc(path, originalWrite);
    Check(new FileInfo(path).Length == originalLength, "test rewrite preserves length");
    Interlocked.Increment(ref revision);
    Check(Read("A").Contains("third"), "local revision invalidates same-size same-timestamp cache");
    Check(reads == 2, "invalidation causes exactly one additional read");

    File.Move(path, path + ".old");
    File.WriteAllText(path, "time |topic=B| rotated\n", Encoding.UTF8);
    Check(Read("B").Contains("rotated"), "rotated log is detected by file metadata");
    Check(reads == 3, "rotation triggers one snapshot load");
    File.Delete(path);
    Check(Read().Contains("尚无 AI 对话日志"), "file deletion evicts previous cached snapshot");

    // Last 250 behavior should match the original implementation.
    File.WriteAllLines(path, Enumerable.Range(0, 320).Select(i => "time |topic=A| line-" + i), Encoding.UTF8);
    Interlocked.Increment(ref revision);
    var selected = Read("A").Split(Environment.NewLine);
    Check(selected.Length == 250, "recent log still limits output to last 250 lines");
    Check(selected[0].Contains("line-70") && selected[^1].Contains("line-319"),
        "last 250 order preserved");

    // If a write overlaps read, do not cache a snapshot indexed under an incorrect stamp.
    var before = reads;
    Interlocked.Increment(ref revision); // Ensure the overlapping loader is executed.
    var overlapping = cache.ReadRecent(path, "A", false, () => revision,
        p => { reads++; File.AppendAllText(p, "time |topic=A| overlap\n"); revision++; return File.ReadAllLines(p); });
    Check(overlapping.Contains("overlap"), "read sees concurrent append");
    var after = Read("A");
    Check(after.Contains("overlap") && reads >= before + 2,
        "overlapping write invalidates next snapshot");
    Console.WriteLine("AI_CHAT_LOG_PERFORMANCE_VERIFIED");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
