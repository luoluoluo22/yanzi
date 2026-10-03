package cc.luoluoluo.yanzi.mobile;

public final class LanConnectionHealthVerification {
    private static int checks;
    private static void check(boolean value, String label) {
        if (!value) throw new AssertionError(label);
        checks++; System.out.println("PASS " + label);
    }
    public static void main(String[] args) {
        LanConnectionHealth h = new LanConnectionHealth();
        check(h.status(0) == LanConnectionHealth.Status.UNAVAILABLE, "Remembered address alone never means connected");
        check(h.beginProbe(0) && !h.beginProbe(1), "Probe requests coalesce within the interval");
        h.success(100);
        check(h.status(100) == LanConnectionHealth.Status.AVAILABLE, "Verified connection available");
        h.transportFailure(200);
        check(h.status(200) == LanConnectionHealth.Status.RECONNECTING, "Single request failure shows honest reconnecting state");
        check(h.beginProbe(200), "Transport failure allows an immediate verification");
        h.probeFailure(200);
        check(h.status(200) == LanConnectionHealth.Status.RECONNECTING, "One failed probe does not flip to cloud");
        check(!h.beginProbe(5199) && h.beginProbe(5200), "Recovery respects five second pacing");
        h.success(5300);
        check(h.status(5300) == LanConnectionHealth.Status.AVAILABLE, "Short outage recovers directly");
        h.probeFailure(6000); h.probeFailure(11000);
        check(h.status(11000) == LanConnectionHealth.Status.UNAVAILABLE && h.shouldRediscover(), "Repeated failure switches to cloud and rediscovery");
        h.probeFailure(16000);
        check(!h.beginProbe(30999) && h.beginProbe(31000), "Persistent outage backs off to fifteen seconds");
        h.probeFailure(32000); h.probeFailure(47000);
        check(!h.beginProbe(76999) && h.beginProbe(77000), "Long outage caps polling at thirty seconds");
        h.success(78000);
        check(!h.shouldRediscover() && h.status(78000) == LanConnectionHealth.Status.AVAILABLE, "Recovery clears failure backoff");
        check(h.status(108000) == LanConnectionHealth.Status.UNAVAILABLE, "Stale success expires after thirty seconds");
        h.reset();
        check(h.status(78001) == LanConnectionHealth.Status.UNAVAILABLE && h.beginProbe(78001), "Wi-Fi or account change discards health immediately");
        check(!LanConnectionHealth.isDesktopPeer("account", "android"), "Other account phones cannot replace the desktop");
        check(LanConnectionHealth.isDesktopPeer("account", "desktop") && LanConnectionHealth.isDesktopPeer("local", ""), "Desktop and legacy manual desktop pairs accepted");
        check(!LanConnectionHealth.isDesktopPeer("account", ""), "Unknown account peer is not treated as a desktop");
        System.out.println("LAN_HEALTH_CHECKS=" + checks);
    }
}
