namespace OpenQuickHost.Sync;

internal static class PersonalSyncAuthority
{
    // Token expiration is a transport/authentication condition, not a change of data ownership.
    internal static PersonalConfigSyncMode SelectMode(SyncSession? session, bool hasSavedCredential) =>
        !string.IsNullOrWhiteSpace(session?.UserId) || hasSavedCredential
            ? PersonalConfigSyncMode.UploadOnlyBackup
            : PersonalConfigSyncMode.Bidirectional;
}
