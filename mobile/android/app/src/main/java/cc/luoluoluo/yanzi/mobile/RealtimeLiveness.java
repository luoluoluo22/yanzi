package cc.luoluoluo.yanzi.mobile;

/** Uses elapsed realtime so changes to wall-clock time cannot keep a dead socket alive. */
final class RealtimeLiveness {
    static final long STALE_MS = 120000;
    static final long PING_MS = 30000;
    volatile long openedAt = -1, lastPongAt = -1, lastEventAt = -1;
    private long pingAt;
    void opened(long now) { openedAt = now; lastPongAt = lastEventAt = -1; pingAt = now; }
    void pong(long now) { lastPongAt = now; }
    void event(long now) { lastEventAt = now; }
    boolean stale(long now) { return openedAt >= 0 && now - Math.max(openedAt, Math.max(lastPongAt, lastEventAt)) >= STALE_MS; }
    boolean pingDue(long now) {
        if (openedAt < 0 || now < pingAt) return false;
        pingAt = now + PING_MS;
        return true;
    }
}
