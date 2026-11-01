using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost.Sync;

/// <summary>
/// Merge only simultaneously pending Gitee reads for the same authenticated
/// repository and path. Never cache a completed result: mutable sync objects,
/// tombstones and cross-device changes must remain observable on the next read.
/// </summary>
internal static class GiteeReadFlightCoordinator
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Active = new(StringComparer.Ordinal);

    internal static long NetworkFlights => Interlocked.Read(ref _networkFlights);
    internal static long SharedWaiters => Interlocked.Read(ref _sharedWaiters);
    internal static int ActiveCount => Active.Count;
    private static long _networkFlights;
    private static long _sharedWaiters;

    // The key contains only a SHA-256 digest. Never retain or log a token.
    internal static string CreateKey(string owner, string repo, string branch, string path, string token)
    {
        var material = string.Join("\u001f", owner, repo, branch, path, token.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    internal static async Task<byte[]?> ReadAsync(
        string key, Func<Task<byte[]?>> fetch, CancellationToken callerCancellation)
    {
        callerCancellation.ThrowIfCancellationRequested();
        var candidate = new Lazy<Task<byte[]?>>(fetch, LazyThreadSafetyMode.ExecutionAndPublication);
        var entry = Active.GetOrAdd(key, candidate);
        var leader = ReferenceEquals(entry, candidate);

        Task<byte[]?> shared;
        try
        {
            shared = entry.Value;
        }
        catch
        {
            RemoveExact(key, entry);
            throw;
        }

        if (leader)
        {
            Interlocked.Increment(ref _networkFlights);
            // Executed exactly once by the leader, even if its own waiter
            // cancels. Completed/faulted results never outlive their flight.
            _ = shared.ContinueWith(
                _ => RemoveExact(key, entry),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        else
        {
            Interlocked.Increment(ref _sharedWaiters);
        }

        // Cancellation belongs to each waiter. One caller must not cancel the
        // shared transport while other devices/components still await it.
        var result = await shared.WaitAsync(callerCancellation).ConfigureAwait(false);
        // Preserve the prior contract: callers own their returned byte arrays.
        return result?.ToArray();
    }

    internal static void Invalidate(string key) => Active.TryRemove(key, out _);

    private static void RemoveExact(string key, Lazy<Task<byte[]?>> entry) =>
        ((ICollection<KeyValuePair<string, Lazy<Task<byte[]?>>>>)Active)
            .Remove(new KeyValuePair<string, Lazy<Task<byte[]?>>>(key, entry));
}
