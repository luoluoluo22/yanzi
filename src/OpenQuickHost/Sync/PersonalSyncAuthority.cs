namespace OpenQuickHost.Sync;

internal static class PersonalSyncAuthority
{
    // Token expiration is a transport/authentication condition, not a change of data ownership.
    internal static PersonalConfigSyncMode SelectMode(SyncSession? session, bool hasSavedCredential) =>
        !string.IsNullOrWhiteSpace(session?.UserId) || hasSavedCredential
            ? PersonalConfigSyncMode.UploadOnlyBackup
            : PersonalConfigSyncMode.Bidirectional;

    internal static PersonalConfigSyncMode SelectMode(SyncSession? session, bool hasSavedCredential, string? provider)
    {
        // P2P 设备直连模式下，两端均为对等设备，执行双向同步
        if (string.Equals(PersonalSyncProviders.Normalize(provider), PersonalSyncProviders.P2p, System.StringComparison.OrdinalIgnoreCase))
        {
            return PersonalConfigSyncMode.Bidirectional;
        }

        return SelectMode(session, hasSavedCredential);
    }
}
