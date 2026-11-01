using System.Collections.Concurrent;

namespace OpenQuickHost.Sync;

internal static class ExtensionDevicePolicy
{
    public static bool ShouldSyncData(string extensionId) => !string.IsNullOrWhiteSpace(extensionId);
}
internal static class ExtensionDataObjectStore
{
    public static string NormalizeExtensionId(string id) => id.Trim();
    public static string NormalizeKey(string key) => key.Replace('\\', '/').Trim();
}
public sealed partial class PersonalSyncService
{
    private readonly FakeBackend _backend;
    public PersonalSyncService(FakeBackend backend) => _backend = backend;
    private static string BuildExtensionDataPath(string extensionId, string key) =>
        $"appdata/{extensionId}/{key}";
}
public sealed class FakeBackend
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();
    private readonly ConcurrentDictionary<string, int> _reads = new();
    private readonly ConcurrentDictionary<string, int> _writes = new();
    public bool FailIndexWrites { get; init; }
    public int TotalWrites => _writes.Values.Sum();
    public int Reads(string path) => _reads.GetValueOrDefault(path);
    public int Writes(string path) => _writes.GetValueOrDefault(path);

    public Task<byte[]?> TryReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _reads.AddOrUpdate(path, 1, (_, old) => old + 1);
        return Task.FromResult(_files.TryGetValue(path, out var value) ? value.ToArray() : null);
    }

    public Task WriteBytesAsync(string path, byte[] value, string contentType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailIndexWrites && path.Contains("/key-index/", StringComparison.Ordinal))
            throw new IOException("Simulated index write failure");
        _files[path] = value.ToArray();
        _writes.AddOrUpdate(path, 1, (_, old) => old + 1);
        return Task.CompletedTask;
    }

    public void ResetCounters()
    {
        _reads.Clear();
        _writes.Clear();
    }
}
