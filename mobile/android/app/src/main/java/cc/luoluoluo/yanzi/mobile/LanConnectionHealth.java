package cc.luoluoluo.yanzi.mobile;

/** Monotonic, bounded recovery. A remembered address is not proof of connectivity. */
final class LanConnectionHealth {
    enum Status { AVAILABLE, RECONNECTING, UNAVAILABLE }
    private long lastSuccess = -1, nextProbe;
    private int failures;
    private boolean needsProbe = true;

    static boolean isDesktopPeer(String mode, String platform) {
        return "desktop".equals(platform) || !"account".equals(mode) && platform.isEmpty();
    }

    boolean beginProbe(long now) {
        if (now < nextProbe) return false;
        nextProbe = now + 8000;
        return true;
    }
    void success(long now) {
        lastSuccess = now; failures = 0; needsProbe = false; nextProbe = now + 8000;
    }
    void transportFailure(long now) {
        if (!needsProbe) nextProbe = now;
        needsProbe = true;
    }
    void probeFailure(long now) {
        needsProbe = true; failures++;
        nextProbe = now + (failures <= 2 ? 5000 : failures <= 4 ? 15000 : 30000);
    }
    Status status(long now) {
        if (lastSuccess < 0 || now - lastSuccess >= 30000 || failures >= 2) return Status.UNAVAILABLE;
        return needsProbe ? Status.RECONNECTING : Status.AVAILABLE;
    }
    boolean shouldRediscover() { return lastSuccess < 0 || failures >= 2; }
    void reset() { lastSuccess = -1; failures = 0; nextProbe = 0; needsProbe = true; }
}
