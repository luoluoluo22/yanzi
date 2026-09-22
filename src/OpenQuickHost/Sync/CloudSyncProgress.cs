namespace OpenQuickHost.Sync;

internal static class CloudSyncProgress
{
    // CurrentRevision may include writes made after the server read this page.
    // A write acknowledgement also says nothing about other objects we have not downloaded.
    internal static long DownloadCursor(long previous, CloudSyncObjectListResponse page) =>
        page.Objects.Count == 0 ? previous : Math.Max(previous, page.Objects.Max(item => item.Revision));
}
