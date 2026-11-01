using System.Collections.Concurrent;

namespace OpenQuickHost.Sync;

/// <summary>
/// A short-lived *decision* memo for account extensions whose manifest versions
/// are equal. Same-version packages are never installed over local source, so
/// a stale entry can delay only conflict reporting, never overwrite user files.
/// FileSystemWatcher invalidates entries immediately; a bounded recheck covers
/// missed filesystem events. New cloud revisions/hashes always force recheck.
/// </summary>
internal sealed class AccountExtensionConflictMemo
{
    private sealed record DecisionEntry(
        string LocalVersion, string RemoteVersion, string RemoteHash,
        int RemoteRevision, string LocalDirectory, long Generation,
        AccountExtensionPullDecision Decision, DateTime CheckedAtUtc);

    private sealed record LogEntry(string Fingerprint, DateTime LoggedAtUtc);

    private readonly ConcurrentDictionary<string, DecisionEntry> _decisions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LogEntry> _logged =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _generations =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeSpan _recheckAfter;
    private readonly TimeSpan _remindAfter;

    public AccountExtensionConflictMemo(TimeSpan? recheckAfter = null, TimeSpan? remindAfter = null)
    {
        _recheckAfter = recheckAfter ?? TimeSpan.FromMinutes(5);
        _remindAfter = remindAfter ?? TimeSpan.FromMinutes(15);
        if (_recheckAfter <= TimeSpan.Zero || _remindAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recheckAfter), "Intervals must be positive.");
    }

    public long CaptureGeneration(string extensionId) =>
        _generations.GetOrAdd(extensionId, 0);

    public void Invalidate(string extensionId)
    {
        _generations.AddOrUpdate(extensionId, 1, static (_, old) => old + 1);
        _decisions.TryRemove(extensionId, out _);
        _logged.TryRemove(extensionId, out _);
    }

    public bool TryGetSameVersionDecision(
        string extensionId, string? localVersion, string? remoteVersion,
        string? remoteHash, int remoteRevision, string? localDirectory,
        DateTime nowUtc, out AccountExtensionPullDecision decision)
    {
        decision = default;
        if (string.IsNullOrWhiteSpace(remoteHash) ||
            !_decisions.TryGetValue(extensionId, out var entry) ||
            entry.Generation != CaptureGeneration(extensionId) ||
            entry.LocalVersion != (localVersion ?? string.Empty) ||
            entry.RemoteVersion != (remoteVersion ?? string.Empty) ||
            !entry.RemoteHash.Equals(remoteHash, StringComparison.OrdinalIgnoreCase) ||
            entry.RemoteRevision != remoteRevision ||
            !string.Equals(entry.LocalDirectory, localDirectory ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            nowUtc < entry.CheckedAtUtc ||
            nowUtc - entry.CheckedAtUtc >= _recheckAfter)
            return false;

        decision = entry.Decision;
        return true;
    }

    public void RememberSameVersionDecision(
        string extensionId, string? localVersion, string? remoteVersion,
        string? remoteHash, int remoteRevision, string? localDirectory,
        long generationBeforeHash, AccountExtensionPullDecision decision, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(remoteHash) ||
            decision is not (AccountExtensionPullDecision.KeepLocal or AccountExtensionPullDecision.SameVersionConflict) ||
            generationBeforeHash != CaptureGeneration(extensionId))
            return;

        _decisions[extensionId] = new DecisionEntry(
            localVersion ?? string.Empty, remoteVersion ?? string.Empty, remoteHash,
            remoteRevision, localDirectory ?? string.Empty, generationBeforeHash, decision, nowUtc);
    }

    public bool ShouldLogConflict(
        string extensionId, string? localVersion, string? remoteVersion,
        string? remoteHash, int remoteRevision, string? localHash, DateTime nowUtc)
    {
        var fingerprint = string.Join("|", localVersion, remoteVersion, remoteHash,
            remoteRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), localHash);
        if (_logged.TryGetValue(extensionId, out var last) &&
            last.Fingerprint == fingerprint &&
            nowUtc >= last.LoggedAtUtc &&
            nowUtc - last.LoggedAtUtc < _remindAfter)
            return false;

        _logged[extensionId] = new LogEntry(fingerprint, nowUtc);
        return true;
    }
}
