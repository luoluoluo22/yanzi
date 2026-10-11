using OpenQuickHost;

internal static class VipVersionPolicyVerification
{
    public static void Run()
    {
        var published = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        static void Must(bool condition, string scenario)
        {
            if (!condition) throw new InvalidOperationException("VIP update entitlement regression: " + scenario);
        }

        Must(AppVersionInfo.IsUpdateEntitled("0.3.19", null, null, null), "legacy 0.x stays free");
        Must(AppVersionInfo.IsUpdateEntitled("1.0.0", null, null, null), "free 1.0.0 stays usable");
        Must(!AppVersionInfo.IsUpdateEntitled("1.0.1", null, null, published), "non-VIP cannot update paid version");
        Must(!AppVersionInfo.IsUpdateEntitled("1.0.1", "year", "2026-10-11T00:00:00Z", null), "unknown release date must fail closed");
        Must(AppVersionInfo.IsUpdateEntitled("1.0.1", "month", "2026-10-11T00:00:00Z", published), "paid period covers release");
        Must(!AppVersionInfo.IsUpdateEntitled("1.0.1", "month", "2026-10-09T23:59:59Z", published), "later releases blocked after expiry");
        Must(AppVersionInfo.IsUpdateEntitled("1.0.1", "lifetime", null, null), "lifetime covers future releases");
        Must(!AppVersionInfo.IsUpdateEntitled("unknown", null, null, null), "unparseable paid release blocked");
        var method = typeof(OpenQuickHost.Sync.CloudSyncClient).GetMethod("IsPaidCloudRequestPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing cloud entitlement path classifier");
        bool RequiresVip(string path) => (bool)(method.Invoke(null, [path]) ?? false);
        Must(RequiresVip("/v1/sync/changes?since=42"), "sync API needs VIP");
        Must(RequiresVip("/v1/me/devices"), "device cloud API needs VIP");
        Must(!RequiresVip("/v1/user/cloud-export?cursor=100"), "legacy export works after membership denied");
        Must(!RequiresVip("/v1/user/vip-status"), "membership renewal lookup exempt");
        Must(!RequiresVip("/v1/licenses/redeem"), "licence redemption exempt");
        Console.WriteLine("VIP_UPDATE_POLICY_PASSED");
    }
}
