namespace OpenQuickHost.Sync;

// A package checksum measures content identity, never release ordering. In particular,
// a same-version checksum mismatch is a conflict, NOT permission to overwrite a PC.
internal enum AccountExtensionPullDecision
{
    InstallMissing,
    InstallNewerVersion,
    KeepLocal,
    SameVersionConflict,
    UnorderedVersionConflict
}

internal static class AccountExtensionPullPolicy
{
    internal static bool IsRemoteVersionNewer(string? localVersion, string? remoteVersion)
    {
        return TryNormalizeVersion(localVersion, out var local) &&
               TryNormalizeVersion(remoteVersion, out var remote) &&
               remote > local;
    }

    internal static AccountExtensionPullDecision Decide(
        bool hasLocal,
        string? localVersion,
        string? remoteVersion,
        string? localArchiveHash = null,
        string? remoteArchiveHash = null)
    {
        if (!hasLocal)
            return AccountExtensionPullDecision.InstallMissing;

        if (!TryNormalizeVersion(localVersion, out var local) ||
            !TryNormalizeVersion(remoteVersion, out var remote))
            return AccountExtensionPullDecision.UnorderedVersionConflict;

        if (remote > local)
            return AccountExtensionPullDecision.InstallNewerVersion;
        if (remote < local)
            return AccountExtensionPullDecision.KeepLocal;

        // A same-version content mismatch may be an unsubmitted developer edit.
        // A checksum change must never be treated as a newer release.
        return !string.IsNullOrWhiteSpace(localArchiveHash) &&
               !string.IsNullOrWhiteSpace(remoteArchiveHash) &&
               !string.Equals(localArchiveHash, remoteArchiveHash,
                   StringComparison.OrdinalIgnoreCase)
            ? AccountExtensionPullDecision.SameVersionConflict
            : AccountExtensionPullDecision.KeepLocal;
    }

    private static bool TryNormalizeVersion(string? text, out Version version)
    {
        var normalized = (text ?? string.Empty).Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }
}
