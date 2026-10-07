package cc.luoluoluo.yanzi.mobile;
/** Conservative presence budget; realtime events are independent of periodic compensation. */
final class BackgroundCadence {
    static final long HEARTBEAT_MS=60000;
    static final long MAINTENANCE_MS=300000;
    static long syncInterval(boolean realtime) { return realtime?900000:60000; }
    static long pollInterval(boolean realtime) { return realtime?1800000:60000; }
    static long deliveryRetryInterval() { return 60000; }
    static long reconnectDelay(int failures) { return Math.min(60000,1000L*(1L<<Math.min(6,Math.max(0,failures)))); }
}
