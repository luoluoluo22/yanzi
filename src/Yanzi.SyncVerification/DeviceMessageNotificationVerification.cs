using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class DeviceMessageNotificationVerification
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "yanzi-device-notification-" + Guid.NewGuid().ToString("N"));
        using var scope = HostAssets.UseIsolatedDataRootForVerification(root);
        var now = new DateTimeOffset(2026, 10, 9, 21, 30, 0, TimeSpan.Zero);
        var account = "notifier-test-1";
        var device = "desktop-test";

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Notification regression: " + label);
        }

        var firstSeen = DeviceMessageNotificationPolicy.ReadOrInitialize(account, device, false, now);
        Check(firstSeen == now, "new device must snapshot first login, not cloud history origin");
        var restored = DeviceMessageNotificationPolicy.ReadOrInitialize(account, device, false, now.AddHours(1));
        Check(restored == firstSeen, "first login baseline survives restart");
        var historic = new DeviceMessageRecord { CreatedAt = now.AddMinutes(-1).ToString("O") };
        var fresh = new DeviceMessageRecord { CreatedAt = now.AddSeconds(2).ToString("O") };
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(historic, firstSeen, now.AddSeconds(3)), "pre-login history must not notify");
        Check(DeviceMessageNotificationPolicy.ShouldPopup(fresh, firstSeen, now.AddSeconds(3)), "post-login message must notify");
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(fresh, firstSeen, now.AddHours(1)), "old offline message must not notify");
        var expired = new DeviceMessageRecord { CreatedAt = now.AddSeconds(2).ToString("O"), ExpiresAt = now.AddSeconds(1).ToString("O") };
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(expired, firstSeen, now.AddSeconds(4)), "expired message must not notify");
        var future = new DeviceMessageRecord { CreatedAt = now.AddMinutes(5).ToString("O") };
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(future, firstSeen, now), "implausible clock skew must not notify");
        var legacy = new DeviceMessageRecord { CreatedAt = "" };
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(legacy, firstSeen, now), "unknown-age first login message must not notify");

        DeviceMessageCursorStore.Write("notifier-test-2", device, 12);
        Check(DeviceMessageCursorStore.Exists("notifier-test-2", device), "existing cursor must be detected");
        var existing = DeviceMessageNotificationPolicy.ReadOrInitialize("notifier-test-2", device, true, now);
        Check(existing == DateTimeOffset.MinValue, "existing devices must retain normal recent message delivery");
        Check(DeviceMessageNotificationPolicy.ShouldPopup(historic, existing, now), "returning device fresh unseen message must notify");
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(historic, existing, now.AddHours(2)), "returning device old backlog must stay quiet");
        Check(DeviceMessageNotificationPolicy.ShouldPopup(legacy, existing, now), "legacy realtime without timestamp stays compatible");
        Check(!DeviceMessageNotificationPolicy.ShouldPopup(new DeviceMessageRecord {
            ExpiresAt = now.AddMinutes(-2).ToString("O")
        }, existing, now), "expired legacy message must stay quiet");

        var unix = new DeviceMessageRecord { CreatedAt = now.ToUnixTimeMilliseconds().ToString() };
        Check(DeviceMessageNotificationPolicy.ShouldPopup(unix, existing, now), "Unix millisecond message dates remain compatible");
        var otherAccount = DeviceMessageNotificationPolicy.ReadOrInitialize("notifier-test-3", device, false, now.AddMinutes(4));
        Check(otherAccount == now.AddMinutes(4), "first login must be scoped to each account");

        Console.WriteLine("Device notification policy PASS: first login, historical backfill, restart, offline aging, expiry, account isolation, timestamps.");
    }
}
