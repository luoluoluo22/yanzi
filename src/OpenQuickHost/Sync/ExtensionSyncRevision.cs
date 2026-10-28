namespace OpenQuickHost.Sync;

internal static class ExtensionSyncRevision
{
    public static long Next(long observedRevision = 0)
    {
        var wallClockFloor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
        return Math.Max(wallClockFloor, observedRevision + 1);
    }

    public static int Compare(WebDavSyncEntry left, WebDavSyncEntry right)
    {
        var revisionCompare = left.Revision.CompareTo(right.Revision);
        if (revisionCompare != 0) return revisionCompare;

        var leftUpdated = ParseTimestamp(left.UpdatedAtUtc);
        var rightUpdated = ParseTimestamp(right.UpdatedAtUtc);
        var timestampCompare = leftUpdated.CompareTo(rightUpdated);
        if (timestampCompare != 0) return timestampCompare;

        if (left.Deleted != right.Deleted) return left.Deleted ? 1 : -1;
        if (left.Purged != right.Purged) return left.Purged ? 1 : -1;

        var deviceCompare = string.Compare(
            left.UpdatedByDeviceId,
            right.UpdatedByDeviceId,
            StringComparison.OrdinalIgnoreCase);
        if (deviceCompare != 0) return deviceCompare;
        return string.Compare(left.PackageHash, right.PackageHash, StringComparison.OrdinalIgnoreCase);
    }

    public static void Stamp(WebDavSyncEntry entry, long observedRevision = 0)
    {
        entry.Revision = Next(Math.Max(entry.Revision, observedRevision));
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        entry.UpdatedByDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        entry.UpdatedByDeviceName = DeviceIdentityStore.GetDesktopDisplayName();
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
}
